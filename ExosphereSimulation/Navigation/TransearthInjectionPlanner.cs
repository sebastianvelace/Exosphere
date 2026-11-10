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

    /// <summary>Largest change of v∞ per refinement iteration (m/s).</summary>
    public const double TrustRadiusMps = 25.0;

    /// <summary>Hours before the planned arrival at which the return is read as an Earth conic.</summary>
    public const double ApproachCheckpointHours = 12.0;

    /// <summary>
    /// Corrects a patched-conic plan against a numerical coast. The patched conic starts the
    /// Earth-centred arc at the Moon's centre and ignores Earth's pull while the vessel climbs
    /// out of the lunar sphere of influence; for a return aimed at an entry corridor a few
    /// kilometres wide that left the first transearth correction at 104 m/s even with the
    /// planned asymptote flown to 0.01°. Here the impulse at the planned burn point puts the
    /// vessel on the departure hyperbola with asymptote v∞ (<see cref="HyperbolicDeparture"/>),
    /// <see cref="PointMassPropagator"/> carries it to <see cref="ApproachCheckpointHours"/>
    /// before the planned arrival, and minimum-norm Newton steps on v∞ put the perigee radius of
    /// the Earth conic osculating there on the corridor. Only the radius is targeted, as the
    /// transearth corrections do: the perigee direction and time the conic chose are not
    /// reachable near its v∞ once the Moon bends the departure (holding them walked v∞ to a
    /// return 33 h later), and the minimum-norm step keeps v∞ as close to the plan as it can.
    /// </summary>
    /// <param name="moonStateAt">Inertial Moon state at a time.</param>
    /// <param name="earthStateAt">Inertial Earth state at a time.</param>
    /// <param name="attractorsAt">Every attractor of the field at a time (Earth, Moon, Sun...).</param>
    /// <param name="perigeeErrorM">Refined perigee radius less the target (m).</param>
    public static TransearthInjectionPlan Refine(TransearthInjectionPlan plan, OrbitalElements lunarOrbit,
        double earthGm, double moonGm, Func<double, (Vector3d Position, Vector3d Velocity)> moonStateAt,
        Func<double, (Vector3d Position, Vector3d Velocity)> earthStateAt,
        Func<double, IReadOnlyList<PointMassAttractor>> attractorsAt,
        out double perigeeErrorM, int maximumIterations = 20, double toleranceM = 100.0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(lunarOrbit);
        ArgumentNullException.ThrowIfNull(moonStateAt);
        ArgumentNullException.ThrowIfNull(earthStateAt);
        ArgumentNullException.ThrowIfNull(attractorsAt);
        var (burnPosition, burnVelocity) = lunarOrbit.GetStateAtTime(plan.BurnTime, moonGm);
        var orbitNormal = burnPosition.Cross(burnVelocity);
        var (moonPosition, moonVelocity) = moonStateAt(plan.BurnTime);
        double checkpoint = plan.ArrivalTime - ApproachCheckpointHours * 3_600.0;
        if (!(checkpoint > plan.BurnTime)) throw new ArgumentException("The return is shorter than its checkpoint.");
        var (earthPosition, earthVelocity) = earthStateAt(checkpoint);
        double targetRadius = plan.AimPosition.Magnitude;

        double PerigeeError(Vector3d vInfinity)
        {
            if (HyperbolicDeparture.RequiredVelocity(moonGm, burnPosition, vInfinity, orbitNormal) is not { } departure)
                return double.NaN;
            var (position, velocity) = PointMassPropagator.Propagate(attractorsAt, moonPosition + burnPosition,
                moonVelocity + departure, plan.BurnTime, checkpoint);
            var r = position - earthPosition;
            var v = velocity - earthVelocity;
            double e = ((r * (v.MagnitudeSquared - earthGm / r.Magnitude) - v * r.Dot(v)) / earthGm).Magnitude;
            double semiLatus = r.Cross(v).MagnitudeSquared / earthGm;
            return semiLatus / (1.0 + e) - targetRadius;
        }

        var vInf = plan.HyperbolicExcessVelocity;
        double error = PerigeeError(vInf);
        if (!double.IsFinite(error)) throw new InvalidOperationException("The planned asymptote has no departure branch.");
        const double probe = 0.05; // m/s
        for (int iteration = 0; iteration < maximumIterations && System.Math.Abs(error) > toleranceM; iteration++)
        {
            var gradient = new Vector3d(
                (PerigeeError(vInf + new Vector3d(probe, 0, 0)) - error) / probe,
                (PerigeeError(vInf + new Vector3d(0, probe, 0)) - error) / probe,
                (PerigeeError(vInf + new Vector3d(0, 0, probe)) - error) / probe);
            if (!(gradient.MagnitudeSquared > 0.0) || !double.IsFinite(gradient.MagnitudeSquared)) break;
            // Minimum-norm Newton step on one condition, bounded by the trust radius.
            var step = gradient * (error / gradient.MagnitudeSquared);
            if (step.Magnitude > TrustRadiusMps) step *= TrustRadiusMps / step.Magnitude;
            bool improved = false;
            for (int halving = 0; halving < 8 && !improved; halving++)
            {
                double next = PerigeeError(vInf - step);
                if (double.IsFinite(next) && System.Math.Abs(next) < System.Math.Abs(error))
                {
                    vInf -= step;
                    error = next;
                    improved = true;
                }
                else step *= 0.5;
            }
            if (!improved) break;
        }

        perigeeErrorM = error;
        var required = HyperbolicDeparture.RequiredVelocity(moonGm, burnPosition, vInf, orbitNormal)!.Value;
        return plan with
        {
            HyperbolicExcessVelocity = vInf,
            TargetSelenocentricEnergy = 0.5 * vInf.MagnitudeSquared,
            ImpulsiveDeltaV = (required - burnVelocity).Magnitude,
        };
    }

    /// <summary>Time (s) from a state to the conic's periapsis, negative once past it.</summary>
    private static double TimeToPerigee(double gm, double radius, double radialDot, double e, double a)
    {
        if (e < 1.0)
        {
            double cosE = (1.0 - radius / a) / e;
            double sinE = radialDot / (e * System.Math.Sqrt(gm * a));
            double anomaly = System.Math.Atan2(sinE, cosE);
            double mean = anomaly - e * sinE;
            return -mean / System.Math.Sqrt(gm / (a * a * a));
        }
        double coshF = (1.0 - radius / a) / e;
        double sinhF = radialDot / (e * System.Math.Sqrt(-gm * a));
        double f = System.Math.Asinh(sinhF);
        double hyperbolicMean = e * sinhF - f;
        return -hyperbolicMean / System.Math.Sqrt(gm / (-a * a * a));
    }
}
