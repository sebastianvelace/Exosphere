namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Xunit;

/// <summary>
/// Promotion gate for the future interest/deferred-work policy.
///
/// The candidate side uses the existing mixed scheduler with a non-active vessel on
/// analytic rails.  The reference side makes the same vessel active at low warp, which
/// forces RK4.  These tests compare physical outcomes at equal simulation epochs; they do
/// not enable the policy in the runtime.  Event boundaries that must never be deferred
/// are checked separately and are expected to fail closed.
/// </summary>
public sealed class InterestPromotionParityTests
{
    private const double PositionToleranceM = 1e-3;
    private const double VelocityToleranceMps = 1e-7;
    private const double OrientationTolerance = 1e-8;

    [Fact]
    public void CoastingCandidateMatchesFullPhysicsReferenceAtEqualEpoch()
    {
        Assert.False(SimulationInterestPolicy.EnabledByDefault);
        var candidate = CreateSafeCoastUniverse(
            "coast-candidate", timeScale: 100.0, active: false,
            out Vessel candidateVessel);
        var reference = CreateSafeCoastUniverse(
            "coast-reference", timeScale: 1.0, active: true,
            out Vessel referenceVessel);
        // Low warp only forces RK4 for a vessel that is not already carrying an
        // explicit analytic-rails flag.  Clear the reference flag to make the oracle
        // genuinely full physics while keeping the candidate on rails.
        referenceVessel.IsOnRails = false;
        referenceVessel.OrbitalState = null;

        SimulationInterestDecision decision =
            candidate.GetSimulationInterestDecision(candidateVessel);
        Assert.Equal(SimulationInterestTier.Dormant, decision.Tier);
        Assert.True(decision.AllowsDeferredWork);
        Assert.Equal(VesselPhysicsWorkload.OnRails,
            candidate.ClassifyMixedPhysicsWorkload(candidateVessel));

        for (int i = 0; i < 5; i++)
        {
            candidate.Tick(0.0002); // 0.02 s of simulation at warp x100.
            reference.Tick(0.02);  // 0.02 s of low-warp RK4 reference.
        }

        Assert.Equal(reference.CurrentTime, candidate.CurrentTime, 12);
        AssertVesselParity(referenceVessel, candidateVessel);
        Assert.True(candidateVessel.IsOnRails,
            "The experimental side should remain analytic while the policy is not promoted.");
        Assert.False(referenceVessel.IsOnRails);
    }

    [Fact]
    public void SaveResumePreservesCandidateRailStateEpochAndPendingCallbacks()
    {
        var catalog = LoadCatalog();
        var source = CreateSafeCoastUniverse(
            "resume-candidate", timeScale: 100.0, active: false,
            out Vessel sourceVessel,
            catalog["command_pod_mk1"]);
        source.Tick(0.0002);

        var metadata = new SaveGameV2
        {
            Mission = new MissionSaveV2
            {
                NextCallbackSequence = 3,
                CallbackEvents =
                [
                    new MissionCallbackState
                    {
                        Sequence = 1,
                        EventType = "PhaseChanged",
                        Payload = "ORBIT",
                        SimulationTime = source.CurrentTime,
                        Delivered = true,
                    },
                    new MissionCallbackState
                    {
                        Sequence = 2,
                        EventType = "VesselStaged",
                        Payload = sourceVessel.Id,
                        SimulationTime = source.CurrentTime,
                        Delivered = false,
                    },
                ],
            },
        };

        string json = SaveGameV2Json.Serialize(
            SaveGameV2Codec.Capture(source, metadata));
        SaveGameV2 decoded = SaveGameV2Json.DeserializeOrMigrate(json);

        Assert.Equal(3, decoded.Mission.NextCallbackSequence);
        Assert.Equal([true, false],
            decoded.Mission.CallbackEvents.Select(callback => callback.Delivered));
        Assert.Equal(2, decoded.Mission.CallbackEvents[1].Sequence);

        var resumed = new Universe { TimeScale = 100.0 };
        resumed.AddBody(VacuumBody());
        SaveGameV2Codec.Restore(resumed, decoded, catalog);

        var resumedVessel = Assert.Single(resumed.Vessels);
        Assert.Equal(source.CurrentTime, resumed.CurrentTime, 12);
        Assert.Equal(sourceVessel.IsOnRails, resumedVessel.IsOnRails);
        Assert.NotNull(resumedVessel.OrbitalState);
        Assert.Equal(SimulationInterestTier.Dormant,
            resumed.GetSimulationInterestDecision(resumedVessel).Tier);

        source.Tick(0.0002);
        resumed.Tick(0.0002);

        Assert.Equal(source.CurrentTime, resumed.CurrentTime, 12);
        AssertVesselParity(sourceVessel, resumedVessel);
        Assert.Equal(sourceVessel.Parts.Parts.Count, resumedVessel.Parts.Parts.Count);
        Assert.Equal(
            sourceVessel.Parts.Parts.Single().Definition.Id,
            resumedVessel.Parts.Parts.Single().Definition.Id);
    }

    [Fact]
    public void StagingWakesDetachedFragmentAndMatchesFullPhysicsReference()
    {
        var candidate = CreateStagingUniverse(
            "candidate-stack", timeScale: 100.0, out Vessel candidateStack);
        var reference = CreateStagingUniverse(
            "reference-stack", timeScale: 1.0, out Vessel referenceStack);

        Vessel candidateDebris = StageAndArmDetached(candidate, candidateStack);
        Vessel referenceDebris = StageAndArmDetached(reference, referenceStack);

        SimulationInterestDecision candidateDecision =
            candidate.GetSimulationInterestDecision(candidateDebris);
        // The detached lower fragment has no command part in this fixture.  Structural
        // control loss therefore promotes it all the way to Active; this is the safe
        // outcome and proves staging cannot silently create a dormant uncontrolled body.
        Assert.Equal(SimulationInterestTier.Active, candidateDecision.Tier);
        Assert.False(candidateDecision.AllowsDeferredWork);
        Assert.Equal(VesselPhysicsWorkload.FullPhysics,
            candidate.ClassifyMixedPhysicsWorkload(candidateDebris));

        candidate.Tick(0.0002);
        reference.Tick(0.02);

        Assert.Equal(reference.CurrentTime, candidate.CurrentTime, 12);
        AssertVesselParity(
            FindByName(reference, referenceStack.Name),
            FindByName(candidate, candidateStack.Name));
        AssertVesselParity(referenceDebris, candidateDebris);
        Assert.False(candidateDebris.IsOnRails);
        Assert.False(candidateStack.IsOnRails);
    }

    [Fact]
    public void DockingIsAForcedWakeBoundaryAndRetainsRelativePoseAcrossSchedulers()
    {
        var candidate = CreateDockingUniverse(
            timeScale: 100.0, active: false,
            out Vessel candidatePrimary, out Vessel candidateSecondary);
        var reference = CreateDockingUniverse(
            timeScale: 1.0, active: true,
            out Vessel referencePrimary, out Vessel referenceSecondary);

        Assert.True(candidate.TryDock(
            candidatePrimary.Id, "candidate-primary-port",
            candidateSecondary.Id, "candidate-secondary-port",
            "promotion-dock").Succeeded);
        Assert.True(reference.TryDock(
            referencePrimary.Id, "reference-primary-port",
            referenceSecondary.Id, "reference-secondary-port",
            "promotion-dock").Succeeded);

        SimulationInterestDecision primaryDecision =
            candidate.GetSimulationInterestDecision(candidatePrimary);
        SimulationInterestDecision secondaryDecision =
            candidate.GetSimulationInterestDecision(candidateSecondary);
        Assert.Equal(SimulationInterestTier.Proximity, primaryDecision.Tier);
        Assert.Equal(SimulationInterestTier.Proximity, secondaryDecision.Tier);
        Assert.False(primaryDecision.AllowsDeferredWork);
        Assert.False(secondaryDecision.AllowsDeferredWork);

        candidate.Tick(0.0002);
        reference.Tick(0.02);

        Assert.Equal(reference.CurrentTime, candidate.CurrentTime, 12);
        Assert.Single(candidate.DockingConnections);
        Assert.Single(reference.DockingConnections);
        AssertVesselParity(referencePrimary, candidatePrimary);
        AssertVesselParity(referenceSecondary, candidateSecondary);
        AssertVectorClose(
            referenceSecondary.Position - referencePrimary.Position,
            candidateSecondary.Position - candidatePrimary.Position,
            PositionToleranceM);
    }

    [Fact]
    public void SoiBoundaryCandidateMatchesFullPhysicsReferenceWithoutInertialJump()
    {
        var candidate = CreateSoiUniverse(
            timeScale: 5.0, active: false, out Vessel candidateVessel);
        var reference = CreateSoiUniverse(
            timeScale: 5.0, active: true, out Vessel referenceVessel);

        candidate.Tick(0.5);
        reference.Tick(0.5);

        Assert.Equal("promotion-moon", candidateVessel.ReferenceBodyId);
        Assert.Equal("promotion-moon", candidateVessel.OrbitalState?.ReferenceBodyId);
        Assert.Equal(reference.CurrentTime, candidate.CurrentTime, 12);
        AssertVesselParity(referenceVessel, candidateVessel, compareReferenceBody: false);
        Assert.True(double.IsFinite(candidateVessel.Position.X));
        Assert.True(double.IsFinite(candidateVessel.Velocity.X));
    }

    [Fact]
    public void EdlCatchRemainsFullPhysicsAndMatchesReferenceDuringSettlement()
    {
        var candidate = CreateCatchUniverse(
            timeScale: 100.0, active: false, out Vessel candidateVessel);
        var reference = CreateCatchUniverse(
            timeScale: 1.0, active: true, out Vessel referenceVessel);

        SimulationInterestDecision decision =
            candidate.GetSimulationInterestDecision(candidateVessel);
        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        Assert.False(decision.AllowsDeferredWork);
        Assert.Equal(VesselPhysicsWorkload.FullPhysics,
            candidate.ClassifyMixedPhysicsWorkload(candidateVessel));

        // 0.75 s of simulated contact/settling time.  Using equal simulated epochs keeps
        // this a scheduler comparison instead of a wall-clock or frame-rate assertion.
        for (int i = 0; i < 150; i++)
        {
            candidate.Tick(0.00005);
            reference.Tick(0.005);
        }

        Assert.Equal(reference.CurrentTime, candidate.CurrentTime, 12);
        Assert.Equal(referenceVessel.IsCaught, candidateVessel.IsCaught);
        Assert.Equal(referenceVessel.IsDestroyed, candidateVessel.IsDestroyed);
        AssertVesselParity(referenceVessel, candidateVessel);
        Assert.NotNull(candidateVessel.LastCatchContact);
    }

    private static Universe CreateSafeCoastUniverse(
        string vesselId,
        double timeScale,
        bool active,
        out Vessel vessel,
        PartDefinition? partDefinition = null)
    {
        var body = VacuumBody();
        vessel = SafeRailVessel(body, vesselId, partDefinition);
        var universe = new Universe
        {
            TimeScale = timeScale,
            ActiveVessel = active ? vessel : null,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Universe CreateStagingUniverse(
        string vesselName,
        double timeScale,
        out Vessel stack)
    {
        var body = VacuumBody();
        stack = BuildTwoPartStack(vesselName);
        ConfigureCircularOrbit(stack, body, radius: 3_000_000.0);
        stack.IsOnRails = true;
        stack.OrbitalState = OrbitalElements.FromStateVector(
            stack.Position - body.Position,
            stack.Velocity - body.Velocity,
            body.GM,
            body.Id,
            0.0);

        var universe = new Universe { TimeScale = timeScale };
        universe.AddBody(body);
        universe.AddVessel(stack);
        return universe;
    }

    private static Vessel StageAndArmDetached(Universe universe, Vessel stack)
    {
        Vessel detached = Assert.IsType<Vessel>(stack.Stage());
        universe.AddVessel(detached);
        detached.IsOnRails = true;
        detached.Throttle = 0.2;
        return detached;
    }

    private static Universe CreateDockingUniverse(
        double timeScale,
        bool active,
        out Vessel primary,
        out Vessel secondary)
    {
        var body = VacuumBody();
        double radius = 3_000_000.0;
        Vector3d position = body.Position + Vector3d.Right * radius;
        Vector3d velocity = body.Velocity
            + Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        primary = new Vessel(active ? "reference-primary" : "candidate-primary")
        {
            Position = position,
            Velocity = velocity,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            IsOnRails = true,
            OrbitalState = OrbitalElements.FromStateVector(
                position - body.Position, velocity - body.Velocity,
                body.GM, body.Id, 0.0),
        };
        secondary = new Vessel(active ? "reference-secondary" : "candidate-secondary")
        {
            Position = position + Vector3d.Up * 0.2,
            Velocity = velocity,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI),
            IsOnRails = true,
            OrbitalState = OrbitalElements.FromStateVector(
                position + Vector3d.Up * 0.2 - body.Position,
                velocity - body.Velocity, body.GM, body.Id, 0.0),
        };
        primary.Parts.SetRoot(DockingPort(
            active ? "reference-primary-port" : "candidate-primary-port"));
        secondary.Parts.SetRoot(DockingPort(
            active ? "reference-secondary-port" : "candidate-secondary-port"));

        var universe = new Universe
        {
            TimeScale = timeScale,
            ActiveVessel = active ? primary : null,
        };
        universe.AddBody(body);
        universe.AddVessel(primary);
        universe.AddVessel(secondary);
        return universe;
    }

    private static Universe CreateSoiUniverse(
        double timeScale,
        bool active,
        out Vessel vessel)
    {
        var earth = new CelestialBody
        {
            Id = "promotion-earth",
            Mass = 1.0e20,
            GM = 1.0,
            Radius = 1_000.0,
            SphereOfInfluence = 1.0e9,
        };
        var moon = new CelestialBody
        {
            Id = "promotion-moon",
            Mass = 1.0e10,
            GM = 1.0e-6,
            Radius = 100.0,
            SphereOfInfluence = 5_000.0,
            Position = Vector3d.Right * 100_000.0,
        };
        vessel = SafeRailVessel(earth, active ? "soi-reference" : "soi-candidate");
        vessel.Position = moon.Position - Vector3d.Right * 1_500.0;
        vessel.Velocity = Vector3d.Right * 1_000.0 + Vector3d.Up * 1_000.0;
        vessel.ReferenceBodyId = earth.Id;
        vessel.IsOnRails = true;
        vessel.OrbitalState = null;

        var universe = new Universe
        {
            TimeScale = timeScale,
            ActiveVessel = active ? vessel : null,
        };
        universe.AddBody(earth);
        universe.AddBody(moon);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Universe CreateCatchUniverse(
        double timeScale,
        bool active,
        out Vessel vessel)
    {
        var body = new CelestialBody
        {
            Id = "promotion-catch-body",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6.371e6,
        };
        vessel = new Vessel(active ? "catch-reference" : "catch-candidate")
        {
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            Orientation = Quaterniond.Identity,
        };
        var nose = new Part(new PartDefinition
        {
            Id = "promotion-catch-nose",
            CategoryStr = "command",
            MassDry = 38_000.0,
            LengthM = 19.0,
            DiameterM = 9.0,
            CatchPinLateralOffsetM = 4.4,
            CatchPinRadiusM = 0.4,
        }, $"{vessel.Id}-nose");
        vessel.Parts.SetRoot(nose);
        vessel.ConfigureCatchContactsFromParts();

        Vector3d cradle = body.Position + Vector3d.Up * (body.Radius + 500.0);
        vessel.Position = cradle + Vector3d.Up * 3.0;
        vessel.Velocity = Vector3d.Up * -1.0;
        vessel.IsAttemptingTowerCatch = true;
        vessel.CatchTargetPositionWorld = cradle;
        vessel.CatchTargetUpWorld = Vector3d.Up;
        vessel.CatchTargetVelocityWorld = Vector3d.Zero;

        var universe = new Universe
        {
            TimeScale = timeScale,
            ActiveVessel = active ? vessel : null,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Vessel SafeRailVessel(
        CelestialBody body,
        string id,
        PartDefinition? partDefinition = null)
    {
        double radius = body.Radius + 2_000_000.0;
        Vector3d position = body.Position + Vector3d.Right * radius;
        Vector3d velocity = body.Velocity
            + Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        var vessel = new Vessel(id)
        {
            Position = position,
            Velocity = velocity,
            ReferenceBodyId = body.Id,
            IsOnRails = true,
            SASEnabled = false,
            OrbitalState = OrbitalElements.FromStateVector(
                position - body.Position, velocity - body.Velocity,
                body.GM, body.Id, 0.0),
        };
        vessel.Parts.SetRoot(new Part(
            partDefinition ?? new PartDefinition
            {
                Id = "promotion-command",
                CategoryStr = "command",
                MassDry = 1_000.0,
                LengthM = 4.0,
                DiameterM = 2.0,
            },
            $"{id}-part"));
        return vessel;
    }

    private static Vessel BuildTwoPartStack(string name)
    {
        var upper = new Part(new PartDefinition
        {
            Id = "promotion-upper",
            CategoryStr = "command",
            MassDry = 4_000.0,
            LengthM = 12.0,
            DiameterM = 3.0,
        }, $"{name}-upper");
        var decoupler = new Part(new PartDefinition
        {
            Id = "promotion-decoupler",
            CategoryStr = "decoupler",
            MassDry = 100.0,
            LengthM = 1.0,
            DiameterM = 3.0,
            StagePriority = 1,
        }, $"{name}-decoupler")
        {
            IsStagingActive = true,
        };
        var lower = new Part(new PartDefinition
        {
            Id = "promotion-lower",
            CategoryStr = "structure",
            MassDry = 6_000.0,
            LengthM = 20.0,
            DiameterM = 3.0,
        }, $"{name}-lower");

        var vessel = new Vessel(name) { SASEnabled = false };
        vessel.Parts.SetRoot(upper);
        vessel.Parts.AddJoint(new Joint(upper, decoupler, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(decoupler, lower, "bottom", "top"));
        return vessel;
    }

    private static Part DockingPort(string instanceId) => new(new PartDefinition
    {
        Id = "promotion-docking-port",
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

    private static Vessel FindByName(Universe universe, string name) =>
        universe.Vessels.Single(vessel => vessel.Name == name);

    private static CelestialBody VacuumBody() => new()
    {
        Id = "promotion-vacuum-body",
        Mass = 5.972e24,
        GM = 3.986004418e14,
        Radius = 1.0e6,
        SphereOfInfluence = 1.0e12,
    };

    private static void ConfigureCircularOrbit(
        Vessel vessel,
        CelestialBody body,
        double radius)
    {
        vessel.Position = body.Position + Vector3d.Right * radius;
        vessel.Velocity = body.Velocity
            + Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        vessel.ReferenceBodyId = body.Id;
    }

    private static PartCatalog LoadCatalog() => PartCatalog.LoadFromDirectory(
        Path.Combine(FindRepoRoot().FullName, "data", "parts"));

    private static void AssertVesselParity(
        Vessel expected,
        Vessel actual,
        bool compareReferenceBody = true)
    {
        AssertVectorClose(expected.Position, actual.Position, PositionToleranceM);
        AssertVectorClose(expected.Velocity, actual.Velocity, VelocityToleranceMps);
        AssertVectorClose(
            expected.AngularVelocity, actual.AngularVelocity, VelocityToleranceMps);
        AssertQuaternionClose(expected.Orientation, actual.Orientation);
        if (compareReferenceBody)
            Assert.Equal(expected.ReferenceBodyId, actual.ReferenceBodyId);
        Assert.Equal(expected.IsDestroyed, actual.IsDestroyed);
        Assert.Equal(expected.IsCaught, actual.IsCaught);
        Assert.Equal(expected.Parts.Parts.Count, actual.Parts.Parts.Count);
        foreach (var expectedPart in expected.Parts.Parts)
        {
            var actualPart = actual.Parts.Parts.SingleOrDefault(part =>
                part.Definition.Id == expectedPart.Definition.Id);
            Assert.NotNull(actualPart);
            Assert.Equal(expectedPart.LiquidFuel, actualPart!.LiquidFuel, 10);
            Assert.Equal(expectedPart.Oxidizer, actualPart.Oxidizer, 10);
            Assert.Equal(expectedPart.ElectricCharge, actualPart.ElectricCharge, 10);
        }
        Assert.True(double.IsFinite(actual.Position.X));
        Assert.True(double.IsFinite(actual.Position.Y));
        Assert.True(double.IsFinite(actual.Position.Z));
        Assert.True(double.IsFinite(actual.Velocity.X));
        Assert.True(double.IsFinite(actual.Velocity.Y));
        Assert.True(double.IsFinite(actual.Velocity.Z));
    }

    private static void AssertVectorClose(
        Vector3d expected,
        Vector3d actual,
        double tolerance)
    {
        Assert.InRange((expected - actual).Magnitude, 0.0, tolerance);
    }

    private static void AssertQuaternionClose(
        Quaterniond expected,
        Quaterniond actual)
    {
        double direct = System.Math.Abs(expected.W - actual.W)
            + System.Math.Abs(expected.X - actual.X)
            + System.Math.Abs(expected.Y - actual.Y)
            + System.Math.Abs(expected.Z - actual.Z);
        double negated = System.Math.Abs(expected.W + actual.W)
            + System.Math.Abs(expected.X + actual.X)
            + System.Math.Abs(expected.Y + actual.Y)
            + System.Math.Abs(expected.Z + actual.Z);
        Assert.InRange(System.Math.Min(direct, negated), 0.0, OrientationTolerance);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
