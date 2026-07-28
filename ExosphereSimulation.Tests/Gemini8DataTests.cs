namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
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
            1_912_740.0,
            engines.Models["titan2-lr87-glv8-1966"]
                .RatedThrustSeaLevelN);
        Assert.Equal(
            444_822.0,
            engines.Models["titan2-lr91-glv8-1966"]
                .RatedThrustVacuumN);
        Assert.Equal(
            2,
            engines.Clusters["titan2-lr87-glv8-1966"].Engines.Count);
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
    }

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
