namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
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

    private const double NodeWindow = 0.10;   // rad: how close to node before igniting

    public void Bind(ManeuverPlanner planner) => _planner = planner;

    public void Arm()
    {
        if (_planner is { HasNode: true } && _planner.DeltaVMagnitude > 0.01)
        {
            IsArmed = true;
            IsBurning = false;
            _deliveredDv = 0.0;
            _prevNu = double.NaN;
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
        if (vessel == null || universe == null) { Disarm(); return; }

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
        var (pro, nrm, rad) = _planner.BurnBasisAtNode();
        Vector3d dir = pro * _planner.DvPrograde + nrm * _planner.DvNormal + rad * _planner.DvRadial;
        if (dir.Magnitude < 1e-6) { EndBurn(); IsArmed = false; return; }
        dir = dir.Normalized;

        // Snap orientation so the engine (+Y local) points along the burn direction.
        vessel.Orientation     = ShortestArc(Vector3d.Up, dir);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll    = Vector3d.Zero;
        vessel.Throttle        = 1.0;

        // Accumulate delivered ΔV from the actual thrust this frame. The vessel
        // integrates over simulation time (delta · TimeScale), so the ΔV book-keeping
        // must use the same scaled step — otherwise the burn over-runs under time warp.
        double mass = vessel.TotalMass;
        if (mass > 0.0)
        {
            double simStep = delta * universe.TimeScale;
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
        _restoreSas = vessel.SASEnabled;
        vessel.SASEnabled = false;   // we command orientation directly
    }

    private void FinishBurn(Vessel vessel)
    {
        vessel.Throttle = 0.0;
        EndBurn();
        IsArmed = false;
        _planner.ClearNode();
    }

    private void EndBurn(Vessel? vessel = null)
    {
        var v = vessel ?? SimulationBridge.Instance?.ActiveVessel;
        if (v != null)
        {
            v.Throttle    = 0.0;
            v.SASEnabled  = _restoreSas;
        }
        IsBurning = false;
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
