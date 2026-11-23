namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class TowerCatchGlidePredictorTests
{
    private static readonly Vector3d Up = Vector3d.Up;
    private const double TurnRate = 0.08;

    [Fact]
    public void OnTimeStraightInNeedsNoTurn()
    {
        // 30 m/s for 40 s covers 1,200 m, leaving exactly an 800 m lead from 2,000 m out.
        var plan = TowerCatchGlidePredictor.Plan(new Vector3d(-2000, 0, 0), new Vector3d(30, 0, 0), Up, 40.0, 800.0, TurnRate);

        Assert.Equal(0.0, plan.TurnRateRadPerS, 9);
        Assert.InRange(plan.PredictedRangeAtIgnitionM, 790.0, 810.0);
    }

    [Fact]
    public void EarlyShipSpendsRangeAndStillEndsHeadOn()
    {
        // Straight in it would arrive 2,400 m early: it must turn away and come back.
        var plan = TowerCatchGlidePredictor.Plan(new Vector3d(-2000, 0, 0), new Vector3d(30, 0, 0), Up, 80.0, 800.0, TurnRate);

        Assert.NotEqual(0.0, plan.TurnRateRadPerS);
        Assert.InRange(plan.PredictedRangeAtIgnitionM, 500.0, 1_100.0);
        Assert.True(System.Math.Abs(plan.PredictedHeadingErrorRad) < 0.35);
    }

    [Fact]
    public void ShipFlyingAwayTurnsBackTowardTheTower()
    {
        var plan = TowerCatchGlidePredictor.Plan(new Vector3d(-800, 0, 300), new Vector3d(-30, 0, 0), Up, 60.0, 800.0, TurnRate);

        Assert.NotEqual(0.0, plan.TurnRateRadPerS);
        Assert.True(System.Math.Abs(plan.PredictedHeadingErrorRad) < 0.35);
    }

    [Fact]
    public void TurnAccelerationIsCentripetal()
    {
        var velocity = new Vector3d(30, 0, 0);

        var accel = TowerCatchGlidePredictor.TurnAcceleration(velocity, Up, 0.1);

        Assert.Equal(0.0, accel.Dot(velocity), 9);
        Assert.Equal(3.0, accel.Magnitude, 9);
    }
}
