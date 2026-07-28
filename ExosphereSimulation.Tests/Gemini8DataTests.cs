namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

public sealed class Gemini8DataTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void TitanAndSpacecraftClosePublishedMassDimensionsAndThrust()
    {
        var parts = LoadParts();
        var variant = LoadVariant("gemini8_titan2_1966.json");
        var assembly = variant.Build(parts);
        var metrics = assembly.ComputeMetrics();

        Assert.Equal("Gemini-Titan II", variant.Family);
        Assert.Equal(new DateOnly(1966, 3, 16), variant.AsOfDate);
        Assert.Equal(154_980.0, metrics.WetMass, 6);
        Assert.Equal(33.223, assembly.ToPartGraph().VehicleLength, 9);
        Assert.Equal(3.05, assembly.ToPartGraph().MaximumDiameter, 9);
        Assert.Equal(1_912_740.0, metrics.SeaLevelThrust, 6);
        Assert.InRange(metrics.SeaLevelTwr, 1.25, 1.27);

        double spacecraftMass = assembly.Parts
            .Where(part => part.DefinitionId is
                "gemini8_docking_nose"
                or "gemini8_reentry_module"
                or "gemini8_retro_module"
                or "gemini8_equipment_adapter")
            .Sum(part => WetMass(parts[part.DefinitionId]));
        Assert.Equal(3_788.0, spacecraftMass, 8);

        var engines = LoadEngines();
        Assert.Equal(
            956_370.0,
            engines.Models["titan2-lr87-glv8-1966"]
                .RatedThrustSeaLevelN);
        Assert.Equal(
            444_822.0,
            engines.Models["titan2-lr91-glv8-1966"]
                .RatedThrustVacuumN);
        Assert.Equal(
            2,
            engines.Clusters["titan2-lr87-glv8-1966"].Engines.Count);
        Assert.Equal(
            1_912_740.0,
            engines.Clusters["titan2-lr87-glv8-1966"].Engines.Sum(mount =>
                engines.Models[string.IsNullOrWhiteSpace(mount.EngineModelId)
                    ? engines.Clusters["titan2-lr87-glv8-1966"].EngineModelId
                    : mount.EngineModelId].RatedThrustSeaLevelN),
            6);
    }

    [Fact]
    public void TitanStagingClosesPublishedAggregateWeightsAndConservesMass()
    {
        var vessel = LoadVariant("gemini8_titan2_1966.json")
            .Build(LoadParts())
            .ToVessel("Gemini VIII");
        double launchMass = vessel.TotalMass;

        Vessel stageOne = Assert.IsType<Vessel>(vessel.Stage());
        Assert.Equal(121_509.0, stageOne.TotalMass, 8);
        Assert.Equal(33_471.0, vessel.TotalMass, 8);
        Assert.Equal(
            launchMass,
            stageOne.TotalMass + vessel.TotalMass,
            8);
        Assert.Contains(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("titan_stage2_engine"));

        Vessel stageTwo = Assert.IsType<Vessel>(vessel.Stage());
        Assert.Equal(29_683.0, stageTwo.TotalMass, 8);
        Assert.Equal(3_788.0, vessel.TotalMass, 8);
        Assert.Equal(
            launchMass,
            stageOne.TotalMass + stageTwo.TotalMass + vessel.TotalMass,
            8);
        Assert.DoesNotContain(vessel.Parts.Parts, part =>
            part.Definition.HasVehicleRole("spacecraft_separation"));
    }

    [Fact]
    public void TitanHypergolicRuntimeClearsHoldDownThrustMargin()
    {
        string data = Path.Combine(Root.FullName, "data");
        var universe = Universe.LoadFromDataDirectory(data);
        var earth = universe.GetBody("earth")!;
        var vessel = LoadVariant("gemini8_titan2_1966.json")
            .Build(LoadParts())
            .ToVessel("Gemini VIII");
        vessel.Position = earth.Position
            + Vector3d.Right * (earth.Radius + 4.0);
        vessel.Throttle = 1.0;

        for (int step = 0; step < 250; step++)
            vessel.Tick(0.02, earth);

        Assert.InRange(vessel.GetThrustToWeightRatio(earth), 1.22, 1.30);
        Assert.All(vessel.Parts.ActiveEngines, engine =>
            Assert.True(engine.ThrottleLevel > 0.99));
    }

    [Fact]
    public void AgenaTargetHasPublishedInsertionMassAndOpposedDockingPort()
    {
        var parts = LoadParts();
        var target = LoadVariant("agena8_target_5003_1966.json")
            .Build(parts);

        Assert.Equal(3_228.0, target.ComputeMetrics().WetMass, 8);
        Assert.Equal(7.7, target.ToPartGraph().VehicleLength, 8);
        var port = parts["agena8_target_docking_adapter"];
        Assert.True(port.IsDockingPort);
        Assert.Equal([0.0, -1.0, 0.0], port.DockingAxisLocal);
        Assert.Equal(0.32, port.DockingMaxCaptureSpeedMps, 8);
    }

    [Fact]
    public void CrewSiteAndEveryOperationalEngineFieldAreTraceable()
    {
        var neil = CrewDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "crew", "neil_a_armstrong_1966.json"));
        var david = CrewDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "crew", "david_r_scott_1966.json"));
        var sites = LaunchSite.LoadAllFromDirectory(Path.Combine(
            Root.FullName, "data", "launch_sites"));
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));

        Assert.Equal("Neil Armstrong", neil.CreateMember().FullName);
        Assert.Equal("David Scott", david.CreateMember().FullName);
        Assert.Equal(28.5064, sites["cape_canaveral_lc19"].Latitude, 4);
        Assert.Equal(-80.5542, sites["cape_canaveral_lc19"].Longitude, 4);

        string[] operationalFields =
        [
            "ratedThrustSeaLevelN",
            "ratedThrustVacuumN",
            "specificImpulseSeaLevelS",
            "specificImpulseVacuumS",
            "minimumThrottle",
            "performanceMap",
            "restartEnvelope",
            "thermalEnvelope",
            "gimbalEnvelope",
            "startupTransient",
            "shutdownTransient",
        ];
        provenance.RequireFields(
            "titan2-lr87-glv8-1966", operationalFields);
        provenance.RequireFields(
            "titan2-lr91-glv8-1966", operationalFields);
        provenance.RequireFields(
            "gemini8-titan2-glv8-1966-03-16",
            "completeStackMassKg",
            "spacecraftLaunchMassKg",
            "stageMassAllocation",
            "dimensionsM",
            "eventSchedule",
            "aerothermalAndParachuteModel");
        provenance.RequireFields(
            "agena-target-5003-gemini8-1966-03-16",
            "insertionMassKg",
            "dimensionsM",
            "dockingCaptureEnvelope");
        provenance.RequireFields(
            "mission-gemini8-1966",
            "missionIdentity",
            "historicalOrbit",
            "historicalDurationS",
            "historicalEvents",
            "thrusterFailure",
            "retrofireDeltaV",
            "objectiveEnvelope");
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "mission-gemini8-1966", "thrusterFailure").Status);
    }

    [Fact]
    public void MissionEvidencePersistsFirstDockingTumbleAndControlRecovery()
    {
        MissionDefinition mission = MissionDefinition.LoadFromJson(
            Path.Combine(Root.FullName, "data", "missions",
                "gemini8_1966.json"));
        var director = new MissionDirector(mission);

        director.Observe(Snapshot(0.0, "PRE_LAUNCH"));
        director.Observe(Snapshot(
            Gemini8FlightProfile.DockingSeconds,
            "ORBIT",
            altitudeM: 271_900.0,
            dockingAchieved: true));
        director.Observe(Snapshot(
            Gemini8FlightProfile.ThrusterIsolationSeconds,
            "ORBIT",
            altitudeM: 271_900.0,
            angularRateDegPerS:
                Gemini8FlightProfile.HistoricalPeakAngularRateDegPerS));

        var restored = new MissionDirector(
            mission, director.CaptureState());
        restored.Observe(Snapshot(
            Gemini8FlightProfile.ControlRecoveredSeconds,
            "ORBIT",
            altitudeM: 271_900.0,
            angularRateDegPerS: 1.5));
        restored.Observe(Snapshot(
            Gemini8FlightProfile.SplashdownSeconds,
            "LANDED",
            altitudeM: 271_900.0,
            splashdown: true));
        MissionDebrief debrief = restored.FinalizeMission();

        Assert.True(restored.Evidence.DockingAchieved);
        Assert.True(restored.Evidence.EmergencyControlRecovered);
        Assert.Equal(
            Gemini8FlightProfile.HistoricalPeakAngularRateDegPerS,
            restored.Evidence.MaximumAngularRateDegPerS);
        Assert.Equal(MissionOutcome.Success, debrief.Outcome);
        Assert.True(debrief.Objectives.Single(result =>
            result.Id == "first-docking").Passed);
        Assert.True(debrief.Objectives.Single(result =>
            result.Id == "survive-tumble").Passed);
    }

    [Fact]
    public void MissionCannotPassWithoutDockingOrARealHighRateRecovery()
    {
        MissionDefinition mission = MissionDefinition.LoadFromJson(
            Path.Combine(Root.FullName, "data", "missions",
                "gemini8_1966.json"));
        var director = new MissionDirector(mission);
        director.Observe(Snapshot(
            Gemini8FlightProfile.SplashdownSeconds,
            "LANDED",
            altitudeM: 271_900.0,
            angularRateDegPerS: 0.0,
            splashdown: true));

        MissionDebrief debrief = director.FinalizeMission();

        Assert.Equal(MissionOutcome.Failure, debrief.Outcome);
        Assert.False(debrief.Objectives.Single(result =>
            result.Id == "first-docking").Passed);
        Assert.False(debrief.Objectives.Single(result =>
            result.Id == "survive-tumble").Passed);
    }

    [Fact]
    public void OamsFailureEnvelopeReachesPublishedRateAndRecovers()
    {
        Assert.Equal(
            0.0,
            Gemini8FlightProfile.AnomalyAngularRateDegPerS(
                Gemini8FlightProfile.ThrusterAnomalySeconds));
        Assert.Equal(
            Gemini8FlightProfile.DockedTumbleRateDegPerS,
            Gemini8FlightProfile.AnomalyAngularRateDegPerS(
                Gemini8FlightProfile.UndockingSeconds),
            8);
        Assert.Equal(
            Gemini8FlightProfile.HistoricalPeakAngularRateDegPerS,
            Gemini8FlightProfile.AnomalyAngularRateDegPerS(
                Gemini8FlightProfile.ThrusterIsolationSeconds),
            8);
        Assert.Equal(
            0.0,
            Gemini8FlightProfile.AnomalyAngularRateDegPerS(
                Gemini8FlightProfile.ControlRecoveredSeconds),
            8);
    }

    [Fact]
    public void HeadlessMissionStagesDocksSurvivesFailureAndCommitsToEntry()
    {
        string data = Path.Combine(Root.FullName, "data");
        var universe = Universe.LoadFromDataDirectory(data);
        var earth = universe.GetBody("earth")!;
        var gemini = LoadVariant("gemini8_titan2_1966.json")
            .Build(LoadParts())
            .ToVessel("Gemini VIII", "gemini8-spacecraft");
        Assert.NotNull(gemini.Stage());
        Assert.NotNull(gemini.Stage());
        var target = LoadVariant("agena8_target_5003_1966.json")
            .Build(LoadParts())
            .ToVessel("Agena 5003", "gemini8-agena-5003");

        Vector3d up = Vector3d.Right;
        Vector3d tangent = Vector3d.Forward;
        double radius = earth.Radius
            + Gemini8FlightProfile.HistoricalTargetOrbitM;
        Vector3d orbitalPosition = earth.Position + up * radius;
        Vector3d orbitalVelocity = earth.Velocity
            + tangent * System.Math.Sqrt(earth.GM / radius);
        Quaterniond orientation =
            Quaterniond.FromTo(Vector3d.Up, tangent);
        Vector3d dockingAxis = orientation.Rotate(Vector3d.Up);
        target.Position = orbitalPosition;
        target.Velocity = orbitalVelocity;
        target.Orientation = orientation;
        gemini.Position = orbitalPosition
            - dockingAxis * (0.975 + 0.10);
        gemini.Velocity = orbitalVelocity + dockingAxis * 0.10;
        gemini.Orientation = orientation;
        gemini.SASEnabled = false;
        target.SASEnabled = false;
        universe.AddVessel(gemini);
        universe.AddVessel(target);
        universe.SetActiveVessel(gemini.Id);

        string geminiPort = gemini.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole(
                "gemini_docking_port")).InstanceId;
        string targetPort = target.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole(
                "agena_target_docking_port")).InstanceId;
        DockingAttempt docking = universe.TryDock(
            gemini.Id,
            geminiPort,
            target.Id,
            targetPort,
            "gemini8-agena-docking");
        Assert.True(docking.Succeeded);
        Assert.Equal(0.10, docking.DistanceM, 8);

        double dockedRate = Gemini8FlightProfile.DockedTumbleRateDegPerS
            * MathUtils.DEG_TO_RAD;
        gemini.AngularVelocity = dockingAxis * dockedRate;
        universe.Tick(0.02);
        Assert.Equal(
            gemini.AngularVelocity,
            target.AngularVelocity);
        Assert.True(universe.Undock("gemini8-agena-docking", 0.25));

        double peakRate =
            Gemini8FlightProfile.HistoricalPeakAngularRateDegPerS
            * MathUtils.DEG_TO_RAD;
        gemini.AngularVelocity = dockingAxis * peakRate;
        universe.Tick(0.02);
        Assert.Equal(peakRate, gemini.AngularVelocity.Magnitude, 5);
        gemini.AngularVelocity = Vector3d.Zero;

        Vector3d relativeVelocity = gemini.Velocity - earth.Velocity;
        gemini.Velocity -= relativeVelocity.Normalized
            * Gemini8FlightProfile.CalibratedEmergencyRetroDeltaVMps;
        OrbitalElements returnOrbit = OrbitalElements.FromStateVector(
            gemini.Position - earth.Position,
            gemini.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            universe.CurrentTime);

        Assert.InRange(
            returnOrbit.PeriapsisRadius - earth.Radius,
            40_000.0,
            140_000.0);
        Assert.Empty(universe.DockingConnections);
        Assert.False(gemini.IsDestroyed);
    }

    private static MissionTelemetrySnapshot Snapshot(
        double time,
        string phase,
        double altitudeM = 0.0,
        bool dockingAchieved = false,
        double angularRateDegPerS = 0.0,
        bool splashdown = false) => new()
    {
        MissionTimeSeconds = time,
        Phase = phase,
        AltitudeM = altitudeM,
        SurfaceSpeedMps = altitudeM > 100_000.0 ? 7_800.0 : 0.0,
        InertialSpeedMps = altitudeM > 100_000.0 ? 7_800.0 : 0.0,
        DockingAchieved = dockingAchieved,
        AngularRateDegPerS = angularRateDegPerS,
        Splashdown = splashdown,
    };

    private static double WetMass(Exosphere.Simulation.Parts.PartDefinition part) =>
        part.MassDry
        + part.FuelCapacityLF
        + part.FuelCapacityOx
        + part.FuelCapacitySolid
        + part.FuelCapacityMono;

    private static PartCatalog LoadParts() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "parts"));

    private static VehicleVariantDefinition LoadVariant(string file) =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "vehicles", file));

    private static EngineDefinitionCatalog LoadEngines()
    {
        string data = Path.Combine(Root.FullName, "data");
        return EngineDefinitionCatalog.Load(
            Path.Combine(data, "engines"),
            Path.Combine(data, "engine_clusters"),
            DataProvenanceRegistry.LoadFromDirectory(
                Path.Combine(data, "provenance")));
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
