namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public sealed class GravityGradientIntegrationTests
{
    [Fact]
    public void EvaluatorUsesCandidateFrameAndCompositeInertiaForAllAttractors()
    {
        var vessel = new Vessel("tidal-frame") { Position = new Vector3d(999, 999, 999) };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "tidal-structure", CategoryStr = "structure", MassDry = 100, LengthM = 10, DiameterM = 1,
        }));
        var state = new RigidBody6DofState(new Vector3d(100, 0, 0), Vector3d.Zero,
            Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), System.Math.PI / 4), Vector3d.Zero);
        var mass = new RigidBodyMassProperties(100, Matrix3x3d.Diagonal(2, 5, 9));
        var bodies = new[]
        {
            new CelestialBody { Id = "left", GM = 1000, Position = Vector3d.Zero },
            new CelestialBody { Id = "right", GM = 2000, Position = new Vector3d(200, 0, 0) },
            new CelestialBody { Id = "massless", GM = 0, Position = state.Position },
        };
        var enabled = RigidBodyForceEvaluator.Evaluate(vessel, state, new RigidBodyForceContext(bodies), mass);
        var disabled = RigidBodyForceEvaluator.Evaluate(vessel, state,
            new RigidBodyForceContext(bodies, gravityGradientEnabled: false), mass);
        // Both attractors have |r|=100; n_x*n_y=-1/2 in the candidate body frame.
        Assert.Equal(-0.0135, enabled.TorqueBody.Z, 14);
        Assert.InRange(new Vector3d(enabled.TorqueBody.X, enabled.TorqueBody.Y, 0).Magnitude, 0, 1e-15);
        Assert.Equal(Vector3d.Zero, disabled.TorqueBody);
        Assert.Equal(Quaterniond.Identity, vessel.Orientation);
        Assert.Equal(new Vector3d(999, 999, 999), vessel.Position);
    }

    [Fact]
    public void UniverseDefaultAppliesTidalTorqueWithoutAnyAttitudeActuator()
    {
        var (enabled, ship) = OrbitingRod();
        var (disabled, baseline) = OrbitingRod();
        disabled.GravityGradientEnabled = false;
        Assert.True(enabled.GravityGradientEnabled);
        enabled.Tick(0.02);
        disabled.Tick(0.02);
        Assert.True(ship.AngularVelocity.Magnitude > 1e-6);
        Assert.Equal(Vector3d.Zero, baseline.AngularVelocity);
        Assert.Equal(0, ship.RcsThrusterMassFlowKgPerS);
        Assert.Equal(1, ship.Orientation.Norm, 14);
        Assert.True(enabled.LastCoupled6DofTelemetry.IsFinite);
    }

    [Fact]
    public void KeplerAttitudeMatchesFineStepCoupledTidesWithoutChangingTheConic()
    {
        var (rails, ship) = HighOrbit();
        var (fine, reference) = HighOrbit();
        rails.TimeScale = 100;
        for (int i = 0; i < 10; i++) rails.Tick(0.02);
        for (int i = 0; i < 1000; i++) fine.Tick(0.02);
        Assert.True(ship.IsOnRails);
        Assert.False(reference.IsOnRails);
        Assert.True(ship.AngularVelocity.Magnitude > 1e-6);
        Assert.InRange((ship.Orientation.Rotate(Vector3d.Up) - reference.Orientation.Rotate(Vector3d.Up)).Magnitude, 0, 1e-9);
        Assert.InRange((ship.AngularVelocity - reference.AngularVelocity).Magnitude, 0, 1e-10);
        Assert.InRange((ship.CenterOfMass - reference.CenterOfMass).Magnitude, 0, 1e-4);
        Assert.InRange((ship.Velocity - reference.Velocity).Magnitude, 0, 1e-8);
        var orbit = ship.OrbitalState!;
        var body = rails.GetBody("coast-body")!;
        var (expectedPosition, expectedVelocity) = orbit.GetStateAtTime(rails.CurrentTime, body.GM);
        var offset = ship.Orientation.Rotate(ship.CenterOfMassOffsetLocal);
        Assert.InRange((ship.CenterOfMass - body.Position - expectedPosition).Magnitude, 0, 1e-8);
        Assert.InRange((ship.Velocity + ship.AngularVelocity.Cross(offset) - body.Velocity - expectedVelocity).Magnitude, 0, 1e-10);
    }

    [Fact]
    public void TorqueFreeKeplerCoastConservesAsymmetricBodyMomentum()
    {
        var (universe, ship) = HighOrbit();
        universe.GravityGradientEnabled = false;
        universe.TimeScale = 100;
        ship.AngularVelocity = new Vector3d(0.3, 0.2, 0.1);
        var inertia = ship.Parts.GetMassProperties().InertiaBody;
        var omega = ship.Orientation.Inverse().Rotate(ship.AngularVelocity);
        var momentum = ship.Orientation.Rotate(inertia.Multiply(omega));
        double energy = omega.Dot(inertia.Multiply(omega)) * 0.5;
        // Preserve the intended circular COM orbit when adding a spin to a datum state.
        var offset = ship.Orientation.Rotate(ship.CenterOfMassOffsetLocal);
        ship.Velocity -= ship.AngularVelocity.Cross(offset);
        for (int i = 0; i < 50; i++) universe.Tick(0.02);
        omega = ship.Orientation.Inverse().Rotate(ship.AngularVelocity);
        Assert.True(ship.IsOnRails);
        Assert.InRange((ship.Orientation.Rotate(inertia.Multiply(omega)) - momentum).Magnitude / momentum.Magnitude, 0, 1e-9);
        Assert.InRange(System.Math.Abs(omega.Dot(inertia.Multiply(omega)) * 0.5 / energy - 1), 0, 1e-9);
    }

    [Fact]
    public void SasWithLiveJetsUsesFundedPhysicsEvenAtHighWarp()
    {
        var (universe, ship) = HighOrbit(thrusters: true);
        universe.TimeScale = 10_000;
        ship.SASEnabled = true;
        ship.AngularVelocity = new Vector3d(0, 0, 0.01);
        var owner = ship.Parts.Root!;
        double fuel = owner.Monopropellant;
        Assert.True(universe.RequiresOffRailsPhysics(ship));
        // Classification itself neither arms systems nor consumes propellant.
        Assert.Equal(fuel, owner.Monopropellant);
        universe.Tick(0.00002);
        Assert.False(ship.IsOnRails);
        Assert.True(owner.Monopropellant < fuel);
        Assert.True(ship.RcsThrusterMassFlowKgPerS > 0);
        Assert.Equal(PhysicsSchedulerBranch.Mixed, universe.LastSchedulerTelemetry.Branch);
    }

    [Fact]
    public void IdleSasDoesNotWakeAnAnchoredCraftWithLiveJets()
    {
        var (universe, ship) = HighOrbit(thrusters: true);
        ship.SASEnabled = true;
        var body = universe.GetBody("coast-body")!;
        ship.Position = Vector3d.Right * (body.Radius + 0.001);
        ship.Velocity = -Vector3d.Right;
        universe.Tick(0.02);
        Assert.True(ship.IsSurfaceSettled);
        double fuel = ship.Parts.Root!.Monopropellant;
        Assert.Equal(VesselPhysicsWorkload.SurfaceSettled, universe.ClassifyMixedPhysicsWorkload(ship));
        universe.Tick(0.1);
        Assert.True(ship.IsSurfaceSettled);
        Assert.Equal(fuel, ship.Parts.Root.Monopropellant);
    }

    [Fact]
    public void SasWithoutLiveActuatorsCanCoastUnderPhysicalTides()
    {
        var (universe, ship) = HighOrbit(thrusters: true);
        ship.SASEnabled = true;
        ship.Parts.ArmStagedReactionControl();
        ship.Parts.Root!.Monopropellant = 0;
        Assert.False(universe.RequiresOffRailsPhysics(ship));
        universe.TimeScale = 100;
        universe.Tick(0.02);
        Assert.True(ship.IsOnRails);
        Assert.True(ship.AngularVelocity.Magnitude > 0);
        Assert.Equal(0, ship.RcsThrusterMassFlowKgPerS);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    public void DeferredProjectionAndWakePreserveTheRotationalEpoch(int frames)
    {
        var (deferred, ship) = HighOrbit();
        var (direct, reference) = HighOrbit();
        deferred.ActiveVessel = null;
        deferred.TimeScale = direct.TimeScale = 100;
        ship.AngularVelocity = reference.AngularVelocity = new Vector3d(0.1, 0.2, 0.3);
        foreach (var vessel in new[] { ship, reference })
            vessel.Velocity -= vessel.AngularVelocity.Cross(vessel.Orientation.Rotate(vessel.CenterOfMassOffsetLocal));
        for (int i = 0; i < frames; i++)
        {
            deferred.Tick(0.005);
            direct.Tick(0.005);
        }
        Assert.True(deferred.LastSchedulerTelemetry.DeadlineProjectedDispatches > 0);
        AssertRotationAndDatumClose();
        ship.PitchYawRoll = reference.PitchYawRoll = new Vector3d(0.1, 0, 0);
        deferred.Tick(0.001);
        direct.Tick(0.001);
        Assert.False(ship.IsOnRails);
        AssertRotationAndDatumClose();

        void AssertRotationAndDatumClose()
        {
            Assert.InRange((ship.Orientation.Rotate(Vector3d.Up) - reference.Orientation.Rotate(Vector3d.Up)).Magnitude, 0, 1e-8);
            Assert.InRange((ship.AngularVelocity - reference.AngularVelocity).Magnitude, 0, 1e-9);
            Assert.InRange((ship.Position - reference.Position).Magnitude, 0, 1e-7);
            Assert.InRange((ship.Velocity - reference.Velocity).Magnitude, 0, 1e-8);
        }
    }

    [Fact]
    public void DockedPrimaryKeplerCoastTracksTheCompositeCenterOfMass()
    {
        var universe = new Universe { TimeScale = 100 };
        var body = new CelestialBody { Id = "docked-body", GM = 1e12, Radius = 1e6, SphereOfInfluence = 1e12 };
        universe.AddBody(body);
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "data", "parts"))) folder = folder.Parent;
        var definition = PartDefinition.LoadFromJson(Path.Combine(folder!.FullName, "data", "parts", "docking_port_standard.json"));
        var q = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), System.Math.PI / 4);
        var primary = new Vessel("primary-coast") { ReferenceBodyId = body.Id, SASEnabled = false,
            Position = Vector3d.Right * 2e6, Orientation = q, Velocity = Vector3d.Up * System.Math.Sqrt(body.GM / 2e6) };
        primary.Parts.SetRoot(new Part(definition, "primary-port"));
        var secondary = new Vessel("secondary-coast") { ReferenceBodyId = body.Id, SASEnabled = false,
            Position = primary.Position + q.Rotate(Vector3d.Up * 0.2),
            Orientation = q * Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI), Velocity = primary.Velocity };
        secondary.Parts.SetRoot(new Part(definition, "secondary-port"));
        universe.AddVessel(primary); universe.AddVessel(secondary);
        Assert.True(universe.TryDock(primary.Id, "primary-port", secondary.Id, "secondary-port", "coasting-dock").Succeeded);
        // Leave the primary non-active so independent-deadline projections are exercised too.
        for (int i = 0; i < 15; i++) universe.Tick(0.005);
        var (expectedPosition, expectedVelocity) = primary.OrbitalState!.GetStateAtTime(universe.CurrentTime, body.GM);
        double totalMass = primary.TotalMass + secondary.TotalMass;
        var center = (primary.CenterOfMass * primary.TotalMass + secondary.CenterOfMass * secondary.TotalMass) / totalMass;
        Vector3d ComVelocity(Vessel vessel) => vessel.Velocity
            + vessel.AngularVelocity.Cross(vessel.Orientation.Rotate(vessel.CenterOfMassOffsetLocal));
        var velocity = (ComVelocity(primary) * primary.TotalMass + ComVelocity(secondary) * secondary.TotalMass) / totalMass;
        Assert.InRange((center - body.Position - expectedPosition).Magnitude, 0, 1e-8);
        Assert.InRange((velocity - body.Velocity - expectedVelocity).Magnitude, 0, 1e-9);
        Assert.True(primary.AngularVelocity.Magnitude > 0);
    }

    private static (Universe, Vessel) HighOrbit(bool thrusters = false)
    {
        var body = new CelestialBody
        {
            Id = "coast-body", GM = 1e12, Radius = 1e6, SphereOfInfluence = 1e12,
        };
        var ship = new Vessel("coast-rod")
        {
            ReferenceBodyId = body.Id, SASEnabled = false,
            Orientation = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), System.Math.PI / 4),
        };
        ship.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "coast-rod-command", CategoryStr = "command", MassDry = 100, LengthM = 10, DiameterM = 1,
            FuelCapacityMono = thrusters ? 10 : 0,
            RcsThrusters = thrusters ?
            [
                new() { Id = "nose-x", Position = [0, 4, 0], Direction = [1, 0, 0], ThrustN = 1, IspS = 70 },
                new() { Id = "tail-x", Position = [0, -4, 0], Direction = [-1, 0, 0], ThrustN = 1, IspS = 70 },
                new() { Id = "nose-z", Position = [0, 4, 0], Direction = [0, 0, 1], ThrustN = 1, IspS = 70 },
                new() { Id = "tail-z", Position = [0, -4, 0], Direction = [0, 0, -1], ThrustN = 1, IspS = 70 },
                new() { Id = "nose-nx", Position = [0, 4, 0], Direction = [-1, 0, 0], ThrustN = 1, IspS = 70 },
                new() { Id = "tail-nx", Position = [0, -4, 0], Direction = [1, 0, 0], ThrustN = 1, IspS = 70 },
                new() { Id = "nose-nz", Position = [0, 4, 0], Direction = [0, 0, -1], ThrustN = 1, IspS = 70 },
                new() { Id = "tail-nz", Position = [0, -4, 0], Direction = [0, 0, 1], ThrustN = 1, IspS = 70 },
            ] : [],
        }));
        ship.Position = Vector3d.Right * 2e6 - ship.Orientation.Rotate(ship.CenterOfMassOffsetLocal);
        ship.Velocity = Vector3d.Up * System.Math.Sqrt(body.GM / 2e6);
        var universe = new Universe { ActiveVessel = ship };
        universe.AddBody(body); universe.AddVessel(ship);
        return (universe, ship);
    }

    private static (Universe, Vessel) OrbitingRod()
    {
        var body = new CelestialBody
        {
            Id = "tidal-body", GM = 1000, Radius = 10, SphereOfInfluence = 1e6,
            Position = Vector3d.Zero, Velocity = Vector3d.Zero,
        };
        var ship = new Vessel("tidal-rod")
        {
            ReferenceBodyId = body.Id, SASEnabled = false, PilotRateEnvelopeEnabled = false,
            Orientation = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), System.Math.PI / 4),
        };
        ship.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "tidal-rod-structure", CategoryStr = "structure", MassDry = 100, LengthM = 10, DiameterM = 1,
        }));
        ship.Position = new Vector3d(100, 0, 0) - ship.Orientation.Rotate(ship.Parts.CenterOfMass);
        ship.Velocity = new Vector3d(0, System.Math.Sqrt(body.GM / 100), 0);
        var universe = new Universe();
        universe.AddBody(body); universe.AddVessel(ship); universe.ActiveVessel = ship;
        return (universe, ship);
    }
}
