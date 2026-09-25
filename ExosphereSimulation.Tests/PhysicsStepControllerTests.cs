namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Xunit;

public sealed class PhysicsStepControllerTests
{
    [Fact]
    public void ControlEpochsAreIndependentOfOuterTickCadence()
    {
        RecordingController at30Fps = RunForOneSecond(30);
        RecordingController at60Fps = RunForOneSecond(60);
        RecordingController at120Fps = RunForOneSecond(120);

        Assert.Equal(at30Fps.Epochs, at60Fps.Epochs);
        Assert.Equal(at30Fps.Epochs, at120Fps.Epochs);
        Assert.Equal(50, at30Fps.Epochs.Count);
        Assert.All(at30Fps.Intervals, interval =>
            Assert.Equal(Universe.DeterministicControlPeriodSeconds, interval, 12));
    }

    [Fact]
    public void MixedPhysicsIsSplitAtEveryControlBoundary()
    {
        var controller = new RecordingController();
        var universe = new Universe
        {
            TimeScale = 10.0,
            PhysicsStepController = controller,
        };

        universe.Tick(0.01);

        Assert.Equal(5, controller.Epochs.Count);
        Assert.Equal(5, universe.LastSchedulerTelemetry.OuterSubsteps);
        Assert.Equal(0.10, universe.CurrentTime, 12);
    }

    [Fact]
    public void InactiveControllerDoesNotChangeSchedulerStepCount()
    {
        var controller = new RecordingController { Active = false };
        var universe = new Universe { PhysicsStepController = controller };

        universe.Tick(0.01);

        Assert.Empty(controller.Epochs);
        Assert.Equal(1, universe.LastSchedulerTelemetry.OuterSubsteps);
    }

    private static RecordingController RunForOneSecond(int framesPerSecond)
    {
        var controller = new RecordingController();
        var universe = new Universe { PhysicsStepController = controller };
        double frameSeconds = 1.0 / framesPerSecond;
        for (int i = 0; i < framesPerSecond; i++)
            universe.Tick(frameSeconds);
        Assert.Equal(1.0, universe.CurrentTime, 10);
        return controller;
    }

    private sealed class RecordingController : IPhysicsStepController
    {
        public bool Active { get; init; } = true;
        public List<double> Epochs { get; } = new();
        public List<double> Intervals { get; } = new();

        public bool RequiresFixedCadence(Universe universe) => Active;

        public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
        {
            Epochs.Add(System.Math.Round(universe.CurrentTime, 12));
            Intervals.Add(controlIntervalSeconds);
        }
    }
}
