namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Verifies the opt-in deferred candidate independently of the normal scheduler. The
/// candidate is allowed to leave a distant rail vessel at its last event-safe epoch, then
/// must catch it up through the same analytic propagator before exposing current state.
/// </summary>
public sealed class DeferredPhysicsCandidateTests
{
    [Fact]
    public void DisabledCandidatePreservesExistingRailDispatch()
    {
        var baseline = CreateUniverse(candidateEnabled: false, out _);
        var candidate = CreateUniverse(candidateEnabled: false, out _);

        baseline.Tick(0.01);
        candidate.Tick(0.01);

        Assert.True(candidate.LastSchedulerTelemetry.DeadlineProjectedDispatches > 0);
        Assert.Equal(0, candidate.LastSchedulerTelemetry.CandidateDeferredSkips);
        Assert.Equal(
            baseline.LastSchedulerTelemetry.TotalWorkDispatches,
            candidate.LastSchedulerTelemetry.TotalWorkDispatches);
        AssertVesselClose(baseline.Vessels[1], candidate.Vessels[1]);
    }

    [Fact]
    public void EnabledCandidateSkipsSafeSlicesAndCatchesUpAtItsDeadline()
    {
        var baseline = CreateUniverse(candidateEnabled: false, out _);
        var candidate = CreateUniverse(candidateEnabled: true, out Vessel candidateVessel);

        Assert.Equal(
            VesselSimulationTier.Hibernated,
            candidate.ClassifySimulationTier(candidateVessel));

        baseline.Tick(0.01);
        candidate.Tick(0.01);

        Assert.True(candidate.LastSchedulerTelemetry.DeadlineDeferredSkips > 0);
        Assert.Equal(0, candidate.LastSchedulerTelemetry.DeadlineProjectedDispatches);
        Assert.True(candidate.LastSchedulerTelemetry.CandidateDeferredSkips > 0);
        Assert.Equal(0, candidate.LastSchedulerTelemetry.DeadlineCatchUpDispatches);
        Assert.Equal(0.0, candidateVessel.Position.Z, 12);

        baseline.Tick(0.01);
        candidate.Tick(0.01);

        Assert.True(candidate.LastSchedulerTelemetry.DeadlineCatchUpDispatches > 0);
        Assert.Equal(baseline.CurrentTime, candidate.CurrentTime, 12);
        AssertVesselClose(baseline.Vessels[1], candidateVessel);
        Assert.True(double.IsFinite(candidateVessel.Position.X));
        Assert.True(double.IsFinite(candidateVessel.Velocity.X));
    }

    [Fact]
    public void MissingOrThrowingEligibilityGuardFailsClosedToExistingPath()
    {
        var candidate = CreateUniverse(candidateEnabled: true, out Vessel vessel);
        candidate.DeferredPhysicsCandidateEligibility = (_, _) =>
            throw new InvalidOperationException("guard unavailable");

        candidate.Tick(0.01);

        Assert.True(candidate.LastSchedulerTelemetry.DeadlineProjectedDispatches > 0);
        Assert.Equal(0, candidate.LastSchedulerTelemetry.CandidateDeferredSkips);
        Assert.NotEqual(Vector3d.Zero, vessel.Position);
    }

    [Fact]
    public void DisabledCandidateNeverInvokesEligibilityGuard()
    {
        var candidate = CreateUniverse(candidateEnabled: false, out _);
        int calls = 0;
        candidate.DeferredPhysicsCandidateEligibility = (_, _) =>
        {
            calls++;
            return true;
        };

        candidate.Tick(0.01);

        Assert.Equal(0, calls);
        Assert.Equal(0, candidate.LastSchedulerTelemetry.CandidateDeferredSkips);
    }

    [Fact]
    public void EligibilityGuardReceivesOnlyFiniteCommittedEpochs()
    {
        var candidate = CreateUniverse(candidateEnabled: true, out _);
        var epochs = new List<double>();
        candidate.DeferredPhysicsCandidateEligibility = (_, epoch) =>
        {
            epochs.Add(epoch);
            return true;
        };

        double firstStart = candidate.CurrentTime;
        candidate.Tick(0.01);
        double firstEnd = candidate.CurrentTime;

        Assert.NotEmpty(epochs);
        Assert.All(epochs, epoch =>
        {
            Assert.True(double.IsFinite(epoch));
            Assert.InRange(epoch, firstStart, firstEnd);
        });
        Assert.All(epochs, epoch =>
            Assert.True(epoch <= firstEnd + 1e-12, $"future eligibility epoch {epoch} > {firstEnd}"));

        int firstTickCalls = epochs.Count;
        double secondStart = candidate.CurrentTime;
        candidate.Tick(0.01);
        double secondEnd = candidate.CurrentTime;

        Assert.True(epochs.Count > firstTickCalls);
        foreach (double epoch in epochs.Skip(firstTickCalls))
        {
            Assert.True(double.IsFinite(epoch));
            Assert.InRange(epoch, secondStart, secondEnd);
            Assert.True(epoch <= secondEnd + 1e-12, $"future eligibility epoch {epoch} > {secondEnd}");
        }
    }

    [Fact]
    public void CandidateTelemetryResetsForInvalidTick()
    {
        var candidate = CreateUniverse(candidateEnabled: true, out _);

        candidate.Tick(0.01);
        Assert.True(candidate.LastSchedulerTelemetry.CandidateDeferredSkips > 0);

        candidate.Tick(double.NaN);

        Assert.Equal(0, candidate.LastSchedulerTelemetry.CandidateDeferredSkips);
        Assert.Equal(PhysicsSchedulerSkipReason.InvalidDelta, candidate.LastSchedulerTelemetry.SkipReason);
    }

    private static Universe CreateUniverse(
        bool candidateEnabled,
        out Vessel distantVessel)
    {
        var earth = new CelestialBody
        {
            Id = "candidate-earth",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6_371_000.0,
            SphereOfInfluence = 1.0e12,
        };
        double radius = earth.Radius + 1_500_000.0;
        double speed = System.Math.Sqrt(earth.GM / radius);
        var active = CreateVessel(
            earth,
            "candidate-active",
            Vector3d.Right * radius,
            Vector3d.Up * speed);
        active.Throttle = 1.0;

        distantVessel = CreateVessel(
            earth,
            "candidate-distant",
            -Vector3d.Right * radius,
            -Vector3d.Up * speed);
        distantVessel.OrbitalState = OrbitalElements.FromStateVector(
            distantVessel.Position - earth.Position,
            distantVessel.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            0.0);

        var universe = new Universe
        {
            ActiveVessel = active,
            TimeScale = 100.0,
            DeferredPhysicsCandidateEnabled = candidateEnabled,
        };
        universe.DeferredPhysicsCandidateEligibility = (_, _) => true;
        universe.AddBody(earth);
        universe.AddVessel(active);
        universe.AddVessel(distantVessel);
        return universe;
    }

    private static Vessel CreateVessel(
        CelestialBody body,
        string id,
        Vector3d position,
        Vector3d velocity)
    {
        var vessel = new Vessel(id)
        {
            Position = body.Position + position,
            Velocity = body.Velocity + velocity,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = $"{id}-command",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 5.0,
            DiameterM = 2.0,
        }));
        return vessel;
    }

    private static void AssertVesselClose(Vessel expected, Vessel actual)
    {
        Assert.True(
            (expected.Position - actual.Position).Magnitude < 1e-3,
            $"position mismatch: expected={expected.Position}, actual={actual.Position}");
        Assert.True(
            (expected.Velocity - actual.Velocity).Magnitude < 1e-6,
            $"velocity mismatch: expected={expected.Velocity}, actual={actual.Velocity}");
    }
}
