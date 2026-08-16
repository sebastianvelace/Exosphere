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
    // These are physical equivalence bounds, not percentage slack.  The safe-rail
    // pair compares Kepler propagation with the low-warp RK4 reference over a short
    // deterministic window; the tighter velocity bound is intentional because a
    // velocity phase error is what later becomes a visible orbital divergence.
    private const double SafeRailPositionToleranceM = 1e-4;
    private const double SafeRailVelocityToleranceMps = 1e-9;
    private const double ForceSensitivePositionToleranceM = 1e-4;
    private const double ForceSensitiveVelocityToleranceMps = 1e-8;
    private const double SyntheticSoiPositionToleranceM = 1e-6;
    private const double SyntheticSoiVelocityToleranceMps = 1e-9;

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

    [Fact]
    public void MixedSchedulerBoundsSecondaryForceSensitiveVesselAndMatchesFineTick()
    {
        var mixedEarth = LoadBody("earth");
        var mixedActive = CoastVessel(mixedEarth, "mixed-active");
        var mixedSecondary = CoastVessel(mixedEarth, "mixed-secondary");
        mixedSecondary.Position = mixedEarth.Position
            + Vector3d.Right * (mixedEarth.Radius + 150_000.0);
        mixedSecondary.Velocity = mixedEarth.Velocity + Vector3d.Up * 7_700.0;

        var mixed = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = mixedActive,
        };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedActive);
        mixed.AddVessel(mixedSecondary);

        Assert.False(mixed.RequiresOffRailsPhysics(mixedActive));
        Assert.True(mixed.RequiresOffRailsPhysics(mixedSecondary));

        // One 20 ms simulated slice at warp x100. The old active-only policy selected
        // MaxCoastStep=2 s here; the secondary would then receive a coarser RK4 step.
        mixed.Tick(0.0002);
        Assert.InRange(mixed.LastMixedPhysicsStepCap, 0.0, 0.020000001);
        Assert.True(double.IsFinite(mixedSecondary.Position.X));
        Assert.True(double.IsFinite(mixedSecondary.Velocity.X));
        Assert.False(mixedSecondary.IsOnRails);

        var fineEarth = LoadBody("earth");
        var fineActive = CoastVessel(fineEarth, "fine-active");
        var fineSecondary = CoastVessel(fineEarth, "fine-secondary");
        fineSecondary.Position = fineEarth.Position
            + Vector3d.Right * (fineEarth.Radius + 150_000.0);
        fineSecondary.Velocity = fineEarth.Velocity + Vector3d.Up * 7_700.0;
        var fine = new Universe
        {
            TimeScale = 1.0,
            ActiveVessel = fineSecondary,
        };
        fine.AddBody(fineEarth);
        fine.AddVessel(fineActive);
        fine.AddVessel(fineSecondary);
        fine.Tick(0.02);

        AssertVectorClose(fineSecondary.Position, mixedSecondary.Position, 1e-5);
        AssertVectorClose(fineSecondary.Velocity, mixedSecondary.Velocity, 1e-8);
    }

    [Fact]
    public void SchedulerTelemetryCountsMixedWorkloadWithoutSkippingVessels()
    {
        var earth = LoadBody("earth");
        var activeRails = CoastVessel(earth, "telemetry-active");
        var secondaryPhysics = CoastVessel(earth, "telemetry-atmosphere");
        secondaryPhysics.Position = earth.Position
            + Vector3d.Right * (earth.Radius + 150_000.0);
        secondaryPhysics.Velocity = earth.Velocity + Vector3d.Up * 7_700.0;
        var nonActiveRails = CoastVessel(earth, "telemetry-rails");
        nonActiveRails.IsOnRails = true;
        var wreck = new Vessel("telemetry-wreck") { IsDestroyed = true };

        var universe = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = activeRails,
        };
        universe.AddBody(earth);
        universe.AddVessel(activeRails);
        universe.AddVessel(secondaryPhysics);
        universe.AddVessel(nonActiveRails);
        universe.AddVessel(wreck);

        universe.Tick(0.0002);

        PhysicsSchedulerTelemetry telemetry = universe.LastSchedulerTelemetry;
        Assert.Equal(PhysicsSchedulerBranch.Mixed, telemetry.Branch);
        Assert.InRange(telemetry.RealDeltaTime, 0.000199999, 0.000200001);
        Assert.InRange(telemetry.SimulatedSeconds, 0.01999999, 0.02000001);
        Assert.Equal(1, telemetry.OuterSubsteps);
        Assert.Equal(1, telemetry.FullPhysicsDispatches);
        Assert.Equal(2, telemetry.OnRailsDispatches);
        Assert.Equal(1, telemetry.DestroyedDispatches);
        Assert.Equal(0, telemetry.SurfaceSettledDispatches);
        Assert.Equal(0, telemetry.GroundHeldDispatches);
        Assert.Equal(0, telemetry.DockedSecondarySkips);
        Assert.Equal(2, telemetry.RailsSlices);
        Assert.Equal(4, telemetry.TotalWorkDispatches);
        Assert.Equal(universe.LastMixedPhysicsStepCap, telemetry.EffectiveStepCap);
    }

    [Fact]
    public void SchedulerTelemetryIdentifiesFullPhysicsAndPureRailsBranches()
    {
        var fullEarth = LoadBody("earth");
        var fullVessel = CoastVessel(fullEarth, "telemetry-full");
        var full = new Universe
        {
            TimeScale = 1.0,
            ActiveVessel = fullVessel,
        };
        full.AddBody(fullEarth);
        full.AddVessel(fullVessel);

        full.Tick(0.02);

        Assert.Equal(PhysicsSchedulerBranch.FullPhysics, full.LastSchedulerTelemetry.Branch);
        Assert.Equal(1, full.LastSchedulerTelemetry.OuterSubsteps);
        Assert.Equal(1, full.LastSchedulerTelemetry.FullPhysicsDispatches);
        Assert.Equal(0, full.LastSchedulerTelemetry.OnRailsDispatches);
        Assert.Equal(0, full.LastSchedulerTelemetry.RailsSlices);

        var railsEarth = LoadBody("earth");
        var railsVessel = CoastVessel(railsEarth, "telemetry-pure-rails");
        railsVessel.IsOnRails = true;
        var rails = new Universe { TimeScale = 2_000.0 };
        rails.AddBody(railsEarth);
        rails.AddVessel(railsVessel);

        rails.Tick(0.02);

        Assert.Equal(PhysicsSchedulerBranch.Rails, rails.LastSchedulerTelemetry.Branch);
        Assert.Equal(1, rails.LastSchedulerTelemetry.OuterSubsteps);
        Assert.Equal(0, rails.LastSchedulerTelemetry.FullPhysicsDispatches);
        Assert.Equal(1, rails.LastSchedulerTelemetry.OnRailsDispatches);
        Assert.True(rails.LastSchedulerTelemetry.RailsSlices > 0);
        Assert.InRange(rails.LastSchedulerTelemetry.SimulatedSeconds, 39.99999, 40.00001);
    }

    [Fact]
    public void SchedulerTelemetryFlagsLargeCatchUpWithoutChangingSimulatedTime()
    {
        var earth = LoadBody("earth");
        var vessel = CoastVessel(earth, "catch-up-warning");
        var universe = new Universe
        {
            ActiveVessel = vessel,
            TimeScale = 1_000.0,
        };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        universe.Tick(0.5);

        Assert.Equal(PhysicsSchedulerBranch.Mixed, universe.LastSchedulerTelemetry.Branch);
        Assert.Equal(500.0, universe.LastSchedulerTelemetry.SimulatedSeconds, 10);
        Assert.Equal(250, universe.LastSchedulerTelemetry.OuterSubsteps);
        Assert.True(universe.LastSchedulerTelemetry.CatchUpRisk);
        Assert.True(double.IsFinite(universe.LastSchedulerTelemetry.WallClockMilliseconds));
        Assert.True(universe.LastSchedulerTelemetry.WallClockMilliseconds >= 0.0);
        Assert.Equal(500.0, universe.CurrentTime, 10);
    }

    [Fact]
    public void SchedulerRejectsInvalidDeltaWithoutCorruptingClock()
    {
        var earth = LoadBody("earth");
        var vessel = CoastVessel(earth, "invalid-delta");
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        universe.Tick(double.NaN);
        Assert.Equal(0.0, universe.CurrentTime);
        Assert.Equal(PhysicsSchedulerBranch.None, universe.LastSchedulerTelemetry.Branch);
        Assert.Equal(0.0, universe.LastSchedulerTelemetry.SimulatedSeconds);

        universe.Tick(-1.0);
        Assert.Equal(0.0, universe.CurrentTime);
        Assert.Equal(PhysicsSchedulerBranch.None, universe.LastSchedulerTelemetry.Branch);

        universe.TimeScale = double.NaN;
        universe.Tick(0.02);
        Assert.Equal(0.0, universe.CurrentTime);
        Assert.Equal(PhysicsSchedulerBranch.None, universe.LastSchedulerTelemetry.Branch);
    }

    [Fact]
    public void SchedulerTelemetryDistinguishesUninitializedPauseAndInvalidInputs()
    {
        var earth = LoadBody("earth");
        var vessel = CoastVessel(earth, "scheduler-skip-reasons");
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.False(universe.LastSchedulerTelemetry.IsInitialized);
        Assert.Equal(
            PhysicsSchedulerSkipReason.NotInitialized,
            universe.LastSchedulerTelemetry.SkipReason);

        universe.TimeScale = 0.0;
        universe.Tick(0.02);
        Assert.True(universe.LastSchedulerTelemetry.IsInitialized);
        Assert.Equal(
            PhysicsSchedulerSkipReason.Paused,
            universe.LastSchedulerTelemetry.SkipReason);
        Assert.Equal(PhysicsSchedulerBranch.None, universe.LastSchedulerTelemetry.Branch);

        universe.TimeScale = 1.0;
        universe.Tick(double.NaN);
        Assert.Equal(
            PhysicsSchedulerSkipReason.InvalidDelta,
            universe.LastSchedulerTelemetry.SkipReason);

        universe.TimeScale = double.PositiveInfinity;
        universe.Tick(0.02);
        Assert.Equal(
            PhysicsSchedulerSkipReason.InvalidTimeScale,
            universe.LastSchedulerTelemetry.SkipReason);

        universe.TimeScale = 1.0;
        universe.Tick(0.001);
        Assert.Equal(
            PhysicsSchedulerSkipReason.None,
            universe.LastSchedulerTelemetry.SkipReason);
        Assert.Equal(PhysicsSchedulerBranch.FullPhysics, universe.LastSchedulerTelemetry.Branch);
    }

    [Fact]
    public void DeferredRailsProjectsCurrentEpochAndMatchesAlwaysCheckedReference()
    {
        var mixedEarth = LoadBody("earth");
        var mixedActive = PoweredVessel(mixedEarth, "deadline-active");
        var mixedRails = SafeRailVessel(mixedEarth, "deadline-rails");
        mixedRails.IsOnRails = true;
        var mixed = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = mixedActive,
        };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedActive);
        mixed.AddVessel(mixedRails);

        var referenceEarth = LoadBody("earth");
        var referenceRails = SafeRailVessel(referenceEarth, "reference-rails");
        referenceRails.IsOnRails = true;
        var reference = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = referenceRails,
        };
        reference.AddBody(referenceEarth);
        reference.AddVessel(referenceRails);

        // The first two ticks establish a conic and its first event-safe epoch.  The
        // following ticks are eligible for projection, so the public state must still
        // advance every tick even though the expensive event scan is deferred.
        for (int i = 0; i < 15; i++)
        {
            mixed.Tick(0.005);
            reference.Tick(0.005);
        }

        Assert.InRange(mixed.CurrentTime, 7.499999, 7.500001);
        Assert.True(mixed.LastSchedulerTelemetry.DeadlineEligibleEvaluations > 0);
        Assert.True(mixed.LastSchedulerTelemetry.DeadlineDeferredSkips > 0);
        Assert.True(mixed.LastSchedulerTelemetry.DeadlineProjectedDispatches > 0);
        Assert.True(mixedRails.IsOnRails);
        AssertVectorClose(referenceRails.Position, mixedRails.Position, 1e-4);
        AssertVectorClose(referenceRails.Velocity, mixedRails.Velocity, 1e-9);
    }

    [Fact]
    public void DeferredRailsCatchesUpBeforeForceSensitiveWake()
    {
        var earth = LoadBody("earth");
        var active = PoweredVessel(earth, "wake-active");
        var rails = SafeRailVessel(earth, "wake-rails");
        rails.IsOnRails = true;
        var universe = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = active,
        };
        universe.AddBody(earth);
        universe.AddVessel(active);
        universe.AddVessel(rails);

        universe.Tick(0.005);
        universe.Tick(0.005);
        universe.Tick(0.005);
        Assert.True(rails.IsOnRails);

        rails.Throttle = 0.1;
        universe.Tick(0.005);

        Assert.True(universe.LastSchedulerTelemetry.DeadlineCatchUpDispatches > 0);
        Assert.True(universe.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.False(rails.IsOnRails);
        Assert.True(double.IsFinite(rails.Position.X));
        Assert.True(double.IsFinite(rails.Position.Y));
        Assert.True(double.IsFinite(rails.Position.Z));
        Assert.True(double.IsFinite(rails.Velocity.X));
        Assert.True(double.IsFinite(rails.Velocity.Y));
        Assert.True(double.IsFinite(rails.Velocity.Z));
    }

    [Fact]
    public void DeferredRailsDeadlineMatchesAlwaysCheckedReferenceWhenDeadlineExpires()
    {
        var mixedEarth = LoadBody("earth");
        var mixedRails = SafeRailVessel(mixedEarth, "deadline-expiry-rails");
        mixedRails.IsOnRails = true;
        var mixed = new Universe { TimeScale = 5.0 };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedRails);

        var referenceEarth = LoadBody("earth");
        var referenceRails = SafeRailVessel(referenceEarth, "deadline-expiry-reference");
        referenceRails.IsOnRails = true;
        var reference = new Universe
        {
            // Below warp 10 an active rail vessel is deliberately integrated by RK4
            // every substep.  This is the always-checked reference for the deadline path.
            TimeScale = 5.0,
            ActiveVessel = referenceRails,
        };
        reference.AddBody(referenceEarth);
        reference.AddVessel(referenceRails);

        // 85 × (0.005 × 5) = 2.125 s.  MaxCoastStep is 2 s, so this crosses the
        // first independent rail deadline instead of testing projection only.
        for (int i = 0; i < 85; i++)
        {
            mixed.Tick(0.005);
            reference.Tick(0.005);
        }

        Assert.Equal(reference.CurrentTime, mixed.CurrentTime, 12);
        Assert.True(mixed.LastSchedulerTelemetry.DeadlineEligibleEvaluations > 0);
        Assert.True(mixed.LastSchedulerTelemetry.DeadlineDeferredSkips > 0);
        Assert.True(
            mixed.LastSchedulerTelemetry.DeadlineProjectedDispatches > 0,
            "the mixed path never exercised a deferred deadline projection");
        Assert.True(
            mixedRails.IsOnRails,
            "a safe coasting vessel must remain analytic after its deadline is serviced");
        Assert.False(
            referenceRails.IsOnRails,
            "the reference must remain on the always-checked low-warp path");
        AssertVesselStateClose(
            referenceRails,
            mixedRails,
            SafeRailPositionToleranceM,
            SafeRailVelocityToleranceMps);
    }

    [Fact]
    public void DeferredRailsForceWakeMatchesAlwaysCheckedReference()
    {
        var mixedEarth = LoadBody("earth");
        var mixedRails = SafeRailVessel(mixedEarth, "deadline-command-rails");
        mixedRails.IsOnRails = true;
        var mixed = new Universe { TimeScale = 5.0 };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedRails);

        var referenceEarth = LoadBody("earth");
        var referenceRails = SafeRailVessel(referenceEarth, "deadline-command-reference");
        referenceRails.IsOnRails = true;
        var reference = new Universe
        {
            TimeScale = 5.0,
            ActiveVessel = referenceRails,
        };
        reference.AddBody(referenceEarth);
        reference.AddVessel(referenceRails);

        for (int i = 0; i < 3; i++)
        {
            mixed.Tick(0.005);
            reference.Tick(0.005);
        }

        // The command is applied at the same public epoch.  The mixed scheduler must
        // restore its anchored conic before RK4; comparing only finiteness would miss a
        // phase jump here.
        mixedRails.Throttle = 0.1;
        referenceRails.Throttle = 0.1;
        mixed.Tick(0.005);
        reference.Tick(0.005);

        Assert.True(mixed.LastSchedulerTelemetry.DeadlineCatchUpDispatches > 0);
        Assert.True(mixed.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.False(mixedRails.IsOnRails);
        Assert.False(referenceRails.IsOnRails);
        AssertVesselStateClose(
            referenceRails,
            mixedRails,
            SafeRailPositionToleranceM,
            SafeRailVelocityToleranceMps);
    }

    [Fact]
    public void AtmosphericRailIsRejectedAndMatchesAlwaysCheckedReference()
    {
        var mixedEarth = LoadBody("earth");
        var mixedAtmospheric = AtmosphericRailVessel(mixedEarth, "atmosphere-rail-equivalence");
        mixedAtmospheric.IsOnRails = true;
        var mixed = new Universe { TimeScale = 100.0 };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedAtmospheric);

        var referenceEarth = LoadBody("earth");
        var referenceAtmospheric = AtmosphericRailVessel(
            referenceEarth,
            "atmosphere-reference-equivalence");
        referenceAtmospheric.IsOnRails = true;
        var reference = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = referenceAtmospheric,
        };
        reference.AddBody(referenceEarth);
        reference.AddVessel(referenceAtmospheric);

        mixed.Tick(0.0002);
        reference.Tick(0.0002);

        Assert.True(mixed.RequiresOffRailsPhysics(mixedAtmospheric));
        Assert.Equal(PhysicsSchedulerDeadlineReason.ForceSensitive,
            mixed.GetPhysicsSchedulerDeadlinePlan(mixedAtmospheric).Reason);
        Assert.Equal(0, mixed.LastSchedulerTelemetry.DeadlineProjectedDispatches);
        Assert.True(mixed.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.False(mixedAtmospheric.IsOnRails);
        AssertVesselStateClose(
            referenceAtmospheric,
            mixedAtmospheric,
            ForceSensitivePositionToleranceM,
            ForceSensitiveVelocityToleranceMps);
    }

    [Fact]
    public void LandingContactRailIsRejectedAndMatchesAlwaysCheckedReference()
    {
        var mixedEarth = LoadBody("earth");
        var mixedContact = LandingContactVessel(mixedEarth, "contact-rail-equivalence");
        mixedContact.IsOnRails = true;
        var mixed = new Universe { TimeScale = 100.0 };
        mixed.AddBody(mixedEarth);
        mixed.AddVessel(mixedContact);

        var referenceEarth = LoadBody("earth");
        var referenceContact = LandingContactVessel(
            referenceEarth,
            "contact-reference-equivalence");
        referenceContact.IsOnRails = true;
        var reference = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = referenceContact,
        };
        reference.AddBody(referenceEarth);
        reference.AddVessel(referenceContact);

        mixed.Tick(0.0002);
        reference.Tick(0.0002);

        Assert.True(mixedContact.HasDeployedLandingGear);
        Assert.True(mixed.RequiresOffRailsPhysics(mixedContact));
        Assert.Equal(PhysicsSchedulerDeadlineReason.ForceSensitive,
            mixed.GetPhysicsSchedulerDeadlinePlan(mixedContact).Reason);
        Assert.Equal(0, mixed.LastSchedulerTelemetry.DeadlineProjectedDispatches);
        Assert.True(mixed.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.False(mixedContact.IsOnRails);
        Assert.Equal(referenceContact.IsSurfaceSettled, mixedContact.IsSurfaceSettled);
        AssertVesselStateClose(
            referenceContact,
            mixedContact,
            ForceSensitivePositionToleranceM,
            ForceSensitiveVelocityToleranceMps);
    }

    [Fact]
    public void SoiCrossingDeadlinePathMatchesAlwaysCheckedReferenceWithoutInertialJump()
    {
        var mixed = CreateSyntheticSoiUniverse(activeReference: false, out Vessel mixedVessel);
        var reference = CreateSyntheticSoiUniverse(activeReference: true, out Vessel referenceVessel);

        // The vessel starts 6 km outside a 5 km lunar SOI and crosses it during the
        // first 2-second mixed slice.  The synthetic body has negligible GM so this
        // isolates the patched-conic frame change from a third-body force difference.
        mixed.Tick(0.5);
        reference.Tick(0.5);

        Assert.Equal("soi-moon", mixedVessel.ReferenceBodyId);
        Assert.Equal("soi-moon", mixedVessel.OrbitalState?.ReferenceBodyId);
        Assert.True(
            mixed.LastSchedulerTelemetry.DeadlineEligibleEvaluations > 0,
            $"SOI fixture did not reach an eligible deadline: {mixed.LastSchedulerTelemetry}; plan={mixed.GetPhysicsSchedulerDeadlinePlan(mixedVessel)}");
        Assert.True(double.IsFinite(mixedVessel.Position.X));
        Assert.True(double.IsFinite(mixedVessel.Velocity.X));
        double soiPositionError = (referenceVessel.Position - mixedVessel.Position).Magnitude;
        double soiVelocityError = (referenceVessel.Velocity - mixedVessel.Velocity).Magnitude;
        Assert.True(
            soiPositionError <= SyntheticSoiPositionToleranceM
                && soiVelocityError <= SyntheticSoiVelocityToleranceMps,
            $"SOI inertial state diverged: position={soiPositionError:R} m, velocity={soiVelocityError:R} m/s; "
                + $"reference={referenceVessel.Position}, rails={mixedVessel.Position}");
        AssertVesselStateClose(
            referenceVessel,
            mixedVessel,
            SyntheticSoiPositionToleranceM,
            SyntheticSoiVelocityToleranceMps);
    }

    [Fact]
    public void AtmosphericRailVesselWakesBeforeMixedSchedulerDispatch()
    {
        var earth = LoadBody("earth");
        var active = CoastVessel(earth, "atmosphere-active");
        var atmospheric = CoastVessel(earth, "atmosphere-rail");
        atmospheric.Position = earth.Position
            + Vector3d.Right * (earth.Radius + 80_000.0);
        atmospheric.Velocity = earth.Velocity + Vector3d.Up * 7_800.0;
        atmospheric.IsOnRails = true;

        var universe = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = active,
        };
        universe.AddBody(earth);
        universe.AddVessel(active);
        universe.AddVessel(atmospheric);

        Assert.True(universe.RequiresOffRailsPhysics(atmospheric));
        PhysicsSchedulerDeadlinePlan plan =
            universe.GetPhysicsSchedulerDeadlinePlan(atmospheric);
        Assert.False(plan.CanDefer);
        Assert.Equal(PhysicsSchedulerDeadlineReason.ForceSensitive, plan.Reason);

        universe.Tick(0.0002);

        Assert.False(atmospheric.IsOnRails);
        Assert.True(universe.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.Equal(0, universe.LastSchedulerTelemetry.DeadlineProjectedDispatches);
        Assert.True(double.IsFinite(atmospheric.Position.X));
        Assert.True(double.IsFinite(atmospheric.Velocity.X));
    }

    [Fact]
    public void LandingContactRailVesselUsesContactCadenceAndFullPhysics()
    {
        var earth = LoadBody("earth");
        var active = CoastVessel(earth, "contact-active");
        var contactVessel = CoastVessel(earth, "contact-rail");
        var gearDefinition = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName,
            "data",
            "parts",
            "starship_landing_gear.json"));
        contactVessel.Parts.AddPart(new Part(gearDefinition) { IsDeployed = true });
        contactVessel.ConfigureLandingContactsFromParts();
        contactVessel.Position = earth.Position
            + Vector3d.Up * (earth.Radius + 8.1);
        contactVessel.Velocity = earth.Velocity
            + earth.GetSurfaceVelocity(contactVessel.Position);
        contactVessel.IsOnRails = true;

        var universe = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = active,
        };
        universe.AddBody(earth);
        universe.AddVessel(active);
        universe.AddVessel(contactVessel);

        Assert.True(contactVessel.HasDeployedLandingGear);
        Assert.True(universe.RequiresOffRailsPhysics(contactVessel));

        universe.Tick(0.0002);

        Assert.Equal(0.005, universe.LastSchedulerTelemetry.EffectiveStepCap, 12);
        Assert.Equal(4, universe.LastSchedulerTelemetry.OuterSubsteps);
        Assert.True(universe.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.False(contactVessel.IsOnRails);
        Assert.True(double.IsFinite(contactVessel.Position.X));
        Assert.True(double.IsFinite(contactVessel.Velocity.X));
    }

    [Fact]
    public void DockedSecondaryIsSkippedWhilePrimaryStillReceivesSchedulerWork()
    {
        var universe = CreateDockingSchedulerUniverse(
            out Vessel primary,
            out Vessel secondary);
        Assert.True(universe.TryDock(
            primary.Id,
            "primary-port",
            secondary.Id,
            "secondary-port",
            "scheduler-dock").Succeeded);

        universe.TimeScale = 100.0;
        universe.ActiveVessel = primary;
        universe.Tick(0.0002);

        Assert.Equal(1, universe.LastSchedulerTelemetry.DockedSecondarySkips);
        Assert.Equal(1, universe.LastSchedulerTelemetry.DockingConstraintApplications);
        Assert.Equal(1, universe.LastSchedulerTelemetry.TotalWorkDispatches);
        Assert.Equal(0, universe.LastSchedulerTelemetry.DeadlineCatchUpDispatches);
        Assert.True(double.IsFinite(primary.Position.X));
        Assert.True(double.IsFinite(secondary.Position.X));
    }

    [Fact]
    public void StagingThenThrottleWakesDetachedFragmentThroughMixedScheduler()
    {
        var earth = LoadBody("earth");
        var stack = BuildFlight7Stack();
        double radius = earth.Radius + 1_000_000.0;
        stack.Position = earth.Position + Vector3d.Right * radius;
        stack.Velocity = earth.Velocity
            + Vector3d.Up * System.Math.Sqrt(earth.GM / radius);
        stack.ReferenceBodyId = earth.Id;

        var universe = new Universe
        {
            TimeScale = 100.0,
            ActiveVessel = stack,
        };
        universe.AddBody(earth);
        universe.AddVessel(stack);

        Vessel detached = Assert.IsType<Vessel>(stack.Stage());
        detached.IsOnRails = true;
        detached.Throttle = 0.1;
        universe.AddVessel(detached);

        universe.Tick(0.0002);

        Assert.False(detached.IsOnRails);
        Assert.True(universe.LastSchedulerTelemetry.FullPhysicsDispatches > 0);
        Assert.True(double.IsFinite(detached.Position.X));
        Assert.True(double.IsFinite(detached.Velocity.X));
    }

    [Fact]
    public void DeadlinePlanRejectsConicsThatEnterProtectedAtmosphere()
    {
        var earth = LoadBody("earth");
        var rails = SafeRailVessel(earth, "deadline-periapsis");
        rails.IsOnRails = true;
        var universe = new Universe { TimeScale = 100.0 };
        universe.AddBody(earth);
        universe.AddVessel(rails);

        universe.Tick(0.02);
        Assert.NotNull(rails.OrbitalState);
        rails.OrbitalState!.PeriapsisRadius = earth.Radius + 1_000.0;

        var plan = universe.GetPhysicsSchedulerDeadlinePlan(rails);

        Assert.False(plan.CanDefer);
        Assert.Equal(PhysicsSchedulerDeadlineReason.PeriapsisEvent, plan.Reason);
        Assert.Equal(0.0, plan.IntervalSeconds);
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

    private static Vessel SafeRailVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        double radius = earth.Radius + 1_500_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity
            + Vector3d.Up * System.Math.Sqrt(earth.GM / radius);
        vessel.ReferenceBodyId = earth.Id;
        return vessel;
    }

    private static Vessel AtmosphericRailVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        vessel.Position = earth.Position
            + Vector3d.Right * (earth.Radius + 80_000.0);
        vessel.Velocity = earth.Velocity + Vector3d.Up * 7_800.0;
        vessel.ReferenceBodyId = earth.Id;
        return vessel;
    }

    private static Vessel LandingContactVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        var gearDefinition = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName,
            "data",
            "parts",
            "starship_landing_gear.json"));
        vessel.Parts.AddPart(new Part(gearDefinition) { IsDeployed = true });
        vessel.ConfigureLandingContactsFromParts();
        vessel.Position = earth.Position
            + Vector3d.Up * (earth.Radius + 8.1);
        vessel.Velocity = earth.Velocity
            + earth.GetSurfaceVelocity(vessel.Position);
        vessel.ReferenceBodyId = earth.Id;
        return vessel;
    }

    private static Universe CreateSyntheticSoiUniverse(
        bool activeReference,
        out Vessel vessel)
    {
        var earth = new CelestialBody
        {
            Id = "soi-earth",
            Mass = 1.0e20,
            GM = 1.0,
            Radius = 1_000.0,
            SphereOfInfluence = 1.0e9,
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
        };
        var moon = new CelestialBody
        {
            Id = "soi-moon",
            Mass = 1.0e10,
            GM = 1.0e-6,
            Radius = 100.0,
            SphereOfInfluence = 5_000.0,
            Position = Vector3d.Right * 100_000.0,
            Velocity = Vector3d.Zero,
        };
        vessel = CoastVessel(earth, activeReference ? "soi-reference" : "soi-rails");
        vessel.Position = moon.Position - Vector3d.Right * 6_000.0;
        vessel.Velocity = Vector3d.Right * 1_000.0 + Vector3d.Up * 1_000.0;
        vessel.ReferenceBodyId = earth.Id;
        vessel.IsOnRails = true;

        var universe = new Universe
        {
            TimeScale = 5.0,
            ActiveVessel = activeReference ? vessel : null,
        };
        universe.AddBody(earth);
        universe.AddBody(moon);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Vessel PoweredVessel(CelestialBody earth, string id)
    {
        var vessel = CoastVessel(earth, id);
        vessel.Position = earth.Position + Vector3d.Up * (earth.Radius + 10_000.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        return vessel;
    }

    private static Universe CreateDockingSchedulerUniverse(
        out Vessel primary,
        out Vessel secondary)
    {
        var body = new CelestialBody
        {
            Id = "scheduler-docking-body",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 1_000_000.0,
            SphereOfInfluence = 1.0e9,
        };
        var definition = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName,
            "data",
            "parts",
            "docking_port_standard.json"));
        primary = new Vessel("scheduler-primary")
        {
            Position = body.Position
                + Vector3d.Right * (body.Radius + 100_000.0),
            Orientation = Quaterniond.Identity,
            SASEnabled = false,
            ReferenceBodyId = body.Id,
        };
        double orbitalRadius = body.Radius + 100_000.0;
        Vector3d circularVelocity =
            Vector3d.Up * System.Math.Sqrt(body.GM / orbitalRadius);
        primary.Velocity = circularVelocity;
        primary.Parts.SetRoot(new Part(definition, "primary-port"));
        secondary = new Vessel("scheduler-secondary")
        {
            Position = primary.Position + Vector3d.Up * 0.2,
            Velocity = circularVelocity,
            Orientation = Quaterniond.FromAxisAngle(
                Vector3d.Right,
                System.Math.PI),
            SASEnabled = false,
            ReferenceBodyId = body.Id,
        };
        secondary.Parts.SetRoot(new Part(definition, "secondary-port"));

        var universe = new Universe { ActiveVessel = primary };
        universe.AddBody(body);
        universe.AddVessel(primary);
        universe.AddVessel(secondary);
        return universe;
    }

    private static Vessel BuildFlight7Stack()
    {
        var defs = PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);

        var vessel = new Vessel("scheduler-staging-stack");
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
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

    private static void AssertVesselStateClose(
        Vessel expected,
        Vessel actual,
        double positionToleranceM,
        double velocityToleranceMps)
    {
        AssertVectorClose(expected.Position, actual.Position, positionToleranceM);
        AssertVectorClose(expected.Velocity, actual.Velocity, velocityToleranceMps);
        Assert.True(double.IsFinite(actual.Position.X));
        Assert.True(double.IsFinite(actual.Position.Y));
        Assert.True(double.IsFinite(actual.Position.Z));
        Assert.True(double.IsFinite(actual.Velocity.X));
        Assert.True(double.IsFinite(actual.Velocity.Y));
        Assert.True(double.IsFinite(actual.Velocity.Z));
    }
}
