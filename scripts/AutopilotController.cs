namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

/// <summary>
/// Executes a planned maneuver. When armed it waits until the active vessel reaches
/// the node's true anomaly, then orients the vessel to the (live) burn direction and
/// throttles up until the planned ΔV magnitude has been delivered.
/// </summary>
public partial class AutopilotController : Node
{
    private ManeuverPlanner _planner = null!;

    public bool IsArmed  { get; private set; }
    public bool IsBurning{ get; private set; }

    private double _deliveredDv;          // m/s accumulated during the burn
    private double _targetDv;             // m/s snapshot at ignition
    private double _prevNu;               // for node-crossing detection
    private bool   _restoreSas;
    private bool   _burnCommandCommitted;
    private Vector3d _burnDirectionWorld = Vector3d.Zero;

    private const double NodeWindow = 0.10;   // rad: how close to node before igniting
    // A deorbit burn commonly starts 180° away from the current thrust axis. The generic
    // guidance defaults leave that exact retrograde turn under-damped and can keep the
    // autopilot in the throttle-inhibited alignment loop. Use a deliberately damped loop
    // for maneuver execution only; EDL/ascent retain their own tuned callers.
    private const double BurnProportionalGain = 2.0;
    private const double BurnDampingGain = 25.0;

    public void Bind(ManeuverPlanner planner) => _planner = planner;

    public void Arm()
    {
        if (_planner is { HasNode: true } && _planner.DeltaVMagnitude > 0.01)
        {
            IsArmed = true;
            IsBurning = false;
            _deliveredDv = 0.0;
            _prevNu = double.NaN;
            _burnCommandCommitted = false;

            // Deorbit-ish retro burn: leave ORBIT into COAST while waiting for ignition.
            if (_planner.DvPrograde < -50.0
                && MissionManager.Instance?.Phase == MissionPhase.ORBIT)
            {
                MissionManager.Instance.EnterPhase(MissionPhase.COAST);
            }
        }
    }

    public void Disarm()
    {
        if (IsBurning) EndBurn();
        IsArmed = false;
        IsBurning = false;
    }

    public override void _Process(double delta)
    {
        if (!IsArmed) return;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || vessel == null || universe == null) { Disarm(); return; }

        var refBody = universe.GetDominantBody(vessel.Position);
        var relPos  = vessel.Position - refBody.Position;
        var relVel  = vessel.Velocity - refBody.Velocity;

        // Re-capture the live orbit so the burn basis stays correct as we thrust.
        _planner.SetOrbit(relPos, relVel, refBody.GM);
        if (!_planner.HasOrbit) return;

        double nu = _planner.TrueAnomalyNow;

        if (!IsBurning)
        {
            // Detect arrival at (or crossing of) the node true anomaly.
            double diff = AngleDiff(nu, _planner.NodeTrueAnomaly);
            bool crossed = !double.IsNaN(_prevNu) &&
                           System.Math.Sign(AngleDiff(_prevNu, _planner.NodeTrueAnomaly)) !=
                           System.Math.Sign(diff);
            _prevNu = nu;

            if (System.Math.Abs(diff) <= NodeWindow || crossed)
                BeginBurn(vessel);
            else
                return;
        }

        // ── Burning ──────────────────────────────────────────────────────────
        // The burn basis is impulsive: once ignition begins, keep the direction computed
        // at the node. Rebuilding it from the changing orbit every frame can rotate the
        // target as the burn itself changes eccentricity, turning a retrograde deorbit into
        // a prograde/normal excursion on larger burns.
        Vector3d dir = _burnDirectionWorld;
        if (dir.Magnitude < 1e-6) { EndBurn(); IsArmed = false; return; }

        // Slew through bounded torque and wait for the physical attitude to settle before
        // ignition. The engine remains pointed by its actual integrated quaternion throughout.
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, dir, vessel.AngularVelocity,
            BurnProportionalGain, BurnDampingGain);
        double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(dir);
        if (!_burnCommandCommitted
            && (alignment < 0.998 || vessel.AngularVelocity.Magnitude > 0.03))
        {
            vessel.Throttle = 0.0;
            return;
        }
        // Once the first ignition command is accepted, do not cut throttle and let the
        // engine lifecycle re-enter Chill/SpinPrime on every small attitude oscillation.
        // A burn is a single committed event; guidance continues correcting attitude while
        // the planned ΔV is delivered, and the engine failure policy remains authoritative.
        _burnCommandCommitted = true;
        vessel.Throttle = 1.0;

        // Accumulate delivered ΔV from the actual thrust in the last committed physics
        // interval. Using render delta · TimeScale would overrun a burn when scheduler
        // debt is capped.
        double mass = vessel.TotalMass;
        if (mass > 0.0)
        {
            double simStep = bridge.LastProcessedSimulationSeconds;
            double thrust  = vessel.ComputeThrust(refBody).Magnitude;
            _deliveredDv += thrust / mass * simStep;
        }

        if (_deliveredDv >= _targetDv)
            FinishBurn(vessel);
    }

    private void BeginBurn(Vessel vessel)
    {
        IsBurning   = true;
        _targetDv   = _planner.DeltaVMagnitude;
        _deliveredDv = 0.0;
        _burnDirectionWorld = _planner.DeltaVInertial().Normalized;
        _restoreSas = vessel.SASEnabled;
        vessel.SASEnabled = false;

        // Orbital deorbit burn (map preset / large retro Δv): expose RETRO_BURN on the mission track.
        if (_planner.DvPrograde < -50.0)
            MissionManager.Instance?.EnterPhase(MissionPhase.RETRO_BURN);
    }

    private void FinishBurn(Vessel vessel)
    {
        bool wasDeorbit = _planner.DvPrograde < -50.0;
        vessel.Throttle = 0.0;
        EndBurn();
        IsArmed = false;
        _planner.ClearNode();

        // After the deorbit burn, coast toward entry interface so EDL can arm ENTRY
        // (RETRO_BURN alone is also used later for the landing flip and is InDescent).
        if (wasDeorbit
            && MissionManager.Instance?.Phase == MissionPhase.RETRO_BURN)
        {
            MissionManager.Instance.EnterPhase(MissionPhase.COAST);
        }
    }

    private void EndBurn(Vessel? vessel = null)
    {
        var v = vessel ?? SimulationBridge.Instance?.ActiveVessel;
        if (v != null)
        {
            v.Throttle    = 0.0;
            v.PitchYawRoll = Vector3d.Zero;
            v.SASEnabled  = _restoreSas;
        }
        IsBurning = false;
        _burnCommandCommitted = false;
        _burnDirectionWorld = Vector3d.Zero;
    }

    // ── Math helpers ──────────────────────────────────────────────────────────

    // Signed smallest angular difference a → b, in [-π, π].
    private static double AngleDiff(double a, double b)
    {
        double d = b - a;
        while (d >  System.Math.PI) d -= 2.0 * System.Math.PI;
        while (d < -System.Math.PI) d += 2.0 * System.Math.PI;
        return d;
    }

    // Shortest-arc quaternion rotating `from` onto `to` (both assumed unit-ish).
    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized;
        var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot >  0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            // 180°: rotate about any axis perpendicular to `from`.
            Vector3d axis = System.Math.Abs(f.X) < 0.9 ? f.Cross(Vector3d.Right) : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(axis.Normalized, System.Math.PI);
        }
        Vector3d a = f.Cross(t).Normalized;
        double angle = System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0));
        return Quaterniond.FromAxisAngle(a, angle);
    }
}
