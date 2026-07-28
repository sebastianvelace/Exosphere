namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
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
            .Take(5)
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
        Assert.Equal(14, restored.Parts.Count);
        Assert.Equal(13, restored.Connections.Count);
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
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "apollo8-saturn5-as503-csm103-1968-12-21",
                "spacecraftLaunchMassKg").Status);
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
