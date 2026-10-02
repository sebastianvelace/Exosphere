namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14LaunchControllerTests(ITestOutputHelper output)
{
    [Fact]
    public void PadToInsertionUsesContinuousResourcesAndDeliveredSeaLevelThrust()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var (universe, earth, ship, controller, guidance, _, _) = run;
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        var booster = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        var phases = new HashSet<Flight14LaunchPhase>();
        double initialMass = ship.TotalMass, lastFuel = tank.LiquidFuel+tank.Oxidizer;
        double settledCoastFuel = double.NaN, maximumRate = 0;
        int insertionRunning = 0, maximumBoosterRunning = 0;
        for (int i = 0; i < 100000 && controller.Phase is not (Flight14LaunchPhase.OrbitReady or Flight14LaunchPhase.Blocked); i++)
        {
            universe.Tick(0.02);
            phases.Add(controller.Phase);
            Assert.False(ship.IsDestroyed);
            Assert.False(ship.StructuralControlLost);
            Assert.True(double.IsFinite(ship.Position.Magnitude) && double.IsFinite(ship.Velocity.Magnitude));
            double fuel = tank.LiquidFuel+tank.Oxidizer;
            Assert.True(fuel <= lastFuel+1e-6);
            lastFuel = fuel;
            maximumRate = System.Math.Max(maximumRate, ship.AngularVelocity.Magnitude);
            if (controller.Phase == Flight14LaunchPhase.BoosterAscent)
                maximumBoosterRunning = System.Math.Max(maximumBoosterRunning,
                    booster.GetEngineTelemetry(0).Count(e => e.ThrustN > 1));
            if (controller.Phase == Flight14LaunchPhase.SuborbitalCoast
                && universe.CurrentTime-controller.LiftoffEpoch-controller.CutoffElapsedSeconds > 2)
            {
                Assert.True(ship.GetAltitude(earth) > earth.Atmosphere!.MaxAltitude);
                Assert.All(engines.GetEngineTelemetry(0), e => Assert.Equal(0, e.ThrustN));
                if (double.IsNaN(settledCoastFuel)) settledCoastFuel = fuel;
                else Assert.InRange(System.Math.Abs(fuel-settledCoastFuel), 0, 1e-6);
            }
            if (controller.Phase == Flight14LaunchPhase.Insertion)
            {
                var running = engines.GetEngineTelemetry(0).Where(e => e.ThrustN > 1).ToArray();
                insertionRunning = System.Math.Max(insertionRunning, running.Length);
                Assert.All(running, e => Assert.Equal(guidance.SeaLevelEngineModelId,
                    engines.EngineStates.Single(s => s.InstanceId == e.InstanceId).EngineModelId));
            }
        }
        Assert.Equal(Flight14LaunchPhase.OrbitReady, controller.Phase);
        Assert.Equal(33, maximumBoosterRunning);
        Assert.Equal(1, insertionRunning);
        Assert.Contains(Flight14LaunchPhase.HotStage, phases);
        Assert.Contains(Flight14LaunchPhase.ShipAscent, phases);
        Assert.Contains(Flight14LaunchPhase.SuborbitalCoast, phases);
        Assert.Contains(Flight14LaunchPhase.Insertion, phases);
        Assert.NotNull(controller.DetachedBooster);
        Assert.Contains(controller.DetachedBooster!, universe.Vessels);
        Assert.Same(ship, universe.ActiveVessel);
        Assert.True(controller.CutoffPeriapsisAltitudeM < earth.Atmosphere!.MaxAltitude);
        Assert.True(controller.InsertionElapsedSeconds-controller.CutoffElapsedSeconds > guidance.MinimumCoastSeconds);
        Assert.True(controller.CoastMinimumAltitudeM > earth.Atmosphere.MaxAltitude);
        Assert.True(lastFuel > guidance.MinimumShipReserveKg);
        Assert.True(initialMass > ship.TotalMass);
        Assert.True(double.IsFinite(settledCoastFuel));
        Assert.InRange(maximumRate, 0, 0.05);
        Assert.Single(engines.EngineStates.Where(s => s.FailureCode == "F14_ESTIMATED_VAC_OUT"));
        Assert.Single(booster.EngineStates.Where(s => s.FailureCode == "F14_ESTIMATED_ASCENT_OUT"));
        var orbit = OrbitalElements.FromStateVector(ship.Position-earth.Position,
            ship.Velocity-earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        Assert.True(orbit.Periapsis-earth.Radius >= guidance.TargetInsertionPeriapsisAltitudeM);
        output.WriteLine($"Continuous launch: stage={controller.StagingElapsedSeconds:F2}s "
            + $"cutoff={controller.CutoffElapsedSeconds:F2}s "
            + $"coastMinimum={controller.CoastMinimumAltitudeM:F1}m "
            + $"insertion={controller.InsertionElapsedSeconds:F2}s orbit={controller.OrbitElapsedSeconds:F2}s "
            + $"remainingPropellant={lastFuel:F1}kg maxRate={maximumRate:F4}rad/s");
    }

    [Fact]
    public void DiagnosticFixedStepDriverDoesNotChooseTheMissionSequenceFromRenderCadence()
    {
        var thirty = RunAtCadence(30);
        var hundredTwenty = RunAtCadence(120);
        Assert.Equal(Flight14LaunchPhase.OrbitReady, thirty.Controller.Phase);
        Assert.Equal(Flight14LaunchPhase.OrbitReady, hundredTwenty.Controller.Phase);
        Assert.InRange(System.Math.Abs(thirty.Controller.CutoffElapsedSeconds-hundredTwenty.Controller.CutoffElapsedSeconds), 0, 1e-8);
        Assert.InRange(System.Math.Abs(thirty.Controller.InsertionElapsedSeconds-hundredTwenty.Controller.InsertionElapsedSeconds), 0, 1e-8);
        Assert.InRange(System.Math.Abs(thirty.Ship.TotalMass-hundredTwenty.Ship.TotalMass), 0, 1e-6);
    }

    [Fact]
    public void LostVehicleOwnershipStopsCommandsWithoutReseedingState()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var other = new Vessel { Name = "Pilot-selected vessel" };
        run.Universe.AddVessel(other);
        run.Universe.SetActiveVessel(other.Id);
        var position = run.Ship.Position;
        var velocity = run.Ship.Velocity;
        var orientation = run.Ship.Orientation;
        run.Controller.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(Flight14LaunchPhase.Blocked, run.Controller.Phase);
        Assert.Equal("active-vessel-changed", run.Controller.BlockReason);
        Assert.Equal(position, run.Ship.Position);
        Assert.Equal(velocity, run.Ship.Velocity);
        Assert.Equal(orientation, run.Ship.Orientation);
        Assert.Equal(0, run.Ship.Throttle);
        Assert.False(run.Controller.RequiresFixedCadence(run.Universe));
    }

    [Fact]
    public void DiagnosticDriverRetainsFrameRemaindersAndRejectsAmbiguousWarp()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        for (int i = 0; i < 60; i++) run.AdvanceFrame(1.0/60);
        Assert.Equal(1, run.Universe.CurrentTime, 10);
        foreach (double delta in new[] { 0.0, -1, double.NaN, double.PositiveInfinity, 2 })
            Assert.Throws<ArgumentOutOfRangeException>(() => run.AdvanceFrame(delta));
        run.Universe.TimeScale = 10;
        Assert.Throws<InvalidOperationException>(() => run.AdvanceFrame(0.02));
        Assert.Equal(1, run.Universe.CurrentTime, 10);
    }

    [Fact]
    public void EmptyBoosterCannotHangTheIgnitionSequence()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var booster = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        booster.LiquidFuel = 0;
        booster.Oxidizer = 0;
        for (int i = 0; i < 1000 && run.Controller.Phase != Flight14LaunchPhase.Blocked; i++)
            run.Universe.Tick(0.02);
        Assert.Equal(Flight14LaunchPhase.Blocked, run.Controller.Phase);
        Assert.Equal("ignition-envelope", run.Controller.BlockReason);
        Assert.True(run.Ship.IsGroundHeld);
        Assert.Equal(0, run.Ship.Throttle);
    }

    private static Flight14LaunchDiagnostic RunAtCadence(int fps)
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        for (int i = 0; i < 2000*fps && run.Controller.Phase is not (Flight14LaunchPhase.OrbitReady or Flight14LaunchPhase.Blocked); i++)
            run.AdvanceFrame(1.0/fps);
        // Compare physical state at the same committed epoch, not two render boundaries.
        if (run.Controller.Phase == Flight14LaunchPhase.OrbitReady)
        {
            double epoch = run.Controller.LiftoffEpoch+run.Controller.OrbitElapsedSeconds+1;
            while (run.Universe.CurrentTime < epoch-1e-9) run.Universe.Tick(0.02);
        }
        return run;
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found."), "data");
    }
}
