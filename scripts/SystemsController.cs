namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Systems;
using Exosphere.Simulation.Math;

/// Nodo que actualiza todos los sistemas cada frame y los expone al HUD.
public partial class SystemsController : Node
{
    public static SystemsController? Instance { get; private set; }

    public LifeSupportSystem LifeSupport { get; } = new();
    public PowerSystem       Power       { get; } = new();
    public ThermalSystem     Thermal     { get; } = new();
    public CommsSystem       Comms       { get; } = new();
    public GroundCommandRelay GroundRelay { get; } = new();

    public bool ControlLimited { get; private set; }

    /// <summary>True when one-way light time is large enough to delay ground stick/throttle.</summary>
    public bool GroundDelayActive =>
        Comms.HasSignal && Comms.SignalDelaySeconds >= GroundCommandRelay.ImmediateThresholdSeconds;

    /// <summary>Mapped systems phase for the active mission phase (for HUD / tests).</summary>
    public SystemsMissionPhase CurrentSystemsPhase { get; private set; } = SystemsMissionPhase.Idle;

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

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt  = vessel.GetAltitude(refBody);

        // Apply light-time-delayed ground commands first so autopilots can still
        // overwrite PitchYawRoll later in the frame (onboard, undelayed).
        FlushGroundCommands(universe.CurrentTime, vessel);

        int crewCount = vessel.Crew.Count > 0 ? vessel.Crew.Count : 4;
        CurrentSystemsPhase = MapMissionPhase(MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
        var sysPhase = CurrentSystemsPhase;
        LifeSupport.Tick(delta, crewCount, sysPhase);

        var earthBody = universe.GetBody("earth");
        var sunBody   = universe.GetBody("sun");
        double solarVisibility = 1.0;
        if (sunBody != null)
        {
            foreach (var body in universe.Bodies)
            {
                if (body.Id == "sun") continue;
                solarVisibility = System.Math.Min(solarVisibility,
                    MissionGeometry.SolarDiscVisibility(vessel.Position, body.Position,
                        body.Radius, sunBody.Position, sunBody.Radius));
            }
        }

        Vector3d sunPos = sunBody?.Position ?? Vector3d.Zero;
        double lsLoadKw = LifeSupport.GetEcLoadKw(crewCount, sysPhase);
        double phaseLoadKw = SystemsPhaseLoads.AvionicsExtraKw(sysPhase);
        Power.Tick(delta, vessel.Position, sunPos, solarVisibility, lsLoadKw + phaseLoadKw);

        bool inAtmo    = refBody.Atmosphere != null && alt < refBody.Atmosphere.MaxAltitude;
        double atmoTemp = inAtmo ? refBody.Atmosphere!.GetTemperature(alt) : 3.0;
        double airspeed = vessel.GetSurfaceVelocity(refBody).Magnitude;
        double airDensity = refBody.GetAtmosphericDensity(vessel.Position);
        double heatFlux = Exosphere.Simulation.Physics.ThermalModel.ComputeHeatFlux(
            airDensity, airspeed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
        Thermal.Tick(delta, solarVisibility, inAtmo, atmoTemp, heatFlux, sysPhase);

        Vector3d earthPos = earthBody?.Position ?? Vector3d.Zero;

        // Feed comms the free-stream entry condition so it can model plasma blackout.
        // These are read-only samples of state the physics already integrated.
        var entryCondition = new PlasmaBlackoutInput
        {
            HeatFluxWm2 = heatFlux,
            DensityKgM3 = airDensity,
            AirspeedMs  = airspeed,
        };
        Comms.Tick(delta, vessel.Position, earthPos, universe.Bodies, entryCondition);

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

        if (!Comms.HasSignal)
            GroundRelay.Clear();

        if (ControlLimited)
        {
            vessel.SASEnabled = false;
            vessel.PitchYawRoll = Vector3d.Zero;
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
