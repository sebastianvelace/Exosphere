namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Launch-to-orbit autopilot (toggle with <b>G</b>). Flies the active vessel from the
/// pad to a circular orbit just above the dominant body's atmosphere: a gravity-turn
/// ascent that lofts the apoapsis to target, a coast to apoapsis, then a closed-loop
/// PEG-lite insertion burn that holds altitude (pointing the thrust at the angle whose
/// vertical component cancels gravity minus the centrifugal term) while building orbital
/// velocity — the correct profile for a TWR~1 upper stage that cannot circularize
/// impulsively. Auto-stages spent boosters. Draws a compact status banner.
///
/// A lighter <b>assist mode</b> (toggle with <b>H</b>) provides ONLY the gravity-turn
/// pitch guidance — steering the nose along the same program via PitchYawRoll — while
/// the player keeps the throttle ([Z]/[X]). Manual attitude input (W/S/A/D/Q/E) always
/// wins: the assist yields any frame the player commands attitude. The two modes are
/// mutually exclusive.
/// </summary>
public partial class AscentController : Control
{
    public static AscentController? Instance { get; private set; }

    private enum Phase { Idle, Ignition, Ascent, Coast, Insert, Done }
    private Phase _phase = Phase.Idle;
    private bool  _active;

    // ── Assist mode ([H]) ───────────────────────────────────────────────────────
    // Pilot-in-the-loop helper: applies ONLY the gravity-turn PITCH GUIDANCE (commanded
    // through PitchYawRoll, NOT by overriding Orientation), leaving THROTTLE to the player
    // (hold-[Z]/[X]). If the player commands attitude (W/S/A/D/Q/E) the assist yields that
    // frame, so manual input always wins. Independent of the full [G] autopilot; engaging
    // one disengages the other.
    // Ayuda con el piloto en el bucle: aplica SOLO la GUÍA de pitch del giro gravitacional
    // (vía PitchYawRoll, sin pisar Orientation) y deja el ACELERADOR al jugador ([Z]/[X]).
    // Si el jugador comanda actitud, el assist cede ese frame: el input manual siempre gana.
    private bool _assist;
    // P-gain on attitude error and damping on body rate — tuned for a smooth, non-twitchy
    // curve the weathervaning aero torque can settle without oscillation.
    private const double AssistGain = 2.2;
    private const double AssistDamp = 0.9;

    // ── Staging (hot-stage at MECO, not at depletion) ──────────────────────────────
    // Real Super Heavy MECO/hot-staging is at ~2.3-2.4 km/s and ~65 km, leaving a
    // boostback/landing reserve — NOT a burn-to-empty at apoapsis. Stage on velocity so
    // the upper stage (Starship) flies the orbital insertion, like the real vehicle.
    private const double StagingSpeed      = 2300.0;   // m/s surface speed to hot-stage SH
    private const double StagingMinAlt     = 45_000.0; // m floor so a slow climb still stages high
    private const double BoosterReserveFrac = 0.06;    // never burn the booster below ~6% prop
    private bool _mecoStaged;

    // Gravity-turn elevation angle (deg above the local horizon) as a function of altitude.
    // Vertical off the pad, then an AGGRESSIVE pitch-over so horizontal velocity builds early
    // instead of lofting to a high ballistic apoapsis: ~57° at 10 km, ~43° at 20 km, ~23° at
    // 40 km, ~5° by ~65 km. (The old law was linear-in-altitude and stayed ~85° to Max-Q.)
    private static double GravityTurnElevationDeg(double altMeters)
    {
        if (altMeters < 600.0) return 90.0;
        double f = System.Math.Sqrt(System.Math.Clamp((altMeters - 600.0) / 64_000.0, 0.0, 1.0));
        return System.Math.Clamp(90.0 - 85.0 * f, 5.0, 90.0);
    }

    // Engine spool-up: thrust ramps over this many seconds while the hold-downs stay clamped;
    // the vehicle only releases once it can actually lift its own weight (TWR > 1).
    private const double IgnitionTime = 3.0;
    private double _ignitionT;
    private double _twr, _q;   // live readout (thrust-to-weight, dynamic pressure)

    // Targets (computed from the body's atmosphere on engage).
    private double _apoTarget, _holdAlt, _peTarget;
    private string _bodyName = "";

    // Live readout.
    private double _apo, _per, _ecc, _alt;
    private Font   _font = null!;

    public override void _Ready()
    {
        Instance = this;
        _font = ThemeDB.GetFallbackFont();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        // Run AFTER the HUD's manual-input pass so the assist's PitchYawRoll guidance
        // overrides the zero the HUD writes when no attitude key is held. (Higher
        // ProcessPriority = later in the frame.)
        // Correr DESPUÉS del pase de input manual del HUD para que la guía del assist
        // sobreescriba el cero que el HUD escribe cuando no se pulsa ninguna tecla.
        ProcessPriority = 100;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.G)
            {
                if (_active) Disengage();
                else         Engage();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.H)
            {
                if (_assist) DisengageAssist();
                else         EngageAssist();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public void Engage()
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        var body = universe.GetDominantBody(vessel.Position);
        double atmoTop = body.Atmosphere?.MaxAltitude ?? 0.0;
        // Park just above the aerodynamically significant atmosphere, with a sane
        // floor. Note: a residual thermosphere now gives this parking orbit a slow
        // (RK4-only) decay rather than being eternal — acceptable for a low LEO hold.
        _holdAlt   = System.Math.Max(atmoTop + 10_000.0, 80_000.0);
        _apoTarget = _holdAlt + 2_000.0;
        _peTarget  = _holdAlt - 7_000.0;
        _bodyName  = body.Name;

        vessel.SASEnabled = false;
        _active = true;
        _mecoStaged = false;
        // Start with a real ignition sequence when still clamped: spool the engines up and only
        // release the hold-downs once thrust exceeds weight. If already flying, skip to ascent.
        if (vessel.IsGroundHeld)
        {
            _phase     = Phase.Ignition;
            _ignitionT = 0.0;
            vessel.Throttle = 0.0;
            MissionManager.Instance?.EnterPhase(MissionPhase.IGNITION);
        }
        else
        {
            _phase = Phase.Ascent;
            MissionManager.Instance?.EnterPhase(MissionPhase.LIFTOFF);
        }
        Visible = true;
        GD.Print($"[ASCENT-AP] engaged → target {_holdAlt/1000:F0} km circular over {_bodyName}");
    }

    private void Disengage()
    {
        _active = false;
        _phase  = Phase.Idle;
        Visible = _assist;   // keep banner if the assist is still on
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveVessel is { } v) v.Throttle = 0.0;
        if (bridge?.Universe is { } u && u.TimeScale > 1.0) u.TimeScale = 1.0;
    }

    // ── Assist ([H]): gravity-turn PITCH GUIDANCE only; player keeps the throttle ──
    public void EngageAssist()
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (vessel == null || bridge?.Universe == null) return;

        // Mutually exclusive with the full autopilot: turning on the assist drops [G].
        if (_active) Disengage();
        _assist = true;
        vessel.SASEnabled = false;   // SAS would fight the guidance torque
        Visible = true;
        GD.Print("[ASCENT-AP] gravity-turn ASSIST engaged — pitch guidance on, throttle is yours");
    }

    public void DisengageAssist()
    {
        _assist = false;
        if (!_active) Visible = false;
        var bridge = SimulationBridge.Instance;
        // Stop steering, but DO NOT touch throttle — the player owns it in assist mode.
        if (bridge?.ActiveVessel is { } v) v.PitchYawRoll = Vector3d.Zero;
        GD.Print("[ASCENT-AP] gravity-turn ASSIST disengaged");
    }

    public override void _Process(double delta)
    {
        // Assist mode runs on its own (it does not require the full autopilot to be active).
        if (_assist && !_active) { ProcessAssist(); return; }
        if (!_active) return;
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { Disengage(); return; }

        var body = universe.GetDominantBody(vessel.Position);

        Vector3d rel  = vessel.Position - body.Position;
        Vector3d vel  = vessel.Velocity - body.Velocity;
        Vector3d up   = rel.Normalized;
        Vector3d east = new Vector3d(0, 1, 0).Cross(up);
        east = east.Magnitude > 1e-6 ? east.Normalized : new Vector3d(0, 0, 1);
        double vUp = vel.Dot(up);
        _alt = body.GetAltitude(vessel.Position);

        // ── Ignition: spool the engines while clamped, lift off only at TWR > 1 ──────
        if (_phase == Phase.Ignition)
        {
            _ignitionT += delta;
            double ramp = System.Math.Clamp(_ignitionT / IgnitionTime, 0.0, 1.0);
            vessel.Throttle        = ramp;                       // engine spool-up
            vessel.Orientation     = ShortestArc(Vector3d.Up, up);
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.PitchYawRoll    = Vector3d.Zero;
            if (universe.TimeScale > 1.0) universe.TimeScale = 1.0;

            double rIgn   = rel.Magnitude;
            double gLocal = body.GM / (rIgn * rIgn);
            _q   = vessel.GetDynamicPressure(body);
            _twr = vessel.ComputeThrust(body).Magnitude
                 / System.Math.Max(vessel.TotalMass * gLocal, 1.0);

            if (vessel.IsGroundHeld)
            {
                // Hold the clamps until the engines can actually lift the stack.
                if (ramp >= 1.0 && _twr > 1.02)
                {
                    vessel.ReleaseGroundHold();
                    MissionManager.Instance?.EnterPhase(MissionPhase.LIFTOFF);
                    GD.Print($"[ASCENT-AP] liftoff — TWR {_twr:F2}");
                }
            }
            else if (_alt > 40.0)
            {
                _phase = Phase.Ascent;     // cleared the pad
            }
            QueueRedraw();
            return;
        }

        var oe = OrbitalElements.FromStateVector(rel, vel, body.GM, body.Id, universe.CurrentTime);
        _apo = oe.Apoapsis  - body.Radius;
        _per = oe.Periapsis - body.Radius;
        _ecc = oe.Eccentricity;

        Vector3d prograde = vel.Magnitude > 1e-3 ? vel.Normalized : east;
        Vector3d tang = prograde - up * prograde.Dot(up);
        tang = tang.Magnitude > 1e-3 ? tang.Normalized : east;

        // Latch into the insertion phase once apoapsis reaches target — it only decreases
        // after apoapsis, so this must not flip back to "ascending".
        if (_phase == Phase.Ascent && _apo >= _apoTarget) _phase = Phase.Insert;

        Vector3d dir;
        double throttle;
        double warp = universe.TimeScale;

        if (_per > _peTarget && _phase != Phase.Ascent)
        {
            // Stable orbit reached (periapsis clears the atmosphere) — cut and hand back.
            _phase = Phase.Done;
            vessel.Orientation = ShortestArc(Vector3d.Up, tang);
            vessel.Throttle = 0.0;
            if (universe.TimeScale > 1.0) universe.TimeScale = 1.0;
            MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
            GD.Print($"[ASCENT-AP] orbit reached {_apo/1000:F0}×{_per/1000:F0} km (e={_ecc:F3}) — disengaging");
            _active = false;
            QueueRedraw();
            return;
        }

        if (_phase == Phase.Ascent)
        {
            // Realistic gravity turn: vertical off the pad, then pitch over by an
            // altitude-scheduled elevation angle (aggressive — builds horizontal velocity
            // early instead of lofting). dir = horizon·cos(elev) + up·sin(elev).
            double elevDeg = GravityTurnElevationDeg(_alt);
            // Loft floor: the low-TWR upper stage flattens out before the apoapsis reaches the
            // parking target and would stall low (then start descending), so keep enough upward
            // pitch to drive the apoapsis up to target — easing the floor off as it nears.
            if (_apo < _apoTarget)
            {
                double need = System.Math.Clamp((_apoTarget - _apo) / 40_000.0, 0.0, 1.0);
                elevDeg = System.Math.Max(elevDeg, 8.0 + need * 22.0);   // 8°..30° floor
            }
            double elev = elevDeg * System.Math.PI / 180.0;
            dir = (east * System.Math.Cos(elev) + up * System.Math.Sin(elev)).Normalized;
            // Ease the throttle down through peak dynamic pressure to limit aero loads, then
            // back to full once past Max-Q — the real "throttling down … throttle up" profile.
            _q = vessel.GetDynamicPressure(body);
            throttle = System.Math.Clamp(1.0 - (_q - 22_000.0) / 18_000.0 * 0.4, 0.62, 1.0);
            // Real time during powered ascent so the climb to orbit takes its full, realistic
            // duration (~8-9 min) and is actually experienced — no fast-forward through it.
            warp = 1.0;
        }
        else if (vUp > 25.0)
        {
            // Coast to apoapsis before the circularization burn.
            _phase = Phase.Coast;
            dir = prograde; throttle = 0.0; warp = 4.0;
        }
        else
        {
            // PEG-lite insertion: thrust angle whose vertical component cancels (gravity −
            // centrifugal) holds altitude exactly while the horizontal component builds
            // orbital velocity; the required pitch falls to zero as speed reaches circular.
            _phase = Phase.Insert;
            double r = rel.Magnitude;
            double gLocal = body.GM / (r * r);
            double vH = (vel - up * vUp).Magnitude;
            double centrifugal = vH * vH / r;
            double aThrust = vessel.GetMaximumThrust(body) /
                System.Math.Max(vessel.TotalMass, 1.0);
            double sinFF  = (gLocal - centrifugal) / System.Math.Max(aThrust, 0.1);
            double altCorr = 0.00002 * (_holdAlt - _alt) - 0.002 * vUp;
            double sinCmd = System.Math.Clamp(sinFF + altCorr, -0.3, 0.95);
            double cosCmd = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - sinCmd * sinCmd));
            dir = (tang * cosCmd + up * sinCmd).Normalized;
            throttle = 1.0; warp = 1.0;   // real-time circularization burn

            // G-cap: limit throttle during insertion to avoid sustained >4.5 g loads
            double currentThrust = vessel.ComputeThrust(body).Magnitude;
            double gAccel        = vessel.TotalMass > 0.0 ? currentThrust / vessel.TotalMass : 0.0;
            double gravAccelMag  = vessel.ComputeGravity(universe.Bodies).Magnitude;
            double gForce        = (gAccel - gravAccelMag) / 9.80665;
            if (gForce > 4.0)
            {
                double targetThrottle = vessel.Throttle * (4.0 / gForce);
                throttle = System.Math.Max(0.2, targetThrottle);
            }
        }

        if (warp != universe.TimeScale) universe.TimeScale = warp;
        vessel.Orientation     = ShortestArc(Vector3d.Up, dir);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll    = Vector3d.Zero;
        vessel.Throttle        = throttle;

        AutoStage(vessel, bridge!, body);
        QueueRedraw();
    }

    // ── Assist guidance: command PitchYawRoll toward the gravity-turn heading ─────
    // Same pitch program as the [G] Ascent phase, but we steer with body-rate commands
    // (PitchYawRoll) instead of snapping Orientation, so the curve feels flown rather than
    // teleported — and we yield the moment the player commands attitude.
    // Mismo programa de pitch que la fase Ascent de [G], pero guiando con PitchYawRoll en
    // vez de fijar Orientation: la curva se siente pilotada y cedemos en cuanto el jugador
    // comanda actitud.
    private void ProcessAssist()
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { DisengageAssist(); return; }

        var body = universe.GetDominantBody(vessel.Position);
        Vector3d rel  = vessel.Position - body.Position;
        Vector3d vel  = vessel.Velocity - body.Velocity;
        Vector3d up   = rel.Magnitude > 1e-6 ? rel.Normalized : Vector3d.Up;
        Vector3d east = new Vector3d(0, 1, 0).Cross(up);
        east = east.Magnitude > 1e-6 ? east.Normalized : new Vector3d(0, 0, 1);
        _alt = body.GetAltitude(vessel.Position);

        // Live readout so the banner shows the orbit building under the player's throttle.
        var oe = OrbitalElements.FromStateVector(rel, vel, body.GM, body.Id, universe.CurrentTime);
        _apo = oe.Apoapsis  - body.Radius;
        _per = oe.Periapsis - body.Radius;
        _ecc = oe.Eccentricity;
        _bodyName = body.Name;
        _q = vessel.GetDynamicPressure(body);

        QueueRedraw();

        // If the player is steering, get out of the way this frame — the HUD already wrote
        // their PitchYawRoll, and running later we must NOT clobber it.
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.S) ||
            Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.D) ||
            Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.E))
            return;

        // Desired heading: straight up until the tower is cleared, then pitch over with
        // altitude toward the horizon (keep ~10% vertical so apoapsis keeps climbing).
        // Dirección deseada: vertical hasta despejar la torre, luego cabeceo con la altitud
        // hacia el horizonte (conservar ~10% vertical para que el apoapsis siga subiendo).
        Vector3d desired;
        if (_alt < 200.0)
        {
            desired = up;
        }
        else
        {
            // Same aggressive gravity-turn elevation schedule as the [G] autopilot.
            double elev = GravityTurnElevationDeg(_alt) * System.Math.PI / 180.0;
            desired = (east * System.Math.Cos(elev) + up * System.Math.Sin(elev)).Normalized;
        }

        // Attitude error: rotate the world error axis (nose × desired) into body space and
        // feed it as a proportional body-rate command, damped by the current body rate.
        Vector3d nose = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
        Vector3d errAxisWorld = nose.Cross(desired);
        double sinErr = System.Math.Clamp(errAxisWorld.Magnitude, 0.0, 1.0);
        double angErr = System.Math.Asin(sinErr);                 // radians off-heading
        Vector3d cmd = Vector3d.Zero;
        if (angErr > 0.0008 && errAxisWorld.Magnitude > 1e-9)
        {
            Quaterniond inv = vessel.Orientation.Inverse();
            Vector3d errLocal  = inv.Rotate(errAxisWorld.Normalized) * angErr;
            Vector3d rateLocal = inv.Rotate(vessel.AngularVelocity);
            cmd = errLocal * AssistGain - rateLocal * AssistDamp;
        }

        // Clamp to the [-1,1] per-axis input range the sim expects; never command roll.
        vessel.PitchYawRoll = new Vector3d(
            System.Math.Clamp(cmd.X, -1.0, 1.0),
            System.Math.Clamp(cmd.Y, -1.0, 1.0),
            0.0);
    }

    // Staging logic. For the Super Heavy first stage, HOT-STAGE at MECO — when it reaches
    // staging velocity (with a boostback/landing reserve still in the tanks) — rather than
    // burning it to depletion at apoapsis. Real SH MECO ~2.3-2.4 km/s, ~65 km; the upper stage
    // then flies the orbital insertion. For any other (upper) stage, drop it at near-depletion.
    private void AutoStage(Vessel vessel, SimulationBridge bridge, CelestialBody body)
    {
        Part? sh = null;
        foreach (var p in vessel.Parts.Parts)
            if (p.Definition.Id == "super_heavy_booster") { sh = p; break; }

        if (sh != null)
        {
            if (_mecoStaged) return;   // already separated this flight (booster may still be in debris)
            double surfSpeed = vessel.GetSurfaceVelocity(body).Magnitude;
            double cap  = sh.Definition.FuelCapacityLF + sh.Definition.FuelCapacityOx;
            double left = sh.LiquidFuel + sh.Oxidizer;
            double frac = cap > 0.0 ? left / cap : 0.0;

            bool mecoBySpeed   = surfSpeed >= StagingSpeed && _alt > StagingMinAlt;
            bool mecoByReserve = frac <= BoosterReserveFrac;   // never burn the booster dry
            if (mecoBySpeed || mecoByReserve)
            {
                MissionManager.Instance?.EnterPhase(MissionPhase.MECO);
                bridge.TriggerStaging();   // hot-stage: drop SH; Starship continues the insertion
                _mecoStaged = true;
            }
            return;
        }

        // Upper stage(s): drop once the burning stage is nearly dry and a decoupler remains.
        double stageFuel = 0.0;
        foreach (var p in vessel.Parts.CurrentStageParts()) stageFuel += p.LiquidFuel;
        if (stageFuel > 4_000.0) return;

        bool hasDecoupler = false;
        foreach (var p in vessel.Parts.Parts)
            if (p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive) { hasDecoupler = true; break; }
        if (hasDecoupler) bridge.TriggerStaging();
    }

    // ── HUD ───────────────────────────────────────────────────────────────────
    public override void _Draw()
    {
        if (!_active && !_assist && _phase != Phase.Done) return;
        var vp = GetViewportRect().Size;

        // Assist banner: distinct copy — the player still flies the throttle.
        if (_assist && !_active)
        {
            const string al = "GRAVITY-TURN ASSIST — PITCH GUIDANCE";
            var ac = new Color(0.6f, 0.95f, 0.7f);
            var asz = _font.GetStringSize(al, HorizontalAlignment.Left, -1, 24);
            var apos = new Vector2((vp.X - asz.X) * 0.5f, vp.Y * 0.215f);
            DrawString(_font, apos + new Vector2(2, 2), al, HorizontalAlignment.Left, -1, 24, new Color(0, 0, 0, 0.7f));
            DrawString(_font, apos, al, HorizontalAlignment.Left, -1, 24, ac);

            string asub = $"THROTTLE: YOU ([Z]/[X])   {_bodyName.ToUpperInvariant()}   "
                        + $"Ap {_apo/1000:F0} km   Pe {_per/1000:F0} km   e {_ecc:F3}";
            var assz = _font.GetStringSize(asub, HorizontalAlignment.Left, -1, 14);
            DrawString(_font, new Vector2((vp.X - assz.X) * 0.5f, vp.Y * 0.215f + 26f), asub,
                       HorizontalAlignment.Left, -1, 14, new Color(0.7f, 0.85f, 0.78f));
            return;
        }

        string label = _phase switch
        {
            Phase.Ignition => "IGNITION — ENGINE START",
            Phase.Ascent => "ASCENT — GRAVITY TURN",
            Phase.Coast  => "ASCENT — COAST TO APOAPSIS",
            Phase.Insert => "ASCENT — ORBITAL INSERTION",
            Phase.Done   => "ORBIT ACHIEVED",
            _ => "",
        };
        var col = _phase == Phase.Done ? new Color(0.45f, 1f, 0.6f) : new Color(0.5f, 0.85f, 1f);
        var size = _font.GetStringSize(label, HorizontalAlignment.Left, -1, 24);
        var pos  = new Vector2((vp.X - size.X) * 0.5f, vp.Y * 0.215f);
        DrawString(_font, pos + new Vector2(2, 2), label, HorizontalAlignment.Left, -1, 24, new Color(0, 0, 0, 0.7f));
        DrawString(_font, pos, label, HorizontalAlignment.Left, -1, 24, col);

        string sub = _phase == Phase.Ignition
            ? $"AUTOPILOT · {_bodyName.ToUpperInvariant()}   ENGINE SPOOL-UP   TWR {_twr:F2}   {(_twr > 1.02 ? "▲ LIFTOFF" : "HOLD-DOWN CLAMPED")}"
            : $"AUTOPILOT · {_bodyName.ToUpperInvariant()}   Ap {_apo/1000:F0} km   Pe {_per/1000:F0} km   e {_ecc:F3}"
              + (_phase == Phase.Ascent && _q > 15_000 ? $"   q {_q/1000:F0} kPa" : $"   →  {_holdAlt/1000:F0} km");
        var ssz = _font.GetStringSize(sub, HorizontalAlignment.Left, -1, 14);
        DrawString(_font, new Vector2((vp.X - ssz.X) * 0.5f, vp.Y * 0.215f + 26f), sub,
                   HorizontalAlignment.Left, -1, 14, new Color(0.7f, 0.78f, 0.88f));
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
