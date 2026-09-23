namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

public readonly record struct EngineFaultIsolationGeometry(
    string InstanceId,
    Vector3d PositionM,
    Vector3d ThrustDirection);

/// <summary>
/// Infers an engine performance fault from redundant propulsion telemetry.
/// This is intentionally separate from failure injection: a flight computer sees
/// delivered thrust and throttle response, not the simulator's failure code.
/// </summary>
public static class EngineFaultIsolation
{
    public const double DefaultMinimumCommandedThrottle = 0.35;
    public const double DefaultMaximumHealthyDeficit = 0.35;
    // A single lost Raptor in a 33-engine cluster is a small fraction of the total
    // moment budget; 0.1% still rejects a healthy peer cluster while retaining that
    // physically meaningful asymmetric signature.
    public const double DefaultMinimumTorqueResidualRatio = 0.001;

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

    /// <summary>
    /// Infers the same peer-thrust fault while also comparing the torque that the healthy
    /// peer model predicts with the torque delivered by the live thrust vectors. The
    /// residual is calculated from mount geometry, not from the simulator failure flag.
    /// </summary>
    public static EngineFaultIsolationResult Infer(
        IReadOnlyList<EngineTelemetry> telemetry,
        IReadOnlyList<EngineFaultIsolationGeometry> geometry,
        Vector3d centerOfMass,
        double minimumCommandedThrottle = DefaultMinimumCommandedThrottle,
        double maximumHealthyDeficit = DefaultMaximumHealthyDeficit)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var result = Infer(
            telemetry,
            minimumCommandedThrottle,
            maximumHealthyDeficit);
        if (result.ReferenceThrustPerThrottleN <= 0.0 || geometry.Count == 0)
            return result;

        var byId = geometry.ToDictionary(row => row.InstanceId, StringComparer.Ordinal);
        var expectedTorque = Vector3d.Zero;
        var deliveredTorque = Vector3d.Zero;
        double characteristicMoment = 0.0;
        foreach (var row in telemetry)
        {
            if (row.CommandedThrottle < minimumCommandedThrottle
                || !byId.TryGetValue(row.InstanceId, out var mount))
                continue;

            double responseThrottle = System.Math.Max(
                row.ActualThrottle,
                row.CommandedThrottle * 0.5);
            double expectedThrust = result.ReferenceThrustPerThrottleN * responseThrottle;
            double deliveredThrust = System.Math.Max(0.0, row.ThrustN);
            var leverArm = mount.PositionM - centerOfMass;
            var expectedForce = mount.ThrustDirection * expectedThrust;
            var deliveredForce = mount.ThrustDirection * deliveredThrust;
            expectedTorque += leverArm.Cross(expectedForce);
            deliveredTorque += leverArm.Cross(deliveredForce);
            characteristicMoment += expectedThrust * leverArm.Magnitude;
        }

        var residual = expectedTorque - deliveredTorque;
        double residualRatio = residual.Magnitude / System.Math.Max(characteristicMoment, 1.0);
        return result with
        {
            ExpectedTorqueNm = expectedTorque,
            DeliveredTorqueNm = deliveredTorque,
            TorqueResidualNm = residual,
            TorqueResidualRatio = residualRatio,
        };
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
    IReadOnlyList<string> SuspectEngineIds,
    Vector3d ExpectedTorqueNm = default,
    Vector3d DeliveredTorqueNm = default,
    Vector3d TorqueResidualNm = default,
    double TorqueResidualRatio = 0.0)
{
    public static EngineFaultIsolationResult None => new(
        false, 0.0, 0.0, 0.0, 0.0, Array.Empty<string>());
}
