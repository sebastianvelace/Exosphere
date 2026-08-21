namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class SoftLandingThresholdTests
{
    [Theory]
    [InlineData(2.5)]
    public void SurfaceImpact_AtOrBelowThreshold_SoftLands(double impactSpeedMps)
    {
        RunSurfaceImpactCase(LoadBody("earth"), impactSpeedMps, expectDestroyed: false);
    }

    private static void RunSurfaceImpactCase(
        CelestialBody body,
        double impactSpeedMps,
        bool expectDestroyed)
    {
        var vessel = new Vessel { ReferenceBodyId = body.Id };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "probe",
            CategoryStr = "command",
            MassDry = 1_000.0,
        }));

        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);

        var up = body.GetGeodeticUp(body.GetPositionAlongDirection(Vector3d.Right, 0.001));
        var pos = body.GetPositionAlongDirection(Vector3d.Right, 0.001);
        var surfacePointVel = body.Velocity + body.GetSurfaceVelocity(pos);

        vessel.Position = pos;
        vessel.Velocity = surfacePointVel - up * impactSpeedMps;

        universe.Tick(0.02);

        Assert.Equal(expectDestroyed, vessel.IsDestroyed);
        Assert.True(body.GetAltitude(vessel.Position) >= 0.0);
        Assert.True(vessel.GetSurfaceVelocity(body).Magnitude <= AscentStagingPolicy.SoftLandingSpeedMps + 0.5);
        Assert.True(vessel.IsSurfaceSettled);

        for (int i = 0; i < 100; i++)
            universe.Tick(0.1);
        Assert.InRange(body.GetAltitude(vessel.Position), 0.99, 1.01);
        Assert.InRange(vessel.GetSurfaceVelocity(body).Magnitude, 0.0, 1e-8);
    }

    [Fact]
    public void PoweredLiftoffThroughTheEllipsoidIsNotASplashdown()
    {
        var earth = LoadBody("earth");
        var pad = earth.GetSurfacePosition(28.5, -80.6, 3.0);
        var up = earth.GetGeodeticUp(pad);
        var vessel = new Vessel { ReferenceBodyId = earth.Id, Throttle = 1.0 };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "capsule",
            CategoryStr = "command",
            MassDry = 1_000.0,
            SplashdownCapable = true,
            MaxSplashdownSpeedMps = 12.5,
        }));
        vessel.Position = earth.GetSurfacePosition(28.5, -80.6, -0.4);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad) + up * 8.0;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);

        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        universe.Tick(0.02);

        Assert.False(vessel.IsSurfaceSettled);
        Assert.False(vessel.IsDestroyed);
        Assert.True(earth.GetAltitude(vessel.Position) >= 0.0);
        Assert.True(vessel.GetSurfaceVelocity(earth).Dot(earth.GetGeodeticUp(vessel.Position)) > 0.0);
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
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
