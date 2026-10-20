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
    private bool _docked;
    private bool _undocked;
    private bool _failureAnnounced;
    private bool _controlRecovered;
    private bool _adapterSeparated;
    private bool _apolloSicSeparated;
    private bool _apolloSiiSeparated;
    private bool _apolloParkingOrbit;
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
        _docked = false;
        _undocked = false;
        _failureAnnounced = false;
        _controlRecovered = false;
        _adapterSeparated = false;
        _apolloSicSeparated = false;
        _apolloSiiSeparated = false;
        _apolloParkingOrbit = false;
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
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
        if (_apollo11Tde)
            GD.Print(
                "[HISTORICAL] Apollo 11 TD&E + docked LOI sequence engaged "
                + "(powered descent deferred).");
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
        // This slice stops at circular LLO — never TEI / Earth return.
        _apolloTeiComplete = true;
        _apolloSmSeparated = true;
        _apollo11EagleExtracted = SimulationBridge.Instance?.Universe.Vessels
            .Any(v => v.Id == Apollo11FlightProfile.EagleVesselId) == true;
        _docked = SimulationBridge.Instance?.Universe.DockingConnections
            .Any(c => c.Id == Apollo11FlightProfile.DockingConnectionId) == true;

        var phase = MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH;
        if (!_docked)
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
            $"[HISTORICAL] Apollo 11 TD&E/LOI sequence resumed at "
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
            || vessel.PitchYawRoll.MagnitudeSquared > 1e-6);

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
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_apolloSicSeparated)
        {
            // Pitch-over then zero-AoA gravity turn; the open-loop elevation program paid
            // ~660 m/s of steering loss on the S-IC.
            Vector3d boostAim = GravityTurnGuidance.ComputeAim(vessel, earth, downrange, elapsed,
                Apollo8FlightProfile.PitchOverDegrees, Apollo8FlightProfile.PitchOverEndSeconds);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation, Vector3d.Up, boostAim, vessel.AngularVelocity,
                proportionalGain: 2.3, dampingGain: 1.2);
            var sicEngines = vessel.Parts.Parts.FirstOrDefault(part =>
                part.Definition.HasVehicleRole("sic_engine_cluster"));
            sicEngines?.SelectEngineCount(
                elapsed >= Apollo8FlightProfile.SicCenterEngineCutoffSeconds
                    ? 4 : 5);
            vessel.Throttle = elapsed
                < Apollo8FlightProfile.SicOutboardCutoffSeconds ? 1.0 : 0.0;
            if (elapsed >= Apollo8FlightProfile.SicSeparationSeconds)
            {
                bridge.TriggerStaging();
                _apolloSicSeparated = true;
                vessel = bridge.ActiveVessel ?? vessel;
                vessel.Throttle = 1.0;
                MissionManager.Instance?.EnterPhase(MissionPhase.SEPARATION);
                GD.Print("[HISTORICAL] AS-503 S-IC separation; S-II ignition.");
            }
            return;
        }

        if (!_apolloSiiSeparated)
        {
            GuideApolloInsertion(vessel, earth, downrange);
            vessel.Throttle = elapsed
                < Apollo8FlightProfile.SiiCutoffSeconds ? 1.0 : 0.0;
            if (!_towerJettisoned
                && elapsed >= Apollo8FlightProfile.EscapeTowerJettisonSeconds)
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
                GD.Print("[HISTORICAL] Apollo 8 launch escape tower jettisoned.");
            }
            if (elapsed >= Apollo8FlightProfile.SiiSeparationSeconds)
            {
                bridge.TriggerStaging();
                _apolloSiiSeparated = true;
                vessel = bridge.ActiveVessel ?? vessel;
                vessel.Throttle = 1.0;
                GD.Print("[HISTORICAL] AS-503 S-II separation; S-IVB ignition.");
            }
            return;
        }

        if (!_apolloParkingOrbit)
        {
            // Closed-loop S-IVB insertion: cut off once the flown orbit clears the
            // atmosphere, not at the historical clock.
            GuideApolloInsertion(vessel, earth, downrange);
            vessel.Throttle = 1.0;
            if (ClosedLoopInsertionGuidance.IsInserted(
                    vessel, earth, ApolloParkingPeriapsisAltitudeM, universe.CurrentTime))
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                // The flown orbit follows the launch heading; rotating it into the lunar
                // plane remains a scripted step until launch-azimuth targeting exists.
                PlaceInEarthParkingOrbit(
                    vessel, earth, moon, downrange, universe.CurrentTime);
                _apolloParkingOrbit = true;
                MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
                _apolloLunarPlan = PlanApolloLunarTransfer(
                    universe,
                    vessel,
                    earth,
                    moon,
                    _startTime + Apollo8FlightProfile.TliIgnitionSeconds);
                bridge.SetWarpIndex(8);
                GD.Print(
                    "[HISTORICAL] Apollo 8 Earth parking orbit; "
                    + "TLI solution loaded.");
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
                $"[HISTORICAL] Apollo 8 TLI ignition; planned Δv "
                + $"{_apolloLunarPlan.InjectionDeltaVMag:F0} m/s.");
            return;
        }

        if (!_apolloTliComplete)
        {
            bridge.SetWarpIndex(0);
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
        if (!_apolloCsmSeparated) return;

        if (_apollo11Tde)
        {
            if (!_docked)
            {
                ProcessApollo11TranspositionAndDocking(bridge, vessel, elapsed);
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

            double altitude = vessel.GetAltitude(moon);
            if (altitude > 600_000.0)
            {
                bridge.SetWarpIndex(5);
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
            double lunarOrbits =
                CampaignRuntime.Instance?.Director?.Evidence
                    .CompletedLunarOrbits ?? 0.0;
            if (lunarOrbits + 1e-6
                < Apollo8FlightProfile.HistoricalLunarRevolutions)
            {
                bridge.SetWarpIndex(9);
                return;
            }

            bridge.SetWarpIndex(0);
            ApplyApolloTransearthInjection(
                vessel, earth, moon, universe.CurrentTime);
            ConsumeApolloSpsPropellant(
                vessel, Apollo8FlightProfile.TeiDeltaVMps);
            _apolloTeiComplete = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.TEI);
            bridge.SetWarpIndex(9);
            GD.Print(
                "[HISTORICAL] Apollo 8 TEI complete after ten lunar revolutions.");
            return;
        }

        dominant = universe.GetDominantBody(vessel.Position);
        if (dominant.Id != earth.Id)
        {
            bridge.SetWarpIndex(9);
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

        if (returnVelocity.MagnitudeSquared > 1e-12)
        {
            vessel.Orientation = Quaterniond.FromTo(
                Vector3d.Up, -returnVelocity.Normalized);
            vessel.AngularVelocity = Vector3d.Zero;
        }
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
    private static void GuideApolloInsertion(Vessel vessel, CelestialBody earth, Vector3d downrange)
    {
        Vector3d desired = ClosedLoopInsertionGuidance.ComputeAim(
            vessel, earth, Apollo8FlightProfile.ParkingOrbitAltitudeM, downrange);
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

    private static void ApplyApolloTransearthInjection(
        Vessel vessel,
        CelestialBody earth,
        CelestialBody moon,
        double departureTime)
    {
        Vector3d departure = vessel.Position - earth.Position;
        Vector3d planeNormal =
            departure.Cross(vessel.Velocity - earth.Velocity).Normalized;
        if (planeNormal.MagnitudeSquared < 1e-9)
            planeNormal = earth.RotationAxis;
        Vector3d targetDirection =
            planeNormal.Cross(departure.Normalized).Normalized;
        Vector3d target = targetDirection
            * (earth.Radius + 60_000.0);
        double returnTime =
            Apollo8FlightProfile.EntryInterfaceSeconds
            - Apollo8FlightProfile.TeiCutoffSeconds;
        LambertSolution solution = LambertSolver.Solve(
            earth.GM,
            departure,
            target,
            returnTime,
            planeNormal);
        vessel.Velocity = earth.Velocity + solution.DepartureVelocity;
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        vessel.ReferenceBodyId = moon.Id;
        vessel.Throttle = 0.0;
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
            var aim = plan.EarthTransferOrbit.GetStateAtTime(plan.ArrivalTime, earth.GM).position;
            _apolloMidcourseBurn = new LambertGuidedBurn(aim, plan.ArrivalTime, correctionTime,
                (vessel.Position - earth.Position).Cross(vessel.Velocity - earth.Velocity),
                cutoffDeltaVMps: 0.1, maximumBurnSeconds: 300.0);
            _apolloGuidedBurnLastTime = universe.CurrentTime;
        }
        bridge.SetWarpIndex(0);
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
        int warpIndex = !coasting || double.IsNaN(timeToPeriapsis) ? 0
            : timeToPeriapsis > 1_200.0 ? 6
            : timeToPeriapsis > 300.0 ? 4
            : 0;
        bridge.SetWarpIndex(warpIndex);
        if (warpIndex == 0)
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

    private static void ConsumeApolloSpsPropellant(
        Vessel vessel,
        double deltaVMps)
    {
        const double ispSeconds = 314.8;
        const double standardGravity = 9.80665;
        double initialMass = vessel.TotalMass;
        double propellant = initialMass * (
            1.0 - System.Math.Exp(
                -deltaVMps / (ispSeconds * standardGravity)));
        var tanks = vessel.Parts.Parts
            .Where(part => part.Definition.HasVehicleRole("service_module"))
            .ToArray();
        double available = tanks.Sum(part =>
            part.LiquidFuel + part.Oxidizer);
        if (available <= 0.0) return;
        double consumed = System.Math.Min(propellant, available);
        foreach (var tank in tanks)
        {
            double tankAvailable = tank.LiquidFuel + tank.Oxidizer;
            double tankShare = consumed * tankAvailable / available;
            double fuelShare = tankAvailable > 0.0
                ? tank.LiquidFuel / tankAvailable : 0.0;
            tank.LiquidFuel = System.Math.Max(
                0.0, tank.LiquidFuel - tankShare * fuelShare);
            tank.Oxidizer = System.Math.Max(
                0.0, tank.Oxidizer - tankShare * (1.0 - fuelShare));
        }
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
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

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
            vessel.Throttle = elapsed
                < Gemini8FlightProfile.StageOneCutoffSeconds ? 1.0 : 0.0;
            if (elapsed >= Gemini8FlightProfile.StageSeparationSeconds)
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
            double elevation = Gemini8FlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d desired = AttitudeGuidance.AimFromElevation(
                up, downrange, elevation);
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                desired,
                vessel.AngularVelocity,
                proportionalGain: 2.3,
                dampingGain: 1.15);
            vessel.Throttle = 1.0;
            if (elapsed >= Gemini8FlightProfile.StageTwoCutoffSeconds)
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

        double approachStart =
            Gemini8FlightProfile.RendezvousCompleteSeconds;
        if (!_orbitWarpArmed
            && elapsed >= Gemini8FlightProfile.SpacecraftSeparationSeconds + 30.0
            && elapsed < approachStart - 60.0)
        {
            bridge.SetWarpIndex(6);
            _orbitWarpArmed = bridge.WarpIndex >= 6;
        }
        else if (_orbitWarpArmed && elapsed >= approachStart - 60.0)
        {
            bridge.SetWarpIndex(0);
            _orbitWarpArmed = false;
        }

        if (!_rendezvousConfigured && elapsed >= approachStart)
        {
            ConfigureGeminiFinalApproach(vessel, target);
            _rendezvousConfigured = true;
            bridge.SetWarpIndex(4);
            MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
            GD.Print(
                "[HISTORICAL] Rendezvous complete; calibrated OAMS final "
                + "approach initiated.");
        }
        if (_rendezvousConfigured
            && !_docked
            && elapsed >= Gemini8FlightProfile.DockingSeconds - 10.0)
            bridge.SetWarpIndex(0);

        if (!_docked
            && !_undocked
            && elapsed >= Gemini8FlightProfile.DockingSeconds)
        {
            DockingAttempt attempt = CaptureGeminiDocking(
                bridge, vessel, target);
            _docked = attempt.Succeeded;
            if (_docked)
                GD.Print("[HISTORICAL] First docking achieved with Agena 5003.");
            else
                GD.PushWarning(
                    $"[HISTORICAL] Gemini docking rejected: {attempt.Failure}.");
        }

        if (!_failureAnnounced
            && elapsed >= Gemini8FlightProfile.ThrusterAnomalySeconds)
        {
            _failureAnnounced = true;
            bridge.SetWarpIndex(4);
            GD.Print(
                "[ANOMALY] OAMS thruster 8 failed ON; docked stack tumbling.");
        }

        if (_failureAnnounced && !_controlRecovered)
        {
            double rate = Gemini8FlightProfile.AnomalyAngularRateDegPerS(
                elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d rollAxis =
                vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            vessel.AngularVelocity = rollAxis * rate;
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

        if (!_controlRecovered
            && elapsed >= Gemini8FlightProfile.ControlRecoveredSeconds)
        {
            _controlRecovered = true;
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.PitchYawRoll = Vector3d.Zero;
            ConsumeGeminiOamsReserve(vessel);
            bridge.SetWarpIndex(6);
            GD.Print(
                "[HISTORICAL] Thruster 8 isolated; reentry RCS restored control.");
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

        if (!_retroComplete
            && elapsed >= Gemini8FlightProfile.RetrofireSeconds)
        {
            Vector3d relativeVelocity = vessel.Velocity - earth.Velocity;
            Vector3d retrograde = -relativeVelocity.Normalized;
            vessel.Velocity += retrograde
                * Gemini8FlightProfile.CalibratedEmergencyRetroDeltaVMps;
            vessel.IsOnRails = false;
            vessel.OrbitalState = null;
            _retroComplete = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.RETRO_BURN);
            bridge.SetWarpIndex(8);
            GD.Print(
                "[HISTORICAL] Emergency retrofire complete; ballistic return.");
        }

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
        double elapsed)
    {
        Vessel? eagle = bridge.EnsureApollo11EagleExtracted();
        if (eagle == null)
        {
            // CSM sep debris with SLA may take a frame to register.
            return;
        }
        _apollo11EagleExtracted = true;

        if (!_rendezvousConfigured
            && elapsed >= Apollo11FlightProfile.EagleExtractSeconds)
        {
            ConfigureApollo11FinalApproach(columbia, eagle);
            _rendezvousConfigured = true;
            bridge.SetWarpIndex(0);
            GD.Print("[HISTORICAL] Columbia on final approach to Eagle.");
            return;
        }

        if (!_rendezvousConfigured) return;

        if (elapsed < Apollo11FlightProfile.DockingSeconds) return;

        DockingAttempt attempt = CaptureApollo11Docking(bridge, columbia, eagle);
        if (!attempt.Succeeded)
        {
            GD.PrintErr(
                $"[HISTORICAL] Apollo 11 docking failed: {attempt.Failure} "
                + $"d={attempt.DistanceM:F2} m v={attempt.RelativeSpeedMps:F2} m/s "
                + $"align={attempt.AlignmentErrorDeg:F1}°");
            _active = false;
            return;
        }

        _docked = true;
        _apolloLoiComplete = false;
        _apolloCircularized = false;
        var earth = bridge.Universe.GetBody("earth");
        if (earth != null)
        {
            columbia.IsOnRails = true;
            columbia.OrbitalState = OrbitalElements.FromStateVector(
                columbia.Position - earth.Position,
                columbia.Velocity - earth.Velocity,
                earth.GM,
                earth.Id,
                bridge.Universe.CurrentTime);
            columbia.ReferenceBodyId = earth.Id;
        }
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

            double altitude = columbia.GetAltitude(moon);
            if (altitude > 600_000.0)
            {
                bridge.SetWarpIndex(5);
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
            MissionManager.Instance?.EnterPhase(MissionPhase.LUNAR_ORBIT);
            bridge.SetWarpIndex(0);
            _active = false;
            GD.Print(
                "[HISTORICAL] Apollo 11 circular lunar orbit — LOI slice complete "
                + "(powered descent deferred).");
            CampaignRuntime.Instance?.RequestFinalize();
        }
    }

    private static void ConfigureApollo11FinalApproach(
        Vessel columbia,
        Vessel eagle)
    {
        eagle.IsOnRails = false;
        eagle.OrbitalState = null;
        // Same orientation on both craft (Gemini pattern): CM +Y probe faces LM -Y drogue.
        Vector3d dockingAxis =
            eagle.Orientation.Rotate(Vector3d.Up).Normalized;
        const double initialPortSeparationM = 180.0;
        double centreToPortsM = Apollo11FlightProfile.DockingCentreToPortsM;
        double coastTime = System.Math.Max(
            30.0,
            Apollo11FlightProfile.DockingSeconds
                - Apollo11FlightProfile.EagleExtractSeconds);
        columbia.Position = eagle.Position
            - dockingAxis * (initialPortSeparationM + centreToPortsM);
        columbia.Velocity = eagle.Velocity
            + dockingAxis * (initialPortSeparationM / coastTime);
        columbia.Orientation = eagle.Orientation;
        columbia.AngularVelocity = Vector3d.Zero;
        columbia.IsOnRails = false;
        columbia.OrbitalState = null;
    }

    private static DockingAttempt CaptureApollo11Docking(
        SimulationBridge bridge,
        Vessel columbia,
        Vessel eagle)
    {
        string? columbiaPort = columbia.Parts.Parts.FirstOrDefault(part =>
            part.Definition.IsDockingPort
            && part.Definition.HasVehicleRole("command_module"))?.InstanceId;
        string? eaglePort = eagle.Parts.Parts.FirstOrDefault(part =>
            part.Definition.IsDockingPort
            && part.Definition.HasVehicleRole(
                "lunar_module_ascent_stage"))?.InstanceId;
        if (columbiaPort == null || eaglePort == null)
            return new DockingAttempt(
                false,
                DockingFailure.PortMissing,
                null,
                double.NaN,
                double.NaN,
                double.NaN);

        // Match Universe.TryGetDockingFrame: Position + R*portLocal (root frame).
        if (!columbia.Parts.TryGetAttachmentNodeLocalPosition(
                columbiaPort, "top", out var cmPortLocal)
            || !eagle.Parts.TryGetAttachmentNodeLocalPosition(
                eaglePort, "top", out var lmPortLocal))
            return new DockingAttempt(
                false,
                DockingFailure.PortMissing,
                null,
                double.NaN,
                double.NaN,
                double.NaN);

        Vector3d axis = eagle.Orientation.Rotate(Vector3d.Up).Normalized;
        const double captureGapM = 0.12;
        columbia.Orientation = eagle.Orientation;
        columbia.Position = eagle.Position
            + eagle.Orientation.Rotate(lmPortLocal - cmPortLocal)
            - axis * captureGapM;
        columbia.Velocity = eagle.Velocity + axis * 0.08;
        columbia.AngularVelocity = Vector3d.Zero;
        eagle.AngularVelocity = Vector3d.Zero;

        return bridge.Universe.TryDock(
            columbia.Id,
            columbiaPort,
            eagle.Id,
            eaglePort,
            Apollo11FlightProfile.DockingConnectionId);
    }

    private static void ConfigureGeminiFinalApproach(
        Vessel gemini,
        Vessel target)
    {
        target.IsOnRails = false;
        target.OrbitalState = null;
        Vector3d dockingAxis =
            target.Orientation.Rotate(Vector3d.Up).Normalized;
        const double initialPortSeparationM = 150.0;
        const double centreToPortsM = 0.975;
        double coastTime = Gemini8FlightProfile.DockingSeconds
            - Gemini8FlightProfile.RendezvousCompleteSeconds;
        gemini.Position = target.Position
            - dockingAxis
                * (initialPortSeparationM + centreToPortsM);
        gemini.Velocity = target.Velocity
            + dockingAxis * (initialPortSeparationM / coastTime);
        gemini.Orientation = target.Orientation;
        gemini.AngularVelocity = Vector3d.Zero;
        gemini.IsOnRails = false;
        gemini.OrbitalState = null;
    }

    private static DockingAttempt CaptureGeminiDocking(
        SimulationBridge bridge,
        Vessel gemini,
        Vessel target)
    {
        Vector3d axis = target.Orientation.Rotate(Vector3d.Up).Normalized;
        const double centreToPortsM = 0.975;
        const double captureGapM = 0.10;
        gemini.Position = target.Position
            - axis * (centreToPortsM + captureGapM);
        gemini.Velocity = target.Velocity + axis * 0.10;
        gemini.Orientation = target.Orientation;
        gemini.AngularVelocity = Vector3d.Zero;
        target.AngularVelocity = Vector3d.Zero;
        string? geminiPort = gemini.Parts.Parts.FirstOrDefault(part =>
            part.Definition.HasVehicleRole(
                "gemini_docking_port"))?.InstanceId;
        string? targetPort = target.Parts.Parts.FirstOrDefault(part =>
            part.Definition.HasVehicleRole(
                "agena_target_docking_port"))?.InstanceId;
        if (geminiPort == null || targetPort == null)
            return new DockingAttempt(
                false,
                DockingFailure.PortMissing,
                null,
                double.NaN,
                double.NaN,
                double.NaN);
        return bridge.Universe.TryDock(
            gemini.Id,
            geminiPort,
            target.Id,
            targetPort,
            "gemini8-agena-docking");
    }

    private static void ConsumeGeminiOamsReserve(Vessel vessel)
    {
        foreach (var part in vessel.Parts.Parts.Where(part =>
                     part.Definition.HasVehicleRole(
                         "gemini_equipment_adapter")))
            part.Monopropellant = System.Math.Min(
                part.Monopropellant,
                part.Definition.FuelCapacityMono * 0.30);
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
