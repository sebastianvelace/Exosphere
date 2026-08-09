namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Systems;

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

    private enum Edl { Inactive, Entry, Peak, Aero, Retro, Catch, Final, Caught, Touchdown }
    private Edl _phase = Edl.Inactive;

    // ── Tower catch (Mechazilla) ───────────────────────────────────────────
    // Abort decision: below this altitude, a catch attempt still outside tolerance must
    // divert to a normal leg landing rather than risk a low, slow collision with tower
    // structure. Above it there is still room to keep closing the position error.
    private const double CatchAbortDecisionAltitudeM = 300.0;
    private const double CatchAbortHorizontalMissToleranceM = 8.0;
    private const double CatchAbortHorizontalSpeedToleranceMps = 3.0;
    private bool _towerCatchAborted;

    // ── Trigger thresholds ────────────────────────────────────────────────────
    private const double EntrySpeed   = 1200.0;   // m/s surface speed to arm entry

    // The landing engines must not be cut by the first foot that happens to enter the
    // penalty-contact envelope. A real vehicle still has thrust and attitude authority while
    // the other legs are loading; cutting at a single contact turns a gentle approach into a
    // falling rigid body and can create a catastrophic one-leg bottom-out on the next tick.
    // Keep this gate deliberately stricter than Universe's final settled criterion: it is only
    // an engine-cut decision, never a touchdown declaration.
    private const double ContactCutoffNormalSpeedMps = 2.0;
    private const double ContactCutoffTangentialSpeedMps = 1.0;
    private const int MinimumFinalApproachEngines = 2;

    // ── Live telemetry (refreshed each frame) ─────────────────────────────────
    private double _alt, _vUp, _horiz, _gForce, _heat;
    private string _bodyName = "";

    // ── Thermal state (the entry is now survivable-or-not, so the crew has to SEE it) ──
    private double _skinTemp;        // TPS face (K) — supposed to be white-hot
    private double _hullRatio;       // structure temperature / tolerance — this is what kills
    private double _thermalDamage;   // irreversible char, 0..1
    private double _shieldAlign;     // 0..1 — how squarely the tiles meet the flow
    private double _fluxNow;         // W/m², free-stream convective flux

    /// <summary>
    /// Sim-side peak-g bookkeeping. Lives in <c>ExosphereSimulation</c> so the peak-load
    /// contract is unit-testable and Godot-free; this controller only samples and draws it.
    /// </summary>
    private readonly EntryLoadTracker _load = new();

    /// <summary>Blackout state read back from the simulation's comms system (never written here).</summary>
    private bool _blackout;
    private double _blackoutSeconds;

    private Font _font = null!;
    private bool _legsDeployed;
    private bool _flipInProgress;
    private bool _landingCutoffCommitted;
    private int _landingEngineCount;
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
        _gForce = vessel.GetProperAcceleration(body).Magnitude / EntryLoadTracker.StandardGravity;
        // Track the peak only once the EDL track is armed, so an ascent's 3 g does not
        // pre-poison the entry readout.
        if (_phase != Edl.Inactive)
            _load.Update(delta, _gForce);

        var comms = SystemsController.Instance?.Comms;
        _blackout = comms?.PlasmaBlackout ?? false;
        _blackoutSeconds = comms?.PlasmaBlackoutSeconds ?? 0.0;

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
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("booster"));
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
                _landingEngineCount = 3;
                _flipElapsed = 0.0;
                _towerCatchAborted = false;
                _load.Reset();
                Visible = true;
                mission?.EnterPhase(MissionPhase.ENTRY);
            }
            else return;
        }

        // Structural dead-stick: stop writing guidance. Atmosphere still flies the wreck.
        if (vessel.StructuralControlLost)
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.SASEnabled = false;
            QueueRedraw();
            return;
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
            p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"));
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

        // Tower-catch horizontal guidance: simple proportional homing on the position error
        // against the cradle, not a full cross-range guidance law (see the EDL scope note in
        // the Mechazilla plan — this only has to null a final-approach miss of tens of
        // metres). Computed once here so both the attitude cant and the throttle's braking
        // term below read the same error instead of two independently-derived vectors.
        Vector3d catchLateralVelocityError = Vector3d.Zero;
        if (_phase == Edl.Catch)
        {
            Vector3d offsetFromTarget = vessel.Position - vessel.CatchTargetPositionWorld;
            Vector3d horizontalOffset = offsetFromTarget - up * offsetFromTarget.Dot(up);
            double missDistance = horizontalOffset.Magnitude;
            double closingSpeed = System.Math.Clamp(missDistance * 0.35, 0.0, 6.0);
            Vector3d towardTarget = missDistance > 1e-3 ? -horizontalOffset.Normalized : Vector3d.Zero;
            Vector3d desiredHorizontalVelocity = towardTarget * closingSpeed;
            catchLateralVelocityError = (surfVel - up * _vUp) - desiredHorizontalVelocity;
        }
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
                if (_alt < 1500.0)
                {
                    bool attemptCatch = vessel.IsAttemptingTowerCatch
                        && !_towerCatchAborted && vessel.HasCatchPins;
                    _phase = attemptCatch ? Edl.Catch : Edl.Final;
                    mission?.EnterPhase(MissionPhase.FINAL_DESCENT);
                }
                break;
            case Edl.Catch:
            {
                Vector3d offset = vessel.Position - vessel.CatchTargetPositionWorld;
                double missDistance = (offset - up * offset.Dot(up)).Magnitude;
                if (_alt < CatchAbortDecisionAltitudeM
                    && (missDistance > CatchAbortHorizontalMissToleranceM
                        || _horiz > CatchAbortHorizontalSpeedToleranceMps))
                {
                    // Diverting rather than forcing the catch: a miss this low and slow is
                    // exactly the case a real abort-to-legs guards against — see the EDL scope
                    // note in the Mechazilla plan. The vessel keeps whatever legs it has;
                    // a V3 ship built catch-only would still need its own legs data to survive
                    // this path, which is a known gap, not one this pass papers over.
                    _towerCatchAborted = true;
                    vessel.IsAttemptingTowerCatch = false;
                    _phase = Edl.Final;
                    GD.Print($"[EDL] tower catch aborted at {_alt:F0} m " +
                        $"(miss={missDistance:F1} m, horiz={_horiz:F1} m/s) — diverting to leg landing");
                    break;
                }
                if (vessel.IsCaught)
                {
                    _phase = Edl.Caught;
                    Caught(vessel, body, mission);
                    return;
                }
                break;
            }
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
            case Edl.Caught:
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
        else if (_phase == Edl.Catch || (_phase == Edl.Final && _horiz < 12.0))
        {
            // Stay primarily upright but cant into the lateral error so the same thrust
            // command can actually remove it. A perfectly vertical axis cannot satisfy a
            // horizontal error and otherwise turns that error into an endless hover.
            // In the last 30 m above the feet, blend that cant back to vertical: arriving
            // tilted consumes suspension stroke geometrically before impact and overloads the
            // downhill foot even at a gentle vertical speed. A catch approach reuses the same
            // shape of blend even though it has no feet, so the final metres still arrive
            // upright into the cradle rather than canted.
            Vector3d lateralVelocity = _phase == Edl.Catch
                ? catchLateralVelocityError
                : surfVel - up * _vUp;
            const double contactDatumAlt = 7.85;
            double flareBlend = System.Math.Clamp(
                (_alt - contactDatumAlt) / 30.0, 0.0, 1.0);
            double tiltRatio = System.Math.Min(
                System.Math.Tan(20.0 * MathUtils.DEG_TO_RAD), lateralVelocity.Magnitude * 0.04)
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
        if (_phase is Edl.Retro or Edl.Catch or Edl.Final)
        {
            // Once at least three feet share a genuinely slow, low-drift contact, commit to
            // the compliant gear: shut the engines down and let spring/damper/friction settle
            // the body. A single foot is not a touchdown. It is a transient load case in which
            // thrust and TVC must remain available while the remaining feet close the gap.
            // This is intentionally a kinematic safety gate, not a distance-based success gate;
            // IsSurfaceSettled below remains the only path to the LANDED mission phase.
            if (_phase == Edl.Final && IsSafeMultiLegContact(vessel, body))
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
            // A catch approach's "ground" is the cradle's height up the tower, not the planet
            // surface below it — the descent profile must arrest at the arms, not fly through
            // them toward the ground the tower stands on.
            double effectiveContactDatumAlt = _phase == Edl.Catch
                ? body.GetAltitude(vessel.CatchTargetPositionWorld)
                : contactDatumAlt;
            // Target descent rate: a gentle LINEAR profile that eases to 1.2 m/s at first
            // physical foot contact and
            // is already below the post-belly-flop terminal velocity (~70 m/s) at the flip, so the
            // burn starts braking immediately. Cap it by a constant-deceleration limit so a faster
            // arrival is still braked hard enough. Close the loop with gravity feed-forward.
            double heightToContact = System.Math.Max(0.0, _alt - effectiveContactDatumAlt);
            double vTargetLin = touchdownRate + heightToContact * 0.035;
            double vTargetMax = System.Math.Sqrt(2.0 * 0.60 * aThrustFull * heightToContact)
                + touchdownRate;
            double vTarget    = System.Math.Min(vTargetLin, vTargetMax);
            double horizontalTarget = System.Math.Max(0.5, _alt * 0.02);
            double verticalError = vDown - vTarget;
            double horizontalError = _phase == Edl.Catch
                ? catchLateralVelocityError.Magnitude
                : _horiz - horizontalTarget;
            double coupledHorizontalError = _phase is Edl.Retro or Edl.Catch ? horizontalError : 0.0;
            double brakingError = System.Math.Max(0.0,
                System.Math.Max(verticalError, coupledHorizontalError));
            // Divide by the commanded thrust axis, not by -velocity. In final vertical flight
            // the velocity can pass through zero while the engine remains upright; using
            // -velocity there creates a singular 5 g command and launches the vehicle upward.
            double thrustUpComponent = System.Math.Max(0.20, aimAxis.Dot(up));
            // Keep tracking the commanded descent instead of asymptotically hovering above
            // the pad.  The previous 0.35 gain settled near zero vertical speed at ~45 m
            // because one Raptor's minimum useful thrust nearly balanced gravity.  A stronger
            // bounded bias gives the controller enough downward acceleration to rejoin the
            // profile, while retaining at least ~0.69 g of support (no free-fall/relight).
            double descentBias = System.Math.Clamp(0.90 * verticalError, -3.0, 0.0);
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

    private void Caught(Vessel vessel, CelestialBody body, MissionManager? mission)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        mission?.EnterPhase(MissionPhase.CAUGHT);
        GD.Print($"[EDL] CAUGHT by the tower at {_bodyName}  vUp={_vUp:F1} m/s " +
            $"contacts={vessel.LastCatchContact?.ContactCount ?? 0}");
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
            _landingEngineCount = 0;
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

        if (_blackout)
            DrawBlackoutBanner(vp);
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
        _fluxNow = vessel.ComputeStagnationHeatFlux(density, surfVel);

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
        DrawGLoad(new Vector2(x, y + 104));
    }

    /// <summary>
    /// Instantaneous load, the held peak, and a bar that escalates through the bands a real
    /// entry walks up. The 4–5 g wall is the whole point, so the bar is scaled to
    /// <see cref="EntryLoadTracker.SevereG"/> and switches to the shared Warning / Alert
    /// tokens exactly at the band edges instead of fading continuously.
    /// </summary>
    private void DrawGLoad(Vector2 origin)
    {
        float x = origin.X, y = origin.Y;
        var label = new Color(0.6f, 0.7f, 0.82f);
        Color gCol = BandColor(_load.Band);

        Text("G-FORCE", new Vector2(x, y), label, 13);
        Text($"{_gForce:F1} g", new Vector2(x, y + 20), gCol, 20);

        // Escalation bar: full scale is the severe band, with a tick at the 4 g wall.
        const float barW = 130f, barH = 8f;
        float barY = y + 28f;
        float frac = (float)System.Math.Clamp(_gForce / EntryLoadTracker.SevereG, 0.0, 1.0);
        DrawRect(new Rect2(x, barY, barW, barH), new Color(0.05f, 0.07f, 0.10f, 0.8f));
        DrawRect(new Rect2(x, barY, barW * frac, barH), gCol);
        float wallX = x + barW * (float)(EntryLoadTracker.HighG / EntryLoadTracker.SevereG);
        DrawLine(new Vector2(wallX, barY - 2), new Vector2(wallX, barY + barH + 2),
            InterfaceTheme.Alert, 1.4f);
        DrawRect(new Rect2(x, barY, barW, barH), new Color(0.45f, 0.65f, 0.95f, 0.45f), false, 1.1f);

        // The held peak is what the crew debriefs on, so it never disappears once recorded.
        if (_load.HasSample && _load.PeakG > 0.05)
        {
            Color peakCol = BandColor(_load.PeakBand);
            Text($"PEAK {_load.PeakG:F1} g", new Vector2(x, barY + barH + 16), peakCol, 15);
            // Only shout while the wall is actually being felt.
            if (_load.Band >= GLoadBand.High)
                Text("HIGH G", new Vector2(x + barW - 34, y + 20), InterfaceTheme.Alert, 15);
        }
    }

    private static Color BandColor(GLoadBand band) => band switch
    {
        GLoadBand.Severe => InterfaceTheme.Alert,
        GLoadBand.High => InterfaceTheme.Alert,
        GLoadBand.Elevated => InterfaceTheme.Warning,
        _ => InterfaceTheme.Text,
    };

    /// <summary>
    /// Comms blackout callout. The state itself is owned by the simulation
    /// (<c>CommsSystem.PlasmaBlackout</c>); this only renders it, centred low on the screen
    /// so it stays clear of the phase/telemetry blocks the rest of the HUD owns.
    /// </summary>
    private void DrawBlackoutBanner(Vector2 vp)
    {
        const string headline = "SIGNAL LOST — PLASMA BLACKOUT";
        string sub = $"IONISED SHEATH · T+{_blackoutSeconds:F0}s · SIGNAL RETURNS AS SPEED BLEEDS OFF";

        var headSize = _font.GetStringSize(headline, HorizontalAlignment.Center, -1, 24);
        var subSize = _font.GetStringSize(sub, HorizontalAlignment.Center, -1, 13);
        float cx = vp.X * 0.5f;
        float top = vp.Y * 0.74f;

        DrawRect(new Rect2(cx - headSize.X * 0.5f - 22f, top - 26f,
            headSize.X + 44f, 60f), new Color(0.04f, 0.06f, 0.09f, 0.72f));
        DrawRect(new Rect2(cx - headSize.X * 0.5f - 22f, top - 26f,
            headSize.X + 44f, 60f), new Color(InterfaceTheme.Alert, 0.55f), false, 1.2f);

        DrawString(_font, new Vector2(cx - headSize.X * 0.5f, top), headline,
            HorizontalAlignment.Left, -1, 24, InterfaceTheme.Alert);
        DrawString(_font, new Vector2(cx - subSize.X * 0.5f, top + 22f), sub,
            HorizontalAlignment.Left, -1, 13, InterfaceTheme.TextMuted);
    }

    /// <summary>
    /// UX-014: the EDL phase title duplicated the HUD's mission-phase banner, so this
    /// controller no longer draws one. It publishes the EDL-only remainder and
    /// <see cref="HUDController"/> renders it inside the single banner.
    /// </summary>
    public string? BannerStatus => _phase == Edl.Inactive
        ? null
        : $"EDL · {_bodyName.ToUpperInvariant()}" + (_legsDeployed ? "  ·  LEGS DOWN" : "");

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

    private void CommandLandingEngines(
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

        int requested = maxLandingEngines;
        for (int count = 1; count <= maxLandingEngines; count++)
        {
            if (desiredThrust <= perEngine * count)
            {
                requested = count;
                break;
            }
        }

        // A landing burn is a monotonic engine-out sequence, not a bank that may chatter
        // on and off with every guidance correction. Start all three centre Raptors during
        // the flip. Once final descent has genuinely reduced the demand, step down 3→2→1
        // with margin; never relight an engine during this same burn.
        if (_landingEngineCount <= 0)
            _landingEngineCount = maxLandingEngines;
        int selected = System.Math.Min(_landingEngineCount, maxLandingEngines);
        if (_phase == Edl.Final)
        {
            // A two-engine final approach is the minimum stable authority for this
            // vehicle. One Raptor can nominally hold weight, but leaves no margin for
            // TVC, wind/drag asymmetry, or the first leg touching before the others. The
            // engine-out step is therefore deferred until the same safe multi-leg contact
            // gate that commits the shutdown; a single contact must never be allowed to
            // turn a controlled descent into an uncontrolled one-leg rebound.
            bool safeContact = IsSafeMultiLegContact(vessel, body);
            if (!safeContact)
                selected = System.Math.Max(MinimumFinalApproachEngines, selected);

            const double StepDownCapacityFraction = 0.75;
            if (safeContact)
            {
                while (selected > 1
                       && requested < selected
                       && desiredThrust <= perEngine * (selected - 1)
                           * StepDownCapacityFraction)
                    selected--;
            }
        }
        _landingEngineCount = selected;
        engineCluster.SelectEngineCount(selected);
        double throttle = desiredThrust / (perEngine * selected);
        vessel.Throttle = engineCluster.ApplyThrottleFloor(
            System.Math.Clamp(throttle, 0.0, 1.0));
    }

    private static bool IsSafeMultiLegContact(Vessel vessel, CelestialBody body)
    {
        var contact = vessel.LastSurfaceContact;
        if (contact == null || contact.ContactCount < 3
            || contact.HasOverload || contact.HasOverTravel)
            return false;

        Vector3d up = (vessel.Position - body.Position).Normalized;
        Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(body);
        double normalSpeed = System.Math.Abs(surfaceVelocity.Dot(up));
        double tangentialSpeed = (surfaceVelocity - up * surfaceVelocity.Dot(up)).Magnitude;
        return normalSpeed <= ContactCutoffNormalSpeedMps
            && tangentialSpeed <= ContactCutoffTangentialSpeedMps;
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
