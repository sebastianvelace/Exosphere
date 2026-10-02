namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

public enum Flight14PayloadPhase { WaitingForOrbit, Coast, Deploying, Complete, Blocked }

public sealed record Flight14PayloadRelease(double MissionElapsedSeconds, string PartInstanceId,
    Vessel Satellite, double CarrierMassBeforeKg, double CarrierMassAfterKg,
    double MassResidualKg, double CenterResidualM, double MomentumResidualKgMps,
    double RelativeOpeningResidualMps, double PeriapsisAltitudeM);

/// <summary>
/// Continuous pad-to-payload diagnostic. The planned release program is an estimate;
/// an achieved orbit, shutdown and physical attitude gates authorize each actual split.
/// </summary>
public sealed class Flight14PayloadDeploymentController : IPhysicsStepController
{
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Flight14LaunchController _launch;
    private readonly Flight14PayloadDefinition _definition;
    private readonly string[] _partIds;
    private readonly List<Flight14PayloadRelease> _releases = new();
    private readonly IReadOnlyList<Flight14PayloadRelease> _releaseView;

    public Flight14PayloadPhase Phase { get; private set; } = Flight14PayloadPhase.WaitingForOrbit;
    public string? BlockReason { get; private set; }
    public IReadOnlyList<Flight14PayloadRelease> Releases => _releaseView;

    public Flight14PayloadDeploymentController(Vessel ship, CelestialBody body,
        Flight14LaunchController launch, Flight14PayloadDefinition definition, IReadOnlyList<string> partIds)
    {
        definition.Validate();
        if (partIds.Count != definition.Count || partIds.Distinct().Count() != partIds.Count
            || partIds.Any(id => !ship.Parts.Parts.Any(p => p.InstanceId == id
                && p.Definition.HasVehicleRole("payload") && ship.Parts.IsEnclosedPayload(p)))
            || body.Atmosphere == null || definition.MinimumAltitudeM <= body.Atmosphere.MaxAltitude
            || definition.MinimumPeriapsisAltitudeM <= body.Atmosphere.MaxAltitude)
            throw new ArgumentException("Flight 14 deployment requires the complete enclosed payload and a safe orbit envelope.");
        _ship = ship; _body = body; _launch = launch; _definition = definition;
        _partIds = partIds.ToArray();
        _releaseView = _releases.AsReadOnly();
    }

    public bool RequiresFixedCadence(Universe universe) => Phase is not (Flight14PayloadPhase.Complete or Flight14PayloadPhase.Blocked);

    public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(universe);
        if (!double.IsFinite(controlIntervalSeconds) || controlIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlIntervalSeconds));
        if (Phase is Flight14PayloadPhase.Complete or Flight14PayloadPhase.Blocked) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (_ship.IsDestroyed || _ship.StructuralControlLost) { Block("vehicle-damaged"); return; }
        if (_launch.Phase != Flight14LaunchPhase.OrbitReady)
        {
            _launch.BeforePhysicsStep(universe, controlIntervalSeconds);
            if (_launch.Phase == Flight14LaunchPhase.Blocked) Block("launch:"+_launch.BlockReason);
            return;
        }
        if (Phase == Flight14PayloadPhase.WaitingForOrbit) Phase = Flight14PayloadPhase.Coast;
        double elapsed = universe.CurrentTime-_launch.LiftoffEpoch;
        if (elapsed > _definition.MaximumMissionSeconds) { Block("deployment-duration-envelope"); return; }
        var relativePosition = _ship.Position-_body.Position;
        var relativeVelocity = _ship.Velocity-_body.Velocity;
        var orbit = OrbitalElements.FromStateVector(relativePosition, relativeVelocity,
            _body.GM, _body.Id, universe.CurrentTime);
        _ship.Throttle = 0;
        _ship.SASEnabled = false;
        _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(_ship.Orientation,
            Vector3d.Up, relativeVelocity.Normalized, _ship.AngularVelocity, 2, 25);
        if (orbit.IsHyperbolic || !double.IsFinite(orbit.Periapsis)
            || orbit.Periapsis-_body.Radius < _definition.MinimumPeriapsisAltitudeM
            || _ship.GetAltitude(_body) < _definition.MinimumAltitudeM)
        { Block("deployment-orbit-envelope"); return; }
        double scheduled = _definition.FirstReleaseMissionSeconds+_releases.Count*_definition.ReleaseIntervalSeconds;
        if (elapsed < scheduled || elapsed-_launch.OrbitElapsedSeconds < _definition.SettleAfterInsertionSeconds
            || _ship.GetCurrentThrust(_body) > 1
            || _ship.AngularVelocity.Magnitude > _definition.MaximumReleaseAngularRateRadPerSecond) return;
        string id = _partIds[_releases.Count];
        var part = _ship.Parts.Parts.SingleOrDefault(p => p.InstanceId == id);
        if (part == null || part.IsBroken) { Block("payload-missing-or-damaged"); return; }
        double beforeMass = _ship.TotalMass;
        var beforePosition = _ship.Position;
        var beforeVelocity = _ship.Velocity;
        var omega = _ship.AngularVelocity;
        var opening = _ship.Orientation.Rotate(_definition.OpeningVelocity);
        var satellite = _ship.DeployPayload(id, $"Starlink V3 { _releases.Count+1:00} (estimate)",
            _definition.OpeningVelocity);
        if (satellite == null) { Block("payload-separation-failed"); return; }
        double mass = satellite.TotalMass+_ship.TotalMass;
        var center = (satellite.Position*satellite.TotalMass+_ship.Position*_ship.TotalMass)/mass;
        var momentum = satellite.Velocity*satellite.TotalMass+_ship.Velocity*_ship.TotalMass;
        var satelliteOrbit = OrbitalElements.FromStateVector(satellite.Position-_body.Position,
            satellite.Velocity-_body.Velocity, _body.GM, _body.Id, universe.CurrentTime);
        var release = new Flight14PayloadRelease(elapsed, id, satellite, beforeMass, _ship.TotalMass,
            mass-beforeMass, center.DistanceTo(beforePosition), momentum.DistanceTo(beforeVelocity*beforeMass),
            (satellite.Velocity-_ship.Velocity-omega.Cross(satellite.Position-_ship.Position)-opening).Magnitude,
            satelliteOrbit.Periapsis-_body.Radius);
        // Unpowered payloads use the existing Kepler coast approximation from their real split state.
        satellite.OrbitalState = satelliteOrbit;
        satellite.IsOnRails = true;
        universe.AddVessel(satellite);
        _releases.Add(release);
        if (satelliteOrbit.IsHyperbolic || release.PeriapsisAltitudeM < _definition.MinimumPeriapsisAltitudeM)
        { Block("released-payload-orbit-envelope"); return; }
        Phase = _releases.Count == _partIds.Length ? Flight14PayloadPhase.Complete : Flight14PayloadPhase.Deploying;
        if (Phase == Flight14PayloadPhase.Complete) _ship.PitchYawRoll = Vector3d.Zero;
    }

    private void Block(string reason)
    {
        _ship.Throttle = 0;
        _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14PayloadPhase.Blocked;
        BlockReason = reason;
    }
}
