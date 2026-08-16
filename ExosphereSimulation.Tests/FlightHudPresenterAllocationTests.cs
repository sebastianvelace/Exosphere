namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Presentation;
using Xunit;

public sealed class FlightHudPresenterAllocationTests
{
    [Fact]
    public void NoAlertPathUsesStableEmptyAlertsAndAlertPathPreservesLatchState()
    {
        var (universe, vessel, tank) = CreateVehicle();
        var presenter = new FlightHudPresenter();

        var noAlert = presenter.Capture(
            universe, vessel, "PRE_LAUNCH", FlightHudViewMode.Exterior);

        Assert.Same(Array.Empty<FlightAlertSnapshot>(), noAlert.Alerts);

        tank.LiquidFuel = 10.0;
        tank.Oxidizer = 10.0;
        var triggered = presenter.Capture(
            universe, vessel, "PRE_LAUNCH", FlightHudViewMode.Exterior);

        Assert.Equal(2, triggered.Alerts.Count);
        Assert.Equal("FUEL-LOW", triggered.Alerts[0].Code);
        Assert.Equal("OX-LOW", triggered.Alerts[1].Code);
        Assert.False(triggered.Alerts[0].Acknowledged);
        Assert.False(triggered.Alerts[1].Acknowledged);

        presenter.AcknowledgeAlert("FUEL-LOW");
        tank.LiquidFuel = 13.0;
        var latched = presenter.Capture(
            universe, vessel, "PRE_LAUNCH", FlightHudViewMode.Exterior);

        Assert.NotSame(triggered.Alerts, latched.Alerts);
        Assert.True(Assert.Single(latched.Alerts, alert => alert.Code == "FUEL-LOW").Acknowledged);
        Assert.False(Assert.Single(latched.Alerts, alert => alert.Code == "OX-LOW").Acknowledged);
        Assert.False(triggered.Alerts[0].Acknowledged);

        tank.LiquidFuel = 16.0;
        tank.Oxidizer = 16.0;
        var cleared = presenter.Capture(
            universe, vessel, "PRE_LAUNCH", FlightHudViewMode.Exterior);

        Assert.Empty(cleared.Alerts);
        Assert.Same(Array.Empty<FlightAlertSnapshot>(), cleared.Alerts);
    }

    private static (Universe universe, Vessel vessel, Part tank) CreateVehicle()
    {
        var root = FindRepoRoot();
        var body = CelestialBody.LoadFromJson(
            Path.Combine(root.FullName, "data", "bodies", "earth.json"));
        var tank = new Part(new PartDefinition
        {
            Id = "hud-allocation-test-tank",
            CategoryStr = "fuel_tank",
            MassDry = 1_000.0,
            FuelCapacityLF = 100.0,
            FuelCapacityOx = 100.0,
        });
        tank.LiquidFuel = 100.0;
        tank.Oxidizer = 100.0;
        double orbitalRadius = body.Radius + 200_000.0;
        var vessel = new Vessel("hud-allocation-test")
        {
            Name = "HUD Allocation Test Vehicle",
            ReferenceBodyId = body.Id,
            Position = body.Position + Vector3d.Right * orbitalRadius,
            Velocity = body.Velocity
                + Vector3d.Forward * System.Math.Sqrt(body.GM / orbitalRadius),
        };
        vessel.Parts.SetRoot(tank);

        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, vessel, tank);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
