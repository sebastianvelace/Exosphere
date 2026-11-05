namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Gate for checkpointed segments: a run resumed from the entry checkpoint must reproduce
/// the continuous pad-to-entry run's next 60 s of descent control exactly. Every segment
/// test that starts from <see cref="Flight14EntryCheckpointFixture"/> rests on this.
/// </summary>
public sealed class Flight14EntryCheckpointTests(ITestOutputHelper output)
{
    [Fact]
    public void ResumedRunReproducesTheContinuousRun()
    {
        var reference = Flight14EntryCheckpointFixture.Value.Reference;
        var resumed = Flight14EntryCheckpointFixture.Resume();
        Assert.Equal(Flight14ReturnPhase.EntryReached, resumed.ReturnController!.Phase);
        Flight14EntryCheckpointFixture.FlyReference(resumed);
        var state = Flight14EntryCheckpointFixture.Sample(resumed);

        output.WriteLine($"position residual {(state.Position - reference.Position).Magnitude:E3} m, "
            + $"velocity residual {(state.Velocity - reference.Velocity).Magnitude:E3} m/s");
        Assert.Equal(reference.Time, state.Time);
        Assert.Equal(reference.Position, state.Position);
        Assert.Equal(reference.Velocity, state.Velocity);
        Assert.Equal(reference.AngularVelocity, state.AngularVelocity);
        Assert.Equal(reference.Orientation, state.Orientation);
        Assert.Equal(reference.Propellant, state.Propellant);
        Assert.Equal(reference.BodyFlapDeflections, state.BodyFlapDeflections);
    }

    [Fact]
    public void CheckpointRoundTripsThroughJson()
    {
        var checkpoint = Flight14EntryCheckpointFixture.Value.Checkpoint;
        var copy = Flight14EntryCheckpoint.FromJson(checkpoint.ToJson());
        Assert.Equal(checkpoint.SaveJson, copy.SaveJson);
        Assert.Equal(checkpoint.Return, copy.Return);
        Assert.Equal(checkpoint.Launch, copy.Launch);
        Assert.Equal(checkpoint.Releases, copy.Releases);
    }
}
