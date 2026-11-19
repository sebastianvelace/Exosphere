namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight15MissionTests(ITestOutputHelper output)
{
    [Fact]
    public void GuidedFlightFromPadTwoDeploysTheFullManifest()
    {
        var run = Flight15Mission.CreateAscent(SaturnVAscentFlight.DataDirectory());
        var exploration = new Flight14Exploration(run, Flight15Mission.AscentProfileId);
        Assert.True(run.Ship.IsGroundHeld);
        Assert.False(run.Guidance.InjectAscentFaults);

        run.Universe.TimeScale = 100;
        while (!exploration.IsStopped && run.Universe.CurrentTime <= run.PayloadDefinition.MaximumMissionSeconds + 5)
            exploration.AdvanceFrame(1.0, 100);

        output.WriteLine($"launch={run.Controller.Phase} deployment={run.PayloadController.Phase} "
            + $"separation={run.Controller.StagingElapsedSeconds:F2}s insertion={run.Controller.OrbitElapsedSeconds:F2}s");
        Assert.Null(exploration.BlockReason);
        Assert.True(exploration.IsTerminal);
        Assert.NotNull(run.Controller.DetachedBooster);
        Assert.Equal(Flight14LaunchPhase.OrbitReady, run.Controller.Phase);
        Assert.Equal(Flight15Mission.PayloadCount, run.PayloadController.Releases.Count);
        Assert.All(run.PayloadController.Releases, release =>
            Assert.True(release.PeriapsisAltitudeM >= run.PayloadDefinition.MinimumPeriapsisAltitudeM));
    }
}
