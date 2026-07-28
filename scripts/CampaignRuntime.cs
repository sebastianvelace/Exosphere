namespace Exosphere.Game;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Persistence;
using Godot;

/// <summary>
/// Godot adapter for the pure campaign contracts. It samples authoritative simulation
/// telemetry and never duplicates objective math in UI code.
/// </summary>
public partial class CampaignRuntime : Node
{
    private const double StandardGravity = 9.80665;

    public static CampaignRuntime? Instance { get; private set; }
    public MissionDirector? Director { get; private set; }
    public CampaignService? Campaign { get; private set; }
    public MissionDebrief? LastDebrief { get; private set; }
    public bool HasActiveMission => Director != null && !_finalized;

    private Vector3d _launchSurfaceDirection;
    private bool _hasLaunchReference;
    private double _missionStartTime;
    private double _orbitalRadians;
    private double _lunarOrbitalRadians;
    private Vector3d _lastOrbitalDirection;
    private bool _hasOrbitalDirection;
    private string _orbitReferenceBodyId = "";
    private bool _finalized;

    public void Initialize(
        string dataPath,
        string? requestedMissionId,
        CampaignSaveV2? campaignState,
        MissionSaveV2? restoredMission)
    {
        Instance = this;
        var catalog = MissionDefinitionCatalog.LoadFromDirectory(
            System.IO.Path.Combine(dataPath, "missions"));
        Campaign = new CampaignService(
            campaignState ?? new CampaignSaveV2(),
            catalog.Ordered);

        string? missionId = requestedMissionId;
        if (string.IsNullOrWhiteSpace(missionId))
            missionId = restoredMission?.MissionId;
        if (string.IsNullOrWhiteSpace(missionId)) return;
        if (!catalog.Definitions.TryGetValue(missionId, out var definition))
            throw new InvalidDataException(
                $"Unknown mission definition '{missionId}'.");
        if (!Campaign.CanStart(missionId)
            && restoredMission?.MissionId != missionId)
            throw new InvalidOperationException(
                $"Campaign mission '{missionId}' is locked.");

        MissionSaveV2? state = restoredMission?.MissionId == missionId
            ? restoredMission : null;
        Director = new MissionDirector(definition, state);
        SimulationBridge.Instance?.SetActiveFlightProfile(
            definition.FlightProfileId);
        AttachHistoricalCrew(dataPath, definition);
        var universe = SimulationBridge.Instance?.Universe;
        _missionStartTime =
            (universe?.CurrentTime ?? 0.0) - Director.Evidence.ElapsedSeconds;
        _orbitalRadians = Director.Evidence.CompletedOrbits
            * System.Math.Tau;
        _lunarOrbitalRadians = Director.Evidence.CompletedLunarOrbits
            * System.Math.Tau;

        if (state?.LaunchSurfaceDirection is { } launchDirection)
        {
            _launchSurfaceDirection = launchDirection.ToVector().Normalized;
            _hasLaunchReference = true;
        }
        else
        {
            CaptureLaunchReference();
        }
    }

    public override void _Process(double delta)
    {
        if (Director == null || Campaign == null || _finalized) return;
        var bridge = SimulationBridge.Instance;
        var universe = bridge?.Universe;
        var vessel = bridge?.ActiveVessel;
        var phase = MissionManager.Instance?.Phase;
        if (universe == null || vessel == null || phase == null) return;

        var body = universe.GetDominantBody(vessel.Position);
        Vector3d radial = vessel.Position - body.Position;
        Vector3d up = radial.MagnitudeSquared > 1e-12
            ? radial.Normalized : Vector3d.Up;
        if (!_hasLaunchReference) CaptureLaunchReference();
        Vector3d currentSurface =
            body.ToBodyFixedDirection(up, universe.CurrentTime).Normalized;
        double downrange = _hasLaunchReference
            ? System.Math.Acos(System.Math.Clamp(
                currentSurface.Dot(_launchSurfaceDirection), -1.0, 1.0))
                * body.Radius
            : 0.0;
        UpdateOrbitProgress(phase.Value, body.Id, up);
        bool expectedCrewPresent = Director.Definition.CrewIds.Count == 0
            || Director.Definition.CrewIds.All(id =>
                vessel.Crew.Any(crew =>
                    string.Equals(crew.Id, id, StringComparison.Ordinal)
                    && crew.Status != CrewStatus.Dead));
        bool landed = phase == MissionPhase.LANDED;
        bool hasLandingGear = vessel.Parts.Parts.Any(
            part => part.Definition.Category
                == Exosphere.Simulation.Parts.PartCategory.Landing);

        LastDebrief = Director.Observe(new MissionTelemetrySnapshot
        {
            MissionTimeSeconds =
                System.Math.Max(0.0, universe.CurrentTime - _missionStartTime),
            Phase = phase.Value.ToString(),
            AltitudeM = System.Math.Max(0.0, vessel.GetAltitude(body)),
            SurfaceSpeedMps = vessel.GetSurfaceVelocity(body).Magnitude,
            InertialSpeedMps =
                (vessel.Velocity - body.Velocity).Magnitude,
            DynamicPressurePa =
                System.Math.Max(0.0, vessel.GetDynamicPressure(body)),
            GForce = vessel.GetProperAcceleration(body).Magnitude
                / StandardGravity,
            DownrangeM = downrange,
            CompletedOrbits = _orbitalRadians / System.Math.Tau,
            CompletedLunarOrbits =
                _lunarOrbitalRadians / System.Math.Tau,
            DockingAchieved = universe.DockingConnections.Any(connection =>
                connection.PrimaryVesselId == vessel.Id
                || connection.SecondaryVesselId == vessel.Id),
            AngularRateDegPerS = vessel.AngularVelocity.Magnitude
                * MathUtils.RAD_TO_DEG,
            CrewAlive = expectedCrewPresent,
            VesselDestroyed = vessel.IsDestroyed,
            Splashdown = landed && body.Id == "earth" && !hasLandingGear,
        });

        if (phase is MissionPhase.LANDED or MissionPhase.CRASHED)
            FinalizeMission();
    }

    public MissionSaveV2? CaptureMissionState()
    {
        if (Director == null) return null;
        var state = Director.CaptureState();
        if (_hasLaunchReference)
            state.LaunchSurfaceDirection =
                VectorSaveV2.From(_launchSurfaceDirection);
        return state;
    }

    private void FinalizeMission()
    {
        if (Director == null || Campaign == null || _finalized) return;
        _finalized = true;
        LastDebrief = Director.FinalizeMission();
        CampaignRewardResult rewards =
            Campaign.ApplyDebrief(LastDebrief);
        CampaignDebriefPanel.Show(LastDebrief, rewards);
        SaveSystem.SaveGame("campaign_autosave");
    }

    private void CaptureLaunchReference()
    {
        var bridge = SimulationBridge.Instance;
        var universe = bridge?.Universe;
        var vessel = bridge?.ActiveVessel;
        if (universe == null || vessel == null) return;
        var body = universe.GetDominantBody(vessel.Position);
        Vector3d up = (vessel.Position - body.Position).Normalized;
        _launchSurfaceDirection =
            body.ToBodyFixedDirection(up, universe.CurrentTime).Normalized;
        _hasLaunchReference = true;
    }

    private void UpdateOrbitProgress(
        MissionPhase phase,
        string referenceBodyId,
        Vector3d radialDirection)
    {
        bool orbitalPhase = phase is MissionPhase.ORBIT
            or MissionPhase.COAST
            or MissionPhase.RETRO_BURN
            or MissionPhase.LOI
            or MissionPhase.LUNAR_ORBIT;
        if (!orbitalPhase)
        {
            _hasOrbitalDirection = false;
            _orbitReferenceBodyId = "";
            return;
        }

        if (!string.Equals(
                _orbitReferenceBodyId,
                referenceBodyId,
                StringComparison.Ordinal))
        {
            _hasOrbitalDirection = false;
            _orbitReferenceBodyId = referenceBodyId;
        }

        if (_hasOrbitalDirection)
        {
            double step = System.Math.Acos(System.Math.Clamp(
                _lastOrbitalDirection.Dot(radialDirection), -1.0, 1.0));
            // A process frame should never traverse half an orbit. Rejecting a larger
            // discontinuity prevents a vessel switch or restored frame from fabricating
            // campaign progress.
            if (step <= System.Math.PI * 0.5)
            {
                if (string.Equals(
                        referenceBodyId,
                        "moon",
                        StringComparison.OrdinalIgnoreCase))
                    _lunarOrbitalRadians += step;
                else
                    _orbitalRadians += step;
            }
        }
        _lastOrbitalDirection = radialDirection;
        _hasOrbitalDirection = true;
    }

    private static void AttachHistoricalCrew(
        string dataPath,
        MissionDefinition definition)
    {
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel == null || definition.CrewIds.Count == 0) return;
        var catalog = CrewDefinition.LoadAllFromDirectory(
            System.IO.Path.Combine(dataPath, "crew"));
        foreach (string crewId in definition.CrewIds)
        {
            if (vessel.Crew.Any(member =>
                    string.Equals(member.Id, crewId, StringComparison.Ordinal)))
                continue;
            if (!catalog.TryGetValue(crewId, out var crew))
                throw new InvalidDataException(
                    $"Mission '{definition.Id}' references unknown crew '{crewId}'.");
            vessel.Crew.Add(crew.CreateMember());
        }
    }
}
