namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Xunit;

public sealed class SurfaceImpactAnchoringTests
{
    [Fact]
    public void DestroyedWreckFollowsOrbitingRotatingEarthInsteadOfEscaping()
    {
        string data = Path.Combine(FindRepoRoot().FullName, "data");
        var universe = Universe.LoadFromDataDirectory(data);
        var earth = universe.GetBody("earth")!;
        var vessel = new Vessel("impact-anchor-regression")
        {
            Name = "Impact anchor regression",
            ReferenceBodyId = earth.Id,
            SASEnabled = false,
        };
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(data, "parts"));
        var command = new Part(catalog["command_pod_mk1"]);
        vessel.Parts.SetRoot(command);

        Vector3d up = Vector3d.Up;
        vessel.Position = earth.Position
            + up * (earth.Radius + 0.5);
        vessel.Velocity = earth.Velocity
            + earth.GetSurfaceVelocity(vessel.Position)
            - up * 250.0;
        universe.AddVessel(vessel);
        universe.SetActiveVessel(vessel.Id);

        universe.Tick(0.1);

        Assert.True(vessel.IsDestroyed);
        Assert.Equal(
            VesselDestructionCause.GroundImpact,
            vessel.DestructionCause);
        Assert.True(vessel.IsSurfaceSettled);
        Assert.Equal(earth.Id, vessel.ReferenceBodyId);
        Assert.InRange(vessel.CrashImpactSpeed, 240.0, 270.0);

        var save = SaveGameV2Codec.Capture(universe);
        var restoredUniverse = Universe.LoadFromDataDirectory(data);
        SaveGameV2Codec.Restore(restoredUniverse, save, catalog);
        vessel = Assert.Single(restoredUniverse.Vessels);
        earth = restoredUniverse.GetBody("earth")!;
        universe = restoredUniverse;

        Assert.True(vessel.IsSurfaceSettled);
        Assert.Equal(earth.Id, vessel.ReferenceBodyId);
        Assert.InRange(vessel.GroundOffset, 0.49, 0.51);

        // The original bug froze the wreck in heliocentric coordinates. After this much
        // simulated time Earth had moved tens of thousands of kilometres, making the wreck
        // appear to rebound above escape velocity. Exercise the pure-rails path explicitly.
        universe.TimeScale = 100_000.0;
        universe.Tick(0.1);

        double altitude = earth.GetAltitude(vessel.Position);
        double surfaceSpeed = vessel.GetSurfaceVelocity(earth).Magnitude;
        Vector3d relativeVelocity = vessel.Velocity - earth.Velocity;
        double specificEarthEnergy =
            0.5 * relativeVelocity.MagnitudeSquared
            - earth.GM / (vessel.Position - earth.Position).Magnitude;

        Assert.InRange(altitude, 0.49, 0.51);
        Assert.InRange(surfaceSpeed, 0.0, 1e-8);
        Assert.True(specificEarthEnergy < 0.0);
        Assert.False(vessel.IsOnRails);
        Assert.Null(vessel.OrbitalState);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repository root.");
    }
}
