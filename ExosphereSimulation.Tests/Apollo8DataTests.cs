namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Exosphere.Simulation.Propulsion;

public sealed class Apollo8DataTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void As503StackClosesPublishedIgnitionMassDimensionsAndThrust()
    {
        var parts = LoadParts();
        var variant = LoadVariant();
        var assembly = variant.Build(parts);
        var metrics = assembly.ComputeMetrics();
        var graph = assembly.ToPartGraph();

        Assert.Equal("Apollo-Saturn V", variant.Family);
        Assert.Equal(new DateOnly(1968, 12, 21), variant.AsOfDate);
        Assert.Equal(2_821_241.12233964, metrics.WetMass, 5);
        Assert.Equal(110.7, graph.VehicleLength, 9);
        Assert.Equal(10.1, graph.MaximumDiameter, 9);
        Assert.Equal(33_850_000.0, metrics.SeaLevelThrust, 5);
        Assert.InRange(metrics.SeaLevelTwr, 1.223, 1.225);

        double spacecraftMass = assembly.Parts
            .Take(6)
            .Sum(part => WetMass(parts[part.DefinitionId]));
        Assert.Equal(43_668.24464464, spacecraftMass, 6);
    }

    [Fact]
    public void LesAndThreeMechanicalStagesConserveMassAndCloseActualCsmWeight()
    {
        var vessel = LoadVariant()
            .Build(LoadParts())
            .ToVessel("Apollo 8");
        double ignitionMass = vessel.TotalMass;
        string lesId = vessel.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole("launch_escape_system")).InstanceId;

        Vessel les = Assert.IsType<Vessel>(
            vessel.DeployPayload(lesId, "Apollo 8 LES"));
        Assert.Equal(4_027.75476976, les.TotalMass, 8);
        Assert.Equal(ignitionMass, vessel.TotalMass + les.TotalMass, 6);

        Vessel sic = Assert.IsType<Vessel>(vessel.Stage());
        Assert.Equal(2_181_053.5519075, sic.TotalMass, 5);
        Assert.Contains(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("sii_engine_cluster"));

        Vessel sii = Assert.IsType<Vessel>(vessel.Stage());
        Assert.Equal(474_720.7025946, sii.TotalMass, 5);
        Assert.Contains(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("sivb_engine"));

        Vessel sivb = Assert.IsType<Vessel>(vessel.Stage());
        Assert.Equal(132_625.1113554, sivb.TotalMass, 5);
        Assert.Equal(28_814.00171188, vessel.TotalMass, 6);
        Assert.Equal(
            ignitionMass,
            les.TotalMass + sic.TotalMass + sii.TotalMass
                + sivb.TotalMass + vessel.TotalMass,
            5);
        Assert.DoesNotContain(vessel.Parts.Parts, part =>
            part.Definition.HasVehicleRole("csm_separation"));
    }

    [Fact]
    public void F1AndJ2ClustersPreserveIndividualMountsAndSivbRestart()
    {
        var engines = LoadEngines();

        Assert.Equal(5, engines.Clusters["f1-five-as503-1968"].Engines.Count);
        Assert.Equal(
            "f1-center",
            engines.Clusters["f1-five-as503-1968"].Engines[^1].InstanceId);
        Assert.False(
            engines.Clusters["f1-five-as503-1968"].Engines[^1].Gimballed);
        Assert.Equal(
            33_850_000.0,
            engines.Clusters["f1-five-as503-1968"].Engines.Sum(mount =>
                engines.Models[string.IsNullOrWhiteSpace(mount.EngineModelId)
                    ? engines.Clusters["f1-five-as503-1968"].EngineModelId
                    : mount.EngineModelId].RatedThrustSeaLevelN),
            5);
        Assert.Equal(
            5_004_249.0,
            engines.Clusters["j2-five-sii-as503-1968"].Engines.Sum(mount =>
                engines.Models[string.IsNullOrWhiteSpace(mount.EngineModelId)
                    ? engines.Clusters["j2-five-sii-as503-1968"].EngineModelId
                    : mount.EngineModelId].RatedThrustVacuumN),
            5);
        Assert.Equal(423.7,
            engines.Models["j2-sii-as503-1968"].SpecificImpulseVacuumS, 8);
        Assert.Equal(428.8,
            engines.Models["j2-sivb-as503-1968"].SpecificImpulseVacuumS, 8);
        Assert.Equal(1, engines.Models["j2-sivb-as503-1968"].RestartLimit);
        Assert.Single(
            engines.Clusters["apollo-sps-single-csm103-1968"].Engines);
        Assert.Equal(91_185.0,
            engines.Models["apollo-sps-csm103-1968"].RatedThrustVacuumN,
            8);
        Assert.True(
            engines.Models["apollo-sps-csm103-1968"].RestartLimit >= 3);
    }

    [Fact]
    public void CraftV2RoundTripPreservesApolloTopologyAndStablePartIds()
    {
        var catalog = LoadParts();
        var original = LoadVariant().Build(catalog);
        CraftDocumentV2 document = original.ToCraftDocument("Apollo 8 AS-503");
        document.VehicleVariantId =
            "apollo8-saturn5-as503-csm103-1968-12-21";

        string json = JsonSerializer.Serialize(document);
        CraftDocumentV2 decoded =
            JsonSerializer.Deserialize<CraftDocumentV2>(json)!;
        var restored = VesselAssembly.FromCraft(catalog, decoded);

        Assert.Equal(document.VehicleVariantId, decoded.VehicleVariantId);
        Assert.Equal(original.ComputeMetrics(), restored.ComputeMetrics());
        Assert.Equal(
            original.Parts.Select(part => part.InstanceId).Order(),
            restored.Parts.Select(part => part.InstanceId).Order());
        Assert.Equal(15, restored.Parts.Count);
        Assert.Equal(14, restored.Connections.Count);
    }

    [Fact]
    public void CrewPadAndEveryApolloHardwareFieldAreTraceable()
    {
        string data = Path.Combine(Root.FullName, "data");
        var crew = CrewDefinition.LoadAllFromDirectory(
            Path.Combine(data, "crew"));
        var sites = LaunchSite.LoadAllFromDirectory(
            Path.Combine(data, "launch_sites"));
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(data, "provenance"));

        Assert.Equal("Frank Borman",
            crew["frank-f-borman-ii"].CreateMember().FullName);
        Assert.Equal("James Lovell",
            crew["james-a-lovell-jr"].CreateMember().FullName);
        Assert.Equal("William Anders",
            crew["william-a-anders"].CreateMember().FullName);
        Assert.Equal(28.608389, sites["kennedy"].Latitude, 6);
        Assert.Equal(-80.604333, sites["kennedy"].Longitude, 6);

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
        provenance.RequireFields("f1-as503-1968", operationalFields);
        provenance.RequireFields("j2-sii-as503-1968", operationalFields);
        provenance.RequireFields("j2-sivb-as503-1968", operationalFields);
        provenance.RequireFields("apollo-sps-csm103-1968", operationalFields);
        provenance.RequireFields(
            "apollo8-saturn5-as503-csm103-1968-12-21",
            "completeStackIgnitionMassKg",
            "spacecraftLaunchMassKg",
            "stageMassAllocation",
            "dimensionsM",
            "eventSchedule",
            "csmMassClosure",
            "lunarTestArticleMassKg",
            "launchSite");
        provenance.RequireFields(
            "mission-apollo8-1968",
            "missionIdentity",
            "historicalEarthParkingOrbit",
            "tliSchedule",
            "lunarOrbitSequence",
            "teiReturn",
            "entryLanding",
            "objectiveEnvelope");
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "apollo8-saturn5-as503-csm103-1968-12-21",
                "spacecraftLaunchMassKg").Status);
    }

    [Fact]
    public void MissionScheduleAndEvaluatorRequireTenLunarRevolutions()
    {
        MissionDefinition mission = LoadMission();
        var director = new MissionDirector(mission);

        director.Observe(Snapshot(0.0, "PRE_LAUNCH"));
        director.Observe(Snapshot(
            Apollo8FlightProfile.ParkingOrbitInsertionSeconds,
            "ORBIT",
            altitudeM: Apollo8FlightProfile.ParkingOrbitAltitudeM));
        director.Observe(Snapshot(
            Apollo8FlightProfile.TliCutoffSeconds,
            "TLI",
            altitudeM: 330_000.0));
        director.Observe(Snapshot(
            Apollo8FlightProfile.LoiCutoffSeconds,
            "LUNAR_ORBIT",
            altitudeM: Apollo8FlightProfile.InitialLunarPeriluneAltitudeM,
            completedLunarOrbits: 0.1));
        director.Observe(Snapshot(
            Apollo8FlightProfile.TeiIgnitionSeconds,
            "LUNAR_ORBIT",
            altitudeM: Apollo8FlightProfile.CircularLunarOrbitAltitudeM,
            completedLunarOrbits: 10.0));

        var restored = new MissionDirector(
            mission, director.CaptureState());
        restored.Observe(Snapshot(
            Apollo8FlightProfile.TeiCutoffSeconds,
            "TEI",
            altitudeM: Apollo8FlightProfile.CircularLunarOrbitAltitudeM,
            completedLunarOrbits: 10.0));
        restored.Observe(Snapshot(
            Apollo8FlightProfile.SplashdownSeconds,
            "LANDED",
            completedLunarOrbits: 10.0,
            gForce: 6.8,
            splashdown: true));

        MissionDebrief debrief = restored.FinalizeMission();

        Assert.Equal(4, mission.Sequence);
        Assert.Equal(
            ["mission-gemini8-1966"],
            mission.PrerequisiteMissionIds);
        Assert.Equal(10.0, restored.Evidence.CompletedLunarOrbits, 8);
        Assert.Equal(
            MissionOutcome.Success,
            debrief.Outcome);
        Assert.True(debrief.Objectives.Single(result =>
            result.Id == "ten-lunar-revolutions").Passed);
        Assert.Equal(529_242.0,
            Apollo8FlightProfile.SplashdownSeconds, 8);
    }

    [Fact]
    public void MissionCannotPassWithoutLunarOrbitOrTeiEvidence()
    {
        var director = new MissionDirector(LoadMission());
        director.Observe(Snapshot(
            Apollo8FlightProfile.SplashdownSeconds,
            "LANDED",
            completedLunarOrbits: 0.0,
            splashdown: true));

        MissionDebrief debrief = director.FinalizeMission();

        Assert.Equal(MissionOutcome.Failure, debrief.Outcome);
        Assert.False(debrief.Objectives.Single(result =>
            result.Id == "ten-lunar-revolutions").Passed);
        Assert.False(debrief.Objectives.Single(result =>
            result.Id == "transearth-injection").Passed);
    }

    [Fact]
    public void HistoricalParkingOrbitProducesSafeEphemerisTargetedTli()
    {
        string data = Path.Combine(Root.FullName, "data");
        Universe universe = Universe.LoadFromDataDirectory(data);
        CelestialBody earth = universe.GetBody("earth")!;
        CelestialBody moon = universe.GetBody("moon")!;
        Assert.NotNull(moon.OrbitalElements);

        var parking = new OrbitalElements
        {
            SemiMajorAxis =
                earth.Radius + Apollo8FlightProfile.ParkingOrbitAltitudeM,
            Eccentricity = 0.00049,
            Inclination = moon.OrbitalElements!.Inclination,
            LongitudeOfAscendingNode =
                moon.OrbitalElements.LongitudeOfAscendingNode,
            ArgumentOfPeriapsis = 0.0,
            MeanAnomalyAtEpoch = 0.0,
            Epoch = 0.0,
            ReferenceBodyId = earth.Id,
        };
        LunarTransferPlan plan = LunarTransferPlanner.Compute(
            earth.GM,
            moon.GM,
            moon.Radius,
            moon.SphereOfInfluence,
            parking,
            moon.OrbitalElements!,
            Apollo8FlightProfile.TliIgnitionSeconds,
            Apollo8FlightProfile.LoiIgnitionSeconds
                - Apollo8FlightProfile.TliIgnitionSeconds,
            Apollo8FlightProfile.InitialLunarPeriluneAltitudeM,
            windowSamples: 60);

        Assert.True(plan.Encounter.HasEncounter);
        Assert.InRange(plan.InjectionDeltaVMag, 3_000.0, 4_500.0);
        Assert.InRange(
            plan.PredictedLunarPeriapsisRadius - moon.Radius,
            80_000.0,
            160_000.0);
        Assert.InRange(
            plan.EstimatedCircularInsertionDeltaV,
            700.0,
            1_200.0);
    }

    private static double WetMass(Exosphere.Simulation.Parts.PartDefinition part) =>
        part.MassDry + part.FuelCapacityLF + part.FuelCapacityOx
        + part.FuelCapacitySolid + part.FuelCapacityMono;

    private static PartCatalog LoadParts() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "parts"));

    private static VehicleVariantDefinition LoadVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "vehicles",
            "apollo8_saturn5_as503_1968.json"));

    private static MissionDefinition LoadMission() =>
        MissionDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "missions", "apollo8_1968.json"));

    private static MissionTelemetrySnapshot Snapshot(
        double time,
        string phase,
        double altitudeM = 0.0,
        double completedLunarOrbits = 0.0,
        double gForce = 0.0,
        bool splashdown = false) => new()
    {
        MissionTimeSeconds = time,
        Phase = phase,
        AltitudeM = altitudeM,
        SurfaceSpeedMps = altitudeM > 100_000.0 ? 7_800.0 : 0.0,
        InertialSpeedMps = altitudeM > 100_000.0 ? 7_800.0 : 0.0,
        CompletedLunarOrbits = completedLunarOrbits,
        GForce = gForce,
        Splashdown = splashdown,
    };

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
