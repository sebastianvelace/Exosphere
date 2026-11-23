namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Replays the game's own single-burn ignition state (SaveGameV2 written by the EDL under
/// <c>EXOSPHERE_CATCH_IGNITION_SAVE</c>) through <see cref="Flight15CatchBench"/>, which runs
/// the burn at the universe's control period as the game does. The capture comes after a glide
/// that lines the nose up with the tower, so the flip throws the ship along the approach.
/// </summary>
public sealed class Flight15CatchIgnitionReplayTests
{
    [Theory]
    [InlineData("ignition_60hz", 1.0 / 60.0)]
    [InlineData("ignition_50hz", 1.0 / 50.0)]
    public void GameIgnitionStateIsCaughtInOneBurn(string capture, double frame)
    {
        var bench = Flight15CatchBench.Restore(capture);
        var ship = bench.Ship;
        Assert.InRange(System.Math.Abs(TowerCatchBurn.BellyRollFromVerticalRad(ship.Orientation, ship.CatchTargetUpWorld)),
            0.0, 5.0 * MathUtils.DEG_TO_RAD);

        bench.IgniteNow();
        Assert.True(bench.IgnitionNoseTowardTower > 0.99, $"nose toward tower {bench.IgnitionNoseTowardTower:F3}");
        bench.Run(80.0, frame);

        var burn = bench.Burn!;
        string steps = string.Join(" ", burn.EngineSteps.Select(s => $"{s.EngineCount}@{s.TimeS:F1}"));
        Assert.True(burn.Phase == TowerCatchBurnPhase.Caught, $"{burn.Phase} {burn.BlockReason} steps={steps}");
        // The game cut the last engine at 38.3–38.9 s and caught about two seconds later. The
        // 60 Hz capture replays within a few tenths of that; the 50 Hz one catches at 46.8 s,
        // since a restore does not carry every control phase and the late overshoot amplifies
        // small differences. The bound guards against the 78–98 s catches this replaced.
        Assert.InRange(burn.CaughtTimeS, 0.0, 50.0);
        Assert.Equal(0, burn.Relights);
        var counts = burn.EngineSteps.Select(s => s.EngineCount).ToArray();
        Assert.Equal(3, counts[0]);
        Assert.True(counts.Zip(counts.Skip(1)).All(p => p.Second < p.First), $"steps={steps}");
    }
}
