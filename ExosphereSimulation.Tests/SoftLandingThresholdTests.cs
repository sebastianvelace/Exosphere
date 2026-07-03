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

        var up = new Vector3d(1.0, 0.0, 0.0);
        var pos = body.Position + up * (body.Radius + 0.001);
        var surfacePointVel = body.Velocity + body.GetSurfaceVelocity(pos);

        vessel.Position = pos;
        vessel.Velocity = surfacePointVel - up * impactSpeedMps;

        universe.Tick(0.02);

        Assert.Equal(expectDestroyed, vessel.IsDestroyed);
        Assert.True(body.GetAltitude(vessel.Position) >= 0.0);
        Assert.True(vessel.GetSurfaceVelocity(body).Magnitude <= AscentStagingPolicy.SoftLandingSpeedMps + 0.5);
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
