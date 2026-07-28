namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public sealed class Apollo11DataTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void As506StackClosesPublishedIgnitionMassDimensionsAndThrust()
    {
        PartCatalog parts = LoadParts();
        VehicleVariantDefinition variant = LoadLaunchVariant();
        VesselAssembly assembly = variant.Build(parts);
        VesselMetrics metrics = assembly.ComputeMetrics();
        PartGraph graph = assembly.ToPartGraph();

        Assert.Equal("Apollo-Saturn V", variant.Family);
        Assert.Equal(new DateOnly(1969, 7, 16), variant.AsOfDate);
        Assert.Equal(2_941_219.9329436, metrics.WetMass, 5);
        Assert.Equal(110.7, graph.VehicleLength, 9);
        Assert.Equal(10.1, graph.MaximumDiameter, 9);
        Assert.Equal(34_046_038.80284804, metrics.SeaLevelThrust, 5);
        Assert.InRange(metrics.SeaLevelTwr, 1.179, 1.181);

        double csmMass = assembly.Parts
            .Skip(1)
            .Take(3)
            .Sum(part => WetMass(parts[part.DefinitionId]));
        Assert.Equal(28_799.94034841, csmMass, 6);
        Assert.Equal(
            21_227.45964503,
            WetMass(parts["apollo11_sla_lm5"]),
            8);
    }

    [Fact]
    public void SaturnStagesAndLesConserveThePublishedIgnitionMass()
    {
        var vessel = LoadLaunchVariant()
            .Build(LoadParts())
            .ToVessel("Apollo 11");
        double ignitionMass = vessel.TotalMass;
        string lesId = vessel.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole("launch_escape_system")).InstanceId;

        var les = Assert.IsType<Exosphere.Simulation.Vessel>(
            vessel.DeployPayload(lesId, "Apollo 11 LES"));
        var sic = Assert.IsType<Exosphere.Simulation.Vessel>(vessel.Stage());
        var sii = Assert.IsType<Exosphere.Simulation.Vessel>(vessel.Stage());
        var sivb = Assert.IsType<Exosphere.Simulation.Vessel>(vessel.Stage());

        Assert.Equal(4_041.5080167, les.TotalMass, 8);
        Assert.Equal(2_282_829.24711149, sic.TotalMass, 5);
        Assert.Equal(484_097.36406724, sii.TotalMass, 5);
        Assert.Equal(141_451.87339976, sivb.TotalMass, 5);
        Assert.Equal(28_799.94034841, vessel.TotalMass, 6);
        Assert.Equal(
            ignitionMass,
            les.TotalMass + sic.TotalMass + sii.TotalMass
                + sivb.TotalMass + vessel.TotalMass,
            5);
        Assert.DoesNotContain(vessel.Parts.Parts, part =>
            part.Definition.HasVehicleRole("csm_separation"));
    }

    [Fact]
    public void EagleClosesPublishedMassAndStagesFromDpsToAps()
    {
        PartCatalog parts = LoadParts();
        VesselAssembly assembly = LoadLmVariant().Build(parts);
        var vessel = assembly.ToVessel("Eagle");

        Assert.Equal(15_061.53464585, vessel.TotalMass, 7);
        Assert.Equal(5, vessel.Parts.Parts.Count);
        Assert.Contains(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("lunar_module_descent_engine"));
        Assert.DoesNotContain(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("lunar_module_ascent_engine"));

        const double lunarGravity = 1.625;
        double descentTwr =
            vessel.Parts.ActiveEngines.Sum(engine => engine.Definition.ThrustVac)
            / (vessel.TotalMass * lunarGravity);
        Assert.InRange(descentTwr, 1.79, 1.80);

        var descentStage = Assert.IsType<Exosphere.Simulation.Vessel>(
            vessel.Stage());

        Assert.Equal(10_243.47649171, descentStage.TotalMass, 7);
        Assert.Equal(4_818.05815414, vessel.TotalMass, 7);
        Assert.Contains(vessel.Parts.ActiveEngines, part =>
            part.Definition.HasVehicleRole("lunar_module_ascent_engine"));
        Assert.DoesNotContain(vessel.Parts.Parts, part =>
            part.Definition.HasVehicleRole("lunar_module_descent_stage"));
        Assert.Equal(
            15_061.53464585,
            descentStage.TotalMass + vessel.TotalMass,
            7);
    }

    [Fact]
    public void EnginesPreserveHistoricalClusterAndLmActuation()
    {
        EngineDefinitionCatalog engines = LoadEngines();

        Assert.Equal(5, engines.Clusters["f1-five-as506-1969"].Engines.Count);
        Assert.False(
            engines.Clusters["f1-five-as506-1969"].Engines[^1].Gimballed);
        Assert.Equal(
            34_046_038.80284804,
            engines.Clusters["f1-five-as506-1969"].Engines.Sum(mount =>
                engines.Models["f1-as506-1969"].RatedThrustSeaLevelN),
            5);
        Assert.Equal(
            5_048_731.533320667,
            engines.Clusters["j2-five-sii-as506-1969"].Engines.Sum(mount =>
                engines.Models["j2-sii-as506-1969"].RatedThrustVacuumN),
            5);
        Assert.Equal(1, engines.Models["j2-sivb-as506-1969"].RestartLimit);
        Assert.Equal(
            43_903.94734262113,
            engines.Models["lm-dps-lm5-1969"].RatedThrustVacuumN,
            8);
        Assert.Equal(
            6.0,
            engines.Models["lm-dps-lm5-1969"].GimbalRangeDeg,
            8);
        Assert.Equal(
            0.0,
            engines.Models["lm-aps-lm5-1969"].GimbalRangeDeg,
            8);
        Assert.Equal(0, engines.Models["lm-aps-lm5-1969"].RestartLimit);
    }

    [Fact]
    public void CraftV2RoundTripsLaunchStackAndOperationalEagle()
    {
        PartCatalog catalog = LoadParts();

        foreach (VehicleVariantDefinition variant in new[]
                 {
                     LoadLaunchVariant(),
                     LoadLmVariant(),
                 })
        {
            VesselAssembly original = variant.Build(catalog);
            CraftDocumentV2 document = original.ToCraftDocument(variant.Name);
            document.VehicleVariantId = variant.Id;

            string json = JsonSerializer.Serialize(document);
            CraftDocumentV2 decoded =
                JsonSerializer.Deserialize<CraftDocumentV2>(json)!;
            VesselAssembly restored =
                VesselAssembly.FromCraft(catalog, decoded);

            Assert.Equal(variant.Id, decoded.VehicleVariantId);
            Assert.Equal(original.ComputeMetrics(), restored.ComputeMetrics());
            Assert.Equal(
                original.Parts.Select(part => part.InstanceId).Order(),
                restored.Parts.Select(part => part.InstanceId).Order());
            Assert.Equal(
                original.Parts.Count - 1,
                restored.Connections.Count);
        }
    }

    [Fact]
    public void CrewAndEveryOperationalFieldAreTraceable()
    {
        string data = Path.Combine(Root.FullName, "data");
        var crew = CrewDefinition.LoadAllFromDirectory(
            Path.Combine(data, "crew"));
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(data, "provenance"));

        Assert.Equal(
            "Neil Armstrong",
            crew["neil-a-armstrong"].CreateMember().FullName);
        Assert.Equal(
            "Buzz Aldrin",
            crew["edwin-e-buzz-aldrin-jr"].CreateMember().FullName);
        Assert.Equal(
            "Michael Collins",
            crew["michael-collins"].CreateMember().FullName);

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
        foreach (string modelId in new[]
                 {
                     "f1-as506-1969",
                     "j2-sii-as506-1969",
                     "j2-sivb-as506-1969",
                     "apollo-sps-csm107-1969",
                     "lm-dps-lm5-1969",
                     "lm-aps-lm5-1969",
                 })
        {
            provenance.RequireFields(modelId, operationalFields);
        }

        provenance.RequireFields(
            "apollo11-saturn5-as506-csm107-lm5-1969-07-16",
            "completeStackIgnitionMassKg",
            "stageMassAllocation",
            "dimensionsM",
            "spacecraftIdentity",
            "csmLaunchMassKg",
            "lmLaunchMassKg");
        provenance.RequireFields(
            "apollo11-lm5-eagle-1969-07-16",
            "massClosure",
            "propulsionEnvelope",
            "stageSeparation");
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "apollo11-saturn5-as506-csm107-lm5-1969-07-16",
                "completeStackIgnitionMassKg").Status);
    }

    private static double WetMass(PartDefinition part) =>
        part.MassDry + part.FuelCapacityLF + part.FuelCapacityOx
        + part.FuelCapacitySolid + part.FuelCapacityMono;

    private static PartCatalog LoadParts() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "parts"));

    private static VehicleVariantDefinition LoadLaunchVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "vehicles",
            "apollo11_saturn5_as506_1969.json"));

    private static VehicleVariantDefinition LoadLmVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "vehicles",
            "apollo11_lm5_eagle_1969.json"));

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
