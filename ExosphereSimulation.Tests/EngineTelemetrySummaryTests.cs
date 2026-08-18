namespace ExosphereSimulation.Tests;

using System.Diagnostics;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Xunit;
using Xunit.Abstractions;

public sealed class EngineTelemetrySummaryTests
{
    private const double SeaLevelPressure = 101_325.0;
    private readonly ITestOutputHelper _output;

    public EngineTelemetrySummaryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RuntimeSummaryMatchesLegacyAggregatesAndCachedFill()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var booster = new Part(catalog["super_heavy_booster"], "summary-runtime-booster");
        var graph = new PartGraph();
        graph.SetRoot(booster);

        for (int i = 0; i < 100; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);

        var rows = new List<EngineReadout>(33);
        graph.FillEngineReadouts(SeaLevelPressure, rows, out var first);

        Assert.Equal(33, first.NominalEngineCount);
        Assert.Equal(33, first.ReadoutEngineCount);
        Assert.Equal(graph.GetCurrentThrust(SeaLevelPressure), first.ThrustN, 8);
        Assert.Equal(graph.GetCurrentMassFlow(SeaLevelPressure), first.MassFlowKgS, 8);
        Assert.Equal(graph.GetCurrentIsp(SeaLevelPressure), first.EffectiveIspSeconds, 8);

        graph.FillEngineReadouts(SeaLevelPressure, rows, out var cached);
        Assert.Equal(first, cached);
        Assert.Equal(33, rows.Count);
        Assert.All(rows, row => Assert.True(row.Throttle > 0.99));

        Assert.True(booster.FailEngine(
            booster.EngineStates[7].InstanceId,
            "SUMMARY_ENGINE_OUT"));
        graph.FillEngineReadouts(SeaLevelPressure, rows, out var afterFailure);

        Assert.Equal(33, afterFailure.NominalEngineCount);
        Assert.Equal(33, afterFailure.ReadoutEngineCount);
        Assert.Equal(graph.GetCurrentThrust(SeaLevelPressure), afterFailure.ThrustN, 8);
        Assert.Equal(graph.GetCurrentMassFlow(SeaLevelPressure), afterFailure.MassFlowKgS, 8);
        Assert.Equal(32, graph.ActiveEngineCount);
        Assert.Contains(rows, row => row.FailureCode == "SUMMARY_ENGINE_OUT");
    }

    [Fact]
    public void AggregateEngineSummaryPreservesDeclaredClusterCount()
    {
        var definitions = PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var engine = new Part(definitions["starship_engines"], "summary-aggregate-engine")
        {
            ThrottleLevel = 1.0,
        };
        engine.SelectEngineCount(3);

        var graph = new PartGraph();
        graph.SetRoot(engine);
        var rows = new List<EngineReadout>(1);
        graph.FillEngineReadouts(0.0, rows, out var summary);

        Assert.Equal(6, summary.NominalEngineCount);
        Assert.Equal(1, summary.ReadoutEngineCount);
        Assert.Single(rows);
        Assert.Equal(graph.GetCurrentThrust(0.0), summary.ThrustN, 8);
        Assert.Equal(graph.GetCurrentMassFlow(0.0), summary.MassFlowKgS, 8);
        Assert.Equal(3, engine.SelectedEngineCount);
    }

    [Fact]
    public void BatchSummaryAvoidsRepeatedAggregateEvaluation()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var booster = new Part(catalog["super_heavy_booster"], "summary-benchmark-booster");
        var graph = new PartGraph();
        graph.SetRoot(booster);
        for (int i = 0; i < 100; i++)
            booster.AdvanceEngineRuntime(1.0, 0.02);

        var rows = new List<EngineReadout>(33);
        for (int i = 0; i < 64; i++)
            graph.FillEngineReadouts(SeaLevelPressure, rows);

        const int samples = 2_000;
        double legacyThrust = 0.0;
        var legacyTimer = Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            graph.FillEngineReadouts(SeaLevelPressure, rows);
            legacyThrust += graph.GetCurrentThrust(SeaLevelPressure);
            _ = graph.GetCurrentMassFlow(SeaLevelPressure);
            _ = graph.GetCurrentIsp(SeaLevelPressure);
        }
        legacyTimer.Stop();

        double batchThrust = 0.0;
        var batchTimer = Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            graph.FillEngineReadouts(SeaLevelPressure, rows, out var summary);
            batchThrust += summary.ThrustN;
            _ = summary.MassFlowKgS;
            _ = summary.EffectiveIspSeconds;
        }
        batchTimer.Stop();

        Assert.Equal(legacyThrust, batchThrust, 8);
        Assert.All(rows, row => Assert.True(double.IsFinite(row.ThrustN)));
        double reduction = 100.0 * (1.0 - batchTimer.Elapsed.TotalMilliseconds /
            System.Math.Max(legacyTimer.Elapsed.TotalMilliseconds, 1e-9));
        _output.WriteLine(
            $"EngineTelemetryBatch: samples={samples}; "
            + $"legacy={legacyTimer.Elapsed.TotalMilliseconds:F3} ms; "
            + $"batch={batchTimer.Elapsed.TotalMilliseconds:F3} ms; "
            + $"reduction={reduction:F2}%");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
