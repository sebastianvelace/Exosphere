namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Cross-checks the development scheduler budget against the existing unbudgeted
/// scheduler.  The budget is intentionally tiny so every test exercises pending
/// simulation time instead of accidentally passing with one full catch-up call.
///
/// These are CPU-only tests.  They do not enable the option in Godot and therefore
/// do not make a release/runtime policy decision; they prove that committing the
/// same temporal interval in smaller global steps preserves the authoritative state.
/// </summary>
public sealed class SchedulerBudgetParityTests
{
    private const int SubstepBudget = 1;
    private const double InitialTimeScale = 1_000.0;
    private const double InitialBudgetedWallDelta = 0.004;
    private const double InitialReferenceWallDelta = 0.002;
    private const double DrainTimeScale = 1_001.0;
    private const double DrainWallDelta = 0.001;
    private const double PositionToleranceM = 1e-5;
    private const double VelocityToleranceMps = 1e-9;

    [Fact]
    public void BudgetedRailsCommitsExactDebtAndMatchesUnbudgetedReference()
    {
        var budgeted = CreateSafeRailUniverse("budgeted-rails", budgetEnabled: true,
            out Vessel budgetedVessel);
        var reference = CreateSafeRailUniverse("reference-rails", budgetEnabled: false,
            out Vessel referenceVessel);

        // Both universes are brought to t=2 s.  The budgeted call asks for 4 s,
        // commits one 2 s global step, and retains the other 2 s as exact debt.
        budgeted.Tick(InitialBudgetedWallDelta);
        reference.Tick(InitialReferenceWallDelta);

        Assert.Equal(2.0, budgeted.CurrentTime, 12);
        Assert.Equal(2.0, reference.CurrentTime, 12);
        Assert.Equal(2.0, budgeted.PendingSimulationSeconds, 12);
        Assert.Equal(0.0, reference.PendingSimulationSeconds, 12);
        Assert.Equal(4.0, budgeted.LastSchedulerTelemetry.RequestedSimulationSeconds, 12);
        Assert.Equal(2.0, budgeted.LastSchedulerTelemetry.ProcessedSimulationSeconds, 12);
        Assert.Equal(2.0, budgeted.LastSchedulerTelemetry.PendingSimulationSeconds, 12);
        Assert.True(budgeted.LastSchedulerTelemetry.BudgetLimited);
        Assert.Equal(PhysicsSchedulerBudgetReason.SubstepLimit,
            budgeted.LastSchedulerTelemetry.BudgetReason);

        DrainBudgetedDebt(budgeted);
        AdvanceReferenceTo(reference, budgeted.CurrentTime);

        Assert.Equal(0.0, budgeted.PendingSimulationSeconds, 12);
        Assert.Equal(reference.CurrentTime, budgeted.CurrentTime, 12);
        Assert.Equal(reference.PendingSimulationSeconds, budgeted.PendingSimulationSeconds, 12);
        AssertVesselParity(referenceVessel, budgetedVessel);
        Assert.True(budgeted.LastSchedulerTelemetry.ProcessedSimulationSeconds > 0.0);
        Assert.False(budgeted.LastSchedulerTelemetry.BudgetLimited);
    }

    [Fact]
    public void BudgetedStagingAtACommonEpochMatchesReferenceAndInvalidatesRails()
    {
        var budgeted = CreateStagingUniverse("budgeted-staging", budgetEnabled: true,
            out Vessel budgetedStack);
        var reference = CreateStagingUniverse("reference-staging", budgetEnabled: false,
            out Vessel referenceStack);

        AlignAtTwoSeconds(budgeted, reference);

        Vessel budgetedDetached = Assert.IsType<Vessel>(budgetedStack.Stage());
        Vessel referenceDetached = Assert.IsType<Vessel>(referenceStack.Stage());
        budgeted.AddVessel(budgetedDetached);
        reference.AddVessel(referenceDetached);

        Assert.False(budgetedStack.IsOnRails);
        Assert.Null(budgetedStack.OrbitalState);
        Assert.False(budgetedDetached.IsOnRails);
        Assert.Null(budgetedDetached.OrbitalState);
        Assert.Equal(budgeted.Vessels.Count, reference.Vessels.Count);

        DrainBudgetedDebt(budgeted);
        AdvanceReferenceTo(reference, budgeted.CurrentTime);

        Assert.Equal(reference.CurrentTime, budgeted.CurrentTime, 12);
        Assert.Equal(0.0, budgeted.PendingSimulationSeconds, 12);
        Assert.Equal(reference.DockingConnections.Count, budgeted.DockingConnections.Count);
        Assert.Equal(reference.Vessels.Count, budgeted.Vessels.Count);
        AssertVesselParity(referenceStack, budgetedStack);
        AssertVesselParity(referenceDetached, budgetedDetached);
        AssertPartMassParity(referenceStack, budgetedStack);
        AssertPartMassParity(referenceDetached, budgetedDetached);
    }

    [Fact]
    public void BudgetedDockingAndUndockingPreserveConnectionAndRigidStateParity()
    {
        var budgeted = CreateDockingUniverse(budgetEnabled: true,
            out Vessel budgetedPrimary, out Vessel budgetedSecondary);
        var reference = CreateDockingUniverse(budgetEnabled: false,
            out Vessel referencePrimary, out Vessel referenceSecondary);

        DockingAttempt budgetedDock = budgeted.TryDock(
            budgetedPrimary.Id, "primary-port",
            budgetedSecondary.Id, "secondary-port", "budget-parity-dock");
        DockingAttempt referenceDock = reference.TryDock(
            referencePrimary.Id, "primary-port",
            referenceSecondary.Id, "secondary-port", "budget-parity-dock");

        Assert.True(budgetedDock.Succeeded);
        Assert.True(referenceDock.Succeeded);
        Assert.Equal(1, budgeted.DockingConnections.Count);
        Assert.Equal(1, reference.DockingConnections.Count);

        // Dock first at the common epoch, then let the budgeted scheduler carry
        // the connection through pending time.  The secondary is a scheduler skip
        // in both runs and its relative pose is constrained after every global step.
        budgeted.Tick(InitialBudgetedWallDelta);
        reference.Tick(InitialReferenceWallDelta);
        int budgetedInitialDockSkips =
            budgeted.LastSchedulerTelemetry.DockedSecondarySkips;
        int referenceInitialDockSkips =
            reference.LastSchedulerTelemetry.DockedSecondarySkips;
        Assert.Equal(reference.CurrentTime, budgeted.CurrentTime, 12);
        Assert.Equal(2.0, budgeted.PendingSimulationSeconds, 12);

        int budgetedDrainDockSkips = DrainBudgetedDebt(budgeted);
        AdvanceReferenceTo(reference, budgeted.CurrentTime);

        Assert.Equal(reference.CurrentTime, budgeted.CurrentTime, 12);
        Assert.Equal(1, budgeted.DockingConnections.Count);
        Assert.Equal(1, reference.DockingConnections.Count);
        Assert.Equal(
            referenceInitialDockSkips + reference.LastSchedulerTelemetry.DockedSecondarySkips,
            budgetedInitialDockSkips + budgetedDrainDockSkips);
        AssertVesselParity(referencePrimary, budgetedPrimary);
        AssertVesselParity(referenceSecondary, budgetedSecondary);

        Assert.True(budgeted.Undock("budget-parity-dock", separationSpeedMps: 0.2));
        Assert.True(reference.Undock("budget-parity-dock", separationSpeedMps: 0.2));
        Assert.Empty(budgeted.DockingConnections);
        Assert.Empty(reference.DockingConnections);
        Assert.False(budgetedPrimary.IsOnRails);
        Assert.False(budgetedSecondary.IsOnRails);
        Assert.Null(budgetedPrimary.OrbitalState);
        Assert.Null(budgetedSecondary.OrbitalState);
        AssertVesselParity(referencePrimary, budgetedPrimary);
        AssertVesselParity(referenceSecondary, budgetedSecondary);
    }

    [Fact]
    public void BudgetedRailWakeUpMatchesReferenceIncludingEngineRuntimeState()
    {
        var budgeted = CreatePoweredRailUniverse("budgeted-wake", budgetEnabled: true,
            out Vessel budgetedVessel);
        var reference = CreatePoweredRailUniverse("reference-wake", budgetEnabled: false,
            out Vessel referenceVessel);

        AlignAtTwoSeconds(budgeted, reference);

        // This is the event boundary: both vessels receive the same command at
        // the same simulation epoch while the budgeted universe still has debt.
        budgetedVessel.Throttle = 0.5;
        referenceVessel.Throttle = 0.5;
        budgeted.TimeScale = 100.0;
        reference.TimeScale = 100.0;

        budgeted.Tick(0.001); // 0.1 s requested; one 0.1 s thrust step commits.
        reference.Tick(0.001);

        Assert.False(budgetedVessel.IsOnRails);
        Assert.Null(budgetedVessel.OrbitalState);
        Assert.False(referenceVessel.IsOnRails);
        Assert.Null(referenceVessel.OrbitalState);
        AssertVesselParity(referenceVessel, budgetedVessel);
        AssertEngineRuntimeParity(referenceVessel, budgetedVessel);

        DrainBudgetedDebt(budgeted, timeScale: 100.0, wallDelta: 0.0001);
        AdvanceReferenceTo(reference, budgeted.CurrentTime);

        Assert.Equal(reference.CurrentTime, budgeted.CurrentTime, 10);
        Assert.Equal(0.0, budgeted.PendingSimulationSeconds, 12);
        AssertVesselParity(referenceVessel, budgetedVessel,
            positionToleranceM: 1e-4, velocityToleranceMps: 1e-8);
        AssertEngineRuntimeParity(referenceVessel, budgetedVessel);
        var lifecycleStates = budgetedVessel.Parts.Parts
            .SelectMany(part => part.EngineStates)
            .Select(state => state.State)
            .ToArray();
        Assert.Contains(lifecycleStates, state => state is
            Exosphere.Simulation.Propulsion.EngineLifecycleState.Chill
            or Exosphere.Simulation.Propulsion.EngineLifecycleState.SpinPrime
            or Exosphere.Simulation.Propulsion.EngineLifecycleState.Ignition
            or Exosphere.Simulation.Propulsion.EngineLifecycleState.Ramp
            or Exosphere.Simulation.Propulsion.EngineLifecycleState.Running);
    }

    private static void AlignAtTwoSeconds(Universe budgeted, Universe reference)
    {
        budgeted.Tick(InitialBudgetedWallDelta);
        reference.Tick(InitialReferenceWallDelta);
        Assert.Equal(2.0, budgeted.CurrentTime, 12);
        Assert.Equal(2.0, reference.CurrentTime, 12);
        Assert.Equal(2.0, budgeted.PendingSimulationSeconds, 12);
        Assert.Equal(0.0, reference.PendingSimulationSeconds, 12);
    }

    private static int DrainBudgetedDebt(
        Universe budgeted,
        double timeScale = DrainTimeScale,
        double wallDelta = DrainWallDelta)
    {
        bool sawBudgetLimit = false;
        int dockedSecondarySkips = 0;
        int ticks = 0;
        while (budgeted.PendingSimulationSeconds > 1e-12)
        {
            double before = budgeted.CurrentTime;
            budgeted.TimeScale = timeScale;
            budgeted.Tick(wallDelta);
            ticks++;

            Assert.True(ticks < 2_000,
                "Budgeted temporal debt did not converge; scheduler may be discarding or adding time.");
            Assert.True(budgeted.CurrentTime > before);
            Assert.True(budgeted.LastSchedulerTelemetry.ProcessedSimulationSeconds > 0.0);
            Assert.True(budgeted.LastSchedulerTelemetry.PendingSimulationSeconds >= -1e-12);
            sawBudgetLimit |= budgeted.LastSchedulerTelemetry.BudgetLimited;
            dockedSecondarySkips += budgeted.LastSchedulerTelemetry.DockedSecondarySkips;
        }

        Assert.True(sawBudgetLimit,
            "The fixture drained without exercising the scheduler substep budget.");
        Assert.Equal(0.0, budgeted.PendingSimulationSeconds, 12);
        return dockedSecondarySkips;
    }

    private static Universe CreateSafeRailUniverse(
        string id,
        bool budgetEnabled,
        out Vessel vessel)
    {
        var earth = LoadBody("earth");
        vessel = SafeRailVessel(earth, id);
        vessel.IsOnRails = true;

        var universe = new Universe
        {
            TimeScale = InitialTimeScale,
            SchedulerBudgetEnabled = budgetEnabled,
            MaxSchedulerSubstepsPerTick = SubstepBudget,
        };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Universe CreateStagingUniverse(
        string id,
        bool budgetEnabled,
        out Vessel stack)
    {
        var earth = LoadBody("earth");
        stack = BuildFlight7Stack(id);
        double radius = earth.Radius + 1_500_000.0;
        stack.Position = earth.Position + Vector3d.Right * radius;
        stack.Velocity = earth.Velocity
            + Vector3d.Up * System.Math.Sqrt(earth.GM / radius);
        stack.ReferenceBodyId = earth.Id;
        stack.IsOnRails = true;
        stack.OrbitalState = OrbitalElements.FromStateVector(
            stack.Position - earth.Position,
            stack.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            0.0);

        var universe = new Universe
        {
            TimeScale = InitialTimeScale,
            SchedulerBudgetEnabled = budgetEnabled,
            MaxSchedulerSubstepsPerTick = SubstepBudget,
        };
        universe.AddBody(earth);
        universe.AddVessel(stack);
        return universe;
    }

    private static Universe CreatePoweredRailUniverse(
        string id,
        bool budgetEnabled,
        out Vessel vessel)
    {
        var universe = CreateStagingUniverse(id, budgetEnabled, out vessel);
        universe.ActiveVessel = vessel;
        return universe;
    }

    private static Universe CreateDockingUniverse(
        bool budgetEnabled,
        out Vessel primary,
        out Vessel secondary)
    {
        var body = new CelestialBody
        {
            Id = "budget-parity-docking-body",
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
        double radius = body.Radius + 100_000.0;
        Vector3d position = body.Position + Vector3d.Right * radius;
        Vector3d circularVelocity = Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        primary = new Vessel("budget-parity-primary")
        {
            Position = position,
            Velocity = circularVelocity,
            Orientation = Quaterniond.Identity,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        primary.Parts.SetRoot(new Part(definition, "primary-port"));
        secondary = new Vessel("budget-parity-secondary")
        {
            Position = position + Vector3d.Up * 0.2,
            Velocity = circularVelocity,
            Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI),
            ReferenceBodyId = body.Id,
            SASEnabled = false,
        };
        secondary.Parts.SetRoot(new Part(definition, "secondary-port"));

        var universe = new Universe
        {
            TimeScale = InitialTimeScale,
            SchedulerBudgetEnabled = budgetEnabled,
            MaxSchedulerSubstepsPerTick = SubstepBudget,
            ActiveVessel = primary,
        };
        universe.AddBody(body);
        universe.AddVessel(primary);
        universe.AddVessel(secondary);
        return universe;
    }

    private static Vessel SafeRailVessel(CelestialBody earth, string id)
    {
        double radius = earth.Radius + 1_500_000.0;
        var vessel = new Vessel(id)
        {
            Position = earth.Position + Vector3d.Right * radius,
            Velocity = earth.Velocity
                + Vector3d.Up * System.Math.Sqrt(earth.GM / radius),
            ReferenceBodyId = earth.Id,
            SASEnabled = false,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "budget-parity-probe",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 5.0,
            DiameterM = 2.0,
        }));
        return vessel;
    }

    private static Vessel BuildFlight7Stack(string id)
    {
        var definitions = PartCatalog.LoadFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var command = new Part(definitions["starship_command"], $"{id}-command");
        var tank = new Part(definitions["starship_tank"], $"{id}-tank");
        var engines = new Part(definitions["starship_engines"], $"{id}-engines");
        var ring = new Part(definitions["decoupler_heavy"], $"{id}-ring");
        var booster = new Part(definitions["super_heavy_booster"], $"{id}-booster");

        var vessel = new Vessel(id);
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return vessel;
    }

    private static void AdvanceReferenceTo(Universe reference, double targetTime)
    {
        double remaining = targetTime - reference.CurrentTime;
        Assert.True(remaining >= -1e-10,
            $"Reference is ahead of budgeted target by {-remaining:R} seconds.");
        if (remaining <= 1e-12)
            return;

        reference.SchedulerBudgetEnabled = false;
        reference.Tick(remaining / reference.TimeScale);
        Assert.Equal(targetTime, reference.CurrentTime, 10);
        Assert.Equal(0.0, reference.PendingSimulationSeconds, 12);
    }

    private static void AssertVesselParity(
        Vessel expected,
        Vessel actual,
        double positionToleranceM = PositionToleranceM,
        double velocityToleranceMps = VelocityToleranceMps)
    {
        AssertVectorClose(expected.Position, actual.Position, positionToleranceM);
        AssertVectorClose(expected.Velocity, actual.Velocity, velocityToleranceMps);
        Assert.Equal(expected.Orientation, actual.Orientation);
        Assert.Equal(expected.AngularVelocity, actual.AngularVelocity);
        Assert.Equal(expected.IsOnRails, actual.IsOnRails);
        Assert.Equal(expected.OrbitalState?.ReferenceBodyId,
            actual.OrbitalState?.ReferenceBodyId);
        Assert.Equal(expected.IsDestroyed, actual.IsDestroyed);
        Assert.Equal(expected.DestructionCause, actual.DestructionCause);
        Assert.Equal(expected.Parts.Parts.Count, actual.Parts.Parts.Count);
        Assert.True(double.IsFinite(actual.Position.X));
        Assert.True(double.IsFinite(actual.Position.Y));
        Assert.True(double.IsFinite(actual.Position.Z));
        Assert.True(double.IsFinite(actual.Velocity.X));
        Assert.True(double.IsFinite(actual.Velocity.Y));
        Assert.True(double.IsFinite(actual.Velocity.Z));
    }

    private static void AssertPartMassParity(Vessel expected, Vessel actual)
    {
        for (int i = 0; i < expected.Parts.Parts.Count; i++)
        {
            var expectedPart = expected.Parts.Parts[i];
            var actualPart = actual.Parts.Parts[i];
            Assert.Equal(expectedPart.Definition.Id, actualPart.Definition.Id);
            Assert.Equal(expectedPart.CurrentMass, actualPart.CurrentMass, 10);
            Assert.Equal(expectedPart.LiquidFuel, actualPart.LiquidFuel, 10);
            Assert.Equal(expectedPart.Oxidizer, actualPart.Oxidizer, 10);
        }
    }

    private static void AssertEngineRuntimeParity(Vessel expected, Vessel actual)
    {
        Assert.Equal(expected.Parts.Parts.Count, actual.Parts.Parts.Count);
        for (int i = 0; i < expected.Parts.Parts.Count; i++)
        {
            var expectedPart = expected.Parts.Parts[i];
            var actualPart = actual.Parts.Parts[i];
            Assert.Equal(expectedPart.EngineStates.Count, actualPart.EngineStates.Count);
            for (int j = 0; j < expectedPart.EngineStates.Count; j++)
            {
                var expectedState = expectedPart.EngineStates[j];
                var actualState = actualPart.EngineStates[j];
                Assert.Equal(expectedState.State, actualState.State);
                Assert.Equal(expectedState.CommandedThrottle, actualState.CommandedThrottle, 10);
                Assert.Equal(expectedState.ActualThrottle, actualState.ActualThrottle, 10);
                Assert.Equal(expectedState.ChamberPressureFraction,
                    actualState.ChamberPressureFraction, 10);
                Assert.Equal(expectedState.FailureCode, actualState.FailureCode);
            }
        }
    }

    private static void AssertVectorClose(Vector3d expected, Vector3d actual, double tolerance)
    {
        Assert.InRange((expected - actual).Magnitude, 0.0, tolerance);
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName,
            "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
