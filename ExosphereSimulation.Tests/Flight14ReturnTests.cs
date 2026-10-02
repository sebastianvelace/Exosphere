namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using System.Text.Json;
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
        var returnDefinition = Flight14ReturnDefinition.LoadFromJson(Path.Combine(DataDirectory(),
            "flight_profiles/starship_flight14_return_estimate.json"));
        var observations = EntryDisplayObservations();
        var samples = new List<(double Time, double Altitude)>();
        int maximumDeorbitEngines = 0;
        double maximumReturnAngularRate = 0;
        for (int i = 0; i < 11020 * fps && returning.Phase is not
            (Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked); i++)
        {
            run.AdvanceFrame(1.0 / fps);
            double elapsed = run.Universe.CurrentTime - run.Controller.LiftoffEpoch;
            if (samples.Count < observations.Length && elapsed >= observations[samples.Count].Time)
                samples.Add((elapsed, run.Ship.GetAltitude(run.Earth)));
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
        Assert.InRange(returning.PostDeorbitPeriapsisAltitudeM,
            returnDefinition.TargetPeriapsisAltitudeM - 10000, returnDefinition.TargetPeriapsisAltitudeM);
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
        // Conditional display calibration: the broadcast does not specify its datum.
        // These are read-only comparison samples, never flight-controller targets.
        Assert.Equal(observations.Length, samples.Count);
        for (int i = 0; i < observations.Length; i++)
        {
            Assert.InRange(samples[i].Time - observations[i].Time, 0, 1.0 / fps + 0.02);
            Assert.InRange(samples[i].Altitude - observations[i].Altitude, -500, 500);
        }
        // Rounded 94.6 -> 93.9 km over 10 seconds suggests ~70 m/s down.
        // Allow rounding, clock quantization and residual model uncertainty.
        double intervalDescent = (samples[1].Altitude - samples[0].Altitude)
            / (samples[1].Time - samples[0].Time);
        Assert.InRange(intervalDescent, -110, -40);
        output.WriteLine($"{fps} FPS: deorbit={returning.DeorbitCommandElapsedSeconds:F2}..{returning.DeorbitShutdownElapsedSeconds:F2}s "
            + $"entry120={returning.EntryInterface.MissionElapsedSeconds:F2}s entry90={returning.DiagnosticEnd.MissionElapsedSeconds:F2}s "
            + $"reserve={returning.PreDeorbitPropellantKg:F2}->{returning.PostDeorbitPropellantKg:F2}kg");
        output.WriteLine($"Conditional geodetic display residuals: {samples[0].Altitude-observations[0].Altitude:F2}, "
            + $"{samples[1].Altitude-observations[1].Altitude:F2}m; interval vertical speed={intervalDescent:F2}m/s");
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
    public void InvalidReturnEstimateCannotExceedItsRadialCommandEnvelope()
    {
        var definition = Flight14ReturnDefinition.LoadFromJson(Path.Combine(DataDirectory(),
            "flight_profiles/starship_flight14_return_estimate.json"));
        definition.TargetPeriapsisAltitudeM = definition.MaximumTargetPeriapsisAltitudeM + 1;
        Assert.Throws<InvalidDataException>(definition.Validate);
    }

    [Fact]
    public void RadialPeriapsisTargetMayExceedTheGeodeticDiagnosticEndpoint()
    {
        var definition = Flight14ReturnDefinition.LoadFromJson(Path.Combine(DataDirectory(),
            "flight_profiles/starship_flight14_return_estimate.json"));
        definition.TargetPeriapsisAltitudeM = 97500;
        Assert.True(definition.TargetPeriapsisAltitudeM > definition.DiagnosticEndAltitudeM);
        definition.Validate();
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

    private static (double Time, double Altitude)[] EntryDisplayObservations()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory(), "..",
            "docs/research/STARSHIP_FLIGHT14_OBSERVED_ANCHORS_2026-10-01.json")));
        var observations = document.RootElement.GetProperty("anchors").EnumerateArray()
            .Where(anchor => anchor.GetProperty("view").GetString() is "luminous_entry" or "luminous_entry_followup")
            .Select(anchor => (Time: anchor.GetProperty("displayed_elapsed_seconds").GetDouble(),
                Altitude: anchor.GetProperty("displayed_altitude_km").GetDouble() * 1000)).ToArray();
        Assert.Equal(2, observations.Length);
        return observations;
    }
}
