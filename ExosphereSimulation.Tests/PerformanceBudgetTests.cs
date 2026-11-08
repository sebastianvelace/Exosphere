namespace ExosphereSimulation.Tests;

using System.Diagnostics;
using Exosphere.Simulation.Flight;
using Xunit.Abstractions;

/// <summary>
/// Performance budgets of the production physics step (docs/audits/PERFORMANCE_AUDIT_2026-11-08.md).
/// Allocation per step is deterministic and machine-independent, so it is a hard budget; it
/// guards the frame-time stutter that garbage collection causes in the game. Time is
/// machine-dependent and shares the CI host with parallel tests, so its bound is a loose
/// regression tripwire (an order of magnitude above the measured cost), not the budget itself.
/// </summary>
public sealed class PerformanceBudgetTests(ITestOutputHelper output)
{
    // Measured 2026-11-08 after O1 part 1: 60 KB per step (from 212 KB). The budget leaves
    // room for ordinary changes; O1b (persistent per-step model) lowers it toward 1 KB.
    private const double AllocationBudgetKbPerStep = 80.0;
    // Debug build on the CI host measures ~1.35 ms (Release ~0.2 ms); 20 ms catches an
    // order-of-magnitude regression without tripping on parallel-test load.
    private const double MeanStepTripwireMs = 20.0;

    [Fact]
    public void Flight14AscentStepStaysWithinItsAllocationBudget()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        // Past liftoff (step ~142), where the variable-mass path becomes active.
        for (int i = 0; i < 300; i++) run.AdvanceFrame(0.02);
        const int steps = 500;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++) run.AdvanceFrame(0.02);
        double meanMs = watch.Elapsed.TotalMilliseconds / steps;
        double kbPerStep = (GC.GetAllocatedBytesForCurrentThread() - before) / 1024.0 / steps;
        output.WriteLine($"Flight 14 ascent: {kbPerStep:F1} KB/step, {meanMs:F3} ms/step mean");
        Assert.Equal("active", run.Ship.LastVariableMassStatus);
        Assert.InRange(kbPerStep, 0.0, AllocationBudgetKbPerStep);
        Assert.InRange(meanMs, 0.0, MeanStepTripwireMs);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
