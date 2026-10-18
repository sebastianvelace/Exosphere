namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// A hard-docked secondary is slaved to its primary, so the primary's thrust must
/// accelerate the combined mass rather than the primary alone.
/// </summary>
public sealed class DockedStackThrustTests
{
    [Fact]
    public void PrimaryThrustAcceleratesTheWholeDockedStack()
    {
        var (alone, aloneVessel, _) = CreateScenario(dock: false);
        var (docked, primary, secondary) = CreateScenario(dock: true);
        double m1 = primary.TotalMass, m2 = secondary.TotalMass;

        var aloneStart = aloneVessel.Velocity;
        var dockedStart = primary.Velocity;
        for (int step = 0; step < 100; step++)
        {
            alone.Tick(0.02);
            docked.Tick(0.02);
        }

        double aloneDeltaV = (aloneVessel.Velocity - aloneStart).Magnitude;
        double dockedDeltaV = (primary.Velocity - dockedStart).Magnitude;
        Assert.True(aloneDeltaV > 0.1, $"The test engine produced only {aloneDeltaV:E3} m/s.");
        // Same burn, same propellant: the rocket equation sets the expected ratio.
        double m1End = primary.TotalMass;
        double expected = System.Math.Log((m1 + m2) / (m1End + m2)) / System.Math.Log(m1 / m1End);
        Assert.InRange(dockedDeltaV / aloneDeltaV, expected * 0.99, expected * 1.01);
        Assert.Equal(primary.Velocity, secondary.Velocity);
    }

    private static (Universe Universe, Vessel Primary, Vessel Secondary) CreateScenario(bool dock)
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(DataDirectory(), "parts"));
        var universe = new Universe();
        universe.AddBody(new CelestialBody
        {
            Id = "test-body",
            Radius = 1.0,
            SphereOfInfluence = 1.0e12,
        });

        var tug = new VesselAssembly(catalog);
        var port = tug.AddRoot("docking_port_standard");
        var tank = tug.AttachPartAutomatically(port.InstanceId, "fuel_tank_small");
        tug.AttachPartAutomatically(tank.InstanceId, "engine_liquid_vac");
        var primary = tug.ToVessel("tug", "tug");
        primary.Position = new Vector3d(1.0e6, 0.0, 0.0);
        primary.ReferenceBodyId = "test-body";
        primary.SASEnabled = false;
        primary.Throttle = 1.0;
        universe.AddVessel(primary);

        var cargo = new VesselAssembly(catalog);
        var cargoPort = cargo.AddRoot("docking_port_standard");
        cargo.AttachPartAutomatically(cargoPort.InstanceId, "fuel_tank_small");
        var secondary = cargo.ToVessel("cargo", "cargo");
        secondary.ReferenceBodyId = "test-body";
        secondary.SASEnabled = false;
        secondary.Position = new Vector3d(-1.0e6, 0.0, 0.0);
        universe.AddVessel(secondary);
        universe.SetActiveVessel(primary.Id);

        if (dock)
        {
            var primaryPort = primary.Parts.Parts.Single(p => p.Definition.IsDockingPort);
            var secondaryPort = secondary.Parts.Parts.Single(p => p.Definition.IsDockingPort);
            Assert.True(primary.Parts.TryGetAttachmentNodeLocalPosition(primaryPort.InstanceId,
                primaryPort.Definition.DockingNodeId, out var primaryNode));
            Assert.True(secondary.Parts.TryGetAttachmentNodeLocalPosition(secondaryPort.InstanceId,
                secondaryPort.Definition.DockingNodeId, out var secondaryNode));
            secondary.Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI);
            secondary.Position = primary.Position + primaryNode - secondary.Orientation.Rotate(secondaryNode);
            secondary.Velocity = primary.Velocity;
            var attempt = universe.TryDock(primary.Id, primaryPort.InstanceId,
                secondary.Id, secondaryPort.InstanceId, "test-dock");
            Assert.True(attempt.Succeeded, attempt.Failure.ToString());
        }

        return (universe, primary, secondary);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
