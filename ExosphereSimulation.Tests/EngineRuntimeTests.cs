namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class EngineRuntimeTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void MerlinOctaweb_TransitionsToRunningWithStablePerEngineIds()
    {
        var engine = CreateMerlinCluster("octaweb-runtime");
        var visited = new HashSet<EngineLifecycleState>();

        for (int i = 0; i < 100; i++)
        {
            engine.AdvanceEngineRuntime(1.0, 0.02);
            visited.Add(engine.EngineStates[0].State);
        }

        Assert.Equal(9, engine.EngineStates.Count);
        Assert.Equal(9, engine.EngineStates.Select(e => e.InstanceId).Distinct().Count());
        Assert.All(engine.EngineStates, state =>
        {
            Assert.StartsWith("octaweb-runtime:engine:", state.InstanceId);
            Assert.Equal(EngineLifecycleState.Running, state.State);
            Assert.Equal(1.0, state.ActualThrottle, 12);
            Assert.Equal(1, state.StartsCompleted);
        });
        Assert.Contains(EngineLifecycleState.Chill, visited);
        Assert.Contains(EngineLifecycleState.SpinPrime, visited);
        Assert.Contains(EngineLifecycleState.Ignition, visited);
        Assert.Contains(EngineLifecycleState.Ramp, visited);
    }

    [Fact]
    public void InjectedEngineOut_ReducesThrustAndMassFlowByExactlyOneNinth()
    {
        var engine = CreateMerlinCluster("octaweb-engine-out");
        for (int i = 0; i < 100; i++) engine.AdvanceEngineRuntime(1.0, 0.02);
        double thrustBefore = engine.GetThrustMagnitude(101_325.0);
        double flowBefore = engine.GetMassFlow(101_325.0);

        string failedId = engine.EngineStates[4].InstanceId;
        Assert.True(engine.FailEngine(failedId, "TEST_ENGINE_OUT"));

        Assert.Equal(8, engine.EngineStates.Count(e => e.ActualThrottle > 0.99));
        Assert.Equal(thrustBefore * 8.0 / 9.0, engine.GetThrustMagnitude(101_325.0), 6);
        Assert.Equal(flowBefore * 8.0 / 9.0, engine.GetMassFlow(101_325.0), 6);
        var failed = Assert.Single(engine.GetEngineTelemetry(101_325.0),
            row => row.InstanceId == failedId);
        Assert.Equal(EngineLifecycleState.Failed, failed.State);
        Assert.Equal("TEST_ENGINE_OUT", failed.FailureCode);
        Assert.Equal(0.0, failed.ThrustN);
    }

    [Fact]
    public void ShutdownPassesThroughPurgeAndReturnsOff()
    {
        var engine = CreateMerlinCluster("octaweb-shutdown");
        for (int i = 0; i < 100; i++) engine.AdvanceEngineRuntime(1.0, 0.02);
        var visited = new HashSet<EngineLifecycleState>();

        for (int i = 0; i < 100; i++)
        {
            engine.AdvanceEngineRuntime(0.0, 0.02);
            visited.Add(engine.EngineStates[0].State);
        }

        Assert.Contains(EngineLifecycleState.Shutdown, visited);
        Assert.Contains(EngineLifecycleState.Purge, visited);
        Assert.Equal(EngineLifecycleState.Off, engine.EngineStates[0].State);
        Assert.Equal(0.0, engine.ThrottleLevel);
    }

    [Fact]
    public void TeleportResetCutsChamberPressureAndGimbalWithoutErasingReliabilityHistory()
    {
        var engine = CreateMerlinCluster("teleport-cutoff");
        for (int i = 0; i < 100; i++)
            engine.AdvanceEngineRuntime(1.0, 0.02);
        engine.GimbalOffset = new Vector3d(0.8, 0.0, -0.6);
        engine.AdvanceEngineRuntime(1.0, 0.02);

        int starts = engine.EngineStates[0].StartsCompleted;
        engine.ResetEngineRuntimeForTeleport();

        Assert.All(engine.EngineStates, state =>
        {
            Assert.Equal(EngineLifecycleState.Off, state.State);
            Assert.Equal(0.0, state.CommandedThrottle);
            Assert.Equal(0.0, state.ActualThrottle);
            Assert.Equal(0.0, state.ChamberPressureFraction);
            Assert.Equal(Vector3d.Zero, state.GimbalDeg);
            Assert.Equal(Vector3d.Zero, state.GimbalVelocityDegPerS);
            Assert.Equal(starts, state.StartsCompleted);
        });
        Assert.Equal(0.0, engine.ThrottleLevel);
    }

    [Fact]
    public void SaveV2_RoundTripPreservesEngineFailureAndTransientState()
    {
        var catalog = LoadCatalog();
        var vessel = new Vessel("engine-save-vessel");
        var engine = new Part(catalog["merlin1d_cluster9_block5"], "engine-save-part");
        engine.GimbalOffset = new(0.6, 0.0, -0.4);
        vessel.Parts.SetRoot(engine);
        var universe = new Universe();
        universe.AddVessel(vessel);
        universe.SetActiveVessel(vessel.Id);

        for (int i = 0; i < 32; i++) engine.AdvanceEngineRuntime(0.8, 0.02);
        string failedId = engine.EngineStates[2].InstanceId;
        engine.FailEngine(failedId, "PUMP_FAILURE");

        string json = SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe));
        var restoredUniverse = new Universe();
        SaveGameV2Codec.Restore(
            restoredUniverse,
            SaveGameV2Json.DeserializeOrMigrate(json),
            catalog);

        var restored = Assert.Single(restoredUniverse.Vessels).Parts.Root!;
        Assert.Equal(9, restored.EngineStates.Count);
        var failed = Assert.Single(restored.EngineStates, e => e.InstanceId == failedId);
        Assert.Equal(EngineLifecycleState.Failed, failed.State);
        Assert.Equal("PUMP_FAILURE", failed.FailureCode);
        Assert.Contains(restored.EngineStates,
            e => e.State is EngineLifecycleState.Ramp or EngineLifecycleState.Running);
        Assert.Contains(restored.EngineStates,
            e => e.ChamberPressureFraction > 0.0);
        Assert.Contains(restored.EngineStates,
            e => e.GimbalDeg.Magnitude > 0.0
                 && e.GimbalVelocityDegPerS.Magnitude > 0.0);
    }

    private static Part CreateMerlinCluster(string instanceId) =>
        new(LoadCatalog()["merlin1d_cluster9_block5"], instanceId);

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));

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
