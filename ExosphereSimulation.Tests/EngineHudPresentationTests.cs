namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Presentation;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class EngineHudPresentationTests
{
    [Fact]
    public void StartupStatesAreNotFailuresOrDeliveredEngines()
    {
        var rows = new[]
        {
            Row(0.0, EngineLifecycleState.Chill),
            Row(0.05, EngineLifecycleState.Ramp),
            Row(1.0, EngineLifecycleState.Running),
        };

        Assert.Equal(EngineHudIndicatorState.Starting,
            EngineHudPresentation.Classify(rows[0]));
        Assert.Equal(EngineHudIndicatorState.Starting,
            EngineHudPresentation.Classify(rows[1]));
        Assert.Equal(EngineHudIndicatorState.Running,
            EngineHudPresentation.Classify(rows[2]));
        // Ramp already has chamber pressure and is therefore counted as delivered,
        // even though its dot remains yellow until the lifecycle reaches Running.
        Assert.Equal(2, EngineHudPresentation.CountDelivered(rows));
        Assert.Equal(0, EngineHudPresentation.CountFailures(rows));
    }

    [Fact]
    public void OnlyConfirmedFailureIsRedAndExcludedFromLitCount()
    {
        var rows = new[]
        {
            Row(1.0, EngineLifecycleState.Running),
            Row(0.0, EngineLifecycleState.Failed, "PROPELLANT_STARVATION"),
            Row(0.0, EngineLifecycleState.Off),
        };

        Assert.Equal(EngineHudIndicatorState.Failed,
            EngineHudPresentation.Classify(rows[1]));
        Assert.Equal(1, EngineHudPresentation.CountDelivered(rows));
        Assert.Equal(1, EngineHudPresentation.CountFailures(rows));
    }

    [Fact]
    public void DeliveredThrottleUsesActualRowsAndIncludesEngineOutInNormalization()
    {
        var rows = new[]
        {
            Row(1.0, EngineLifecycleState.Running),
            Row(0.5, EngineLifecycleState.Running),
            Row(0.0, EngineLifecycleState.Failed, "TEST_ENGINE_OUT"),
            Row(0.0, EngineLifecycleState.Off),
        };

        Assert.Equal(0.375, EngineHudPresentation.DeliveredThrottle(rows), 12);
        Assert.True(double.IsFinite(EngineHudPresentation.DeliveredThrottle(rows)));
    }

    private static EngineReadout Row(
        double throttle,
        EngineLifecycleState state,
        string? failureCode = null) =>
        new("test-engine", "Test Engine", throttle, throttle * 1_000.0,
            throttle * 10.0, state, failureCode);
}
