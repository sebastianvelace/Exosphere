namespace ExosphereSimulation.Tests;

using System.Diagnostics;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Regression coverage for the CPU hot paths exercised by a Flight 7-class Starship stack.
/// The budget is deliberately generous for CI/llvmpipe hosts; the timing is diagnostic, not
/// a replacement for a profiler on the target machine.
/// </summary>
public sealed class StarshipPerformanceRegressionTests
{
    private const double TickDt = 0.02;
    private readonly ITestOutputHelper _output;

    public StarshipPerformanceRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Flight7TickHotPathStaysFiniteAndWithinDiagnosticBudget()
    {
        var earth = LoadBody("earth");
        var vessel = BuildFlight7Stack();
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        vessel.PitchYawRoll = new Vector3d(0.35, -0.2, 0.15);
        vessel.Throttle = 1.0;

        for (int i = 0; i < 100; i++)
            vessel.Tick(TickDt, earth);

        const int measuredTicks = 500;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < measuredTicks; i++)
            vessel.Tick(TickDt, earth);
        stopwatch.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(double.IsFinite(vessel.TotalMass) && vessel.TotalMass > 0.0);
        Assert.True(double.IsFinite(vessel.AngularVelocity.X));
        Assert.True(double.IsFinite(vessel.AngularVelocity.Y));
        Assert.True(double.IsFinite(vessel.AngularVelocity.Z));
        Assert.All(vessel.Parts.Parts, part =>
        {
            Assert.True(double.IsFinite(part.LiquidFuel));
            Assert.True(double.IsFinite(part.Oxidizer));
        });

        double millisecondsPerTick = stopwatch.Elapsed.TotalMilliseconds / measuredTicks;
        double allocatedBytesPerTick = allocatedBytes / (double)measuredTicks;
        _output.WriteLine(
            $"Flight7Tick: {measuredTicks} ticks in {stopwatch.Elapsed.TotalMilliseconds:F3} ms; "
            + $"{millisecondsPerTick:F6} ms/tick; engines={vessel.ActiveEngineCount}; "
            + $"managedAlloc={allocatedBytes:N0} bytes; "
            + $"managedAllocPerTick={allocatedBytesPerTick:N2} bytes/tick");

        Assert.InRange(millisecondsPerTick, 0.0, 25.0);
        // This is deliberately an allocation budget, not a machine-specific time budget.
        // The previous LINQ-heavy path measured ~5.32 KiB/tick; keep the optimized path
        // below 5 KiB so a new per-tick iterator/list cannot silently return.
        Assert.InRange(allocatedBytesPerTick, 0.0, 5_000.0);
    }

    [Fact]
    public void IdenticalFlight7TicksRemainDeterministic()
    {
        var earth = LoadBody("earth");
        var left = BuildFlight7Stack();
        var right = BuildFlight7Stack();
        left.Position = right.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        left.PitchYawRoll = right.PitchYawRoll = new Vector3d(0.35, -0.2, 0.15);
        left.Throttle = right.Throttle = 1.0;

        for (int i = 0; i < 250; i++)
        {
            left.Tick(TickDt, earth);
            right.Tick(TickDt, earth);
        }

        Assert.Equal(left.AngularVelocity, right.AngularVelocity);
        Assert.Equal(left.Orientation, right.Orientation);
        Assert.Equal(left.TotalMass, right.TotalMass, 12);
        Assert.Equal(left.Parts.Parts.Count, right.Parts.Parts.Count);
        for (int i = 0; i < left.Parts.Parts.Count; i++)
        {
            var a = left.Parts.Parts[i];
            var b = right.Parts.Parts[i];
            Assert.Equal(a.LiquidFuel, b.LiquidFuel, 12);
            Assert.Equal(a.Oxidizer, b.Oxidizer, 12);
            Assert.Equal(a.ThrottleLevel, b.ThrottleLevel, 12);
            Assert.Equal(a.EngineStates.Count, b.EngineStates.Count);
            for (int j = 0; j < a.EngineStates.Count; j++)
            {
                Assert.Equal(a.EngineStates[j].State, b.EngineStates[j].State);
                Assert.Equal(a.EngineStates[j].ChamberPressureFraction,
                    b.EngineStates[j].ChamberPressureFraction, 12);
                Assert.Equal(a.EngineStates[j].GimbalDeg, b.EngineStates[j].GimbalDeg);
            }
        }
    }

    [Fact]
    public void TickCachesFollowHotStageAndMechanicalStageTransitions()
    {
        var earth = LoadBody("earth");
        var vessel = BuildFlight7Stack();
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        vessel.Throttle = 1.0;

        vessel.BeginHotStageOverlap(0.08);
        vessel.Tick(TickDt, earth);
        Assert.Equal(39, vessel.ActiveEngineCount);

        for (int i = 0; i < 8; i++)
            vessel.Tick(TickDt, earth);
        Assert.Equal(33, vessel.ActiveEngineCount);

        var detached = vessel.Stage();
        Assert.NotNull(detached);
        Assert.Equal(6, vessel.ActiveEngineCount);
        vessel.Tick(TickDt, earth);
        Assert.Equal(6, vessel.ActiveEngineCount);
        Assert.True(double.IsFinite(vessel.TotalMass) && vessel.TotalMass > 0.0);
    }

    [Fact]
    public void HotStagePropellantPoolStaysWithinAllocationBudget()
    {
        var earth = LoadBody("earth");
        var vessel = BuildFlight7Stack();
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        vessel.Throttle = 1.0;
        vessel.BeginHotStageOverlap(10.0);

        for (int i = 0; i < 32; i++)
            vessel.Tick(TickDt, earth);

        Assert.True(vessel.IsHotStageOverlapping);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int measuredTicks = 128;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
            vessel.Tick(TickDt, earth);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double allocatedBytesPerTick = allocatedBytes / (double)measuredTicks;

        _output.WriteLine(
            $"HotStageTick: {measuredTicks} ticks; "
            + $"managedAllocPerTick={allocatedBytesPerTick:F2} bytes/tick");
        Assert.InRange(allocatedBytesPerTick, 0.0, 800.0);
        Assert.True(double.IsFinite(vessel.TotalMass) && vessel.TotalMass > 0.0);
    }

    [Fact]
    public void RuntimeFlight7TickReportsEnginePerformanceAllocationCost()
    {
        var earth = LoadBody("earth");
        var vessel = BuildRuntimeFlight7Stack();
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        vessel.Throttle = 1.0;

        for (int i = 0; i < 32; i++)
            vessel.Tick(TickDt, earth);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        const int measuredTicks = 128;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredTicks; i++)
            vessel.Tick(TickDt, earth);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double allocatedBytesPerTick = allocatedBytes / (double)measuredTicks;

        _output.WriteLine(
            $"RuntimeFlight7Tick: {measuredTicks} ticks; "
            + $"managedAllocPerTick={allocatedBytesPerTick:F2} bytes/tick; "
            + $"engines={vessel.ActiveEngineCount}");
        Assert.InRange(allocatedBytesPerTick, 0.0, 1_000.0);
        Assert.True(double.IsFinite(vessel.TotalMass) && vessel.TotalMass > 0.0);
    }

    [Fact]
    public void RuntimeFlight7AllocationBreakdownReportsControlHotPaths()
    {
        var earth = LoadBody("earth");
        foreach (var scenario in new[]
        {
            (Name: "engines_off", Throttle: 0.0, Input: Vector3d.Zero),
            (Name: "engines_on", Throttle: 1.0, Input: Vector3d.Zero),
            (Name: "engines_on_tvc", Throttle: 1.0, Input: new Vector3d(0.2, -0.1, 0.15)),
        })
        {
            var vessel = BuildRuntimeFlight7Stack();
            vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
            vessel.Throttle = scenario.Throttle;
            vessel.PitchYawRoll = scenario.Input;
            for (int i = 0; i < 32; i++)
                vessel.Tick(TickDt, earth);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            const int measuredTicks = 128;
            for (int i = 0; i < measuredTicks; i++)
                vessel.Tick(TickDt, earth);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _output.WriteLine(
                $"RuntimeFlight7Breakdown: {scenario.Name}; "
                + $"managedAllocPerTick={allocatedBytes / (double)measuredTicks:F2}");
            Assert.InRange(allocatedBytes / (double)measuredTicks, 0.0, 1_000.0);
        }
    }

    [Fact]
    public void AllocationAudit_Flight7EngineTelemetryBufferReportsManagedCost()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var booster = new Part(catalog["super_heavy_booster"], "allocation-audit-booster");
        var graph = new PartGraph();
        graph.SetRoot(booster);
        for (int i = 0; i < 100; i++)
            booster.AdvanceEngineRuntime(1.0, TickDt);

        var buffer = new List<EngineReadout>(33);
        for (int i = 0; i < 100; i++)
            graph.FillEngineReadouts(101_325.0, buffer);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        const int samples = 1_000;
        for (int i = 0; i < samples; i++)
            graph.FillEngineReadouts(101_325.0, buffer);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(33, buffer.Count);
        Assert.All(buffer, row => Assert.True(row.Throttle > 0.99));
        double allocatedBytesPerSample = allocatedBytes / (double)samples;
        _output.WriteLine(
            $"AllocationAudit EngineTelemetryFill: {samples} samples; "
            + $"managedAlloc={allocatedBytes:N0} bytes; "
            + $"managedAllocPerSample={allocatedBytesPerSample:F2} bytes/sample");
        Assert.InRange(allocatedBytesPerSample, 0.0, 1_000.0);
    }

    [Fact]
    public void AllocationAudit_Flight7EngineTelemetryCacheInvalidatesAfterEngineFailure()
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var booster = new Part(catalog["super_heavy_booster"], "allocation-audit-failure");
        var graph = new PartGraph();
        graph.SetRoot(booster);
        for (int i = 0; i < 100; i++)
            booster.AdvanceEngineRuntime(1.0, TickDt);

        var buffer = new List<EngineReadout>(33);
        graph.FillEngineReadouts(101_325.0, buffer);
        Assert.DoesNotContain(buffer, row => row.FailureCode != null);

        string failedInstanceId = booster.EngineStates[7].InstanceId;
        Assert.True(booster.FailEngine(failedInstanceId, "ALLOCATION_AUDIT_ENGINE_OUT"));
        graph.FillEngineReadouts(101_325.0, buffer);

        var failed = Assert.Single(buffer, row => row.InstanceId == failedInstanceId);
        Assert.Equal(EngineLifecycleState.Failed, failed.State);
        Assert.Equal("ALLOCATION_AUDIT_ENGINE_OUT", failed.FailureCode);
        Assert.Equal(32, graph.ActiveEngineCount);
    }

    private static Vessel BuildFlight7Stack()
    {
        var root = FindRepoRoot();
        var defs = PartDefinition.LoadAllFromDirectory(Path.Combine(root.FullName, "data", "parts"));
        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);

        var vessel = new Vessel("perf-flight7") { Name = "Performance Flight 7" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return vessel;
    }

    private static Vessel BuildRuntimeFlight7Stack()
    {
        var root = FindRepoRoot();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root.FullName, "data", "parts"));
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        var ring = new Part(catalog["decoupler_heavy"]);
        var booster = new Part(catalog["super_heavy_booster"]);

        var vessel = new Vessel("perf-runtime-flight7") { Name = "Runtime Performance Flight 7" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return vessel;
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
