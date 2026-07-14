namespace Exosphere.Simulation.Navigation;

/// <summary>
/// Pure deorbit-burn planner. Treats the burn radius as the <em>apoapsis</em> of the
/// resulting ellipse (the circular-LEO / burn-at-current-radius assumption used by
/// map presets). Returns the positive magnitude of the required retrograde Δv.
/// </summary>
public static class DeorbitPlanner
{
    /// <summary>Hard floor: periapsis must stay at least this far above the surface.</summary>
    public const double SafetyFloorAboveSurfaceM = 20_000.0;

    /// <summary>
    /// Impulsive retro Δv (m/s, positive magnitude) that lowers periapsis of an ellipse
    /// whose apoapsis is at <paramref name="radiusNow"/>.
    /// <para>
    /// Pre-burn speed is assumed circular: <c>v = √(μ/r)</c>. Post-burn speed is
    /// vis-viva at apoapsis of the (r, rₚ) ellipse. Valid for near-circular LEO; for
    /// eccentric orbits the caller should pass the burn-site radius (typically apoapsis).
    /// </para>
    /// Target periapsis is clamped into
    /// [<paramref name="bodyRadius"/> + 20 km, <paramref name="bodyRadius"/> + atmo max]
    /// when those bounds are supplied.
    /// </summary>
    public static double ComputeRetroDeltaV(
        double mu,
        double radiusNow,
        double targetPeriapsisRadius,
        double bodyRadius = 0.0,
        double atmosphereMaxAltitude = double.NaN)
    {
        if (mu <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(mu), "Gravitational parameter must be positive.");
        if (radiusNow <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(radiusNow), "Burn radius must be positive.");

        double rp = ClampTargetPeriapsisRadius(
            targetPeriapsisRadius, bodyRadius, atmosphereMaxAltitude);

        // Need a lower periapsis than the burn (apo) radius.
        if (rp >= radiusNow * (1.0 - 1e-9))
            return 0.0;

        double a = 0.5 * (radiusNow + rp);
        if (a <= 0.0)
            return 0.0;

        double vCircular = System.Math.Sqrt(mu / radiusNow);
        double vApoapsis = System.Math.Sqrt(mu * (2.0 / radiusNow - 1.0 / a));
        double dv = vCircular - vApoapsis;
        return dv > 0.0 ? dv : 0.0;
    }

    /// <summary>
    /// Clamps a periapsis radius into the safe deorbit band:
    /// above body radius + 20 km, and (when known) below the atmosphere top.
    /// </summary>
    public static double ClampTargetPeriapsisRadius(
        double targetPeriapsisRadius,
        double bodyRadius,
        double atmosphereMaxAltitude = double.NaN)
    {
        double rp = targetPeriapsisRadius;
        if (bodyRadius > 0.0)
        {
            double floor = bodyRadius + SafetyFloorAboveSurfaceM;
            if (rp < floor) rp = floor;

            if (!double.IsNaN(atmosphereMaxAltitude) && atmosphereMaxAltitude > SafetyFloorAboveSurfaceM)
            {
                double ceiling = bodyRadius + atmosphereMaxAltitude;
                // Keep the ceiling strictly above the floor so a thin atmosphere still deorbits.
                if (ceiling > floor && rp > ceiling) rp = ceiling;
            }
        }

        return rp;
    }
}
