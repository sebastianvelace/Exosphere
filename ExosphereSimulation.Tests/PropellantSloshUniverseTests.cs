namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Physics;
using Xunit;

[Trait("FidelityGate", "F5")]
[Trait("GateKind", "analytic")]
public sealed class PropellantSloshUniverseTests
{
    [Fact]
    public void ProductionAdvancesTheModeAndConservesIndependentClosedSystemMomenta()
    {
        using var bench = new Bench(); var world = bench.World; var vessel = world.ActiveVessel!;
        var origin = vessel.Position; var initial = IndependentClosedMomenta(vessel, origin);
        var modal = vessel.PropellantSloshStates.Single().State;
        for (int i = 0; i < 250; i++) world.Tick(0.002);
        var final = IndependentClosedMomenta(vessel, origin);
        Assert.Equal("conservative-unsettled", vessel.LastSloshStatus);
        Assert.NotEqual(modal, vessel.PropellantSloshStates.Single().State);
        Near(initial.P, final.P, 1e-7); Near(initial.H, final.H, 1e-6);
        Assert.InRange(System.Math.Abs(initial.E - final.E), 0, 1e-7);
        Assert.Equal(4300, vessel.TotalMass);
        Assert.Equal(300, vessel.Parts.Root!.LiquidFuel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeplerCoastAdvancesFluidAndRebuildsTheDatumFromTheMovingMassCenter(bool active)
    {
        using var bench = new Bench(1e8); var world = bench.World; var vessel = world.ActiveVessel!;
        var body = world.GetBody("body")!;
        var center = new Vector3d(10000, 0, 0); var meanVelocity = new Vector3d(0, 0, 100);
        var offset = vessel.Orientation.Rotate(vessel.GetMassProperties().CenterOfMassBody);
        var correction = vessel.CenterOfMassMeanVelocity - vessel.CenterOfMassVelocity;
        vessel.Position = center - offset;
        vessel.Velocity = meanVelocity - vessel.AngularVelocity.Cross(offset) - correction;
        vessel.IsOnRails = true;
        vessel.OrbitalState = vessel.ComputeCenterOfMassOrbit(body, 0);
        world.ActiveVessel = active ? vessel : null; world.TimeScale = 10;
        var initialMode = vessel.PropellantSloshStates.Single().State;
        var internalBefore = IndependentClosedMomenta(vessel, vessel.CenterOfMass);
        double initialIntrinsicEnergy = internalBefore.E - internalBefore.P.MagnitudeSquared / (2 * vessel.TotalMass);
        var orbit = vessel.OrbitalState;
        for (int i = 0; i < 30; i++) world.Tick(0.02);
        SaveGameV2Codec.Capture(world); // Materialize any deferred conic before inspection.
        var expected = Exosphere.Simulation.Integrators.KeplerPropagator.PropagateToTime(orbit,
            world.CurrentTime, body.GM);
        Assert.True(vessel.IsOnRails);
        Assert.Equal("conservative-coast", vessel.LastSloshStatus);
        Assert.NotEqual(initialMode, vessel.PropellantSloshStates.Single().State);
        Near(expected.position, vessel.CenterOfMass, 1e-7);
        Near(expected.velocity, vessel.CenterOfMassMeanVelocity, 1e-8);
        var internalAfter = IndependentClosedMomenta(vessel, vessel.CenterOfMass);
        double finalIntrinsicEnergy = internalAfter.E - internalAfter.P.MagnitudeSquared / (2 * vessel.TotalMass);
        Assert.InRange(System.Math.Abs(finalIntrinsicEnergy - initialIntrinsicEnergy), 0, 1e-6);
        Assert.Equal(4300, vessel.TotalMass);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavingAMovingKeplerModeContinuesAtTheSameCommittedEpoch(bool active)
    {
        using var bench = new Bench(1e8); var original = bench.World; var ship = original.ActiveVessel!;
        ship.Velocity = new(0, 0, 100);
        ship.IsOnRails = true; ship.OrbitalState = ship.ComputeCenterOfMassOrbit(original.GetBody("body")!, 0);
        original.ActiveVessel = active ? ship : null; original.TimeScale = 10;
        for (int i = 0; i < 3; i++) original.Tick(0.02);
        var saved = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(original)));
        var restored = Bench.EmptyWorld(1e8);
        SaveGameV2Codec.Restore(restored, saved, bench.Catalog);
        restored.ActiveVessel = active ? restored.Vessels.Single() : null;
        for (int i = 0; i < 20; i++)
        {
            original.Tick(0.02); restored.Tick(0.02);
            var a = original.Vessels.Single(); var b = restored.Vessels.Single();
            Assert.Equal(a.Position, b.Position); Assert.Equal(a.Velocity, b.Velocity);
            Assert.Equal(a.Orientation, b.Orientation); Assert.Equal(a.AngularVelocity, b.AngularVelocity);
            Assert.Equal(a.PropellantSloshStates, b.PropellantSloshStates);
        }
    }

    [Fact]
    public void SaveDuringDrainThenFlowStopContinuesBitForBit()
    {
        using var bench = new Bench(); var original = bench.World;
        original.ActiveVessel!.PropellantVentRateKgPerS = 3;
        for (int i = 0; i < 20; i++) original.Tick(0.02);
        var saved = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(original)));
        var record = Assert.Single(saved.Vessels.Single().PropellantSloshStates!);
        Assert.True(Assert.IsType<CylindricalSloshParameters>(record.PreviousParameters).ModalMassKg.Rate < 0);
        var restored = Bench.EmptyWorld();
        SaveGameV2Codec.Restore(restored, saved, bench.Catalog);
        Assert.Equal(original.ActiveVessel!.PropellantSloshStates, restored.ActiveVessel!.PropellantSloshStates);
        for (int i = 0; i < 50; i++)
        {
            if (i == 10) original.ActiveVessel.PropellantVentRateKgPerS = restored.ActiveVessel.PropellantVentRateKgPerS = 0;
            original.Tick(0.02); restored.Tick(0.02);
            Assert.Equal(original.ActiveVessel.Position, restored.ActiveVessel.Position);
            Assert.Equal(original.ActiveVessel.Velocity, restored.ActiveVessel.Velocity);
            Assert.Equal(original.ActiveVessel.Orientation, restored.ActiveVessel.Orientation);
            Assert.Equal(original.ActiveVessel.AngularVelocity, restored.ActiveVessel.AngularVelocity);
            Assert.Equal(original.ActiveVessel.PropellantSloshStates, restored.ActiveVessel.PropellantSloshStates);
            Assert.Equal(original.ActiveVessel.Parts.Root!.LiquidFuel, restored.ActiveVessel.Parts.Root!.LiquidFuel);
        }
        Assert.True(original.ActiveVessel.PropellantSloshStates.Single().State.RateBodyMps.Magnitude > 0);
    }

    [Fact]
    public void ExhaustionFundsOnlyTheActualStoreAndRetiresItsModeAfterTheStoppedFlowHandoff()
    {
        using var bench = new Bench(); var world = bench.World; var vessel = world.ActiveVessel!;
        vessel.Parts.Root!.LiquidFuel = 0.3;
        vessel.SetPropellantSloshState(new("tank", PropellantResourceKind.LiquidFuel), new(new(0.1, 0, 0.05), new(0.2, 0, -0.1)));
        vessel.PropellantVentRateKgPerS = 3;
        for (int i = 0; i < 6; i++) world.Tick(0.02);
        Assert.Equal(4000, vessel.TotalMass);
        Assert.Equal(0, vessel.Parts.Root.LiquidFuel);
        Assert.Equal(0.3, vessel.VentedPropellantKg, 12);
        Assert.Empty(vessel.PropellantSloshStates);
        Assert.True(double.IsFinite(vessel.Velocity.Magnitude));
        Assert.True(double.IsFinite(vessel.AngularVelocity.Magnitude));
    }

    [Fact]
    public void ConfiguredDampingUsesActualProperAccelerationAndActsOnlyOnRelativeTangentMotion()
    {
        using var bench = new Bench(); var ship = bench.World.ActiveVessel!;
        ship.CaptureResourceFunding = true; ship.Tick(0.02, bench.World.GetBody("body")!);
        var window = Assert.IsType<ResourceFundingWindow>(ship.LastResourceFunding);
        Assert.True(VariableMassStepModel.TryBuild(ship.Parts, window, ship, null, out var funded, out var reason,
            includeConstantStores: true), reason);
        var model = new SloshStepModel(ship, funded!); var profile = model.Sample(0, MassBoundarySide.Right);
        var stage = Exosphere.Simulation.Integrators.Slosh6DofIntegrator.Sample(new(
            new(ship.Position, ship.Velocity, ship.Orientation, ship.Orientation.Inverse().Rotate(ship.AngularVelocity)),
            model.InitialStates()), profile);
        var noAcceleration = model.InternalAccelerations(stage, default).Single();
        Assert.Equal(Vector3d.Zero, noAcceleration);
        var applied = model.InternalAccelerations(stage, Vector3d.Up * 12).Single();
        var mode = stage.State.Modes.Single(); var normal = mode.Sample(profile.Tanks.Single().Parameters.CurvaturePerM).Normal;
        var tangent = mode.RateBodyMps - normal * normal.Dot(mode.RateBodyMps);
        double frequency = System.Math.Sqrt(12 * profile.Tanks.Single().Parameters.CurvaturePerM.Value);
        double expectedPower = -2 * 0.02 * frequency * stage.Corrections.Single().ModalMassKg * tangent.MagnitudeSquared;
        double actualPower = applied.Dot(tangent) * stage.Corrections.Single().ModalMassKg;
        Assert.InRange(System.Math.Abs(actualPower - expectedPower), 0, 1e-12);
        Assert.InRange(System.Math.Abs(applied.Dot(normal)), 0, 1e-14);
        Assert.True(actualPower < 0);
    }

    [Fact]
    public void PositiveAccelerationLocatesTheAdiabaticBoundaryWithoutAParameterValueJump()
    {
        using var bench = new Bench(seed: false); var ship = bench.World.ActiveVessel!;
        ship.Parts.Root!.LiquidFuel = 20.7; ship.PropellantVentRateKgPerS = 3; ship.CaptureResourceFunding = true;
        ship.Tick(0.02, bench.World.GetBody("body")!); // Capture funding only; no modal integration.
        var window = Assert.IsType<ResourceFundingWindow>(ship.LastResourceFunding);
        Assert.True(VariableMassStepModel.TryBuild(ship.Parts, window, ship, null, out var funded, out var reason), reason);
        var model = new SloshStepModel(ship, funded!);
        var tank = ship.Parts.Root.Definition.PropellantTanks.Single();
        double lambda = tank.DensityKgPerM3 * System.Math.PI * tank.RadiusM * tank.RadiusM;
        var critical = PropellantSlosh.SampleParameters(tank.RadiusM, lambda, 20.67, -3).CurvaturePerM;
        double acceleration = System.Math.Pow(System.Math.Abs(critical.Rate)
            / (SloshStepModel.AdiabaticRelativeRateLimit * System.Math.Pow(critical.Value, 1.5)), 2);
        model.ConfigureContinuations(0, 0.02, Vector3d.Up * acceleration);
        double cut = Assert.Single(model.ContinuationCuts(0, 0.02));
        Assert.InRange(System.Math.Abs(cut - 0.01), 0, 1e-12);
        var left = model.Sample(cut, MassBoundarySide.Left).Tanks.Single().Parameters;
        var right = model.Sample(cut, MassBoundarySide.Right).Tanks.Single().Parameters;
        Assert.Equal(left.CurvaturePerM.Value, right.CurvaturePerM.Value);
        Assert.True(left.CurvaturePerM.Rate < 0); Assert.Equal(0, right.CurvaturePerM.Rate);
        Assert.Equal(left.ModalMassKg, right.ModalMassKg); Assert.Equal(left.HeightAboveFloorM, right.HeightAboveFloorM);
        Assert.Equal(right.CurvaturePerM.Value, model.Sample(0.02, MassBoundarySide.Left).Tanks.Single().Parameters.CurvaturePerM.Value);
        Assert.Equal(20.64, ship.Parts.Root.LiquidFuel, 12);
    }

    [Theory]
    [InlineData(-0.8)]
    [InlineData(0.8)]
    public void NonsettlingDrainKeepsBothHemispheresFiniteThroughExhaustionAndReload(double normalY)
    {
        using var bench = new Bench(); var world = bench.World; var ship = world.ActiveVessel!;
        ship.Parts.Root!.LiquidFuel = 0.3;
        var tank = ship.Parts.Root.Definition.PropellantTanks.Single();
        double lambda = tank.DensityKgPerM3 * System.Math.PI * tank.RadiusM * tank.RadiusM;
        double k = PropellantSlosh.SampleParameters(tank.RadiusM, lambda, 0.3).CurvaturePerM.Value;
        var n = new Vector3d(System.Math.Sqrt(1 - normalY * normalY), normalY, 0);
        ship.SetPropellantSloshState(new("tank", PropellantResourceKind.LiquidFuel), new((n + Vector3d.Up) / k, new(0, 0, 0.1)));
        ship.PropellantVentRateKgPerS = 3;
        for (int i = 0; i < 2; i++) world.Tick(0.02);
        var mode = Assert.Single(ship.PropellantSloshStates);
        Assert.Equal(k, Assert.IsType<SloshCurvatureHold>(mode.CurvatureHold).CurvaturePerM);
        Assert.Equal(SloshContinuationKind.NonsettlingDrain, mode.CurvatureHold!.Value.Reason);
        Assert.Equal("conservative-drain", ship.LastSloshStatus);
        var saved = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(world)));
        var restored = Bench.EmptyWorld(); SaveGameV2Codec.Restore(restored, saved, bench.Catalog);
        for (int i = 0; i < 5; i++)
        {
            world.Tick(0.02); restored.Tick(0.02);
            Assert.Equal(ship.Position, restored.ActiveVessel!.Position);
            Assert.Equal(ship.Velocity, restored.ActiveVessel.Velocity);
            Assert.Equal(ship.AngularVelocity, restored.ActiveVessel.AngularVelocity);
            Assert.Equal(ship.PropellantSloshStates, restored.ActiveVessel.PropellantSloshStates);
        }
        Assert.Empty(ship.PropellantSloshStates); Assert.Equal(4000, ship.TotalMass);
        Assert.True(double.IsFinite(ship.Velocity.Magnitude)); Assert.True(double.IsFinite(ship.AngularVelocity.Magnitude));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.2)]
    public void DockCaptureAndUndockPreserveMovingLiquidMomentumAndCaptureDissipatesEnergy(double separationSpeed)
    {
        using var bench = new Bench(); var world = bench.World; var primary = world.ActiveVessel!;
        primary.AngularVelocity *= 0.1;
        var secondary = new Vessel("slosh-secondary") { Orientation = (primary.Orientation
                * Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI)).Normalize(),
            Velocity = primary.Velocity + new Vector3d(0.05, -0.03, 0.02), AngularVelocity = new(-0.002, 0.001, -0.001),
            SASEnabled = false, ReferenceBodyId = "body" };
        secondary.Parts.SetRoot(new Part(bench.Catalog["bench-tank"], "secondary-tank"));
        Assert.True(Universe.TryGetDockingFrame(primary, primary.Parts.Root!, out var port, out var axis, out _));
        Assert.True(Universe.TryGetDockingFrame(secondary, secondary.Parts.Root!, out var secondaryPort, out _, out _));
        secondary.Position = port + axis * 0.2 - secondaryPort;
        secondary.SetPropellantSloshState(new("secondary-tank", PropellantResourceKind.LiquidFuel),
            new(new(-0.07, 0, 0.12), new(-0.1, 0, 0.25)));
        world.AddVessel(secondary);
        var origin = primary.Position; var a = IndependentClosedMomenta(primary, origin); var b = IndependentClosedMomenta(secondary, origin);
        var totalP = a.P + b.P; var totalH = a.H + b.H; double energy = a.E + b.E;
        Assert.True(world.TryDock(primary.Id, "tank", secondary.Id, "secondary-tank", "fluid-dock").Succeeded);
        a = IndependentClosedMomenta(primary, origin); b = IndependentClosedMomenta(secondary, origin);
        Near(totalP, a.P + b.P, 1e-7); Near(totalH, a.H + b.H, 1e-6);
        Assert.True(a.E + b.E <= energy + 1e-8);
        for (int i = 0; i < 20; i++) world.Tick(0.002);
        a = IndependentClosedMomenta(primary, origin); b = IndependentClosedMomenta(secondary, origin);
        Near(totalP, a.P + b.P, 1e-7); Near(totalH, a.H + b.H, 1e-6);
        var saved = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(world)));
        SaveGameV2Codec.Restore(world, saved, bench.Catalog);
        primary = world.ActiveVessel!; secondary = world.Vessels.Single(v => v.Id == "slosh-secondary");
        Assert.True(world.Undock("fluid-dock", separationSpeed));
        a = IndependentClosedMomenta(primary, origin); b = IndependentClosedMomenta(secondary, origin);
        Near(totalP, a.P + b.P, 1e-7); Near(totalH, a.H + b.H, 1e-6);
        Assert.Single(primary.PropellantSloshStates); Assert.Single(secondary.PropellantSloshStates);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("break")]
    [InlineData("payload")]
    [InlineData("zero")]
    public void ModesFollowTheirSourcePartsAndSeparationImpulsesConserveTotalMomentum(string kind)
    {
        using var bench = new Bench(); var world = bench.World; var vessel = world.ActiveVessel!;
        var other = new Part(bench.Catalog["bench-tank"], "tank-b");
        if (kind == "stage")
        {
            var splitter = new Part(bench.Catalog["bench-splitter"], "splitter") { IsStagingActive = true };
            vessel.Parts.AddJoint(new(vessel.Parts.Root!, splitter, "top", "bottom"));
            vessel.Parts.AddJoint(new(splitter, other, "top", "bottom"));
        }
        else vessel.Parts.AddJoint(new(vessel.Parts.Root!, other, "top", "bottom"));
        vessel.SetPropellantSloshState(new("tank-b", PropellantResourceKind.LiquidFuel),
            new(new(-0.07, 0, 0.12), new(-0.1, 0, 0.25)));
        var origin = vessel.Position; var initial = IndependentClosedMomenta(vessel, origin);
        var initialCenterMoment = vessel.CenterOfMass * vessel.TotalMass;
        var fragment = kind switch
        {
            "stage" => vessel.Stage(),
            "break" => vessel.BreakAtJoint(vessel.Parts.Joints.Single()),
            _ => vessel.DeployPayload("tank-b", separationVelocityLocal: kind == "zero" ? Vector3d.Zero : new(0.1, 0.2, -0.05)),
        };
        world.AddVessel(Assert.IsType<Vessel>(fragment));
        var a = IndependentClosedMomenta(vessel, origin); var b = IndependentClosedMomenta(fragment!, origin);
        Near(initial.P, a.P + b.P, 1e-7); Near(initial.H, a.H + b.H, 1e-6);
        Near(initialCenterMoment, vessel.CenterOfMass * vessel.TotalMass + fragment!.CenterOfMass * fragment.TotalMass, 1e-7);
        Assert.Single(vessel.PropellantSloshStates); Assert.Single(fragment.PropellantSloshStates);
        foreach (var member in world.Vessels)
            foreach (var mode in member.PropellantSloshStates)
                Assert.Contains(member.Parts.Parts, p => p.InstanceId == mode.Store.PartInstanceId);
        var saved = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(world)));
        SaveGameV2Codec.Restore(world, saved, bench.Catalog);
        var totalP = Vector3d.Zero; var totalH = Vector3d.Zero;
        foreach (var member in world.Vessels)
        {
            var value = IndependentClosedMomenta(member, origin); totalP += value.P; totalH += value.H;
        }
        Near(initial.P, totalP, 1e-7); Near(initial.H, totalH, 1e-6);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("duplicate")]
    [InlineData("nonfinite")]
    [InlineData("negative-curvature")]
    [InlineData("solid")]
    [InlineData("coefficient-mass")]
    [InlineData("constraint")]
    public void InvalidSavedModesAreRejectedBeforeTheLiveWorldChanges(string invalid)
    {
        using var bench = new Bench(); var world = bench.World; var original = world.ActiveVessel;
        var saved = SaveGameV2Codec.Capture(world);
        var mode = saved.Vessels.Single().PropellantSloshStates!.Single();
        if (invalid == "owner") mode.PartInstanceId = "missing";
        if (invalid == "duplicate") saved.Vessels.Single().PropellantSloshStates!.Add(mode);
        if (invalid == "nonfinite") mode.Rate.X = double.NaN;
        if (invalid == "solid") mode.Resource = PropellantResourceKind.SolidFuel;
        if (invalid == "constraint") mode.Displacement.Y += 0.1;
        if (invalid == "coefficient-mass") mode.PreviousParameters = mode.PreviousParameters!.Value
            with { ModalMassKg = new(mode.PreviousParameters.Value.ModalMassKg.Value + 1, 0, 0) };
        if (invalid == "negative-curvature") mode.PreviousParameters = mode.PreviousParameters!.Value with { CurvaturePerM = new(-1, 0, 0) };
        Assert.Throws<InvalidDataException>(() => SaveGameV2Codec.Restore(world, saved, bench.Catalog));
        Assert.Same(original, world.ActiveVessel);
    }

    [Fact]
    public void OlderSavesWithoutModalFieldsStartWithTheLockedDistribution()
    {
        using var bench = new Bench(); var saved = SaveGameV2Codec.Capture(bench.World);
        saved.Vessels.Single().PropellantSloshStates = null;
        SaveGameV2Codec.Restore(bench.World, saved, bench.Catalog);
        var vessel = bench.World.ActiveVessel!;
        var mode = Assert.Single(vessel.PropellantSloshStates);
        Assert.Equal(default, mode.State);
        Assert.Equal(vessel.Parts.GetMassProperties().CenterOfMassBody, vessel.GetMassProperties().CenterOfMassBody);
        Assert.Equal(vessel.Parts.GetMassProperties().InertiaBody, vessel.GetMassProperties().InertiaBody);
    }

    [Fact]
    public void FundedRcsDatumTorqueDoesNotMoveWithTheLiquidCentroid()
    {
        using var bench = new Bench(rcs: true);
        var vessel = bench.World.ActiveVessel!;
        vessel.CaptureRcsFunding = true;
        vessel.TranslationCommand = Vector3d.Right;
        bench.World.Tick(0.02);
        var funding = Assert.IsType<RcsPropellantFundingSnapshot>(vessel.LastRcsFunding);
        Assert.NotEmpty(funding.Thrusters);
        var mass = vessel.GetMassProperties();
        var material = new RigidBody6DofState(vessel.CenterOfMass, vessel.CenterOfMassVelocity,
            vessel.Orientation, vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity));
        var evaluated = RigidBodyForceEvaluator.Evaluate(vessel, material,
            new RigidBodyForceContext([], null, default, default, false, false, false), mass);
        Vector3d force = default, datumTorque = default;
        foreach (var jet in funding.Thrusters)
        {
            var thrust = jet.ForceDirectionBody * (jet.MassFlowKgPerS * 9.80665);
            force += thrust;
            datumTorque += jet.NozzlePositionBodyM.Cross(thrust);
        }
        Near(force, vessel.Orientation.Inverse().Rotate(evaluated.ForceWorld), 1e-12);
        Near(datumTorque, evaluated.TorqueBody + mass.CenterOfMassBody.Cross(force), 1e-12);
    }

    private static (Vector3d P, Vector3d H, double E) IndependentClosedMomenta(Vessel vessel, Vector3d origin)
    {
        var locked = vessel.Parts.GetMassProperties(); var q = vessel.Orientation;
        var w = q.Inverse().Rotate(vessel.AngularVelocity); var position = vessel.Position - origin;
        var centerVelocity = vessel.Velocity + q.Rotate(w.Cross(locked.CenterOfMassBody));
        var p = centerVelocity * locked.Mass;
        var h = (position + q.Rotate(locked.CenterOfMassBody)).Cross(p) + q.Rotate(locked.InertiaBody.Multiply(w));
        double energy = 0.5 * locked.Mass * centerVelocity.MagnitudeSquared + 0.5 * w.Dot(locked.InertiaBody.Multiply(w));
        var positions = vessel.Parts.ComputePartLocalPositions();
        foreach (var runtime in vessel.PropellantSloshStates)
        {
            var part = vessel.Parts.Parts.Single(p => p.InstanceId == runtime.Store.PartInstanceId);
            var tank = part.Definition.PropellantTanks.Single();
            double depth = part.LiquidFuel / (tank.DensityKgPerM3 * System.Math.PI * tank.RadiusM * tank.RadiusM);
            var mode = PropellantSlosh.FirstMode(tank.RadiusM, depth, part.LiquidFuel, 0);
            var r0 = positions[part] + Vector3d.Up * (tank.BottomYM + mode.SloshMassHeightM);
            var r = r0 + runtime.State.DisplacementBodyM;
            var v0 = vessel.Velocity + q.Rotate(w.Cross(r0));
            var v1 = vessel.Velocity + q.Rotate(w.Cross(r) + runtime.State.RateBodyMps);
            p += (v1 - v0) * mode.SloshMassKg;
            h += ((position + q.Rotate(r)).Cross(v1) - (position + q.Rotate(r0)).Cross(v0)) * mode.SloshMassKg;
            energy += 0.5 * mode.SloshMassKg * (v1.MagnitudeSquared - v0.MagnitudeSquared);
        }
        return (p, h, energy);
    }

    private static void Near(Vector3d expected, Vector3d actual, double tolerance) =>
        Assert.InRange((expected - actual).Magnitude, 0, tolerance);

    private sealed class Bench : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "exo-slosh-bench-" + Guid.NewGuid().ToString("N"));
        public PartCatalog Catalog { get; }
        public Universe World { get; }
        public Bench(double gm = 0, bool seed = true, bool rcs = false)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "tank.json"), """
            {"id":"bench-tank","category":"fuel_tank","mass_dry":4000,"length_m":8,"diameter_m":4,
             "is_docking_port":true,"docking_node_id":"top",
             "fuel_capacity_lf":300,"propellant_tanks":[{"resource":"liquid_fuel","bottom_y_m":-4,
               "top_y_m":4,"radius_m":2,"density_kg_m3":7.957747154594767,"slosh_damping_ratio":0.02}],
             "vent_outlet":{"position":[0.25,-5,0.1],"exhaust_direction":[0,-1,0]},
             "attachment_nodes":[{"id":"top","position":[0,4,0]},{"id":"bottom","position":[0,-4,0]}]}
            """);
            File.WriteAllText(Path.Combine(_directory, "splitter.json"), """
            {"id":"bench-splitter","category":"decoupler","mass_dry":20,"length_m":0.2,"diameter_m":4,
             "stage_priority":100,"separation_impulse_ns":3500,
             "attachment_nodes":[{"id":"top","position":[0,0.1,0]},{"id":"bottom","position":[0,-0.1,0]}]}
            """);
            if (rcs)
            {
                var path = Path.Combine(_directory, "tank.json");
                var definition = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
                definition["category"] = "command";
                definition["fuel_capacity_mono"] = 100;
                definition["rcs_thrusters"] = System.Text.Json.Nodes.JsonNode.Parse("""
                [{"id":"right","position":[2,-4,0],"direction":[1,0,0],"thrust_n":9.80665,"isp_s":1},
                 {"id":"left","position":[2,-4,0],"direction":[-1,0,0],"thrust_n":9.80665,"isp_s":1}]
                """);
                File.WriteAllText(path, definition.ToJsonString());
            }
            Catalog = PartCatalog.LoadFromDirectory(_directory);
            var vessel = new Vessel("slosh-production-bench") { Position = new(10000, 0, 0), Velocity = new(0.2, -0.1, 0.05),
                Orientation = Quaterniond.FromEuler(15, -30, 10), AngularVelocity = new(0.01, -0.02, 0.03),
                ReferenceBodyId = "body", SASEnabled = false };
            vessel.Parts.SetRoot(new Part(Catalog["bench-tank"], "tank"));
            if (seed) vessel.SetPropellantSloshState(new("tank", PropellantResourceKind.LiquidFuel),
                new(new(0.1, 0, -0.05), new(0.2, 0, -0.1)));
            World = EmptyWorld(gm); World.AddVessel(vessel); World.ActiveVessel = vessel;
        }
        public static Universe EmptyWorld(double gm = 0)
        {
            var world = new Universe { GravityGradientEnabled = false };
            world.AddBody(new CelestialBody { Id = "body", Radius = 1, GM = gm, SphereOfInfluence = 1e9 });
            return world;
        }
        public void Dispose() => Directory.Delete(_directory, true);
    }
}
