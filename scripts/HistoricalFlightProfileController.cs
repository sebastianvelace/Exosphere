namespace Exosphere.Game;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Godot;

/// <summary>
/// Adapter for non-orbital historical profiles. It issues commands while the regular
/// rigid-body, propulsion, atmosphere, staging and parachute systems remain authoritative.
/// </summary>
public partial class HistoricalFlightProfileController : Node
{
    public static HistoricalFlightProfileController? Instance { get; private set; }

    private bool _active;

    /// <summary>True while a historical automatic flight sequence owns guidance.</summary>
    public bool IsEngaged => _active;

    /// <summary>
    /// Historical profiles are another possible attitude/throttle writer.  A navigation
    /// jump must invalidate them just like the ordinary ascent and maneuver controllers;
    /// otherwise a profile resumed on the old body can reapply stale commands after J.
    /// </summary>
    public void CancelGuidanceForTeleport()
    {
        _active = false;
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel == null) return;

        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        vessel.SASEnabled = true;
    }

    private bool _atlasProfile;
    private bool _geminiProfile;
    private bool _apolloProfile;
    private bool _apollo11Tde;
    private bool _apollo11EagleExtracted;
    private double _startTime;
    private bool _meco;
    private bool _boosterPackageJettisoned;
    private bool _separated;
    private bool _towerJettisoned;
    private bool _retroComplete;
    private bool _entryAnnounced;
    private bool _chutesDeployed;
    private bool _orbitWarpArmed;
    private bool _geminiStageOneSeparated;
    private bool _rendezvousConfigured;
    private RendezvousInterceptPlan? _geminiTpi;
    private bool _geminiTpiReplanned;
    private bool _geminiReentryRcsArmed;
    private double _geminiBeco = double.NaN;
    private double _geminiRetroIgnition = double.NaN;
    private bool _geminiThrusterIsolated;
    private TranslationLambertBurn? _geminiBurn;
    private int _geminiBurnIndex;
    private ProximityApproach? _geminiProximity;
    private ProximityApproach? _apollo11Proximity;
    private LunarPoweredDescent? _apollo11Descent;
    private double _apollo11CircularizedTime = double.NaN;
    private double _apollo11UndockTime = double.NaN;
    private bool _docked;
    private bool _undocked;
    private bool _failureAnnounced;
    private bool _controlRecovered;
    private bool _adapterSeparated;
    private bool _apolloSicSeparated;
    private bool _apolloSiiSeparated;
    private bool _apolloParkingOrbit;
    private double _apolloSicCutoffElapsed = double.NaN;
    private bool _apolloTliStarted;
    private bool _apolloTliComplete;
    private bool _apolloCsmSeparated;
    private bool _apolloLoiComplete;
    private bool _apolloCircularized;
    private bool _apolloTeiComplete;
    private bool _apolloSmSeparated;
    private double _apolloTliStartTime;
    private double _apolloLoiTime;
    private PeriapsisCaptureBurn? _apolloLunarBurn;
    private double _apolloLunarBurnLastTime = double.NaN;
    private ProgradeEnergyBurn? _apolloTliBurn;
    // Planned at countdown request (before Engage resets the profile) and adopted at liftoff.
    private LunarLaunchWindowSolution? _pendingApolloLaunchWindow;
    private LunarLaunchWindowSolution? _apolloLaunchWindow;
    private LambertGuidedBurn? _apolloTranslunarTrim;
    private bool _apolloTranslunarTrimDone;
    private ProgradeEnergyBurn? _apolloTeiBurn;
    private double _apolloTeiIgnitionTime;
    private LambertGuidedBurn? _apolloTransearthBurn;
    private int _apolloTransearthCorrections;
    private double _apolloEarthReturnTime = double.NaN;
    private LambertGuidedBurn? _apolloMidcourseBurn;
    private double _apolloGuidedBurnLastTime = double.NaN;
    private bool _apolloMidcourseComplete;
    private LunarTransferPlan? _apolloLunarPlan;

    public override void _ExitTree()
    {
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    public override void _Ready()
    {
        Instance = this;
        var runtime = CampaignRuntime.Instance;
        var phase = MissionManager.Instance?.Phase;
        string? profileId = SimulationBridge.Instance?.ActiveFlightProfileId;
        if (profileId is MercuryRedstoneFlightProfile.Id
                or MercuryAtlasFlightProfile.Id
                or Gemini8FlightProfile.Id
                or Apollo8FlightProfile.Id
                or Apollo11FlightProfile.Id
            && runtime?.Director != null
            && phase is not null
            && phase is not MissionPhase.PRE_LAUNCH
                and not MissionPhase.COUNTDOWN
                and not MissionPhase.IGNITION
                and not MissionPhase.LANDED
                and not MissionPhase.CRASHED)
        {
            double elapsed = runtime.Director.Evidence.ElapsedSeconds;
            if (profileId == Apollo11FlightProfile.Id)
                ResumeApollo11(elapsed);
            else if (profileId == Apollo8FlightProfile.Id)
                ResumeApollo8(elapsed);
            else if (profileId == Gemini8FlightProfile.Id)
                ResumeGemini(elapsed);
            else
                Resume(
                    elapsed,
                    profileId == MercuryAtlasFlightProfile.Id);
            // A save records where the flight is; the clock may not match the historical one.
            AlignFlagsWithPhase(phase.Value);
        }
    }

    /// <summary>
    /// Plans the next Apollo lunar launch window for liftoff no earlier than
    /// <paramref name="earliestLiftoffTime"/> and returns its liftoff time, or null for
    /// profiles without one (the countdown then starts immediately).
    /// </summary>
    public double? PlanLaunchWindow(double earliestLiftoffTime)
    {
        _pendingApolloLaunchWindow = null;
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveFlightProfileId is not (Apollo8FlightProfile.Id or Apollo11FlightProfile.Id))
            return null;
        var earth = bridge.Universe.GetBody("earth");
        var moon = bridge.Universe.GetBody("moon");
        if (earth == null || moon?.OrbitalElements == null || bridge.LaunchSiteOrNull is not { } site)
            return null;
        _pendingApolloLaunchWindow = LunarLaunchWindow.FindNext(earth, moon.OrbitalElements, site,
            earliestLiftoffTime, Apollo8FlightProfile.LoiIgnitionSeconds,
            Apollo8FlightProfile.MinimumLaunchAzimuthDegrees,
            Apollo8FlightProfile.MaximumLaunchAzimuthDegrees);
        if (_pendingApolloLaunchWindow is not { } window)
        {
            GD.PrintErr("[HISTORICAL] No Apollo launch window within 24 h; launching due east.");
            return null;
        }
        GD.Print($"[HISTORICAL] Apollo launch window: azimuth {window.AzimuthDegrees:F1} deg, "
            + $"liftoff in {(window.LiftoffTime - bridge.Universe.CurrentTime) / 3600.0:F2} h.");
        return window.LiftoffTime;
    }

    /// <summary>
    /// [K]: hands the flight to Mission Control (this director) from wherever the player is, or
    /// gives it back. On the pad it starts the count; in flight it resumes the profile and then
    /// trusts the mission phase, which <see cref="MissionManager"/> keeps from the flown state,
    /// over the historical clock (a hand-flown ascent reaches orbit on its own schedule).
    /// Returns true while Mission Control is flying.
    /// </summary>
    public bool ToggleMissionControl()
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return false;
        if (_active)
        {
            // The throttle stays where it is, as with any autopilot: dropping it mid-ascent
            // would drop the rocket. The crew cuts it with [X] when they want.
            _active = false;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.TranslationCommand = Vector3d.Zero;
            vessel.SASEnabled = true;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Mission Control hands the spacecraft back to the crew.");
            return false;
        }

        var mission = MissionManager.Instance;
        var phase = mission?.Phase ?? MissionPhase.PRE_LAUNCH;
        string? profileId = bridge.ActiveFlightProfileId;
        if (profileId is not (MercuryRedstoneFlightProfile.Id or MercuryAtlasFlightProfile.Id
                or Gemini8FlightProfile.Id or Apollo8FlightProfile.Id or Apollo11FlightProfile.Id)
            || phase is MissionPhase.LANDED or MissionPhase.CAUGHT or MissionPhase.CRASHED)
            return false;
        if (phase is MissionPhase.PRE_LAUNCH)
        {
            // The count engages Mission Control at T-0 (and holds for a lunar window first).
            mission?.StartCountdown();
            return mission?.Phase != MissionPhase.PRE_LAUNCH || mission.IsHoldingForLaunchWindow;
        }
        if (phase is MissionPhase.COUNTDOWN or MissionPhase.IGNITION) return true;

        // The director's own liftoff clock when it launched this flight; the campaign clock
        // also counts the countdown, which put a resumed Freedom 7 ten seconds ahead.
        double elapsed = _startTime > 0.0 && _startTime <= bridge.Universe.CurrentTime
            ? bridge.Universe.CurrentTime - _startTime
            : CampaignRuntime.Instance?.Director?.Evidence.ElapsedSeconds ?? 0.0;
        if (profileId == Apollo11FlightProfile.Id) ResumeApollo11(elapsed);
        else if (profileId == Apollo8FlightProfile.Id) ResumeApollo8(elapsed);
        else if (profileId == Gemini8FlightProfile.Id) ResumeGemini(elapsed);
        else Resume(elapsed, profileId == MercuryAtlasFlightProfile.Id);
        AlignFlagsWithPhase(phase);
        GD.Print($"[HISTORICAL] Mission Control takes over in phase {phase}.");
        return _active;
    }

    /// <summary>The mission phase, not the historical clock, says which steps are done.</summary>
    private void AlignFlagsWithPhase(MissionPhase phase)
    {
        bool reached(MissionPhase milestone) => phase >= milestone;
        if (_apolloProfile)
        {
            double now = SimulationBridge.Instance!.Universe.CurrentTime;
            _apolloParkingOrbit = reached(MissionPhase.ORBIT);
            _apolloTliComplete = _apolloTliStarted = reached(MissionPhase.TLI);
            _apolloLunarPlan = null;
            if (_apolloTliComplete && !_apolloCsmSeparated)
                // Separate on the next step: the injection is already flown.
                _apolloTliStartTime = now
                    - (Apollo8FlightProfile.CsmSivbSeparationSeconds - Apollo8FlightProfile.TliIgnitionSeconds);
            else if (_apolloTliComplete)
                _apolloTliStartTime = now;
            _apolloMidcourseComplete = reached(MissionPhase.LUNAR_APPROACH);
            _apolloLoiComplete = reached(MissionPhase.LOI);
            if (_apolloLoiComplete) _apolloLoiTime = now;
            _apolloCircularized = reached(MissionPhase.LUNAR_ORBIT);
            if (!_apollo11Tde) _apolloTeiComplete = reached(MissionPhase.TEI);
        }
        else if (!_geminiProfile)
        {
            // Mercury: in orbit the booster is spent whatever the clock says.
            _meco = reached(MissionPhase.ORBIT) || _meco;
        }
        else
        {
            _meco = reached(MissionPhase.ORBIT) || _meco;
            if (!_docked)
            {
                // Not docked yet: fly the rendezvous from here, the anomaly comes after docking.
                _rendezvousConfigured = false;
                _undocked = false;
                _failureAnnounced = false;
                _controlRecovered = false;
            }
        }
    }

    public bool EngageIfSupported()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveFlightProfileId is not (
                MercuryRedstoneFlightProfile.Id
                or MercuryAtlasFlightProfile.Id
                or Gemini8FlightProfile.Id
                or Apollo8FlightProfile.Id
                or Apollo11FlightProfile.Id))
            return false;
        var vessel = bridge.ActiveVessel;
        if (vessel == null) return false;

        _active = true;
        _atlasProfile =
            bridge.ActiveFlightProfileId == MercuryAtlasFlightProfile.Id;
        _geminiProfile =
            bridge.ActiveFlightProfileId == Gemini8FlightProfile.Id;
        _apollo11Tde =
            bridge.ActiveFlightProfileId == Apollo11FlightProfile.Id;
        _apolloProfile =
            bridge.ActiveFlightProfileId == Apollo8FlightProfile.Id
            || _apollo11Tde;
        _startTime = bridge.Universe.CurrentTime;
        _meco = false;
        _boosterPackageJettisoned = false;
        _separated = false;
        _towerJettisoned = false;
        _retroComplete = false;
        _entryAnnounced = false;
        _chutesDeployed = false;
        _orbitWarpArmed = false;
        _geminiStageOneSeparated = false;
        _rendezvousConfigured = false;
        _geminiTpi = null;
        _geminiTpiReplanned = false;
        _geminiReentryRcsArmed = false;
        _geminiThrusterIsolated = false;
        _geminiBeco = double.NaN;
        _geminiRetroIgnition = double.NaN;
        _geminiBurn = null;
        _geminiBurnIndex = 0;
        _geminiProximity = null;
        _apollo11Proximity = null;
        _apollo11Descent = null;
        _apollo11CircularizedTime = double.NaN;
        _apollo11UndockTime = double.NaN;
        _docked = false;
        _undocked = false;
        _failureAnnounced = false;
        _controlRecovered = false;
        _adapterSeparated = false;
        _apolloSicSeparated = false;
        _apolloSiiSeparated = false;
        _apolloParkingOrbit = false;
        _apolloSicCutoffElapsed = double.NaN;
        _apolloTliStarted = false;
        _apolloTliComplete = false;
        _apolloCsmSeparated = false;
        _apolloLoiComplete = false;
        _apolloCircularized = false;
        _apolloTeiComplete = false;
        _apolloSmSeparated = false;
        _apollo11EagleExtracted = false;
        _apolloTliStartTime = 0.0;
        _apolloLoiTime = 0.0;
        _apolloLunarPlan = null;
        _apolloTliBurn = null;
        _apolloMidcourseBurn = null;
        _apolloMidcourseComplete = false;
        _apolloLunarBurn = null;
        _apolloLaunchWindow = _apolloProfile ? _pendingApolloLaunchWindow : null;
        ResetApolloReturn();
        _pendingApolloLaunchWindow = null;
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
        if (_apollo11Tde)
            GD.Print(
                "[HISTORICAL] Apollo 11 TD&E, docked LOI and powered descent "
                + "sequence engaged.");
        else if (_apolloProfile)
            GD.Print(
                "[HISTORICAL] Apollo 8 lunar-orbit and Earth-return "
                + "sequence engaged.");
        else if (_geminiProfile)
        {
            bridge.EnsureGemini8AgenaTarget();
            GD.Print(
                "[HISTORICAL] Gemini VIII rendezvous and emergency-return "
                + "sequence engaged.");
        }
        else
            GD.Print(_atlasProfile
                ? "[HISTORICAL] Friendship 7 automatic flight sequence engaged."
                : "[HISTORICAL] Freedom 7 automatic flight sequence engaged.");
        return true;
    }

    private void Resume(double elapsedSeconds, bool atlasProfile)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;
        _active = true;
        _atlasProfile = atlasProfile;
        _startTime = bridge.Universe.CurrentTime
            - System.Math.Max(0.0, elapsedSeconds);
        _meco = elapsedSeconds >= (_atlasProfile
            ? MercuryAtlasFlightProfile.SustainerEngineCutoffSeconds
            : MercuryRedstoneFlightProfile.MainEngineCutoffSeconds);
        _boosterPackageJettisoned = !_atlasProfile
            || !vessel.Parts.Parts.Any(part =>
                part.Definition.HasVehicleRole("booster_engine_package"));
        _separated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole(_atlasProfile
                ? "atlas_tank"
                : "booster_engine"));
        _towerJettisoned = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("escape_tower"));
        _retroComplete = elapsedSeconds >= (_atlasProfile
            ? MercuryAtlasFlightProfile.RetroSequenceSeconds
                + MercuryAtlasFlightProfile.RetroBurnDurationSeconds
            : MercuryRedstoneFlightProfile.RetroSequenceSeconds
                + MercuryRedstoneFlightProfile.RetroBurnDurationSeconds);
        _entryAnnounced = MissionManager.Instance?.InDescent == true;
        _chutesDeployed = vessel.HasDeployedParachute;
        _orbitWarpArmed = _atlasProfile
            && bridge.WarpIndex >= 6;
        vessel.SASEnabled = false;
        GD.Print(
            $"[HISTORICAL] {(_atlasProfile ? "Friendship 7" : "Freedom 7")} "
            + $"sequence resumed at T+{elapsedSeconds:F1}s.");
    }

    private void ResumeGemini(double elapsedSeconds)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;
        _active = true;
        _atlasProfile = false;
        _geminiProfile = true;
        _startTime = bridge.Universe.CurrentTime
            - System.Math.Max(0.0, elapsedSeconds);
        _meco = elapsedSeconds >= Gemini8FlightProfile.StageTwoCutoffSeconds;
        _geminiStageOneSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("titan_stage1_engine"));
        _separated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("spacecraft_separation"));
        _rendezvousConfigured =
            elapsedSeconds >= Gemini8FlightProfile.RendezvousCompleteSeconds;
        _docked = bridge.Universe.DockingConnections.Any(connection =>
            connection.PrimaryVesselId == vessel.Id
            || connection.SecondaryVesselId == vessel.Id);
        _undocked = elapsedSeconds >= Gemini8FlightProfile.UndockingSeconds
            && !_docked;
        _failureAnnounced =
            elapsedSeconds >= Gemini8FlightProfile.ThrusterAnomalySeconds;
        _controlRecovered =
            elapsedSeconds >= Gemini8FlightProfile.ControlRecoveredSeconds;
        _geminiReentryRcsArmed =
            elapsedSeconds >= Gemini8FlightProfile.ReentryControlActivationSeconds;
        _geminiThrusterIsolated =
            elapsedSeconds >= Gemini8FlightProfile.ThrusterIsolationSeconds;
        // The stuck-thruster torque is not persisted: a save taken mid-anomaly resumes it.
        if (_failureAnnounced && !_geminiThrusterIsolated)
        {
            vessel.DisturbanceTorqueBody = new Vector3d(0.0, Gemini8FlightProfile.StuckThrusterTorqueNm, 0.0);
            vessel.DisturbanceMassFlowKgPerS = Gemini8FlightProfile.StuckThrusterMassFlowKgPerS;
        }
        _adapterSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("gemini_equipment_adapter"));
        _retroComplete =
            elapsedSeconds >= Gemini8FlightProfile.RetrofireSeconds;
        _entryAnnounced = MissionManager.Instance?.InDescent == true;
        _chutesDeployed = vessel.HasDeployedParachute;
        _orbitWarpArmed = bridge.WarpIndex >= 6;
        vessel.SASEnabled = false;
        bridge.EnsureGemini8AgenaTarget();
        GD.Print(
            $"[HISTORICAL] Gemini VIII sequence resumed at "
            + $"T+{elapsedSeconds:F1}s.");
    }

    private void ResumeApollo8(double elapsedSeconds)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;
        _active = true;
        _atlasProfile = false;
        _geminiProfile = false;
        _apolloProfile = true;
        _apollo11Tde = false;
        // The launch window is not persisted: a resumed ascent keeps the scripted fallback.
        _apolloLaunchWindow = null;
        ResetApolloReturn();
        _startTime = bridge.Universe.CurrentTime
            - System.Math.Max(0.0, elapsedSeconds);
        _apolloSicSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("sic_engine_cluster"));
        _apolloSiiSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("sii_engine_cluster"));
        _towerJettisoned = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("launch_escape_system"));
        _apolloParkingOrbit =
            elapsedSeconds >= Apollo8FlightProfile.ParkingOrbitInsertionSeconds;
        _apolloTliComplete =
            elapsedSeconds >= Apollo8FlightProfile.TliCutoffSeconds;
        // A save taken mid-injection resumes by re-planning and re-igniting from the
        // current state; the finite burn object is not persisted.
        _apolloTliStarted = _apolloTliComplete;
        _apolloTliBurn = null;
        _apolloMidcourseBurn = null;
        _apolloLunarBurn = null;
        _apolloMidcourseComplete = elapsedSeconds >= Apollo8FlightProfile.LoiIgnitionSeconds;
        _apolloCsmSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("csm_separation"));
        _apolloLoiComplete =
            elapsedSeconds >= Apollo8FlightProfile.LoiCutoffSeconds;
        _apolloCircularized =
            elapsedSeconds >= Apollo8FlightProfile.CircularizationCutoffSeconds;
        _apolloTeiComplete =
            elapsedSeconds >= Apollo8FlightProfile.TeiCutoffSeconds;
        _apolloSmSeparated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("service_module"));
        _apolloTliStartTime = _startTime
            + Apollo8FlightProfile.TliIgnitionSeconds;
        _apolloLoiTime = _startTime
            + Apollo8FlightProfile.LoiIgnitionSeconds;
        vessel.SASEnabled = false;
        GD.Print(
            $"[HISTORICAL] Apollo 8 sequence resumed at "
            + $"T+{elapsedSeconds:F1}s.");
    }

    private void ResumeApollo11(double elapsedSeconds)
    {
        ResumeApollo8(elapsedSeconds);
        _apollo11Tde = true;
        // This slice ends at the lunar landing — never TEI / Earth return.
        _apolloTeiComplete = true;
        _apolloSmSeparated = true;
        _apollo11EagleExtracted = SimulationBridge.Instance?.Universe.Vessels
            .Any(v => v.Id == Apollo11FlightProfile.EagleVesselId) == true;
        _docked = SimulationBridge.Instance?.Universe.DockingConnections
            .Any(c => c.Id == Apollo11FlightProfile.DockingConnectionId) == true;

        var phase = MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH;
        var bridge = SimulationBridge.Instance;
        if (!_docked && phase == MissionPhase.LUNAR_ORBIT
            && bridge?.ActiveVessel?.Id == Apollo11FlightProfile.EagleVesselId)
        {
            // Saved after undocking: Eagle flies on, DOI is skipped if already flown.
            _docked = true;
            _apolloLoiComplete = true;
            _apolloCircularized = true;
            _apollo11UndockTime = bridge.Universe.CurrentTime - Apollo11FlightProfile.DoiStandoffSeconds;
            _apollo11Descent = new LunarPoweredDescent();
        }
        else if (!_docked)
        {
            _apolloLoiComplete = false;
            _apolloCircularized = false;
        }
        else
        {
            _apolloLoiComplete = phase is MissionPhase.LOI
                or MissionPhase.LUNAR_ORBIT
                or MissionPhase.TEI
                or MissionPhase.ENTRY
                or MissionPhase.LANDED;
            _apolloCircularized = phase is MissionPhase.LUNAR_ORBIT
                or MissionPhase.TEI
                or MissionPhase.ENTRY
                or MissionPhase.LANDED;
        }

        GD.Print(
            $"[HISTORICAL] Apollo 11 TD&E/LOI/descent sequence resumed at "
            + $"T+{elapsedSeconds:F1}s.");
    }

    public override void _Process(double delta)
    {
        // Powered and held phases run on the simulation's fixed control clock (see
        // AdvancePhysicsStep); long coasts are sequenced here at render cadence.
        if (!_active || RequiresFixedPhysicsCadence) return;
        StepProfile();
    }

    /// <summary>
    /// True while thrust, a held pad or an attitude command is in play: guidance must then
    /// refresh at every 20 ms control boundary instead of at the render frame rate.
    /// </summary>
    public bool RequiresFixedPhysicsCadence =>
        _active && SimulationBridge.Instance?.ActiveVessel is { } vessel
        && (vessel.IsGroundHeld || vessel.Throttle > 1e-3
            || vessel.PitchYawRoll.MagnitudeSquared > 1e-6
            || vessel.TranslationCommand.MagnitudeSquared > 1e-12);

    /// <summary>Advances the profile at a simulation control boundary, before integration.</summary>
    public void AdvancePhysicsStep()
    {
        if (_active) StepProfile();
    }

    private void StepProfile()
    {
        if (!_active) return;
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || vessel == null || universe == null)
        {
            _active = false;
            return;
        }

        var earth = universe.GetBody("earth");
        if (earth == null) return;
        // The director owns commit-to-launch for historical profiles, with the same gate
        // as [G] and manual ignition: hold-downs release only once thrust can lift the
        // stack at near-full throttle. Mission elapsed time starts at that release.
        if (vessel.IsGroundHeld)
        {
            vessel.Throttle = 1.0;
            if (!HoldDownReleasePolicy.CanRelease(vessel.GetThrustToWeightRatio(earth), vessel.Throttle))
                return;
            vessel.ReleaseGroundHold();
            _startTime = universe.CurrentTime;
            MissionManager.Instance?.NotifyHoldDownReleased();
            GD.Print("[HISTORICAL] Hold-down release; liftoff.");
        }
        double elapsed = universe.CurrentTime - _startTime;
        if (_apolloProfile)
        {
            ProcessApollo8(bridge, vessel, earth, elapsed);
            return;
        }
        if (_geminiProfile)
        {
            ProcessGemini8(bridge, vessel, earth, elapsed);
            return;
        }
        if (_atlasProfile)
        {
            ProcessMercuryAtlas(bridge, vessel, earth, elapsed);
            return;
        }
        double altitude = vessel.GetAltitude(earth);
        Vector3d up = earth.GetGeodeticUp(vessel.Position);
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_meco)
        {
            double elevation = MercuryRedstoneFlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d target = AttitudeGuidance.AimFromElevation(
                up, downrange, elevation);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                target,
                vessel.AngularVelocity,
                proportionalGain: 2.8,
                dampingGain: 1.25);
            vessel.Throttle = 1.0;

            if (elapsed >= MercuryRedstoneFlightProfile.MainEngineCutoffSeconds)
            {
                _meco = true;
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                MissionManager.Instance?.EnterPhase(MissionPhase.MECO);
                GD.Print("[HISTORICAL] MR-3 MECO.");
            }
            return;
        }

        if (!_separated
            && elapsed >= MercuryRedstoneFlightProfile.SpacecraftSeparationSeconds)
        {
            bridge.TriggerStaging();
            _separated = true;
            vessel = bridge.ActiveVessel;
            MissionManager.Instance?.EnterPhase(MissionPhase.SEPARATION);
        }

        if (!_towerJettisoned
            && elapsed >= MercuryRedstoneFlightProfile.EscapeTowerJettisonSeconds)
        {
            string? towerId = vessel?.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole("escape_tower"))?.InstanceId;
            if (towerId != null)
                bridge.DeployPartAsVessel(
                    towerId,
                    "Freedom 7 Escape Tower",
                    new Vector3d(0.0, 1.5, 0.0));
            _towerJettisoned = true;
            vessel = bridge.ActiveVessel;
        }

        if (vessel == null) return;
        altitude = vessel.GetAltitude(earth);
        up = (vessel.Position - earth.Position).Normalized;
        Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(earth);
        double radialSpeed = surfaceVelocity.Dot(up);
        double retroEnd = MercuryRedstoneFlightProfile.RetroSequenceSeconds
            + MercuryRedstoneFlightProfile.RetroBurnDurationSeconds;
        if (_towerJettisoned)
        {
            Vector3d heatShieldTarget = -surfaceVelocity.Normalized;
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                heatShieldTarget,
                vessel.AngularVelocity,
                proportionalGain: 2.4,
                dampingGain: 1.2);
        }

        if (!_retroComplete
            && elapsed >= MercuryRedstoneFlightProfile.RetroSequenceSeconds)
        {
            if (elapsed < retroEnd)
                vessel.Throttle = 1.0;
            else
            {
                vessel.Throttle = 0.0;
                _retroComplete = true;
                GD.Print("[HISTORICAL] Freedom 7 retrograde sequence complete.");
            }
        }

        if (!_entryAnnounced && radialSpeed < 0.0 && altitude < 100_000.0)
        {
            _entryAnnounced = true;
            bridge.SetWarpIndex(0);
            MissionManager.Instance?.EnterPhase(MissionPhase.ENTRY);
        }
        else if (!_entryAnnounced && elapsed > 180.0)
        {
            bridge.SetWarpIndex(2);
            MissionManager.Instance?.EnterPhase(MissionPhase.COAST);
        }

        if (_entryAnnounced && altitude < 25_000.0)
            MissionManager.Instance?.EnterPhase(MissionPhase.AERO_DESCENT);

        if (!_chutesDeployed
            && radialSpeed < 0.0
            && altitude <= MercuryRedstoneFlightProfile.DrogueDeployAltitudeM)
        {
            _chutesDeployed = vessel.DeployParachutes(earth) > 0;
            if (_chutesDeployed)
            {
                vessel.PitchYawRoll = Vector3d.Zero;
                GD.Print("[HISTORICAL] Freedom 7 parachute sequence armed.");
            }
        }

        if (altitude <= 1.1
            && !vessel.IsDestroyed
            && vessel.GetSurfaceVelocity(earth).Magnitude < 1.0)
        {
            vessel.PitchYawRoll = Vector3d.Zero;
            MissionManager.Instance?.EnterPhase(MissionPhase.LANDED);
            _active = false;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Freedom 7 splashdown.");
        }
    }

    /// <summary>
    /// Warp for guided spacecraft burns: they run on the fixed 20 ms control clock, so x10
    /// (the bridge's cap while thrusting in vacuum) shortens only the wall time of a TLI or
    /// LOI, never the flown burn.
    /// </summary>
    private const int BurnWarpIndex = 4;

    private string SaturnLabel => _apollo11Tde ? "AS-506" : "AS-503";
    private string ApolloLabel => _apollo11Tde ? "Apollo 11" : "Apollo 8";

    private void ProcessApollo8(
        SimulationBridge bridge,
        Vessel vessel,
        CelestialBody earth,
        double elapsed)
    {
        var universe = bridge.Universe;
        var moon = universe.GetBody("moon");
        if (moon == null) return;

        CelestialBody dominant = universe.GetDominantBody(vessel.Position);
        var ascent = _apollo11Tde ? SaturnVAscentSchedule.As506 : SaturnVAscentSchedule.As503;
        // A planned window fixes the parking-orbit plane: downrange is the in-plane horizontal.
        Vector3d? launchPlane = _apolloLaunchWindow?.PlaneNormal;
        Vector3d downrange = launchPlane is { } plane
            ? plane.Cross(earth.GetGeodeticUp(vessel.Position)).Normalized
            : bridge.GetLaunchHeadingDirection();

        if (!_apolloSicSeparated)
        {
            // Pitch-over then zero-AoA gravity turn; the open-loop elevation program paid
            // ~660 m/s of steering loss on the S-IC.
            Vector3d boostAim = GravityTurnGuidance.ComputeAim(vessel, earth, downrange, elapsed,
                ascent.PitchOverDegrees, Apollo8FlightProfile.PitchOverEndSeconds);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation, Vector3d.Up, boostAim, vessel.AngularVelocity,
                proportionalGain: 2.3, dampingGain: 1.2);
            var sicEngines = vessel.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole("sic_engine_cluster"));
            sicEngines?.SelectEngineCount(
                elapsed >= ascent.SicCenterEngineCutoffSeconds
                    ? 4 : 5);
            if (double.IsNaN(_apolloSicCutoffElapsed) && ascent.IsSicCutoff(vessel, elapsed))
                _apolloSicCutoffElapsed = elapsed;
            vessel.Throttle = double.IsNaN(_apolloSicCutoffElapsed) ? 1.0 : 0.0;
            if (elapsed >= _apolloSicCutoffElapsed + ascent.SicSeparationDelaySeconds)
            {
                bridge.TriggerStaging();
                _apolloSicSeparated = true;
                vessel = bridge.ActiveVessel ?? vessel;
                vessel.Throttle = 1.0;
                MissionManager.Instance?.EnterPhase(MissionPhase.SEPARATION);
                GD.Print($"[HISTORICAL] {SaturnLabel} S-IC separation; S-II ignition.");
            }
            return;
        }

        if (!_apolloSiiSeparated)
        {
            GuideApolloInsertion(vessel, earth, downrange, launchPlane);
            vessel.Parts.Parts.FirstOrDefault(part => part.Definition.HasVehicleRole("sii_engine_cluster"))
                ?.SelectEngineCount(ascent.SiiEngineCount(elapsed));
            vessel.Throttle = elapsed
                < ascent.SiiCutoffSeconds ? 1.0 : 0.0;
            if (!_towerJettisoned
                && elapsed >= ascent.EscapeTowerJettisonSeconds)
            {
                string? towerId = vessel.Parts.Parts.FirstOrDefault(part =>
                    part.Definition.HasVehicleRole(
                        "launch_escape_system"))?.InstanceId;
                if (towerId != null)
                    bridge.DeployPartAsVessel(
                        towerId,
                        "Apollo 8 Launch Escape System",
                        new Vector3d(0.0, 1.5, 0.0));
                _towerJettisoned = true;
                vessel = bridge.ActiveVessel ?? vessel;
                GD.Print($"[HISTORICAL] {ApolloLabel} launch escape tower jettisoned.");
            }
            if (ascent.IsSiiSeparationDue(vessel, earth, elapsed))
            {
                bridge.TriggerStaging();
                _apolloSiiSeparated = true;
                vessel = bridge.ActiveVessel ?? vessel;
                vessel.Throttle = 1.0;
                GD.Print($"[HISTORICAL] {SaturnLabel} S-II separation; S-IVB ignition.");
            }
            return;
        }

        if (!_apolloParkingOrbit)
        {
            // Closed-loop S-IVB insertion: cut off once the flown orbit clears the
            // atmosphere, not at the historical clock.
            GuideApolloInsertion(vessel, earth, downrange, launchPlane);
            vessel.Throttle = 1.0;
            if (ClosedLoopInsertionGuidance.IsInserted(
                    vessel, earth, ApolloParkingPeriapsisAltitudeM, universe.CurrentTime))
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                _apolloParkingOrbit = true;
                MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
                // A windowed launch flies straight into the lunar plane: TLI is planned from
                // the flown orbit. Without a window (resumed ascent, no window in 24 h) the
                // flown plane is rotated into the lunar plane by script, and says so.
                _apolloLunarPlan = _apolloLaunchWindow != null
                    ? TryPlanApolloLunarTransfer(universe, vessel, earth, moon)
                    : null;
                if (_apolloLunarPlan == null)
                {
                    GD.PrintErr("[HISTORICAL] Parking orbit outside the lunar plane; "
                        + "applying the scripted plane correction.");
                    PlaceInEarthParkingOrbit(
                        vessel, earth, moon, downrange, universe.CurrentTime);
                    _apolloLunarPlan = PlanApolloLunarTransfer(
                        universe,
                        vessel,
                        earth,
                        moon,
                        _startTime + Apollo8FlightProfile.TliIgnitionSeconds);
                }
                bridge.SetWarpIndex(8);
                GD.Print(
                    $"[HISTORICAL] {ApolloLabel} Earth parking orbit; "
                    + $"TLI solution loaded ({_apolloLunarPlan.InjectionDeltaVMag:F0} m/s).");
            }
            return;
        }

        if (!_apolloTliStarted)
        {
            _apolloLunarPlan ??= PlanApolloLunarTransfer(
                universe,
                vessel,
                earth,
                moon,
                _startTime + Apollo8FlightProfile.TliIgnitionSeconds);
            // Finite S-IVB burn centred on the planned impulse and cut at the planned
            // transfer energy; the mid-course correction removes the remaining aim error.
            double halfBurn = 0.5 * vessel.TotalMass * _apolloLunarPlan.InjectionDeltaVMag
                / System.Math.Max(1.0, vessel.GetMaximumThrust(earth));
            double ignitionTime = _apolloLunarPlan.BurnTime - halfBurn;
            if (universe.CurrentTime < ignitionTime - 180.0)
            {
                bridge.SetWarpIndex(8);
                return;
            }
            bridge.SetWarpIndex(0);
            vessel.ReferenceBodyId = earth.Id;
            _apolloTliBurn = new ProgradeEnergyBurn(
                -earth.GM / (2.0 * _apolloLunarPlan.EarthTransferOrbit.SemiMajorAxis),
                ignitionTime, maximumBurnSeconds: 900.0);
            _apolloGuidedBurnLastTime = universe.CurrentTime;
            _apolloTliStartTime = ignitionTime;
            _apolloTliStarted = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.TLI);
            GD.Print(
                $"[HISTORICAL] {ApolloLabel} TLI ignition; planned Δv "
                + $"{_apolloLunarPlan.InjectionDeltaVMag:F0} m/s.");
            return;
        }

        if (!_apolloTliComplete)
        {
            bridge.SetWarpIndex(BurnWarpIndex);
            vessel.IsOnRails = false;
            vessel.OrbitalState = null;
            _apolloTliBurn!.Advance(vessel, earth, GuidedBurnStep(universe), universe.CurrentTime);
            if (!_apolloTliBurn.IsFinished) return;

            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            if (_apolloTliBurn.Status == ProgradeEnergyBurnStatus.Blocked)
                GD.PrintErr($"[HISTORICAL] TLI stopped: {_apolloTliBurn.BlockReason}");
            _apolloTliComplete = true;
            bridge.SetWarpIndex(8);
            GD.Print($"[HISTORICAL] Apollo TLI cutoff after {_apolloTliBurn.BurnSeconds:F1} s; translunar coast.");
            return;
        }

        if (!_apolloCsmSeparated
            && universe.CurrentTime - _apolloTliStartTime
                >= Apollo8FlightProfile.CsmSivbSeparationSeconds
                   - Apollo8FlightProfile.TliIgnitionSeconds)
        {
            bridge.SetWarpIndex(0);
            bridge.TriggerStaging();
            _apolloCsmSeparated = true;
            vessel = bridge.ActiveVessel ?? vessel;
            vessel.Throttle = 0.0;
            vessel.IsOnRails = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.LUNAR_APPROACH);
            if (_apollo11Tde)
            {
                bridge.SetWarpIndex(0);
                GD.Print("[HISTORICAL] CSM-107 separated from S-IVB/SLA; TD&E begins.");
            }
            else
            {
                bridge.SetWarpIndex(9);
                GD.Print("[HISTORICAL] CSM-103 separated from S-IVB/LTA-B.");
            }
        }
        if (!_apolloCsmSeparated)
        {
            // Re-assert every frame: right after cutoff the spooling J-2 briefly limits warp,
            // and a one-shot request left the post-TLI coast at x3.
            bridge.SetWarpIndex(8);
            return;
        }

        if (_apollo11Tde)
        {
            if (!_docked)
            {
                ProcessApollo11TranspositionAndDocking(bridge, vessel, earth);
                return;
            }
            if (_apolloCircularized)
            {
                ProcessApollo11Descent(bridge);
                return;
            }
            ProcessApollo11LunarOrbitInsertion(bridge, vessel);
            return;
        }

        if (!_apolloMidcourseComplete && !RunApolloMidcourse(bridge, vessel, earth))
            return;

        dominant = universe.GetDominantBody(vessel.Position);
        if (!_apolloLoiComplete)
        {
            if (dominant.Id != moon.Id)
            {
                bridge.SetWarpIndex(9);
                return;
            }

            if (!_apolloTranslunarTrimDone)
            {
                RunApolloTranslunarTrim(bridge, vessel, moon);
                return;
            }

            double altitude = vessel.GetAltitude(moon);
            if (altitude > 600_000.0)
            {
                // Approach from the sphere-of-influence edge: a fixed x50 spent ~20 min of
                // wall time here; scale the warp down as perilune nears instead.
                double untilPerilune = PeriapsisCaptureBurn.TimeToPeriapsis(
                    vessel.Position - moon.Position, vessel.Velocity - moon.Velocity, moon.GM);
                bridge.SetWarpIndex(double.IsNaN(untilPerilune) || untilPerilune < 3_600.0 ? 5
                    : untilPerilune < 6.0 * 3_600.0 ? 7 : 8);
                return;
            }
            if (!RunApolloLunarBurn(bridge, vessel, moon,
                    Apollo8FlightProfile.InitialLunarApoluneAltitudeM, maximumBurnSeconds: 600.0))
                return;
            _apolloLoiComplete = true;
            _apolloLoiTime = universe.CurrentTime;
            MissionManager.Instance?.EnterPhase(MissionPhase.LOI);
            bridge.SetWarpIndex(8);
            GD.Print(
                "[HISTORICAL] Apollo 8 LOI complete; finite SPS burn into lunar orbit.");
            return;
        }

        if (!_apolloCircularized)
        {
            double circularizationDelay =
                Apollo8FlightProfile.CircularizationIgnitionSeconds
                - Apollo8FlightProfile.LoiIgnitionSeconds;
            if (universe.CurrentTime - _apolloLoiTime
                < circularizationDelay)
            {
                bridge.SetWarpIndex(8);
                return;
            }
            if (!RunApolloLunarBurn(bridge, vessel, moon,
                    Apollo8FlightProfile.CircularLunarOrbitAltitudeM, maximumBurnSeconds: 120.0))
                return;
            _apolloCircularized = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.LUNAR_ORBIT);
            bridge.SetWarpIndex(9);
            GD.Print("[HISTORICAL] Apollo 8 lunar orbit circularized.");
            return;
        }

        if (!_apolloTeiComplete)
        {
            // Campaign evidence counts revolutions only when a campaign director runs; a sandbox
            // or harness flight had none and waited in lunar orbit forever. Count from LOI too.
            var lunarState = OrbitalElements.FromStateVector(vessel.Position - moon.Position,
                vessel.Velocity - moon.Velocity, moon.GM, moon.Id, universe.CurrentTime);
            double lunarPeriod = 2.0 * System.Math.PI
                * System.Math.Sqrt(System.Math.Pow(lunarState.SemiMajorAxis, 3.0) / moon.GM);
            double lunarOrbits = System.Math.Max(
                CampaignRuntime.Instance?.Director?.Evidence.CompletedLunarOrbits ?? 0.0,
                (universe.CurrentTime - _apolloLoiTime) / lunarPeriod);
            if (lunarOrbits + 1e-6
                < Apollo8FlightProfile.HistoricalLunarRevolutions)
            {
                bridge.SetWarpIndex(9);
                return;
            }

            if (!RunApolloTransearthInjection(bridge, vessel, earth, moon))
                return;
            _apolloTeiComplete = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.TEI);
            bridge.SetWarpIndex(9);
            return;
        }

        dominant = universe.GetDominantBody(vessel.Position);
        if (dominant.Id != earth.Id)
        {
            bridge.SetWarpIndex(9);
            return;
        }

        // Transearth corrections to the entry-corridor perigee on TransearthCorrection's
        // schedule: one after leaving the Moon's sphere of influence, then trims 8 h and 2 h out.
        if (_apolloTransearthCorrections < TransearthCorrection.CorrectionCount && !_apolloSmSeparated)
        {
            RunApolloTransearthCorrection(bridge, vessel, earth);
            return;
        }

        double earthAltitude = vessel.GetAltitude(earth);
        Vector3d returnUp = (vessel.Position - earth.Position).Normalized;
        Vector3d returnVelocity = vessel.Velocity - earth.Velocity;
        double returnRadialSpeed = returnVelocity.Dot(returnUp);
        if (earthAltitude > 5_000_000.0)
        {
            bridge.SetWarpIndex(9);
            return;
        }
        if (earthAltitude > 800_000.0)
        {
            bridge.SetWarpIndex(5);
            return;
        }

        bridge.SetWarpIndex(0);
        if (!_apolloSmSeparated && returnRadialSpeed < 0.0)
        {
            string? serviceModuleId = vessel.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole("service_module"))?.InstanceId;
            if (serviceModuleId != null)
                bridge.DeployPartAsVessel(
                    serviceModuleId,
                    "Apollo 8 Service Module",
                    new Vector3d(0.0, -0.5, 0.0));
            _apolloSmSeparated = true;
            vessel = bridge.ActiveVessel ?? vessel;
            GD.Print("[HISTORICAL] Apollo 8 CM/SM separation.");
        }

        // The CM is aerodynamically stable at its trim angle; reaction control holds the trim
        // attitude before the air bites and then only banks the lift vector.
        if (_apolloSmSeparated)
            vessel.PitchYawRoll = CapsuleEntryGuidance.ComputeCommand(vessel, earth);
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        vessel.ReferenceBodyId = earth.Id;

        if (returnRadialSpeed < 0.0
            && earthAltitude <= Apollo8FlightProfile.EntryInterfaceAltitudeM)
            MissionManager.Instance?.EnterPhase(MissionPhase.ENTRY);
        if (earthAltitude < 25_000.0)
            MissionManager.Instance?.EnterPhase(MissionPhase.AERO_DESCENT);
        if (!_chutesDeployed
            && returnRadialSpeed < 0.0
            && earthAltitude <= 7_300.0)
        {
            _chutesDeployed = vessel.DeployParachutes(earth) > 0;
            if (_chutesDeployed)
                GD.Print("[HISTORICAL] Apollo 8 parachute sequence armed.");
        }
        if (earthAltitude <= 1.1
            && !vessel.IsDestroyed
            && vessel.GetSurfaceVelocity(earth).Magnitude < 1.0)
        {
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.PitchYawRoll = Vector3d.Zero;
            MissionManager.Instance?.EnterPhase(MissionPhase.LANDED);
            _active = false;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Apollo 8 Pacific splashdown.");
        }
    }

    private const double ApolloParkingPeriapsisAltitudeM = 180_000.0;

    /// <summary>S-II/S-IVB closed-loop steering toward the parking-orbit altitude.</summary>
    private static void GuideApolloInsertion(Vessel vessel, CelestialBody earth, Vector3d downrange,
        Vector3d? launchPlane)
    {
        Vector3d desired = ClosedLoopInsertionGuidance.ComputeAim(
            vessel, earth, Apollo8FlightProfile.ParkingOrbitAltitudeM, downrange, launchPlane);
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation,
            Vector3d.Up,
            desired,
            vessel.AngularVelocity,
            proportionalGain: 2.3,
            dampingGain: Apollo8FlightProfile.UpperStageDampingGain(
                vessel.GetPitchYawAngularAuthority(earth)));
    }

    private static void PlaceInEarthParkingOrbit(
        Vessel vessel,
        CelestialBody earth,
        CelestialBody moon,
        Vector3d requestedTangent,
        double epoch)
    {
        Vector3d up = (vessel.Position - earth.Position).Normalized;
        Vector3d lunarPlaneNormal = (
            moon.Position - earth.Position).Cross(
                moon.Velocity - earth.Velocity).Normalized;
        Vector3d planeUp = (
            up - lunarPlaneNormal * up.Dot(lunarPlaneNormal)).Normalized;
        if (planeUp.MagnitudeSquared > 1e-9)
            up = planeUp;
        Vector3d tangent = lunarPlaneNormal.Cross(up).Normalized;
        if (tangent.MagnitudeSquared < 1e-9)
            tangent = (
                requestedTangent - up * requestedTangent.Dot(up)).Normalized;
        double radius =
            earth.Radius + Apollo8FlightProfile.ParkingOrbitAltitudeM;
        vessel.Position = earth.Position + up * radius;
        vessel.Velocity = earth.Velocity
            + tangent * System.Math.Sqrt(earth.GM / radius);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.IsOnRails = true;
        vessel.OrbitalState = OrbitalElements.FromStateVector(
            vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            epoch);
        vessel.ReferenceBodyId = earth.Id;
        vessel.Throttle = 0.0;
    }

    private LunarTransferPlan? TryPlanApolloLunarTransfer(
        Universe universe, Vessel vessel, CelestialBody earth, CelestialBody moon)
    {
        try
        {
            return PlanApolloLunarTransfer(universe, vessel, earth, moon,
                _startTime + Apollo8FlightProfile.TliIgnitionSeconds);
        }
        catch (InvalidOperationException error)
        {
            GD.PrintErr($"[HISTORICAL] TLI from the flown orbit failed: {error.Message}");
            return null;
        }
    }

    private static LunarTransferPlan PlanApolloLunarTransfer(
        Universe universe,
        Vessel vessel,
        CelestialBody earth,
        CelestialBody moon,
        double historicalTliTime)
    {
        if (moon.OrbitalElements == null)
            throw new InvalidOperationException(
                "Apollo 8 requires the offline lunar ephemeris.");
        OrbitalElements parking = OrbitalElements.FromStateVector(
            vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            universe.CurrentTime);
        double historicalTimeOfFlight =
            Apollo8FlightProfile.LoiIgnitionSeconds
            - Apollo8FlightProfile.TliIgnitionSeconds;
        LunarTransferPlan plan = LunarTransferPlanner.Compute(
            earth.GM,
            moon.GM,
            moon.Radius,
            moon.SphereOfInfluence,
            parking,
            moon.OrbitalElements,
            System.Math.Max(
                universe.CurrentTime + 60.0,
                historicalTliTime),
            historicalTimeOfFlight,
            Apollo8FlightProfile.InitialLunarPeriluneAltitudeM,
            windowSamples: 60);
        if (plan.InjectionDeltaVMag > 4_500.0)
            throw new InvalidOperationException(
                $"Apollo 8 TLI solution exceeds the public mission envelope "
                + $"({plan.InjectionDeltaVMag:F0} m/s).");
        return plan;
    }

    /// <summary>
    /// Translunar trim (MCC-4) inside the Moon's sphere of influence: a periapsis correction of
    /// the Moon-centred hyperbola to the LOI perilune, once perilune is close enough.
    /// </summary>
    private void RunApolloTranslunarTrim(SimulationBridge bridge, Vessel vessel, CelestialBody moon)
    {
        var universe = bridge.Universe;
        if (_apolloTranslunarTrim == null)
        {
            if (!TranslunarCorrection.IsTrimDue(universe, vessel, moon))
            {
                // Already past perilune (no periapsis ahead): nothing left to trim; LOI decides.
                if (universe.GetDominantBody(vessel.Position) == moon && double.IsNaN(
                        PeriapsisCaptureBurn.TimeToPeriapsis(vessel.Position - moon.Position,
                            vessel.Velocity - moon.Velocity, moon.GM)))
                {
                    _apolloTranslunarTrimDone = true;
                    return;
                }
                bridge.SetWarpIndex(8);
                return;
            }
            bridge.SetWarpIndex(0);
            _apolloTranslunarTrim = TranslunarCorrection.CreateTrim(vessel, moon, universe.CurrentTime,
                moon.Radius + Apollo8FlightProfile.InitialLunarPeriluneAltitudeM);
            _apolloGuidedBurnLastTime = universe.CurrentTime;
        }
        bridge.SetWarpIndex(BurnWarpIndex);
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        _apolloTranslunarTrim.Advance(vessel, moon, GuidedBurnStep(universe), universe.CurrentTime);
        if (!_apolloTranslunarTrim.IsFinished) return;

        if (_apolloTranslunarTrim.Status == LambertGuidedBurnStatus.Blocked)
            GD.PrintErr($"[HISTORICAL] Translunar trim stopped: {_apolloTranslunarTrim.BlockReason}");
        var approach = OrbitalElements.FromStateVector(vessel.Position - moon.Position,
            vessel.Velocity - moon.Velocity, moon.GM, moon.Id, universe.CurrentTime);
        GD.Print($"[HISTORICAL] Translunar trim {_apolloTranslunarTrim.BurnSeconds:F1} s, perilune "
            + $"{(approach.Periapsis - moon.Radius) / 1000.0:F1} km.");
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        _apolloTranslunarTrim = null;
        _apolloTranslunarTrimDone = true;
    }

    private void ResetApolloReturn()
    {
        _apolloTranslunarTrim = null;
        _apolloTranslunarTrimDone = false;
        _apolloTeiBurn = null;
        _apolloTeiIgnitionTime = 0.0;
        _apolloTransearthBurn = null;
        _apolloTransearthCorrections = 0;
        _apolloEarthReturnTime = double.NaN;
    }

    /// <summary>
    /// Transearth injection as a finite prograde SPS burn cut on the planned Moon-relative
    /// energy (<see cref="TransearthInjectionPlanner"/>). Returns true once it has finished.
    /// </summary>
    private bool RunApolloTransearthInjection(SimulationBridge bridge, Vessel vessel,
        CelestialBody earth, CelestialBody moon)
    {
        var universe = bridge.Universe;
        if (_apolloTeiBurn == null)
        {
            var lunarOrbit = OrbitalElements.FromStateVector(vessel.Position - moon.Position,
                vessel.Velocity - moon.Velocity, moon.GM, moon.Id, universe.CurrentTime);
            var plan = TransearthInjectionPlanner.Compute(earth.GM, moon.GM, moon.OrbitalElements!,
                lunarOrbit, universe.CurrentTime + 600.0, Apollo8FlightProfile.TransearthTimeOfFlightSeconds,
                earth.Radius + Apollo8FlightProfile.EntryCorridorVacuumPerigeeAltitudeM,
                maximumTimeOfFlight: Apollo8FlightProfile.MaximumTransearthTimeOfFlightSeconds);
            double halfBurn = 0.5 * vessel.TotalMass * plan.ImpulsiveDeltaV
                / System.Math.Max(1.0, vessel.GetMaximumThrust(moon));
            _apolloTeiIgnitionTime = plan.BurnTime - halfBurn;
            _apolloTeiBurn = new ProgradeEnergyBurn(plan.TargetSelenocentricEnergy,
                _apolloTeiIgnitionTime, maximumBurnSeconds: 600.0);
            GD.Print($"[HISTORICAL] Apollo TEI planned: {plan.ImpulsiveDeltaV:F0} m/s, ignition in "
                + $"{(_apolloTeiIgnitionTime - universe.CurrentTime) / 60.0:F0} min.");
        }
        double untilIgnition = _apolloTeiIgnitionTime - universe.CurrentTime;
        if (_apolloTeiBurn.Status == ProgradeEnergyBurnStatus.WaitingForIgnition && untilIgnition > 300.0)
        {
            bridge.SetWarpIndex(untilIgnition > 1_200.0 ? 6 : 4);
            _apolloGuidedBurnLastTime = universe.CurrentTime;
            return false;
        }
        bridge.SetWarpIndex(BurnWarpIndex);
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        _apolloTeiBurn.Advance(vessel, moon, GuidedBurnStep(universe), universe.CurrentTime);
        if (!_apolloTeiBurn.IsFinished) return false;

        if (_apolloTeiBurn.Status == ProgradeEnergyBurnStatus.Blocked)
            GD.PrintErr($"[HISTORICAL] TEI stopped: {_apolloTeiBurn.BlockReason}");
        GD.Print($"[HISTORICAL] Apollo TEI cutoff after {_apolloTeiBurn.BurnSeconds:F1} s; transearth coast.");
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        _apolloTeiBurn = null;
        return true;
    }

    /// <summary>Flies the next transearth correction once it is due (see the call site).</summary>
    private void RunApolloTransearthCorrection(SimulationBridge bridge, Vessel vessel, CelestialBody earth)
    {
        var universe = bridge.Universe;
        var relative = vessel.Position - earth.Position;
        var velocity = vessel.Velocity - earth.Velocity;
        if (_apolloTransearthBurn == null)
        {
            if (double.IsNaN(_apolloEarthReturnTime)) _apolloEarthReturnTime = universe.CurrentTime;
            double timeToPerigee = PeriapsisCaptureBurn.TimeToPeriapsis(relative, velocity, earth.GM);
            if (!TransearthCorrection.IsDue(_apolloTransearthCorrections,
                    universe.CurrentTime - _apolloEarthReturnTime, timeToPerigee))
            {
                bridge.SetWarpIndex(8);
                return;
            }
            bridge.SetWarpIndex(0);
            _apolloTransearthBurn = TransearthCorrection.Create(vessel, earth, universe.CurrentTime,
                earth.Radius + Apollo8FlightProfile.EntryCorridorVacuumPerigeeAltitudeM,
                TransearthCorrection.CutoffDeltaVFor(_apolloTransearthCorrections));
            _apolloGuidedBurnLastTime = universe.CurrentTime;
        }
        bridge.SetWarpIndex(BurnWarpIndex);
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        _apolloTransearthBurn.Advance(vessel, earth, GuidedBurnStep(universe), universe.CurrentTime);
        if (!_apolloTransearthBurn.IsFinished) return;

        if (_apolloTransearthBurn.Status == LambertGuidedBurnStatus.Blocked)
            GD.PrintErr($"[HISTORICAL] Transearth correction stopped: {_apolloTransearthBurn.BlockReason}");
        var corrected = OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        GD.Print($"[HISTORICAL] Transearth correction {_apolloTransearthCorrections + 1}: "
            + $"{_apolloTransearthBurn.BurnSeconds:F1} s, vacuum perigee "
            + $"{(corrected.Periapsis - earth.Radius) / 1000.0:F1} km.");
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        _apolloTransearthBurn = null;
        _apolloTransearthCorrections++;
        bridge.SetWarpIndex(8);
    }

    /// <summary>Simulation seconds since the previous guided-burn update (paused frames: one control period).</summary>
    private double GuidedBurnStep(Universe universe)
    {
        double step = universe.CurrentTime - _apolloGuidedBurnLastTime;
        _apolloGuidedBurnLastTime = universe.CurrentTime;
        return step > 1e-9 ? step : Universe.DeterministicControlPeriodSeconds;
    }

    /// <summary>
    /// SPS mid-course correction toward the planned lunar aim point, flown with
    /// velocity-to-be-gained guidance two hours after the spacecraft is free of the S-IVB.
    /// Returns true once the correction has finished.
    /// </summary>
    private bool RunApolloMidcourse(SimulationBridge bridge, Vessel vessel, CelestialBody earth)
    {
        var universe = bridge.Universe;
        var plan = _apolloLunarPlan;
        if (plan == null || universe.GetDominantBody(vessel.Position) != earth)
        {
            _apolloMidcourseComplete = true;
            return true;
        }
        if (_apolloMidcourseBurn == null)
        {
            double correctionTime = _apolloTliStartTime + 2.0 * 3_600.0
                + (Apollo8FlightProfile.CsmSivbSeparationSeconds - Apollo8FlightProfile.TliIgnitionSeconds);
            if (universe.CurrentTime < correctionTime - 120.0)
            {
                bridge.SetWarpIndex(8);
                return false;
            }
            bridge.SetWarpIndex(0);
            // Aim at the perilune altitude through the predicted encounter, not at the point
            // the TLI plan estimated (that left a 211 km perilune instead of 111 km).
            var aim = plan.EarthTransferOrbit.GetStateAtTime(plan.ArrivalTime, earth.GM).position;
            var moon = universe.GetBody("moon")!;
            _apolloMidcourseBurn = TranslunarCorrection.Create(vessel, earth, moon, universe.CurrentTime,
                plan.ArrivalTime, aim, moon.Radius + Apollo8FlightProfile.InitialLunarPeriluneAltitudeM);
            _apolloGuidedBurnLastTime = universe.CurrentTime;
        }
        bridge.SetWarpIndex(BurnWarpIndex);
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        _apolloMidcourseBurn.Advance(vessel, earth, GuidedBurnStep(universe), universe.CurrentTime);
        if (!_apolloMidcourseBurn.IsFinished) return false;

        if (_apolloMidcourseBurn.Status == LambertGuidedBurnStatus.Blocked)
            GD.PrintErr($"[HISTORICAL] Mid-course correction stopped: {_apolloMidcourseBurn.BlockReason}");
        GD.Print($"[HISTORICAL] Mid-course correction {_apolloMidcourseBurn.BurnSeconds:F1} s.");
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        _apolloMidcourseBurn = null;
        _apolloMidcourseComplete = true;
        bridge.SetWarpIndex(9);
        return true;
    }

    /// <summary>
    /// Flies a lunar SPS burn with <see cref="PeriapsisCaptureBurn"/>: real-time warp, attitude
    /// and throttle commands only, and whatever orbit the integrated burn produces. Returns
    /// true once the burn has finished (completed or blocked).
    /// </summary>
    private bool RunApolloLunarBurn(SimulationBridge bridge, Vessel vessel, CelestialBody moon,
        double targetApoapsisAltitudeM, double maximumBurnSeconds)
    {
        var universe = bridge.Universe;
        // Coast to the ignition window under warp; align and burn in real time.
        double timeToPeriapsis = PeriapsisCaptureBurn.TimeToPeriapsis(
            vessel.Position - moon.Position, vessel.Velocity - moon.Velocity, moon.GM);
        bool coasting = _apolloLunarBurn?.Status is null or PeriapsisCaptureBurnStatus.Coasting;
        int warpIndex = !coasting ? BurnWarpIndex
            : double.IsNaN(timeToPeriapsis) ? 0
            : timeToPeriapsis > 1_200.0 ? 6
            : timeToPeriapsis > 300.0 ? 4
            : 0;
        bridge.SetWarpIndex(warpIndex);
        if (warpIndex <= BurnWarpIndex)
        {
            vessel.IsOnRails = false;
            vessel.OrbitalState = null;
        }
        if (_apolloLunarBurn == null)
        {
            _apolloLunarBurn = new PeriapsisCaptureBurn(targetApoapsisAltitudeM, maximumBurnSeconds);
            _apolloLunarBurnLastTime = universe.CurrentTime;
        }
        double step = universe.CurrentTime - _apolloLunarBurnLastTime;
        _apolloLunarBurnLastTime = universe.CurrentTime;
        _apolloLunarBurn.Advance(vessel, moon, step > 1e-9 ? step : Universe.DeterministicControlPeriodSeconds,
            universe.CurrentTime);
        if (!_apolloLunarBurn.IsFinished) return false;

        if (_apolloLunarBurn.Status == PeriapsisCaptureBurnStatus.Blocked)
            GD.PrintErr($"[HISTORICAL] Lunar SPS burn stopped: {_apolloLunarBurn.BlockReason}");
        GD.Print($"[HISTORICAL] Lunar SPS burn {_apolloLunarBurn.BurnSeconds:F1} s -> "
            + $"{_apolloLunarBurn.PeriapsisAltitudeM / 1000.0:F1} x "
            + $"{_apolloLunarBurn.ApoapsisAltitudeM / 1000.0:F1} km");
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        _apolloLunarBurn = null;
        return true;
    }

    private void ProcessGemini8(
        SimulationBridge bridge,
        Vessel vessel,
        CelestialBody earth,
        double elapsed)
    {
        Vessel? target = bridge.EnsureGemini8AgenaTarget();
        double altitude = vessel.GetAltitude(earth);
        Vector3d up = earth.GetGeodeticUp(vessel.Position);
        // The Agena's orbit plane: Titan flies into it and stage two steers to stay in it.
        Vector3d? agenaPlane = target == null ? null
            : (target.Position - earth.Position).Cross(target.Velocity - earth.Velocity).Normalized;
        Vector3d downrange = agenaPlane is { } plane
            ? plane.Cross(up).Normalized
            : bridge.GetLaunchHeadingDirection();

        if (!_geminiStageOneSeparated)
        {
            double elevation = Gemini8FlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d desired = AttitudeGuidance.AimFromElevation(
                up, downrange, elevation);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                desired,
                vessel.AngularVelocity,
                proportionalGain: 2.5,
                dampingGain: 1.2);
            // BECO was propellant depletion, not a clock: burn stage one out, then separate.
            vessel.Throttle = 1.0;
            if (double.IsNaN(_geminiBeco)
                && Gemini8FlightProfile.IsStageOneDepleted(vessel, earth, elapsed))
                _geminiBeco = elapsed;
            if (!double.IsNaN(_geminiBeco)
                && elapsed >= _geminiBeco + Gemini8FlightProfile.StagingDelaySeconds)
            {
                bridge.TriggerStaging();
                _geminiStageOneSeparated = true;
                vessel = bridge.ActiveVessel ?? vessel;
                vessel.Throttle = 1.0;
                MissionManager.Instance?.EnterPhase(MissionPhase.SEPARATION);
                GD.Print("[HISTORICAL] GLV-8 BECO and Stage I separation.");
            }
            return;
        }

        if (!_meco)
        {
            // Closed-loop stage-two insertion into the Agena plane; cut on the flown periapsis
            // or at burnout. The open-loop program stalled at 115 km and fell back.
            Vector3d desired = ClosedLoopInsertionGuidance.ComputeAim(vessel, earth,
                Gemini8FlightProfile.InsertionAltitudeM, downrange, agenaPlane);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                desired,
                vessel.AngularVelocity,
                proportionalGain: 2.3,
                dampingGain: Gemini8FlightProfile.StageTwoDampingGain(vessel.GetPitchYawAngularAuthority(earth)));
            vessel.Throttle = 1.0;
            double sinceBeco = double.IsNaN(_geminiBeco) ? double.PositiveInfinity : elapsed - _geminiBeco;
            if (ClosedLoopInsertionGuidance.IsInserted(vessel, earth,
                    Gemini8FlightProfile.MinimumInsertionPeriapsisM, bridge.Universe.CurrentTime)
                || Gemini8FlightProfile.IsStageTwoBurnedOut(vessel, earth, sinceBeco))
            {
                _meco = true;
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                MissionManager.Instance?.EnterPhase(MissionPhase.MECO);
                GD.Print("[HISTORICAL] GLV-8 SECO.");
            }
            return;
        }

        if (!_separated
            && elapsed >= Gemini8FlightProfile.SpacecraftSeparationSeconds)
        {
            bridge.TriggerStaging();
            _separated = true;
            vessel = bridge.ActiveVessel ?? vessel;
            vessel.SASEnabled = false;
            MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
            GD.Print("[HISTORICAL] Spacecraft 8 separated in rendezvous orbit.");
        }
        if (!_separated || target == null) return;

        // Rendezvous on OAMS translation thrusters: terminal phase initiation, a
        // mid-transfer correction, then braking and a final approach to a real capture.
        if (!_rendezvousConfigured)
        {
            RunGeminiIntercept(bridge, vessel, target, earth);
            return;
        }
        if (!_docked && !_undocked)
        {
            RunGeminiProximity(bridge, vessel, target, earth);
            if (!_docked) return;
        }

        // Docked coast to the anomaly: warp, stepping down so the failure epoch is not overshot.
        if (_docked && !_failureAnnounced)
        {
            double untilAnomaly = Gemini8FlightProfile.ThrusterAnomalySeconds - elapsed;
            bridge.SetWarpIndex(untilAnomaly > 3_600.0 ? 8 : untilAnomaly > 600.0 ? 6
                : untilAnomaly > 60.0 ? 4 : 0);
        }

        // OAMS thruster 8 stuck on: a real torque and propellant drain, not a rate profile.
        if (!_failureAnnounced
            && elapsed >= Gemini8FlightProfile.ThrusterAnomalySeconds)
        {
            _failureAnnounced = true;
            vessel.DisturbanceTorqueBody = new Vector3d(0.0, Gemini8FlightProfile.StuckThrusterTorqueNm, 0.0);
            vessel.DisturbanceMassFlowKgPerS = Gemini8FlightProfile.StuckThrusterMassFlowKgPerS;
            vessel.PitchYawRoll = Vector3d.Zero;
            bridge.SetWarpIndex(0);
            GD.Print(
                "[ANOMALY] OAMS thruster 8 failed ON; docked stack tumbling.");
        }

        if (_failureAnnounced && !_controlRecovered)
        {
            bridge.SetWarpIndex(0);
            // The crew hands attitude control from the OAMS to the re-entry RCS rings...
            if (!_geminiReentryRcsArmed
                && elapsed >= Gemini8FlightProfile.ReentryControlActivationSeconds)
            {
                _geminiReentryRcsArmed = true;
                SetGeminiAttitudeRcs(vessel, oamsEnabled: false, reentryEnabled: true);
                GD.Print("[HISTORICAL] Re-entry control system armed; OAMS attitude off.");
            }
            // ...then isolates the failed circuit.
            if (!_geminiThrusterIsolated
                && elapsed >= Gemini8FlightProfile.ThrusterIsolationSeconds)
            {
                _geminiThrusterIsolated = true;
                vessel.DisturbanceTorqueBody = Vector3d.Zero;
                vessel.DisturbanceMassFlowKgPerS = 0.0;
                GD.Print("[HISTORICAL] Thruster 8 isolated.");
            }
            vessel.PitchYawRoll = _geminiReentryRcsArmed ? RateDampingCommand(vessel) : Vector3d.Zero;
        }

        if (!_undocked
            && elapsed >= Gemini8FlightProfile.UndockingSeconds)
        {
            _undocked = bridge.Universe.Undock(
                "gemini8-agena-docking", 0.25);
            _docked = !_undocked;
            if (_undocked)
                GD.Print(
                    "[HISTORICAL] Emergency undocking; Spacecraft 8 still "
                    + "accelerating in roll.");
        }

        if (!_controlRecovered && _geminiThrusterIsolated
            && vessel.AngularVelocity.Magnitude < Gemini8FlightProfile.ControlRecoveredRateRadPerS)
        {
            _controlRecovered = true;
            vessel.PitchYawRoll = Vector3d.Zero;
            bridge.SetWarpIndex(6);
            GD.Print(
                "[HISTORICAL] Re-entry RCS damped the roll; control restored "
                + $"({vessel.Parts.TotalMonopropellant:F0} kg OAMS left).");
        }

        // Coast to the retro sequence: re-asserted every frame so a transient limit cannot stick.
        if (_controlRecovered && !_adapterSeparated)
        {
            double untilAdapter = Gemini8FlightProfile.AdapterSeparationSeconds - elapsed;
            bridge.SetWarpIndex(untilAdapter > 3_600.0 ? 8 : untilAdapter > 600.0 ? 6
                : untilAdapter > 60.0 ? 4 : 0);
        }

        if (!_adapterSeparated
            && elapsed >= Gemini8FlightProfile.AdapterSeparationSeconds)
        {
            bridge.SetWarpIndex(0);
            string? adapterId = vessel.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole(
                    "gemini_equipment_adapter"))?.InstanceId;
            if (adapterId != null)
                bridge.DeployPartAsVessel(
                    adapterId,
                    "Gemini VIII Equipment Adapter",
                    new Vector3d(0.0, -0.5, 0.0));
            _adapterSeparated = true;
            GD.Print("[HISTORICAL] Equipment adapter separated.");
        }

        // Retrofire with the retrograde section's solid rockets: hold the thrust axis
        // retrograde on the re-entry RCS, fire once aligned, and let them burn out.
        if (!_retroComplete && _adapterSeparated
            && elapsed >= Gemini8FlightProfile.RetrofireSeconds - 120.0)
        {
            bridge.SetWarpIndex(0);
            vessel.IsOnRails = false;
            vessel.OrbitalState = null;
            Vector3d retrograde = -(vessel.Velocity - earth.Velocity).Normalized;
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation, Vector3d.Up,
                retrograde, vessel.AngularVelocity, proportionalGain: 2.3,
                dampingGain: AttitudeGuidance.DampingGainFor(2.3, vessel.GetPitchYawAngularAuthority(earth)));
            var retroPack = vessel.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole("gemini_retro_module"));
            bool aligned = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(retrograde)
                > System.Math.Cos(3.0 * MathUtils.DEG_TO_RAD);
            if (elapsed >= Gemini8FlightProfile.RetrofireSeconds && (aligned || vessel.Throttle > 0.0))
            {
                if (vessel.Throttle <= 0.0)
                    MissionManager.Instance?.EnterPhase(MissionPhase.RETRO_BURN);
                vessel.Throttle = 1.0;
            }
            if (vessel.Throttle > 0.0 && double.IsNaN(_geminiRetroIgnition))
                _geminiRetroIgnition = elapsed;
            // Burnout is the end of thrust (the solid motors leave a sub-tick residue), then the
            // retrograde section is jettisoned to expose the heat shield, as on the flight.
            if (!double.IsNaN(_geminiRetroIgnition) && elapsed - _geminiRetroIgnition > 2.0
                && vessel.ComputeThrust(earth).Magnitude < 1.0)
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                if (retroPack != null)
                    bridge.DeployPartAsVessel(retroPack.InstanceId, "Gemini VIII Retrograde Section",
                        new Vector3d(0.0, -0.5, 0.0));
                vessel = bridge.ActiveVessel ?? vessel;
                _retroComplete = true;
                bridge.SetWarpIndex(8);
                GD.Print("[HISTORICAL] Emergency retrofire complete; retrograde section jettisoned.");
            }
        }

        // Heat shield into the airstream from retrofire through entry.
        if (_retroComplete && vessel.GetAltitude(earth) < 400_000.0)
            vessel.PitchYawRoll = CapsuleEntryGuidance.ComputeCommand(vessel, earth);

        altitude = vessel.GetAltitude(earth);
        up = (vessel.Position - earth.Position).Normalized;
        Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(earth);
        double radialSpeed = surfaceVelocity.Dot(up);
        if (!_entryAnnounced
            && _retroComplete
            && radialSpeed < 0.0
            && altitude <= 120_000.0)
        {
            _entryAnnounced = true;
            bridge.SetWarpIndex(0);
            MissionManager.Instance?.EnterPhase(MissionPhase.ENTRY);
        }
        if (_entryAnnounced && altitude < 25_000.0)
            MissionManager.Instance?.EnterPhase(MissionPhase.AERO_DESCENT);

        if (!_chutesDeployed
            && radialSpeed < 0.0
            && altitude <= 15_000.0)
        {
            _chutesDeployed = vessel.DeployParachutes(earth) > 0;
            if (_chutesDeployed)
                GD.Print("[HISTORICAL] Gemini VIII parachute sequence armed.");
        }

        if (altitude <= 1.1
            && !vessel.IsDestroyed
            && vessel.GetSurfaceVelocity(earth).Magnitude < 1.0)
        {
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.PitchYawRoll = Vector3d.Zero;
            MissionManager.Instance?.EnterPhase(MissionPhase.LANDED);
            _active = false;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Gemini VIII emergency splashdown.");
        }
    }

    private void ProcessApollo11TranspositionAndDocking(
        SimulationBridge bridge,
        Vessel columbia,
        CelestialBody earth)
    {
        Vessel? eagle = bridge.EnsureApollo11EagleExtracted();
        if (eagle == null)
        {
            // CSM sep debris with SLA may take a frame to register.
            return;
        }
        _apollo11EagleExtracted = true;

        // Transposition and docking flown on SM RCS: the CSM turns its probe toward Eagle's
        // drogue and closes to a real port capture (no state assignment).
        bridge.SetWarpIndex(0);
        columbia.IsOnRails = false;
        columbia.OrbitalState = null;
        eagle.IsOnRails = false;
        eagle.OrbitalState = null;
        var columbiaPort = columbia.Parts.Parts.FirstOrDefault(part =>
            part.Definition.IsDockingPort && part.Definition.HasVehicleRole("command_module"));
        var eaglePort = eagle.Parts.Parts.FirstOrDefault(part =>
            part.Definition.IsDockingPort && part.Definition.HasVehicleRole("lunar_module_ascent_stage"));
        if (columbiaPort == null || eaglePort == null) return;
        if (_apollo11Proximity == null)
        {
            _apollo11Proximity = new ProximityApproach();
            GD.Print("[HISTORICAL] Columbia transposing toward Eagle on SM RCS.");
        }
        _apollo11Proximity.Advance(columbia, columbiaPort, eagle, eaglePort, earth);
        if (!_apollo11Proximity.IsOnFinalApproach || _apollo11Proximity.PortDistanceM > 0.65) return;
        DockingAttempt attempt = bridge.Universe.TryDock(columbia.Id, columbiaPort.InstanceId, eagle.Id,
            eaglePort.InstanceId, Apollo11FlightProfile.DockingConnectionId);
        if (!attempt.Succeeded) return;

        columbia.TranslationCommand = Vector3d.Zero;
        columbia.PitchYawRoll = Vector3d.Zero;
        _docked = true;
        _apolloLoiComplete = false;
        _apolloCircularized = false;
        columbia.IsOnRails = true;
        columbia.OrbitalState = OrbitalElements.FromStateVector(
            columbia.Position - earth.Position,
            columbia.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            bridge.Universe.CurrentTime);
        columbia.ReferenceBodyId = earth.Id;
        columbia.Throttle = 0.0;
        bridge.SetWarpIndex(9);
        GD.Print(
            "[HISTORICAL] Columbia hard-docked to Eagle — TD&E complete; "
            + "docked stack coasts to LOI.");
    }

    private void ProcessApollo11LunarOrbitInsertion(
        SimulationBridge bridge,
        Vessel columbia)
    {
        var universe = bridge.Universe;
        var moon = universe.GetBody("moon");
        var earth = universe.GetBody("earth");
        if (moon == null || earth == null) return;

        if (!_apolloMidcourseComplete && !RunApolloMidcourse(bridge, columbia, earth))
            return;

        if (!_apolloLoiComplete)
        {
            CelestialBody dominant = universe.GetDominantBody(columbia.Position);
            if (dominant.Id != moon.Id)
            {
                bridge.SetWarpIndex(9);
                return;
            }

            // The finite LOI pulls the perilune down: trim it inside the sphere first, as Apollo 8.
            if (!_apolloTranslunarTrimDone)
            {
                RunApolloTranslunarTrim(bridge, columbia, moon);
                return;
            }
            double altitude = columbia.GetAltitude(moon);
            if (altitude > 600_000.0)
            {
                double untilPerilune = PeriapsisCaptureBurn.TimeToPeriapsis(
                    columbia.Position - moon.Position, columbia.Velocity - moon.Velocity, moon.GM);
                bridge.SetWarpIndex(double.IsNaN(untilPerilune) || untilPerilune < 3_600.0 ? 5
                    : untilPerilune < 6.0 * 3_600.0 ? 7 : 8);
                return;
            }
            // Columbia is the docked primary: its SPS accelerates the CSM + Eagle stack.
            if (!RunApolloLunarBurn(bridge, columbia, moon,
                    Apollo11FlightProfile.InitialLunarApoluneAltitudeM, maximumBurnSeconds: 600.0))
                return;
            _apolloLoiComplete = true;
            _apolloLoiTime = universe.CurrentTime;
            MissionManager.Instance?.EnterPhase(MissionPhase.LOI);
            bridge.SetWarpIndex(8);
            GD.Print(
                "[HISTORICAL] Apollo 11 docked-stack LOI complete; "
                + "elliptical lunar orbit.");
            return;
        }

        if (!_apolloCircularized)
        {
            if (universe.CurrentTime - _apolloLoiTime
                < Apollo11FlightProfile.CircularizationDelaySeconds)
            {
                bridge.SetWarpIndex(8);
                return;
            }
            if (!RunApolloLunarBurn(bridge, columbia, moon,
                    Apollo11FlightProfile.CircularLunarOrbitAltitudeM, maximumBurnSeconds: 120.0))
                return;
            _apolloCircularized = true;
            _apollo11CircularizedTime = universe.CurrentTime;
            MissionManager.Instance?.EnterPhase(MissionPhase.LUNAR_ORBIT);
            bridge.SetWarpIndex(8);
            GD.Print("[HISTORICAL] Apollo 11 circular lunar orbit; Eagle checkout before undocking.");
        }
    }

    /// <summary>
    /// Undocking, descent orbit insertion and powered descent to the surface. Armstrong and
    /// Aldrin move to Eagle through the tunnel while docked; Eagle then separates on the
    /// docking springs and takes the camera, and <see cref="LunarPoweredDescent"/> flies DOI,
    /// PDI and the let-down on the DPS.
    /// </summary>
    private void ProcessApollo11Descent(SimulationBridge bridge)
    {
        var universe = bridge.Universe;
        var moon = universe.GetBody("moon");
        var eagle = universe.Vessels.FirstOrDefault(v => v.Id == Apollo11FlightProfile.EagleVesselId);
        if (moon == null || eagle == null) return;

        if (double.IsNaN(_apollo11UndockTime))
        {
            if (universe.CurrentTime - _apollo11CircularizedTime < Apollo11FlightProfile.UndockDelaySeconds)
            {
                bridge.SetWarpIndex(8);
                return;
            }
            bridge.SetWarpIndex(0);
            var columbia = bridge.ActiveVessel;
            if (columbia == null) return;
            var lunarCrew = columbia.Crew
                .Where(crew => Apollo11FlightProfile.LunarModuleCrewIds.Contains(crew.Id))
                .Select(crew => crew.Id)
                .ToList();
            if (lunarCrew.Count > 0 && !universe.TransferCrew(columbia.Id, eagle.Id, lunarCrew))
                GD.PrintErr("[HISTORICAL] Apollo 11 crew transfer to Eagle failed.");
            if (!universe.Undock(Apollo11FlightProfile.DockingConnectionId,
                    Apollo11FlightProfile.UndockingSeparationSpeedMps))
            {
                GD.PrintErr("[HISTORICAL] Apollo 11 undocking failed.");
                _active = false;
                return;
            }
            columbia.Throttle = 0.0;
            columbia.PitchYawRoll = Vector3d.Zero;
            columbia.SASEnabled = true;
            bridge.TransferControlTo(eagle);
            _apollo11UndockTime = universe.CurrentTime;
            _apollo11Descent = new LunarPoweredDescent();
            GD.Print($"[HISTORICAL] Eagle undocked from Columbia with {lunarCrew.Count} crew; "
                + "\"The Eagle has wings.\"");
            return;
        }

        // Stand off from Columbia before the DPS lights for DOI.
        if (universe.CurrentTime - _apollo11UndockTime < Apollo11FlightProfile.DoiStandoffSeconds)
        {
            bridge.SetWarpIndex(0);
            return;
        }

        var descent = _apollo11Descent ??= new LunarPoweredDescent();
        double timeToPerilune = PeriapsisCaptureBurn.TimeToPeriapsis(
            eagle.Position - moon.Position, eagle.Velocity - moon.Velocity, moon.GM);
        // Burns run on the fixed control clock, so warp only shortens the wait: x10 while
        // braking high up, real time for the last 3 km and the contact.
        int warpIndex = descent.Status is LunarPoweredDescentStatus.DescentOrbitInsertion
                or LunarPoweredDescentStatus.Braking
            ? (descent.AltitudeM > 3_000.0 ? 4 : 0)
            : descent.Status != LunarPoweredDescentStatus.Coasting || double.IsNaN(timeToPerilune) ? 0
            : timeToPerilune > 1_200.0 ? 6
            : timeToPerilune > 300.0 ? 4
            : 0;
        bridge.SetWarpIndex(warpIndex);
        if (warpIndex == 0)
        {
            eagle.IsOnRails = false;
            eagle.OrbitalState = null;
        }
        var before = descent.Status;
        descent.Advance(eagle, moon, GuidedBurnStep(universe), universe.CurrentTime);
        if (descent.Status != before)
            GD.Print($"[HISTORICAL] Eagle descent: {before} -> {descent.Status} "
                + $"(alt {descent.AltitudeM / 1000.0:F1} km).");
        if (!descent.IsFinished) return;

        _active = false;
        bridge.SetWarpIndex(0);
        if (descent.Status == LunarPoweredDescentStatus.Blocked)
        {
            GD.PrintErr($"[HISTORICAL] Eagle descent stopped: {descent.BlockReason}");
            return;
        }
        MissionManager.Instance?.EnterPhase(MissionPhase.LANDED);
        GD.Print($"[HISTORICAL] Tranquility Base: Eagle landed at {descent.TouchdownSpeedMps:F2} m/s "
            + $"after a {descent.BurnSeconds:F0} s powered descent.");
        CampaignRuntime.Instance?.RequestFinalize();
    }

    private void RunGeminiIntercept(SimulationBridge bridge, Vessel gemini, Vessel agena, CelestialBody earth)
    {
        var universe = bridge.Universe;
        const double standoffM = 500.0;
        if (_geminiTpi == null)
        {
            _geminiTpi = RendezvousPlanner.PlanIntercept(earth.GM, Osculating(gemini, earth, universe.CurrentTime),
                Osculating(agena, earth, universe.CurrentTime), universe.CurrentTime + 600.0, 6.0 * 3_600.0,
                standoffM);
            GD.Print($"[HISTORICAL] Gemini TPI planned in {(_geminiTpi.BurnTime - universe.CurrentTime) / 60.0:F0} min "
                + $"({_geminiTpi.TotalDeltaVMps:F0} m/s with braking).");
        }
        if (_geminiBurn == null)
        {
            // The OAMS TPI is a several-minute burn: centre it on the impulsive epoch.
            double ignition = _geminiBurnIndex == 0
                ? _geminiTpi.BurnTime - RendezvousPlanner.HalfBurnSeconds(gemini, _geminiTpi.InitiationDeltaV.Magnitude)
                : 0.5 * (_geminiTpi.BurnTime + _geminiTpi.ArrivalTime);
            double untilIgnition = ignition - universe.CurrentTime;
            // The coast is integrated with oblateness and drag; replan the TPI from the flown
            // states 15 min out so it does not pay for the two-body prediction error.
            if (_geminiBurnIndex == 0 && !_geminiTpiReplanned && untilIgnition <= 900.0)
            {
                _geminiTpi = RendezvousPlanner.PlanIntercept(earth.GM, Osculating(gemini, earth, universe.CurrentTime),
                    Osculating(agena, earth, universe.CurrentTime), universe.CurrentTime + 300.0, 1_800.0, standoffM);
                _geminiTpiReplanned = true;
                return;
            }
            if (untilIgnition > 0.0)
            {
                bridge.SetWarpIndex(untilIgnition > 3_600.0 ? 7 : untilIgnition > 600.0 ? 5 : untilIgnition > 60.0 ? 3 : 0);
                return;
            }
            bridge.SetWarpIndex(0);
            var aim = RendezvousPlanner.AimPosition(Osculating(agena, earth, universe.CurrentTime), earth.GM,
                _geminiTpi.ArrivalTime, standoffM);
            _geminiBurn = new TranslationLambertBurn(aim, _geminiTpi.ArrivalTime, universe.CurrentTime,
                (gemini.Position - earth.Position).Cross(gemini.Velocity - earth.Velocity));
            _apolloGuidedBurnLastTime = universe.CurrentTime;
        }
        if (!_geminiBurn.IsFinished)
        {
            bridge.SetWarpIndex(0);
            gemini.IsOnRails = false;
            gemini.OrbitalState = null;
            _geminiBurn.Advance(gemini, earth, GuidedBurnStep(universe), universe.CurrentTime);
            if (!_geminiBurn.IsFinished) return;
            if (_geminiBurn.Status == TranslationBurnStatus.Blocked)
                GD.PrintErr($"[HISTORICAL] Gemini OAMS burn stopped: {_geminiBurn.BlockReason}");
            GD.Print($"[HISTORICAL] Gemini {(_geminiBurnIndex == 0 ? "TPI" : "transfer correction")}: "
                + $"{_geminiBurn.BurnSeconds:F1} s on OAMS.");
            _geminiBurnIndex++;
            if (_geminiBurnIndex < 2) _geminiBurn = null;
            return;
        }
        double range = (gemini.Position - agena.Position).Magnitude;
        if (range > ProximityApproach.HandoverRangeM(gemini, _geminiTpi.BrakingDeltaV.Magnitude))
        {
            bridge.SetWarpIndex(range > 60_000.0 ? 4 : 2);
            return;
        }
        bridge.SetWarpIndex(0);
        _rendezvousConfigured = true;
        _geminiProximity = new ProximityApproach();
        MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
        GD.Print($"[HISTORICAL] Rendezvous: {range:F0} m from Agena 5003; braking and final approach.");
    }

    private void RunGeminiProximity(SimulationBridge bridge, Vessel gemini, Vessel agena, CelestialBody earth)
    {
        bridge.SetWarpIndex(0);
        gemini.IsOnRails = false;
        gemini.OrbitalState = null;
        agena.IsOnRails = false;
        agena.OrbitalState = null;
        var geminiPort = gemini.Parts.Parts.FirstOrDefault(part => part.Definition.HasVehicleRole("gemini_docking_port"));
        var agenaPort = agena.Parts.Parts.FirstOrDefault(part => part.Definition.HasVehicleRole("agena_target_docking_port"));
        if (geminiPort == null || agenaPort == null) return;
        _geminiProximity ??= new ProximityApproach();
        _geminiProximity.Advance(gemini, geminiPort, agena, agenaPort, earth);
        if (!_geminiProximity.IsOnFinalApproach || _geminiProximity.PortDistanceM > 0.65) return;
        var attempt = bridge.Universe.TryDock(agena.Id, agenaPort.InstanceId, gemini.Id, geminiPort.InstanceId,
            "gemini8-agena-docking");
        if (!attempt.Succeeded) return;
        _docked = true;
        gemini.TranslationCommand = Vector3d.Zero;
        gemini.PitchYawRoll = Vector3d.Zero;
        GD.Print("[HISTORICAL] First docking achieved with Agena 5003.");
    }

    private static OrbitalElements Osculating(Vessel vessel, CelestialBody earth, double epoch) =>
        OrbitalElements.FromStateVector(vessel.Position - earth.Position, vessel.Velocity - earth.Velocity,
            earth.GM, earth.Id, epoch);

    private static void SetGeminiAttitudeRcs(Vessel vessel, bool oamsEnabled, bool reentryEnabled)
    {
        foreach (var part in vessel.Parts.Parts)
        {
            if (part.Definition.HasVehicleRole("gemini_equipment_adapter")) part.RcsAttitudeEnabled = oamsEnabled;
            if (part.Definition.HasVehicleRole("gemini_reentry_capsule")) part.RcsAttitudeEnabled = reentryEnabled;
        }
    }

    /// <summary>Pure rate damping on all three axes (Gemini re-entry RCS rate mode).</summary>
    private static Vector3d RateDampingCommand(Vessel vessel)
    {
        var rate = vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity);
        const double gain = 4.0;
        return new Vector3d(System.Math.Clamp(-gain * rate.X, -1.0, 1.0),
            System.Math.Clamp(-gain * rate.Z, -1.0, 1.0), System.Math.Clamp(-gain * rate.Y, -1.0, 1.0));
    }

    private void ProcessMercuryAtlas(
        SimulationBridge bridge,
        Vessel vessel,
        CelestialBody earth,
        double elapsed)
    {
        double altitude = vessel.GetAltitude(earth);
        Vector3d up = earth.GetGeodeticUp(vessel.Position);
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_meco)
        {
            double elevation = MercuryAtlasFlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d target = AttitudeGuidance.AimFromElevation(
                up, downrange, elevation);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                target,
                vessel.AngularVelocity,
                proportionalGain: 2.6,
                dampingGain: 1.2);
            vessel.Throttle = _boosterPackageJettisoned
                ? MercuryAtlasFlightProfile.SustainerThrottle(vessel.TotalMass)
                : 1.0;

            if (!_boosterPackageJettisoned
                && elapsed >= MercuryAtlasFlightProfile
                    .BoosterEngineCutoffSeconds)
            {
                string? packageId = vessel.Parts.Parts.FirstOrDefault(part =>
                    part.Definition.HasVehicleRole(
                        "booster_engine_package"))?.InstanceId;
                if (packageId != null)
                    bridge.DeployPartAsVessel(
                        packageId,
                        "Atlas 109-D Booster Package",
                        new Vector3d(0.0, -0.8, 0.0));
                _boosterPackageJettisoned = true;
                GD.Print("[HISTORICAL] MA-6 BECO; YLR-89 package jettisoned.");
            }

            if (!_towerJettisoned
                && elapsed >= MercuryAtlasFlightProfile.TowerJettisonSeconds)
            {
                string? towerId = vessel.Parts.Parts.FirstOrDefault(part =>
                    part.Definition.HasVehicleRole("escape_tower"))?.InstanceId;
                if (towerId != null)
                    bridge.DeployPartAsVessel(
                        towerId,
                        "Friendship 7 Escape Tower",
                        new Vector3d(0.0, 1.5, 0.0));
                _towerJettisoned = true;
                GD.Print("[HISTORICAL] MA-6 escape tower released.");
            }

            if (elapsed >= MercuryAtlasFlightProfile
                    .SustainerEngineCutoffSeconds)
            {
                _meco = true;
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                MissionManager.Instance?.EnterPhase(MissionPhase.MECO);
                GD.Print("[HISTORICAL] MA-6 SECO.");
            }
            return;
        }

        if (!_separated
            && elapsed >= MercuryAtlasFlightProfile
                .SpacecraftSeparationSeconds)
        {
            bridge.TriggerStaging();
            _separated = true;
            vessel = bridge.ActiveVessel ?? vessel;
            vessel.SASEnabled = false;
            MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
            GD.Print("[HISTORICAL] Friendship 7 separated; orbital coast.");
        }

        if (!_separated) return;
        altitude = vessel.GetAltitude(earth);
        up = (vessel.Position - earth.Position).Normalized;
        Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(earth);
        if (surfaceVelocity.MagnitudeSquared > 1e-12)
        {
            Vector3d heatShieldTarget = -surfaceVelocity.Normalized;
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                heatShieldTarget,
                vessel.AngularVelocity,
                proportionalGain: 2.2,
                dampingGain: 1.15);
        }

        double retroApproach =
            MercuryAtlasFlightProfile.RetroSequenceSeconds - 60.0;
        if (!_orbitWarpArmed
            && elapsed >= MercuryAtlasFlightProfile
                .SpacecraftSeparationSeconds + 20.0
            && elapsed < retroApproach)
        {
            bridge.SetWarpIndex(6);
            _orbitWarpArmed = bridge.WarpIndex >= 6;
        }
        else if (_orbitWarpArmed && elapsed >= retroApproach)
        {
            bridge.SetWarpIndex(0);
            _orbitWarpArmed = false;
        }

        double retroEnd = MercuryAtlasFlightProfile.RetroSequenceSeconds
            + MercuryAtlasFlightProfile.RetroBurnDurationSeconds;
        if (!_retroComplete
            && elapsed >= MercuryAtlasFlightProfile.RetroSequenceSeconds)
        {
            bridge.SetWarpIndex(0);
            _orbitWarpArmed = false;
            MissionManager.Instance?.EnterPhase(MissionPhase.RETRO_BURN);

            if (elapsed < retroEnd)
                vessel.Throttle = 1.0;
            else
            {
                vessel.Throttle = 0.0;
                _retroComplete = true;
                MissionManager.Instance?.EnterPhase(MissionPhase.COAST);
                GD.Print(
                    "[HISTORICAL] Friendship 7 retrofire complete; "
                    + "retropack retained for entry.");
            }
        }

        double radialSpeed = surfaceVelocity.Dot(up);
        if (!_entryAnnounced
            && _retroComplete
            && radialSpeed < 0.0
            && altitude <= MercuryAtlasFlightProfile.EntryInterfaceAltitudeM)
        {
            _entryAnnounced = true;
            bridge.SetWarpIndex(0);
            MissionManager.Instance?.EnterPhase(MissionPhase.ENTRY);
        }

        if (_entryAnnounced && altitude < 25_000.0)
            MissionManager.Instance?.EnterPhase(MissionPhase.AERO_DESCENT);

        if (!_chutesDeployed
            && radialSpeed < 0.0
            && altitude <= MercuryAtlasFlightProfile.DrogueDeployAltitudeM)
        {
            _chutesDeployed = vessel.DeployParachutes(earth) > 0;
            if (_chutesDeployed)
            {
                vessel.PitchYawRoll = Vector3d.Zero;
                GD.Print("[HISTORICAL] Friendship 7 parachute sequence armed.");
            }
        }

        if (altitude <= 1.1
            && !vessel.IsDestroyed
            && vessel.GetSurfaceVelocity(earth).Magnitude < 1.0)
        {
            vessel.PitchYawRoll = Vector3d.Zero;
            MissionManager.Instance?.EnterPhase(MissionPhase.LANDED);
            _active = false;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Friendship 7 splashdown.");
        }
    }
}
