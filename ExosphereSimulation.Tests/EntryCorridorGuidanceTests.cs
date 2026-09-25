namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class EntryCorridorGuidanceTests
{
    [Fact]
    public void PredictsFutureCrossTrackMissWithoutChasingDownrange()
    {
        var prediction = EntryCorridorGuidance.Predict(
            targetOffsetWorld: Vector3d.Forward * 30_000.0,
            vehicleSurfaceVelocity: Vector3d.Right * 7_600.0,
            targetSurfaceVelocity: Vector3d.Forward * 7_600.0 + Vector3d.Right * 8_000.0,
            bodyUp: Vector3d.Up,
            altitudeM: 70_000.0,
            downwardSpeedMps: 260.0,
            gravityMps2: 9.7);

        Assert.True(prediction.TimeToGroundS > 20.0);
        Assert.True(prediction.PredictedCrossRangeM > 500_000.0,
            $"expected a large future miss, got {prediction.PredictedCrossRangeM:F0} m");
        Assert.True(prediction.LiftDirection.Dot(Vector3d.Forward) > 0.9,
            "the lift command must oppose the projected cross-track miss");
        Assert.True(prediction.PredictedDownrangeM > 20_000.0,
            "downrange error must remain observable without becoming a bank command");
    }

    [Fact]
    public void UsesCurrentCorridorWhenRelativeVelocityIsZero()
    {
        var prediction = EntryCorridorGuidance.Predict(
            targetOffsetWorld: Vector3d.Forward * 5_000.0,
            vehicleSurfaceVelocity: Vector3d.Right * 20.0,
            targetSurfaceVelocity: Vector3d.Right * 20.0,
            bodyUp: Vector3d.Up,
            altitudeM: 1_000.0,
            downwardSpeedMps: 10.0,
            gravityMps2: 9.8);

        Assert.Equal(Vector3d.Forward, prediction.LiftDirection);
        Assert.InRange(prediction.PredictedCrossRangeM, 4_999.9, 5_000.1);
        Assert.Equal(0.0, prediction.PredictedDownrangeM, 10);
    }

    [Fact]
    public void HorizonIsBoundedForHighAltitudeAndNearGroundCases()
    {
        var high = EntryCorridorGuidance.Predict(
            Vector3d.Right, Vector3d.Zero, Vector3d.Zero, Vector3d.Up,
            altitudeM: 1_000_000.0, downwardSpeedMps: 0.0, gravityMps2: 9.8,
            minHorizonS: 15.0, maxHorizonS: 120.0);
        var low = EntryCorridorGuidance.Predict(
            Vector3d.Right, Vector3d.Zero, Vector3d.Zero, Vector3d.Up,
            altitudeM: 0.0, downwardSpeedMps: 100.0, gravityMps2: 9.8,
            minHorizonS: 15.0, maxHorizonS: 120.0);

        Assert.Equal(120.0, high.TimeToGroundS);
        Assert.Equal(15.0, low.TimeToGroundS);
    }

    [Fact]
    public void SelectsDownLiftWhenFutureFootprintHasPassedTarget()
    {
        var prediction = new EntryCorridorGuidance.Prediction(
            LiftDirection: Vector3d.Zero,
            PredictedCrossRangeM: 0.0,
            PredictedDownrangeM: -400_000.0,
            TimeToGroundS: 60.0);

        var selected = EntryCorridorGuidance.SelectLiftDirection(
            prediction, Vector3d.Up);

        Assert.True(selected.Dot(-Vector3d.Up) > 0.99,
            $"expected down-lift braking, got {selected}");
    }

    [Fact]
    public void SelectsUpLiftWhenFutureFootprintRemainsShortOfTarget()
    {
        var prediction = new EntryCorridorGuidance.Prediction(
            LiftDirection: Vector3d.Zero,
            PredictedCrossRangeM: 0.0,
            PredictedDownrangeM: 400_000.0,
            TimeToGroundS: 60.0);

        var selected = EntryCorridorGuidance.SelectLiftDirection(
            prediction, Vector3d.Up);

        Assert.True(selected.Dot(Vector3d.Up) > 0.99,
            $"expected up-lift extension, got {selected}");
    }

    [Fact]
    public void TerminalCorridorRespondsToKilometreScaleDownrangeError()
    {
        var prediction = new EntryCorridorGuidance.Prediction(
            LiftDirection: Vector3d.Zero,
            PredictedCrossRangeM: 0.0,
            PredictedDownrangeM: 2_000.0,
            TimeToGroundS: 30.0);

        var selected = EntryCorridorGuidance.SelectLiftDirection(
            prediction,
            Vector3d.Up,
            corridorMeters: 20_000.0,
            authorityMeters: 180_000.0,
            downrangeCorridorMeters: 500.0,
            downrangeAuthorityMeters: 2_500.0);

        Assert.True(selected.Dot(Vector3d.Up) > 0.2,
            $"terminal lift must extend a short footprint, got {selected}");
    }

    [Fact]
    public void KeepsLiftUpInsideTheDeadband()
    {
        var prediction = new EntryCorridorGuidance.Prediction(
            LiftDirection: Vector3d.Forward,
            PredictedCrossRangeM: 5_000.0,
            PredictedDownrangeM: 5_000.0,
            TimeToGroundS: 60.0);

        var selected = EntryCorridorGuidance.SelectLiftDirection(
            prediction, Vector3d.Up);

        Assert.True(selected.Dot(Vector3d.Up) > 0.999,
            $"nominal entry must remain lift-up inside the corridor, got {selected}");
    }

}
