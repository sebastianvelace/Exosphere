namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Presentation;
using Xunit;

public sealed class FlightHudPresenterTests
{
    [Fact]
    public void Capture_ProducesOrbitalSnapshotWithoutUiPhysics()
    {
        var (universe, body, vessel, _) = CreateVehicle();
        double radius = body.Radius + 200_000.0;
        vessel.Position = body.Position + Vector3d.Right * radius;
        vessel.Velocity = body.Velocity
            + new Vector3d(0.0, 0.0, System.Math.Sqrt(body.GM / radius));

        var snapshot = new FlightHudPresenter().Capture(
            universe,
            vessel,
            "ORBIT",
            FlightHudViewMode.Exterior);

        Assert.Equal(vessel.Id, snapshot.VesselId);
        Assert.Equal(body.Id, snapshot.ReferenceBodyId);
        Assert.InRange(snapshot.AltitudeM, 199_999.0, 200_001.0);
        Assert.NotNull(snapshot.ApoapsisAltitudeM);
        Assert.NotNull(snapshot.PeriapsisAltitudeM);
        Assert.False(snapshot.IsImpactTrajectory);
        Assert.Equal(FlightNavigationMode.Orb, snapshot.NavigationMode);
        Assert.All(
            new[]
            {
                snapshot.AltitudeM,
                snapshot.SurfaceSpeedMps,
                snapshot.VerticalSpeedMps,
                snapshot.DynamicPressurePa,
                snapshot.TotalMassKg,
            },
            value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void MapSnapshot_UsesTargetNavigationWhenTargetExists()
    {
        var (universe, _, vessel, _) = CreateVehicle();

        var snapshot = new FlightHudPresenter().Capture(
            universe,
            vessel,
            "COAST",
            FlightHudViewMode.Map,
            hasNavigationTarget: true);

        Assert.Equal(FlightNavigationMode.Tgt, snapshot.NavigationMode);
        Assert.Equal(FlightHudViewMode.Map, snapshot.ViewMode);
    }

    [Fact]
    public void PropellantAlert_HasHysteresisAndAcknowledgement()
    {
        var (universe, _, vessel, tank) = CreateVehicle();
        var presenter = new FlightHudPresenter();
        tank.LiquidFuel = 10.0;

        var triggered = presenter.Capture(
            universe, vessel, "ASCENT_SH", FlightHudViewMode.Exterior);
        var alert = Assert.Single(triggered.Alerts, a => a.Code == "FUEL-LOW");
        Assert.Equal(FlightAlertSeverity.Caution, alert.Severity);
        Assert.False(alert.Acknowledged);
        Assert.NotEmpty(alert.RecommendedAction);

        presenter.AcknowledgeAlert(alert.Code);
        tank.LiquidFuel = 13.0;
        var hysteresis = presenter.Capture(
            universe, vessel, "ASCENT_SH", FlightHudViewMode.Exterior);
        Assert.True(Assert.Single(
            hysteresis.Alerts, a => a.Code == "FUEL-LOW").Acknowledged);

        tank.LiquidFuel = 16.0;
        var cleared = presenter.Capture(
            universe, vessel, "ASCENT_SH", FlightHudViewMode.Exterior);
        Assert.DoesNotContain(cleared.Alerts, a => a.Code == "FUEL-LOW");
    }

    private static (Universe universe, CelestialBody body, Vessel vessel, Part tank)
        CreateVehicle()
    {
        var body = CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", "earth.json"));
        var tank = new Part(new PartDefinition
        {
            Id = "hud-test-tank",
            CategoryStr = "fuel_tank",
            MassDry = 1_000.0,
            FuelCapacityLF = 100.0,
            FuelCapacityOx = 100.0,
        });
        var vessel = new Vessel("hud-test")
        {
            Name = "HUD Test Vehicle",
            ReferenceBodyId = body.Id,
            Position = body.Position + Vector3d.Right * (body.Radius + 100.0),
            Velocity = body.Velocity,
        };
        vessel.Parts.SetRoot(tank);

        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, body, vessel, tank);
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
