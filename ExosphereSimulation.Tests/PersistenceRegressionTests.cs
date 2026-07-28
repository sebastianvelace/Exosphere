namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using System.Text.Json;

public sealed class PersistenceRegressionTests
{
    [Fact]
    public void SaveV2RoundTripPreservesStableIdsPartsResourcesCrewAndMetadata()
    {
        var catalog = LoadCatalog();
        var universe = new Universe { TimeScale = 4.0 };
        universe.SetSimulationTime(123_456.25);

        var vessel = new Vessel("vessel-primary")
        {
            Name = "Persistence Test",
            Position = new Vector3d(1, 2, 3),
            Velocity = new Vector3d(4, 5, 6),
            Orientation = Quaterniond.FromEuler(10, 20, 30),
            AngularVelocity = new Vector3d(0.1, 0.2, 0.3),
            Throttle = 0.42,
            PitchYawRoll = new Vector3d(-0.2, 0.3, 0.4),
            ReferenceBodyId = "earth",
            IsGroundHeld = true,
            GroundNormal = Vector3d.Up,
            GroundOffset = 18.5,
        };
        var command = new Part(catalog["command_pod_mk1"], "part-command")
        {
            ElectricCharge = 12.5,
            Temperature = 310,
        };
        var tank = new Part(catalog["fuel_tank_small"], "part-tank")
        {
            LiquidFuel = 111,
            Oxidizer = 222,
            SkinTemperature = 444,
        };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(tank);
        var joint = new Joint(command, tank, "bottom", "top")
        {
            CurrentTensileLoad = 1234,
            CurrentShearLoad = 4321,
        };
        vessel.Parts.AddJoint(joint);
        vessel.Crew.Add(new CrewMember
        {
            Id = "crew-1",
            FirstName = "Alex",
            LastName = "Rivera",
            PilotLevel = 3,
            EVAExperience = 7.5,
            EVASuitEC = 91,
        });
        universe.AddVessel(vessel);
        Assert.True(universe.SetActiveVessel(vessel.Id));

        var metadata = new SaveGameV2();
        metadata.Mission.MissionId = "falcon9-demo";
        metadata.Campaign.UnlockedIds.Add("falcon9-block5");
        metadata.PersistentAssets.Add(new PersistentAssetSaveV2
        {
            AssetId = "asset-1", AssetType = "satellite", VesselId = vessel.Id,
        });

        string json = SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe, metadata));
        var decoded = SaveGameV2Json.DeserializeOrMigrate(json);
        var restored = new Universe();
        SaveGameV2Codec.Restore(restored, decoded, catalog);

        Assert.Equal(123_456.25, restored.CurrentTime);
        Assert.Equal(4.0, restored.TimeScale);
        Assert.Equal("vessel-primary", restored.ActiveVessel?.Id);
        var actual = Assert.Single(restored.Vessels);
        Assert.Equal(new Vector3d(1, 2, 3), actual.Position);
        Assert.Equal("part-command", actual.Parts.Root?.InstanceId);
        Assert.Equal(111, actual.Parts.Parts.Single(p => p.InstanceId == "part-tank").LiquidFuel);
        Assert.Equal(444, actual.Parts.Parts.Single(p => p.InstanceId == "part-tank").SkinTemperature);
        Assert.Equal(1234, Assert.Single(actual.Parts.Joints).CurrentTensileLoad);
        Assert.Equal("crew-1", Assert.Single(actual.Crew).Id);
        Assert.Equal("falcon9-demo", decoded.Mission.MissionId);
        Assert.Contains("falcon9-block5", decoded.Campaign.UnlockedIds);
        Assert.Single(decoded.PersistentAssets);
    }

    [Fact]
    public void LegacyPartialSaveMigratesWithoutChangingVesselIdentity()
    {
        const string legacy = """
        {
          "CurrentTime": 42.5,
          "ActiveVesselId": "legacy-vessel",
          "Vessels": [{
            "Id": "legacy-vessel",
            "Name": "Old Save",
            "PositionX": 1, "PositionY": 2, "PositionZ": 3,
            "VelocityX": 4, "VelocityY": 5, "VelocityZ": 6,
            "OrientationW": 1, "OrientationX": 0,
            "OrientationY": 0, "OrientationZ": 0,
            "ReferenceBodyId": "earth"
          }]
        }
        """;

        var migrated = SaveGameV2Json.DeserializeOrMigrate(legacy);

        Assert.Equal(2, migrated.SchemaVersion);
        Assert.Equal(42.5, migrated.SimulationTime);
        Assert.Equal("legacy-vessel", migrated.ActiveVesselId);
        Assert.Equal("legacy-vessel", Assert.Single(migrated.Vessels).Id);
    }

    [Fact]
    public void InvalidSaveIsRejectedBeforeLiveUniverseIsMutated()
    {
        var catalog = LoadCatalog();
        var existing = new Vessel("keep-me");
        var universe = new Universe();
        universe.AddVessel(existing);
        universe.SetActiveVessel(existing.Id);
        var bad = new SaveGameV2
        {
            Vessels =
            [
                new VesselSaveV2
                {
                    Id = "bad",
                    Position = new VectorSaveV2 { X = double.NaN },
                },
            ],
            ActiveVesselId = "bad",
        };

        Assert.Throws<InvalidDataException>(() => SaveGameV2Codec.Restore(universe, bad, catalog));
        Assert.Same(existing, universe.ActiveVessel);
        Assert.Single(universe.Vessels);
    }

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(FindRepoRoot().FullName, "data", "parts"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln"))) return dir;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
