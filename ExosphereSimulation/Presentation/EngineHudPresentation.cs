namespace Exosphere.Simulation.Presentation;

using System.Collections.Generic;
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

    /// <summary>
    /// Super Heavy Flight 7/12 template. The HUD keeps this 20/10/3 ring board when
    /// the live vessel actually has 33 engines; it is not a global maximum.
    /// </summary>
    public const int SuperHeavyTemplateCount = 33;

    /// <summary>
    /// Ring populations for the engine board, outer-first. Custom and VAB stacks
    /// pack into concentric rings so the board can grow or shrink with
    /// <c>NominalEngineCount</c> instead of clamping to 33 dots.
    /// </summary>
    public static void FillBoardRings(int nominalCount, List<int> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        rings.Clear();
        int n = System.Math.Max(0, nominalCount);
        if (n == SuperHeavyTemplateCount)
        {
            rings.Add(20);
            rings.Add(10);
            rings.Add(3);
            return;
        }

        if (n == 0) return;
        if (n <= 8)
        {
            rings.Add(n);
            return;
        }

        int remaining = n;
        int ringIndex = 0;
        var innerFirst = new List<int>(8);
        while (remaining > 0)
        {
            int capacity = ringIndex == 0 ? 1 : ringIndex * 6;
            int take = System.Math.Min(capacity, remaining);
            innerFirst.Add(take);
            remaining -= take;
            ringIndex++;
        }

        for (int i = innerFirst.Count - 1; i >= 0; i--)
            rings.Add(innerFirst[i]);
    }
}
