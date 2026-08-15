namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class AdvancedEnginePhysicsTests
{
    private const double SeaLevelPressure = 101_325.0;
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void PerformanceMap_InterpolatesPressureAndThrottle()
    {
        var model = SyntheticModel();
        model.PerformanceMap =
        [
            new() { AmbientPressurePa = 0, Throttle = 0.5, ThrustN = 60, SpecificImpulseS = 300 },
            new() { AmbientPressurePa = 100, Throttle = 0.5, ThrustN = 50, SpecificImpulseS = 280 },
            new() { AmbientPressurePa = 0, Throttle = 1.0, ThrustN = 140, SpecificImpulseS = 320 },
            new() { AmbientPressurePa = 100, Throttle = 1.0, ThrustN = 100, SpecificImpulseS = 300 },
        ];

        var sample = EnginePerformanceEvaluator.Evaluate(model, 50, 0.75);

        Assert.Equal(87.5, sample.ThrustN, 12);
        Assert.Equal(300.0, sample.SpecificImpulseS, 12);
        Assert.Equal(
            sample.ThrustN / (sample.SpecificImpulseS * 9.80665),
            sample.MassFlowKgS,
            12);
    }

    [Fact]
    public void PerformanceMap_ReusesNoManagedObjectsPerSample()
    {
        var model = SyntheticModel();
        model.PerformanceMap =
        [
            new() { AmbientPressurePa = 0, Throttle = 0.5, ThrustN = 60, SpecificImpulseS = 300 },
            new() { AmbientPressurePa = 100, Throttle = 0.5, ThrustN = 50, SpecificImpulseS = 280 },
            new() { AmbientPressurePa = 0, Throttle = 1.0, ThrustN = 140, SpecificImpulseS = 320 },
            new() { AmbientPressurePa = 100, Throttle = 1.0, ThrustN = 100, SpecificImpulseS = 300 },
        ];

        for (int i = 0; i < 32; i++)
            EnginePerformanceEvaluator.Evaluate(model, 50.0, 0.75);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        const int samples = 1_000;
        for (int i = 0; i < samples; i++)
            EnginePerformanceEvaluator.Evaluate(model, 50.0, 0.75);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0L, allocatedBytes);
    }

    [Fact]
    public void NozzleEquation_UsesMomentumAndPressureTermsWhenComplete()
    {
        var model = SyntheticModel();
        model.PerformanceMap.Clear();
        model.ExitAreaM2 = 2.0;
        model.NominalMassFlowKgS = 10.0;
        model.EffectiveExhaustVelocityMps = 3_000.0;
        model.EffectiveExitPressurePa = 20_000.0;

        var sample = EnginePerformanceEvaluator.Evaluate(model, 5_000.0, 0.6);

        double expectedFlow = 6.0;
        double expectedThrust =
            expectedFlow * 3_000.0 + 2.0 * (20_000.0 - 5_000.0) * 0.6;
        Assert.Equal(expectedFlow, sample.MassFlowKgS, 12);
        Assert.Equal(expectedThrust, sample.ThrustN, 12);
        Assert.Equal(
            expectedThrust / (expectedFlow * 9.80665),
            sample.SpecificImpulseS,
            12);
    }

    [Fact]
    public void FeedBranchLimit_FailsOnlyTheRestrictedEngine()
    {
        var definition = LoadCatalog()["merlin1d_cluster9_block5"];
        var source = definition.ResolvedEngineCluster!;
        definition.ResolvedEngineCluster = new EngineClusterDefinition
        {
            Id = source.Id,
            Name = source.Name,
            EngineModelId = source.EngineModelId,
            Engines = source.Engines,
            FeedNetwork = new FeedNetwork
            {
                FuelResource = source.FeedNetwork.FuelResource,
                OxidizerResource = source.FeedNetwork.OxidizerResource,
                CrossfeedEnabled = true,
                Branches = source.FeedNetwork.Branches
                    .Select((branch, index) => new FeedBranch
                    {
                        EngineInstanceId = branch.EngineInstanceId,
                        MaximumFlowKgS = index == 4
                            ? 1.0
                            : branch.MaximumFlowKgS,
                    })
                    .ToList(),
            },
        };
        var engine = new Part(definition, "limited-octaweb");
        RunToRated(engine);
        var graph = BuildFueledGraph(engine, 10_000, 30_000);

        graph.ConsumePropellant(0.02, SeaLevelPressure);

        var failed = Assert.Single(engine.EngineStates,
            state => state.FailureCode == "FEED_BRANCH_FLOW_LIMIT");
        Assert.Equal(engine.EngineStates[4].InstanceId, failed.InstanceId);
        Assert.Equal(8, engine.EngineStates.Count(
            state => state.State == EngineLifecycleState.Running));
    }

    [Fact]
    public void StarvationFundsBranchesDeterministicallyAndConservesPropellant()
    {
        var engine = new Part(
            LoadCatalog()["merlin1d_cluster9_block5"],
            "starved-octaweb");
        RunToRated(engine);
        double oneEngineFlow = engine
            .GetEngineTelemetry(SeaLevelPressure)
            .First()
            .MassFlowKgS;
        const double dt = 0.1;
        double fuelFraction = 1.0 / (1.0 + engine.Definition.MixtureRatio);
        double fuel = oneEngineFlow * fuelFraction * dt * 1.1;
        double oxidizer = oneEngineFlow * (1.0 - fuelFraction) * dt * 1.1;
        var graph = BuildFueledGraph(engine, fuel, oxidizer);
        double before = graph.TotalLiquidFuel + graph.TotalOxidizer;

        graph.ConsumePropellant(dt, SeaLevelPressure);

        Assert.Equal(1, engine.EngineStates.Count(
            state => state.State == EngineLifecycleState.Running));
        Assert.Equal(8, engine.EngineStates.Count(
            state => state.FailureCode == "PROPELLANT_STARVATION"));
        double consumed = before - graph.TotalLiquidFuel - graph.TotalOxidizer;
        Assert.Equal(oneEngineFlow * dt, consumed, 8);
        Assert.All(graph.Parts, part =>
        {
            Assert.True(part.LiquidFuel >= -1e-9);
            Assert.True(part.Oxidizer >= -1e-9);
        });
    }

    [Fact]
    public void ChamberAndGimbalRespectTransientRateAndAccelerationLimits()
    {
        var engine = new Part(
            LoadCatalog()["merlin1d_cluster9_block5"],
            "dynamic-octaweb")
        {
            GimbalOffset = new(1.0, 0.0, 0.0),
        };

        engine.AdvanceEngineRuntime(1.0, 0.1);
        var first = engine.EngineStates[0];
        Assert.Equal(0.18, first.GimbalDeg.X, 12);
        Assert.Equal(1.8, first.GimbalVelocityDegPerS.X, 12);
        Assert.Equal(0.0, first.ChamberPressureFraction);

        for (int i = 0; i < 6; i++)
            engine.AdvanceEngineRuntime(1.0, 0.1);
        Assert.True(first.ActualThrottle > first.ChamberPressureFraction);

        for (int i = 0; i < 24; i++)
            engine.AdvanceEngineRuntime(1.0, 0.1);

        Assert.Equal(5.0, first.GimbalDeg.X, 12);
        Assert.Equal(1.0, first.ChamberPressureFraction, 12);
        Assert.Equal(1.0, engine.ThrottleLevel, 12);
    }

    private static PartGraph BuildFueledGraph(
        Part engine,
        double fuel,
        double oxidizer)
    {
        var tank = new Part(new PartDefinition
        {
            Id = "test-tank",
            Name = "Test Tank",
            CategoryStr = "fuel_tank",
            MassDry = 1.0,
        })
        {
            LiquidFuel = fuel,
            Oxidizer = oxidizer,
        };
        var graph = new PartGraph();
        graph.SetRoot(tank);
        graph.AddPart(engine);
        return graph;
    }

    private static void RunToRated(Part engine)
    {
        for (int i = 0; i < 150; i++)
            engine.AdvanceEngineRuntime(1.0, 0.02);
        Assert.All(engine.EngineStates,
            state => Assert.Equal(EngineLifecycleState.Running, state.State));
    }

    private static EngineModelDefinition SyntheticModel() => new()
    {
        Id = "synthetic",
        Name = "Synthetic",
        Variant = "test",
        AsOfDate = new DateOnly(2026, 7, 28),
        Fuel = "fuel",
        Oxidizer = "oxidizer",
        MixtureRatioOxidizerToFuel = 2.0,
        RatedThrustSeaLevelN = 100.0,
        RatedThrustVacuumN = 140.0,
        SpecificImpulseSeaLevelS = 300.0,
        SpecificImpulseVacuumS = 320.0,
        MinimumThrottle = 0.0,
        MaximumThrottle = 1.0,
        StartupSeconds = 1.0,
        ShutdownSeconds = 1.0,
    };

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "parts"));

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
