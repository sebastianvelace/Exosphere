namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class NewGlennTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void OfficialConflictsRemainExplicitPublicThrustEnvelopes()
    {
        var catalog = LoadEngineCatalog();
        var be4 = catalog.Models["be4-newglenn-public-2026"];
        var be3u = catalog.Models["be3u-newglenn-public-2026"];

        Assert.Equal(2_446_520.0, be4.PublishedThrustLowerN);
        Assert.Equal(2_846_000.0, be4.PublishedThrustUpperN);
        Assert.Equal(2_846_000.0, be4.RatedThrustSeaLevelN);
        Assert.Equal(711_715.0, be3u.PublishedThrustLowerN);
        Assert.Equal(890_000.0, be3u.PublishedThrustUpperN);
        Assert.Equal(890_000.0, be3u.RatedThrustVacuumN);
        Assert.Equal(0.70, be3u.MinimumThrottle, 12);
        Assert.Equal(0.50, be3u.PublishedMinimumThrottleLower);
        Assert.Equal(0.70, be3u.PublishedMinimumThrottleUpper);

        var provenance = LoadProvenance();
        var throttle = provenance.Require(
            be3u.Id, "minimumThrottle");
        Assert.Equal(ProvenanceStatus.Published, throttle.Status);
        Assert.Contains("50–70%", throttle.Uncertainty);
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(be4.Id, "publicThrustEnvelope").Status);
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(be3u.Id, "publicThrottleEnvelope").Status);
    }

    [Fact]
    public void PublicUpgradeVariantBuildsAFlightReadyNinetyEightMeterStack()
    {
        var variant = LoadVariant();
        var assembly = variant.Build(LoadPartCatalog());
        var metrics = assembly.ComputeMetrics();

        Assert.Equal(new DateOnly(2026, 7, 28), variant.AsOfDate);
        Assert.Equal("New Glenn", variant.Family);
        Assert.Equal(7, assembly.Parts.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.Equal(1_269_000.0, metrics.WetMass, 6);
        Assert.Equal(144_000.0, metrics.DryMass, 6);
        Assert.Equal(1_125_000.0, metrics.PropellantMass, 6);
        Assert.Equal(19_922_000.0, metrics.SeaLevelThrust, 6);
        Assert.InRange(metrics.SeaLevelTwr, 1.59, 1.62);
        Assert.InRange(metrics.VacuumDeltaV, 4_550.0, 4_650.0);

        var graph = assembly.ToPartGraph();
        Assert.Equal(98.0, graph.VehicleLength, 8);
        Assert.Equal(7.0, graph.MaximumDiameter, 8);
    }

    [Fact]
    public void EngineClustersResolveEveryFeedBranchAndStableMount()
    {
        var catalog = LoadEngineCatalog();
        var booster = catalog.Clusters["newglenn-7x2-be4-seven-2026"];
        var upper = catalog.Clusters["newglenn-7x2-be3u-two-2026"];

        Assert.Equal(7, booster.Engines.Count);
        Assert.Equal(7, booster.FeedNetwork.Branches.Count);
        Assert.Equal(2, upper.Engines.Count);
        Assert.Equal(2, upper.FeedNetwork.Branches.Count);
        Assert.All(
            booster.FeedNetwork.Branches,
            branch => Assert.Equal(1_000.0, branch.MaximumFlowKgS));
        Assert.Equal(
            booster.Engines.Count,
            booster.Engines.Select(engine => engine.InstanceId)
                .Distinct(StringComparer.Ordinal).Count());

        var parts = LoadPartCatalog();
        Assert.Equal(
            booster.Id,
            parts["be4_cluster7_newglenn_2026"]
                .ResolvedEngineCluster?.Id);
        Assert.Equal(
            upper.Id,
            parts["be3u_cluster2_newglenn_2026"]
                .ResolvedEngineCluster?.Id);
    }

    [Fact]
    public void SevenBE4RuntimeConservesMixtureAndSupportsEngineOut()
    {
        var assembly = LoadVariant().Build(LoadPartCatalog());
        var graph = assembly.ToPartGraph();
        var engine = Assert.Single(graph.ActiveEngines);
        for (int i = 0; i < 350; i++)
            engine.AdvanceEngineRuntime(1.0, 0.02);

        Assert.Equal(7, engine.EngineStates.Count);
        Assert.Equal(
            19_922_000.0,
            engine.GetThrustMagnitude(SeaLevelPressure),
            5);
        var rows = engine.GetEngineTelemetry(SeaLevelPressure).ToArray();
        Assert.All(rows, row =>
            Assert.True(row.MassFlowKgS
                <= engine.GetEngineFeedLimitKgS(
                    Array.IndexOf(rows, row))));

        double fuelBefore = graph.TotalLiquidFuel;
        double oxidizerBefore = graph.TotalOxidizer;
        graph.ConsumePropellant(0.1, SeaLevelPressure);
        double fuelUsed = fuelBefore - graph.TotalLiquidFuel;
        double oxidizerUsed = oxidizerBefore - graph.TotalOxidizer;
        Assert.Equal(3.6, oxidizerUsed / fuelUsed, 10);

        Assert.True(engine.FailEngine(
            engine.EngineStates[2].InstanceId,
            "NEWGLENN_ENGINE_OUT_TEST"));
        Assert.Equal(
            19_922_000.0 * 6.0 / 7.0,
            engine.GetThrustMagnitude(SeaLevelPressure),
            5);
    }

    [Fact]
    public void VehicleAndEstimatedAllocationsHaveMandatoryProvenance()
    {
        var provenance = LoadProvenance();
        provenance.RequireFields(
            "be4_cluster7_newglenn_2026",
            "thrust_sl", "thrust_vac", "mass_dry");
        provenance.RequireFields(
            "be3u_cluster2_newglenn_2026",
            "thrust_vac", "thrust_sl", "mass_dry");
        provenance.RequireFields(
            "newglenn_first_stage_tank",
            "propellant_mass", "mass_dry");
        provenance.RequireFields(
            "newglenn_second_stage_tank",
            "envelope", "propellant_mass", "mass_dry");
        provenance.RequireFields(
            "newglenn_fairing_7m",
            "envelope", "mass_dry");
        provenance.RequireFields(
            "newglenn-7x2-public-2026-07",
            "vehicleEnvelope");
        provenance.RequireFields(
            "cape_canaveral_lc36",
            "geodeticLocation");

        var sites = LaunchSite.LoadAllFromDirectory(
            Path.Combine(Root.FullName, "data", "launch_sites"));
        var lc36 = sites["cape_canaveral_lc36"];
        Assert.Equal(28.4709, lc36.Latitude, 4);
        Assert.Equal(-80.5418, lc36.Longitude, 4);
    }

    private static VehicleVariantDefinition LoadVariant() =>
        VehicleVariantDefinition.LoadFromJson(
            Path.Combine(
                Root.FullName,
                "data",
                "vehicles",
                "newglenn_7x2_public_2026.json"));

    private static PartCatalog LoadPartCatalog() =>
        PartCatalog.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "parts"));

    private static EngineDefinitionCatalog LoadEngineCatalog() =>
        EngineDefinitionCatalog.Load(
            Path.Combine(Root.FullName, "data", "engines"),
            Path.Combine(Root.FullName, "data", "engine_clusters"),
            LoadProvenance());

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
