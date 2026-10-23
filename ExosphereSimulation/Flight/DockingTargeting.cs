namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>Relative state of the nearest docking target, as a pilot reads it.</summary>
/// <param name="RangeM">Port-to-port distance (m).</param>
/// <param name="ClosingSpeedMps">Rate the range shrinks (m/s); negative while opening.</param>
/// <param name="LateralSpeedMps">Relative speed across the line of sight (m/s).</param>
/// <param name="AlignmentErrorDeg">Angle between the two port axes and nose-to-nose (deg).</param>
/// <param name="RelativeVelocity">Chaser port velocity minus target port velocity (m/s, world).</param>
/// <param name="LineOfSight">Unit vector from the chaser port to the target port (world).</param>
public readonly record struct DockingTarget(
    Vessel Target,
    Part ChaserPort,
    Part TargetPort,
    double RangeM,
    double ClosingSpeedMps,
    double LateralSpeedMps,
    double AlignmentErrorDeg,
    Vector3d RelativeVelocity,
    Vector3d LineOfSight);

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
                var relative = chaserVelocity - targetVelocity;
                double closing = relative.Dot(lineOfSight);
                double alignment = System.Math.Acos(System.Math.Clamp(chaserAxis.Dot(-targetAxis), -1.0, 1.0))
                    * MathUtils.RAD_TO_DEG;
                best = new DockingTarget(target, chaserPort, port, range, closing,
                    (relative - lineOfSight * closing).Magnitude, alignment, relative, lineOfSight);
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

    private static IEnumerable<Part> FreePorts(Universe universe, Vessel vessel) =>
        vessel.Parts.Parts.Where(part => part.Definition.IsDockingPort
            && universe.DockingConnections.All(connection =>
                connection.PrimaryPortPartId != part.InstanceId && connection.SecondaryPortPartId != part.InstanceId));
}
