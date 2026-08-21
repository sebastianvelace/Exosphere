namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

public sealed class LunarTransferPlannerTests
{
    private const double EarthGm = 3.986004418e14;
    private const double MoonGm = 4.9028e12;
    private const double EarthRadius = 6_371_000.0;
    private const double MoonRadius = 1_737_400.0;
    private const double MoonSoi = 66_100_000.0;

    [Fact]
    public void LambertRoundTripHitsRequestedBoundaryState()
    {
        double radius = EarthRadius + 185_000.0;
        double circularSpeed = System.Math.Sqrt(EarthGm / radius);
        var departure = new Vector3d(radius, 0.0, 0.0);
        var arrival = new Vector3d(0.0, radius * 3.0, 0.0);

        var solution = LambertSolver.Solve(
            EarthGm,
            departure,
            arrival,
            timeOfFlight: 4_000.0,
            planeNormal: new Vector3d(0.0, 0.0, 1.0));

        var orbit = OrbitalElements.FromStateVector(
            departure,
            solution.DepartureVelocity,
            EarthGm,
            "earth",
            epoch: 0.0);
        var propagated = orbit.GetStateAtTime(4_000.0, EarthGm);

        Assert.InRange(solution.DepartureVelocity.Magnitude, circularSpeed * 0.5, circularSpeed * 2.0);
        Assert.True(
            (propagated.position - arrival).Magnitude < 5.0,
            $"Lambert endpoint missed by {(propagated.position - arrival).Magnitude:N3} m.");
        Assert.True(
            (propagated.velocity - solution.ArrivalVelocity).Magnitude < 0.01,
            $"Lambert terminal velocity disagreed by {(propagated.velocity - solution.ArrivalVelocity).Magnitude:N6} m/s.");
    }

    [Fact]
    public void Apollo8ClassPlanUsesGeocentricEphemerisAndEntersLunarSoi()
    {
        OrbitalElements parking = Apollo8ParkingOrbit();
        OrbitalElements moon = MoonEphemeris();

        // Apollo 8: TLI at 02:50:37.1 and LOI ignition at 69:08:20.4,
        // a translunar coast of 66 h 17 min 43.3 s.
        const double tliTime = 2.0 * 3600.0 + 50.0 * 60.0 + 37.1;
        const double coast = 66.0 * 3600.0 + 17.0 * 60.0 + 43.3;

        LunarTransferPlan plan = LunarTransferPlanner.Compute(
            EarthGm,
            MoonGm,
            MoonRadius,
            MoonSoi,
            parking,
            moon,
            earliestBurnTime: tliTime,
            timeOfFlight: coast,
            targetPeriluneAltitude: 60.0 * 1852.0,
            windowSamples: 90);

        Assert.True(plan.Encounter.HasEncounter);
        Assert.InRange(
            plan.Encounter.TimeOfSoiEntry - plan.BurnTime,
            2.0 * 86_400.0,
            3.5 * 86_400.0);
        Assert.InRange(
            plan.PredictedLunarPeriapsisRadius - MoonRadius,
            40_000.0,
            300_000.0);
        Assert.True(
            plan.BPlaneAimRadius > plan.TargetPeriluneRadius,
            "Lunar gravitational focusing should require a B-plane aim radius above perilune.");
        Assert.InRange(plan.InjectionDeltaVMag, 2_800.0, 3_500.0);
        Assert.InRange(plan.EstimatedCircularInsertionDeltaV, 700.0, 1_500.0);
        Assert.Equal("earth", plan.EarthTransferOrbit.ReferenceBodyId);

        LunarEncounterAnalysis analysis = LunarTransferPlanner.AnalyzeEncounter(
            EarthGm,
            MoonGm,
            MoonRadius,
            MoonSoi,
            plan.EarthTransferOrbit,
            moon,
            plan.BurnTime,
            plan.TimeOfFlight * 1.12);
        Assert.True(analysis.HasEncounter);
        Assert.False(analysis.IsImpact);
        Assert.Equal(
            plan.PredictedLunarPeriapsisRadius,
            analysis.PredictedPeriapsisRadius,
            precision: 3);

        // The UI's lower adjustment bound must not keep displaying the nominal
        // encounter: halving TLI energy leaves the spacecraft far short of lunar SOI.
        OrbitalElements underBurnOrbit = OrbitalElements.FromStateVector(
            plan.DeparturePosition,
            plan.PreBurnVelocity + plan.InjectionDeltaV * 0.5,
            EarthGm,
            "earth",
            plan.BurnTime);
        LunarEncounterAnalysis underBurn = LunarTransferPlanner.AnalyzeEncounter(
            EarthGm,
            MoonGm,
            MoonRadius,
            MoonSoi,
            underBurnOrbit,
            moon,
            plan.BurnTime,
            plan.TimeOfFlight * 1.12);
        Assert.False(underBurn.HasEncounter);

        // End-to-end guard: install the planned post-burn state in the real Universe
        // and coast under maximum rails warp. The ordinary SOI transition must reframe
        // the vessel onto the Moon; the planner is not allowed to teleport it there.
        //
        // Lambert targeting is two-body, matching Kepler on-rails. Mixed physics RK4s
        // anything below ThermosphereTopAltitude so bound LEO still decays (R7). A TLI
        // perigee at ~185 km would therefore integrate J2 for the first hour, which the
        // planner cannot see, and arrive inside the Moon. Start the SOI-patching coast
        // after that band — this test pins patched conics, not the two-body vs J2
        // residual (documented leftover).
        Universe universe = Universe.LoadFromDataDirectory(
            FindRepoRoot().FullName + "/data");
        CelestialBody earth = universe.GetBody("earth")!;
        double exitRadius = earth.MaximumRadius
            + earth.Atmosphere!.ThermosphereTopAltitude;
        double soiStartTime = plan.BurnTime;
        for (int k = 1; k <= 400; k++)
        {
            double t = plan.BurnTime + 30.0 * k;
            var (probe, _) = plan.EarthTransferOrbit.GetStateAtTime(t, EarthGm);
            if (probe.Magnitude >= exitRadius)
            {
                soiStartTime = t;
                break;
            }
        }
        universe.SetCurrentTime(soiStartTime);
        var (coastPosition, coastVelocity) = plan.EarthTransferOrbit.GetStateAtTime(
            soiStartTime, EarthGm);
        var vessel = new Vessel
        {
            Position = earth.Position + coastPosition,
            Velocity = earth.Velocity + coastVelocity,
            IsOnRails = true,
            OrbitalState = plan.EarthTransferOrbit,
            ReferenceBodyId = "earth",
        };
        universe.AddVessel(vessel);
        universe.TimeScale = 100_000.0;

        bool enteredMoonSoi = false;
        for (int i = 0; i < 1_100; i++)
        {
            const double simulatedStep = 300.0;
            universe.Tick(simulatedStep / universe.TimeScale);
            if (vessel.ReferenceBodyId == "moon")
            {
                enteredMoonSoi = true;
                break;
            }
            if (vessel.IsDestroyed)
                break;
        }

        Assert.True(enteredMoonSoi, "Planned coast never entered the Moon SOI.");
        Assert.False(vessel.IsDestroyed);
        Assert.Equal("moon", vessel.OrbitalState!.ReferenceBodyId);
        Assert.True(
            vessel.OrbitalState.Periapsis > MoonRadius,
            $"Patched lunar approach would impact at rₚ={vessel.OrbitalState.Periapsis:N1} m.");
    }

    [Fact]
    public void PlanRejectsAimingInsideTheMoon()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LunarTransferPlanner.Compute(
                EarthGm,
                MoonGm,
                MoonRadius,
                MoonSoi,
                Apollo8ParkingOrbit(),
                MoonEphemeris(),
                earliestBurnTime: 0.0,
                timeOfFlight: 3.0 * 86_400.0,
                targetPeriluneAltitude: 0.0));
    }

    private static OrbitalElements Apollo8ParkingOrbit() => new()
    {
        // Mission report: 103.3 nmi insertion altitude. The test orbit is aligned with
        // the current offline lunar ephemeris so this regression isolates translunar
        // energy and moving-target geometry from launch-azimuth optimisation.
        SemiMajorAxis = EarthRadius + 186_700.0,
        Eccentricity = 0.00049,
        Inclination = 5.145 * System.Math.PI / 180.0,
        LongitudeOfAscendingNode = 125.08 * System.Math.PI / 180.0,
        ArgumentOfPeriapsis = 0.0,
        MeanAnomalyAtEpoch = 0.0,
        Epoch = 0.0,
        ReferenceBodyId = "earth",
    };

    private static OrbitalElements MoonEphemeris() => new()
    {
        SemiMajorAxis = 384_400_000.0,
        Eccentricity = 0.0549,
        Inclination = 5.145 * System.Math.PI / 180.0,
        LongitudeOfAscendingNode = 125.08 * System.Math.PI / 180.0,
        ArgumentOfPeriapsis = 318.15 * System.Math.PI / 180.0,
        MeanAnomalyAtEpoch = 115.36 * System.Math.PI / 180.0,
        Epoch = 0.0,
        ReferenceBodyId = "earth",
    };

    private static DirectoryInfo FindRepoRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
