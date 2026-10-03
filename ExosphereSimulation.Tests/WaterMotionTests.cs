namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

public sealed class WaterMotionTests(ITestOutputHelper output)
{
    [Fact]
    public void SkirtConversionPreservesRotatingPointKinematics()
    {
        var position = new Vector3d(123, 456, 789);
        var velocity = new Vector3d(1, 2, 3);
        var orientation = Quaterniond.FromEuler(25, 36, -40);
        var spin = new Vector3d(0.1, 0.2, -0.3);
        var offsetBody = new Vector3d(0.2, 24, -0.1);
        var state = WaterMotionFrame.FromSkirt(position, velocity, orientation, spin, offsetBody);
        var offsetWorld = orientation.Rotate(offsetBody);
        Assert.InRange((state.Position-position-offsetWorld).Magnitude, 0, 1e-12);
        Assert.InRange((state.Velocity-velocity-spin.Cross(offsetWorld)).Magnitude, 0, 1e-12);
        var datum = WaterMotionFrame.ToSkirt(state, offsetBody);
        Assert.InRange((datum.Position-position).Magnitude, 0, 1e-12);
        Assert.InRange((datum.Velocity-velocity).Magnitude, 0, 1e-12);
    }

    [Fact]
    public void FreeRotationMovesTheSkirtAroundAConservedMassCenter()
    {
        var mass = new RigidBodyMassProperties(100, Vector3d.Up*22, Matrix3x3d.Diagonal(250, 40, 250));
        var orientation = Quaterniond.FromEuler(20, 10, 30);
        var spin = orientation.Rotate(Vector3d.Right*0.2);
        var velocity = new Vector3d(1, 2, 3);
        var offsetWorld = orientation.Rotate(mass.CenterOfMassBody);
        var initial = WaterMotionFrame.FromSkirt(Vector3d.Zero, velocity-spin.Cross(offsetWorld), orientation,
            spin, mass.CenterOfMassBody);
        var state = initial;
        var momentum = initial.Orientation.Rotate(mass.InertiaBody.Multiply(initial.AngularVelocityBody));
        double energy = initial.AngularVelocityBody.Dot(mass.InertiaBody.Multiply(initial.AngularVelocityBody))*0.5;
        for (int i = 0; i < 1000; i++)
            state = RigidBody6DofIntegrator.Step(state, i*0.02, 0.02, mass, (_, _) => RigidBodyForces.Zero);
        Assert.InRange((state.Position-initial.Position-velocity*20).Magnitude, 0, 1e-9);
        Assert.InRange((state.Velocity-velocity).Magnitude, 0, 1e-12);
        Assert.InRange((state.Orientation.Rotate(mass.InertiaBody.Multiply(state.AngularVelocityBody))-momentum).Magnitude, 0, 1e-9);
        Assert.InRange(System.Math.Abs(state.AngularVelocityBody.Dot(mass.InertiaBody.Multiply(state.AngularVelocityBody))*0.5-energy), 0, 1e-10);
        var datum = WaterMotionFrame.ToSkirt(state, mass.CenterOfMassBody);
        Assert.True((datum.Position-velocity*20).Magnitude > 20); // A rotating skirt is not a freely translating origin.
    }

    [Fact]
    public void LoadedPartGraphMapsItsRootCentreToTheVisibleSkirtAndUpdatesWithFuel()
    {
        var (_, ship, _, water) = Fixture();
        var properties = ship.Parts.GetMassProperties();
        var resolved = WaterMotionFrame.ResolveMassProperties(ship.Parts, water);
        Assert.True(water.SkirtDatumYM < 0);
        Assert.InRange((resolved.CenterOfMassBody-(properties.CenterOfMassBody-Vector3d.Up*water.SkirtDatumYM!.Value)).Magnitude, 0, 1e-12);
        Assert.Equal(properties.InertiaBody, resolved.InertiaBody);
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        tank.LiquidFuel *= 0.5; tank.Oxidizer *= 0.5;
        var updated = WaterMotionFrame.ResolveMassProperties(ship.Parts, water);
        Assert.True(updated.Mass < resolved.Mass);
        Assert.True((updated.CenterOfMassBody-resolved.CenterOfMassBody).Magnitude > 0.1);
    }

    [Fact]
    public void ProductionWaterMotionConvergesAtTwentyTenAndFiveMilliseconds()
    {
        var coarse = Run(0.02); var medium = Run(0.01); var fine = Run(0.005);
        double coarseError = (coarse.Position-fine.Position).Magnitude;
        double mediumError = (medium.Position-fine.Position).Magnitude;
        output.WriteLine($"COM convergence: 20ms={coarseError:G6}m 10ms={mediumError:G6}m; "
            + $"rate error={(coarse.AngularVelocityWorld-fine.AngularVelocityWorld).Magnitude:G6}rad/s");
        Assert.InRange(coarseError, 0, 0.02);
        Assert.InRange(mediumError, 0, 0.005);
        Assert.True(mediumError < coarseError);
        Assert.InRange((coarse.Velocity-fine.Velocity).Magnitude, 0, 0.02);
        Assert.InRange((coarse.AngularVelocityWorld-fine.AngularVelocityWorld).Magnitude, 0, 0.001);
        Assert.InRange((coarse.Orientation.Rotate(Vector3d.Up)-fine.Orientation.Rotate(Vector3d.Up)).Magnitude, 0, 0.001);
    }

    private RigidBody6DofState Run(double step)
    {
        var (universe, ship, body, water) = Fixture();
        double initialMass = ship.TotalMass;
        for (int i = 0; i < (int)System.Math.Round(10/step); i++)
        {
            universe.Tick(step);
            Assert.False(ship.IsDestroyed); Assert.False(ship.IsGroundHeld);
            Assert.True(ship.LastWaterMotionTelemetry?.IsFinite);
            Assert.InRange(ship.Orientation.Norm, 0.999999999, 1.000000001);
            if (i % (int)System.Math.Round(0.5/step) == 0) {
                var upTrace = body.GetGeodeticUp(ship.Position);
                output.WriteLine($"t={universe.CurrentTime:F3} h={ship.GetAltitude(body):F3} "
                    + $"v={ship.GetSurfaceVelocity(body).Dot(upTrace):F3} angle={System.Math.Acos(ship.Orientation.Rotate(Vector3d.Up).Dot(upTrace))*MathUtils.RAD_TO_DEG:F3} "
                    + $"waterV={ship.LastWaterContact?.SubmergedVolumeM3:G6} F={ship.LastContactForceWorld.Dot(upTrace):G6}");
            }
        }
        Assert.Equal(initialMass, ship.TotalMass);
        Assert.False(universe.Coupled6DofIntegrationEnabled);
        Assert.True(ship.LastWaterContact?.SubmergedVolumeM3 > 0);
        var up = body.GetGeodeticUp(ship.Position);
        output.WriteLine($"step={step} altitude={ship.GetAltitude(body):G6}m "
            + $"tilt={System.Math.Acos(ship.Orientation.Rotate(Vector3d.Up).Dot(up))*MathUtils.RAD_TO_DEG:G6}deg "
            + $"rate={ship.AngularVelocity.Magnitude:G6}rad/s");
        return WaterMotionFrame.FromSkirt(ship.Position, ship.Velocity, ship.Orientation, ship.AngularVelocity,
            WaterMotionFrame.ResolveMassProperties(ship.Parts, water).CenterOfMassBody);
    }

    private static (Universe, Vessel, CelestialBody, WaterContactDefinition) Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        string data = Path.Combine(directory!.FullName, "data");
        var body = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        body.OrbitalElements = null;
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles/starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Water-motion fixture");
        ship.Stage();
        foreach (var payload in ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).ToArray())
            ship.Parts.DetachSubtree(payload.InstanceId);
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        tank.LiquidFuel = 15000; tank.Oxidizer = 55000;
        var positions = ship.Parts.ComputePartLocalPositions();
        double bottom = positions.Min(p => p.Value.Y-p.Key.Definition.LengthM*0.5);
        var water = new WaterContactDefinition(ship.MaximumDiameter*0.5, ship.VehicleLength, -3.78,
            ship.Parts.CenterOfMass.Y-bottom, 1025, 0.8, 0.5, 25, 35, -165, -140, bottom);
        ship.WaterContact = water; ship.WaterMotionEnabled = true;
        ship.Position = body.GetSurfacePositionAtTime(28, -150, 0, 3.6);
        var up = body.GetGeodeticUp(ship.Position); var east = body.GetEastDirection(ship.Position);
        ship.Orientation = Quaterniond.FromAxisAngle(east, 2*MathUtils.DEG_TO_RAD)*Quaterniond.FromTo(Vector3d.Up, up);
        ship.AngularVelocity = body.RotationAxis*body.AngularSpeed;
        ship.Velocity = body.GetSurfaceVelocity(ship.Position)-up*2.3;
        ship.ReferenceBodyId = body.Id; ship.SASEnabled = false;
        var universe = new Universe(); universe.AddBody(body); universe.AddVessel(ship); universe.ActiveVessel = ship;
        return (universe, ship, body, water);
    }
}
