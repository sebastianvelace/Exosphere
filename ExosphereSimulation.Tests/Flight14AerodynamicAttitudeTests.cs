namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14AerodynamicAttitudeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(70, 4000, 50000, false)]
    [InlineData(90, 80, 3000, false)]
    [InlineData(90, 80, 3000, true)]
    public void PhysicalFlapServosHoldEntryAndBroadsideAgainstTheStaticMoment(
        double angle, double speed, double altitude, bool enginesFailed)
    {
        var root = DataDirectory();
        var body = CelestialBody.LoadFromJson(Path.Combine(root, "bodies/earth.json"));
        body.Position = Vector3d.Zero; body.Velocity = Vector3d.Zero;
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(root,
            "vehicles/starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"))).ToVessel("Flap wind-tunnel fixture");
        Assert.NotNull(ship.Stage());
        if (enginesFailed)
        {
            ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines")).IsBroken = true;
            Assert.Equal(ControlAuthority.FlapsOnly, ControlAuthority.Evaluate(ship));
        }
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        // This is a constant-wind actuator bench, not a seeded mission-acceptance run.
        tank.LiquidFuel = 15000; tank.Oxidizer = 55000;
        ship.Position = body.GetSurfacePositionAtTime(23, -178, 0, altitude);
        var up = body.GetGeodeticUp(ship.Position);
        var flow = angle == 90 ? -up : up.Cross(Vector3d.Up).Normalized;
        ship.Velocity = body.GetSurfaceVelocity(ship.Position) + flow*speed;
        var lift = up-flow*up.Dot(flow);
        var reference = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, lift, angle), flow);
        ship.Orientation = reference; ship.Throttle = 0; ship.SASEnabled = false;
        double initialFuel = tank.LiquidFuel + tank.Oxidizer;
        for (int i = 0; i < 3000; i++)
        {
            var position = ship.Position; var velocity = ship.Velocity; var pose = ship.Orientation;
            ship.PitchYawRoll = Flight14AerodynamicAttitude.ComputeCommand(ship, body, reference, 250, 1000);
            Assert.Equal(position, ship.Position); Assert.Equal(velocity, ship.Velocity); Assert.Equal(pose, ship.Orientation);
            ship.Tick(0.02, body); // Rotation and finite flap servo response; wind is constant.
        }
        double error = AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference)*MathUtils.RAD_TO_DEG;
        output.WriteLine($"{angle}deg, q={ship.GetDynamicPressure(body):F1}Pa, enginesFailed={enginesFailed}: "
            + $"trim tracking error={error:F3}deg");
        Assert.InRange(error, 0, 1);
        Assert.InRange(ship.AngularVelocity.Magnitude, 0, 0.002);
        Assert.Equal(initialFuel, tank.LiquidFuel + tank.Oxidizer);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found."), "data");
    }
}
