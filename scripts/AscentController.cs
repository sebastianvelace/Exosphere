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
/// </summary>
public partial class AscentController : Control
{
    public static AscentController? Instance { get; private set; }

    private enum Phase { Idle, Ignition, Ascent, Coast, Insert, Done }
    private Phase _phase = Phase.Idle;
    private bool  _active;

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
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false, Keycode: Key.G })
        {
            if (_active) Disengage();
            else         Engage();
            GetViewport().SetInputAsHandled();
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
        // Park just above the atmosphere (so the orbit doesn't decay), with a sane floor.
        _holdAlt   = System.Math.Max(atmoTop + 10_000.0, 80_000.0);
        _apoTarget = _holdAlt + 2_000.0;
        _peTarget  = _holdAlt - 7_000.0;
        _bodyName  = body.Name;

        vessel.SASEnabled = false;
        _active = true;
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
        Visible = false;
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveVessel is { } v) v.Throttle = 0.0;
        if (bridge?.Universe is { } u && u.TimeScale > 1.0) u.TimeScale = 1.0;
    }

    public override void _Process(double delta)
    {
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
            // Gravity turn: pitch over with altitude, keeping ~10% vertical so apoapsis
            // keeps climbing toward target even with a low-TWR upper stage.
            double f = System.Math.Clamp((_alt - 2_000.0) / 90_000.0, 0.0, 0.90);
            dir = (up * (1.0 - f) + east * f).Normalized;
            // Ease the throttle down through peak dynamic pressure to limit aero loads, then
            // back to full once past Max-Q — the real "throttling down … throttle up" profile.
            _q = vessel.GetDynamicPressure(body);
            throttle = System.Math.Clamp(1.0 - (_q - 22_000.0) / 18_000.0 * 0.4, 0.62, 1.0);
            warp = 2.0;
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
            double thrustVac = 0.0;
            foreach (var e in vessel.Parts.ActiveEngines) thrustVac += e.Definition.ThrustVac;
            double aThrust = thrustVac / System.Math.Max(vessel.TotalMass, 1.0);
            double sinFF  = (gLocal - centrifugal) / System.Math.Max(aThrust, 0.1);
            double altCorr = 0.00002 * (_holdAlt - _alt) - 0.002 * vUp;
            double sinCmd = System.Math.Clamp(sinFF + altCorr, -0.3, 0.95);
            double cosCmd = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - sinCmd * sinCmd));
            dir = (tang * cosCmd + up * sinCmd).Normalized;
            throttle = 1.0; warp = 4.0;
        }

        if (warp != universe.TimeScale) universe.TimeScale = warp;
        vessel.Orientation     = ShortestArc(Vector3d.Up, dir);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll    = Vector3d.Zero;
        vessel.Throttle        = throttle;

        AutoStage(vessel, bridge!);
        QueueRedraw();
    }

    // Drop a spent stage once the burning stage is nearly dry and a decoupler remains.
    private static void AutoStage(Vessel vessel, SimulationBridge bridge)
    {
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
        if (!_active && _phase != Phase.Done) return;
        var vp = GetViewportRect().Size;

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
