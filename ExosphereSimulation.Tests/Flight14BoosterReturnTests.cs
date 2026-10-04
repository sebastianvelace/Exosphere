namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
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
        for (int step = 0; step < 100000 && run.Controller.Phase != Flight14LaunchPhase.Blocked
            && !(run.Controller.Phase == Flight14LaunchPhase.OrbitReady && controller.IsStopped); step++)
        {
            universe.Tick(0.02);
            Assert.Same(run.Ship, universe.ActiveVessel);
            Assert.False(run.Ship.IsDestroyed);
            Assert.True(part.LiquidFuel+part.Oxidizer <= previousFuel+1e-6);
            previousFuel = part.LiquidFuel+part.Oxidizer;
            if (controller.Phase is Flight14BoosterReturnPhase.Flip or Flight14BoosterReturnPhase.Boostback or Flight14BoosterReturnPhase.Coast)
            {
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
        // LOX exhaustion can leave unmatched fuel in the inherited tank mixture.
        Assert.InRange(cutoff.PropellantKg, reserveLF+reserveOx-1e-6, reserveLF+reserveOx+1000);
        var contact = Assert.Single(controller.Events.Where(e => e.Phase == Flight14BoosterReturnPhase.WaterEntry));
        Assert.InRange(contact.AirspeedMps, 0, 8);
        Assert.True(contact.VerticalSpeedMps < 0);
        Assert.InRange(contact.LatitudeDegrees, 23, 29); Assert.InRange(contact.LongitudeDegrees, -97.25, -90);
        Assert.True(controller.Booster.WaterMotionEnabled);
        Assert.NotNull(controller.Booster.LastWaterMotionTelemetry);
        Assert.False(universe.Coupled6DofIntegrationEnabled);
        Assert.True(controller.FlightTerminationTriggered);
        Assert.Equal(VesselDestructionCause.FlightTermination, controller.Booster.DestructionCause);
        Assert.Equal(0, controller.Booster.Throttle);
        Assert.False(new Flight14Exploration(run).ObserveBooster(true));
        Assert.Single(part.EngineStates.Where(e => e.FailureCode == "F14_ESTIMATED_ASCENT_OUT"));
        output.WriteLine($"Booster: boostback cutoff T+{cutoff.MissionElapsedSeconds:F2}; water T+{contact.MissionElapsedSeconds:F2}; "
            + $"speed={contact.AirspeedMps:F3}m/s latitude={contact.LatitudeDegrees:F6} longitude={contact.LongitudeDegrees:F6}; "
            + $"ship orbit T+{run.Controller.OrbitElapsedSeconds:F2}");
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
