namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14ReturnTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    public void LoadedPadToEntryConsumesTheSameReserveWithoutReseeding(int fps)
    {
        var run = Flight14LaunchDiagnostic.CreateWithReturn(DataDirectory());
        var returning = run.ReturnController!;
        var guard = new KinematicGuard(returning, run.Ship);
        run.Universe.PhysicsStepController = guard;
        var engines = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        var tank = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        var originalTank = tank;
        int maximumDeorbitEngines = 0;
        double maximumReturnAngularRate = 0;
        for (int i = 0; i < 11020 * fps && returning.Phase is not
            (Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked); i++)
        {
            run.AdvanceFrame(1.0 / fps);
            if (returning.Phase != Flight14ReturnPhase.WaitingForPayload)
                maximumReturnAngularRate = System.Math.Max(maximumReturnAngularRate, run.Ship.AngularVelocity.Magnitude);
            if (returning.Phase == Flight14ReturnPhase.DeorbitBurn)
            {
                var delivered = engines.GetEngineTelemetry(0).Where(e => e.ThrustN > 1).ToArray();
                maximumDeorbitEngines = System.Math.Max(maximumDeorbitEngines, delivered.Length);
                Assert.All(delivered, e => Assert.Equal(run.Guidance.SeaLevelEngineModelId,
                    engines.EngineStates.Single(s => s.InstanceId == e.InstanceId).EngineModelId));
            }
        }
        Assert.True(returning.Phase == Flight14ReturnPhase.EntryReached, returning.BlockReason);
        Assert.False(guard.KinematicsChangedByReturnControl);
        Assert.InRange(maximumReturnAngularRate, 0, 0.15);
        Assert.Same(originalTank, run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank")));
        Assert.Equal(26, run.PayloadController.Releases.Count);
        Assert.Equal(26, run.PayloadController.Releases.Select(r => r.Satellite.Id).Distinct().Count());
        Assert.DoesNotContain(run.Ship.Parts.Parts, p => p.Definition.HasVehicleRole("payload"));
        Assert.Equal(returning.PostDeploymentPropellantKg, returning.PreDeorbitPropellantKg, 6);
        Assert.True(returning.PostDeorbitPropellantKg < returning.PreDeorbitPropellantKg);
        Assert.True(returning.PostDeorbitEnergyJPerKg < returning.PreDeorbitEnergyJPerKg);
        Assert.InRange(returning.PostDeorbitPeriapsisAltitudeM, 50000, 60000);
        Assert.True(returning.HasDeliveredDeorbitThrust);
        Assert.Equal(1, maximumDeorbitEngines);
        Assert.Equal(0, run.Ship.Throttle);
        Assert.False(run.Ship.IsDestroyed);
        Assert.False(run.Ship.StructuralControlLost);
        Assert.NotNull(returning.EntryInterface);
        Assert.NotNull(returning.DiagnosticEnd);
        Assert.InRange(returning.EntryInterface.GeodeticAltitudeM, 119990, 120000);
        Assert.InRange(returning.DiagnosticEnd.GeodeticAltitudeM, 89990, 90000);
        Assert.True(returning.EntryInterface.MissionElapsedSeconds < returning.DiagnosticEnd.MissionElapsedSeconds);
        Assert.True(returning.DiagnosticEnd.VerticalSpeedMps < -20);
        Assert.True(returning.EntryInterface.WindwardShieldDotVelocity > 0.85);
        Assert.True(returning.DiagnosticEnd.WindwardShieldDotVelocity > 0.85);
        Assert.InRange(returning.DiagnosticEnd.InertialSpeedMps, 7000, 8500);
        Assert.Equal(returning.DiagnosticEnd.RemainingPropellantKg, tank.LiquidFuel + tank.Oxidizer, 6);
        // The state remains free fall: orbit-relative speed is distinct from rotating-air speed.
        var relativeVelocity = run.Ship.Velocity - run.Earth.Velocity;
        Assert.Equal(relativeVelocity - run.Earth.GetSurfaceVelocity(run.Ship.Position),
            run.Ship.GetSurfaceVelocity(run.Earth));
        output.WriteLine($"{fps} FPS: deorbit={returning.DeorbitCommandElapsedSeconds:F2}..{returning.DeorbitShutdownElapsedSeconds:F2}s "
            + $"entry120={returning.EntryInterface.MissionElapsedSeconds:F2}s entry90={returning.DiagnosticEnd.MissionElapsedSeconds:F2}s "
            + $"reserve={returning.PreDeorbitPropellantKg:F2}->{returning.PostDeorbitPropellantKg:F2}kg");
    }

    [Fact]
    public void OwnershipChangeBlocksTheComposedMissionAndCutsThrottle()
    {
        var run = Flight14LaunchDiagnostic.CreateWithReturn(DataDirectory());
        run.Ship.Throttle = 1;
        run.Universe.ActiveVessel = null;
        run.ReturnController!.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(Flight14ReturnPhase.Blocked, run.ReturnController.Phase);
        Assert.Equal("active-vessel-changed", run.ReturnController.BlockReason);
        Assert.Equal(0, run.Ship.Throttle);
    }

    [Fact]
    public void InvalidReturnEstimateCannotPlacePeriapsisAboveItsEntryGate()
    {
        var definition = Flight14ReturnDefinition.LoadFromJson(Path.Combine(DataDirectory(),
            "flight_profiles/starship_flight14_return_estimate.json"));
        definition.TargetPeriapsisAltitudeM = definition.EntryInterfaceAltitudeM;
        Assert.Throws<InvalidDataException>(definition.Validate);
    }

    private sealed class KinematicGuard(Flight14ReturnController controller, Vessel ship) : IPhysicsStepController
    {
        public bool KinematicsChangedByReturnControl { get; private set; }
        public bool RequiresFixedCadence(Universe universe) => controller.RequiresFixedCadence(universe);
        public void BeforePhysicsStep(Universe universe, double interval)
        {
            var phase = controller.Phase;
            var position = ship.Position;
            var velocity = ship.Velocity;
            var orientation = ship.Orientation;
            var angularVelocity = ship.AngularVelocity;
            controller.BeforePhysicsStep(universe, interval);
            // Launch staging and payload splits legitimately move the aggregate datum.
            if (phase != Flight14ReturnPhase.WaitingForPayload)
                KinematicsChangedByReturnControl |= position != ship.Position || velocity != ship.Velocity
                    || !orientation.Equals(ship.Orientation) || angularVelocity != ship.AngularVelocity;
        }
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found."), "data");
    }
}
