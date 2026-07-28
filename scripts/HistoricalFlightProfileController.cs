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
    private double _startTime;
    private bool _meco;
    private bool _boosterPackageJettisoned;
    private bool _separated;
    private bool _towerJettisoned;
    private bool _retroComplete;
    private bool _entryAnnounced;
    private bool _chutesDeployed;
    private bool _orbitWarpArmed;

    public override void _Ready()
    {
        Instance = this;
        var runtime = CampaignRuntime.Instance;
        var phase = MissionManager.Instance?.Phase;
        string? profileId = SimulationBridge.Instance?.ActiveFlightProfileId;
        if (profileId is MercuryRedstoneFlightProfile.Id
                or MercuryAtlasFlightProfile.Id
            && runtime?.Director != null
            && phase is not null
            && phase is not MissionPhase.PRE_LAUNCH
                and not MissionPhase.COUNTDOWN
                and not MissionPhase.IGNITION
                and not MissionPhase.LANDED
                and not MissionPhase.CRASHED)
            Resume(
                runtime.Director.Evidence.ElapsedSeconds,
                profileId == MercuryAtlasFlightProfile.Id);
    }

    public bool EngageIfSupported()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveFlightProfileId is not (
                MercuryRedstoneFlightProfile.Id
                or MercuryAtlasFlightProfile.Id))
            return false;
        var vessel = bridge.ActiveVessel;
        if (vessel == null) return false;

        _active = true;
        _atlasProfile =
            bridge.ActiveFlightProfileId == MercuryAtlasFlightProfile.Id;
        _startTime = bridge.Universe.CurrentTime;
        _meco = false;
        _boosterPackageJettisoned = false;
        _separated = false;
        _towerJettisoned = false;
        _retroComplete = false;
        _entryAnnounced = false;
        _chutesDeployed = false;
        _orbitWarpArmed = false;
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
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
