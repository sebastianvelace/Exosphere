namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class StarshipFlight7DataTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void HistoricalVariantIsDatedAndDoesNotIncludeFictionalLandingGear()
    {
        var variant = LoadVariant();
        var assembly = variant.Build(LoadPartCatalog());
        var metrics = assembly.ComputeMetrics();

        Assert.Equal("starship-flight-7-block-2-2025-01-16", variant.Id);
        Assert.Equal(new DateOnly(2025, 1, 16), variant.AsOfDate);
        Assert.Equal("Starship", variant.Family);
        Assert.DoesNotContain("starship_landing_gear", variant.StackTopToBottom);
        Assert.Equal(5, assembly.Parts.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.Equal(4_800_000.0, metrics.WetMass, 5);
        Assert.Equal(300_000.0, metrics.DryMass, 5);
        Assert.Equal(4_500_000.0, metrics.PropellantMass, 5);

        var graph = assembly.ToPartGraph();
        Assert.Equal(123.1, graph.VehicleLength, 8);
        Assert.Equal(9.0, graph.MaximumDiameter, 8);
    }

    [Fact]
    public void ShipClusterKeepsSeaLevelAndVacuumRaptorsDistinct()
    {
        var engines = LoadEngineCatalog();
        var cluster = engines.Clusters["starship-flight7-ship-six"];

        Assert.Equal(6, cluster.Engines.Count);
        Assert.Equal(3, cluster.Engines.Count(mount =>
            mount.EngineModelId == "raptor2-starship-sl-flight7"));
        Assert.Equal(3, cluster.Engines.Count(mount =>
            mount.EngineModelId == "raptor2-starship-vac-flight7"));
        Assert.All(cluster.Engines.Take(3), mount => Assert.True(mount.Gimballed));
        Assert.All(cluster.Engines.Skip(3), mount => Assert.False(mount.Gimballed));

        var part = new Part(LoadPartCatalog()["starship_engines"], "ship-engine-part");
        Assert.Equal(
            new[]
            {
                "raptor2-starship-sl-flight7",
                "raptor2-starship-sl-flight7",
                "raptor2-starship-sl-flight7",
                "raptor2-starship-vac-flight7",
                "raptor2-starship-vac-flight7",
                "raptor2-starship-vac-flight7",
            },
            part.EngineStates.Select(state => state.EngineModelId));

        for (int i = 0; i < 250; i++)
            part.AdvanceEngineRuntime(1.0, 0.02);
        Assert.Equal(11_000_000.0, part.GetThrustMagnitude(SeaLevelPressure), 4);
        Assert.Equal(13_500_000.0, part.GetThrustMagnitude(0.0), 4);

        part.SelectEngineCount(3);
        for (int i = 0; i < 100; i++)
            part.AdvanceEngineRuntime(1.0, 0.02);
        Assert.Equal(6_000_000.0, part.GetThrustMagnitude(SeaLevelPressure), 4);
        Assert.All(part.EngineStates.Skip(3), state =>
            Assert.Equal(0.0, state.CommandedThrottle));
    }

    [Fact]
    public void CenterRaptorsSupportAscentInsertionDeorbitAndLandingSequence()
    {
        var part = new Part(
            LoadPartCatalog()["starship_engines"],
            "ship-engine-restart-sequence");

        // The simulator's Flight 7 profile has four distinct starts: hot-stage ascent,
        // post-coast insertion, deorbit and the landing burn. Vacuum Raptors are not
        // selected for landing; the three gimballed sea-level engines must remain usable.
        RunEngineCycle(part, selectedEngines: 6);
        RunEngineCycle(part, selectedEngines: 6);
        RunEngineCycle(part, selectedEngines: 6);

        part.SelectEngineCount(3);
        Advance(part, 1.0, 100);

        Assert.All(part.EngineStates.Take(3), state =>
        {
            Assert.Equal(EngineLifecycleState.Running, state.State);
            Assert.Equal(4, state.StartsCompleted);
            Assert.Null(state.FailureCode);
            Assert.True(state.ChamberPressureFraction > 0.99);
        });
        Assert.Equal(6_000_000.0,
            part.GetThrustMagnitude(SeaLevelPressure), 4);
    }

    [Fact]
    public void BoosterRuntimeRepresentsAllThirtyThreeMountsAndEngineOut()
    {
        var catalog = LoadPartCatalog();
        var booster = new Part(catalog["super_heavy_booster"], "booster-14");

        Assert.Equal(33, booster.EngineStates.Count);
        Assert.Equal(33, booster.Definition.ResolvedEngineCluster?.Engines.Count);
        Assert.Equal(33, booster.Definition.ResolvedEngineCluster
            ?.FeedNetwork.Branches.Count);
        Assert.Equal(20, booster.Definition.ResolvedEngineCluster
            ?.Engines.Count(mount => !mount.Gimballed));

        for (int i = 0; i < 250; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        Assert.Equal(74_400_000.0,
            booster.GetThrustMagnitude(SeaLevelPressure), 3);

        Assert.True(booster.FailEngine(
            booster.EngineStates[7].InstanceId,
            "FLIGHT7_ENGINE_OUT_TEST"));
        Assert.Equal(32, booster.GetEngineTelemetry(SeaLevelPressure)
            .Count(row => row.ThrustN > 0.0));
        Assert.Equal(74_400_000.0 * 32.0 / 33.0,
            booster.GetThrustMagnitude(SeaLevelPressure), 3);
    }

    [Fact]
    public void BoosterEngineTelemetryReportsThirtyThreeLitAfterStartupWithoutFailures()
    {
        var catalog = LoadPartCatalog();
        var booster = new Part(catalog["super_heavy_booster"], "booster-hud-telemetry");
        var graph = new PartGraph();
        graph.SetRoot(booster);

        for (int i = 0; i < 100; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);

        Assert.Equal(33, graph.ActiveEngineCount);
        Assert.Equal(33, graph.GetEngineReadouts(101_325.0)
            .Count(row => row.Throttle > 0.99 && row.FailureCode == null));
        Assert.DoesNotContain(graph.GetEngineReadouts(101_325.0),
            row => row.FailureCode != null);
    }

    [Fact]
    public void BoosterEngineOutProducesAsymmetricTorque_NotJustProportionalThrustLoss()
    {
        var catalog = LoadPartCatalog();
        var booster = new Part(catalog["super_heavy_booster"], "booster-torque-14");
        var graph = new PartGraph();
        graph.SetRoot(booster);

        for (int i = 0; i < 250; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        Assert.Equal(0.0, graph.GetTotalTorque(SeaLevelPressure).Z, 3);

        Assert.True(booster.FailEngine(
            booster.EngineStates[7].InstanceId,
            "FLIGHT7_ENGINE_OUT_TEST"));

        // The scalar-lever approximation only reduces total thrust proportionally and
        // reports zero torque; the real per-mount geometry must show a nonzero moment
        // once an off-axis engine drops out.
        var torque = graph.GetTotalTorque(SeaLevelPressure);
        Assert.False(torque.X == 0.0 && torque.Y == 0.0 && torque.Z == 0.0);
    }

    [Fact]
    public void FlightConfigurationAndEveryOperationalModelFieldAreTraced()
    {
        var provenance = LoadProvenance();
        foreach (string modelId in new[]
                 {
                     "raptor2-superheavy-flight7",
                     "raptor2-starship-sl-flight7",
                     "raptor2-starship-vac-flight7",
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
        }

        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "super_heavy_booster", "engineConfiguration").Status);
        Assert.Equal(
            ProvenanceStatus.Estimated,
            provenance.Require(
                "starship-flight-7-block-2-2025-01-16",
                "vehicleEnvelope").Status);
        provenance.RequireFields(
            "starship-flight-7-block-2-2025-01-16",
            "missionIdentity", "vehicleEnvelope");
        provenance.RequireFields("starbase", "flight7MissionUse");
    }

    private static VehicleVariantDefinition LoadVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName,
            "data",
            "vehicles",
            "starship_flight7_block2_2025.json"));

    private static void RunEngineCycle(Part part, int selectedEngines)
    {
        part.SelectEngineCount(selectedEngines);
        Advance(part, 1.0, 100);
        Advance(part, 0.0, 100);
    }

    private static void Advance(Part part, double throttle, int steps)
    {
        for (int i = 0; i < steps; i++)
            part.AdvanceEngineRuntime(throttle, 0.02);
    }

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
