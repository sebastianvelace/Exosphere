namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class Coupled6DofUniverseTests
{
    [Fact]
    public void OptInCoupledPathIntegratesTheVesselThroughUniverse()
    {
        var body = new CelestialBody
        {
            Id = "test-body",
            Name = "Test Body",
            Mass = 1.0e20,
            GM = 0.0,
            Radius = 1_000.0,
            SphereOfInfluence = 1.0e9,
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
        };
        var vessel = new Vessel("coupled")
        {
            Position = Vector3d.Up * 5_000.0,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "payload",
            CategoryStr = "structure",
            MassDry = 1_000.0,
            LengthM = 10.0,
            DiameterM = 2.0,
        }));

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = true,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);

        universe.Tick(0.02);

        Assert.Equal(0.02, universe.CurrentTime, precision: 12);
        Assert.False(vessel.IsDestroyed);
        Assert.True(double.IsFinite(vessel.Position.X));
        Assert.True(double.IsFinite(vessel.Position.Y));
        Assert.True(double.IsFinite(vessel.Position.Z));
        Assert.True(double.IsFinite(vessel.Velocity.X));
        Assert.True(double.IsFinite(vessel.Velocity.Y));
        Assert.True(double.IsFinite(vessel.Velocity.Z));
        Assert.Equal(1.0, vessel.Orientation.Norm, precision: 12);

        var telemetry = universe.LastCoupled6DofTelemetry;
        Assert.True(telemetry.Integrated);
        Assert.True(telemetry.IsFinite);
        Assert.Equal(0.02, telemetry.SimulationTime, precision: 12);
        Assert.Equal(0.02, telemetry.StepSeconds, precision: 12);
        Assert.Equal(1_000.0, telemetry.MassKg, precision: 8);
        Assert.Equal(vessel.Orientation.Norm, telemetry.OrientationNorm, precision: 12);
        Assert.Equal(
            vessel.Position + vessel.Orientation.Rotate(vessel.Parts.CenterOfMass),
            telemetry.FinalCenterOfMassPositionWorld);
    }
}
