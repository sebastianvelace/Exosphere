namespace Exosphere.Simulation;

/// <summary>Pure physical gates and profiles shared by vehicle visual rendering.</summary>
public static class VehicleVisualPhysics
{
    public const double VisibleReentryFluxWm2 = 25_000.0;
    public const double SaturatedReentryFluxWm2 = 300_000.0;
    public const double DescendingThresholdMs = -20.0;

    /// <summary>
    /// Radius of a tangent-ogive nose, parameterised from barrel junction
    /// (<paramref name="u"/> = 0) to tip (<paramref name="u"/> = 1).
    /// </summary>
    public static double TangentOgiveRadius(double u, double baseRadius, double length)
    {
        if (!double.IsFinite(u) || !double.IsFinite(baseRadius) || !double.IsFinite(length)
            || baseRadius <= 0.0 || length <= 0.0)
            return 0.0;
        u = System.Math.Clamp(u, 0.0, 1.0);
        double rho = (baseRadius * baseRadius + length * length) / (2.0 * baseRadius);
        double distanceFromTip = (1.0 - u) * length;
        double value = rho * rho
            - (length - distanceFromTip) * (length - distanceFromTip);
        double radius = System.Math.Sqrt(System.Math.Max(0.0, value)) - (rho - baseRadius);
        return System.Math.Clamp(radius, 0.0, baseRadius);
    }

    public static bool IsVisibleReentryHeating(double radialSpeedMs, double heatFluxWm2) =>
        double.IsFinite(radialSpeedMs) && double.IsFinite(heatFluxWm2)
        && radialSpeedMs < DescendingThresholdMs
        && heatFluxWm2 >= VisibleReentryFluxWm2;

    public static double ReentryGlow(double radialSpeedMs, double heatFluxWm2,
        double hottestSkinK)
    {
        if (!IsVisibleReentryHeating(radialSpeedMs, heatFluxWm2)) return 0.0;
        double flux = System.Math.Clamp(
            (heatFluxWm2 - VisibleReentryFluxWm2)
            / (SaturatedReentryFluxWm2 - VisibleReentryFluxWm2), 0.0, 1.0);
        double thermal = System.Math.Clamp((hottestSkinK - 750.0) / 1_250.0, 0.0, 1.0);
        return System.Math.Max(flux, thermal);
    }
}
