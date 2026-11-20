namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class TowerCatchApproachPlannerTests
{
    private static readonly Vector3d Up = Vector3d.Up;

    [Fact]
    public void IgnitionPointLiesShortOfTheTowerOnTheLineOfSight()
    {
        var cradle = new Vector3d(0, 60, 0);
        var ship = new Vector3d(-3000, 5000, 400);

        var point = TowerCatchApproachPlanner.IgnitionPoint(cradle, Up, ship, 850.0);

        var fromCradle = point - cradle;
        Assert.Equal(850.0, fromCradle.Magnitude, 6);
        Assert.Equal(0.0, fromCradle.Dot(Up), 9);
        var towardShip = new Vector3d(-3000, 0, 400).Normalized;
        Assert.Equal(1.0, fromCradle.Normalized.Dot(towardShip), 9);
    }

    [Fact]
    public void GlideCommandClosesOnTheIgnitionPointAndDampsCrossDrift()
    {
        var definition = TowerCatchBurnTests.Bench.Definition();
        var ship = new Vector3d(-3000, 5000, 0);
        var point = new Vector3d(-850, 60, 0);
        var sideways = new Vector3d(0, 0, 20);

        var command = TowerCatchApproachPlanner.GlideAccelerationCommand(Up, ship, sideways, point, definition);

        Assert.True(command.X > 0.0);          // toward the point
        Assert.True(command.Z < 0.0);          // against the cross drift
        Assert.Equal(0.0, command.Y, 9);       // horizontal only
    }

    [Theory]
    [InlineData(80.0)]
    [InlineData(120.0)]
    [InlineData(150.0)]
    public void IgnitionClearanceIsWhereTheIgnitionTestFires(double descent)
    {
        var definition = TowerCatchBurnTests.Bench.Definition();

        double h = definition.IgnitionClearanceM(descent);

        Assert.False(definition.ShouldIgnite(h + 2.0, descent));
        Assert.True(definition.ShouldIgnite(h - 2.0, descent));
    }

    [Fact]
    public void LeadGrowsWithTheGlideTheFlipAddsTo()
    {
        var definition = TowerCatchBurnTests.Bench.Definition();

        double still = definition.IgnitionLeadDistanceM(0.0);
        double gliding = definition.IgnitionLeadDistanceM(22.7);

        Assert.Equal(380.0, still, 9);
        Assert.InRange(definition.IgnitionLeadDistanceM(35.0), 845.0, 855.0);   // bench: caught from 850 m at 35 m/s
        Assert.True(gliding > still);
    }

    [Fact]
    public void NearVerticalFallTurnsTheLiftToAnyHeading()
    {
        var flow = new Vector3d(0.1, -1.0, 0.0).Normalized;
        var neutral = Up;

        var lift = TowerCatchApproachPlanner.LiftDirection(flow, new Vector3d(0, 0, 2.0), neutral);

        Assert.Equal(0.0, lift.Dot(flow), 9);
        Assert.True(lift.Z > 0.99);
        Assert.Equal(neutral, TowerCatchApproachPlanner.LiftDirection(flow, Vector3d.Zero, neutral));
    }
}
