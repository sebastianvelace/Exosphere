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
    private const double RuntimeEpochToleranceSeconds = 1e-7;
    private readonly VesselSystemsRuntime _fallbackRuntime =
        new("__unbound_systems__", simulationTime: 0.0);
    private VesselSystemsRuntime _activeRuntime;

    public static SystemsController? Instance { get; private set; }

    /// <summary>Explicitly materialized systems runtimes keyed by stable vessel id.</summary>
    public VesselSystemsRuntimeRegistry RuntimeRegistry { get; } = new();

    public VesselSystemsRuntime ActiveRuntime => _activeRuntime;
    public LifeSupportSystem LifeSupport => _activeRuntime.LifeSupport;
    public PowerSystem       Power       => _activeRuntime.Power;
    public ThermalSystem     Thermal     => _activeRuntime.Thermal;
    public CommsSystem       Comms       => _activeRuntime.Comms;
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

    public SystemsController()
    {
        _activeRuntime = _fallbackRuntime;
    }

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
        systemsDeadline = MinDeadline(systemsDeadline, Thermal.GetNextAlertDeadlineSeconds());

        return new SimulationExternalInterestInputs(
            IsMissionControlled: missionControlled,
            IsMissionCriticalState: missionCritical,
            IsAtmosphereOrReentry: MissionManager.Instance?.InDescent == true,
            HasPendingMissionCallback:
                MissionManager.Instance?.HasPendingMissionCallbacks == true,
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

        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveVessel is { } active && bridge.Universe != null)
        {
            if (!TryActivateVesselRuntime(active, bridge.Universe.CurrentTime))
                GD.PushWarning("[Systems] Active vessel runtime could not be materialized.");

            if (SaveSystem.PendingMaterializedSystemsStates is { } pendingStates)
            {
                SaveSystem.PendingMaterializedSystemsStates = null;
                RestoreMaterializedSaveStates(
                    pendingStates, bridge.Universe.CurrentTime);
            }
            else if (SaveSystem.PendingSystemsState is { } pending)
            {
                SaveSystem.PendingSystemsState = null;
                RestoreSaveState(pending, active.Id, bridge.Universe.CurrentTime);
            }
        }
        else if (SaveSystem.PendingMaterializedSystemsStates is not null
            || SaveSystem.PendingSystemsState is not null)
            GD.PushWarning("[Systems] Loaded state deferred: active vessel is not ready.");

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

    /// <summary>Captures all persistent systems state at one committed simulation epoch.</summary>
    public VesselSystemsState CaptureSaveState(string vesselId, double simulationTime)
    {
        if (!string.Equals(_activeRuntime.VesselId, vesselId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Active systems runtime belongs to '{_activeRuntime.VesselId}', "
                + $"not '{vesselId}'.");
        EnsureRuntimeEpoch(_activeRuntime, simulationTime);
        var state = _activeRuntime.CaptureState();
        state.Validate();
        return state;
    }

    /// <summary>
    /// Captures every systems runtime currently materialized by vessel identity. The
    /// registry rejects a mixed-epoch map before it can reach SaveGameV2.
    /// </summary>
    public Dictionary<string, VesselSystemsState> CaptureMaterializedSaveStates(
        double simulationTime) => RuntimeRegistry.CaptureStates(simulationTime);

    /// <summary>
    /// Restores all materialized vessel runtimes atomically after Universe has replaced
    /// its vessel instances. Vessels omitted from the map remain unmaterialized and cannot
    /// be treated as dormant by a future scheduler.
    /// </summary>
    public void RestoreMaterializedSaveStates(
        IReadOnlyDictionary<string, VesselSystemsState> states,
        double simulationTime)
    {
        ArgumentNullException.ThrowIfNull(states);
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null)
            throw new InvalidDataException("Universe is not ready for systems restore.");

        foreach (var (vesselId, state) in states)
        {
            if (state is null
                || !string.Equals(vesselId, state.VesselId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Systems state map key does not match its vessel id for '{vesselId}'.");
            }
        }

        string[] knownVesselIds = bridge.Universe.Vessels
            .Select(vessel => vessel.Id)
            .ToArray();
        RuntimeRegistry.RestoreStates(
            states.Values,
            knownVesselIds,
            simulationTime);

        _activeRuntime = _fallbackRuntime;
        if (bridge.ActiveVessel is { } active
            && RuntimeRegistry.TryGet(active.Id, out var restored)
            && restored is not null)
        {
            EnsureRuntimeEpoch(restored, simulationTime);
            _activeRuntime = restored;
        }
        GroundRelay.Clear();
        ControlLimited = false;
        CurrentSystemsPhase = MapMissionPhase(
            MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
    }

    /// <summary>
    /// Restores a state already validated by SaveGameV2. Delayed ground commands are
    /// intentionally discarded because their original link epoch may no longer exist.
    /// </summary>
    public void RestoreSaveState(
        VesselSystemsState state,
        string expectedVesselId,
        double expectedSimulationTime)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        if (!string.Equals(state.VesselId, expectedVesselId, StringComparison.Ordinal))
            throw new System.IO.InvalidDataException(
                "Loaded systems state targets another vessel.");
        if (!double.IsFinite(expectedSimulationTime)
            || System.Math.Abs(state.SimulationTime - expectedSimulationTime) > 1e-9)
        {
            throw new System.IO.InvalidDataException(
                "Loaded systems state is at another epoch.");
        }

        // SaveGameV2 replaces the Universe's vessel instances before this method runs.
        // Discard a runtime that belongs to the previous object/epoch, then materialize
        // the authoritative loaded snapshot at the committed save epoch.
        RuntimeRegistry.Remove(expectedVesselId);
        if (string.Equals(_activeRuntime.VesselId, expectedVesselId, StringComparison.Ordinal))
            _activeRuntime = _fallbackRuntime;
        if (!TryActivateVesselRuntimeById(expectedVesselId, expectedSimulationTime))
            throw new InvalidDataException(
                $"Could not materialize systems runtime for '{expectedVesselId}'.");
        _activeRuntime.RestoreState(state);
        GroundRelay.Clear();
        ControlLimited = false;
        CurrentSystemsPhase = MapMissionPhase(
            MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
    }

    /// <summary>Clears system state when loading a legacy save with no system snapshot.</summary>
    public void ResetForLoadedSimulation()
    {
        var bridge = SimulationBridge.Instance;
        RuntimeRegistry.Clear();
        _activeRuntime = _fallbackRuntime;
        if (bridge?.ActiveVessel is { } active && bridge.Universe != null)
            TryActivateVesselRuntime(active, bridge.Universe.CurrentTime);
        _activeRuntime.Reset(bridge?.Universe?.CurrentTime ?? _activeRuntime.SimulationTime);
        GroundRelay.Clear();
        ControlLimited = false;
        CurrentSystemsPhase = MapMissionPhase(
            MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
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
    /// Activates the systems runtime for the vessel that is about to enter physics. A
    /// vessel switch clears delayed ground commands so an uplink sample cannot cross
    /// ownership. Unknown vessels are materialized only at this committed epoch.
    /// </summary>
    public bool PrepareForPhysicsTick(
        Exosphere.Simulation.Vessel vessel,
        double simulationTime)
    {
        return TryActivateVesselRuntime(vessel, simulationTime);
    }

    private bool TryActivateVesselRuntime(
        Exosphere.Simulation.Vessel vessel,
        double simulationTime)
    {
        if (!double.IsFinite(simulationTime) || simulationTime < 0.0)
        {
            ControlLimited = true;
            GD.PushError("[Systems] Cannot activate runtime at an invalid epoch.");
            return false;
        }

        if (ReferenceEquals(_activeRuntime, _fallbackRuntime)
            || !string.Equals(_activeRuntime.VesselId, vessel.Id, StringComparison.Ordinal))
        {
            try
            {
                if (!RuntimeRegistry.TryGet(vessel.Id, out var existing)
                    || existing is null)
                    existing = RuntimeRegistry.Materialize(vessel.Id, simulationTime);
                EnsureRuntimeEpoch(existing, simulationTime);
                _activeRuntime = existing;
                GroundRelay.Clear();
                ControlLimited = false;
                CurrentSystemsPhase = MapMissionPhase(
                    MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
            }
            catch (System.Exception ex)
            {
                ControlLimited = true;
                GD.PushError($"[Systems] Runtime handoff failed: {ex.Message}");
                return false;
            }
            return true;
        }

        try
        {
            EnsureRuntimeEpoch(_activeRuntime, simulationTime);
            return true;
        }
        catch (System.Exception ex)
        {
            ControlLimited = true;
            GD.PushError($"[Systems] Runtime epoch mismatch: {ex.Message}");
            return false;
        }
    }

    private bool TryActivateVesselRuntimeById(string vesselId, double simulationTime)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null)
            return false;

        foreach (var vessel in bridge.Universe.Vessels)
        {
            if (string.Equals(vessel.Id, vesselId, StringComparison.Ordinal))
                return TryActivateVesselRuntime(vessel, simulationTime);
        }
        return false;
    }

    private static void EnsureRuntimeEpoch(
        VesselSystemsRuntime runtime,
        double expectedSimulationTime)
    {
        if (!double.IsFinite(expectedSimulationTime)
            || System.Math.Abs(runtime.SimulationTime - expectedSimulationTime)
                > RuntimeEpochToleranceSeconds)
        {
            throw new InvalidOperationException(
                $"Runtime '{runtime.VesselId}' is at {runtime.SimulationTime:R}, "
                + $"expected {expectedSimulationTime:R}.");
        }
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

        var earthBody = _cachedEarth;
        var sunBody   = _cachedSun;
        double solarVisibility = SunController.SolarVisibility;

        Vector3d sunPos = sunBody?.Position ?? Vector3d.Zero;

        bool inAtmo    = refBody.Atmosphere != null && alt < refBody.Atmosphere.MaxAltitude;
        double atmoTemp = inAtmo ? refBody.Atmosphere!.GetTemperature(alt) : 3.0;
        double airspeed = vessel.GetSurfaceVelocity(refBody).Magnitude;
        double airDensity = refBody.GetAtmosphericDensity(vessel.Position);
        double heatFlux = Exosphere.Simulation.Physics.ThermalModel.ComputeHeatFlux(
            airDensity, airspeed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));

        Vector3d earthPos = earthBody?.Position ?? Vector3d.Zero;

        // Feed comms the free-stream entry condition so it can model plasma blackout.
        // These are read-only samples of state the physics already integrated.
        var entryCondition = new PlasmaBlackoutInput
        {
            HeatFluxWm2 = heatFlux,
            DensityKgM3 = airDensity,
            AirspeedMs  = airspeed,
        };
        _activeRuntime.Tick(new VesselSystemsTickInput(
            DeltaSeconds: processedSimDelta,
            SimulationTime: universe.CurrentTime,
            CrewCount: crewCount,
            Phase: sysPhase,
            VesselPosition: vessel.Position,
            EarthPosition: earthPos,
            SunPosition: sunPos,
            Bodies: universe.Bodies,
            SolarVisibility: solarVisibility,
            InAtmosphere: inAtmo,
            AtmosphericTemperatureK: atmoTemp,
            AeroHeatFluxWm2: heatFlux,
            EntryCondition: entryCondition));

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
