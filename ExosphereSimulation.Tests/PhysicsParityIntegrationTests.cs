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
        var legacy = CreateUniverse("legacy", coupled: false, powered: false);
        var coupled = CreateUniverse("coupled", coupled: true, powered: false);

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

    [Fact]
    public void LegacyAndCoupledPathsRemainWithinPoweredAscentTolerance()
    {
        var legacy = CreateUniverse("legacy-powered", coupled: false, powered: true);
        var coupled = CreateUniverse("coupled-powered", coupled: true, powered: true);

        for (int i = 0; i < 25; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.5,
                VelocityMetersPerSecond: 0.5,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled powered divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.Parts.TotalLiquidFuel
            < legacy.ActiveVessel!.Parts.TotalLiquidFuel + 1e-9);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    private static Universe CreateUniverse(string vesselId, bool coupled, bool powered)
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
            Position = Vector3d.Up * (body.Radius + (powered ? 1_000.0 : 250_000.0)),
            Velocity = powered ? Vector3d.Zero : Vector3d.Up * 7_600.0,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            Throttle = powered ? 1.0 : 0.0,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = powered ? "parity-engine" : "parity-payload",
            CategoryStr = powered ? "engine" : "structure",
            MassDry = powered ? 1_000.0 : 1_000.0,
            LengthM = 10.0,
            DiameterM = 2.0,
            ThrustVac = powered ? 50_000.0 : 0.0,
            ThrustSL = powered ? 50_000.0 : 0.0,
            IspVac = powered ? 300.0 : 0.0,
            IspSL = powered ? 300.0 : 0.0,
            FuelTypeStr = powered ? "LiquidFuel+Oxidizer" : "",
            MixtureRatio = powered ? 2.0 : 0.0,
            FuelCapacityLF = powered ? 1_000.0 : 0.0,
            FuelCapacityOx = powered ? 2_000.0 : 0.0,
            ThrustPositionYM = powered ? -5.0 : 0.0,
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
