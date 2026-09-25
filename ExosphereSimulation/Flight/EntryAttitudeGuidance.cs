namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Pure entry-attitude target construction shared by the pre-entry coast handoff and tests.
/// The target is a real orientation reference; it does not mutate a vessel or apply a force.
/// </summary>
public static class EntryAttitudeGuidance
{
    /// <summary>
    /// Builds the Starship belly-first target used before atmospheric interface.  The
    /// longitudinal axis keeps the nominal 70° angle of attack, while the local -X thermal
    /// protection faces the velocity vector. <paramref name="liftTowardBody"/> is an explicit
    /// reversal for a measured overflight; normal entry interface uses lift-up.
    /// </summary>
    public static Quaterniond ComputeTarget(
        Vector3d bodyUp,
        Vector3d velocityDirection,
        bool liftTowardBody)
    {
        var flow = velocityDirection.Normalized;
        if (flow.MagnitudeSquared < 1e-12)
            return Quaterniond.Identity;

        var axis = liftTowardBody
            ? AerodynamicsModel.ComputeLiftDownEntryAxis(bodyUp, flow)
            : AerodynamicsModel.ComputeLiftUpEntryAxis(bodyUp, flow);
        return AerodynamicsModel.ComputeBellyFirstOrientation(axis, flow);
    }
}
