namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class PhysicsParityIntegrationTests
{
    [Fact]
    public void LegacyAndCoupledPathsRemainWithinCoastTolerance()
    {
        var legacy = CreateUniverse("legacy", coupled: false, powered: false);
        var coupled = CreateUniverse("coupled", coupled: true, powered: false);

        for (int i = 0; i < 50; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.1,
                VelocityMetersPerSecond: 0.1,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled coast divergence exceeded tolerance: {result}");
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void LegacyAndCoupledPathsRemainWithinPoweredAscentTolerance()
    {
        var legacy = CreateUniverse("legacy-powered", coupled: false, powered: true);
        var coupled = CreateUniverse("coupled-powered", coupled: true, powered: true);

        for (int i = 0; i < 25; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.5,
                VelocityMetersPerSecond: 0.5,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled powered divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.Parts.TotalLiquidFuel
            < legacy.ActiveVessel!.Parts.TotalLiquidFuel + 1e-9);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void LegacyAndCoupledPathsRemainWithinFlight7AscentTolerance()
    {
        var assembly = LoadFlight7Assembly(FindRepoRoot());
        var metrics = assembly.ComputeMetrics();
        Assert.Equal(4_800_000.0, metrics.WetMass, 5);
        Assert.Equal(300_000.0, metrics.DryMass, 5);
        Assert.Equal(4_500_000.0, metrics.PropellantMass, 5);
        Assert.Equal(5, assembly.Parts.Count);

        var legacy = CreateFlight7Universe("legacy-flight7", coupled: false);
        var coupled = CreateFlight7Universe("coupled-flight7", coupled: true);

        for (int i = 0; i < 100; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 5.0,
                VelocityMetersPerSecond: 5.0,
                AttitudeRadians: 1e-5,
                AngularVelocityRadiansPerSecond: 1e-5));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.Parts.TotalLiquidFuel
            < legacy.ActiveVessel!.Parts.TotalLiquidFuel + 1e-9);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7ControlledPitchRemainsWithinAtmosphericParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-controlled", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-controlled", coupled: true, useEarthData: true);
        var command = new Vector3d(0.15, 0.0, 0.0);
        legacy.ActiveVessel!.PitchYawRoll = command;
        coupled.ActiveVessel!.PitchYawRoll = command;

        for (int i = 0; i < 50; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel),
            new RigidBodyParityTolerance(
                PositionMeters: 25.0,
                VelocityMetersPerSecond: 25.0,
                AttitudeRadians: 0.05,
                AngularVelocityRadiansPerSecond: 0.05));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled controlled Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel.AngularVelocity.Magnitude > 1e-4);
        Assert.True(legacy.ActiveVessel.FlapActuators.Pitch > 0.0);
        Assert.Equal(
            legacy.ActiveVessel.FlapActuators.Pitch,
            coupled.ActiveVessel.FlapActuators.Pitch,
            precision: 12);
        Assert.Contains(
            coupled.ActiveVessel.Parts.ActiveEngines,
            part => part.EngineStates.Any(state => state.GimbalDeg.Magnitude > 1e-3));
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void FlapOverrideAdvancesWithoutReintroducingTvcCommand()
    {
        var legacy = CreateFlight7Universe("legacy-flap-override", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flap-override", coupled: true, useEarthData: true);
        var demand = new Vector3d(0.4, -0.2, 0.3);

        legacy.ActiveVessel!.PitchYawRoll = Vector3d.Zero;
        coupled.ActiveVessel!.PitchYawRoll = Vector3d.Zero;
        legacy.ActiveVessel.FlapCommandOverride = demand;
        coupled.ActiveVessel.FlapCommandOverride = demand;

        for (int i = 0; i < 10; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        Assert.Equal(Vector3d.Zero, legacy.ActiveVessel.PitchYawRoll);
        Assert.Equal(Vector3d.Zero, coupled.ActiveVessel.PitchYawRoll);
        Assert.True(legacy.ActiveVessel.FlapActuators.Pitch > 0.0);
        Assert.True(legacy.ActiveVessel.FlapActuators.Yaw < 0.0);
        Assert.True(legacy.ActiveVessel.FlapActuators.Roll > 0.0);
        Assert.Equal(
            legacy.ActiveVessel.FlapActuators,
            coupled.ActiveVessel.FlapActuators);
    }

    [Fact]
    public void Flight7ClosedLoopElevationProgramRemainsWithinParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-guidance", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-guidance", coupled: true, useEarthData: true);
        bool commandSeen = false;
        const double dt = 0.02;

        for (int i = 0; i < 250; i++)
        {
            double elapsed = i * dt;
            commandSeen |= ApplyElevationGuidance(legacy, elapsed);
            commandSeen |= ApplyElevationGuidance(coupled, elapsed);
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 100.0,
                AttitudeRadians: 0.10,
                AngularVelocityRadiansPerSecond: 0.10));

        Assert.True(commandSeen);
        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled closed-loop Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.AngularVelocity.Magnitude > 1e-4);
        Assert.Contains(
            coupled.ActiveVessel.Parts.ActiveEngines,
            part => part.EngineStates.Any(state => state.GimbalDeg.Magnitude > 1e-3));
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7BoosterEngineOutRemainsWithinAsymmetricTorqueParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-engine-out", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-engine-out", coupled: true, useEarthData: true);
        const double dt = 0.02;

        for (int i = 0; i < 150; i++)
        {
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        FailFlight7BoosterEngine(legacy, 13);
        FailFlight7BoosterEngine(coupled, 13);

        var legacyBody = legacy.GetBody(legacy.ActiveVessel!.ReferenceBodyId!)!;
        double pressure = legacyBody.GetAtmosphericPressure(legacy.ActiveVessel.Position);
        var legacyTorque = legacy.ActiveVessel.Parts.GetTotalTorque(pressure);
        var coupledTorque = coupled.ActiveVessel!.Parts.GetTotalTorque(pressure);
        Assert.True(legacyTorque.Magnitude > 1.0);
        Assert.True(coupledTorque.Magnitude > 1.0);
        Assert.True(
            legacyTorque.Z * coupledTorque.Z > 0.0,
            $"Expected matching asymmetric yaw torque signs: legacy={legacyTorque}, coupled={coupledTorque}");

        for (int i = 0; i < 100; i++)
        {
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 100.0,
                AttitudeRadians: 0.10,
                AngularVelocityRadiansPerSecond: 0.10));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled engine-out divergence exceeded tolerance: {result}");
        Assert.True(legacy.ActiveVessel.AngularVelocity.Magnitude > 1e-4);
        Assert.True(coupled.ActiveVessel.AngularVelocity.Magnitude > 1e-4);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7EngineOutRecoveryReducesAngularRateAndPreservesParity()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-recovery", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-recovery", coupled: true, useEarthData: true);
        var baseline = CreateFlight7Universe("baseline-flight7-recovery", coupled: true, useEarthData: true);
        const double dt = 0.02;

        for (int i = 0; i < 150; i++)
        {
            legacy.Tick(dt);
            coupled.Tick(dt);
            baseline.Tick(dt);
        }

        FailFlight7BoosterEngine(legacy, 13);
        FailFlight7BoosterEngine(coupled, 13);
        FailFlight7BoosterEngine(baseline, 13);
        Assert.True(EngineOutRecoveryGuidance.Inspect(coupled.ActiveVessel!).IsEngineOut);

        var legacyCommand = Vector3d.Zero;
        var coupledCommand = Vector3d.Zero;
        for (int i = 0; i < 100; i++)
        {
            legacyCommand = ApplyEngineOutRecovery(legacy, legacyCommand, dt);
            coupledCommand = ApplyEngineOutRecovery(coupled, coupledCommand, dt);
            baseline.Tick(dt);
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 100.0,
                AttitudeRadians: 0.15,
                AngularVelocityRadiansPerSecond: 0.15));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled engine-out recovery divergence exceeded tolerance: {result}");
        var recoveredVessel = coupled.ActiveVessel!;
        var baselineVessel = baseline.ActiveVessel!;
        Assert.True(
            recoveredVessel.AngularVelocity.Magnitude < baselineVessel.AngularVelocity.Magnitude,
            $"Engine-out recovery did not reduce angular rate: recovered={recoveredVessel.AngularVelocity.Magnitude:F6}, baseline={baselineVessel.AngularVelocity.Magnitude:F6}, command={coupledCommand.Magnitude:F6}");
        Assert.True(legacyCommand.Magnitude > 1e-3);
        Assert.True(coupledCommand.Magnitude > 1e-3);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7EngineOutRecoveryWaitsForSensorLatencyThenPreservesParity()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-recovery-latency", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-recovery-latency", coupled: true, useEarthData: true);
        var baseline = CreateFlight7Universe("baseline-flight7-recovery-latency", coupled: true, useEarthData: true);
        var legacyController = new EngineOutRecoveryController(0.10);
        var coupledController = new EngineOutRecoveryController(0.10);
        const double dt = 0.02;

        for (int i = 0; i < 150; i++)
        {
            legacy.Tick(dt);
            coupled.Tick(dt);
            baseline.Tick(dt);
        }

        FailFlight7BoosterEngine(legacy, 13);
        FailFlight7BoosterEngine(coupled, 13);
        FailFlight7BoosterEngine(baseline, 13);

        int detectionStep = -1;
        for (int i = 0; i < 100; i++)
        {
            var legacyVessel = legacy.ActiveVessel!;
            var legacyBody = legacy.GetBody(legacyVessel.ReferenceBodyId!)!;
            var coupledVessel = coupled.ActiveVessel!;
            var coupledBody = coupled.GetBody(coupledVessel.ReferenceBodyId!)!;
            var legacyCommand = legacyController.ComputeCommand(
                legacyVessel,
                legacyBody.GetGeodeticUp(legacyVessel.Position),
                dt);
            var coupledCommand = coupledController.ComputeCommand(
                coupledVessel,
                coupledBody.GetGeodeticUp(coupledVessel.Position),
                dt);

            if (i < 4)
            {
                Assert.False(legacyController.Sensor.LastState.DetectedEngineOut);
                Assert.False(coupledController.Sensor.LastState.DetectedEngineOut);
                Assert.Equal(0.0, legacyCommand.Magnitude, precision: 12);
                Assert.Equal(0.0, coupledCommand.Magnitude, precision: 12);
            }
            else if (detectionStep < 0 && coupledController.Sensor.LastState.DetectedEngineOut)
            {
                detectionStep = i;
            }

            legacyVessel.PitchYawRoll = legacyCommand;
            legacyVessel.SASEnabled = false;
            coupledVessel.PitchYawRoll = coupledCommand;
            coupledVessel.SASEnabled = false;
            baseline.Tick(dt);
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        Assert.Equal(4, detectionStep);
        Assert.True(coupledController.LastCommand.Magnitude > 1e-3);
        Assert.True(coupledController.Sensor.LastState.Isolation.FaultDetected);
        Assert.True(coupledController.Sensor.LastState.TorqueCorroborated);
        Assert.True(
            coupled.ActiveVessel!.AngularVelocity.Magnitude
                < baseline.ActiveVessel!.AngularVelocity.Magnitude);

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 100.0,
                AttitudeRadians: 0.15,
                AngularVelocityRadiansPerSecond: 0.15));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled delayed engine-out recovery divergence exceeded tolerance: {result}");
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7ThirtySecondGuidedAscentWithSasRemainsWithinParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-long", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-long", coupled: true, useEarthData: true);
        legacy.ActiveVessel!.SASEnabled = true;
        coupled.ActiveVessel!.SASEnabled = true;
        const double dt = 0.02;
        double worstAttitude = 0.0;

        for (int i = 0; i < 1500; i++)
        {
            double elapsed = i * dt;
            // Pitch over during the first 5 s, then hold the program; hand attitude to SAS
            // for one second in every ten so both control laws are exercised.
            bool sasWindow = elapsed >= 10.0 && elapsed % 10.0 < 1.0;
            foreach (var universe in new[] { legacy, coupled })
            {
                if (sasWindow) universe.ActiveVessel!.PitchYawRoll = Vector3d.Zero;
                else ApplyElevationGuidance(universe, elapsed);
                universe.Tick(dt);
            }
            worstAttitude = System.Math.Max(worstAttitude, RigidBodyParityComparer.Compare(
                RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
                RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
                new RigidBodyParityTolerance(1e9, 1e9, 1e9, 1e9)).AttitudeErrorRadians);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 10.0,
                AttitudeRadians: 0.05,
                AngularVelocityRadiansPerSecond: 0.02));

        Assert.True(result.IsFinite);
        Assert.True(result.IsWithinTolerance,
            $"Legacy/coupled 30 s guided Flight 7 divergence exceeded tolerance: {result}; worst attitude {worstAttitude}");
        Assert.True(worstAttitude < 0.05, $"worst transient attitude divergence {worstAttitude}");
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
        Assert.False(coupled.ActiveVessel!.IsDestroyed);
    }

    [Fact]
    public void TerminalFlipAndLandingBurnTouchDownLikeLegacy()
    {
        var legacy = CreateTerminalDescentUniverse("legacy-terminal", coupled: false);
        var coupled = CreateTerminalDescentUniverse("coupled-terminal", coupled: true);
        var legacyLog = FlyTerminalDescent(legacy);
        var coupledLog = FlyTerminalDescent(coupled);

        Assert.False(legacy.Vessel.IsDestroyed, $"legacy {legacyLog}");
        Assert.False(coupled.Vessel.IsDestroyed, $"coupled {coupledLog}");
        Assert.True(legacy.Vessel.IsSurfaceSettled, $"legacy never settled: {legacyLog}");
        Assert.True(coupled.Vessel.IsSurfaceSettled, $"coupled never settled: {coupledLog}");
        Assert.True(coupled.Universe.LastCoupled6DofTelemetry.Integrated);
        string summary = $"legacy h40={legacyLog.AltitudeAt40:F2} vD40={legacyLog.DescentRateAt40:F2} contact={legacyLog.ContactSpeed:F2} load={legacyLog.PeakFootLoad:F0} settledTilt={legacyLog.SettledTiltRad:F3}; "
            + $"coupled h40={coupledLog.AltitudeAt40:F2} vD40={coupledLog.DescentRateAt40:F2} contact={coupledLog.ContactSpeed:F2} load={coupledLog.PeakFootLoad:F0} settledTilt={coupledLog.SettledTiltRad:F3}";
        // Continuous powered phase: after the flip both paths must fly the same descent. The
        // settle time is deliberately not compared: the discrete 3→2→1 engine step-down and the
        // 40% throttle floor turn a one-metre altitude difference into a hover of several
        // seconds, which is controller branching, not integrator divergence.
        Assert.True(System.Math.Abs(legacyLog.AltitudeAt40 - coupledLog.AltitudeAt40) <= 0.5, summary);
        Assert.True(System.Math.Abs(legacyLog.DescentRateAt40 - coupledLog.DescentRateAt40) <= 0.1, summary);
        // Mid-flip (T+2 s, still rotating): both paths turn about the centre of mass, so the
        // reported datum (skirt) velocity carries the same ω×r term on both.
        double flipVelocityGap = (legacyLog.DatumVelocityAt2 - coupledLog.DatumVelocityAt2).Magnitude;
        Assert.True(legacyLog.AngularRateAt2 > 0.05, $"not rotating at T+2 s: {legacyLog.AngularRateAt2:F3}");
        Assert.True(flipVelocityGap <= 0.25, $"mid-flip datum velocity gap {flipVelocityGap:F3} m/s; {summary}");
        // Contact outcome: both soft, upright and inside the gear's structural envelope.
        Assert.True(legacyLog.ContactSpeed < 3.0 && coupledLog.ContactSpeed < 3.0, summary);
        Assert.True(legacyLog.SettledTiltRad < 0.05 && coupledLog.SettledTiltRad < 0.05, summary);
        Assert.True(legacyLog.PeakFootLoad < 2_500_000.0 && coupledLog.PeakFootLoad < 2_500_000.0, summary);
        double loadRatio = legacyLog.PeakFootLoad / coupledLog.PeakFootLoad;
        Assert.True(loadRatio is >= 0.5 and <= 2.0, summary);
    }

    private sealed record TerminalDescentLog(
        double ContactSpeed, double PeakFootLoad, double SettledTiltRad,
        double AltitudeAt40, double DescentRateAt40,
        Vector3d DatumVelocityAt2, double AngularRateAt2);

    private sealed record TerminalDescentCase(Universe Universe, CelestialBody Body, Vessel Vessel, Part Engines);

    // The end of a Starship flip: 600 m up, 70 m/s down, the thrust axis still 60° from
    // vertical, three sea-level Raptors and deployed legs. The controller is the shape of
    // EDLController's final descent (axis pointing to geodetic up, descent-rate profile with
    // gravity feed-forward), written once and applied to both integration paths.
    private static TerminalDescentCase CreateTerminalDescentUniverse(string vesselId, bool coupled)
    {
        var root = FindRepoRoot();
        var body = CelestialBody.LoadFromJson(Path.Combine(root.FullName, "data", "bodies", "earth.json"));
        var assembly = new VesselAssembly(PartCatalog.LoadFromDirectory(
            Path.Combine(root.FullName, "data", "parts")));
        var current = assembly.AddRoot("starship_command");
        foreach (string id in new[] { "starship_tank", "starship_engines", "starship_landing_gear" })
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var vessel = assembly.ToVessel("Terminal descent", vesselId);
        foreach (var part in vessel.Parts.Parts)
        {
            if (part.Definition.Category == PartCategory.Landing) part.IsDeployed = true;
            if (part.Definition.HasVehicleRole("tank") || part.LiquidFuel > 0.0)
            {
                part.LiquidFuel *= 0.10;
                part.Oxidizer *= 0.10;
            }
        }
        vessel.ConfigureLandingContactsFromParts();
        var engines = vessel.Parts.Parts.Single(p => p.Definition.Category == PartCategory.Engine);

        var surface = body.GetSurfacePosition(0.0, 0.0, 600.0);
        var up = body.GetGeodeticUp(surface);
        var east = body.GetEastDirection(surface);
        vessel.Position = surface;
        vessel.Velocity = body.Velocity + body.GetSurfaceVelocity(surface) - up * 70.0;
        var tilted = (up * System.Math.Cos(60.0 * MathUtils.DEG_TO_RAD)
            + east * System.Math.Sin(60.0 * MathUtils.DEG_TO_RAD)).Normalized;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, tilted);
        vessel.ReferenceBodyId = body.Id;
        vessel.SASEnabled = false;
        engines.SelectEngineCount(System.Math.Min(3, engines.Definition.EngineCount));
        vessel.Throttle = 1.0;

        var universe = new Universe { ActiveVessel = vessel, Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        // Engines already lit for the flip, as in the flown sequence.
        for (int i = 0; i < 150; i++) engines.AdvanceEngineRuntime(1.0, 0.02);
        return new TerminalDescentCase(universe, body, vessel, engines);
    }

    private static TerminalDescentLog FlyTerminalDescent(TerminalDescentCase run)
    {
        const double dt = 0.02;
        var (universe, body, vessel, engines) = (run.Universe, run.Body, run.Vessel, run.Engines);
        double contactSpeed = double.NaN, peakLoad = 0.0;
        double altitudeAt40 = double.NaN, descentRateAt40 = double.NaN;
        Vector3d datumVelocityAt2 = Vector3d.Zero;
        double angularRateAt2 = double.NaN;
        int selected = System.Math.Min(3, engines.Definition.EngineCount);
        for (int i = 0; i < 6_000 && !vessel.IsSurfaceSettled && !vessel.IsDestroyed; i++)
        {
            var up = body.GetGeodeticUp(vessel.Position);
            var surfaceVelocity = vessel.GetSurfaceVelocity(body);
            double altitude = body.GetAltitude(vessel.Position);
            double vDown = -surfaceVelocity.Dot(up);
            var axis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            if (i == 2_000) { altitudeAt40 = altitude; descentRateAt40 = vDown; }
            if (i == 100) { datumVelocityAt2 = surfaceVelocity; angularRateAt2 = vessel.AngularVelocity.Magnitude; }

            if (vessel.HasSurfaceContact)
            {
                if (double.IsNaN(contactSpeed)) contactSpeed = surfaceVelocity.Magnitude;
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
            }
            else
            {
                double heightToContact = System.Math.Max(0.0, altitude - 7.85);
                int represented = System.Math.Max(1, engines.Definition.EngineCount);
                double perEngineAcceleration = engines.GetRatedFullThrottleThrustMagnitude(
                    vessel.GetAmbientPressure(body)) / represented / vessel.TotalMass;
                double vTarget = System.Math.Min(1.5 + heightToContact * 0.06,
                    System.Math.Sqrt(2.0 * 0.6 * perEngineAcceleration * selected * heightToContact) + 1.2);
                double g = body.GetSurfaceGravity();
                double demand = 1.6 * (vDown - vTarget) + g / System.Math.Max(0.2, axis.Dot(up));
                // Same monotone 3→2→1 engine-out sequence as EDLController: step down only once
                // the demand fits fewer engines with margin, and never relight during the burn.
                if (axis.Dot(up) > System.Math.Cos(15.0 * MathUtils.DEG_TO_RAD))
                    while (selected > 1 && demand <= perEngineAcceleration * (selected - 1) * 0.75)
                        selected--;
                engines.SelectEngineCount(selected);
                double command = demand / System.Math.Max(1e-3, perEngineAcceleration * selected);
                vessel.Throttle = engines.ApplyThrottleFloor(System.Math.Clamp(command, 0.0, 1.0));
                // EDLController's lateral law: cant the thrust axis against horizontal drift
                // (left over from the flip), blending back to vertical over the last 30 m.
                var lateral = surfaceVelocity - up * surfaceVelocity.Dot(up);
                double flare = System.Math.Clamp(heightToContact / 30.0, 0.0, 1.0);
                double cant = System.Math.Min(System.Math.Tan(20.0 * MathUtils.DEG_TO_RAD),
                    lateral.Magnitude * 0.04) * flare;
                var aim = lateral.Magnitude > 1e-3 ? (up - lateral.Normalized * cant).Normalized : up;
                vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                    vessel.Orientation, Vector3d.Up, aim, vessel.AngularVelocity,
                    proportionalGain: 2.2, dampingGain: 6.0);
            }
            universe.Tick(dt);
            peakLoad = System.Math.Max(peakLoad,
                vessel.LastSurfaceContact?.Points.Max(p => p.NormalLoadN) ?? 0.0);
        }
        var finalUp = body.GetGeodeticUp(vessel.Position);
        double settledTilt = System.Math.Acos(System.Math.Clamp(
            vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(finalUp), -1, 1));
        return new TerminalDescentLog(contactSpeed, peakLoad, settledTilt, altitudeAt40, descentRateAt40,
            datumVelocityAt2, angularRateAt2);
    }

    [Fact]
    public void CoupledRcsFloorRotatesUnpoweredVesselLikeLegacy()
    {
        var legacy = CreateUnpoweredFlight7Universe("legacy-rcs-floor", coupled: false);
        var coupled = CreateUnpoweredFlight7Universe("coupled-rcs-floor", coupled: true);
        var command = new Vector3d(0.5, 0.0, 0.0);
        legacy.ActiveVessel!.PitchYawRoll = command;
        coupled.ActiveVessel!.PitchYawRoll = command;

        for (int i = 0; i < 100; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        double legacyRate = legacy.ActiveVessel.AngularVelocity.Magnitude;
        double coupledRate = coupled.ActiveVessel.AngularVelocity.Magnitude;
        // 0.5 command × 0.01 rad/s² RCS floor × 2 s: no engine torque exists, so only the
        // reaction-control floor can rotate this vessel on either path.
        Assert.InRange(legacyRate, 0.009, 0.011);
        Assert.Equal(legacyRate, coupledRate, precision: 4);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void CoupledSasDampsResidualRateLikeLegacy()
    {
        var legacy = CreateUnpoweredFlight7Universe("legacy-sas", coupled: false);
        var coupled = CreateUnpoweredFlight7Universe("coupled-sas", coupled: true);
        foreach (var universe in new[] { legacy, coupled })
        {
            var vessel = universe.ActiveVessel!;
            vessel.SASEnabled = true;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.AngularVelocity = vessel.Orientation.Rotate(new Vector3d(0.2, 0.0, 0.0));
        }

        for (int i = 0; i < 250; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        double legacyRate = legacy.ActiveVessel!.AngularVelocity.Magnitude;
        double coupledRate = coupled.ActiveVessel!.AngularVelocity.Magnitude;
        // SAS can only shed what its 0.01 rad/s² authority removes: 0.05 rad/s in 5 s.
        Assert.InRange(legacyRate, 0.145, 0.155);
        Assert.Equal(legacyRate, coupledRate, precision: 4);
    }

    [Fact]
    public void CoupledPilotedRateEnvelopeMatchesLegacy()
    {
        var legacy = CreateFlight7Universe("legacy-rate-envelope", coupled: false);
        var coupled = CreateFlight7Universe("coupled-rate-envelope", coupled: true);
        var command = new Vector3d(1.0, 0.0, 0.0);
        double legacyPeak = 0.0;
        double coupledPeak = 0.0;

        for (int i = 0; i < 1000; i++)
        {
            legacy.ActiveVessel!.PitchYawRoll = command;
            coupled.ActiveVessel!.PitchYawRoll = command;
            legacy.Tick(0.02);
            coupled.Tick(0.02);
            legacyPeak = System.Math.Max(legacyPeak, legacy.ActiveVessel.AngularVelocity.Magnitude);
            coupledPeak = System.Math.Max(coupledPeak, coupled.ActiveVessel.AngularVelocity.Magnitude);
        }

        Assert.True(legacyPeak > 0.34, $"legacy never reached the envelope: {legacyPeak}");
        // A held full command must saturate at the 20°/s pilot-authority envelope on both
        // paths instead of the coupled body spinning up without bound.
        Assert.True(legacyPeak <= 0.35 + 1e-9, $"legacy peak {legacyPeak}");
        Assert.True(coupledPeak <= 0.35 + 1e-9, $"coupled peak {coupledPeak}");
    }

    // Engines off and no atmosphere: neither engine nor aerodynamic torque exists, so only
    // reaction control and SAS can change the attitude rate on either integration path.
    private static Universe CreateUnpoweredFlight7Universe(string vesselId, bool coupled)
    {
        var universe = CreateFlight7Universe(vesselId, coupled);
        universe.ActiveVessel!.Throttle = 0.0;
        return universe;
    }

    private static Universe CreateUniverse(string vesselId, bool coupled, bool powered)
    {
        var body = new CelestialBody
        {
            Id = "parity-body",
            Name = "Parity Body",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6_371_000.0,
            SphereOfInfluence = 1.0e9,
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
        };
        var vessel = new Vessel(vesselId)
        {
            Position = Vector3d.Up * (body.Radius + (powered ? 1_000.0 : 250_000.0)),
            Velocity = powered ? Vector3d.Zero : Vector3d.Up * 7_600.0,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            Throttle = powered ? 1.0 : 0.0,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = powered ? "parity-engine" : "parity-payload",
            CategoryStr = powered ? "engine" : "structure",
            MassDry = powered ? 1_000.0 : 1_000.0,
            LengthM = 10.0,
            DiameterM = 2.0,
            ThrustVac = powered ? 50_000.0 : 0.0,
            ThrustSL = powered ? 50_000.0 : 0.0,
            IspVac = powered ? 300.0 : 0.0,
            IspSL = powered ? 300.0 : 0.0,
            FuelTypeStr = powered ? "LiquidFuel+Oxidizer" : "",
            MixtureRatio = powered ? 2.0 : 0.0,
            FuelCapacityLF = powered ? 1_000.0 : 0.0,
            FuelCapacityOx = powered ? 2_000.0 : 0.0,
            ThrustPositionYM = powered ? -5.0 : 0.0,
        }));

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Universe CreateFlight7Universe(
        string vesselId,
        bool coupled,
        bool useEarthData = false)
    {
        var root = FindRepoRoot();
        var body = useEarthData
            ? CelestialBody.LoadFromJson(Path.Combine(
                root.FullName, "data", "bodies", "earth.json"))
            : new CelestialBody
            {
                Id = "flight7-body",
                Name = "Flight 7 Body",
                Mass = 5.972e24,
                GM = 3.986004418e14,
                Radius = 6_371_000.0,
                SphereOfInfluence = 1.0e9,
                Position = Vector3d.Zero,
                Velocity = Vector3d.Zero,
            };
        var vessel = LoadFlight7Assembly(root).ToVessel("Starship Flight 7", vesselId);
        // 2 km above the reference surface: on the oblate Earth, mean radius + 2 km
        // can lie below the ellipsoid depending on latitude.
        vessel.Position = useEarthData
            ? body.GetSurfacePoint(Vector3d.Up * body.Radius, 2_000.0)
            : Vector3d.Up * (body.Radius + 2_000.0);
        vessel.Velocity = useEarthData
            ? body.GetSurfaceVelocity(vessel.Position)
            : Vector3d.Zero;
        vessel.ReferenceBodyId = body.Id;
        vessel.SASEnabled = false;
        vessel.Throttle = 1.0;

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static VesselAssembly LoadFlight7Assembly(DirectoryInfo root)
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(root.FullName, "data", "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(Path.Combine(
            root.FullName,
            "data",
            "vehicles",
            "starship_flight7_block2_2025.json"));
        return variant.Build(catalog);
    }

    private static bool ApplyElevationGuidance(Universe universe, double elapsedSeconds)
    {
        var vessel = universe.ActiveVessel!;
        var body = universe.GetBody(vessel.ReferenceBodyId!)!;
        double elevation = (90.0 - 55.0 * System.Math.Clamp(
            elapsedSeconds / 5.0, 0.0, 1.0)) * MathUtils.DEG_TO_RAD;
        var target = AttitudeGuidance.AimFromElevation(
            body.GetGeodeticUp(vessel.Position),
            body.GetEastDirection(vessel.Position),
            elevation);
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation,
            Vector3d.Up,
            target,
            vessel.AngularVelocity,
            proportionalGain: 2.6,
            dampingGain: 1.2);
        return vessel.PitchYawRoll.Magnitude > 1e-3;
    }

    private static void FailFlight7BoosterEngine(Universe universe, int engineIndex)
    {
        var booster = universe.ActiveVessel!.Parts.Parts.Single(
            part => part.Definition.Id == "super_heavy_booster");
        string instanceId = booster.EngineStates[engineIndex].InstanceId;
        Assert.True(booster.FailEngine(instanceId, "PARITY_ENGINE_OUT"));
    }

    private static Vector3d ApplyEngineOutRecovery(
        Universe universe,
        Vector3d previousCommand,
        double deltaSeconds)
    {
        var vessel = universe.ActiveVessel!;
        var body = universe.GetBody(vessel.ReferenceBodyId!)!;
        var command = EngineOutRecoveryGuidance.ComputeCommand(
            vessel,
            body.GetGeodeticUp(vessel.Position),
            previousCommand,
            deltaSeconds);
        vessel.PitchYawRoll = command;
        vessel.SASEnabled = false;
        return command;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }

    [Fact]
    public void FlapControlledEntryFliesLikeLegacy()
    {
        var legacy = CreateEntryCase("legacy-entry", coupled: false, 75_000.0, 7_300.0, -1.0, 70.0);
        var coupled = CreateEntryCase("coupled-entry", coupled: true, 75_000.0, 7_300.0, -1.0, 70.0);
        var legacyLog = FlyFlapEntry(legacy, steps: 3_000);
        var coupledLog = FlyFlapEntry(coupled, steps: 3_000);
        string summary = $"legacy {legacyLog}; coupled {coupledLog}";

        Assert.False(legacy.Vessel.IsDestroyed, summary);
        Assert.False(coupled.Vessel.IsDestroyed, summary);
        Assert.True(coupled.Universe.LastCoupled6DofTelemetry.Integrated);
        // Dynamic pressure must actually build (the flaps fly against a real static moment),
        // and both paths must hold the 70° belly-first trim on flaps alone.
        Assert.True(legacyLog.MaxDynamicPressurePa > 1_500.0, summary);
        Assert.Equal(1.0, legacyLog.FlapActiveFraction, precision: 2);
        Assert.Equal(1.0, coupledLog.FlapActiveFraction, precision: 2);
        Assert.True(legacyLog.MaxTrackingErrorDeg < 3.0 && coupledLog.MaxTrackingErrorDeg < 3.0, summary);
        Assert.True(System.Math.Abs(legacyLog.FinalTrackingErrorDeg - coupledLog.FinalTrackingErrorDeg) < 0.25, summary);
        Assert.True(System.Math.Abs(legacyLog.MaxAngularRate - coupledLog.MaxAngularRate) < 0.005, summary);
        // Sixty seconds and ~420 km down-range at 7 km/s: the trajectories stay together.
        Assert.True(System.Math.Abs(legacyLog.FinalAltitudeM - coupledLog.FinalAltitudeM) < 50.0, summary);
        Assert.True(System.Math.Abs(legacyLog.FinalSpeedMps - coupledLog.FinalSpeedMps) < 5.0, summary);
        Assert.True((legacyLog.FinalPosition - coupledLog.FinalPosition).Magnitude < 150.0, summary);
        Assert.True((legacyLog.FinalVelocity - coupledLog.FinalVelocity).Magnitude < 5.0, summary);
        Assert.True(System.Math.Abs(legacyLog.MaxDynamicPressurePa - coupledLog.MaxDynamicPressurePa)
            < 0.02 * legacyLog.MaxDynamicPressurePa, summary);
    }

    [Fact]
    public void SubsonicBellyFlopOnFlapsFliesLikeLegacy()
    {
        // The terminal belly-flop: 3 km, 80 m/s straight down, broadside. Twenty seconds of
        // flap-only attitude hold in the dense lower atmosphere, where the legacy path still
        // applies its post-aero rate envelope and the coupled path does not.
        var legacy = CreateEntryCase("legacy-bellyflop", coupled: false, 3_000.0, 80.0, -90.0, 90.0);
        var coupled = CreateEntryCase("coupled-bellyflop", coupled: true, 3_000.0, 80.0, -90.0, 90.0);
        var legacyLog = FlyFlapEntry(legacy, steps: 1_000);
        var coupledLog = FlyFlapEntry(coupled, steps: 1_000);
        string summary = $"legacy {legacyLog}; coupled {coupledLog}";

        Assert.False(legacy.Vessel.IsDestroyed, summary);
        Assert.False(coupled.Vessel.IsDestroyed, summary);
        Assert.True(legacyLog.MaxDynamicPressurePa > 2_500.0, summary);
        Assert.Equal(1.0, coupledLog.FlapActiveFraction, precision: 2);
        // The reference turns as horizontal drift tilts the flow off vertical, so transient
        // tracking is looser than in the hypersonic gate; both paths must agree on it.
        Assert.True(legacyLog.MaxTrackingErrorDeg < 8.0 && coupledLog.MaxTrackingErrorDeg < 8.0, summary);
        Assert.True(legacyLog.FinalTrackingErrorDeg < 2.0 && coupledLog.FinalTrackingErrorDeg < 2.0, summary);
        Assert.True(System.Math.Abs(legacyLog.FinalTrackingErrorDeg - coupledLog.FinalTrackingErrorDeg) < 0.25, summary);
        Assert.True(System.Math.Abs(legacyLog.MaxAngularRate - coupledLog.MaxAngularRate) < 0.01, summary);
        Assert.True(System.Math.Abs(legacyLog.FinalAltitudeM - coupledLog.FinalAltitudeM) < 5.0, summary);
        Assert.True(System.Math.Abs(legacyLog.FinalSpeedMps - coupledLog.FinalSpeedMps) < 0.5, summary);
        Assert.True((legacyLog.FinalPosition - coupledLog.FinalPosition).Magnitude < 15.0, summary);
        Assert.True((legacyLog.FinalVelocity - coupledLog.FinalVelocity).Magnitude < 1.0, summary);
    }

    private sealed record FlapEntryLog(
        double FinalAltitudeM, double FinalSpeedMps, double MaxDynamicPressurePa,
        double MaxTrackingErrorDeg, double FinalTrackingErrorDeg, double MaxAngularRate,
        double FlapActiveFraction, Vector3d FinalPosition, Vector3d FinalVelocity);

    private sealed record FlapEntryCase(Universe Universe, CelestialBody Body, Vessel Vessel, double AngleDeg);

    // Starship Flight 12 V3 Ship at 75 km, 7.3 km/s relative to the air and a -1° flight-path
    // angle, belly-first at 70° with the lift vector up. The four body flaps (with the
    // Flight 14 static-moment trim) are the only attitude actuators: engines off, SAS off.
    private static FlapEntryCase CreateEntryCase(string vesselId, bool coupled,
        double altitudeM, double airspeedMps, double flightPathDeg, double angleDeg)
    {
        var root = FindRepoRoot();
        var data = Path.Combine(root.FullName, "data");
        var body = CelestialBody.LoadFromJson(Path.Combine(data, "bodies", "earth.json"));
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data,
                "vehicles", "starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts")))
            .ToVessel("Entry parity", vesselId);
        Assert.NotNull(ship.Stage());
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        tank.LiquidFuel = 15_000; tank.Oxidizer = 55_000;

        ship.Position = body.GetSurfacePosition(23.0, -178.0, altitudeM);
        var up = body.GetGeodeticUp(ship.Position);
        var east = body.GetEastDirection(ship.Position);
        double gamma = flightPathDeg * MathUtils.DEG_TO_RAD;
        var flow = (east * System.Math.Cos(gamma) + up * System.Math.Sin(gamma)).Normalized;
        ship.Velocity = body.Velocity + body.GetSurfaceVelocity(ship.Position) + flow * airspeedMps;
        ship.Orientation = EntryReference(ship, body, angleDeg);
        ship.ReferenceBodyId = body.Id;
        ship.Throttle = 0.0;
        ship.SASEnabled = false;

        var universe = new Universe { ActiveVessel = ship, Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false };
        universe.AddBody(body);
        universe.AddVessel(ship);
        return new FlapEntryCase(universe, body, ship, angleDeg);
    }

    private static Quaterniond EntryReference(Vessel ship, CelestialBody body, double angleDeg)
    {
        var up = body.GetGeodeticUp(ship.Position);
        var flow = ship.GetSurfaceVelocity(body).Normalized;
        // Falling nearly straight down, `up` no longer defines a lift plane (the residual
        // horizontal drift would spin the reference in roll); hold a fixed east heading.
        var liftHint = System.Math.Abs(flow.Dot(up)) > 0.95 ? body.GetEastDirection(ship.Position) : up;
        var lift = liftHint - flow * liftHint.Dot(flow);
        return AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, lift.Normalized, angleDeg), flow);
    }

    private static FlapEntryLog FlyFlapEntry(FlapEntryCase run, int steps)
    {
        const double dt = 0.02;
        var (universe, body, ship) = (run.Universe, run.Body, run.Vessel);
        double maxQ = 0.0, maxError = 0.0, maxRate = 0.0, flapActive = 0.0, error = 0.0;
        int flown = 0;
        for (int i = 0; i < steps && !ship.IsDestroyed; i++)
        {
            var reference = EntryReference(ship, body, run.AngleDeg);
            error = AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference) * MathUtils.RAD_TO_DEG;
            // Tracking is scored after the 5 s flap-servo settle from the seeded pose.
            if (i >= 250) maxError = System.Math.Max(maxError, error);
            ship.PitchYawRoll = Flight14AerodynamicAttitude.ComputeCommand(ship, body, reference, 250, 1000);
            universe.Tick(dt);
            maxQ = System.Math.Max(maxQ, ship.GetDynamicPressure(body));
            maxRate = System.Math.Max(maxRate, ship.AngularVelocity.Magnitude);
            flapActive += ship.FlapActuators.IsZero ? 0.0 : 1.0;
            flown++;
        }
        return new FlapEntryLog(body.GetAltitude(ship.Position),
            ship.GetSurfaceVelocity(body).Magnitude, maxQ, maxError, error, maxRate,
            flapActive / System.Math.Max(1, flown), ship.Position, ship.Velocity);
    }

    [Fact]
    public void ContinuousEntryToBellyFlopFliesLikeLegacy()
    {
        var definition = Flight14DescentDefinition.LoadFromJson(Path.Combine(FindRepoRoot().FullName,
            "data", "flight_profiles", "starship_flight14_descent_estimate.json"));
        var legacy = CreateEntryCase("legacy-continuous", coupled: false, 90_000.0, 7_300.0, -1.5,
            definition.EntryAngleOfAttackDegrees);
        var coupled = CreateEntryCase("coupled-continuous", coupled: true, 90_000.0, 7_300.0, -1.5,
            definition.EntryAngleOfAttackDegrees);
        var legacyLog = FlyProductionDescent(legacy, definition);
        var coupledLog = FlyProductionDescent(coupled, definition);
        string summary = $"legacy {legacyLog}; coupled {coupledLog}";

        // Both paths complete the descent on flaps alone, through Mach 1, intact.
        Assert.True(legacyLog.Reached && coupledLog.Reached, summary);
        Assert.False(legacy.Vessel.IsDestroyed || coupled.Vessel.IsDestroyed, summary);
        Assert.False(legacy.Vessel.StructuralControlLost || coupled.Vessel.StructuralControlLost, summary);
        Assert.True(legacyLog.PeakLoadG > 1.0 && legacyLog.PeakQPa > 1_000.0, summary);
        Assert.False(double.IsNaN(legacyLog.TimeAtMach1S) || double.IsNaN(coupledLog.TimeAtMach1S), summary);
        Assert.True(legacyLog.EndVerticalMps < 0 && coupledLog.EndVerticalMps < 0, summary);
        // Timeline: ~15 min from 90 km to the 3 km belly-flop (measured 0.4 s and 0.3 s apart).
        Assert.True(System.Math.Abs(legacyLog.ElapsedS - coupledLog.ElapsedS) < 3.0, summary);
        Assert.True(System.Math.Abs(legacyLog.TimeAtMach1S - coupledLog.TimeAtMach1S) < 3.0, summary);
        // Loads and heating peaks (measured within 0.3%).
        Assert.True(System.Math.Abs(legacyLog.PeakQPa - coupledLog.PeakQPa) < 0.02 * legacyLog.PeakQPa, summary);
        Assert.True(System.Math.Abs(legacyLog.PeakLoadG - coupledLog.PeakLoadG) < 0.02 * legacyLog.PeakLoadG, summary);
        Assert.True(System.Math.Abs(legacyLog.PeakHeatFlux - coupledLog.PeakHeatFlux) < 0.02 * legacyLog.PeakHeatFlux, summary);
        // Attitude dynamics, including the transonic band where the legacy post-aero rate
        // envelope is the one remaining unported difference.
        Assert.True(System.Math.Abs(legacyLog.PeakRate - coupledLog.PeakRate) < 0.005, summary);
        Assert.True(System.Math.Abs(legacyLog.TransonicPeakRate - coupledLog.TransonicPeakRate) < 0.005, summary);
        Assert.True(System.Math.Abs(legacyLog.EndTrackingDeg - coupledLog.EndTrackingDeg) < 2.0, summary);
        // Endpoint: belly-flop state and ground track after ~5 000 km. The elapsed-time gate
        // above already bounds the along-track timing offset; at entry speed that offset alone
        // moves the endpoint (0.86 s ≈ 6 km), so it is not charged twice here.
        Assert.True(System.Math.Abs(legacyLog.EndSpeedMps - coupledLog.EndSpeedMps) < 1.0, summary);
        double alongTrackOffsetM = 7_300.0 * System.Math.Abs(legacyLog.ElapsedS - coupledLog.ElapsedS);
        Assert.True((legacyLog.EndPosition - coupledLog.EndPosition).Magnitude < 5_000.0 + alongTrackOffsetM, summary);
    }

    private sealed record ContinuousEntryLog(
        bool Reached, double ElapsedS, double EndAltitudeM, double EndSpeedMps, double EndVerticalMps,
        double PeakQPa, double PeakLoadG, double PeakHeatFlux, double PeakRate,
        double TransonicPeakRate, double EndTrackingDeg, double TimeAtMach1S,
        Vector3d EndPosition);

    // Production Flight 14 descent law (bank-limited hypersonic corridor, 55°→90° angle
    // schedule, slewed belly-first reference, flap trim) from the 90 km handoff to the 3 km
    // belly-flop endpoint, applied identically to both integration paths.
    private static ContinuousEntryLog FlyProductionDescent(FlapEntryCase run, Flight14DescentDefinition definition)
    {
        const double dt = 0.02;
        var (universe, body, ship) = (run.Universe, run.Body, run.Vessel);
        var reference = ship.Orientation;
        var priorLift = ship.Orientation.Rotate(Vector3d.Up);
        double t = 0, peakQ = 0, peakLoad = 0, peakHeat = 0, peakRate = 0, transonicRate = 0, mach1 = double.NaN;
        bool reached = false;
        for (int i = 0; i < 75_000 && !ship.IsDestroyed; i++)
        {
            var state = EntryFlightDiagnostics.Evaluate(ship, body);
            peakQ = System.Math.Max(peakQ, state.DynamicPressurePa);
            peakLoad = System.Math.Max(peakLoad, state.AerodynamicLoadG);
            peakHeat = System.Math.Max(peakHeat, state.StagnationHeatFluxWPerM2);
            double speed = ship.GetSurfaceVelocity(body).Magnitude;
            if (double.IsNaN(mach1) && state.Mach < 1.0) mach1 = t;
            if (state.AltitudeM <= definition.DiagnosticEndAltitudeM) { reached = true; break; }
            ship.SASEnabled = false;
            ship.Throttle = 0.0;
            ship.PitchYawRoll = Flight14DescentController.ComputeGuidanceCommand(
                ship, body, definition, state, dt, ref reference, ref priorLift);
            universe.Tick(dt);
            t += dt;
            peakRate = System.Math.Max(peakRate, ship.AngularVelocity.Magnitude);
            if (state.Mach is > 0.5 and < 2.0)
                transonicRate = System.Math.Max(transonicRate, ship.AngularVelocity.Magnitude);
        }
        var surface = ship.GetSurfaceVelocity(body);
        return new ContinuousEntryLog(reached, t, body.GetAltitude(ship.Position), surface.Magnitude,
            surface.Dot(body.GetGeodeticUp(ship.Position)), peakQ, peakLoad, peakHeat, peakRate, transonicRate,
            AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference) * MathUtils.RAD_TO_DEG, mach1,
            ship.Position);
    }

    [Fact]
    public void ProductionAscentThroughStagingFliesLikeLegacy()
    {
        var legacy = FlyProductionAscent(coupled: false);
        var coupled = FlyProductionAscent(coupled: true);
        string summary = $"legacy {legacy}; coupled {coupled}";

        Assert.Equal(Flight14LaunchPhase.OrbitReady, legacy.Phase);
        Assert.Equal(Flight14LaunchPhase.OrbitReady, coupled.Phase);
        Assert.True(coupled.Coupled && !legacy.Coupled, summary);
        // Booster phase and hot staging (measured: identical staging time, 0.57 km and 2 m/s).
        Assert.True(System.Math.Abs(legacy.StagingS - coupled.StagingS) < 0.5, summary);
        Assert.True(System.Math.Abs(legacy.StagingAltitudeM - coupled.StagingAltitudeM) < 1_500.0, summary);
        Assert.True(System.Math.Abs(legacy.StagingSpeedMps - coupled.StagingSpeedMps) < 15.0, summary);
        // Max-q (measured 2.2%, 0.7 s): the coupled evaluator samples air and gravity at the
        // centre of mass, the legacy tick at the aft datum tens of metres lower.
        Assert.True(System.Math.Abs(legacy.PeakQPa - coupled.PeakQPa) < 0.05 * legacy.PeakQPa, summary);
        Assert.True(System.Math.Abs(legacy.MaxQTimeS - coupled.MaxQTimeS) < 2.0, summary);
        // Ship cutoff and insertion (measured 0.28 s, 0.3 / 0.6 km apsides, 3.6 s, 0.4% mass).
        Assert.True(System.Math.Abs(legacy.CutoffS - coupled.CutoffS) < 2.0, summary);
        Assert.True(System.Math.Abs(legacy.CutoffApoapsisM - coupled.CutoffApoapsisM) < 2_000.0, summary);
        Assert.True(System.Math.Abs(legacy.CutoffPeriapsisM - coupled.CutoffPeriapsisM) < 2_000.0, summary);
        Assert.True(System.Math.Abs(legacy.InsertionS - coupled.InsertionS) < 10.0, summary);
        Assert.True(System.Math.Abs(legacy.ShipMassKg - coupled.ShipMassKg) < 0.01 * legacy.ShipMassKg, summary);
        // Attitude dynamics through max-q, staging transients and the Ship burn.
        Assert.True(System.Math.Abs(legacy.PeakRateBooster - coupled.PeakRateBooster) < 0.1 * legacy.PeakRateBooster, summary);
        Assert.True(System.Math.Abs(legacy.PeakRateShip - coupled.PeakRateShip) < 0.1 * legacy.PeakRateShip, summary);
    }

    private sealed record ProductionAscentLog(
        Flight14LaunchPhase Phase, string? BlockReason, double StagingS, double CutoffS, double InsertionS,
        double CutoffApoapsisM, double CutoffPeriapsisM, double ShipMassKg, double PeakRate,
        double PeakRateBooster, double PeakRateShip, double PeakQPa, double MaxQTimeS,
        double StagingAltitudeM, double StagingSpeedMps, bool Coupled);

    // The production Flight 14 launch (pad, 33 Raptors, max-q, hot staging, Ship ascent,
    // coast and insertion) flown by Flight14LaunchController on each integration path.
    private static ProductionAscentLog FlyProductionAscent(bool coupled)
    {
        var data = Path.Combine(FindRepoRoot().FullName, "data");
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies", "earth.json"));
        earth.Position = Vector3d.Zero; earth.Velocity = Vector3d.Zero; earth.OrbitalElements = null;
        var universe = new Universe { Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false };
        universe.AddBody(earth);
        var run = Flight14LaunchDiagnostic.Create(data, universe);
        var (ship, controller) = (run.Ship, run.Controller);
        double peakRate = 0, peakBooster = 0, peakShip = 0, peakQ = 0, maxQTime = 0;
        double stagingAltitude = double.NaN, stagingSpeed = double.NaN;
        bool coupledSeen = false;
        for (int i = 0; i < 100_000 && controller.Phase is not (Flight14LaunchPhase.OrbitReady or Flight14LaunchPhase.Blocked)
            && !ship.IsDestroyed; i++)
        {
            universe.Tick(0.02);
            coupledSeen |= universe.LastCoupled6DofTelemetry.Integrated;
            double rate = ship.AngularVelocity.Magnitude;
            peakRate = System.Math.Max(peakRate, rate);
            if (controller.Phase == Flight14LaunchPhase.BoosterAscent) peakBooster = System.Math.Max(peakBooster, rate);
            if (controller.Phase == Flight14LaunchPhase.ShipAscent) peakShip = System.Math.Max(peakShip, rate);
            double q = ship.GetDynamicPressure(earth);
            if (q > peakQ) { peakQ = q; maxQTime = universe.CurrentTime - controller.LiftoffEpoch; }
            if (double.IsNaN(stagingAltitude) && !double.IsNaN(controller.StagingElapsedSeconds))
            {
                stagingAltitude = earth.GetAltitude(ship.Position);
                stagingSpeed = (ship.Velocity - earth.Velocity).Magnitude;
            }
        }
        return new ProductionAscentLog(controller.Phase, controller.BlockReason, controller.StagingElapsedSeconds,
            controller.CutoffElapsedSeconds, controller.InsertionElapsedSeconds,
            controller.CutoffApoapsisAltitudeM, controller.CutoffPeriapsisAltitudeM, ship.TotalMass,
            peakRate, peakBooster, peakShip, peakQ, maxQTime, stagingAltitude, stagingSpeed, coupledSeen);
    }

    [Fact]
    public void BellyFlopFlipAndRelightFliesLikeLegacy()
    {
        var legacy = FlyProductionFlip(coupled: false);
        var coupled = FlyProductionFlip(coupled: true);
        string summary = $"legacy {legacy}; coupled {coupled}";

        // Integrator parity, whatever the outcome: the guidance is tuned on the distributed
        // aerodynamics, while this gate flies both integrators on the aggregate model the legacy
        // path supports, under which the post-flip braking does not reach the 100 m witness.
        Assert.Equal(legacy.Phase, coupled.Phase);
        Assert.Equal(legacy.BlockReason, coupled.BlockReason);
        Assert.True(coupled.Coupled && !legacy.Coupled, summary);
        Assert.True(legacy.ThreeEngines && coupled.ThreeEngines, summary);
        Assert.Equal(legacy.MinimumEngines, coupled.MinimumEngines);
        Assert.True(System.Math.Abs(legacy.FlipCompleteS - coupled.FlipCompleteS) < 0.5, summary);
        Assert.True(System.Math.Abs(legacy.PeakRate - coupled.PeakRate) < 0.05 * legacy.PeakRate, summary);
        Assert.True(System.Math.Abs(legacy.EndS - coupled.EndS) < 1.0, summary);
        Assert.True(System.Math.Abs(legacy.PropellantUsedKg - coupled.PropellantUsedKg) < 0.02 * legacy.PropellantUsedKg, summary);
        if (legacy.Phase == Flight14LandingPhase.TerminalReached)
        {
            Assert.True(System.Math.Abs(legacy.EndSpeedMps - coupled.EndSpeedMps) < 0.5, summary);
            Assert.True(System.Math.Abs(legacy.EndVerticalMps - coupled.EndVerticalMps) < 0.5, summary);
            Assert.True(System.Math.Abs(legacy.EndUpright - coupled.EndUpright) < 0.005, summary);
        }
    }

    private sealed record FlipLog(Flight14LandingPhase Phase, string? BlockReason, double FlipCompleteS,
        double EndS, double EndAltitudeM, double EndSpeedMps, double EndVerticalMps, double EndUpright,
        double PropellantUsedKg, double PeakRate, int MinimumEngines, bool ThreeEngines, bool Coupled);

    private sealed class LandingBurnStep(Flight14LandingBurn burn) : IPhysicsStepController
    {
        public bool RequiresFixedCadence(Universe universe) => true;
        public void BeforePhysicsStep(Universe universe, double interval) => burn.Advance(universe, interval);
    }

    // The production Flight 14 flip: belly-flop at the 1.1 km gate and ~100 m/s down, three
    // sea-level Raptors relit from start budget, rotation to vertical on gimbal and flaps, then
    // the braking burn and engine step-down to the retained 100 m terminal witness.
    private static FlipLog FlyProductionFlip(bool coupled)
    {
        var data = Path.Combine(FindRepoRoot().FullName, "data");
        var body = CelestialBody.LoadFromJson(Path.Combine(data, "bodies", "earth.json"));
        body.Position = Vector3d.Zero; body.Velocity = Vector3d.Zero; body.OrbitalElements = null;
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles", "starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Flip parity", coupled ? "coupled-flip" : "legacy-flip");
        Assert.NotNull(ship.Stage());
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        var landing = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles", "starship_flight14_landing_estimate.json"));
        // The production state at the flip gate: main tanks vented before entry, header
        // tanks full, falling belly-first at the trimmed 80° and ~100 m/s of the full return.
        tank.LiquidFuel = 0; tank.Oxidizer = 0;
        ship.Position = body.GetSurfacePositionAtTime(23, -178, 0, landing.FlipAltitudeM);
        var up = body.GetGeodeticUp(ship.Position);
        var flow = -up;
        ship.Velocity = body.GetSurfaceVelocity(ship.Position) + flow * 100;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, Vector3d.Right, 80), flow);
        ship.ReferenceBodyId = body.Id; ship.SASEnabled = false;
        var universe = new Universe { Coupled6DofIntegrationEnabled = coupled, DistributedAerodynamicsEnabled = false };
        universe.AddBody(body); universe.AddVessel(ship); universe.ActiveVessel = ship;
        var definition = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles", "starship_flight14_landing_estimate.json"));
        definition.ContinueToWaterContact = false;
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        universe.PhysicsStepController = new LandingBurnStep(burn);

        double startPropellant = ship.Parts.Parts.Sum(p => p.LiquidFuel + p.Oxidizer + p.HeaderLiquidFuel + p.HeaderOxidizer);
        double t = 0, flipComplete = double.NaN;
        bool coupledSeen = false;
        for (int i = 0; i < 6_000 && burn.Phase is not
            (Flight14LandingPhase.TerminalReached or Flight14LandingPhase.Blocked); i++)
        {
            universe.Tick(0.02);
            t += 0.02;
            coupledSeen |= universe.LastCoupled6DofTelemetry.Integrated;
            if (double.IsNaN(flipComplete) && burn.Phase == Flight14LandingPhase.Braking) flipComplete = t;
        }
        var end = burn.EndWitness;
        return new FlipLog(burn.Phase, burn.BlockReason, flipComplete, t,
            end?.GeodeticAltitudeM ?? double.NaN, end?.AtmosphereRelativeSpeedMps ?? double.NaN,
            end?.VerticalSpeedMps ?? double.NaN, end?.UprightAlignment ?? double.NaN,
            startPropellant - ship.Parts.Parts.Sum(p => p.LiquidFuel + p.Oxidizer + p.HeaderLiquidFuel + p.HeaderOxidizer),
            burn.MaximumAngularRateRadPerSecond, burn.MinimumSelectedEngineCount,
            burn.HasDeliveredThreeSeaLevelEngines, coupledSeen);
    }
}
