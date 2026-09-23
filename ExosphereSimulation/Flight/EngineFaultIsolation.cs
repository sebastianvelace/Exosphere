namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Propulsion;

/// <summary>
/// Infers an engine performance fault from redundant propulsion telemetry.
/// This is intentionally separate from failure injection: a flight computer sees
/// delivered thrust and throttle response, not the simulator's failure code.
/// </summary>
public static class EngineFaultIsolation
{
    public const double DefaultMinimumCommandedThrottle = 0.35;
    public const double DefaultMaximumHealthyDeficit = 0.35;

    public static EngineFaultIsolationResult Infer(
        IReadOnlyList<EngineTelemetry> telemetry,
        double minimumCommandedThrottle = DefaultMinimumCommandedThrottle,
        double maximumHealthyDeficit = DefaultMaximumHealthyDeficit)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!double.IsFinite(minimumCommandedThrottle)
            || minimumCommandedThrottle <= 0.0
            || minimumCommandedThrottle > 1.0)
            throw new ArgumentOutOfRangeException(nameof(minimumCommandedThrottle));
        if (!double.IsFinite(maximumHealthyDeficit)
            || maximumHealthyDeficit <= 0.0
            || maximumHealthyDeficit >= 1.0)
            throw new ArgumentOutOfRangeException(nameof(maximumHealthyDeficit));

        var commanded = telemetry
            .Where(row => row.CommandedThrottle >= minimumCommandedThrottle)
            .ToArray();
        if (commanded.Length < 2)
            return EngineFaultIsolationResult.None;

        // Normalize thrust by actual throttle. The median rejects one weak engine
        // and remains stable while the rest of the cluster is still ramping.
        var healthyRates = commanded
            .Where(row => row.ActualThrottle > 0.25 && row.ThrustN > 0.0)
            .Select(row => row.ThrustN / row.ActualThrottle)
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToArray();
        if (healthyRates.Length < 2)
            return EngineFaultIsolationResult.None;

        double referenceRate = Median(healthyRates);
        if (referenceRate <= 0.0)
            return EngineFaultIsolationResult.None;

        var suspects = new List<string>();
        double expectedTotal = 0.0;
        double deliveredTotal = 0.0;
        double strongestDeficit = 0.0;
        foreach (var row in commanded)
        {
            double responseThrottle = System.Math.Max(row.ActualThrottle, row.CommandedThrottle * 0.5);
            double expected = referenceRate * responseThrottle;
            double delivered = System.Math.Max(0.0, row.ThrustN);
            expectedTotal += expected;
            deliveredTotal += delivered;
            if (expected <= 1e-9) continue;

            double deficit = 1.0 - delivered / expected;
            if (deficit > maximumHealthyDeficit)
            {
                suspects.Add(row.InstanceId);
                strongestDeficit = System.Math.Max(strongestDeficit, System.Math.Min(1.0, deficit));
            }
        }

        if (suspects.Count == 0)
            return new EngineFaultIsolationResult(
                false, 0.0, referenceRate, expectedTotal, deliveredTotal, suspects);

        double coverage = suspects.Count / (double)commanded.Length;
        double confidence = System.Math.Clamp(
            strongestDeficit * 0.85 + (1.0 - coverage) * 0.15, 0.0, 1.0);
        return new EngineFaultIsolationResult(
            true, confidence, referenceRate, expectedTotal, deliveredTotal, suspects);
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) * 0.5
            : sorted[middle];
    }
}

public readonly record struct EngineFaultIsolationResult(
    bool FaultDetected,
    double Confidence,
    double ReferenceThrustPerThrottleN,
    double ExpectedThrustN,
    double DeliveredThrustN,
    IReadOnlyList<string> SuspectEngineIds)
{
    public static EngineFaultIsolationResult None => new(
        false, 0.0, 0.0, 0.0, 0.0, Array.Empty<string>());
}
