namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

/// <summary>
/// Entry, Descent and Landing director + HUD overlay. Activates when the active
/// vessel is descending fast into an atmospheric body, then sequences
/// ENTRY → PEAK HEATING → AERO DESCENT → RETRO BURN → FINAL → TOUCHDOWN, drawing a
/// dedicated descent HUD (radar altimeter, vertical/horizontal speed, g-force,
/// plasma vignette) and flying a retrograde suicide-burn autopilot to a soft landing.
/// </summary>
public partial class EDLController : Control
{
    public static EDLController? Instance { get; private set; }

    private enum Edl { Inactive, Entry, Peak, Aero, Retro, Final, Touchdown }
    private Edl _phase = Edl.Inactive;

    // ── Trigger thresholds ────────────────────────────────────────────────────
    private const double EntrySpeed   = 1200.0;   // m/s surface speed to arm entry

    // ── Live telemetry (refreshed each frame) ─────────────────────────────────
    private double _alt, _vUp, _horiz, _gForce, _heat;
    private string _bodyName = "";

    // ── Thermal state (the entry is now survivable-or-not, so the crew has to SEE it) ──
    private double _skinTemp;        // TPS face (K) — supposed to be white-hot
    private double _hullRatio;       // structure temperature / tolerance — this is what kills
    private double _thermalDamage;   // irreversible char, 0..1
    private double _shieldAlign;     // 0..1 — how squarely the tiles meet the flow
    private double _fluxNow;         // W/m², free-stream convective flux

    private Font _font = null!;
    private bool _legsDeployed;
    private bool _flipInProgress;
    private bool _landingCutoffCommitted;
    private double _flipElapsed;
    private double _attitudeErrorDeg;

    public override void _Ready()
    {
        Instance = this;
        // EDL is the final writer of throttle and attitude during entry.  This
        // must run after ascent guidance and HUD systems so a stale ascent
        // command cannot cancel a landing ignition in the same frame.
        ProcessPriority = 200;
        _font = ThemeDB.GetFallbackFont();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var mission = MissionManager.Instance;
        if (vessel == null || universe == null) { Visible = false; return; }

        var body = universe.GetDominantBody(vessel.Position);
        if (body.Atmosphere == null) { Deactivate(); return; }

        // ── Refresh telemetry ──────────────────────────────────────────────────
        Vector3d up      = (vessel.Position - body.Position).Normalized;
        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        _alt    = body.GetAltitude(vessel.Position);
        _vUp    = surfVel.Dot(up);                         // + up, − down
        _horiz  = (surfVel - up * _vUp).Magnitude;
        _bodyName = body.Name;

        double mass = vessel.TotalMass;
        _gForce = vessel.GetProperAcceleration(body).Magnitude / 9.80665;

        double density = body.Atmosphere.GetDensity(_alt);
        double speed   = surfVel.Magnitude;
        _heat = density * speed * speed * speed;            // ∝ convective heat flux

        RefreshThermalState(vessel, body, density, speed, surfVel);

        // Do not disarm after entry has begun.  A lifting skip can briefly climb
        // back above the nominal atmosphere and a landing burn can cross zero
        // vertical speed; both are normal trajectory segments, not EDL aborts.

        // ── Activation check ───────────────────────────────────────────────────
        if (_phase == Edl.Inactive)
        {
            bool descending = _vUp < -20.0;
            bool inAtmo     = _alt < body.Atmosphere.MaxAltitude * 1.05;
            bool hasSuperHeavy = vessel.Parts.Parts.Any(
                p => p.Definition.Id == "super_heavy_booster");
            if (descending && inAtmo && hasSuperHeavy)
            {
                // Defensive recovery for an externally-started/full-stack entry.
                // Starship cannot perform a belly-flop while still attached.
                bridge!.TriggerStaging();
                return;
            }
            if (descending && inAtmo && speed > EntrySpeed)
            {
                // Arm from ORBIT/COAST, or from a pre-entry deorbit RETRO_BURN.
                // Block only when already deep in the EDL track (ENTRY onward) so we don't
                // re-trigger from Inactive while MissionManager still shows a descent phase.
                bool blockedByMission = mission != null
                    && mission.InDescent
                    && mission.Phase is not MissionPhase.RETRO_BURN;
                if (blockedByMission)
                    return;

                _phase = Edl.Entry;
                _legsDeployed = false;
                _flipInProgress = false;
                _landingCutoffCommitted = false;
                _flipElapsed = 0.0;
                Visible = true;
                mission?.EnterPhase(MissionPhase.ENTRY);
            }
            else return;
        }

        AdvancePhase(vessel, body, mission, mass, speed, up, surfVel, delta, universe);
        QueueRedraw();   // live telemetry overlay
    }

    private void AdvancePhase(Vessel vessel, CelestialBody body, MissionManager? mission,
        double mass, double speed, Vector3d up, Vector3d surfVel, double delta, Universe universe)
    {
        double g = body.GetSurfaceGravity();

        double vDown   = System.Math.Max(0.0, -_vUp);
        double vertFrac = speed > 1e-3 ? vDown / speed : 1.0;
        Vector3d velDir = surfVel.Magnitude > 1e-3 ? surfVel.Normalized : -up;
        Quaterniond retroTarget = ShortestArc(Vector3d.Up, -velDir);
        Part? shipEngines = vessel.Parts.Parts.FirstOrDefault(
            p => p.Definition.Id == "starship_engines");
        bool aeroPhase = _phase is Edl.Entry or Edl.Peak or Edl.Aero;
        if (aeroPhase)
            shipEngines?.SelectEngineCount(System.Math.Min(3,
                System.Math.Max(1, shipEngines.Definition.EngineCount)));
        double aThrustFull = MaxLandingThrustAccel(vessel, body, shipEngines, mass);

        // Distance the FULL retrograde burn needs to null the WHOLE velocity vector (not just the
        // vertical part) — engines point retrograde, so they kill total speed. Net decel is the
        // thrust minus the along-track gravity component.
        double aBrake   = aThrustFull - g * vertFrac;
        double stopDist = aBrake > 0.5 ? speed * speed / (2.0 * aBrake) : double.MaxValue;

        double atmoTop = body.Atmosphere!.MaxAltitude;

        // Flip to the landing burn LOW, after the belly-flop has bled off velocity aerodynamically
        // (real Starship belly-flops to near terminal velocity, then flips at ~0.5-2 km). Hold the
        // broadside attitude through the whole aero descent — flipping to engines-retrograde high
        // up loses the drag, lets the vessel penetrate deep at hypersonic speed, and burns it up.
        // Gate: drop the belly-flop only once we're within the burn's stopping distance (so a fast
        // arrival still ignites in time) AND below a low flip ceiling so a nominal aero-braked entry
        // doesn't flip prematurely and waste propellant on a huge high-altitude burn.
        // Flip altitude: scale with the burn's stopping distance for a fast arrival, but never
        // below ~800 m so a vessel already at belly-flop terminal velocity (~70-100 m/s) still has
        // comfortable room to flip and null the descent (a too-low flip can't arrest it in time).
        const double FlipCeiling = 8_000.0;
        double pressure = vessel.GetAmbientPressure(body);
        double flipIgnitionThrottle = shipEngines?.Definition.MinThrottle > 0.0
            ? shipEngines.Definition.MinThrottle
            : 0.40;
        double fullAngularAuthority = aeroPhase
            ? System.Math.Max(0.01,
                vessel.Parts.GetMaximumPitchYawAngularAcceleration(pressure) * flipIgnitionThrottle)
            : 0.01;
        double flipAngle = AttitudeGuidance.ErrorAngleRadians(vessel.Orientation, retroTarget);
        double flipTime = EstimateFlipTime(flipAngle, fullAngularAuthority, maxRate: 0.35);
        double flipAlt = aBrake > 0.5
            ? System.Math.Clamp(stopDist * 2.2 + vDown * (flipTime + 3.0), 3_000.0, FlipCeiling)
            : 0.0;   // can't brake yet (still hypersonic) — keep belly-flop
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero && vDown > 5.0 && _alt <= flipAlt)
        {
            _phase = Edl.Retro;
            _flipInProgress = true;
            _flipElapsed = 0.0;
            mission?.EnterPhase(MissionPhase.RETRO_BURN);
        }

        switch (_phase)
        {
            case Edl.Entry:
                if (_heat > 4.0e7) { _phase = Edl.Peak; mission?.EnterPhase(MissionPhase.PEAK_HEATING); }
                break;
            case Edl.Peak:
                // Heating subsides once we've descended through the dense layer.
                if (_alt < atmoTop * 0.40) { _phase = Edl.Aero; mission?.EnterPhase(MissionPhase.AERO_DESCENT); }
                break;
            case Edl.Aero:
                break;   // retro ignition handled by the physics gate above
            case Edl.Retro:
                if (_alt < 1500.0) { _phase = Edl.Final; mission?.EnterPhase(MissionPhase.FINAL_DESCENT); }
                break;
            case Edl.Final:
                if (_alt < 500.0)
                {
                    _legsDeployed = true;
                    foreach (var gear in vessel.Parts.Parts.Where(
                                 p => p.Definition.Category == PartCategory.Landing))
                        gear.IsDeployed = true;
                }
                if (vessel.IsSurfaceSettled)
                {
                    _phase = Edl.Touchdown;
                    Touchdown(vessel, body, mission);
                    return;
                }
                break;
            case Edl.Touchdown:
                return;
        }

        // ── Attitude: belly-flop in the aero phases, flip-and-burn for the descent ─
        // Entry/Peak/Aero: present the long axis broadside to the airflow (max drag,
        // heat-shield windward) to bleed velocity aerodynamically like real Starship.
        // Retro/Final: flip so the engines (local +Y thrust) point retrograde.
        Vector3d aimAxis;
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero)
        {
            // Fly a lift-up ~70° AoA instead of exact 90° broadside. Exact broadside has
            // CL=0 for a symmetric body and degenerates into a steep ballistic entry; this
            // target retains nearly all projected drag while generating Starship-like L/D.
            aimAxis = AerodynamicsModel.ComputeLiftUpEntryAxis(up, velDir);
        }
        else if (_phase == Edl.Final && _horiz < 12.0)
        {
            // Stay primarily upright but cant into the lateral velocity so the same thrust
            // command can actually remove drift. A perfectly vertical axis cannot satisfy a
            // horizontal-speed error and otherwise turns that error into an endless hover.
            // In the last 30 m above the feet, blend that cant back to vertical: arriving
            // tilted consumes suspension stroke geometrically before impact and overloads the
            // downhill foot even at a gentle vertical speed.
            Vector3d lateralVelocity = surfVel - up * _vUp;
            const double contactDatumAlt = 7.85;
            double flareBlend = System.Math.Clamp(
                (_alt - contactDatumAlt) / 30.0, 0.0, 1.0);
            double tiltRatio = System.Math.Min(
                System.Math.Tan(20.0 * MathUtils.DEG_TO_RAD), _horiz * 0.04)
                * flareBlend;
            aimAxis = lateralVelocity.Magnitude > 1e-3
                ? (up - lateralVelocity.Normalized * tiltRatio).Normalized
                : up;
        }
        else
        {
            aimAxis = -velDir;                              // engines retrograde
        }
        // In the aero phases pitch is not enough: roll the vehicle so the actual tiled
        // local -X belly faces the velocity vector. This keeps rendering, heating and drag
        // on the same physical side of the Ship. During the landing burn only the thrust
        // axis matters, so use the shortest rotation.
        Quaterniond desiredAttitude = _phase is Edl.Entry or Edl.Peak or Edl.Aero
            ? AerodynamicsModel.ComputeBellyFirstOrientation(aimAxis, velDir)
            : ShortestArc(Vector3d.Up, aimAxis);
        vessel.PitchYawRoll = _phase is Edl.Entry or Edl.Peak or Edl.Aero
            ? AttitudeGuidance.ComputeCommand(
                vessel.Orientation,
                desiredAttitude,
                vessel.AngularVelocity,
                proportionalGain: 2.6,
                dampingGain: 1.2,
                allowRoll: true)
            : AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                aimAxis,
                vessel.AngularVelocity,
                proportionalGain: 2.2,
                dampingGain: 6.0);
        _attitudeErrorDeg = AttitudeGuidance.ErrorAngleRadians(
            vessel.Orientation, desiredAttitude) * MathUtils.RAD_TO_DEG;

        if (_flipInProgress)
        {
            _flipElapsed += delta;
            if (_attitudeErrorDeg < 5.0)
            {
                _flipInProgress = false;
                GD.Print($"[EDL] physical flip complete in {_flipElapsed:F1}s");
            }
        }

        // ── Throttle: closed-loop descent-rate profile to a soft touchdown ──────
        // By the time we flip (low, post-belly-flop) the velocity is mostly vertical. Track a
        // target descent rate that follows a constant-deceleration profile easing to 1.2 m/s at
        // first foot contact. Reserve braking authority (use 60%
        // of thrust for the profile) so the closed loop has headroom and the engine spool can keep
        // up — the old minimum-energy "stop exactly at the ground" burn commanded almost no thrust
        // until the last instant and touched down hot.
        if (_phase is Edl.Retro or Edl.Final)
        {
            // Once a foot has touched, commit to the compliant gear: shut the engines down
            // and let spring/damper/friction settle the body. Relighting against a loaded foot
            // would hide the contact dynamics and can overload one leg.
            if (_phase == Edl.Final && vessel.HasSurfaceContact)
                _landingCutoffCommitted = true;
            if (_phase == Edl.Final && _landingCutoffCommitted)
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                shipEngines?.SelectEngineCount(0);
                return;
            }

            const double contactDatumAlt = 7.85; // 7.50 m leg offset + 0.35 m foot radius
            const double touchdownRate = 1.20;
            // Target descent rate: a gentle LINEAR profile that eases to 1.2 m/s at first
            // physical foot contact and
            // is already below the post-belly-flop terminal velocity (~70 m/s) at the flip, so the
            // burn starts braking immediately. Cap it by a constant-deceleration limit so a faster
            // arrival is still braked hard enough. Close the loop with gravity feed-forward.
            double heightToContact = System.Math.Max(0.0, _alt - contactDatumAlt);
            double vTargetLin = touchdownRate + heightToContact * 0.035;
            double vTargetMax = System.Math.Sqrt(2.0 * 0.60 * aThrustFull * heightToContact)
                + touchdownRate;
            double vTarget    = System.Math.Min(vTargetLin, vTargetMax);
            double horizontalTarget = System.Math.Max(0.5, _alt * 0.02);
            double verticalError = vDown - vTarget;
            double horizontalError = _horiz - horizontalTarget;
            double coupledHorizontalError = _phase == Edl.Retro ? horizontalError : 0.0;
            double brakingError = System.Math.Max(0.0,
                System.Math.Max(verticalError, coupledHorizontalError));
            // Divide by the commanded thrust axis, not by -velocity. In final vertical flight
            // the velocity can pass through zero while the engine remains upright; using
            // -velocity there creates a singular 5 g command and launches the vehicle upward.
            double thrustUpComponent = System.Math.Max(0.20, aimAxis.Dot(up));
            // A small bounded descent bias prevents an endless hover when below the target
            // rate, while retaining at least ~0.85 g of support (no free-fall/relight cycle).
            double descentBias = System.Math.Clamp(0.35 * verticalError, -1.5, 0.0);
            double aCmd = 1.6 * brakingError
                + g / thrustUpComponent
                + descentBias
                - 1.2 * System.Math.Max(0.0, _vUp);
            bool alignedForBurn = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(aimAxis)
                > System.Math.Cos(15.0 * MathUtils.DEG_TO_RAD);
            if (!alignedForBurn && _phase == Edl.Retro)
            {
                // Flight-proven Starship sequence: ignite three centre Raptors as
                // the flip begins, then let gimbal authority rotate the vehicle.
                shipEngines?.SelectEngineCount(3);
                vessel.Throttle = shipEngines?.ApplyThrottleFloor(flipIgnitionThrottle)
                    ?? flipIgnitionThrottle;
            }
            else
            {
                CommandLandingEngines(vessel, body, shipEngines, aCmd, mass);
            }
        }
        else
        {
            vessel.Throttle = 0.0;                          // unpowered aero entry
        }
    }

    private void Touchdown(Vessel vessel, CelestialBody body, MissionManager? mission)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        mission?.EnterPhase(MissionPhase.LANDED);
        GD.Print($"[EDL] TOUCHDOWN settled on {body.Name}  vUp={_vUp:F1} m/s " +
            $"contacts={vessel.LastSurfaceContact?.ContactCount ?? 0}");
    }

    private void Deactivate()
    {
        if (_phase != Edl.Inactive)
        {
            var vessel = SimulationBridge.Instance?.ActiveVessel;
            foreach (var engine in vessel?.Parts.Parts.Where(
                         p => p.Definition.Category == PartCategory.Engine)
                     ?? Enumerable.Empty<Part>())
                engine.SelectEngineCount(System.Math.Max(1, engine.Definition.EngineCount));
            foreach (var gear in vessel?.Parts.Parts.Where(
                         p => p.Definition.Category == PartCategory.Landing)
                     ?? Enumerable.Empty<Part>())
                gear.IsDeployed = false;
            if (vessel != null)
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
            }
            _phase = Edl.Inactive;
            _flipInProgress = false;
            _landingCutoffCommitted = false;
            Visible = false;
        }
    }

    // ── HUD overlay ─────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (_phase == Edl.Inactive) return;
        var vp = GetViewportRect().Size;

        // Plasma vignette during high heating.
        if (_phase is Edl.Entry or Edl.Peak)
        {
            float intensity = (float)System.Math.Clamp(_heat / 8.0e7, 0.0, 1.0);
            DrawPlasma(vp, intensity);
        }

        DrawAltimeter(vp);
        DrawTelemetry(vp);

        // The aero phases are the ones that can burn the vehicle; below them the panel would
        // just be noise on a descent that is already thermally over.
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero)
            DrawThermal(vp);

        DrawPhaseBanner(vp);
    }

    private void DrawPlasma(Vector2 vp, float k)
    {
        // Layered translucent bands from screen edge — brighter at the bottom (windward).
        var hot = new Color(1.0f, 0.45f, 0.12f);
        int bands = 7;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            float thick = vp.Y * 0.5f * (1f - t);
            float a = k * 0.22f * (1f - t);
            DrawRect(new Rect2(0, vp.Y - thick, vp.X, thick), new Color(hot, a));   // bottom glow
            DrawRect(new Rect2(0, 0, vp.X, thick * 0.5f), new Color(hot, a * 0.4f)); // top
        }
    }

    private void DrawAltimeter(Vector2 vp)
    {
        const float maxAlt = 5000f;
        float x = 70f, top = vp.Y * 0.25f, h = vp.Y * 0.5f, w = 22f;
        DrawRect(new Rect2(x, top, w, h), new Color(0.05f, 0.07f, 0.10f, 0.75f));
        DrawRect(new Rect2(x, top, w, h), new Color(0.45f, 0.65f, 0.95f, 0.6f), false, 1.4f);

        float frac = (float)System.Math.Clamp(_alt / maxAlt, 0, 1);
        float markY = top + h * (1f - frac);
        var col = _legsDeployed ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.8f, 0.25f);
        DrawRect(new Rect2(x - 5, markY - 2, w + 10, 4), col);
        Text($"{_alt:F0} m", new Vector2(x + w + 10, markY + 5), col, 16);

        // ticks
        for (int i = 0; i <= 5; i++)
        {
            float ty = top + h * (i / 5f);
            DrawLine(new Vector2(x, ty), new Vector2(x + 6, ty), new Color(0.5f, 0.6f, 0.7f, 0.7f), 1f);
            Text($"{(5 - i) * 1000}", new Vector2(x - 50, ty + 5), new Color(0.55f, 0.62f, 0.72f), 11);
        }
    }

    /// <summary>
    /// Reads the same thermal state the simulation is actually integrating, so the crew sees
    /// the numbers that decide their survival rather than a decorative gauge.
    /// </summary>
    private void RefreshThermalState(
        Vessel vessel, CelestialBody body, double density, double speed, Vector3d surfVel)
    {
        _fluxNow = ThermalModel.ComputeHeatFlux(
            density, speed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));

        var flowLocal = speed > 1e-6
            ? vessel.Orientation.Inverse().Rotate(surfVel.Normalized)
            : Vector3d.Zero;
        _shieldAlign = ThermalModel.WindwardFactor(flowLocal);

        _skinTemp = 0.0;
        _hullRatio = 0.0;
        _thermalDamage = 0.0;

        foreach (var part in vessel.Parts.Parts)
        {
            // Only the tiled parts say anything about the heat shield. The engine cluster
            // carries no tiles by design and runs hot without meaning anything is wrong.
            if (!part.Definition.HasHeatShield) continue;

            if (part.SkinTemperature > _skinTemp)   _skinTemp = part.SkinTemperature;
            if (part.ThermalRatio    > _hullRatio)  _hullRatio = part.ThermalRatio;
            if (part.ThermalDamage   > _thermalDamage) _thermalDamage = part.ThermalDamage;
        }
    }

    private void DrawThermal(Vector2 vp)
    {
        // Left-hand column, clear of the flight/orbit panels the rest of the HUD owns.
        float px = 260f, py = vp.Y * 0.30f;
        const float pw = 230f, ph = 200f;

        DrawRect(new Rect2(px - 14, py - 26, pw, ph), new Color(0.04f, 0.06f, 0.09f, 0.78f));
        DrawRect(new Rect2(px - 14, py - 26, pw, ph), new Color(0.45f, 0.65f, 0.95f, 0.35f), false, 1.2f);
        Text("THERMAL", new Vector2(px - 6, py - 8), new Color(0.55f, 0.68f, 0.85f), 13);

        float x = px, y = py + 14f;
        var label = new Color(0.6f, 0.7f, 0.82f);

        Text("TPS FACE", new Vector2(x, y), label, 13);
        Text($"{_skinTemp:F0} K", new Vector2(x, y + 20),
            _skinTemp > 1200 ? new Color(1f, 0.62f, 0.25f) : new Color(0.9f, 0.95f, 1f), 20);

        // The hull bar is the one that matters: at 1.0 the structure is failing.
        Text("HULL", new Vector2(x, y + 52), label, 13);
        float ratio = (float)System.Math.Clamp(_hullRatio, 0.0, 1.2);
        Color hullCol = _hullRatio > 0.9 ? new Color(1f, 0.3f, 0.25f)
                      : _hullRatio > 0.65 ? new Color(1f, 0.8f, 0.3f)
                      : new Color(0.45f, 1f, 0.6f);

        const float barW = 130f, barH = 12f;
        DrawRect(new Rect2(x, y + 60, barW, barH), new Color(0.05f, 0.07f, 0.10f, 0.8f));
        DrawRect(new Rect2(x, y + 60, barW * ratio / 1.2f, barH), hullCol);
        DrawRect(new Rect2(x, y + 60, barW, barH), new Color(0.45f, 0.65f, 0.95f, 0.5f), false, 1.2f);
        Text($"{_hullRatio * 100.0:F0}%", new Vector2(x + barW + 10, y + 71), hullCol, 16);

        // Shield alignment is the ACTIONABLE number — it is the one the pilot can fix.
        Text("SHIELD", new Vector2(x, y + 92), label, 13);
        Color alignCol = _shieldAlign > 0.85 ? new Color(0.45f, 1f, 0.6f)
                       : _shieldAlign > 0.5  ? new Color(1f, 0.8f, 0.3f)
                       : new Color(1f, 0.3f, 0.25f);
        Text($"{_shieldAlign * 100.0:F0}%", new Vector2(x, y + 112), alignCol, 20);

        // Only shout when it actually matters: a shield off the flow with real heat behind it.
        if (_shieldAlign < 0.7 && _fluxNow > 5.0e4)
            Text("SHIELD OFF FLOW", new Vector2(x, y + 140), new Color(1f, 0.3f, 0.25f), 18);

        if (_thermalDamage > 0.0)
            Text($"TPS DAMAGE {_thermalDamage * 100.0:F0}%",
                new Vector2(x, y + 164), new Color(1f, 0.45f, 0.3f), 16);
    }

    private void DrawTelemetry(Vector2 vp)
    {
        float x = 120f, y = vp.Y * 0.25f - 70f;
        double vDown = -_vUp;
        Color vsCol = System.Math.Abs(vDown) > 50 ? new Color(1f, 0.35f, 0.3f)
                    : System.Math.Abs(vDown) > 10 ? new Color(1f, 0.82f, 0.3f)
                    : new Color(0.4f, 1f, 0.5f);
        Text("VERTICAL",   new Vector2(x, y),      new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{vDown:+0;-0} m/s", new Vector2(x, y + 20), vsCol, 22);
        Text("HORIZONTAL", new Vector2(x, y + 52), new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{_horiz:F0} m/s", new Vector2(x, y + 72), new Color(0.9f, 0.95f, 1f), 20);
        Text("G-FORCE",    new Vector2(x, y + 104), new Color(0.6f, 0.7f, 0.82f), 13);
        Color gCol = _gForce > 4 ? new Color(1f, 0.4f, 0.3f) : new Color(0.85f, 0.9f, 1f);
        Text($"{_gForce:F1} g", new Vector2(x, y + 124), gCol, 20);
    }

    private void DrawPhaseBanner(Vector2 vp)
    {
        string label = _phase switch
        {
            Edl.Entry => "ATMOSPHERIC ENTRY",
            Edl.Peak  => "PEAK HEATING",
            Edl.Aero  => "AERODYNAMIC DESCENT",
            Edl.Retro => "RETRO BURN",
            Edl.Final => "FINAL DESCENT",
            Edl.Touchdown => "TOUCHDOWN",
            _ => "",
        };
        var col = _phase switch
        {
            Edl.Entry => new Color(1f, 0.6f, 0.3f),
            Edl.Peak  => new Color(1f, 0.35f, 0.2f),
            Edl.Aero  => new Color(1f, 0.78f, 0.4f),
            Edl.Retro => new Color(0.5f, 0.85f, 1f),
            Edl.Final => new Color(0.6f, 0.9f, 1f),
            Edl.Touchdown => new Color(0.45f, 1f, 0.6f),
            _ => Colors.White,
        };
        var size = _font.GetStringSize(label, HorizontalAlignment.Center, -1, 30);
        var pos  = new Vector2((vp.X - size.X) * 0.5f, vp.Y * 0.16f);
        DrawString(_font, pos + new Vector2(2, 2), label, HorizontalAlignment.Left, -1, 30, new Color(0, 0, 0, 0.7f));
        DrawString(_font, pos, label, HorizontalAlignment.Left, -1, 30, col);

        string sub = $"EDL · {_bodyName.ToUpperInvariant()}" + (_legsDeployed ? "   LEGS DOWN" : "");
        Text(sub, new Vector2((vp.X - _font.GetStringSize(sub, HorizontalAlignment.Center, -1, 14).X) * 0.5f, vp.Y * 0.16f + 34), new Color(0.7f, 0.78f, 0.88f), 14);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void Text(string s, Vector2 pos, Color c, int size) =>
        DrawString(_font, pos, s, HorizontalAlignment.Left, -1, size, c);

    private static double MaxThrustAccel(Vessel vessel, CelestialBody body, double mass)
    {
        if (mass <= 0) return 0;
        return vessel.GetMaximumThrust(body) / mass;
    }

    private static double MaxLandingThrustAccel(
        Vessel vessel, CelestialBody body, Part? engineCluster, double mass)
    {
        if (mass <= 0.0) return 0.0;
        if (engineCluster == null) return MaxThrustAccel(vessel, body, mass);
        int represented = System.Math.Max(1, engineCluster.Definition.EngineCount);
        int landingCount = System.Math.Min(3, represented);
        double rated = engineCluster.GetRatedFullThrottleThrustMagnitude(
            vessel.GetAmbientPressure(body));
        return rated * landingCount / represented / mass;
    }

    private static void CommandLandingEngines(
        Vessel vessel, CelestialBody body, Part? engineCluster, double accelerationCmd, double mass)
    {
        if (engineCluster == null || mass <= 0.0)
        {
            vessel.Throttle = 0.0;
            return;
        }

        int represented = System.Math.Max(1, engineCluster.Definition.EngineCount);
        int maxLandingEngines = System.Math.Min(3, represented);
        double ratedCluster = engineCluster.GetRatedFullThrottleThrustMagnitude(
            vessel.GetAmbientPressure(body));
        double perEngine = ratedCluster / represented;
        double desiredThrust = System.Math.Max(0.0, accelerationCmd * mass);
        if (perEngine <= 1.0 || desiredThrust <= 1.0)
        {
            engineCluster.SelectEngineCount(0);
            vessel.Throttle = 0.0;
            return;
        }

        int selected = maxLandingEngines;
        for (int count = 1; count <= maxLandingEngines; count++)
        {
            if (desiredThrust <= perEngine * count)
            {
                selected = count;
                break;
            }
        }

        engineCluster.SelectEngineCount(selected);
        double throttle = desiredThrust / (perEngine * selected);
        vessel.Throttle = engineCluster.ApplyThrottleFloor(
            System.Math.Clamp(throttle, 0.0, 1.0));
    }

    private static double EstimateFlipTime(double angle, double angularAcceleration, double maxRate)
    {
        if (angle <= 0.0) return 0.0;
        angularAcceleration = System.Math.Max(1e-4, angularAcceleration);
        maxRate = System.Math.Max(1e-3, maxRate);
        double triangularAngle = maxRate * maxRate / angularAcceleration;
        if (angle <= triangularAngle)
            return 2.0 * System.Math.Sqrt(angle / angularAcceleration);
        return 2.0 * maxRate / angularAcceleration
             + (angle - triangularAngle) / maxRate;
    }

    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized; var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot > 0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d ax = System.Math.Abs(f.X) < 0.9 ? f.Cross(Vector3d.Right) : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(ax.Normalized, System.Math.PI);
        }
        return Quaterniond.FromAxisAngle(f.Cross(t).Normalized,
            System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0)));
    }
}
