namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Parity fixtures for the phase-45 interest adapter.  These tests deliberately inspect
/// decisions without enabling deferred dispatch: the legacy scheduler remains the
/// authoritative path until every wake-up/event boundary has an equivalent fixture.
/// </summary>
public sealed class SimulationInterestUniverseParityTests
{
    [Fact]
    public void ActiveVesselIsAlwaysFullResolutionAndTheQueryIsReadOnly()
    {
        var body = Body();
        var vessel = SafeRailVessel(body, "active");
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);

        Vector3d positionBefore = vessel.Position;
        Vector3d velocityBefore = vessel.Velocity;
        bool railsBefore = vessel.IsOnRails;
        double epochBefore = universe.CurrentTime;

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(vessel);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        Assert.False(decision.IsFailClosed);
        Assert.Equal(positionBefore, vessel.Position);
        Assert.Equal(velocityBefore, vessel.Velocity);
        Assert.Equal(railsBefore, vessel.IsOnRails);
        Assert.Equal(epochBefore, universe.CurrentTime);
    }

    [Fact]
    public void CoastingRailVesselGetsADormantDecisionWithoutStateMutation()
    {
        var body = Body();
        var active = new Vessel("active")
        {
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var candidate = SafeRailVessel(body, "coasting");
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        Vector3d positionBefore = candidate.Position;
        Vector3d velocityBefore = candidate.Velocity;
        OrbitalElements? orbitBefore = candidate.OrbitalState;

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.True(
            decision.Tier == SimulationInterestTier.Dormant,
            $"Unexpected interest decision: {decision}; "
            + $"position={candidate.Position}, active={active.Position}, "
            + $"control={candidate.ControlAuthorityFactor}, "
            + $"deadline={universe.GetPhysicsSchedulerDeadlinePlan(candidate)}");
        Assert.Equal(SimulationWakeReason.None, decision.WakeReasons);
        Assert.True(decision.AllowsDeferredWork);
        Assert.Equal(positionBefore, candidate.Position);
        Assert.Equal(velocityBefore, candidate.Velocity);
        Assert.Same(orbitBefore, candidate.OrbitalState);
    }

    [Fact]
    public void StagingOrACommandWakesAPreviouslyDeferrableVessel()
    {
        var body = Body();
        var active = new Vessel("active")
        {
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var candidate = SafeRailVessel(body, "staging-fragment");
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        SimulationInterestDecision coastDecision =
            universe.GetSimulationInterestDecision(candidate);
        Assert.True(
            coastDecision.AllowsDeferredWork,
            $"Unexpected coast decision: {coastDecision}; "
            + $"deadline={universe.GetPhysicsSchedulerDeadlinePlan(candidate)}");

        // A newly staged fragment that receives a burn/engine command must not remain in
        // a dormant bucket. This is the same wake contract a future Stage() adapter
        // must preserve before it can opt into deferred dispatch.
        candidate.Throttle = 0.5;
        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.Thrust);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.Command);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.AtmosphereReentry);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void AttitudeCommandWakesADeferredVesselWithZeroThrottle()
    {
        var body = Body();
        var active = new Vessel("active")
        {
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var candidate = SafeRailVessel(body, "attitude-command");
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        candidate.PitchYawRoll = new Vector3d(0.25, 0.0, 0.0);

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        Assert.Equal(0.0, candidate.Throttle);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.Command);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.AtmosphereReentry);
        Assert.False(decision.AllowsDeferredWork);
        Assert.Equal(
            VesselPhysicsWorkload.FullPhysics,
            universe.ClassifyMixedPhysicsWorkload(candidate));
    }

    [Fact]
    public void NonFiniteAttitudeCommandFailsClosed()
    {
        var body = Body();
        var candidate = SafeRailVessel(body, "invalid-attitude-command");
        candidate.PitchYawRoll = new Vector3d(double.NaN, 0.0, 0.0);
        var universe = new Universe();
        universe.AddBody(body);
        universe.AddVessel(candidate);

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.InvalidInput);
        Assert.True(decision.IsFailClosed);
        Assert.Equal(
            VesselPhysicsWorkload.FullPhysics,
            universe.ClassifyMixedPhysicsWorkload(candidate));
    }

    [Fact]
    public void DockingConnectionWakesTheSecondaryVessel()
    {
        var body = Body();
        var primary = new Vessel("primary")
        {
            Position = body.Position + Vector3d.Right * (body.Radius + 1_500_000.0),
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var secondary = new Vessel("secondary")
        {
            Position = primary.Position + Vector3d.Up * 0.2,
            Velocity = Vector3d.Zero,
            Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI),
            ReferenceBodyId = body.Id,
        };
        primary.Parts.SetRoot(DockingPort("primary-port"));
        secondary.Parts.SetRoot(DockingPort("secondary-port"));

        var universe = new Universe { ActiveVessel = primary };
        universe.AddBody(body);
        universe.AddVessel(primary);
        universe.AddVessel(secondary);

        DockingAttempt docking = universe.TryDock(
            primary.Id,
            "primary-port",
            secondary.Id,
            "secondary-port",
            "interest-parity-dock");

        Assert.True(docking.Succeeded, $"Docking failed: {docking.Failure}");
        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(secondary);

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.DockingContact);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void PeriapsisDeadlineIsClassifiedAsSoiWakeBeforeDeferredWork()
    {
        var body = Body();
        var active = new Vessel("active")
        {
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var candidate = SafeRailVessel(body, "soi-boundary");
        candidate.OrbitalState = new OrbitalElements
        {
            SemiMajorAxis = 10_000_000.0,
            Eccentricity = 0.9,
            PeriapsisRadius = 1_000_000.0,
            ReferenceBodyId = body.Id,
            Epoch = 0.0,
        };
        candidate.IsOnRails = true;

        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        PhysicsSchedulerDeadlinePlan plan =
            universe.GetPhysicsSchedulerDeadlinePlan(candidate);
        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.False(plan.CanDefer);
        Assert.Equal(PhysicsSchedulerDeadlineReason.PeriapsisEvent, plan.Reason);
        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.SoiDeadline);
    }

    [Fact]
    public void TowerCatchApproachIsMissionCriticalAndCannotBeDeferred()
    {
        var body = Body();
        var candidate = SafeRailVessel(body, "edl-catch");
        candidate.IsAttemptingTowerCatch = true;
        candidate.CatchTargetPositionWorld = candidate.Position;
        candidate.CatchTargetEpochSeconds = 0.0;

        var universe = new Universe { ActiveVessel = null };
        universe.AddBody(body);
        universe.AddVessel(candidate);

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.DockingContact);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.AtmosphereReentry);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.MissionCriticalState);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void InvalidPhysicalStateFailsClosedInsteadOfEnteringDeferredTiers()
    {
        var body = Body();
        var candidate = SafeRailVessel(body, "invalid");
        candidate.Position = new Vector3d(double.NaN, 0.0, 0.0);
        var universe = new Universe();
        universe.AddBody(body);
        universe.AddVessel(candidate);

        SimulationInterestDecision decision =
            universe.GetSimulationInterestDecision(candidate);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.InvalidInput);
        Assert.True(decision.IsFailClosed);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void ExternalSystemsAlertIsObservedWithoutMutatingUniverseState()
    {
        var body = Body();
        var active = new Vessel("active")
        {
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
            ReferenceBodyId = body.Id,
        };
        var candidate = SafeRailVessel(body, "systems-alert");
        var universe = new Universe { ActiveVessel = active, TimeScale = 100.0 };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(candidate);

        Vector3d positionBefore = candidate.Position;
        Vector3d velocityBefore = candidate.Velocity;
        bool railsBefore = candidate.IsOnRails;
        double epochBefore = universe.CurrentTime;

        SimulationInterestDecision decision = universe.GetSimulationInterestDecision(
            candidate,
            SimulationExternalInterestInputs.None with { HasSystemsAlert = true });

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        AssertContainsFlag(decision.WakeReasons, SimulationWakeReason.SystemsAlert);
        Assert.Equal(positionBefore, candidate.Position);
        Assert.Equal(velocityBefore, candidate.Velocity);
        Assert.Equal(railsBefore, candidate.IsOnRails);
        Assert.Equal(epochBefore, universe.CurrentTime);
    }

    [Fact]
    public void PolicyMatrixCoversStagingDockingSoiEdlAndSystemsWakeReasons()
    {
        var cases = new[]
        {
            ("staging", new SimulationInterestInputs(
                false, false, false, false, true, true, false, false, false,
                null, false, 10_000_000.0, null),
                SimulationInterestTier.Proximity, SimulationWakeReason.Thrust | SimulationWakeReason.Command),
            ("docking", new SimulationInterestInputs(
                false, false, false, false, false, false, true, false, false,
                null, false, 10_000_000.0, 0.0),
                SimulationInterestTier.Proximity, SimulationWakeReason.DockingContact),
            ("soi", new SimulationInterestInputs(
                false, false, false, false, false, false, false, false, true,
                null, false, 10_000_000.0, null),
                SimulationInterestTier.Proximity, SimulationWakeReason.SoiDeadline),
            ("edl", new SimulationInterestInputs(
                false, false, false, false, false, false, true, true, false,
                null, false, 10_000_000.0, 0.0),
                SimulationInterestTier.Proximity,
                SimulationWakeReason.DockingContact | SimulationWakeReason.AtmosphereReentry),
            ("systems", new SimulationInterestInputs(
                false, false, false, false, false, false, false, false, false,
                null, true, 10_000_000.0, null),
                SimulationInterestTier.Active, SimulationWakeReason.MissionCriticalState),
        };

        foreach (var (_, inputs, expectedTier, expectedReasons) in cases)
        {
            SimulationInterestDecision decision = SimulationInterestPolicy.Classify(inputs);
            Assert.Equal(expectedTier, decision.Tier);
            Assert.Equal(expectedReasons, decision.WakeReasons);
            Assert.False(decision.AllowsDeferredWork);
        }
    }

    private static void AssertContainsFlag(
        SimulationWakeReason actual,
        SimulationWakeReason expected)
    {
        Assert.True(
            (actual & expected) == expected,
            $"Expected wake reason {expected} in {actual}.");
    }

    private static CelestialBody Body() => new()
    {
        Id = "interest-test-body",
        Name = "Interest Test Body",
        Mass = 5.972e24,
        GM = 3.986004418e14,
        Radius = 6_371_000.0,
        SphereOfInfluence = 1.0e12,
    };

    private static Vessel SafeRailVessel(CelestialBody body, string id)
    {
        double radius = body.Radius + 1_500_000.0;
        Vector3d position = body.Position + Vector3d.Right * radius;
        Vector3d velocity = body.Velocity
            + Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        var vessel = new Vessel(id)
        {
            Position = position,
            Velocity = velocity,
            ReferenceBodyId = body.Id,
            IsOnRails = true,
            OrbitalState = OrbitalElements.FromStateVector(
                position - body.Position,
                velocity - body.Velocity,
                body.GM,
                body.Id,
                0.0),
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "interest-test-command",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 4.0,
            DiameterM = 2.0,
        }, $"{id}-command"));
        return vessel;
    }

    private static Part DockingPort(string instanceId) => new(new PartDefinition
    {
        Id = "interest-test-docking-port",
        CategoryStr = "command",
        MassDry = 1_000.0,
        LengthM = 2.0,
        DiameterM = 2.0,
        IsDockingPort = true,
        DockingNodeId = "dock",
        DockingAxisLocal = [0.0, 1.0, 0.0],
        AttachmentNodes =
        [
            new AttachmentNodeDef
            {
                Id = "dock",
                Position = [0.0, 0.0, 0.0],
                Size = 1,
                Type = "docking",
            },
        ],
    }, instanceId);
}
