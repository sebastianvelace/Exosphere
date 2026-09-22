namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class PhysicsParityIntegrationTests
{
    [Fact]
    public void LegacyAndCoupledPathsRemainWithinCoastTolerance()
    {
        var legacy = CreateUniverse("legacy", coupled: false);
        var coupled = CreateUniverse("coupled", coupled: true);

        for (int i = 0; i < 50; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.1,
                VelocityMetersPerSecond: 0.1,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled coast divergence exceeded tolerance: {result}");
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    private static Universe CreateUniverse(string vesselId, bool coupled)
    {
        var body = new CelestialBody
        {
            Id = "parity-body",
            Name = "Parity Body",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6_371_000.0,
            SphereOfInfluence = 1.0e9,
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
        };
        var vessel = new Vessel(vesselId)
        {
            Position = Vector3d.Right * (body.Radius + 250_000.0),
            Velocity = Vector3d.Up * 7_600.0,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "parity-payload",
            CategoryStr = "structure",
            MassDry = 1_000.0,
            LengthM = 10.0,
            DiameterM = 2.0,
        }));

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = coupled,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }
}
