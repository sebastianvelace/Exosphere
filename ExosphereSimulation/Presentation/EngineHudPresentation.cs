namespace Exosphere.Simulation.Presentation;

using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

/// <summary>
/// Presentation-only interpretation of a live engine telemetry row.
/// <see cref="EngineReadout.Throttle"/> is delivered chamber pressure, not the
/// vessel throttle command. Keeping this rule here prevents the HUD and the exterior
/// renderer from inventing different meanings for the same engine state.
/// </summary>
public enum EngineHudIndicatorState
{
    Off,
    Starting,
    Running,
    Failed,
}

public static class EngineHudPresentation
{
    public const double DeliveredThrottleThreshold = 1e-3;

    public static EngineHudIndicatorState Classify(in EngineReadout readout)
    {
        if (readout.FailureCode != null
            || readout.State == EngineLifecycleState.Failed)
            return EngineHudIndicatorState.Failed;

        if (readout.State is EngineLifecycleState.Chill
            or EngineLifecycleState.SpinPrime
            or EngineLifecycleState.Ignition
            or EngineLifecycleState.Ramp)
            return EngineHudIndicatorState.Starting;

        return IsDelivered(readout)
            ? EngineHudIndicatorState.Running
            : EngineHudIndicatorState.Off;
    }

    public static bool IsDelivered(in EngineReadout readout) =>
        readout.FailureCode == null
        && readout.State != EngineLifecycleState.Failed
        && double.IsFinite(readout.Throttle)
        && readout.Throttle > DeliveredThrottleThreshold;

    public static int CountDelivered(IReadOnlyList<EngineReadout> readouts)
    {
        ArgumentNullException.ThrowIfNull(readouts);

        int count = 0;
        for (int i = 0; i < readouts.Count; i++)
            if (IsDelivered(readouts[i]))
                count++;
        return count;
    }

    public static int CountFailures(IReadOnlyList<EngineReadout> readouts)
    {
        ArgumentNullException.ThrowIfNull(readouts);

        int count = 0;
        for (int i = 0; i < readouts.Count; i++)
        {
            var readout = readouts[i];
            if (readout.FailureCode != null
                || readout.State == EngineLifecycleState.Failed)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Returns the fraction of nominal engine output currently delivered. It is used by
    /// grouped exterior plumes, so a starting or failed engine cannot render at the
    /// commanded vessel throttle. The denominator intentionally remains the full row
    /// count: an engine-out reduces the aggregate visual output rather than disappearing
    /// from the normalization.
    /// </summary>
    public static double DeliveredThrottle(IReadOnlyList<EngineReadout> readouts)
    {
        ArgumentNullException.ThrowIfNull(readouts);
        if (readouts.Count == 0) return 0.0;

        double sum = 0.0;
        for (int i = 0; i < readouts.Count; i++)
        {
            var readout = readouts[i];
            if (IsDelivered(readout))
                sum += System.Math.Clamp(readout.Throttle, 0.0, 1.0);
        }

        return double.IsFinite(sum)
            ? System.Math.Clamp(sum / readouts.Count, 0.0, 1.0)
            : 0.0;
    }
}
