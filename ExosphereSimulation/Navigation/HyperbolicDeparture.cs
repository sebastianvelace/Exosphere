namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>
/// The departure hyperbola through a given position with a given outgoing asymptote: the
/// velocity a vessel needs at <c>r</c> to leave along <c>v∞</c> (direction and speed). It lies
/// in the plane of r̂ and the asymptote direction ŝ, turning the same way as the vessel's orbit.
/// With θ the angle from r̂ to ŝ in the direction of motion, the true anomaly at r is
/// ν = ν∞ − θ (cos ν∞ = −1/e) and p = (μ/v∞²)(e² − 1), so r = p / (1 + e cos ν) fixes e; then
/// v = √(μ/p) [e sin ν r̂ + (1 + e cos ν) t̂].
/// </summary>
public static class HyperbolicDeparture
{
    /// <param name="gm">Gravitational parameter of the body departed (m³/s²).</param>
    /// <param name="relativePosition">Position relative to that body (m).</param>
    /// <param name="vInfinity">Required outgoing asymptotic velocity (m/s).</param>
    /// <param name="orbitNormal">Angular-momentum direction of the current orbit: sets the turn sense.</param>
    /// <returns>The required velocity relative to the body, or null when no outgoing branch
    /// through the position leaves along that asymptote (it points back at or behind the body).</returns>
    public static Vector3d? RequiredVelocity(double gm, Vector3d relativePosition, Vector3d vInfinity,
        Vector3d orbitNormal)
    {
        if (!(gm > 0.0)) throw new ArgumentOutOfRangeException(nameof(gm));
        double r = relativePosition.Magnitude, vInf = vInfinity.Magnitude;
        if (!(r > 0.0) || !(vInf > 0.0) || orbitNormal.MagnitudeSquared < 1e-24) return null;
        var radial = relativePosition / r;
        var asymptote = vInfinity / vInf;

        var normal = radial.Cross(asymptote);
        if (normal.MagnitudeSquared < 1e-20) return null; // Asymptote along the radius: radial only.
        normal = normal.Normalized;
        double theta = System.Math.Acos(System.Math.Clamp(radial.Dot(asymptote), -1.0, 1.0));
        if (normal.Dot(orbitNormal) < 0.0)
        {
            // Keep the orbit's turn sense: the asymptote is reached the long way round.
            normal = -normal;
            theta = 2.0 * System.Math.PI - theta;
        }
        var tangential = normal.Cross(radial);

        double scale = gm / (vInf * vInf);
        double Residual(double e)
        {
            double nu = System.Math.Acos(-1.0 / e) - theta;
            double denominator = 1.0 + e * System.Math.Cos(nu);
            return denominator <= 0.0 ? double.PositiveInfinity : scale * (e * e - 1.0) / denominator - r;
        }

        // e → 1⁺ collapses the conic (residual → −r); the upper end either sends the position
        // to infinity (θ > π: ν → −ν∞ at e = −1/cos(θ/2)) or grows without bound.
        double low = 1.0 + 1e-12;
        double high = theta > System.Math.PI ? -1.0 / System.Math.Cos(0.5 * theta) : 2.0;
        if (!(high > low)) return null;
        if (theta <= System.Math.PI)
            while (Residual(high) < 0.0) { high *= 2.0; if (high > 1e12) return null; }
        if (!(Residual(low) < 0.0)) return null;
        for (int i = 0; i < 200 && high - low > 1e-15 * high; i++)
        {
            double mid = 0.5 * (low + high);
            if (Residual(mid) < 0.0) low = mid; else high = mid;
        }
        double eccentricity = 0.5 * (low + high);
        double trueAnomaly = System.Math.Acos(-1.0 / eccentricity) - theta;
        double semiLatusRectum = scale * (eccentricity * eccentricity - 1.0);
        double factor = System.Math.Sqrt(gm / semiLatusRectum);
        return radial * (factor * eccentricity * System.Math.Sin(trueAnomaly))
            + tangential * (factor * (1.0 + eccentricity * System.Math.Cos(trueAnomaly)));
    }
}
