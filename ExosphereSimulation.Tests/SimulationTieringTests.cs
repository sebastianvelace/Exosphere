namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Contract tests for the simulation-tier classification introduced as a safe first
/// step toward deferred updates.  These tests intentionally do not assert wall-time
/// savings: the current phase is classification-only and leaves the physics routes
/// unchanged until a temporal wake-up implementation is validated separately.
/// </summary>
public sealed class SimulationTieringTests
{
    [Fact]
    public void ClassificationUsesAllFourTiersWithStablePrecedence()
    {
        var active = Probe("active", 0.0);
        var nearby = Probe("nearby", Universe.NearbyVesselDistance - 1.0);
        var rails = Probe("rails", Universe.HibernatedVesselDistance + 1.0);
        rails.IsOnRails = true;
        var hibernated = Probe("hibernated", Universe.HibernatedVesselDistance + 2.0);

        var universe = new Universe
        {
            ActiveVessel = active,
            TimeScale = 100.0,
        };
        universe.AddVessel(active);
        universe.AddVessel(nearby);
        universe.AddVessel(rails);
        universe.AddVessel(hibernated);

        Assert.Equal(VesselSimulationTier.Active, universe.ClassifySimulationTier(active));
        Assert.Equal(VesselSimulationTier.Nearby, universe.ClassifySimulationTier(nearby));
        Assert.Equal(VesselSimulationTier.OnRails, universe.ClassifySimulationTier(rails));
        Assert.Equal(VesselSimulationTier.Hibernated, universe.ClassifySimulationTier(hibernated));
    }

    [Fact]
    public void ClassificationIsMutuallyExclusiveAndDoesNotMutateVesselState()
    {
        var active = Probe("active", 0.0);
        var candidate = Probe("candidate", Universe.HibernatedVesselDistance + 1.0);
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        var beforePosition = candidate.Position;
        var beforeVelocity = candidate.Velocity;
        var beforeRails = candidate.IsOnRails;
        var beforeOrbit = candidate.OrbitalState;

        var tier = universe.ClassifySimulationTier(candidate);

        Assert.Equal(VesselSimulationTier.Hibernated, tier);
        Assert.Equal(beforePosition, candidate.Position);
        Assert.Equal(beforeVelocity, candidate.Velocity);
        Assert.Equal(beforeRails, candidate.IsOnRails);
        Assert.Same(beforeOrbit, candidate.OrbitalState);
    }

    [Fact]
    public void WakeUpConditionsImmediatelyLeaveHibernatedTier()
    {
        var active = Probe("active", 0.0);
        var candidate = Probe("candidate", Universe.HibernatedVesselDistance + 1.0);
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        Assert.Equal(VesselSimulationTier.Hibernated, universe.ClassifySimulationTier(candidate));

        candidate.Throttle = 0.5;
        Assert.Equal(VesselSimulationTier.Nearby, universe.ClassifySimulationTier(candidate));

        candidate.Throttle = 0.0;
        universe.ActiveVessel = candidate;
        Assert.Equal(VesselSimulationTier.Active, universe.ClassifySimulationTier(candidate));
    }

    [Fact]
    public void ClassificationIsDeterministicRegardlessOfVesselInsertionOrder()
    {
        var first = CreateUniverse(reverseOrder: false);
        var second = CreateUniverse(reverseOrder: true);

        foreach (var id in new[] { "active", "nearby", "rails", "hibernated" })
        {
            var firstVessel = first.Vessels.Single(vessel => vessel.Id == id);
            var secondVessel = second.Vessels.Single(vessel => vessel.Id == id);
            Assert.Equal(
                first.ClassifySimulationTier(firstVessel),
                second.ClassifySimulationTier(secondVessel));
        }
    }

    [Fact]
    public void ActiveVesselRemainsFullPhysicsAndFiniteAfterTierQueries()
    {
        var active = Probe("active", 0.0);
        var distant = Probe("distant", Universe.HibernatedVesselDistance + 1.0);
        var universe = new Universe { ActiveVessel = active, TimeScale = 1.0 };
        universe.AddVessel(active);
        universe.AddVessel(distant);

        Assert.Equal(VesselSimulationTier.Active, universe.ClassifySimulationTier(active));
        Assert.Equal(VesselPhysicsWorkload.FullPhysics, universe.ClassifyMixedPhysicsWorkload(active));
        AssertFinite(active.Position);
        AssertFinite(active.Velocity);
        Assert.Equal(VesselSimulationTier.Nearby, universe.ClassifySimulationTier(distant));
    }

    [Fact]
    public void NonFinitePositionUsesNearbyFailSafeInsteadOfDeferredTier()
    {
        var active = Probe("active", 0.0);
        var corrupted = Probe("corrupted", Universe.HibernatedVesselDistance + 1.0);
        corrupted.Position = new Vector3d(double.NaN, 0.0, 0.0);
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };

        Assert.Equal(VesselSimulationTier.Nearby, universe.ClassifySimulationTier(corrupted));
    }

    [Fact]
    public void DestroyedVesselIsTerminallyHibernatedAndNullIsRejected()
    {
        var universe = new Universe();
        var wreck = Probe("wreck", 0.0);
        wreck.IsDestroyed = true;

        Assert.Equal(VesselSimulationTier.Hibernated, universe.ClassifySimulationTier(wreck));
        Assert.Throws<ArgumentNullException>(() => universe.ClassifySimulationTier(null!));
    }

    private static Universe CreateUniverse(bool reverseOrder)
    {
        var active = Probe("active", 0.0);
        var nearby = Probe("nearby", Universe.NearbyVesselDistance - 1.0);
        var rails = Probe("rails", Universe.HibernatedVesselDistance + 1.0);
        rails.IsOnRails = true;
        var hibernated = Probe("hibernated", Universe.HibernatedVesselDistance + 2.0);

        var vessels = new[] { active, nearby, rails, hibernated };
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        foreach (var vessel in reverseOrder ? vessels.Reverse() : vessels)
        {
            universe.AddVessel(vessel);
        }
        return universe;
    }

    private static Vessel Probe(string id, double distanceFromOrigin)
    {
        return new Vessel(id)
        {
            Position = Vector3d.Right * distanceFromOrigin,
            Velocity = Vector3d.Zero,
            SASEnabled = false,
        };
    }

    private static void AssertFinite(Vector3d value)
    {
        Assert.True(double.IsFinite(value.X));
        Assert.True(double.IsFinite(value.Y));
        Assert.True(double.IsFinite(value.Z));
    }
}
