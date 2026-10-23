namespace Exosphere.Simulation.Navigation;

/// <summary>
/// Impulsive periapsis burns for arriving at a body: the change of speed at periapsis that
/// puts the far point of the new orbit at a chosen radius. From a hyperbolic approach that is
/// lunar orbit insertion; from an ellipse it lowers (or raises) the apoapsis, and with the
/// target at the periapsis radius it circularizes.
/// </summary>
public static class CapturePlanner
{
    /// <summary>
    /// Δv (m/s; negative = retrograde) at periapsis to put the apoapsis at
    /// <paramref name="targetApoapsisRadius"/>. <paramref name="semiMajorAxis"/> is negative
    /// for a hyperbola. The target is clamped to at least the periapsis radius.
    /// </summary>
    public static double PeriapsisDeltaV(double mu, double periapsisRadius, double semiMajorAxis,
        double targetApoapsisRadius) =>
        ApsisDeltaV(mu, periapsisRadius, semiMajorAxis, System.Math.Max(periapsisRadius, targetApoapsisRadius));

    /// <summary>
    /// Δv (m/s) at an apsis (or anywhere on a circular orbit) that puts the opposite apsis at
    /// <paramref name="targetOppositeRadius"/>, above or below the burn radius: the first
    /// half of a Hohmann change between circular orbits.
    /// </summary>
    public static double ApsisDeltaV(double mu, double burnRadius, double semiMajorAxis, double targetOppositeRadius)
    {
        if (!(mu > 0.0) || !(burnRadius > 0.0) || !(targetOppositeRadius > 0.0)
            || !double.IsFinite(semiMajorAxis) || semiMajorAxis == 0.0)
            throw new ArgumentOutOfRangeException(nameof(burnRadius));
        double current = System.Math.Sqrt(mu * (2.0 / burnRadius - 1.0 / semiMajorAxis));
        double target = System.Math.Sqrt(mu * (2.0 / burnRadius - 2.0 / (burnRadius + targetOppositeRadius)));
        return target - current;
    }
}
