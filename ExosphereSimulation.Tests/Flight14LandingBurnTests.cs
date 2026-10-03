namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14LandingBurnTests(ITestOutputHelper output)
{
    [Fact]
    public void FiniteEngineSpoolAndGimbalPerformTheFlipAndTerminalBurn()
    {
        var (universe, ship, body, definition) = Fixture();
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        universe.PhysicsStepController = new Controller(burn);
        double finalStepBudgetKg = 0;
        for (int i = 0; i < 6000 && burn.Phase is not
            (Flight14LandingPhase.TerminalReached or Flight14LandingPhase.Blocked); i++)
        {
            finalStepBudgetKg = ship.Parts.GetCurrentMassFlow(ship.GetAmbientPressure(body))*0.02;
            universe.Tick(0.02);
        }
        output.WriteLine($"{burn.Phase} {burn.BlockReason}: alt={ship.GetAltitude(body):F2} "
            + $"air={ship.GetSurfaceVelocity(body).Magnitude:F2}m/s T={universe.CurrentTime:F2}s");
        Assert.True(burn.Phase == Flight14LandingPhase.TerminalReached, burn.BlockReason);
        Assert.True(burn.HasDeliveredThreeSeaLevelEngines);
        var enginePart = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        Assert.All(enginePart.EngineStates.Where(e => e.EngineModelId == "raptor3-starship-sl-flight12"),
            e => Assert.InRange(e.StartAttempts, 1, 4));
        Assert.NotNull(burn.EndWitness);
        Assert.InRange(burn.EndWitness.GeodeticAltitudeM, 99, 100);
        Assert.InRange(burn.EndWitness.AtmosphereRelativeSpeedMps, 0, definition.MaximumEndAirspeedMps);
        Assert.True(burn.EndWitness.VerticalSpeedMps < 0);
        Assert.True(burn.EndWitness.PropellantKg < burn.StartWitness!.PropellantKg);
        double remaining = ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer);
        Assert.InRange(remaining, burn.EndWitness.PropellantKg-finalStepBudgetKg-1e-6,
            burn.EndWitness.PropellantKg);
        Assert.False(ship.IsDestroyed);
        Assert.Equal(1, burn.MinimumSelectedEngineCount);
        Assert.InRange(burn.MaximumAngularRateRadPerSecond, 0.05, 0.350000001);
    }

    [Fact]
    public void FlipContinuesToRealNozzleContactAndIntegratesWaterWithoutGroundClamp()
    {
        var (universe, ship, body, definition) = Fixture();
        definition.ContinueToWaterContact = true;
        // This bench is open water near the date line, separate from the loaded return envelope.
        definition.MinimumWaterLatitudeDegrees = 22;
        definition.MaximumWaterLatitudeDegrees = 24;
        definition.MinimumWaterLongitudeDegrees = -179;
        definition.MaximumWaterLongitudeDegrees = -177;
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        universe.PhysicsStepController = new Controller(burn);
        for (int i = 0; i < 6000 && burn.Phase is not
            (Flight14LandingPhase.SplashdownReached or Flight14LandingPhase.Blocked); i++) universe.Tick(0.02);
        output.WriteLine($"Water: {burn.Phase} {burn.BlockReason} alt={ship.GetAltitude(body):F3} "
            + $"entry={burn.WaterEntryWitness?.AtmosphereRelativeSpeedMps:F3}m/s "
            + $"v={ship.GetSurfaceVelocity(body).Magnitude:F3} up={ship.Orientation.Rotate(Vector3d.Up).Dot(body.GetGeodeticUp(ship.Position)):F4} "
            + $"water={ship.LastWaterContact}");
        Assert.True(burn.Phase == Flight14LandingPhase.SplashdownReached, burn.BlockReason);
        Assert.NotNull(burn.EndWitness); // The 100 m diagnostic remains evidence, not a cutoff.
        Assert.NotNull(burn.WaterEntryWitness);
        Assert.InRange(burn.WaterEntryWitness.GeodeticAltitudeM, 3.6, 3.8);
        Assert.InRange(burn.WaterEntryWitness.AtmosphereRelativeSpeedMps, 0, definition.MaximumWaterEntrySpeedMps);
        Assert.True(burn.WaterEntryWitness.VerticalSpeedMps < 0);
        Assert.True(universe.CurrentTime-burn.WaterEntryWitness.SimulationTimeSeconds >= definition.WaterObservationSeconds);
        Assert.NotNull(ship.LastWaterContact);
        Assert.True(ship.LastWaterContact.Value.LowestPointAltitudeM < 0
            || ship.LastWaterContact.Value.HullLowestAltitudeM < 0);
        Assert.True(ship.LastWaterContact.Value.SubmergedVolumeM3 > 0);
        Assert.True(ship.LastWaterContact.Value.ForceWorld.Magnitude > 0);
        Assert.False(ship.IsGroundHeld); Assert.False(ship.IsDestroyed);
        Assert.Equal(0, ship.Throttle);
        Assert.True(ship.WaterMotionEnabled);
        Assert.True(ship.LastWaterMotionTelemetry?.IsFinite);
        Assert.NotNull(burn.WaterMotionEndWitness);
        Assert.InRange(burn.WaterMotionEndWitness.SimulationTimeSeconds-burn.WaterEntryWitness.SimulationTimeSeconds,
            definition.WaterObservationSeconds, definition.WaterObservationSeconds+0.04);
    }

    [Fact]
    public void ContactOutsideDeclaredOceanCannotBeReportedAsSplashdown()
    {
        var (universe, ship, body, definition) = Fixture();
        definition.ContinueToWaterContact = true; // The default region excludes the seeded bench.
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        universe.PhysicsStepController = new Controller(burn);
        for (int i = 0; i < 6000 && burn.Phase != Flight14LandingPhase.Blocked; i++) universe.Tick(0.02);
        Assert.Equal(Flight14LandingPhase.Blocked, burn.Phase);
        Assert.Equal("water-entry-outside-declared-ocean-region", burn.BlockReason);
        Assert.Null(burn.WaterEntryWitness);
        Assert.Equal(0, ship.Throttle);
        Assert.False(ship.IsGroundHeld);
    }

    [Fact]
    public void FailedCentreEngineCannotBeReplacedByAVacuumRaptor()
    {
        var (universe, ship, body, definition) = Fixture();
        var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        engines.FailEngine(engines.EngineStates[0].InstanceId, "TEST_SL_OUT");
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        var position = ship.Position; var velocity = ship.Velocity; var pose = ship.Orientation;
        burn.Advance(universe, 0.02);
        Assert.Equal(Flight14LandingPhase.Blocked, burn.Phase);
        Assert.Equal("three-healthy-sea-level-engines-required", burn.BlockReason);
        Assert.Equal(0, ship.Throttle);
        Assert.Equal(position, ship.Position); Assert.Equal(velocity, ship.Velocity); Assert.Equal(pose, ship.Orientation);
    }

    [Fact]
    public void ExhaustedStartBudgetBlocksBeforeIgnition()
    {
        var (universe, ship, body, definition) = Fixture();
        var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        engines.EngineStates[0].StartAttempts = engines.Definition.ResolvedEngineModels[
            "raptor3-starship-sl-flight12"].RestartLimit + 1;
        var burn = new Flight14LandingBurn(ship, body, definition, "raptor3-starship-sl-flight12", 0);
        burn.Advance(universe, 0.02);
        Assert.Equal(Flight14LandingPhase.Blocked, burn.Phase);
        Assert.Equal("landing-restart-budget-exhausted", burn.BlockReason);
        Assert.Null(burn.StartWitness);
        Assert.Equal(0, ship.Throttle);
    }

    [Fact]
    public void LoadedPadToTerminalBurnRetainsItsPhysicalShipAndOriginalFuel()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        var run = Flight14LaunchDiagnostic.CreateWithPoweredReturn(Path.Combine(directory!.FullName, "data"));
        var tank = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        var controller = run.PoweredReturnController!;
        bool controlChangedKinematicsOrFuel = false;
        var guard = new Guard(run, () => controlChangedKinematicsOrFuel = true);
        run.Universe.PhysicsStepController = guard;
        for (int i = 0; i < 13020*60 && controller.BlockReason == null
            && controller.Landing?.Phase != Flight14LandingPhase.TerminalReached; i++) run.AdvanceFrame(1.0/60);
        Assert.True(controller.Landing?.Phase == Flight14LandingPhase.TerminalReached, controller.BlockReason);
        var burn = controller.Landing!;
        Assert.False(controlChangedKinematicsOrFuel);
        Assert.Same(tank, run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank")));
        Assert.Equal(26, run.PayloadController.Releases.Count);
        Assert.True(burn.HasDeliveredThreeSeaLevelEngines);
        Assert.NotNull(burn.EndWitness);
        Assert.Equal(run.ReturnController!.PostDeorbitPropellantKg, burn.StartWitness!.PropellantKg, 6);
        Assert.True(burn.EndWitness.PropellantKg < burn.StartWitness.PropellantKg);
        // The witness precedes the physical step that delivers finite shutdown thrust.
        // The tank may lose that step's propellant, but cannot refill or jump in mass.
        Assert.InRange(tank.LiquidFuel+tank.Oxidizer,
            burn.EndWitness.PropellantKg-guard.TerminalStepPropellantBudgetKg-1e-6,
            burn.EndWitness.PropellantKg);
        Assert.Equal(0, run.Ship.Throttle);
        Assert.True(burn.EndWitness.VerticalSpeedMps < 0);
        Assert.InRange(burn.EndWitness.AtmosphereRelativeSpeedMps, 0, 15);
        Assert.InRange(burn.EndWitness.GeodeticAltitudeM, 99, 100);
        Assert.False(run.Ship.IsDestroyed);
        Assert.False(run.Ship.StructuralControlLost);
        Assert.InRange(burn.MaximumAngularRateRadPerSecond, 0.05, 0.350000001);
        output.WriteLine($"Loaded terminal return: T+{burn.EndWitness.MissionElapsedSeconds:F2}s "
            + $"airspeed={burn.EndWitness.AtmosphereRelativeSpeedMps:F2}m/s "
            + $"reserve={burn.StartWitness.PropellantKg:F2}->{burn.EndWitness.PropellantKg:F2}kg");
    }

    private sealed class Guard(Flight14LaunchDiagnostic run, Action changed) : IPhysicsStepController
    {
        public double TerminalStepPropellantBudgetKg { get; private set; }
        public bool RequiresFixedCadence(Universe universe) => run.PoweredReturnController!.RequiresFixedCadence(universe);
        public void BeforePhysicsStep(Universe universe, double interval)
        {
            bool monitor = run.DescentController!.Phase != Flight14DescentPhase.WaitingForEntry;
            var position = run.Ship.Position; var velocity = run.Ship.Velocity; var pose = run.Ship.Orientation;
            var omega = run.Ship.AngularVelocity;
            double fuel = run.Ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer);
            run.PoweredReturnController!.BeforePhysicsStep(universe, interval);
            if (run.PoweredReturnController.Landing?.Phase == Flight14LandingPhase.TerminalReached)
                TerminalStepPropellantBudgetKg += run.Ship.Parts.GetCurrentMassFlow(
                    run.Ship.GetAmbientPressure(run.Earth))*interval;
            if (monitor && (position != run.Ship.Position || velocity != run.Ship.Velocity
                || !pose.Equals(run.Ship.Orientation) || omega != run.Ship.AngularVelocity
                || fuel != run.Ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer))) changed();
        }
    }

    private sealed class Controller(Flight14LandingBurn burn) : IPhysicsStepController
    {
        public bool RequiresFixedCadence(Universe universe) => true;
        public void BeforePhysicsStep(Universe universe, double interval) => burn.Advance(universe, interval);
    }
    private static (Universe, Vessel, CelestialBody, Flight14LandingDefinition) Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        string data = Path.Combine(directory!.FullName, "data");
        var body = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        body.Position = Vector3d.Zero; body.Velocity = Vector3d.Zero; body.OrbitalElements = null;
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles/starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Terminal burn control fixture");
        Assert.NotNull(ship.Stage());
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        // Seeded control bench only: full mission acceptance requires the loaded-pad run.
        tank.LiquidFuel = 15000; tank.Oxidizer = 55000;
        ship.Position = body.GetSurfacePositionAtTime(23, -178, 0, 1200);
        var up = body.GetGeodeticUp(ship.Position); var flow = -up;
        ship.Velocity = body.GetSurfaceVelocity(ship.Position) + flow*70;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, Vector3d.Right, 90), flow);
        ship.ReferenceBodyId = body.Id; ship.SASEnabled = false;
        var universe = new Universe(); universe.AddBody(body); universe.AddVessel(ship); universe.ActiveVessel = ship;
        var definition = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles/starship_flight14_landing_estimate.json"));
        definition.ContinueToWaterContact = false;
        return (universe, ship, body, definition);
    }
}
