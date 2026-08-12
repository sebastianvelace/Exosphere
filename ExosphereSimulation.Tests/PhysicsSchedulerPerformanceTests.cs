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
