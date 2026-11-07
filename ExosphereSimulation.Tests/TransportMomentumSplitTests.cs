namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Xunit;

public sealed class TransportMomentumSplitTests
{
    [Fact]
    public void LegacyAggregateKeepsItsAngularMomentumWhenStagingChangesTheCenter()
    {
        var (world, catalog) = Fixture("stage");
        var save = SaveGameV2Codec.Capture(world);
        save.Vessels[0].TransportRelativeMomentum = VectorSaveV2.From(new(4, 0, 0));
        save.Vessels[0].TransportRelativeAngularMomentum = VectorSaveV2.From(Vector3d.Zero);
        save.Vessels[0].TransportMomentumShares = null;
        SaveGameV2Codec.Restore(world, save, catalog);
        var ship = world.ActiveVessel!;
        var origin = ship.CenterOfMass;
        var before = Total(world, origin);
        world.AddVessel(Assert.IsType<Vessel>(ship.Stage()));
        SameMomentum(before, Total(world, origin));
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("break")]
    [InlineData("payload")]
    public void StoreMomentaFollowTheirFragmentsAndSurviveReload(string mode)
    {
        var (world, catalog) = Fixture(mode);
        var ship = world.ActiveVessel!;
        var owners = mode == "stage"
            ? new[] { ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank")),
                ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster")) }
            : new[] { ship.Parts.Root!, ship.Parts.Parts.Single(p => p != ship.Parts.Root) };
        Vector3d[] momenta = [new(4, 0, 0), new(0, 0, 6)];
        var positions = ship.Parts.ComputePartLocalPositions();
        var linear = momenta[0] + momenta[1];
        var datumAngular = positions[owners[0]].Cross(momenta[0]) + positions[owners[1]].Cross(momenta[1]);
        var save = SaveGameV2Codec.Capture(world);
        var record = save.Vessels[0];
        record.TransportRelativeMomentum = VectorSaveV2.From(linear);
        record.TransportRelativeAngularMomentum = VectorSaveV2.From(
            datumAngular - ship.Parts.CenterOfMass.Cross(linear));
        record.TransportMomentumShares = owners.Select((owner, i) => new TransportMomentumShareSaveV2
        {
            PartInstanceId = owner.InstanceId, Linear = VectorSaveV2.From(momenta[i]),
            AngularAboutDatum = VectorSaveV2.From(positions[owner].Cross(momenta[i])),
        }).ToList();
        SaveGameV2Codec.Restore(world, save, catalog);
        ship = world.ActiveVessel!;
        var origin = ship.CenterOfMass;
        var before = Total(world, origin);
        var fragment = mode switch
        {
            "stage" => ship.Stage(),
            "break" => ship.BreakAtJoint(ship.Parts.Joints.Single()),
            _ => ship.DeployPayload(owners[1].InstanceId, separationVelocityLocal: Vector3d.Zero),
        };
        world.AddVessel(Assert.IsType<Vessel>(fragment));
        SameMomentum(before, Total(world, origin));
        var split = SaveGameV2Codec.Capture(world);
        foreach (var vessel in split.Vessels)
            foreach (var share in Assert.IsType<List<TransportMomentumShareSaveV2>>(vessel.TransportMomentumShares))
                Assert.Contains(vessel.Parts, part => part.InstanceId == share.PartInstanceId);
        Assert.Equal(momenta[0], split.Vessels.Single(v => v.Id == ship.Id).TransportRelativeMomentum!.ToVector());
        Assert.Equal(momenta[1], split.Vessels.Single(v => v.Id == fragment!.Id).TransportRelativeMomentum!.ToVector());
        SaveGameV2Codec.Restore(world, SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(split)), catalog);
        SameMomentum(before, Total(world, origin));
    }

    [Fact]
    public void AnUnknownFlowStaysUnknownInBothFragments()
    {
        var (world, catalog) = Fixture("stage");
        var save = SaveGameV2Codec.Capture(world);
        save.Vessels[0].TransportRelativeMomentum = null;
        save.Vessels[0].TransportRelativeAngularMomentum = null;
        save.Vessels[0].TransportMomentumShares = null;
        SaveGameV2Codec.Restore(world, save, catalog);
        world.AddVessel(Assert.IsType<Vessel>(world.ActiveVessel!.Stage()));
        foreach (var fragment in SaveGameV2Codec.Capture(world).Vessels)
        {
            Assert.Null(fragment.TransportMomentumShares);
            Assert.Null(fragment.TransportRelativeMomentum);
            Assert.Null(fragment.TransportRelativeAngularMomentum);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidSavedStoreMomentumIsRejected(bool missingOwner)
    {
        var (world, _) = Fixture("payload");
        var save = SaveGameV2Codec.Capture(world);
        save.Vessels[0].TransportMomentumShares = [new()
        {
            PartInstanceId = missingOwner ? "missing" : save.Vessels[0].Parts[0].InstanceId,
            Linear = VectorSaveV2.From(missingOwner ? Vector3d.Zero : new(double.NaN, 0, 0)),
        }];
        Assert.Throws<InvalidDataException>(() => SaveGameV2Codec.Validate(save));
    }

    private static (Universe World, PartCatalog Catalog) Fixture(string mode)
    {
        var data = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        Vessel ship;
        if (mode == "stage")
            ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data,
                "vehicles/starship_flight12_v3_2026.json")).Build(catalog).ToVessel("split flow bench");
        else
        {
            var root = new Part(catalog["fuel_tank_small"], "tank-a");
            var other = new Part(catalog["fuel_tank_small"], "tank-b");
            ship = new Vessel("split flow bench");
            ship.Parts.SetRoot(root);
            ship.Parts.AddJoint(new(root, other, "top", "bottom"));
        }
        ship.Position = Vector3d.Right * 10000;
        ship.ReferenceBodyId = "earth";
        var world = new Universe { ActiveVessel = ship };
        world.AddBody(new CelestialBody { Id = "earth", Radius = 1 });
        world.AddVessel(ship);
        return (world, catalog);
    }

    private static (Vector3d Linear, Vector3d Angular) Total(Universe world, Vector3d origin)
    {
        var saved = SaveGameV2Codec.Capture(world);
        var linear = Vector3d.Zero;
        var angular = Vector3d.Zero;
        foreach (var vessel in world.Vessels)
        {
            var record = saved.Vessels.Single(v => v.Id == vessel.Id);
            var mass = vessel.Parts.GetMassProperties();
            var p = vessel.CenterOfMassVelocity * mass.Mass
                + vessel.Orientation.Rotate(record.TransportRelativeMomentum!.ToVector());
            linear += p;
            angular += (vessel.CenterOfMass - origin).Cross(p)
                + vessel.Orientation.Rotate(mass.InertiaBody.Multiply(
                    vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity))
                    + record.TransportRelativeAngularMomentum!.ToVector());
        }
        return (linear, angular);
    }

    private static void SameMomentum((Vector3d Linear, Vector3d Angular) before,
        (Vector3d Linear, Vector3d Angular) after)
    {
        Assert.InRange((after.Linear - before.Linear).Magnitude, 0, 1e-7);
        Assert.InRange((after.Angular - before.Angular).Magnitude, 0, 1e-7);
    }
}
