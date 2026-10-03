namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class WaterContactSolverTests
{
    private static readonly WaterContactDefinition Water = new(4.5, 52, -3.78, 22,
        1025, 0.8, 0.5, 20, 35, -165, -140);

    [Fact]
    public void DryHullHasNoHydrodynamicLoad()
    {
        var body = new CelestialBody { Id = "earth", Radius = 6371000, GM = 3.986004418e14 };
        var ship = new Vessel();
        var position = Vector3d.Up*(body.Radius+10);
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position, -Vector3d.Up*2);
        Assert.Equal(Vector3d.Zero, wrench.ForceWorld);
        Assert.Equal(0, wrench.SubmergedVolumeM3);
        Assert.InRange(wrench.LowestPointAltitudeM, 6.219, 6.221);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void DragDissipatesRelativeMotionAndBuoyancyMatchesDisplacedWater(double speed)
    {
        var body = new CelestialBody { Id = "earth", Radius = 6371000, GM = 3.986004418e14,
            Position = new Vector3d(1e10, 2e10, 3e10), Velocity = Vector3d.Right*30000 };
        var ship = new Vessel();
        var position = body.Position+Vector3d.Up*(body.Radius-2);
        var relative = Vector3d.Up*speed;
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position,
            body.Velocity+body.GetSurfaceVelocity(position)+relative);
        double volume = System.Math.PI*4.5*4.5*2;
        double support = 1025*volume*body.GetGravityAt(position).Magnitude;
        Assert.Equal(volume, wrench.SubmergedVolumeM3, 6);
        var drag = wrench.ForceWorld-Vector3d.Up*support;
        Assert.True(drag.Dot(relative) < 0);
        Assert.InRange(wrench.TorqueWorld.Magnitude, 0, 1e-6);
    }
}
