namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Presentation;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14BoosterReturnTests(ITestOutputHelper output)
{
    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data/flight_profiles")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }

    [Fact]
    public void MovingEarthLoadedMissionReturnsOriginalBoosterAndKeepsShipInFlight()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory(), universe);
        var controller = run.BoosterReturnController!;
        var part = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        double reserveLF = part.ReservedLiquidFuel, reserveOx = part.ReservedOxidizer;
        double previousFuel = part.LiquidFuel+part.Oxidizer;
        var deliveredLandingCounts = new HashSet<int>();
        int minimumSelected = 11;
        double maximumAltitude = 0, deliveredThreeEngineSeconds = 0, previousDeltaVProgress = 0;
        double previousElevation = 90;
        bool observedBoostbackThrottle = false;
        var presenter = new FlightHudPresenter();
        for (int step = 0; step < 100000 && run.Controller.Phase != Flight14LaunchPhase.Blocked
            && !(run.Controller.Phase == Flight14LaunchPhase.OrbitReady && controller.IsStopped); step++)
        {
            universe.Tick(0.02);
            Assert.Same(run.Ship, universe.ActiveVessel);
            Assert.False(run.Ship.IsDestroyed);
            Assert.True(part.LiquidFuel+part.Oxidizer <= previousFuel+1e-6);
            previousFuel = part.LiquidFuel+part.Oxidizer;
            if (controller.Booster is { } observed && !controller.IsStopped)
                maximumAltitude = System.Math.Max(maximumAltitude, observed.GetAltitude(run.Earth));
            if (!observedBoostbackThrottle && controller.HasDeliveredBoostbackEngines
                && controller.Phase == Flight14BoosterReturnPhase.Boostback)
            {
                var snapshot = presenter.Capture(universe, controller.Booster!, "BOOSTBACK", FlightHudViewMode.Exterior);
                Assert.Equal(controller.Booster!.Id, snapshot.VesselId);
                Assert.Equal(controller.Booster.Throttle, snapshot.Throttle);
                Assert.InRange(snapshot.Throttle, 0.4, 1);
                observedBoostbackThrottle = true;
            }
            if (controller.Phase is Flight14BoosterReturnPhase.Flip or Flight14BoosterReturnPhase.Boostback or Flight14BoosterReturnPhase.Coast)
            {
                if (controller.Phase is Flight14BoosterReturnPhase.Flip or Flight14BoosterReturnPhase.Boostback)
                {
                    Assert.InRange(controller.BoostbackDeltaVProgress, previousDeltaVProgress, 1);
                    Assert.InRange(controller.BoostbackElevationDegrees, -78, previousElevation);
                    previousDeltaVProgress = controller.BoostbackDeltaVProgress;
                    previousElevation = controller.BoostbackElevationDegrees;
                    Assert.True(double.IsFinite(controller.Booster!.PitchYawRoll.Magnitude));
                }
                Assert.InRange(part.LiquidFuel, reserveLF-1e-6, double.MaxValue);
                Assert.InRange(part.Oxidizer, reserveOx-1e-6, double.MaxValue);
                Assert.Equal(reserveLF, part.ReservedLiquidFuel); Assert.Equal(reserveOx, part.ReservedOxidizer);
            }
            if (controller.Phase == Flight14BoosterReturnPhase.Landing)
            {
                Assert.True(controller.Booster!.GetSurfaceVelocity(run.Earth)
                    .Dot(run.Earth.GetGeodeticUp(controller.Booster.Position)) < 0,
                    "Landing guidance must not create a climb before contact.");
                Assert.True(part.SelectedEngineCount <= minimumSelected);
                minimumSelected = part.SelectedEngineCount;
                int running = part.GetEngineTelemetry(controller.Booster!.GetAmbientPressure(run.Earth)).Count(e => e.State == Exosphere.Simulation.Propulsion.EngineLifecycleState.Running && e.ThrustN > 1);
                if (running == part.SelectedEngineCount) deliveredLandingCounts.Add(running);
                if (running == 3) deliveredThreeEngineSeconds += 0.02;
            }
        }
        Assert.Equal(Flight14LaunchPhase.OrbitReady, run.Controller.Phase);
        Assert.Equal(Flight14BoosterReturnPhase.ContactObserved, controller.Phase);
        Assert.Null(controller.BlockReason);
        Assert.Same(run.Controller.DetachedBooster, controller.Booster);
        Assert.Same(part, controller.Booster!.Parts.Root);
        Assert.False(controller.Booster.IsGroundHeld);
        Assert.True(controller.HasDeliveredBoostbackEngines);
        Assert.True(controller.HasDeliveredLandingIgnition);
        Assert.Contains(11, deliveredLandingCounts); Assert.Contains(5, deliveredLandingCounts); Assert.Contains(3, deliveredLandingCounts);
        var cutoff = Assert.Single(controller.Events.Where(e => e.Phase == Flight14BoosterReturnPhase.Coast));
        Assert.InRange(cutoff.MainOxidizerKg, 0, 1e-6);
        Assert.InRange(controller.BoostbackDeltaVProgress, 0.99, 1);
        // LOX exhaustion can leave unmatched fuel in the inherited tank mixture.
        Assert.InRange(cutoff.PropellantKg, reserveLF+reserveOx-1e-6, reserveLF+reserveOx+1000);
        var contact = Assert.Single(controller.Events.Where(e => e.Phase == Flight14BoosterReturnPhase.WaterEntry));
        Assert.InRange(contact.AirspeedMps, 0, 8);
        Assert.True(contact.VerticalSpeedMps < 0);
        // Broad engineering regression envelopes, not measured Flight 14 tolerances.
        // Reject the former 207 km apogee / T+582 return and instant final relight.
        Assert.InRange(maximumAltitude, 80_000, 115_000);
        Assert.InRange(contact.MissionElapsedSeconds, 380, 460);
        Assert.InRange(deliveredThreeEngineSeconds, 1, 30);
        Assert.True(observedBoostbackThrottle);
        Assert.InRange(contact.LatitudeDegrees, 23, 29); Assert.InRange(contact.LongitudeDegrees, -97.25, -90);
        Assert.True(controller.Booster.WaterMotionEnabled);
        Assert.NotNull(controller.Booster.LastWaterMotionTelemetry);
        Assert.False(universe.Coupled6DofIntegrationEnabled);
        Assert.True(controller.FlightTerminationTriggered);
        Assert.Equal(VesselDestructionCause.FlightTermination, controller.Booster.DestructionCause);
        Assert.Equal(0, controller.Booster.Throttle);
        var terminal = controller.Events.Last();
        var terminalSnapshot = presenter.CaptureRetiredBooster(controller, run.Earth.Id,
            run.Controller.LiftoffEpoch, universe.TimeScale, FlightHudViewMode.Exterior);
        Assert.Equal(terminal.AltitudeM, terminalSnapshot.AltitudeM);
        Assert.Equal(terminal.AirspeedMps, terminalSnapshot.SurfaceSpeedMps);
        Assert.Equal(terminal.VerticalSpeedMps, terminalSnapshot.VerticalSpeedMps);
        Assert.Equal(run.Controller.LiftoffEpoch + terminal.MissionElapsedSeconds, terminalSnapshot.MissionTimeS);
        Assert.Null(terminalSnapshot.ApoapsisAltitudeM); Assert.Null(terminalSnapshot.PeriapsisAltitudeM);
        Assert.True(double.IsNaN(terminalSnapshot.ProperAccelerationG));
        Assert.True(double.IsNaN(terminalSnapshot.DynamicPressurePa));
        Assert.True(double.IsNaN(terminalSnapshot.VehiclePitchDeg));
        Assert.True(double.IsNaN(terminalSnapshot.HeadingDeg));
        Assert.False(terminalSnapshot.HasDownrangeReference);
        Assert.Equal(33, terminalSnapshot.NominalEngineCount);
        Assert.Empty(terminalSnapshot.Alerts); Assert.Equal(0, terminalSnapshot.ActiveEngineCount);
        // Earth has continued to propagate all the way to the ship's orbital boundary.
        // A wreck's old inertial position must never masquerade as a new altitude/orbit.
        Assert.NotEqual(terminal.AltitudeM, controller.Booster.GetAltitude(run.Earth));
        var repeated = presenter.CaptureRetiredBooster(controller, run.Earth.Id,
            run.Controller.LiftoffEpoch, universe.TimeScale, FlightHudViewMode.Cockpit);
        Assert.Equal(terminalSnapshot.AltitudeM, repeated.AltitudeM);
        Assert.Equal(terminalSnapshot.SurfaceSpeedMps, repeated.SurfaceSpeedMps);
        var shipSnapshot = presenter.Capture(universe, run.Ship, "ORBIT", FlightHudViewMode.Exterior);
        Assert.Equal(run.Ship.Id, shipSnapshot.VesselId);
        Assert.Equal(run.Ship.GetAltitude(run.Earth), shipSnapshot.AltitudeM);
        Assert.True(double.IsFinite(shipSnapshot.DynamicPressurePa));
        Assert.False(new Flight14Exploration(run).ObserveBooster(true));
        Assert.Single(part.EngineStates.Where(e => e.FailureCode == "F14_ESTIMATED_ASCENT_OUT"));
        output.WriteLine($"Booster: boostback cutoff T+{cutoff.MissionElapsedSeconds:F2}; water T+{contact.MissionElapsedSeconds:F2}; "
            + $"speed={contact.AirspeedMps:F3}m/s latitude={contact.LatitudeDegrees:F6} longitude={contact.LongitudeDegrees:F6}; "
            + $"ship orbit T+{run.Controller.OrbitElapsedSeconds:F2}");
    }

    [Theory]
    [InlineData(double.NaN, -78)]
    [InlineData(double.PositiveInfinity, -78)]
    [InlineData(90, -78)]
    [InlineData(-90, -89)]
    [InlineData(89, double.NaN)]
    [InlineData(89, double.NegativeInfinity)]
    [InlineData(89, -90)]
    [InlineData(89, 90)]
    [InlineData(89, 89)]
    [InlineData(-79, -78)]
    public void ReturnEstimateRejectsInvalidPitchSweep(double initialElevation, double finalElevation)
    {
        var definition = Flight14BoosterReturnDefinition.LoadFromJson(
            Path.Combine(DataDirectory(), "flight_profiles/starship_flight14_booster_return_estimate.json"));
        definition.BoostbackInitialElevationDegrees = initialElevation;
        definition.BoostbackFinalElevationDegrees = finalElevation;
        Assert.Throws<InvalidDataException>(() => definition.Validate());
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0.99)]
    [InlineData(2.01)]
    public void ReturnEstimateRejectsInvalidBrakingMargin(double margin)
    {
        var definition = Flight14BoosterReturnDefinition.LoadFromJson(
            Path.Combine(DataDirectory(), "flight_profiles/starship_flight14_booster_return_estimate.json"));
        definition.LandingBrakingMargin = margin;
        Assert.Throws<InvalidDataException>(() => definition.Validate());
    }

    [Fact]
    public void UnintegratedControlUpdatesCannotAdvancePitchSweepOrFlightState()
    {
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory());
        var controller = run.BoosterReturnController!;
        while (!controller.HasDeliveredBoostbackEngines && !controller.IsStopped && run.Universe.CurrentTime < 200)
            run.Universe.Tick(0.02);
        Assert.True(controller.HasDeliveredBoostbackEngines);
        var booster = controller.Booster!;
        // Synchronize the pre-integration command reference after the last tick.
        controller.BeforePhysicsStep(run.Universe, 0.02);
        var position = booster.Position; var velocity = booster.Velocity; var orientation = booster.Orientation;
        double mass = booster.TotalMass, progress = controller.BoostbackDeltaVProgress, elevation = controller.BoostbackElevationDegrees;
        for (int i = 0; i < 100; i++) controller.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(progress, controller.BoostbackDeltaVProgress);
        Assert.Equal(elevation, controller.BoostbackElevationDegrees);
        Assert.Equal(mass, booster.TotalMass);
        Assert.Equal(position, booster.Position); Assert.Equal(velocity, booster.Velocity);
        Assert.Equal(orientation, booster.Orientation);
    }

    [Fact]
    public void ExhaustedMainFeedCannotCreatePitchProgressOrCertifyBoostback()
    {
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory());
        var controller = run.BoosterReturnController!;
        while (controller.Booster == null && run.Universe.CurrentTime < 200) run.Universe.Tick(0.02);
        var engine = controller.Booster!.Parts.Root!;
        engine.LiquidFuel = engine.ReservedLiquidFuel; engine.Oxidizer = engine.ReservedOxidizer;
        controller.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(Flight14BoosterReturnPhase.Blocked, controller.Phase);
        Assert.Equal("boostback-thrust-not-delivered", controller.BlockReason);
        Assert.False(controller.HasDeliveredBoostbackEngines);
        Assert.Equal(0, controller.Booster.Throttle);
        Assert.True(double.IsFinite(controller.BoostbackDeltaVProgress));
        Assert.True(double.IsFinite(controller.Booster.PitchYawRoll.Magnitude));
        Assert.Same(run.Ship, run.Universe.ActiveVessel);
    }

    [Fact]
    public void BoosterEngineShortfallDoesNotBlockOrReplaceShipAuthority()
    {
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory());
        var c = run.BoosterReturnController!;
        while (c.Booster == null && run.Universe.CurrentTime < 200) run.Universe.Tick(0.02);
        var booster = c.Booster!; var part = booster.Parts.Root!;
        foreach (var engine in part.EngineStates.Take(2)) part.FailEngine(engine.InstanceId, "TEST_RETURN_SHORTFALL");
        while (!c.IsStopped && run.Universe.CurrentTime < 250) run.Universe.Tick(0.02);
        Assert.Equal(Flight14BoosterReturnPhase.Blocked, c.Phase);
        Assert.Equal("boostback-engine-count-unavailable", c.BlockReason);
        Assert.Equal(0, booster.Throttle);
        Assert.Same(run.Ship, run.Universe.ActiveVessel);
        Assert.NotEqual(Flight14LaunchPhase.Blocked, run.Controller.Phase);
        Assert.False(new Flight14Exploration(run).IsStopped);
    }

    [Fact]
    public void ObserverSelectionDoesNotChangePhysicsOwnerAndTerminalBoundaryCanReturnToShip()
    {
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory());
        var preview = new Flight14Exploration(run);
        Assert.False(preview.ObserveBooster(true));
        Assert.Throws<InvalidOperationException>(() => new FlightHudPresenter().CaptureRetiredBooster(
            run.BoosterReturnController!, run.Earth.Id, 0, 1, FlightHudViewMode.Exterior));
        while (run.BoosterReturnController!.Booster == null && run.Universe.CurrentTime < 200) run.Universe.Tick(0.02);
        Assert.True(preview.ObserveBooster(true));
        Assert.Same(run.BoosterReturnController!.Booster, preview.ObservedVessel);
        Assert.Same(run.Ship, run.Universe.ActiveVessel);
        // A physical unavailable-stage boundary, not a fake terminal success.
        preview.ObservedVessel.Parts.Root!.IsBroken = true;
        preview.AdvanceFrame(0.02);
        Assert.True(preview.IsPaused); Assert.False(preview.IsStopped);
        double epoch = run.Universe.CurrentTime;
        Assert.Equal(0, preview.AdvanceFrame(1)); Assert.Equal(epoch, run.Universe.CurrentTime);
        Assert.True(preview.ObserveBooster(false));
        Assert.False(preview.IsPaused); Assert.Same(run.Ship, preview.ObservedVessel);
        Assert.InRange(preview.AdvanceFrame(0.02), 0.01999, 0.02001);
    }

    [Fact]
    public void LandingFeedIsolationConservesWetMassAndSurvivesBothSaveFormats()
    {
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(DataDirectory());
        var part = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(DataDirectory(), "parts"));
        var inherited = Flight14LaunchDiagnostic.Create(DataDirectory()).Ship.Parts.Parts
            .Single(p => p.Definition.HasVehicleRole("booster"));
        Assert.Equal(inherited.LiquidFuel, part.LiquidFuel);
        Assert.Equal(inherited.Oxidizer, part.Oxidizer);
        double mass = part.CurrentMass;
        var legacy = MissionSaveSerializer.Capture(run.Universe);
        var legacyUniverse = new Universe();
        MissionSaveSerializer.Restore(legacyUniverse, legacy, PartDefinition.LoadAllFromDirectory(Path.Combine(DataDirectory(), "parts")));
        var legacyPart = legacyUniverse.ActiveVessel!.Parts.Parts.Single(p => p.Definition.Id == part.Definition.Id);
        Assert.Equal(part.ReservedOxidizer, legacyPart.ReservedOxidizer);
        var save = SaveGameV2Codec.Capture(run.Universe);
        var restored = new Universe(); SaveGameV2Codec.Restore(restored, save, catalog);
        var copy = restored.ActiveVessel!.Parts.Parts.Single(p => p.Definition.Id == part.Definition.Id);
        Assert.Equal(part.ReservedLiquidFuel, copy.ReservedLiquidFuel); Assert.Equal(part.ReservedOxidizer, copy.ReservedOxidizer);
        Assert.Equal(mass, copy.CurrentMass);
        copy.OpenLandingPropellantFeed(); Assert.Equal(mass, copy.CurrentMass); Assert.Equal(0, copy.ReservedOxidizer);
        save.Vessels[0].Parts.Single(p => p.DefinitionId == part.Definition.Id).ReservedOxidizer = part.Oxidizer+1;
        Assert.Throws<InvalidDataException>(() => SaveGameV2Codec.Validate(save));
    }

    [Fact]
    public void StandaloneAvionicsAndStaticMarginDoNotAlterAttachedStackAuthority()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        Assert.Null(run.Ship.Parts.AerodynamicCenterOffsetYM);
        var booster = run.Ship.Stage()!;
        Assert.Equal(ControlAuthority.Full, booster.ControlAuthorityFactor);
        Assert.Equal(5.44, booster.Parts.AerodynamicCenterOffsetYM);
        booster.Parts.Root!.IsBroken = true;
        Assert.True(booster.StructuralControlLost);
        Assert.Equal(ControlAuthority.Full, run.Ship.ControlAuthorityFactor);
    }
}
