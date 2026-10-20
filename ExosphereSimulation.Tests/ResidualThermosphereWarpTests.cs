namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Low Earth orbit stays inside the residual thermosphere, so it is never put on rails.
/// An idle coast there must still be affordable at high warp: coast-sized RK4 steps keep
/// integrating drag and must reproduce the 20 ms decay.
/// </summary>
public sealed class ResidualThermosphereWarpTests
{
    private const double CircularAltitude = 400_000.0;

    // Apollo's parking orbit: denser residual air and ~0.9 kW/m² free-molecular heating.
    private const double ApolloParkingAltitude = 191_310.0;

    [Theory]
    [InlineData(CircularAltitude)]
    [InlineData(ApolloParkingAltitude)]
    public void IdleLeoCoastUsesCoastStepsAtHighWarp(double altitude)
    {
        var (universe, vessel, _) = CircularOrbit(altitude);
        Assert.True(universe.RequiresOffRailsPhysics(vessel));
        Assert.True(universe.IsResidualThermosphereCoast(vessel));
        Assert.True(universe.AllowsHighWarpCoast(vessel));

        universe.TimeScale = 1000.0;
        universe.Tick(1.0 / 60.0);

        Assert.Equal(Universe.MaxCoastStep, universe.LastMixedPhysicsStepCap);
        Assert.True(universe.LastSchedulerTelemetry.OuterSubsteps <= 10);
        Assert.False(vessel.IsOnRails);
    }

    [Theory]
    [InlineData(CircularAltitude)]
    [InlineData(ApolloParkingAltitude)]
    public void CoastStepsReproduceTwentyMillisecondDecayOverOneOrbit(double altitude)
    {
        var (coarse, coarseVessel, coarseEarth) = CircularOrbit(altitude);
        var (fine, fineVessel, fineEarth) = CircularOrbit(altitude);
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(
            System.Math.Pow(fineEarth.Radius + altitude, 3) / fineEarth.GM);
        double a0 = SemiMajorAxis(fineVessel, fineEarth, 0.0);

        coarse.TimeScale = 1000.0;
        while (coarse.CurrentTime < period - 1e-9)
            coarse.Tick(System.Math.Min(1.0 / 60.0, (period - coarse.CurrentTime) / 1000.0));
        while (fine.CurrentTime < coarse.CurrentTime - 1e-9)
            fine.Tick(System.Math.Min(0.02, coarse.CurrentTime - fine.CurrentTime));

        double coarseDecay = SemiMajorAxis(coarseVessel, coarseEarth, coarse.CurrentTime) - a0;
        double fineDecay = SemiMajorAxis(fineVessel, fineEarth, fine.CurrentTime) - a0;
        Assert.True(fineDecay < 0.0, "The residual thermosphere must decay the orbit.");
        Assert.InRange(coarseDecay / fineDecay, 0.95, 1.05);
        Assert.True(((coarseVessel.Position - coarseEarth.Position)
            - (fineVessel.Position - fineEarth.Position)).Magnitude < 50.0);
    }

    [Fact]
    public void OrbitThatReachesTheAtmosphereIsNotAHighWarpCoast()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = Probe(earth);
        double apoapsis = earth.Radius + 400_000.0, periapsis = earth.Radius + 150_000.0;
        double a = 0.5 * (apoapsis + periapsis);
        vessel.Position = earth.Position + Vector3d.Right * apoapsis;
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0)
            * System.Math.Sqrt(earth.GM * (2.0 / apoapsis - 1.0 / a));
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        Assert.False(universe.IsResidualThermosphereCoast(vessel));
        Assert.False(universe.AllowsHighWarpCoast(vessel));
    }

    [Fact]
    public void PadHoldAllowsHighWarpOnlyWithoutCommands()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = Probe(earth);
        var pad = earth.GetSurfacePosition(25.997, -97.157, 0.0);
        vessel.Position = pad;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad);
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = (pad - earth.Position).Normalized;
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        Assert.True(universe.AllowsHighWarpCoast(vessel));
        vessel.Throttle = 0.5;
        Assert.False(universe.AllowsHighWarpCoast(vessel));
    }

    private static (Universe Universe, Vessel Vessel, CelestialBody Earth) CircularOrbit(double altitude)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = Probe(earth);
        double radius = earth.Radius + altitude;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0) * System.Math.Sqrt(earth.GM / radius);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel, earth);
    }

    private static Vessel Probe(CelestialBody earth)
    {
        var vessel = new Vessel("thermosphere-coast") { SASEnabled = false, ReferenceBodyId = earth.Id };
        var hull = new Part(new PartDefinition
        {
            Id = "thermosphere-coast-hull",
            CategoryStr = "command",
            MassDry = 20_000.0,
            LengthM = 10.0,
            DiameterM = 3.0,
        });
        vessel.Parts.SetRoot(hull);
        vessel.Parts.AddPart(hull);
        return vessel;
    }

    private static double SemiMajorAxis(Vessel vessel, CelestialBody earth, double time) =>
        OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, time).SemiMajorAxis;

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
