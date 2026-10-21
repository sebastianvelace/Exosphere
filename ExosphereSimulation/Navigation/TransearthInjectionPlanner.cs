namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>Patched-conic plan for a prograde burn out of lunar orbit back to Earth.</summary>
/// <param name="BurnTime">Impulsive burn epoch (s); centre a finite burn on it.</param>
/// <param name="TargetSelenocentricEnergy">Moon-relative specific energy to cut the burn at (J/kg).</param>
/// <param name="HyperbolicExcessVelocity">Required Moon-relative departure asymptote (m/s).</param>
/// <param name="AimPosition">Earth-relative vacuum-perigee aim point at <paramref name="ArrivalTime"/>.</param>
/// <param name="ArrivalTime">Epoch the return conic reaches the aim point (s).</param>
/// <param name="ImpulsiveDeltaV">Prograde impulsive Δv at the burn point (m/s).</param>
/// <param name="AsymptoteErrorRad">Angle between the burn's asymptote and the required one.</param>
public sealed record TransearthInjectionPlan(
    double BurnTime,
    double TargetSelenocentricEnergy,
    Vector3d HyperbolicExcessVelocity,
    Vector3d AimPosition,
    double ArrivalTime,
    double ImpulsiveDeltaV,
    double AsymptoteErrorRad);

/// <summary>
/// Plans transearth injection (TEI) from a lunar orbit. For each burn epoch over one lunar
/// revolution and each aim angle, an Earth-centred Lambert arc from the Moon to a vacuum
/// perigee aim point gives the required departure asymptote v∞. A prograde burn at the
/// vessel's position leaves along an asymptote rotated ν∞ = acos(−1/e) ahead of it; the epoch
/// whose asymptote best matches v∞ for the least Δv wins. The finite burn is cut on the
/// selenocentric energy v∞²/2, and a transearth correction removes the remaining aim error.
/// The return time of flight is searched too, from the nominal value up to
/// <c>maximumTimeOfFlight</c>: a longer coast needs a smaller v∞, which is the trade Apollo
/// made between SPS propellant and recovery timing.
/// </summary>
public static class TransearthInjectionPlanner
{
    public static TransearthInjectionPlan Compute(
        double earthGm,
        double moonGm,
        OrbitalElements moonEphemeris,
        OrbitalElements lunarOrbit,
        double earliestBurnTime,
        double timeOfFlight,
        double targetPerigeeRadius,
        int burnSamples = 120,
        double? maximumTimeOfFlight = null)
    {
        ArgumentNullException.ThrowIfNull(moonEphemeris);
        ArgumentNullException.ThrowIfNull(lunarOrbit);
        if (!(earthGm > 0.0) || !(moonGm > 0.0) || !(timeOfFlight > 0.0)
            || !(targetPerigeeRadius > 0.0) || burnSamples < 8 || !double.IsFinite(earliestBurnTime))
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight));

        double longestTimeOfFlight = System.Math.Max(timeOfFlight, maximumTimeOfFlight ?? timeOfFlight);
        const int flightTimeSamples = 6;
        double lunarPeriod = 2.0 * System.Math.PI
            * System.Math.Sqrt(System.Math.Pow(lunarOrbit.SemiMajorAxis, 3.0) / moonGm);
        TransearthInjectionPlan? best = null;
        double bestScore = double.PositiveInfinity;

        for (int sample = 0; sample < burnSamples; sample++)
        {
            double burnTime = earliestBurnTime + lunarPeriod * sample / burnSamples;
            var (burnPosition, burnVelocity) = lunarOrbit.GetStateAtTime(burnTime, moonGm);
            var (moonPosition, moonVelocity) = moonEphemeris.GetStateAtTime(burnTime, earthGm);
            var moonNormal = moonPosition.Cross(moonVelocity).Normalized;

            for (int flightSample = 0; flightSample < flightTimeSamples; flightSample++)
            for (double aimDeg = 150.0; aimDeg <= 178.0; aimDeg += 2.0)
            {
                double flightTime = timeOfFlight + (longestTimeOfFlight - timeOfFlight)
                    * flightSample / (flightTimeSamples - 1.0);
                double arrivalTime = burnTime + flightTime;
                var aimDirection = Quaterniond.FromAxisAngle(moonNormal, aimDeg * MathUtils.DEG_TO_RAD)
                    .Rotate(moonPosition.Normalized);
                var aim = aimDirection * targetPerigeeRadius;
                LambertSolution arc;
                try
                {
                    arc = LambertSolver.Solve(earthGm, moonPosition, aim, flightTime, moonNormal);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    continue;
                }

                var vInfinity = arc.DepartureVelocity - moonVelocity;
                double vInf = vInfinity.Magnitude;
                double r = burnPosition.Magnitude;
                double eccentricity = 1.0 + r * vInf * vInf / moonGm;
                double nuInfinity = System.Math.Acos(-1.0 / eccentricity);
                var radial = burnPosition / r;
                var along = (burnVelocity - radial * burnVelocity.Dot(radial)).Normalized;
                var asymptote = radial * System.Math.Cos(nuInfinity) + along * System.Math.Sin(nuInfinity);
                double asymptoteError = System.Math.Acos(System.Math.Clamp(
                    asymptote.Dot(vInfinity / vInf), -1.0, 1.0));
                double impulsive = System.Math.Sqrt(vInf * vInf + 2.0 * moonGm / r) - burnVelocity.Magnitude;
                // Cost of the burn plus the Δv a later correction pays to turn the asymptote.
                double score = impulsive + vInf * asymptoteError;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new TransearthInjectionPlan(burnTime, 0.5 * vInf * vInf, vInfinity, aim,
                        arrivalTime, impulsive, asymptoteError);
                }
            }
        }

        return best ?? throw new InvalidOperationException("No transearth injection solution in one lunar revolution.");
    }
}
