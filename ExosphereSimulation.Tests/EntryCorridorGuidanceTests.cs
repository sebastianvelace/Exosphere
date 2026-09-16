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
}
