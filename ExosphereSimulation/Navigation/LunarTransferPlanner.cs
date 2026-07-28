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
/// Moon-centred interpretation of an Earth-relative transfer conic.
/// </summary>
public readonly record struct LunarEncounterAnalysis(
    EncounterResult Encounter,
    bool             HasEncounter,
    bool             IsImpact,
    double           PredictedPeriapsisRadius,
    double           HyperbolicExcessSpeed,
    double           EstimatedCircularInsertionDeltaV);

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

                    if (!TryFindInboundSoiEntry(
                            transferOrbit,
                            moonEphemeris,
                            earthGm,
                            moonSoiRadius,
                            burnTime,
                            arrivalTime,
                            out double candidateEntryTime))
                        continue;

                    var (candidateEntryPosition, candidateEntryVelocity) =
                        transferOrbit.GetStateAtTime(candidateEntryTime, earthGm);
                    var (candidateMoonPosition, candidateMoonVelocity) =
                        moonEphemeris.GetStateAtTime(candidateEntryTime, earthGm);

                    // Rank candidates from the Moon-centred conic at the actual inbound
                    // SOI boundary. This includes lunar gravitational focusing while
                    // avoiding a full coarse encounter scan for every B-plane sample.
                    OrbitalElements lunarApproach = OrbitalElements.FromStateVector(
                        candidateEntryPosition - candidateMoonPosition,
                        candidateEntryVelocity - candidateMoonVelocity,
                        moonGm,
                        "moon",
                        candidateEntryTime);
                    double predictedPerilune = lunarApproach.Periapsis;
                    closestAltitudeSeen = System.Math.Min(
                        closestAltitudeSeen,
                        predictedPerilune - moonRadius);
                    if (!double.IsFinite(predictedPerilune) ||
                        predictedPerilune <= moonRadius)
                        continue;

                    double lunarEnergy =
                        (candidateEntryVelocity - candidateMoonVelocity).MagnitudeSquared * 0.5
                        - moonGm / (candidateEntryPosition - candidateMoonPosition).Magnitude;
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
                        default,
                        vInfinity,
                        circularInsertion,
                        targetRadius,
                        aimRadius,
                        predictedPerilune);
                }
            }
        }

        if (best is null)
            throw new InvalidOperationException(
                $"No safe lunar SOI encounter was found in the requested parking-orbit window " +
                $"(closest predicted altitude {closestAltitudeSeen:G6} m).");

        LunarEncounterAnalysis finalAnalysis = AnalyzeEncounter(
            earthGm,
            moonGm,
            moonRadius,
            moonSoiRadius,
            best.EarthTransferOrbit,
            moonEphemeris,
            best.BurnTime,
            best.TimeOfFlight * 1.12);
        if (!finalAnalysis.HasEncounter)
            throw new InvalidOperationException(
                "The selected lunar transfer did not cross the Moon sphere of influence.");
        if (finalAnalysis.IsImpact)
            throw new InvalidOperationException(
                $"The selected lunar transfer intersects the Moon " +
                $"(rₚ={finalAnalysis.PredictedPeriapsisRadius:G6} m).");

        return best with
        {
            Encounter = finalAnalysis.Encounter,
            PredictedLunarRelativeSpeed =
                finalAnalysis.HyperbolicExcessSpeed,
            EstimatedCircularInsertionDeltaV =
                finalAnalysis.EstimatedCircularInsertionDeltaV,
            PredictedLunarPeriapsisRadius =
                finalAnalysis.PredictedPeriapsisRadius,
        };
    }

    private const double TwoPi = 2.0 * System.Math.PI;

    /// <summary>
    /// Evaluates a post-burn Earth conic against the moving Moon and converts its
    /// inbound SOI state into a focused Moon-centred hyperbola.
    /// </summary>
    public static LunarEncounterAnalysis AnalyzeEncounter(
        double          earthGm,
        double          moonGm,
        double          moonRadius,
        double          moonSoiRadius,
        OrbitalElements earthTransferOrbit,
        OrbitalElements moonEphemeris,
        double          burnTime,
        double          searchWindow)
    {
        if (!double.IsFinite(earthGm) || earthGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(earthGm));
        if (!double.IsFinite(moonGm) || moonGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(moonGm));
        if (!double.IsFinite(moonRadius) || moonRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(moonRadius));
        if (!double.IsFinite(moonSoiRadius) || moonSoiRadius <= moonRadius)
            throw new ArgumentOutOfRangeException(nameof(moonSoiRadius));
        if (earthTransferOrbit is null)
            throw new ArgumentNullException(nameof(earthTransferOrbit));
        if (moonEphemeris is null)
            throw new ArgumentNullException(nameof(moonEphemeris));
        if (!double.IsFinite(burnTime))
            throw new ArgumentOutOfRangeException(nameof(burnTime));
        if (!double.IsFinite(searchWindow) || searchWindow <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(searchWindow));

        EncounterResult encounter = TrajectoryPrediction.FindEncounter(
            earthTransferOrbit,
            earthGm,
            t => moonEphemeris.GetStateAtTime(t, earthGm).position,
            moonSoiRadius,
            burnTime,
            searchWindow,
            coarseSteps: 1440);
        if (!encounter.HasEncounter)
        {
            return new LunarEncounterAnalysis(
                encounter,
                HasEncounter: false,
                IsImpact: false,
                PredictedPeriapsisRadius: double.PositiveInfinity,
                HyperbolicExcessSpeed: 0.0,
                EstimatedCircularInsertionDeltaV: 0.0);
        }

        double entryTime = encounter.TimeOfSoiEntry;
        var (entryPosition, entryVelocity) =
            earthTransferOrbit.GetStateAtTime(entryTime, earthGm);
        var (moonPosition, moonVelocity) =
            moonEphemeris.GetStateAtTime(entryTime, earthGm);
        Vector3d lunarPosition = entryPosition - moonPosition;
        Vector3d lunarVelocity = entryVelocity - moonVelocity;
        OrbitalElements lunarApproach = OrbitalElements.FromStateVector(
            lunarPosition,
            lunarVelocity,
            moonGm,
            "moon",
            entryTime);
        double perilune = lunarApproach.Periapsis;
        bool impact = !double.IsFinite(perilune) || perilune <= moonRadius;
        if (impact)
        {
            return new LunarEncounterAnalysis(
                encounter,
                HasEncounter: true,
                IsImpact: true,
                PredictedPeriapsisRadius: perilune,
                HyperbolicExcessSpeed: 0.0,
                EstimatedCircularInsertionDeltaV: 0.0);
        }

        double energy =
            lunarVelocity.MagnitudeSquared * 0.5
            - moonGm / lunarPosition.Magnitude;
        double vInfinity = energy > 0.0
            ? System.Math.Sqrt(2.0 * energy)
            : 0.0;
        double periluneSpeed = System.Math.Sqrt(
            vInfinity * vInfinity + 2.0 * moonGm / perilune);
        double circularSpeed = System.Math.Sqrt(moonGm / perilune);

        return new LunarEncounterAnalysis(
            encounter,
            HasEncounter: true,
            IsImpact: false,
            PredictedPeriapsisRadius: perilune,
            HyperbolicExcessSpeed: vInfinity,
            EstimatedCircularInsertionDeltaV:
                System.Math.Max(0.0, periluneSpeed - circularSpeed));
    }

    private static bool TryFindInboundSoiEntry(
        OrbitalElements transferOrbit,
        OrbitalElements moonEphemeris,
        double          earthGm,
        double          moonSoiRadius,
        double          burnTime,
        double          arrivalTime,
        out double      entryTime)
    {
        double Separation(double t)
        {
            Vector3d vesselPosition =
                transferOrbit.GetStateAtTime(t, earthGm).position;
            Vector3d moonPosition =
                moonEphemeris.GetStateAtTime(t, earthGm).position;
            return (vesselPosition - moonPosition).Magnitude;
        }

        double lower = burnTime;
        double upper = arrivalTime;
        if (Separation(lower) <= moonSoiRadius ||
            Separation(upper) > moonSoiRadius)
        {
            entryTime = double.PositiveInfinity;
            return false;
        }

        // A direct zero-revolution translunar arc has one inbound SOI crossing.
        // Bisect that outside→inside transition to millisecond-scale timing.
        for (int i = 0; i < 60; i++)
        {
            double middle = 0.5 * (lower + upper);
            if (Separation(middle) <= moonSoiRadius)
                upper = middle;
            else
                lower = middle;
        }

        entryTime = upper;
        return true;
    }

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
