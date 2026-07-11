namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class WarpPhysicsParityTests
{
    [Fact]
    public void AtmosphericVesselAtExtremeWarpLeavesRailsAndRunsThermalPhysics()
    {
        var earth = LoadBody("earth");
        var vessel = EntryProbe(earth);
        vessel.IsOnRails = true;
        double initialTemperature = vessel.Parts.Parts[0].Temperature;
        var universe = new Universe { TimeScale = 10_000.0, ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        universe.Tick(0.02 / universe.TimeScale);

        Assert.False(vessel.IsOnRails);
        Assert.True(vessel.Parts.Parts[0].Temperature > initialTemperature);
    }

    [Fact]
    public void AtmosphericHeatingMatchesBetweenRealtimeAndMixedWarp()
    {
        var earthA = LoadBody("earth");
        var a = EntryProbe(earthA);
        var realtime = new Universe { TimeScale = 1.0, ActiveVessel = a };
        realtime.AddBody(earthA);
        realtime.AddVessel(a);

        var earthB = LoadBody("earth");
        var b = EntryProbe(earthB);
        var warped = new Universe { TimeScale = 100.0, ActiveVessel = b };
        warped.AddBody(earthB);
        warped.AddVessel(b);

        realtime.Tick(0.10);
        warped.Tick(0.10 / warped.TimeScale);

        AssertClose(a.Parts.Parts[0].Temperature, b.Parts.Parts[0].Temperature, 1e-10);
        Assert.True((a.Position - b.Position).Magnitude < 1e-5);
        Assert.True((a.Velocity - b.Velocity).Magnitude < 1e-8);
    }

    [Fact]
    public void AtmosphericPeriapsisForcesBoundedWarpBeforeEntryInterface()
    {
        var earth = LoadBody("earth");
        var vessel = EntryProbe(earth);
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 300_000.0);
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 7_700.0, 0.0);
        vessel.IsOnRails = true;
        vessel.OrbitalState = new OrbitalElements
        {
            SemiMajorAxis = earth.Radius + 175_000.0,
            Eccentricity = 125_000.0 / (earth.Radius + 175_000.0),
            ReferenceBodyId = earth.Id,
        };
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.False(universe.RequiresOffRailsPhysics(vessel));
        Assert.True(universe.RequiresBoundedWarpPropagation(vessel));
    }

    private static Vessel EntryProbe(CelestialBody earth)
    {
        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "entry-probe",
            CategoryStr = "command",
            MassDry = 100_000.0,
            LengthM = 50.0,
            DiameterM = 9.0,
            HeatTolerance = 10_000.0,
        }));
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
        var surface = earth.GetSurfaceVelocity(vessel.Position);
        vessel.Velocity = earth.Velocity + surface + new Vector3d(0.0, 3_000.0, 0.0);
        vessel.SASEnabled = false;
        return vessel;
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static void AssertClose(double expected, double actual, double relativeTolerance)
    {
        double scale = System.Math.Max(System.Math.Abs(expected), 1.0);
        Assert.True(System.Math.Abs(expected - actual) <= scale * relativeTolerance,
            $"Expected {expected:R}, got {actual:R}.");
    }
}
