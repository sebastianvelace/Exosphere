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
    private double _startTime;
    private bool _meco;
    private bool _separated;
    private bool _towerJettisoned;
    private bool _retroComplete;
    private bool _entryAnnounced;
    private bool _chutesDeployed;

    public override void _Ready()
    {
        Instance = this;
        var runtime = CampaignRuntime.Instance;
        var phase = MissionManager.Instance?.Phase;
        if (SimulationBridge.Instance?.ActiveFlightProfileId
                == MercuryRedstoneFlightProfile.Id
            && runtime?.Director != null
            && phase is not null
            && phase is not MissionPhase.PRE_LAUNCH
                and not MissionPhase.COUNTDOWN
                and not MissionPhase.IGNITION
                and not MissionPhase.LANDED
                and not MissionPhase.CRASHED)
            Resume(runtime.Director.Evidence.ElapsedSeconds);
    }

    public bool EngageIfSupported()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveFlightProfileId != MercuryRedstoneFlightProfile.Id)
            return false;
        var vessel = bridge.ActiveVessel;
        if (vessel == null) return false;

        _active = true;
        _startTime = bridge.Universe.CurrentTime;
        _meco = false;
        _separated = false;
        _towerJettisoned = false;
        _retroComplete = false;
        _entryAnnounced = false;
        _chutesDeployed = false;
        vessel.SASEnabled = false;
        bridge.SetWarpIndex(0);
        GD.Print("[HISTORICAL] Freedom 7 automatic flight sequence engaged.");
        return true;
    }

    private void Resume(double elapsedSeconds)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;
        _active = true;
        _startTime = bridge.Universe.CurrentTime
            - System.Math.Max(0.0, elapsedSeconds);
        _meco = elapsedSeconds
            >= MercuryRedstoneFlightProfile.MainEngineCutoffSeconds;
        _separated = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("booster_engine"));
        _towerJettisoned = !vessel.Parts.Parts.Any(part =>
            part.Definition.HasVehicleRole("escape_tower"));
        _retroComplete = elapsedSeconds
            >= MercuryRedstoneFlightProfile.RetroSequenceSeconds
                + MercuryRedstoneFlightProfile.RetroBurnDurationSeconds;
        _entryAnnounced = MissionManager.Instance?.InDescent == true;
        _chutesDeployed = vessel.HasDeployedParachute;
        vessel.SASEnabled = false;
        GD.Print(
            $"[HISTORICAL] Freedom 7 sequence resumed at T+{elapsedSeconds:F1}s.");
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
}
