namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Filtered attitude reference for the belly flop. The vehicle's aerodynamic reference cannot
/// jump with one noisy guidance sample. This is a reference filter only: the vessel still
/// follows it through physical flap and torque authority. The game's EDL director and the
/// glide-and-burn bench share it.
/// </summary>
public sealed class BellyFlopAttitudeReference
{
    public const double TimeConstantSeconds = 0.75;
    public const double SlewRateRadPerSecond = 6.0 * MathUtils.DEG_TO_RAD;
    /// <summary>Longest step the filter integrates at once (s).</summary>
    public const double MaximumStepSeconds = 0.20;

    private Vector3d _axis;
    private Vector3d _flow;
    private Quaterniond _attitude;

    public bool IsInitialized { get; private set; }
    public Vector3d Flow => _flow;
    public Quaterniond Attitude => _attitude;

    /// <summary>Angle of attack of the filtered reference (deg), or zero before it starts.</summary>
    public double AngleOfAttackDegrees
    {
        get
        {
            if (!IsInitialized || _flow.MagnitudeSquared < 1e-12) return 0.0;
            var axis = _attitude.Rotate(Vector3d.Up).Normalized;
            return System.Math.Acos(System.Math.Clamp(axis.Dot(_flow.Normalized), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
        }
    }

    public void Reset() => IsInitialized = false;

    /// <summary>Advances the reference toward <paramref name="aimAxis"/> and returns it.</summary>
    public Quaterniond Update(Quaterniond vehicleOrientation, Vector3d aimAxis, Vector3d flowDirection, double delta)
    {
        double filterDelta = System.Math.Clamp(delta, 0.0, MaximumStepSeconds);
        if (!IsInitialized)
        {
            // Seed from the physical vehicle state. The orbital return has already prepared a
            // belly-first attitude during coast; seeding from aimAxis here would introduce an
            // artificial 90–180° roll step when corridor guidance selects a different lift side
            // on the first atmospheric frame.
            _axis = vehicleOrientation.Rotate(Vector3d.Up).Normalized;
            _flow = flowDirection.Normalized;
            _attitude = vehicleOrientation;
            IsInitialized = true;
        }
        else
        {
            _axis = AttitudeGuidance.SmoothDirection(_axis, aimAxis, filterDelta, TimeConstantSeconds);
            _flow = AttitudeGuidance.SmoothDirection(_flow, flowDirection, filterDelta, TimeConstantSeconds);
        }

        // Independent reference filters can change the mutual angle between the axis and flow.
        // Rebuild the axis from its filtered lift side so alpha remains the nominal physical
        // entry target instead of collapsing into a nose-first dive.
        var constrainedAxis = AerodynamicsModel.ConstrainEntryAxisToAngle(_flow, _axis);
        var target = AerodynamicsModel.ComputeBellyFirstOrientation(constrainedAxis, _flow);
        var slewed = AttitudeGuidance.SlewQuaternion(_attitude, target, filterDelta, SlewRateRadPerSecond);
        // Slerp smooths roll and lift-side changes, but its intermediate axis can temporarily
        // leave the nominal entry cone. Re-project that axis before the command is published;
        // otherwise a corridor correction silently turns the filtered reference into a
        // nose-first dive even while attitude error is small.
        _attitude = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(slewed, _flow);
        return _attitude;
    }
}
