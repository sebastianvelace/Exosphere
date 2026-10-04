using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;

/// <summary>Continuous moving-Earth, original-stage evidence. No prepared entry state.</summary>
internal static class Flight14BoosterProbe
{
    public static int Execute(string dataDirectory, string outputDirectory)
    {
        string data = Path.GetFullPath(dataDirectory), output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var run = Flight14LaunchDiagnostic.CreateWithBoosterReturn(data, Universe.LoadFromDataDirectory(data));
        var controller = run.BoosterReturnController!;
        using var trace = new StreamWriter(Path.Combine(output, "booster-telemetry.jsonl"));
        double nextSample = 0, maximumAltitude = 0;
        string previousPhase = "";
        while (!controller.IsStopped && run.Universe.CurrentTime < 2000 && !run.Ship.IsDestroyed)
        {
            run.Universe.Tick(Universe.DeterministicControlPeriodSeconds);
            double elapsed = run.Universe.CurrentTime-run.Controller.LiftoffEpoch;
            if (controller.FlightTerminationTriggered)
            {
                // The wreck no longer advances with the moving world's next epoch.
                // Its immutable pre-retirement witness is the terminal state, not a
                // newly evaluated altitude relative to the later Earth position.
                var terminal = controller.Events[^1];
                Console.WriteLine($"T+{terminal.MissionElapsedSeconds:F2} {terminal.Phase} alt={terminal.AltitudeM:F1} m speed={terminal.AirspeedMps:F2} m/s (terminal witness)");
                break;
            }
            var booster = controller.Booster;
            var vessel = booster ?? run.Ship;
            var engine = vessel.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
            bool changed = previousPhase != controller.Phase.ToString();
            if (!double.IsFinite(elapsed) || elapsed < 0 || !changed && elapsed < nextSample) continue;
            var velocity = vessel.GetSurfaceVelocity(run.Earth);
            run.Earth.GetGeodeticCoordinatesAtTime(vessel.Position, run.Universe.CurrentTime,
                out double latitude, out double longitude, out _);
            double altitude = vessel.GetAltitude(run.Earth);
            if (booster != null) maximumAltitude = System.Math.Max(maximumAltitude, altitude);
            trace.WriteLine(JsonSerializer.Serialize(new
            {
                missionElapsedSeconds = elapsed, phase = controller.Phase.ToString(), originalBoosterPartId = engine.InstanceId,
                vesselId = vessel.Id, geodeticAltitudeM = altitude,
                radialAltitudeM = (vessel.Position-run.Earth.Position).Magnitude-run.Earth.Radius,
                atmosphereRelativeSpeedMps = velocity.Magnitude,
                verticalSpeedMps = velocity.Dot(run.Earth.GetGeodeticUp(vessel.Position)),
                bodyFixedLatitudeDegrees = latitude, bodyFixedLongitudeDegrees = longitude,
                propellantKg = engine.LiquidFuel+engine.Oxidizer,
                protectedLandingPropellantKg = engine.ReservedLiquidFuel+engine.ReservedOxidizer,
                availableFeedOxidizerKg = engine.AvailableOxidizer,
                selectedEngines = engine.SelectedEngineCount,
                runningEngines = engine.GetEngineTelemetry(vessel.GetAmbientPressure(run.Earth)).Count(e => e.State == Exosphere.Simulation.Propulsion.EngineLifecycleState.Running && e.ThrustN > 1),
                angularRateRadPerSecond = vessel.AngularVelocity.Magnitude,
                groundHeld = vessel.IsGroundHeld, flightTerminationTriggered = controller.FlightTerminationTriggered,
            }));
            if (changed) Console.WriteLine($"T+{elapsed:F2} {controller.Phase} alt={altitude:F1} m speed={velocity.Magnitude:F2} m/s");
            previousPhase = controller.Phase.ToString(); nextSample = System.Math.Floor(elapsed)+1;
        }
        var summary = new
        {
            status = "engineering-estimate", controller.Phase, controller.BlockReason,
            controller.HasDeliveredBoostbackEngines, controller.HasDeliveredLandingIgnition,
            controller.FlightTerminationTriggered, maximumAltitudeM = maximumAltitude,
            shipPhase = run.Controller.Phase, shipDestroyed = run.Ship.IsDestroyed,
            activeVesselRemainsShip = ReferenceEquals(run.Universe.ActiveVessel, run.Ship),
            events = controller.Events,
            comparison = "Published timeline is approximate planned timing. Partial manual booster altitude/clock anchors are recorded in docs/research/STARSHIP_FLIGHT14_BOOSTER_BROWSER_ANCHORS_2026-10-03.json; model agreement is not established.",
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })+"\n");
        Console.WriteLine($"{controller.Phase}: {controller.BlockReason ?? "continuous contact observed; FTS abstracted"}");
        return controller.Phase == Flight14BoosterReturnPhase.ContactObserved && !run.Ship.IsDestroyed ? 0 : 2;
    }
}
