namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Physics;

public sealed class VariableMassFlowTransitionTests
{
    [Fact]
    public void StoppingRecordedInternalFlowTransfersItsMomentumToTheStructure()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(
            ExosphereSimulation.Tests.SaturnVAscentFlight.DataDirectory(), "parts"));
        var vessel = Ship(new Part(catalog["fuel_tank_small"], "tank"));
        var world = World(vessel);
        var save = SaveGameV2Codec.Capture(world);
        save.Vessels[0].TransportRelativeMomentum = VectorSaveV2.From(new(4, 0, 0));
        save.Vessels[0].TransportRelativeAngularMomentum = VectorSaveV2.From(new(0, 0, 6));
        SaveGameV2Codec.Restore(world, save, catalog);
        vessel = world.ActiveVessel!;
        world.Tick(0.02);
        var props = vessel.Parts.GetMassProperties();
        var momentum = vessel.CenterOfMassVelocity * props.Mass;
        var angular = vessel.Orientation.Rotate(props.InertiaBody.Multiply(
            vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity)));
        Assert.InRange((momentum - new Vector3d(4, 0, 0)).Magnitude, 0, 1e-9);
        Assert.InRange((angular - new Vector3d(0, 0, 6)).Magnitude, 0, 1e-9);
        Assert.Equal("no-outflow", vessel.LastVariableMassStatus);
        var firstVelocity = vessel.CenterOfMassVelocity;
        world.Tick(0.02);
        Assert.InRange((vessel.CenterOfMassVelocity - firstVelocity).Magnitude, 0, 1e-10);
    }

    [Fact]
    public void OpeningAnOffAxisFeedConservesMomentumAboutTheActualCenter()
    {
        var core = new Part(new PartDefinition
        {
            Id = "core", CategoryStr = "command", MassDry = 4000, LengthM = 2, DiameterM = 2,
            AttachmentNodes = [new() { Id = "bottom", Position = [0, -1, 0] }],
        }, "core");
        var jets = new Part(new PartDefinition
        {
            Id = "jets", MassDry = 100, LengthM = 1, DiameterM = 1, FuelCapacityMono = 100,
            RcsAttitudeStartsEnabled = true,
            RcsThrusters = [new() { Id = "forward", Position = [2, 0, 0], Direction = [1, 0, 0], ThrustN = 9.80665, IspS = 1 },
                new() { Id = "reverse", Position = [2, 0, 0], Direction = [-1, 0, 0], ThrustN = 9.80665, IspS = 1 }],
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.5, 0] }],
        }, "jets") { RcsAttitudeEnabled = true };
        var vessel = Ship(core); vessel.Parts.AddJoint(new Joint(core, jets, "bottom", "top"));
        vessel.TranslationCommand = Vector3d.Right;
        var world = World(vessel);
        const double dt = 1e-5;
        world.Tick(dt);
        Assert.Equal("active", vessel.LastVariableMassStatus);
        var flow = Assert.IsType<OpenSystemTransportSample>(vessel.LastTransportSample);
        Assert.True(flow.RelativeAngularMomentumBodyKgM2PerS.Magnitude > 1e-6);
        var props = vessel.Parts.GetMassProperties();
        var saved = SaveGameV2Codec.Capture(world).Vessels.Single();
        var shares = Assert.IsType<List<TransportMomentumShareSaveV2>>(saved.TransportMomentumShares);
        Assert.NotEmpty(shares);
        Assert.All(shares, share => Assert.Equal("jets", share.PartInstanceId));
        var routedLinear = shares.Aggregate(Vector3d.Zero, (sum, share) => sum + share.Linear.ToVector());
        var routedAngular = shares.Aggregate(Vector3d.Zero, (sum, share) => sum + share.AngularAboutDatum.ToVector())
            - props.CenterOfMassBody.Cross(routedLinear);
        Assert.InRange((routedLinear - flow.RelativeLinearMomentumBodyKgMps).Magnitude, 0, 1e-8);
        Assert.InRange((routedAngular - flow.RelativeAngularMomentumBodyKgM2PerS).Magnitude, 0, 1e-8);
        var momentum = vessel.CenterOfMassVelocity * props.Mass
            + vessel.Orientation.Rotate(flow.RelativeLinearMomentumBodyKgMps);
        var angular = vessel.Orientation.Rotate(props.InertiaBody.Multiply(
            vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity)) + flow.RelativeAngularMomentumBodyKgM2PerS);
        // External impulses and advected momentum are O(dt); the short step makes the
        // neglected variation below this bound, while a missing feed reaction is O(1).
        Assert.InRange((momentum - vessel.RcsThrusterForceBody * dt).Magnitude, 0, 1e-6);
        Assert.InRange((angular - vessel.RcsThrusterTorqueBody * dt).Magnitude, 0, 1e-6);
    }

    [Fact]
    public void DrainageOfAStationaryTankDoesNotCreateAtmosphericAirspeed()
    {
        static Vessel Drain(bool atmosphere)
        {
            var part = new Part(new PartDefinition
            {
                Id = "draining-column", MassDry = 100, FuelCapacityLF = 100,
                LengthM = 100, DiameterM = 2,
                PropellantTanks = [new() { Resource = "liquid_fuel", BottomYM = -50,
                    TopYM = 50, RadiusM = 1, DensityKgPerM3 = 1 / System.Math.PI }],
                VentOutlet = new() { PositionM = [0, 50, 0] },
            }, "tank");
            var vessel = Ship(part);
            vessel.PropellantVentRateKgPerS = 10;
            var world = new Universe { ActiveVessel = vessel, GravityGradientEnabled = false };
            world.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9,
                Atmosphere = atmosphere ? new AtmosphereModel { MaxAltitude = 1e6,
                    ScaleHeight = 1e9, SeaLevelDensity = 1 } : null });
            world.AddVessel(vessel);
            world.Tick(1e-5);
            Assert.Equal("active", vessel.LastVariableMassStatus);
            return vessel;
        }

        var vacuum = Drain(false);
        var air = Drain(true);
        // The liquid centroid moves as the tank drains, but the rigid surface starts at
        // rest in both worlds. Its aerodynamic impulse vanishes with the short step.
        Assert.InRange((air.Velocity - vacuum.Velocity).Magnitude, 0, 1e-10);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ARecordedFeedStopIsResolvedBeforeAnalyticWarp(bool active, bool linearFlow)
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(
            ExosphereSimulation.Tests.SaturnVAscentFlight.DataDirectory(), "parts"));
        Universe Make(bool settled)
        {
            var ship = Ship(new Part(catalog["fuel_tank_small"], "tank"));
            ship.Position = new(1e6, 0, 0); ship.Velocity = new(0, 0, 10);
            var world = new Universe { GravityGradientEnabled = false, TimeScale = 10 };
            world.AddBody(new CelestialBody { Id = "body", Radius = 1, GM = 1e8, SphereOfInfluence = 1e9 });
            world.AddVessel(ship); world.ActiveVessel = active ? ship : null;
            var momentum = linearFlow ? new Vector3d(4, 0, 0) : Vector3d.Zero;
            var angular = linearFlow ? Vector3d.Zero : new Vector3d(0, 0, 6);
            var save = SaveGameV2Codec.Capture(world);
            var record = save.Vessels.Single();
            record.TransportRelativeMomentum = VectorSaveV2.From(settled ? Vector3d.Zero : momentum);
            record.TransportRelativeAngularMomentum = VectorSaveV2.From(settled ? Vector3d.Zero : angular);
            if (settled)
            {
                var mass = ship.Parts.GetMassProperties();
                var omega = mass.InverseInertiaBody.Multiply(angular);
                record.Velocity = VectorSaveV2.From(ship.Velocity + momentum / mass.Mass
                    - omega.Cross(mass.CenterOfMassBody));
                record.AngularVelocity = VectorSaveV2.From(omega);
            }
            SaveGameV2Codec.Restore(world, save, catalog);
            world.ActiveVessel = active ? world.Vessels.Single() : null;
            return world;
        }
        var pending = Make(false); var reference = Make(true);
        Assert.True(pending.RequiresOffRailsPhysics(pending.Vessels.Single()));
        pending.Tick(0.02); reference.Tick(0.02);
        var actual = pending.Vessels.Single(); var expected = reference.Vessels.Single();
        var saved = SaveGameV2Codec.Capture(pending).Vessels.Single();
        Assert.Equal("no-outflow", actual.LastVariableMassStatus);
        Assert.Equal(Vector3d.Zero, saved.TransportRelativeMomentum!.ToVector());
        Assert.Equal(Vector3d.Zero, saved.TransportRelativeAngularMomentum!.ToVector());
        Assert.Empty(saved.TransportMomentumShares!);
        Assert.InRange((actual.CenterOfMass - expected.CenterOfMass).Magnitude, 0, 1e-7);
        Assert.InRange((actual.CenterOfMassVelocity - expected.CenterOfMassVelocity).Magnitude, 0, 1e-7);
        Assert.InRange((actual.AngularVelocity - expected.AngularVelocity).Magnitude, 0, 1e-9);
        Assert.False(pending.RequiresOffRailsPhysics(actual));
    }

    [Fact]
    public void CancellingStoreMomentaAreRetiredBeforeTheyCanBeSplitLater()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(
            ExosphereSimulation.Tests.SaturnVAscentFlight.DataDirectory(), "parts"));
        var ship = Ship(new Part(catalog["fuel_tank_small"], "tank"));
        ship.Position = new(1e6, 0, 0); ship.Velocity = new(0, 0, 10);
        var world = new Universe { GravityGradientEnabled = false, TimeScale = 10, ActiveVessel = ship };
        world.AddBody(new CelestialBody { Id = "body", Radius = 1, GM = 1e8, SphereOfInfluence = 1e9 });
        world.AddVessel(ship);
        var save = SaveGameV2Codec.Capture(world);
        save.Vessels.Single().TransportMomentumShares =
        [
            new() { PartInstanceId = "tank", Linear = VectorSaveV2.From(new(4, 0, 0)) },
            new() { PartInstanceId = "tank", Linear = VectorSaveV2.From(new(-4, 0, 0)) },
        ];
        SaveGameV2Codec.Restore(world, save, catalog);
        Assert.True(world.RequiresOffRailsPhysics(world.ActiveVessel!));
        world.Tick(0.02);
        var settled = SaveGameV2Codec.Capture(world).Vessels.Single();
        Assert.Empty(settled.TransportMomentumShares!);
        Assert.Equal(Vector3d.Zero, settled.TransportRelativeMomentum!.ToVector());
        Assert.Equal(Vector3d.Zero, settled.TransportRelativeAngularMomentum!.ToVector());
        Assert.False(world.RequiresOffRailsPhysics(world.ActiveVessel!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnpoweredFlowCommandsCannotBeSkippedByAnalyticWarp(bool vent)
    {
        var part = new Part(new PartDefinition { Id = "flow-source", MassDry = 100,
            FuelCapacityLF = 100, FuelCapacityMono = 100, LengthM = 2, DiameterM = 1,
            VentOutlet = new() { PositionM = [2, 0, 0] } }, "source");
        var ship = Ship(part); ship.Position = new(1e6, 0, 0); ship.Velocity = new(0, 0, 10);
        ship.PropellantVentRateKgPerS = vent ? 2 : 0;
        ship.DisturbanceMassFlowKgPerS = vent ? 0 : 2;
        ship.DisturbanceOutletBodyM = new(2, 0, 0);
        ship.DisturbanceExhaustDirectionBody = -Vector3d.Up;
        var world = new Universe { GravityGradientEnabled = false, TimeScale = 10, ActiveVessel = ship };
        world.AddBody(new CelestialBody { Id = "body", Radius = 1, GM = 1e8, SphereOfInfluence = 1e9 });
        world.AddVessel(ship);
        world.Tick(0.02);
        Assert.False(ship.IsOnRails);
        Assert.Equal(299.6, ship.TotalMass, 8); // One 2 kg/s debit over 0.2 simulated seconds.
        Assert.Equal("active", ship.LastVariableMassStatus);
        Assert.Equal(2, Assert.IsType<OpenSystemTransportSample>(ship.LastTransportSample).OutflowKgPerS, 10);
    }

    [Fact]
    public void AConstantInventoryDoesNotConstructADiscardedTransportModel()
    {
        var part = new Part(new PartDefinition { Id = "constant", MassDry = 100, LengthM = 2, DiameterM = 1 }, "constant");
        var vessel = Ship(part); vessel.CaptureResourceFunding = true;
        vessel.Tick(0.02, new CelestialBody { Id = "body", Radius = 1 });
        Assert.False(VariableMassStepModel.TryBuild(vessel.Parts, vessel.LastResourceFunding, vessel,
            null, out var model, out var reason));
        Assert.Null(model); Assert.Equal("no-outflow", reason);
    }

    private static Vessel Ship(Part part)
    {
        var ship = new Vessel("flow transition") { Position = Vector3d.Right * 10000,
            ReferenceBodyId = "body", SASEnabled = false };
        ship.Parts.SetRoot(part); return ship;
    }
    private static Universe World(Vessel vessel)
    {
        var world = new Universe { ActiveVessel = vessel, GravityGradientEnabled = false };
        world.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        world.AddVessel(vessel); return world;
    }
}
