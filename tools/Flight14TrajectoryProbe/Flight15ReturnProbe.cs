using Exosphere.Simulation;
using Exosphere.Simulation.Flight;

/// <summary>Continuous pad-to-entry trial of the Flight 15 return estimate.</summary>
internal static class Flight15ReturnProbe
{
    public static int Execute(string dataDirectory, string outputDirectory, bool includeDescent = false,
        bool useFullUniverse = false)
    {
        var definition = Flight15Mission.LoadReturnEstimate(dataDirectory);
        var descentDefinition = includeDescent ? Flight15Mission.LoadDescentEstimate(dataDirectory) : null;
        var world = useFullUniverse ? Universe.LoadFromDataDirectory(dataDirectory) : null;
        var run = Flight15Mission.CreateAscent(dataDirectory, world);
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

        Record($"PROFILE {definition.Id} coastWarp=100 burnStepS={Universe.DeterministicControlPeriodSeconds} "
            + $"bodies={universe.Bodies.Count}");
        if (descentDefinition != null)
            Record($"DESCENT_PROFILE {descentDefinition.Id} bankDeg={descentDefinition.NominalBankDegrees} "
                + $"alphaDeg={descentDefinition.EntryAngleOfAttackDegrees} "
                + $"descentRateFraction={descentDefinition.DownwardSpeedFraction}");
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
        var descent = descentDefinition != null
            ? new Flight14DescentController(ship, earth, returning, launch, descentDefinition) : null;
        universe.PhysicsStepController = (IPhysicsStepController?)descent ?? returning;
        universe.TimeScale = 1;
        bool DescentStarted() => descent is { Phase: not Flight14DescentPhase.WaitingForEntry };
        string CurrentPhase() => DescentStarted() ? descent!.Phase.ToString() : returning.Phase.ToString();
        bool IsTerminal() => descent == null
            ? returning.Phase is Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked
            : descent.Phase is Flight14DescentPhase.DescentReached or Flight14DescentPhase.Blocked;
        string previousPhase = CurrentPhase();
        int maxBurnEngines = 0;
        double nextSample = Met();
        double deadline = descentDefinition?.MaximumMissionSeconds ?? definition.MaximumMissionSeconds;
        while (!IsTerminal()
            && Met() <= deadline + Universe.DeterministicControlPeriodSeconds)
        {
            universe.Tick(Universe.DeterministicControlPeriodSeconds);
            if (returning.Phase == Flight14ReturnPhase.DeorbitBurn)
                maxBurnEngines = System.Math.Max(maxBurnEngines,
                    engines.GetEngineTelemetry(0).Count(engine => engine.ThrustN > 1));
            string phase = CurrentPhase();
            if (phase == previousPhase && Met() < nextSample)
                continue;
            previousPhase = phase;
            nextSample = Met() + (DescentStarted() ? 15 : 60);
            Record($"STATE phase={phase} met={Met():F2} altitudeM={ship.GetAltitude(earth):F2} "
                + $"airSpeedMps={ship.GetSurfaceVelocity(earth).Magnitude:F2} radialPeriapsisM={Periapsis():F2} "
                + $"angularRateRadPerS={ship.AngularVelocity.Magnitude:F4} "
                + $"starbaseRangeM={SiteRange(earth, site, ship.Position, universe.CurrentTime):F2}");
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
        if (descent != null)
        {
            Record($"DESCENT phase={descent.Phase} maxAngularRateRadPerS={descent.MaximumAngularRateRadPerSecond:F4} "
                + $"endAngularRateRadPerS={ship.AngularVelocity.Magnitude:F4} "
                + $"peakLoadG={descent.PeakAerodynamicLoadG:F3} peakPressurePa={descent.PeakDynamicPressurePa:F1} "
                + $"peakHeatFluxWPerM2={descent.PeakStagnationHeatFluxWPerM2:F1}");
            if (descent.EndWitness is not { } end)
                return Fail(descent.BlockReason ?? "descent-endpoint-timeout");
            Record(Witness("DESCENT_ENDPOINT", end, earth, site));
        }
        return result.HasDeliveredDeorbitThrust && maxBurnEngines == 1 ? 0 : Fail("single-engine-thrust-missing");
    }

    private static string Witness(string label, Flight14EntryWitness witness, CelestialBody earth, LaunchSite site)
    {
        double time = witness.SimulationTimeSeconds;
        var position = earth.GetSurfacePositionAtTime(witness.BodyFixedLatitudeDegrees,
            witness.BodyFixedLongitudeDegrees, time, witness.GeodeticAltitudeM);
        double range = SiteRange(earth, site, position, time);
        return $"{label} met={witness.MissionElapsedSeconds:F2} altitudeM={witness.GeodeticAltitudeM:F2} "
            + $"airSpeedMps={witness.AtmosphereRelativeSpeedMps:F2} verticalMps={witness.VerticalSpeedMps:F2} "
            + $"shieldAlignment={witness.WindwardShieldDotVelocity:F3} "
            + $"lat={witness.BodyFixedLatitudeDegrees:F5} lon={witness.BodyFixedLongitudeDegrees:F5} starbaseRangeM={range:F2}";
    }

    private static double SiteRange(CelestialBody earth, LaunchSite site,
        Exosphere.Simulation.Math.Vector3d position, double time)
    {
        var surface = (position - earth.Position).Normalized;
        var target = (site.GetPosition(earth, time) - earth.Position).Normalized;
        return earth.Radius * System.Math.Atan2(surface.Cross(target).Magnitude, surface.Dot(target));
    }
}
