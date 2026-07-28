namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;

public sealed class DockingSystemTests
{
    [Fact]
    public void CaptureRequiresRangeClosingSpeedAndOpposedPortAxes()
    {
        var (universe, primary, secondary) = CreatePair();

        secondary.Position = primary.Position + Vector3d.Up;
        Assert.Equal(
            DockingFailure.OutsideCaptureRange,
            universe.TryDock(
                primary.Id, "primary-port",
                secondary.Id, "secondary-port").Failure);

        secondary.Position = primary.Position + Vector3d.Up * 0.2;
        secondary.Velocity = primary.Velocity + Vector3d.Right;
        Assert.Equal(
            DockingFailure.ExcessiveClosingSpeed,
            universe.TryDock(
                primary.Id, "primary-port",
                secondary.Id, "secondary-port").Failure);

        secondary.Velocity = primary.Velocity;
        secondary.Orientation = Quaterniond.Identity;
        Assert.Equal(
            DockingFailure.Misaligned,
            universe.TryDock(
                primary.Id, "primary-port",
                secondary.Id, "secondary-port").Failure);
    }

    [Fact]
    public void HardDockConservesLinearMomentumAndUndockKeepsRigidBodyVelocity()
    {
        var (universe, primary, secondary) = CreatePair();
        secondary.Position = primary.Position + Vector3d.Up * 0.2;
        primary.Velocity = new Vector3d(2.0, 4.0, -1.0);
        secondary.Velocity = new Vector3d(2.1, 3.9, -0.95);
        primary.AngularVelocity = new Vector3d(0.0, 0.0, 0.04);
        secondary.AngularVelocity = new Vector3d(0.0, 0.0, -0.02);
        Vector3d momentumBefore =
            primary.Velocity * primary.TotalMass
            + secondary.Velocity * secondary.TotalMass;
        Vector3d angularMomentumBefore =
            AngularMomentum(primary, secondary);

        DockingAttempt result = universe.TryDock(
            primary.Id, "primary-port",
            secondary.Id, "secondary-port",
            "dock-gemini-agena");

        Assert.True(result.Succeeded);
        Assert.Equal(DockingFailure.None, result.Failure);
        Assert.Equal("dock-gemini-agena", result.Connection?.Id);
        Vector3d momentumAfter =
            primary.Velocity * primary.TotalMass
            + secondary.Velocity * secondary.TotalMass;
        AssertVectorClose(momentumBefore, momentumAfter, 1e-10);
        AssertVectorClose(
            angularMomentumBefore,
            AngularMomentum(primary, secondary),
            1e-10);
        Assert.Equal(primary.AngularVelocity, secondary.AngularVelocity);

        Vector3d separationBefore =
            secondary.Position - primary.Position;
        universe.Tick(0.02);
        AssertVectorClose(
            primary.Orientation.Rotate(
                result.Connection!.SecondaryPositionPrimaryLocal),
            secondary.Position - primary.Position,
            1e-10);
        Assert.InRange(
            ((secondary.Position - primary.Position).Magnitude
                - separationBefore.Magnitude),
            -1e-10, 1e-10);

        Vector3d momentumDocked =
            primary.Velocity * primary.TotalMass
            + secondary.Velocity * secondary.TotalMass;
        Vector3d angularMomentumDocked =
            AngularMomentum(primary, secondary);
        Assert.True(universe.Undock("dock-gemini-agena", 0.2));
        Vector3d momentumUndocked =
            primary.Velocity * primary.TotalMass
            + secondary.Velocity * secondary.TotalMass;
        AssertVectorClose(momentumDocked, momentumUndocked, 1e-10);
        AssertVectorClose(
            angularMomentumDocked,
            AngularMomentum(primary, secondary),
            1e-10);
        Assert.Empty(universe.DockingConnections);
    }

    [Fact]
    public void SaveV2RoundTripRestoresDockingIdsPortsAndRelativePose()
    {
        var (universe, primary, secondary) = CreatePair();
        secondary.Position = primary.Position + Vector3d.Up * 0.2;
        Assert.True(universe.TryDock(
            primary.Id, "primary-port",
            secondary.Id, "secondary-port",
            "persistent-dock").Succeeded);
        universe.SetActiveVessel(primary.Id);

        SaveGameV2 save = SaveGameV2Codec.Capture(universe);
        string json = SaveGameV2Json.Serialize(save);
        SaveGameV2 decoded = SaveGameV2Json.DeserializeOrMigrate(json);
        var restored = CreateUniverse();
        SaveGameV2Codec.Restore(restored, decoded, LoadCatalog());

        DockingConnection connection =
            Assert.Single(restored.DockingConnections);
        Assert.Equal("persistent-dock", connection.Id);
        Assert.Equal(primary.Id, connection.PrimaryVesselId);
        Assert.Equal(secondary.Id, connection.SecondaryVesselId);
        Assert.Equal("primary-port", connection.PrimaryPortPartId);
        Assert.Equal("secondary-port", connection.SecondaryPortPartId);
        Assert.Equal(primary.Id, restored.ActiveVessel?.Id);
        Assert.Equal(2, restored.Vessels.Count);
    }

    [Fact]
    public void InvalidDockingReferenceIsRejectedBeforeRestore()
    {
        var save = new SaveGameV2
        {
            Vessels =
            [
                new VesselSaveV2 { Id = "only-vessel" },
            ],
            DockingConnections =
            [
                new DockingConnectionSaveV2
                {
                    Id = "bad-dock",
                    PrimaryVesselId = "only-vessel",
                    SecondaryVesselId = "missing-vessel",
                    PrimaryPortPartId = "missing-port-a",
                    SecondaryPortPartId = "missing-port-b",
                    PrimaryPortNodeId = "dock",
                    SecondaryPortNodeId = "dock",
                },
            ],
        };

        Assert.Throws<InvalidDataException>(
            () => SaveGameV2Codec.Validate(save));
    }

    [Fact]
    public void NonDockingPartCannotReplaceLiveUniverseDuringRestore()
    {
        var live = CreateUniverse();
        var existing = new Vessel("keep-live");
        existing.Parts.SetRoot(new Part(
            LoadCatalog()["command_pod_mk1"], "existing-command"));
        live.AddVessel(existing);
        live.SetActiveVessel(existing.Id);

        var save = new SaveGameV2
        {
            Vessels =
            [
                SavedCommandVessel("replacement-a", "command-a"),
                SavedCommandVessel("replacement-b", "command-b"),
            ],
            DockingConnections =
            [
                new DockingConnectionSaveV2
                {
                    Id = "invalid-part-kind",
                    PrimaryVesselId = "replacement-a",
                    SecondaryVesselId = "replacement-b",
                    PrimaryPortPartId = "command-a",
                    SecondaryPortPartId = "command-b",
                    PrimaryPortNodeId = "bottom",
                    SecondaryPortNodeId = "bottom",
                },
            ],
        };

        Assert.Throws<InvalidDataException>(
            () => SaveGameV2Codec.Restore(live, save, LoadCatalog()));
        Assert.Same(existing, live.ActiveVessel);
        Assert.Same(existing, Assert.Single(live.Vessels));
    }

    private static (Universe universe, Vessel primary, Vessel secondary)
        CreatePair()
    {
        var universe = CreateUniverse();
        var definition = LoadCatalog()["docking_port_standard"];
        var primary = new Vessel("gemini")
        {
            Position = new Vector3d(100.0, 0.0, 0.0),
            Orientation = Quaterniond.Identity,
            SASEnabled = false,
            ReferenceBodyId = "test-body",
        };
        primary.Parts.SetRoot(new Part(definition, "primary-port"));
        var secondary = new Vessel("agena")
        {
            Position = new Vector3d(100.0, 0.2, 0.0),
            Orientation = Quaterniond.FromAxisAngle(
                Vector3d.Right, System.Math.PI),
            SASEnabled = false,
            ReferenceBodyId = "test-body",
        };
        secondary.Parts.SetRoot(new Part(definition, "secondary-port"));
        universe.AddVessel(primary);
        universe.AddVessel(secondary);
        universe.SetActiveVessel(primary.Id);
        return (universe, primary, secondary);
    }

    private static Universe CreateUniverse()
    {
        var universe = new Universe();
        universe.AddBody(new CelestialBody
        {
            Id = "test-body",
            Radius = 1.0,
            SphereOfInfluence = 1_000_000.0,
        });
        return universe;
    }

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            FindRepoRoot().FullName, "data", "parts"));

    private static VesselSaveV2 SavedCommandVessel(
        string vesselId,
        string partId) => new()
    {
        Id = vesselId,
        RootPartInstanceId = partId,
        Parts =
        [
            new PartSaveV2
            {
                InstanceId = partId,
                DefinitionId = "command_pod_mk1",
            },
        ],
    };

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertVectorClose(
        Vector3d expected,
        Vector3d actual,
        double tolerance)
    {
        Assert.InRange(
            (expected - actual).Magnitude,
            0.0,
            tolerance);
    }

    private static Vector3d AngularMomentum(
        Vessel first,
        Vessel second)
    {
        double totalMass = first.TotalMass + second.TotalMass;
        Vector3d centre = (
            first.Position * first.TotalMass
            + second.Position * second.TotalMass) / totalMass;
        Vector3d centreVelocity = (
            first.Velocity * first.TotalMass
            + second.Velocity * second.TotalMass) / totalMass;
        return first.AngularVelocity
                * first.Parts.TransverseMomentOfInertia
            + second.AngularVelocity
                * second.Parts.TransverseMomentOfInertia
            + (first.Position - centre).Cross(
                (first.Velocity - centreVelocity) * first.TotalMass)
            + (second.Position - centre).Cross(
                (second.Velocity - centreVelocity) * second.TotalMass);
    }
}
