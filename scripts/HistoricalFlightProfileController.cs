namespace Exosphere.Game;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
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

    public override void _Ready()
    {
        Instance = this;
        var runtime = CampaignRuntime.Instance;
        var phase = MissionManager.Instance?.Phase;
        string? profileId = SimulationBridge.Instance?.ActiveFlightProfileId;
        if (profileId is MercuryRedstoneFlightProfile.Id
                or MercuryAtlasFlightProfile.Id
                or Gemini8FlightProfile.Id
            && runtime?.Director != null
            && phase is not null
            && phase is not MissionPhase.PRE_LAUNCH
                and not MissionPhase.COUNTDOWN
                and not MissionPhase.IGNITION
                and not MissionPhase.LANDED
                and not MissionPhase.CRASHED)
        {
            double elapsed = runtime.Director.Evidence.ElapsedSeconds;
            if (profileId == Gemini8FlightProfile.Id)
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
                or Gemini8FlightProfile.Id))
            return false;
        var vessel = bridge.ActiveVessel;
        if (vessel == null) return false;

        _active = true;
        _atlasProfile =
            bridge.ActiveFlightProfileId == MercuryAtlasFlightProfile.Id;
        _geminiProfile =
            bridge.ActiveFlightProfileId == Gemini8FlightProfile.Id;
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
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
        if (_geminiProfile)
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
