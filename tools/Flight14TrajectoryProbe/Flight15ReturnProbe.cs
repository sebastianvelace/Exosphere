using Exosphere.Simulation;
using Exosphere.Simulation.Flight;

/// <summary>Continuous pad-to-entry trial of the Flight 15 return estimate.</summary>
internal static class Flight15ReturnProbe
{
    public static int Execute(string dataDirectory, string outputDirectory)
    {
        var definition = Flight15Mission.LoadReturnEstimate(dataDirectory);
        var run = Flight15Mission.CreateAscent(dataDirectory);
        var exploration = new Flight14Exploration(run, Flight15Mission.AscentProfileId);
        var site = LaunchSite.LoadFromJson(Path.Combine(dataDirectory, "launch_sites",
            Flight15Mission.LaunchSiteId + ".json"));
        var (universe, earth, ship, launch, guidance, payload, deployment) = run;
        var tank = ship.Parts.Parts.Single(part => part.Definition.HasVehicleRole("tank"));
        var engines = ship.Parts.Parts.Single(part => part.Definition.HasVehicleRole("ship_engines"));

        Directory.CreateDirectory(outputDirectory);
        using var trace = new StreamWriter(Path.Combine(outputDirectory, "flight15-return.log")) { AutoFlush = true };
        void Record(string line)
        {
            trace.WriteLine(line);
            Console.WriteLine(line);
        }
        int Fail(string reason)
        {
            Record("BLOCKED " + reason);
            return 3;
        }
        double Met() => universe.CurrentTime - launch.LiftoffEpoch;
        double Propellant() => tank.LiquidFuel + tank.Oxidizer;
        double Periapsis() => ship.ComputeCenterOfMassOrbit(earth, universe.CurrentTime).Periapsis - earth.Radius;

        Record($"PROFILE {definition.Id} coastWarp=100 burnStepS={Universe.DeterministicControlPeriodSeconds}");
        universe.TimeScale = 100;
        while (!exploration.IsStopped && universe.CurrentTime <= payload.MaximumMissionSeconds + 5)
            exploration.AdvanceFrame(1.0);
        if (exploration.BlockReason is { } launchBlock)
            return Fail(launchBlock);
        if (!exploration.IsTerminal || deployment.Releases.Count != Flight15Mission.PayloadCount)
            return Fail("incomplete-deployment");

        double deploymentPropellant = Propellant();
        Record($"DEPLOY met={Met():F2} releases={deployment.Releases.Count} massKg={ship.TotalMass:F2} "
            + $"propellantKg={deploymentPropellant:F2} radialPeriapsisM={Periapsis():F2}");
        // The completed deployment controller requests no further fixed-cadence steps.
        // Let the ordinary warp scheduler advance the original fleet to alignment.
        double alignmentMet = definition.EarliestDeorbitMissionSeconds - definition.AlignmentLeadSeconds;
        if (Met() >= alignmentMet)
            return Fail("deployment-missed-alignment-window");
        while (Met() + 1e-7 < alignmentMet)
        {
            double coastBatchSeconds = System.Math.Min(60, alignmentMet - Met());
            universe.Tick(coastBatchSeconds / universe.TimeScale);
            if (ship.IsDestroyed || ship.StructuralControlLost)
                return Fail($"ship-damaged-during-coast met={Met():F2}");
        }
        Record($"COAST met={Met():F2} propellantKg={Propellant():F2} radialPeriapsisM={Periapsis():F2}");

        var returning = new Flight14ReturnController(ship, earth, launch, deployment, definition, guidance);
        universe.PhysicsStepController = returning;
        universe.TimeScale = 1;
        var previousPhase = returning.Phase;
        int maxBurnEngines = 0;
        double nextSample = Met();
        while (returning.Phase is not (Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked)
            && Met() <= definition.MaximumMissionSeconds + Universe.DeterministicControlPeriodSeconds)
        {
            universe.Tick(Universe.DeterministicControlPeriodSeconds);
            if (returning.Phase == Flight14ReturnPhase.DeorbitBurn)
                maxBurnEngines = System.Math.Max(maxBurnEngines,
                    engines.GetEngineTelemetry(0).Count(engine => engine.ThrustN > 1));
            if (returning.Phase == previousPhase && Met() < nextSample)
                continue;
            previousPhase = returning.Phase;
            nextSample = Met() + 60;
            Record($"STATE phase={returning.Phase} met={Met():F2} altitudeM={ship.GetAltitude(earth):F2} "
                + $"airSpeedMps={ship.GetSurfaceVelocity(earth).Magnitude:F2} radialPeriapsisM={Periapsis():F2}");
        }
        if (returning.Phase != Flight14ReturnPhase.EntryReached)
            return Fail(returning.BlockReason ?? "entry-endpoint-timeout");

        var result = returning.CaptureSummary();
        Record($"BURN commandMet={result.DeorbitCommandElapsedSeconds:F2} "
            + $"targetMet={result.DeorbitTargetElapsedSeconds:F2} shutdownMet={result.DeorbitShutdownElapsedSeconds:F2} "
            + $"delivered={result.HasDeliveredDeorbitThrust} engines={maxBurnEngines} "
            + $"propellantKg={result.PreDeorbitPropellantKg:F2}->{result.PostDeorbitPropellantKg:F2} "
            + $"energyJPerKg={result.PreDeorbitEnergyJPerKg:F2}->{result.PostDeorbitEnergyJPerKg:F2} "
            + $"radialPeriapsisM={result.PostDeorbitPeriapsisAltitudeM:F2}");
        Record(Witness("INTERFACE", result.EntryInterface, earth, site));
        Record(Witness("ENDPOINT", result.DiagnosticEnd, earth, site));
        return result.HasDeliveredDeorbitThrust && maxBurnEngines == 1 ? 0 : Fail("single-engine-thrust-missing");
    }

    private static string Witness(string label, Flight14EntryWitness witness, CelestialBody earth, LaunchSite site)
    {
        double time = witness.SimulationTimeSeconds;
        var position = earth.GetSurfacePositionAtTime(witness.BodyFixedLatitudeDegrees,
            witness.BodyFixedLongitudeDegrees, time, witness.GeodeticAltitudeM);
        var surface = (position - earth.Position).Normalized;
        var target = (site.GetPosition(earth, time) - earth.Position).Normalized;
        double range = earth.Radius * System.Math.Atan2(surface.Cross(target).Magnitude, surface.Dot(target));
        return $"{label} met={witness.MissionElapsedSeconds:F2} altitudeM={witness.GeodeticAltitudeM:F2} "
            + $"airSpeedMps={witness.AtmosphereRelativeSpeedMps:F2} verticalMps={witness.VerticalSpeedMps:F2} "
            + $"shieldAlignment={witness.WindwardShieldDotVelocity:F3} "
            + $"lat={witness.BodyFixedLatitudeDegrees:F5} lon={witness.BodyFixedLongitudeDegrees:F5} starbaseRangeM={range:F2}";
    }
}
