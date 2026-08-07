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
    private LunarTransferPlan? _apolloLunarPlan;

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
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
        if (_apollo11Tde)
            GD.Print(
                "[HISTORICAL] Apollo 11 transposition-and-docking "
                + "sequence engaged (lunar landing deferred).");
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
        _apolloTliStarted =
            elapsedSeconds >= Apollo8FlightProfile.TliIgnitionSeconds;
        _apolloTliComplete =
            elapsedSeconds >= Apollo8FlightProfile.TliCutoffSeconds;
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
        _apolloLoiComplete = true; // never run A8 lunar path on A11 TD&E
        _apolloCircularized = true;
        _apolloTeiComplete = true;
        _apolloSmSeparated = true;
        _apollo11EagleExtracted = SimulationBridge.Instance?.Universe.Vessels
            .Any(v => v.Id == Apollo11FlightProfile.EagleVesselId) == true;
        _docked = SimulationBridge.Instance?.Universe.DockingConnections
            .Any(c => c.Id == Apollo11FlightProfile.DockingConnectionId) == true;
        GD.Print(
            $"[HISTORICAL] Apollo 11 TD&E sequence resumed at "
            + $"T+{elapsedSeconds:F1}s.");
    }

    public override void _Process(double delta)
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
        Vector3d up = (vessel.Position - earth.Position).Normalized;
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_meco)
        {
            double elevation = MercuryRedstoneFlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d target = (
                downrange * System.Math.Cos(elevation)
                + up * System.Math.Sin(elevation)).Normalized;
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
        Vector3d earthUp = (vessel.Position - earth.Position).Normalized;
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_apolloSicSeparated)
        {
            GuideApolloAscent(vessel, earthUp, downrange, elapsed, 2.5);
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
            GuideApolloAscent(vessel, earthUp, downrange, elapsed, 2.3);
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
            GuideApolloAscent(vessel, earthUp, downrange, elapsed, 2.1);
            vessel.Throttle = elapsed
                < Apollo8FlightProfile.SivbFirstCutoffSeconds ? 1.0 : 0.0;
            if (elapsed >= Apollo8FlightProfile.ParkingOrbitInsertionSeconds)
            {
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
                bridge.SetWarpIndex(7);
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
            double burnTime = _apolloLunarPlan.BurnTime;
            if (universe.CurrentTime < burnTime - 45.0)
            {
                bridge.SetWarpIndex(7);
                return;
            }
            bridge.SetWarpIndex(0);
            if (universe.CurrentTime < burnTime) return;

            vessel.Position =
                earth.Position + _apolloLunarPlan.DeparturePosition;
            vessel.Velocity =
                earth.Velocity + _apolloLunarPlan.PreBurnVelocity;
            Vector3d burnDirection =
                _apolloLunarPlan.InjectionDeltaV.Normalized;
            vessel.Orientation =
                Quaterniond.FromTo(Vector3d.Up, burnDirection);
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.IsOnRails = false;
            vessel.OrbitalState = null;
            vessel.ReferenceBodyId = earth.Id;
            vessel.Throttle = 1.0;
            _apolloTliStartTime = universe.CurrentTime;
            _apolloTliStarted = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.TLI);
            GD.Print(
                $"[HISTORICAL] Apollo 8 TLI ignition; planned Δv "
                + $"{_apolloLunarPlan.InjectionDeltaVMag:F0} m/s.");
            return;
        }

        if (!_apolloTliComplete)
        {
            double burnDuration = Apollo8FlightProfile.TliCutoffSeconds
                - Apollo8FlightProfile.TliIgnitionSeconds;
            if (universe.CurrentTime - _apolloTliStartTime < burnDuration)
            {
                vessel.Throttle = 1.0;
                return;
            }

            vessel.Throttle = 0.0;
            var state = _apolloLunarPlan!.EarthTransferOrbit.GetStateAtTime(
                universe.CurrentTime, earth.GM);
            vessel.Position = earth.Position + state.position;
            vessel.Velocity = earth.Velocity + state.velocity;
            vessel.OrbitalState = OrbitalElements.FromStateVector(
                state.position,
                state.velocity,
                earth.GM,
                earth.Id,
                universe.CurrentTime);
            vessel.ReferenceBodyId = earth.Id;
            vessel.IsOnRails = true;
            _apolloTliComplete = true;
            bridge.SetWarpIndex(7);
            GD.Print("[HISTORICAL] Apollo 8 TLI cutoff; translunar coast.");
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
                bridge.SetWarpIndex(8);
                GD.Print("[HISTORICAL] CSM-103 separated from S-IVB/LTA-B.");
            }
        }
        if (!_apolloCsmSeparated) return;

        if (_apollo11Tde)
        {
            ProcessApollo11TranspositionAndDocking(bridge, vessel, elapsed);
            return;
        }

        dominant = universe.GetDominantBody(vessel.Position);
        if (!_apolloLoiComplete)
        {
            if (dominant.Id != moon.Id)
            {
                bridge.SetWarpIndex(8);
                return;
            }

            double altitude = vessel.GetAltitude(moon);
            Vector3d moonUp = (vessel.Position - moon.Position).Normalized;
            double radialSpeed =
                (vessel.Velocity - moon.Velocity).Dot(moonUp);
            if (altitude > 600_000.0)
            {
                bridge.SetWarpIndex(5);
                return;
            }
            bridge.SetWarpIndex(0);
            if (altitude > 180_000.0 && radialSpeed < 0.0) return;

            Vector3d retrograde =
                -(vessel.Velocity - moon.Velocity).Normalized;
            vessel.Velocity += retrograde * Apollo8FlightProfile.LoiDeltaVMps;
            ConsumeApolloSpsPropellant(
                vessel, Apollo8FlightProfile.LoiDeltaVMps);
            PlaceInLunarOrbit(
                vessel,
                moon,
                Apollo8FlightProfile.InitialLunarPeriluneAltitudeM,
                Apollo8FlightProfile.InitialLunarApoluneAltitudeM,
                universe.CurrentTime);
            _apolloLoiComplete = true;
            _apolloLoiTime = universe.CurrentTime;
            MissionManager.Instance?.EnterPhase(MissionPhase.LOI);
            bridge.SetWarpIndex(7);
            GD.Print(
                "[HISTORICAL] Apollo 8 LOI complete; "
                + "60.0 × 168.5 nmi lunar orbit.");
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
                bridge.SetWarpIndex(7);
                return;
            }
            bridge.SetWarpIndex(0);
            Vector3d retrograde =
                -(vessel.Velocity - moon.Velocity).Normalized;
            vessel.Velocity += retrograde
                * Apollo8FlightProfile.CircularizationDeltaVMps;
            ConsumeApolloSpsPropellant(
                vessel, Apollo8FlightProfile.CircularizationDeltaVMps);
            PlaceInLunarOrbit(
                vessel,
                moon,
                Apollo8FlightProfile.CircularLunarOrbitAltitudeM,
                Apollo8FlightProfile.CircularLunarOrbitAltitudeM,
                universe.CurrentTime);
            _apolloCircularized = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.LUNAR_ORBIT);
            bridge.SetWarpIndex(8);
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
                bridge.SetWarpIndex(8);
                return;
            }

            bridge.SetWarpIndex(0);
            ApplyApolloTransearthInjection(
                vessel, earth, moon, universe.CurrentTime);
            ConsumeApolloSpsPropellant(
                vessel, Apollo8FlightProfile.TeiDeltaVMps);
            _apolloTeiComplete = true;
            MissionManager.Instance?.EnterPhase(MissionPhase.TEI);
            bridge.SetWarpIndex(8);
            GD.Print(
                "[HISTORICAL] Apollo 8 TEI complete after ten lunar revolutions.");
            return;
        }

        dominant = universe.GetDominantBody(vessel.Position);
        if (dominant.Id != earth.Id)
        {
            bridge.SetWarpIndex(8);
            return;
        }

        double earthAltitude = vessel.GetAltitude(earth);
        Vector3d returnUp = (vessel.Position - earth.Position).Normalized;
        Vector3d returnVelocity = vessel.Velocity - earth.Velocity;
        double returnRadialSpeed = returnVelocity.Dot(returnUp);
        if (earthAltitude > 5_000_000.0)
        {
            bridge.SetWarpIndex(8);
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

    private static void GuideApolloAscent(
        Vessel vessel,
        Vector3d up,
        Vector3d downrange,
        double elapsed,
        double gain)
    {
        double elevation = Apollo8FlightProfile
            .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
        Vector3d desired = (
            downrange * System.Math.Cos(elevation)
            + up * System.Math.Sin(elevation)).Normalized;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation,
            Vector3d.Up,
            desired,
            vessel.AngularVelocity,
            proportionalGain: gain,
            dampingGain: 1.2);
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

    private static void PlaceInLunarOrbit(
        Vessel vessel,
        CelestialBody moon,
        double periluneAltitudeM,
        double apoluneAltitudeM,
        double epoch)
    {
        Vector3d up = (vessel.Position - moon.Position).Normalized;
        Vector3d velocity = vessel.Velocity - moon.Velocity;
        Vector3d tangent =
            (velocity - up * velocity.Dot(up)).Normalized;
        if (tangent.MagnitudeSquared < 1e-9)
            tangent = moon.RotationAxis.Cross(up).Normalized;
        double radius = moon.Radius + periluneAltitudeM;
        double apolune = moon.Radius + apoluneAltitudeM;
        double semiMajorAxis = (radius + apolune) * 0.5;
        double speed = System.Math.Sqrt(
            moon.GM * (2.0 / radius - 1.0 / semiMajorAxis));
        vessel.Position = moon.Position + up * radius;
        vessel.Velocity = moon.Velocity + tangent * speed;
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.IsOnRails = true;
        vessel.OrbitalState = OrbitalElements.FromStateVector(
            vessel.Position - moon.Position,
            vessel.Velocity - moon.Velocity,
            moon.GM,
            moon.Id,
            epoch);
        vessel.ReferenceBodyId = moon.Id;
        vessel.Throttle = 0.0;
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
        Vector3d up = (vessel.Position - earth.Position).Normalized;
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_geminiStageOneSeparated)
        {
            double elevation = Gemini8FlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d desired = (
                downrange * System.Math.Cos(elevation)
                + up * System.Math.Sin(elevation)).Normalized;
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
            Vector3d desired = (
                downrange * System.Math.Cos(elevation)
                + up * System.Math.Sin(elevation)).Normalized;
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
            bridge.SetWarpIndex(7);
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
        if (_docked)
        {
            bridge.SetWarpIndex(0);
            return;
        }

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
        _active = false;
        bridge.SetWarpIndex(0);
        GD.Print(
            "[HISTORICAL] Columbia hard-docked to Eagle — TD&E complete "
            + "(lunar landing deferred).");
        CampaignRuntime.Instance?.RequestFinalize();
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
        Vector3d up = (vessel.Position - earth.Position).Normalized;
        Vector3d downrange = bridge.GetLaunchHeadingDirection();

        if (!_meco)
        {
            double elevation = MercuryAtlasFlightProfile
                .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
            Vector3d target = (
                downrange * System.Math.Cos(elevation)
                + up * System.Math.Sin(elevation)).Normalized;
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
