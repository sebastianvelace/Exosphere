namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Regression coverage for the mixed scheduler's dispatch boundary.  These tests
/// intentionally exercise the public simulation path rather than measuring wall time:
/// a timing assertion would be machine-dependent, while proving that zero vessels enter
/// RK4 is deterministic and directly observable through the resulting state.
/// </summary>
public sealed class PhysicsSchedulerPerformanceTests
{
    [Fact]
    public void ClassificationSkipsDestroyedAndSettledVesselsButKeepsActiveThrustFullPhysics()
    {
        var earth = LoadBody("earth");
        var universe = new Universe { TimeScale = 100.0 };
        universe.AddBody(earth);

        var wreck = new Vessel("destroyed") { IsDestroyed = true };
        var settled = SettledVessel(earth, "settled");
        var activeThrusting = PoweredVessel(earth, "active-thrusting");
        activeThrusting.IsOnRails = true;
        activeThrusting.Throttle = 1.0;

        Assert.Equal(
            VesselPhysicsWorkload.Destroyed,
            universe.ClassifyMixedPhysicsWorkload(wreck));
        Assert.Equal(
            VesselPhysicsWorkload.SurfaceSettled,
            universe.ClassifyMixedPhysicsWorkload(settled));

        universe.ActiveVessel = activeThrusting;
        Assert.Equal(
            VesselPhysicsWorkload.FullPhysics,
            universe.ClassifyMixedPhysicsWorkload(activeThrusting));
    }

    [Fact]
    public void ActiveVesselOnRailsBelowWarpTenIsNotClassifiedAsAnalytic()
    {
        var earth = LoadBody("earth");
        var active = CoastVessel(earth, "active-low-warp");
        active.IsOnRails = true;
        var universe = new Universe
        {
            TimeScale = 5.0,
            ActiveVessel = active,
        };
        universe.AddBody(earth);
        universe.AddVessel(active);

        Assert.Equal(
            VesselPhysicsWorkload.FullPhysics,
            universe.ClassifyMixedPhysicsWorkload(active));

        universe.Tick(0.02);

        Assert.False(active.IsOnRails);
        Assert.Null(active.OrbitalState);
    }

    [Fact]
    public void NonActiveCoastingRailsVesselPropagatesWithoutLeavingRails()
    {
        var earth = LoadBody("earth");
        var vessel = CoastVessel(earth, "coasting-rails");
        vessel.IsOnRails = true;
        var initialPosition = vessel.Position;
        var universe = new Universe { TimeScale = 100.0 };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.Equal(
            VesselPhysicsWorkload.OnRails,
            universe.ClassifyMixedPhysicsWorkload(vessel));

        universe.Tick(0.02);

        Assert.True(vessel.IsOnRails);
        Assert.NotNull(vessel.OrbitalState);
        Assert.True((vessel.Position - initialPosition).Magnitude > 0.0);
    }

    [Fact]
    public void RailsClassificationAndPropagationAreDeterministic()
    {
        var first = CreateCoastingUniverse("deterministic-a");
        var second = CreateCoastingUniverse("deterministic-b");

        Assert.Equal(
            first.ClassifyMixedPhysicsWorkload(first.Vessels[0]),
            second.ClassifyMixedPhysicsWorkload(second.Vessels[0]));

        for (int i = 0; i < 5; i++)
        {
            first.Tick(0.02);
            second.Tick(0.02);
        }

        var a = first.Vessels[0];
        var b = second.Vessels[0];
        AssertVectorClose(a.Position, b.Position, 1e-9);
        AssertVectorClose(a.Velocity, b.Velocity, 1e-12);
        Assert.Equal(a.IsOnRails, b.IsOnRails);
        Assert.Equal(a.OrbitalState?.ReferenceBodyId, b.OrbitalState?.ReferenceBodyId);
    }

    private static Universe CreateCoastingUniverse(string vesselId)
    {
        var earth = LoadBody("earth");
        var vessel = CoastVessel(earth, vesselId);
        vessel.IsOnRails = true;
        var universe = new Universe { TimeScale = 100.0 };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Vessel CoastVessel(CelestialBody earth, string id)
    {
        var vessel = new Vessel(id)
        {
            Position = earth.Position + Vector3d.Right * (earth.Radius + 1_000_000.0),
            Velocity = earth.Velocity + Vector3d.Up * 7_350.0,
            ReferenceBodyId = earth.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "scheduler-probe",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 5.0,
            DiameterM = 2.0,
        }));
        return vessel;
    }

    private static Vessel PoweredVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        vessel.Position = earth.Position + Vector3d.Up * (earth.Radius + 10_000.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        return vessel;
    }

    private static Vessel SettledVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        vessel.Position = earth.Position + Vector3d.Up * (earth.Radius - 0.5);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        vessel.ReferenceBodyId = earth.Id;
        var settlingUniverse = new Universe
        {
            TimeScale = 1.0,
            ActiveVessel = vessel,
        };
        settlingUniverse.AddBody(earth);
        settlingUniverse.AddVessel(vessel);
        settlingUniverse.Tick(0.02);
        Assert.True(vessel.IsSurfaceSettled);
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

    private static void AssertVectorClose(Vector3d expected, Vector3d actual, double tolerance)
    {
        Assert.InRange((expected - actual).Magnitude, 0.0, tolerance);
    }
}
