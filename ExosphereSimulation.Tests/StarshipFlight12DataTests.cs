namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class StarshipFlight12DataTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void V3VariantIsDatedDistinctAndUsesTheFlownThirtyThreePlusSixConfiguration()
    {
        var variant = LoadVariant("starship_flight12_v3_2026.json");
        var flight7 = LoadVariant("starship_flight7_block2_2025.json");
        var catalog = LoadPartCatalog();
        var assembly = variant.Build(catalog);
        var metrics = assembly.ComputeMetrics();

        Assert.Equal("starship-flight-12-v3-2026-05-22", variant.Id);
        Assert.Equal(new DateOnly(2026, 5, 22), variant.AsOfDate);
        Assert.Equal("Starship", variant.Family);
        Assert.Empty(
            variant.StackTopToBottom.Intersect(flight7.StackTopToBottom));
        Assert.Empty(
            variant.EngineClusterIds.Intersect(flight7.EngineClusterIds));
        Assert.DoesNotContain("starship_landing_gear", variant.StackTopToBottom);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.Equal(5_335_000.0, metrics.WetMass, 5);
        Assert.Equal(335_000.0, metrics.DryMass, 5);
        Assert.Equal(5_000_000.0, metrics.PropellantMass, 5);
        Assert.Equal(125.5, assembly.ToPartGraph().VehicleLength, 8);

        Assert.All(assembly.Parts, part =>
            Assert.True(catalog[part.DefinitionId].IsStarshipFamily));
        Assert.Equal(
            ["command", "tank", "ship_engines", "hotstage", "booster"],
            assembly.Parts.Select(part => catalog[part.DefinitionId].VehicleRole));
    }

    [Fact]
    public void Raptor3ClustersKeepEveryEngineIndependentAndSupportEngineOut()
    {
        var catalog = LoadPartCatalog();
        var booster = new Part(
            catalog["super_heavy_v3_booster_flight12"], "flight12-booster");
        var ship = new Part(
            catalog["starship_v3_engines_flight12"], "flight12-ship");

        Assert.Equal(33, booster.EngineStates.Count);
        Assert.Equal(33, booster.Definition.ResolvedEngineCluster?.Engines.Count);
        Assert.Equal(33, booster.Definition.ResolvedEngineCluster
            ?.FeedNetwork.Branches.Count);
        Assert.Equal(6, ship.EngineStates.Count);
        Assert.Equal(3, ship.EngineStates.Count(state =>
            state.EngineModelId == "raptor3-starship-sl-flight12"));
        Assert.Equal(3, ship.EngineStates.Count(state =>
            state.EngineModelId == "raptor3-starship-vac-flight12"));

        for (int i = 0; i < 250; i++)
        {
            booster.AdvanceEngineRuntime(1.0, 0.02);
            ship.AdvanceEngineRuntime(1.0, 0.02);
        }
        Assert.Equal(92_400_000.0,
            booster.GetThrustMagnitude(SeaLevelPressure), 3);
        Assert.Equal(15_300_000.0,
            ship.GetThrustMagnitude(SeaLevelPressure), 3);
        Assert.Equal(18_600_000.0, ship.GetThrustMagnitude(0.0), 3);

        Assert.True(booster.FailEngine(
            booster.EngineStates[12].InstanceId, "FLIGHT12_ENGINE_OUT_TEST"));
        Assert.Equal(32, booster.GetEngineTelemetry(SeaLevelPressure)
            .Count(row => row.ThrustN > 0.0));
        Assert.Equal(92_400_000.0 * 32.0 / 33.0,
            booster.GetThrustMagnitude(SeaLevelPressure), 3);
    }

    [Fact]
    public void PublicMissionFactsEstimatesAndRegulatoryEnvelopeStaySeparated()
    {
        var provenance = LoadProvenance();
        foreach (string modelId in new[]
                 {
                     "raptor3-superheavy-flight12",
                     "raptor3-starship-sl-flight12",
                     "raptor3-starship-vac-flight12",
                 })
        {
            provenance.RequireFields(
                modelId,
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
                "shutdownTransient");
            Assert.Equal(
                ProvenanceStatus.Estimated,
                provenance.Require(modelId, "ratedThrustVacuumN").Status);
        }

        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "super_heavy_v3_booster_flight12",
                "engineConfiguration").Status);
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "starship-flight-12-v3-2026-05-22",
                "missionIdentity").Status);
        Assert.Equal(
            ProvenanceStatus.Estimated,
            provenance.Require(
                "starship-flight-12-v3-2026-05-22",
                "vehicleEnvelope").Status);
        Assert.Equal(
            ProvenanceStatus.RegulatoryEnvelope,
            provenance.Require(
                "starship-ksc39a-future-envelope",
                "engineAndThrustEnvelope").Status);
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require("starbase_pad2", "missionUse").Status);
        Assert.Equal(
            ProvenanceStatus.Estimated,
            provenance.Require("starbase_pad2", "geodeticLocation").Status);
    }

    [Fact]
    public void Flight7PhysicalBaselineRemainsUnchanged()
    {
        var assembly = LoadVariant("starship_flight7_block2_2025.json")
            .Build(LoadPartCatalog());
        var metrics = assembly.ComputeMetrics();

        Assert.Equal(4_800_000.0, metrics.WetMass, 5);
        Assert.Equal(300_000.0, metrics.DryMass, 5);
        Assert.Equal(4_500_000.0, metrics.PropellantMass, 5);
        Assert.Equal(123.1, assembly.ToPartGraph().VehicleLength, 8);
        Assert.Equal(
            "raptor2-superheavy-flight7",
            LoadPartCatalog()["super_heavy_booster"].EngineModelId);
    }

    private static VehicleVariantDefinition LoadVariant(string fileName) =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "vehicles", fileName));

    private static PartCatalog LoadPartCatalog() =>
        PartCatalog.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "parts"));

    private static DataProvenanceRegistry LoadProvenance() =>
        DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));

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
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
