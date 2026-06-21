namespace Exosphere.Simulation.Navigation;

public sealed record HohmannTransferPlan(
    double InitialRadius,
    double TargetRadius,
    double TransferSemiMajorAxis,
    double FirstBurnDeltaV,
    double SecondBurnDeltaV,
    double TimeOfFlight,
    double RequiredPhaseAngle);

public static class HohmannTransfer
{
    public static HohmannTransferPlan Compute(double centralGm, double initialRadius, double targetRadius)
    {
        if (centralGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(centralGm), "Central body GM must be positive.");
        if (initialRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialRadius), "Initial radius must be positive.");
        if (targetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(targetRadius), "Target radius must be positive.");
        if (System.Math.Abs(initialRadius - targetRadius) / initialRadius < 1e-6)
            throw new ArgumentException("Initial and target radii are too similar for a useful Hohmann transfer.");

        double sma = (initialRadius + targetRadius) * 0.5;
        double v1Circular = System.Math.Sqrt(centralGm / initialRadius);
        double v2Circular = System.Math.Sqrt(centralGm / targetRadius);
        double vTransfer1 = System.Math.Sqrt(centralGm * (2.0 / initialRadius - 1.0 / sma));
        double vTransfer2 = System.Math.Sqrt(centralGm * (2.0 / targetRadius - 1.0 / sma));
        double tof = System.Math.PI * System.Math.Sqrt(sma * sma * sma / centralGm);

        // Required target lead angle at departure for a coplanar circular target orbit:
        // target_angle - vessel_angle = pi - n_target * tof, wrapped to [0, 2pi).
        double targetMeanMotion = System.Math.Sqrt(centralGm / (targetRadius * targetRadius * targetRadius));
        double phase = WrapTwoPi(System.Math.PI - targetMeanMotion * tof);

        return new HohmannTransferPlan(
            initialRadius,
            targetRadius,
            sma,
            vTransfer1 - v1Circular,
            v2Circular - vTransfer2,
            tof,
            phase);
    }

    private static double WrapTwoPi(double x)
    {
        x %= 2.0 * System.Math.PI;
        if (x < 0.0) x += 2.0 * System.Math.PI;
        return x;
    }
}
