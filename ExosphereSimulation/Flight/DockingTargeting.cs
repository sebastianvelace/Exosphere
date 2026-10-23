namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>Relative state of the nearest docking target, as a pilot reads it.</summary>
/// <param name="RangeM">Port-to-port distance (m).</param>
/// <param name="ClosingSpeedMps">Rate the range shrinks (m/s); negative while opening.</param>
/// <param name="LateralSpeedMps">Relative speed across the line of sight (m/s).</param>
/// <param name="AlignmentErrorDeg">Angle between the two port axes and nose-to-nose (deg).</param>
/// <param name="RelativeVelocity">
/// Chaser port velocity relative to the target port in the target's orbit frame (m/s, world
/// axes): two craft on the same circular orbit read zero, as the pilot sees it, even though
/// their inertial velocities differ by v·θ.
/// </param>
/// <param name="LineOfSight">Unit vector from the chaser port to the target port (world).</param>
/// <param name="CrossTrackVelocityBody">
/// Relative velocity across the line of sight in the chaser's body axes (X right, Z up), the
/// axes its translation keys fire along: the drift to null.
/// </param>
public readonly record struct DockingTarget(
    Vessel Target,
    Part ChaserPort,
    Part TargetPort,
    double RangeM,
    double ClosingSpeedMps,
    double LateralSpeedMps,
    double AlignmentErrorDeg,
    Vector3d RelativeVelocity,
    Vector3d LineOfSight,
    Vector3d CrossTrackVelocityBody);

/// <summary>
/// Hand-flown proximity operations: picks the nearest vessel with a free docking port and
/// lets a pilot dock by flying into the port's real capture envelope, the same
/// <see cref="Universe.TryDock"/> the automated approaches use.
/// </summary>
public static class DockingTargeting
{
    public const double DefaultSearchRangeM = 1_000_000.0;

    public static DockingTarget? FindNearest(Universe universe, Vessel chaser, double maximumRangeM = DefaultSearchRangeM)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(chaser);
        var chaserPort = FreePorts(universe, chaser).FirstOrDefault();
        if (chaserPort == null
            || !Universe.TryGetDockingFrame(chaser, chaserPort, out var chaserPosition, out var chaserAxis,
                out var chaserVelocity))
            return null;

        DockingTarget? best = null;
        foreach (var target in universe.Vessels)
        {
            if (target == chaser || target.IsDestroyed) continue;
            foreach (var port in FreePorts(universe, target))
            {
                if (!Universe.TryGetDockingFrame(target, port, out var targetPosition, out var targetAxis,
                        out var targetVelocity))
                    continue;
                var offset = targetPosition - chaserPosition;
                double range = offset.Magnitude;
                if (range > maximumRangeM || best is { } current && current.RangeM <= range) continue;
                var lineOfSight = range > 1e-9 ? offset / range : chaserAxis;
                var relative = chaserVelocity - targetVelocity - OrbitRate(universe, target).Cross(-offset);
                double closing = relative.Dot(lineOfSight);
                double alignment = System.Math.Acos(System.Math.Clamp(chaserAxis.Dot(-targetAxis), -1.0, 1.0))
                    * MathUtils.RAD_TO_DEG;
                var crossTrack = relative - lineOfSight * closing;
                best = new DockingTarget(target, chaserPort, port, range, closing,
                    crossTrack.Magnitude, alignment, relative, lineOfSight,
                    chaser.Orientation.Inverse().Rotate(crossTrack));
            }
        }
        return best;
    }

    /// <summary>Docks when the nearest ports sit inside their capture envelope.</summary>
    public static DockingAttempt? TryCapture(Universe universe, Vessel chaser)
    {
        if (FindNearest(universe, chaser, 5.0) is not { } target) return null;
        double captureRange = System.Math.Min(target.ChaserPort.Definition.DockingCaptureRangeM,
            target.TargetPort.Definition.DockingCaptureRangeM);
        if (target.RangeM > captureRange) return null;
        return universe.TryDock(chaser.Id, target.ChaserPort.InstanceId, target.Target.Id,
            target.TargetPort.InstanceId, $"{chaser.Id}-{target.Target.Id}-docking");
    }

    /// <summary>Angular velocity of the target's orbit frame (r × v / r²) about its body.</summary>
    private static Vector3d OrbitRate(Universe universe, Vessel target)
    {
        var body = universe.GetDominantBody(target.Position);
        var r = target.Position - body.Position;
        double r2 = r.MagnitudeSquared;
        return r2 > 1e-6 ? r.Cross(target.Velocity - body.Velocity) / r2 : Vector3d.Zero;
    }

    private static IEnumerable<Part> FreePorts(Universe universe, Vessel vessel) =>
        vessel.Parts.Parts.Where(part => part.Definition.IsDockingPort
            && universe.DockingConnections.All(connection =>
                connection.PrimaryPortPartId != part.InstanceId && connection.SecondaryPortPartId != part.InstanceId));
}
