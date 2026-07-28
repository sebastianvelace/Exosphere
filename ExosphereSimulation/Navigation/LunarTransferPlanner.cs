namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>
/// Ephemeris-targeted patched-conic plan from an Earth parking orbit to a lunar encounter.
/// All states are Earth-centred inertial and all quantities use SI units.
/// </summary>
public sealed record LunarTransferPlan(
    double          BurnTime,
    double          ArrivalTime,
    Vector3d        DeparturePosition,
    Vector3d        PreBurnVelocity,
    Vector3d        PostBurnVelocity,
    Vector3d        InjectionDeltaV,
    OrbitalElements EarthTransferOrbit,
    EncounterResult Encounter,
    double          PredictedLunarRelativeSpeed,
    double          EstimatedCircularInsertionDeltaV,
    double          TargetPeriluneRadius,
    double          BPlaneAimRadius,
    double          PredictedLunarPeriapsisRadius)
{
    public double TimeOfFlight       => ArrivalTime - BurnTime;
    public double InjectionDeltaVMag => InjectionDeltaV.Magnitude;
}

/// <summary>
/// Plans a translunar injection against the Moon's moving ephemeris.
///
/// Unlike a heliocentric Hohmann shortcut, this searches a real burn window on the
/// parking orbit, solves an Earth-centred Lambert boundary problem to a lunar B-plane
/// aim point, and verifies the resulting conic enters the Moon's sphere of influence.
/// </summary>
public static class LunarTransferPlanner
{
    /// <summary>
    /// Finds the lowest-injection-cost encounter in one parking-orbit search window.
    /// </summary>
    public static LunarTransferPlan Compute(
        double          earthGm,
        double          moonGm,
        double          moonRadius,
        double          moonSoiRadius,
        OrbitalElements parkingOrbit,
        OrbitalElements moonEphemeris,
        double          earliestBurnTime,
        double          timeOfFlight,
        double          targetPeriluneAltitude,
        int             windowSamples = 180)
    {
        Validate(
            earthGm, moonGm, moonRadius, moonSoiRadius, parkingOrbit,
            moonEphemeris, earliestBurnTime, timeOfFlight,
            targetPeriluneAltitude, windowSamples);

        double parkingPeriod = TwoPi *
            System.Math.Sqrt(System.Math.Pow(parkingOrbit.SemiMajorAxis, 3.0) / earthGm);
        double targetRadius = moonRadius + targetPeriluneAltitude;

        LunarTransferPlan? best = null;
        double bestScore = double.PositiveInfinity;
        double closestAltitudeSeen = double.PositiveInfinity;
        int encounterCount = 0;

        for (int sample = 0; sample < windowSamples; sample++)
        {
            double burnTime = earliestBurnTime + parkingPeriod * sample / windowSamples;
            double arrivalTime = burnTime + timeOfFlight;
            var (departurePosition, preBurnVelocity) =
                parkingOrbit.GetStateAtTime(burnTime, earthGm);
            var (moonPosition, moonVelocity) =
                moonEphemeris.GetStateAtTime(arrivalTime, earthGm);

            Vector3d orbitNormal = moonPosition.Cross(moonVelocity).Normalized;
            if (orbitNormal.Magnitude < 1e-12)
                continue;

            LambertSolution centreSolution;
            try
            {
                centreSolution = LambertSolver.Solve(
                    earthGm,
                    departurePosition,
                    moonPosition,
                    timeOfFlight,
                    departurePosition.Cross(preBurnVelocity).Normalized);
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (ArgumentException)
            {
                continue;
            }

            Vector3d centreArrivalRelative =
                centreSolution.ArrivalVelocity - moonVelocity;
            Vector3d bPlaneAxis =
                centreArrivalRelative.Normalized.Cross(orbitNormal).Normalized;
            if (bPlaneAxis.Magnitude < 1e-12)
                continue;

            // Search both sides and a range of offsets in the lunar B-plane. The B-plane
            // aim radius is larger than the desired perilune because lunar gravity
            // focuses the hyperbolic approach after SOI entry.
            for (int side = -1; side <= 1; side += 2)
            {
                const int aimSamples = 12;
                for (int aimSample = 0; aimSample < aimSamples; aimSample++)
                {
                    double aimRadius = targetRadius *
                        (1.0 + 4.0 * aimSample / (aimSamples - 1.0));
                    Vector3d targetPosition =
                        moonPosition + bPlaneAxis * (side * aimRadius);
                    LambertSolution solution;
                    try
                    {
                        solution = LambertSolver.Solve(
                            earthGm,
                            departurePosition,
                            targetPosition,
                            timeOfFlight,
                            departurePosition.Cross(preBurnVelocity).Normalized);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    Vector3d injectionDeltaV =
                        solution.DepartureVelocity - preBurnVelocity;
                    var transferOrbit = OrbitalElements.FromStateVector(
                        departurePosition,
                        solution.DepartureVelocity,
                        earthGm,
                        parkingOrbit.ReferenceBodyId,
                        burnTime);

                    EncounterResult encounter = TrajectoryPrediction.FindEncounter(
                        transferOrbit,
                        earthGm,
                        t => moonEphemeris.GetStateAtTime(t, earthGm).position,
                        moonSoiRadius,
                        burnTime,
                        timeOfFlight * 1.12,
                        coarseSteps: 720);

                    closestAltitudeSeen = System.Math.Min(
                        closestAltitudeSeen,
                        encounter.ClosestApproachDistance - moonRadius);
                    if (encounter.HasEncounter)
                        encounterCount++;
                    if (!encounter.HasEncounter)
                        continue;

                    double entryTime = encounter.TimeOfSoiEntry;
                    var (entryPosition, entryVelocity) =
                        transferOrbit.GetStateAtTime(entryTime, earthGm);
                    var (moonEntryPosition, moonEntryVelocity) =
                        moonEphemeris.GetStateAtTime(entryTime, earthGm);
                    OrbitalElements lunarApproach = OrbitalElements.FromStateVector(
                        entryPosition - moonEntryPosition,
                        entryVelocity - moonEntryVelocity,
                        moonGm,
                        "moon",
                        entryTime);
                    double predictedPerilune = lunarApproach.Periapsis;
                    if (!double.IsFinite(predictedPerilune) ||
                        predictedPerilune <= moonRadius)
                        continue;

                    double lunarEnergy =
                        (entryVelocity - moonEntryVelocity).MagnitudeSquared * 0.5
                        - moonGm / (entryPosition - moonEntryPosition).Magnitude;
                    double vInfinity = lunarEnergy > 0.0
                        ? System.Math.Sqrt(2.0 * lunarEnergy)
                        : 0.0;
                    double hyperbolicPeriluneSpeed = System.Math.Sqrt(
                        vInfinity * vInfinity + 2.0 * moonGm / predictedPerilune);
                    double circularSpeed =
                        System.Math.Sqrt(moonGm / predictedPerilune);
                    double circularInsertion = System.Math.Max(
                        0.0, hyperbolicPeriluneSpeed - circularSpeed);

                    // Stay close to the requested perilune while minimising TLI. A
                    // 10 km miss is scored as 1 m/s so grossly wrong fly-bys cannot
                    // beat the physically intended lunar approach on tiny TLI savings.
                    double miss = System.Math.Abs(predictedPerilune - targetRadius);
                    double score = injectionDeltaV.Magnitude + miss / 10_000.0;

                    if (score >= bestScore)
                        continue;

                    bestScore = score;
                    best = new LunarTransferPlan(
                        burnTime,
                        arrivalTime,
                        departurePosition,
                        preBurnVelocity,
                        solution.DepartureVelocity,
                        injectionDeltaV,
                        transferOrbit,
                        encounter,
                        vInfinity,
                        circularInsertion,
                        targetRadius,
                        aimRadius,
                        predictedPerilune);
                }
            }
        }

        return best ?? throw new InvalidOperationException(
            $"No safe lunar SOI encounter was found in the requested parking-orbit window " +
            $"({encounterCount} SOI crossings; closest altitude {closestAltitudeSeen:G6} m).");
    }

    private const double TwoPi = 2.0 * System.Math.PI;

    private static void Validate(
        double earthGm,
        double moonGm,
        double moonRadius,
        double moonSoiRadius,
        OrbitalElements parkingOrbit,
        OrbitalElements moonEphemeris,
        double earliestBurnTime,
        double timeOfFlight,
        double targetPeriluneAltitude,
        int windowSamples)
    {
        if (!double.IsFinite(earthGm) || earthGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(earthGm));
        if (!double.IsFinite(moonGm) || moonGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(moonGm));
        if (!double.IsFinite(moonRadius) || moonRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(moonRadius));
        if (!double.IsFinite(moonSoiRadius) || moonSoiRadius <= moonRadius)
            throw new ArgumentOutOfRangeException(nameof(moonSoiRadius));
        if (parkingOrbit is null)
            throw new ArgumentNullException(nameof(parkingOrbit));
        if (moonEphemeris is null)
            throw new ArgumentNullException(nameof(moonEphemeris));
        if (parkingOrbit.IsHyperbolic || parkingOrbit.SemiMajorAxis <= 0.0)
            throw new ArgumentException("Parking orbit must be a bound Earth orbit.", nameof(parkingOrbit));
        if (moonEphemeris.IsHyperbolic || moonEphemeris.SemiMajorAxis <= moonRadius)
            throw new ArgumentException("Moon ephemeris must be a bound Earth-relative orbit.", nameof(moonEphemeris));
        if (!double.IsFinite(earliestBurnTime))
            throw new ArgumentOutOfRangeException(nameof(earliestBurnTime));
        if (!double.IsFinite(timeOfFlight) || timeOfFlight <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight));
        if (!double.IsFinite(targetPeriluneAltitude) || targetPeriluneAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(targetPeriluneAltitude));
        if (windowSamples < 12 || windowSamples > 4096)
            throw new ArgumentOutOfRangeException(nameof(windowSamples));
    }
}
