namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Systems;
using Exosphere.Simulation.Math;

/// Nodo que actualiza todos los sistemas cada frame y los expone al HUD.
public partial class SystemsController : Node
{
    // The system ticks themselves stay per-frame: their dt integration and command
    // ordering are gameplay-relevant. SunController owns the 20 Hz solar-disc sample;
    // reusing its value here avoids a duplicate body loop and keeps power/thermal/HUD
    // lighting on the same eclipse sample.
    private Universe? _cachedUniverse;
    private CelestialBody? _cachedEarth;
    private CelestialBody? _cachedSun;

    public static SystemsController? Instance { get; private set; }

    public LifeSupportSystem LifeSupport { get; } = new();
    public PowerSystem       Power       { get; } = new();
    public ThermalSystem     Thermal     { get; } = new();
    public CommsSystem       Comms       { get; } = new();
    public GroundCommandRelay GroundRelay { get; } = new();

    public bool ControlLimited { get; private set; }

    /// <summary>
    /// Drops ground-link commands that were scheduled for the old world position.
    /// A navigation jump changes both the reference body and the one-way light-time
    /// path; applying those delayed samples after the jump would overwrite the fresh
    /// teleport attitude/throttle reset and look like an unexplained tumble.
    /// </summary>
    public void ClearPendingGroundCommandsForTeleport() => GroundRelay.Clear();

    /// <summary>True when one-way light time is large enough to delay ground stick/throttle.</summary>
    public bool GroundDelayActive =>
        Comms.HasSignal && Comms.SignalDelaySeconds >= GroundCommandRelay.ImmediateThresholdSeconds;

    /// <summary>Mapped systems phase for the active mission phase (for HUD / tests).</summary>
    public SystemsMissionPhase CurrentSystemsPhase { get; private set; } = SystemsMissionPhase.Idle;

    /// <summary>
    /// Creates the game-layer portion of the observational interest snapshot. It is only
    /// valid for the active vessel: these systems are intentionally not instantiated for
    /// every distant vessel. The scheduler remains unchanged; this is a parity boundary
    /// for a future, separately gated deferred-work implementation.
    /// </summary>
    public SimulationExternalInterestInputs BuildSimulationInterestInputs()
    {
        MissionPhase phase = MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH;
        bool missionControlled = phase is MissionPhase.COUNTDOWN
            or MissionPhase.IGNITION
            or MissionPhase.LIFTOFF
            or MissionPhase.ASCENT_SH
            or MissionPhase.MAX_Q
            or MissionPhase.MECO
            or MissionPhase.SEPARATION
            or MissionPhase.ASCENT_SHIP
            or MissionPhase.ENTRY
            or MissionPhase.PEAK_HEATING
            or MissionPhase.AERO_DESCENT
            or MissionPhase.RETRO_BURN
            or MissionPhase.FINAL_DESCENT;
        bool missionCritical = phase is MissionPhase.LIFTOFF
            or MissionPhase.MAX_Q
            or MissionPhase.MECO
            or MissionPhase.SEPARATION
            or MissionPhase.ENTRY
            or MissionPhase.PEAK_HEATING
            or MissionPhase.AERO_DESCENT
            or MissionPhase.RETRO_BURN
            or MissionPhase.FINAL_DESCENT
            or MissionPhase.LANDED
            or MissionPhase.CAUGHT
            or MissionPhase.CRASHED;
        bool systemsAlert = ControlLimited
            || LifeSupport.OxygenAlert
            || LifeSupport.CO2Alert
            || Power.LowPowerAlert
            || Power.NoPowerAlert
            || Thermal.HotAlert
            || Thermal.ColdAlert
            || (!Comms.HasSignal && !Comms.PlasmaBlackout);
        var activeVessel = SimulationBridge.Instance?.ActiveVessel;
        int crewCount = activeVessel != null && activeVessel.Crew.Count > 0
            ? activeVessel.Crew.Count
            : 4;
        SystemsMissionPhase systemsPhase = MapMissionPhase(phase);
        double? systemsDeadline = MinDeadline(
            LifeSupport.GetNextAlertDeadlineSeconds(crewCount, systemsPhase),
            Power.GetNextAlertDeadlineSeconds());

        return new SimulationExternalInterestInputs(
            IsMissionControlled: missionControlled,
            IsMissionCriticalState: missionCritical,
            IsAtmosphereOrReentry: MissionManager.Instance?.InDescent == true,
            HasPendingMissionCallback: false,
            HasSystemsAlert: systemsAlert,
            SecondsUntilNextSystemsDeadline: systemsDeadline);
    }

    private static double? MinDeadline(double? first, double? second)
    {
        if (first is double a && second is double b)
            return System.Math.Min(a, b);
        return first ?? second;
    }

    public override void _Ready()
    {
        Instance = this;
        // Flush delayed ground commands before HUD enqueues this frame's stick sample
        // and before onboard guidance (Ascent/EDL) overwrites PitchYawRoll.
        ProcessPriority = -50;

        var uiLayer = GetTree().Root.FindChild("UI", true, false) as CanvasLayer;
        if (uiLayer != null)
        {
            var hud = new SystemsHUD { Name = "SystemsHUD" };
            uiLayer.CallDeferred("add_child", hud);
        }
    }

    public override void _Process(double _delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || vessel == null || universe == null) return;

        // Apply light-time-delayed ground commands first so autopilots can still
        // overwrite PitchYawRoll later in the frame (onboard, undelayed).
        FlushGroundCommands(universe.CurrentTime, vessel);

        // Consequences from the previous post-physics integration are applied before
        // the next physics tick. The consumable/system integration itself happens from
        // SimulationBridge.AdvanceProcessedSimulation after Universe.Tick, so it uses
        // the same committed vessel state and cannot consume wall-clock time twice.
        ApplyGameplayConsequences(vessel);
    }

    /// <summary>
    /// Advances gameplay systems using the simulation seconds actually committed by the
    /// most recent <see cref="Universe.Tick(double)"/>. SimulationBridge calls this once
    /// after physics and before ascent/EDL post-processors; it must not be called from a
    /// render-only controller with wall-clock delta.
    /// </summary>
    public void AdvanceProcessedSimulation()
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || vessel == null || universe == null) return;

        double processedSimDelta = bridge.LastProcessedSimulationSeconds;
        if (processedSimDelta <= 0.0) return;

        if (!ReferenceEquals(_cachedUniverse, universe))
        {
            _cachedUniverse = universe;
            _cachedEarth = universe.GetBody("earth");
            _cachedSun = universe.GetBody("sun");
        }

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt  = vessel.GetAltitude(refBody);

        int crewCount = vessel.Crew.Count > 0 ? vessel.Crew.Count : 4;
        CurrentSystemsPhase = MapMissionPhase(MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
        var sysPhase = CurrentSystemsPhase;
        LifeSupport.Tick(processedSimDelta, crewCount, sysPhase);

        var earthBody = _cachedEarth;
        var sunBody   = _cachedSun;
        double solarVisibility = SunController.SolarVisibility;

        Vector3d sunPos = sunBody?.Position ?? Vector3d.Zero;
        double lsLoadKw = LifeSupport.GetEcLoadKw(crewCount, sysPhase);
        double phaseLoadKw = SystemsPhaseLoads.AvionicsExtraKw(sysPhase);
        Power.Tick(processedSimDelta, vessel.Position, sunPos, solarVisibility,
            lsLoadKw + phaseLoadKw);

        bool inAtmo    = refBody.Atmosphere != null && alt < refBody.Atmosphere.MaxAltitude;
        double atmoTemp = inAtmo ? refBody.Atmosphere!.GetTemperature(alt) : 3.0;
        double airspeed = vessel.GetSurfaceVelocity(refBody).Magnitude;
        double airDensity = refBody.GetAtmosphericDensity(vessel.Position);
        double heatFlux = Exosphere.Simulation.Physics.ThermalModel.ComputeHeatFlux(
            airDensity, airspeed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
        Thermal.Tick(processedSimDelta, solarVisibility, inAtmo, atmoTemp, heatFlux, sysPhase);

        Vector3d earthPos = earthBody?.Position ?? Vector3d.Zero;

        // Feed comms the free-stream entry condition so it can model plasma blackout.
        // These are read-only samples of state the physics already integrated.
        var entryCondition = new PlasmaBlackoutInput
        {
            HeatFluxWm2 = heatFlux,
            DensityKgM3 = airDensity,
            AirspeedMs  = airspeed,
        };
        Comms.Tick(processedSimDelta, vessel.Position, earthPos, universe.Bodies, entryCondition);

        ApplyGameplayConsequences(vessel);
    }

    /// <summary>
    /// Enqueue a ground-link attitude sample. No-ops during LOS/blackout.
    /// Onboard controllers must set <c>vessel.PitchYawRoll</c> directly.
    /// </summary>
    public void SubmitGroundAttitude(Vector3d pitchYawRoll)
    {
        var universe = SimulationBridge.Instance?.Universe;
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (universe == null || vessel == null) return;

        GroundRelay.SubmitAttitude(
            universe.CurrentTime,
            Comms.SignalDelaySeconds,
            pitchYawRoll,
            linkUp: Comms.HasSignal,
            applyNow: pyr => vessel.PitchYawRoll = pyr);
    }

    /// <summary>
    /// Enqueue a ground-link throttle delta (same units as SimulationBridge.ThrottleUp).
    /// </summary>
    public void SubmitGroundThrottleDelta(double deltaThrottle)
    {
        var universe = SimulationBridge.Instance?.Universe;
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (universe == null || vessel == null) return;

        GroundRelay.SubmitThrottleDelta(
            universe.CurrentTime,
            Comms.SignalDelaySeconds,
            deltaThrottle,
            linkUp: Comms.HasSignal,
            applyNow: d =>
            {
                vessel.Throttle = System.Math.Clamp(vessel.Throttle + d, 0.0, 1.0);
            });
    }

    private void FlushGroundCommands(double simTime, Exosphere.Simulation.Vessel vessel)
    {
        GroundRelay.Tick(
            simTime,
            applyAttitude: pyr => vessel.PitchYawRoll = pyr,
            applyThrottleDelta: d =>
            {
                vessel.Throttle = System.Math.Clamp(vessel.Throttle + d, 0.0, 1.0);
            });
    }

    private void ApplyGameplayConsequences(Exosphere.Simulation.Vessel vessel)
    {
        bool structuralLost = vessel.StructuralControlLost;
        // A plasma blackout is a comms outage, not a control outage: entry guidance is flown
        // onboard precisely BECAUSE the ground link is unavailable through the sheath. Every
        // real orbital entry is flown with the radio dead. Excluding it here also keeps the
        // blackout purely presentational for ControlLimited, while GroundCommandRelay still
        // drops uplink samples when HasSignal is false.
        bool geometricLos = !Comms.HasSignal && !Comms.PlasmaBlackout;
        ControlLimited = Power.NoPowerAlert || geometricLos || !LifeSupport.CrewAlive
            || structuralLost;

        // LOS/blackout clears the ground uplink queue only. Local stick (crewed FCS)
        // must not be zeroed here — Systems runs before physics (prio −50), so wiping
        // PitchYawRoll would kill the player's command for the whole tick. Structural
        // dead-stick is enforced in Vessel.Tick via ControlAuthority.
        if (!Comms.HasSignal)
            GroundRelay.Clear();

        if (ControlLimited)
        {
            vessel.SASEnabled = false;
            GroundRelay.Clear();

            ManeuverExecutor.Instance?.Abort();

            if (GetTree().Root.FindChild("AutopilotController", true, false) is AutopilotController ap)
                ap.Disarm();

            // Structural dead-stick: cut commanded throttle so a tumbling wreck does not
            // keep burning propellant under a stuck autopilot setpoint.
            if (structuralLost)
                vessel.Throttle = 0.0;
        }
    }

    /// <summary>Maps game mission phases onto the coarser systems consumption enum.</summary>
    public static SystemsMissionPhase MapMissionPhase(MissionPhase phase) => phase switch
    {
        MissionPhase.PRE_LAUNCH or MissionPhase.COUNTDOWN
            or MissionPhase.LANDED or MissionPhase.CAUGHT => SystemsMissionPhase.Idle,

        MissionPhase.PEAK_HEATING => SystemsMissionPhase.PeakHeating,

        MissionPhase.ENTRY or MissionPhase.AERO_DESCENT => SystemsMissionPhase.Entry,

        MissionPhase.IGNITION or MissionPhase.LIFTOFF or MissionPhase.ASCENT_SH
            or MissionPhase.MAX_Q or MissionPhase.ASCENT_SHIP
            or MissionPhase.RETRO_BURN or MissionPhase.FINAL_DESCENT
            => SystemsMissionPhase.HighLoad,

        _ => SystemsMissionPhase.Active,
    };
}
