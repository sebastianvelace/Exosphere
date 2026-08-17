namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Systems;
using Xunit;

public sealed class VesselSystemsRuntimeRegistryTests
{
    [Fact]
    public void MaterializedVesselsAreIsolatedAndExistingEpochCannotBeOverwritten()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        var first = registry.Materialize("vessel-a", simulationTime: 50.0);
        var second = registry.Materialize("vessel-b", simulationTime: 50.0);

        first.LifeSupport.RestoreState(new LifeSupportState { OxygenKg = 100.0 });
        Assert.Equal(100.0, first.LifeSupport.OxygenKg, 12);
        Assert.Equal(200.0, second.LifeSupport.OxygenKg, 12);
        Assert.Same(first, registry.Materialize("vessel-a", simulationTime: 50.0));
        Assert.Throws<InvalidOperationException>(
            () => registry.Materialize("vessel-a", simulationTime: 51.0));
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void CaptureRequiresEveryMaterializedRuntimeAtTheCommittedEpoch()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        registry.Materialize("vessel-a", simulationTime: 10.0);
        registry.Materialize("vessel-b", simulationTime: 10.0);

        var states = registry.CaptureStates(committedEpoch: 10.0);
        Assert.Equal(2, states.Count);
        Assert.Equal(10.0, states["vessel-a"].SimulationTime, 12);
        Assert.Equal(10.0, states["vessel-b"].SimulationTime, 12);

        Assert.Throws<InvalidOperationException>(
            () => registry.CaptureStates(committedEpoch: 10.5));
    }

    [Fact]
    public void RestoreIsAtomicAndDoesNotInventMissingVesselStates()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        registry.Materialize("old-vessel", simulationTime: 20.0);
        var valid = new VesselSystemsState
        {
            VesselId = "vessel-a",
            SimulationTime = 30.0,
        };

        registry.RestoreStates(
            [valid],
            knownVesselIds: ["vessel-a", "vessel-b"],
            committedEpoch: 30.0);

        Assert.False(registry.Contains("old-vessel"));
        Assert.True(registry.Contains("vessel-a"));
        Assert.False(registry.Contains("vessel-b"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void InvalidRestoreLeavesPreviousRegistryUntouched()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        var original = registry.Materialize("original", simulationTime: 40.0);
        original.LifeSupport.RestoreState(new LifeSupportState { OxygenKg = 123.0 });

        var invalid = new VesselSystemsState
        {
            VesselId = "missing",
            SimulationTime = 41.0,
        };
        Assert.Throws<InvalidDataException>(() => registry.RestoreStates(
            [invalid],
            knownVesselIds: ["original"],
            committedEpoch: 41.0));

        Assert.True(registry.Contains("original"));
        Assert.Equal(1, registry.Count);
        Assert.Equal(123.0, original.LifeSupport.OxygenKg, 12);
    }

    [Fact]
    public void RemoveAndClearOnlyAffectMaterializedRegistryEntries()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        registry.Materialize("vessel-a", simulationTime: 1.0);
        registry.Materialize("vessel-b", simulationTime: 1.0);

        Assert.True(registry.Remove("vessel-a"));
        Assert.False(registry.Contains("vessel-a"));
        Assert.True(registry.Contains("vessel-b"));
        Assert.False(registry.Remove("vessel-a"));

        registry.Clear();
        Assert.Equal(0, registry.Count);
    }
}
