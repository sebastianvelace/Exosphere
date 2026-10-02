namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public enum Flight14ReturnPhase
{
    WaitingForPayload, OrbitalCoast, AligningDeorbit, DeorbitBurn,
    EntryCoast, AtmosphericEntry, EntryReached, Blocked
}

public sealed record Flight14EntryWitness(double MissionElapsedSeconds, double GeodeticAltitudeM,
    double RadialAltitudeM, double InertialSpeedMps, double AtmosphereRelativeSpeedMps,
    double VerticalSpeedMps, double SpecificEnergyJPerKg, double RemainingPropellantKg,
    double WindwardShieldDotVelocity, double SimulationTimeSeconds,
    double BodyFixedLatitudeDegrees, double BodyFixedLongitudeDegrees);

/// <summary>
/// Composes the loaded launch/deployment with a real single-engine deorbit and entry.
/// No state reseeding, resource refill, landing-site targeting or water-return acceptance.
/// </summary>
public sealed class Flight14ReturnController : IPhysicsStepController
{
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Flight14LaunchController _launch;
    private readonly Flight14PayloadDeploymentController _payload;
    private readonly Flight14ReturnDefinition _definition;
    private readonly Part _tank;
    private readonly Flight14OrbitalBurn _burn;
    private Quaterniond _entryReference;
    private Quaterniond _deorbitReference;

    public Flight14ReturnPhase Phase { get; private set; } = Flight14ReturnPhase.WaitingForPayload;
    public string? BlockReason { get; private set; }
    public bool HasDeliveredDeorbitThrust => _burn.HasDeliveredThrust;
    public double PostDeploymentMassKg { get; private set; } = double.NaN;
    public double PostDeploymentPropellantKg { get; private set; } = double.NaN;
    public double DeorbitCommandElapsedSeconds { get; private set; } = double.NaN;
    public double DeorbitTargetElapsedSeconds { get; private set; } = double.NaN;
    public double DeorbitShutdownElapsedSeconds { get; private set; } = double.NaN;
    public double PreDeorbitEnergyJPerKg { get; private set; } = double.NaN;
    public double PostDeorbitEnergyJPerKg { get; private set; } = double.NaN;
    public double PostDeorbitPeriapsisAltitudeM { get; private set; } = double.NaN;
    public double PreDeorbitPropellantKg { get; private set; } = double.NaN;
    public double PostDeorbitPropellantKg { get; private set; } = double.NaN;
    public Flight14EntryWitness? EntryInterface { get; private set; }
    public Flight14EntryWitness? DiagnosticEnd { get; private set; }

    public Flight14ReturnController(Vessel ship, CelestialBody body,
        Flight14LaunchController launch, Flight14PayloadDeploymentController payload,
        Flight14ReturnDefinition definition, Flight14LaunchGuidanceDefinition guidance)
    {
        definition.Validate();
        if (body.Atmosphere == null || definition.MinimumBurnAltitudeM <= body.Atmosphere.MaxAltitude
            || definition.EntryInterfaceAltitudeM > body.Atmosphere.MaxAltitude)
            throw new ArgumentException("Flight 14 return requires an atmospheric body and a vacuum burn envelope.");
        _ship = ship; _body = body; _launch = launch; _payload = payload; _definition = definition;
        _tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        _burn = new Flight14OrbitalBurn(Flight14BurnKind.Deorbit,
            definition.TargetPeriapsisAltitudeM, guidance.SeaLevelEngineModelId,
            3, definition.MinimumBurnAltitudeM, definition.MaximumBurnSeconds);
    }

    public bool RequiresFixedCadence(Universe universe) => Phase is not
        (Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked);

    public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(universe);
        if (!double.IsFinite(controlIntervalSeconds) || controlIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlIntervalSeconds));
        if (Phase is Flight14ReturnPhase.EntryReached or Flight14ReturnPhase.Blocked) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (_ship.IsDestroyed || _ship.StructuralControlLost) { Block("vehicle-damaged"); return; }
        if (Phase == Flight14ReturnPhase.WaitingForPayload)
        {
            _payload.BeforePhysicsStep(universe, controlIntervalSeconds);
            if (_payload.Phase == Flight14PayloadPhase.Blocked) { Block("payload:" + _payload.BlockReason); return; }
            if (_payload.Phase != Flight14PayloadPhase.Complete) return;
            PostDeploymentMassKg = _ship.TotalMass;
            PostDeploymentPropellantKg = PropellantKg;
            Phase = Flight14ReturnPhase.OrbitalCoast;
        }
        double elapsed = universe.CurrentTime - _launch.LiftoffEpoch;
        double altitude = _ship.GetAltitude(_body);
        var velocity = _ship.Velocity - _body.Velocity;
        if (!double.IsFinite(elapsed) || elapsed > _definition.MaximumMissionSeconds
            || !double.IsFinite(altitude) || !double.IsFinite(velocity.Magnitude)
            || !double.IsFinite(_ship.AngularVelocity.Magnitude))
        { Block("return-state-or-duration-envelope"); return; }
        _ship.SASEnabled = false;
        if (Phase is Flight14ReturnPhase.OrbitalCoast or Flight14ReturnPhase.AligningDeorbit)
        {
            _ship.Throttle = 0;
            var orbit = Elements(universe.CurrentTime);
            if (orbit.IsHyperbolic || !double.IsFinite(orbit.Periapsis)
                || orbit.Periapsis - _body.Radius <= _body.Atmosphere!.MaxAltitude
                || altitude < _definition.MinimumBurnAltitudeM)
            { Block("pre-deorbit-orbit-envelope"); return; }
            bool prepare = elapsed >= _definition.EarliestDeorbitMissionSeconds - _definition.AlignmentLeadSeconds;
            if (prepare)
            {
                if (Phase == Flight14ReturnPhase.OrbitalCoast) _deorbitReference = _ship.Orientation;
                Phase = Flight14ReturnPhase.AligningDeorbit;
                var desiredAxis = -velocity.Normalized;
                var desiredReference = Quaterniond.FromTo(_deorbitReference.Rotate(Vector3d.Up),
                    desiredAxis) * _deorbitReference;
                _deorbitReference = AttitudeGuidance.SlewQuaternion(_deorbitReference, desiredReference,
                    controlIntervalSeconds, _definition.DeorbitReferenceRateRadPerSecond);
                PointAxis(_deorbitReference.Rotate(Vector3d.Up));
            }
            else PointAxis(velocity.Normalized);
            if (elapsed < _definition.EarliestDeorbitMissionSeconds) return;
            if (PropellantKg < _definition.MinimumRemainingPropellantKg)
            { Block("deorbit-reserve-envelope"); return; }
            PreDeorbitEnergyJPerKg = Energy;
            PreDeorbitPropellantKg = PropellantKg;
            Phase = Flight14ReturnPhase.DeorbitBurn;
        }
        if (Phase == Flight14ReturnPhase.DeorbitBurn)
        {
            if (PropellantKg < _definition.MinimumRemainingPropellantKg)
            { Block("deorbit-reserve-envelope"); return; }
            _burn.Advance(_ship, _body, controlIntervalSeconds, universe.CurrentTime);
            if (_burn.Status is Flight14BurnStatus.Blocked or Flight14BurnStatus.TargetAlreadySatisfied)
            { Block(_burn.BlockReason ?? "no-delivered-deorbit"); return; }
            if (_burn.Status == Flight14BurnStatus.Burning && double.IsNaN(DeorbitCommandElapsedSeconds))
                DeorbitCommandElapsedSeconds = elapsed;
            if (_burn.Status != Flight14BurnStatus.Completed) return;
            if (double.IsNaN(DeorbitTargetElapsedSeconds)) DeorbitTargetElapsedSeconds = elapsed;
            // A throttle-off command is not engine shutdown. Keep the axis retrograde
            // until delivered residual thrust has stopped before the entry turn.
            PointAxis(-velocity.Normalized);
            if (elapsed - DeorbitTargetElapsedSeconds > _definition.MaximumShutdownSeconds)
            { Block("deorbit-shutdown-envelope"); return; }
            if (_ship.GetCurrentThrust(_body) > 1) return;
            DeorbitShutdownElapsedSeconds = elapsed;
            PostDeorbitEnergyJPerKg = Energy;
            PostDeorbitPeriapsisAltitudeM = Elements(universe.CurrentTime).Periapsis - _body.Radius;
            PostDeorbitPropellantKg = PropellantKg;
            if (PostDeorbitEnergyJPerKg >= PreDeorbitEnergyJPerKg
                || PostDeorbitPropellantKg >= PreDeorbitPropellantKg
                || PostDeorbitPeriapsisAltitudeM >= _body.Atmosphere!.MaxAltitude)
            { Block("deorbit-physical-response-envelope"); return; }
            _entryReference = _ship.Orientation;
            Phase = Flight14ReturnPhase.EntryCoast;
        }
        _ship.Throttle = 0;
        var up = _body.GetGeodeticUp(_ship.Position);
        var surfaceVelocity = _ship.GetSurfaceVelocity(_body);
        var desired = EntryAttitudeGuidance.ComputeTarget(up, surfaceVelocity.Normalized, liftTowardBody: false);
        _entryReference = AttitudeGuidance.SlewQuaternion(_entryReference, desired,
            controlIntervalSeconds, _definition.EntryReferenceRateRadPerSecond);
        _ship.PitchYawRoll = AttitudeGuidance.ComputeCommand(_ship.Orientation,
            _entryReference, _ship.AngularVelocity, 2, 25);
        double verticalSpeed = surfaceVelocity.Dot(up);
        if (altitude <= _definition.EntryInterfaceAltitudeM && verticalSpeed < -20
            && surfaceVelocity.Magnitude >= _definition.MinimumEntrySpeedMps)
        {
            EntryInterface ??= Witness(elapsed, universe.CurrentTime);
            Phase = Flight14ReturnPhase.AtmosphericEntry;
        }
        if (Phase == Flight14ReturnPhase.AtmosphericEntry
            && altitude <= _definition.DiagnosticEndAltitudeM && verticalSpeed < 0)
        {
            DiagnosticEnd = Witness(elapsed, universe.CurrentTime);
            _ship.PitchYawRoll = Vector3d.Zero;
            Phase = Flight14ReturnPhase.EntryReached;
        }
    }

    private double PropellantKg => _tank.LiquidFuel + _tank.Oxidizer;
    private double Energy => (_ship.Velocity - _body.Velocity).MagnitudeSquared * 0.5
        - _body.GM / (_ship.Position - _body.Position).Magnitude;
    private OrbitalElements Elements(double epoch) => OrbitalElements.FromStateVector(
        _ship.Position - _body.Position, _ship.Velocity - _body.Velocity, _body.GM, _body.Id, epoch);
    private void PointAxis(Vector3d aim) => _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
        _ship.Orientation, Vector3d.Up, aim, _ship.AngularVelocity, 2, 25);
    private Flight14EntryWitness Witness(double elapsed, double simulationTime)
    {
        _body.GetGeodeticCoordinatesAtTime(_ship.Position, simulationTime,
            out double latitude, out double longitude, out _);
        return new(elapsed, _ship.GetAltitude(_body), (_ship.Position - _body.Position).Magnitude - _body.Radius,
            (_ship.Velocity - _body.Velocity).Magnitude, _ship.GetSurfaceVelocity(_body).Magnitude,
            _ship.GetSurfaceVelocity(_body).Dot(_body.GetGeodeticUp(_ship.Position)), Energy, PropellantKg,
            -_ship.Orientation.Rotate(Vector3d.Right).Normalized.Dot(_ship.GetSurfaceVelocity(_body).Normalized),
            simulationTime, latitude, longitude);
    }
    private void Block(string reason)
    {
        _ship.Throttle = 0;
        _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14ReturnPhase.Blocked;
        BlockReason = reason;
    }
}
