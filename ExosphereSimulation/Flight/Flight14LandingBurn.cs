namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public enum Flight14LandingPhase { Flip, Braking, TerminalReached, Blocked }
public sealed record Flight14LandingWitness(double MissionElapsedSeconds, double SimulationTimeSeconds,
    double GeodeticAltitudeM, double AtmosphereRelativeSpeedMps, double VerticalSpeedMps,
    double BodyFixedLatitudeDegrees, double BodyFixedLongitudeDegrees, double PropellantKg,
    double UprightAlignment, double AngularRateRadPerSecond);

/// <summary>
/// Three-sea-level-engine physical flip and terminal burn. No water contact or geographic
/// targeting is implied by its 100 m endpoint. Writes control/selection commands only.
/// </summary>
public sealed class Flight14LandingBurn
{
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Part _engines;
    private readonly Flight14LandingDefinition _definition;
    private readonly string _seaLevelModelId;
    private readonly double _liftoffEpoch;
    private double _startTime = double.NaN;
    private int _engineCount = 3;
    public Flight14LandingPhase Phase { get; private set; } = Flight14LandingPhase.Flip;
    public string? BlockReason { get; private set; }
    public Flight14LandingWitness? StartWitness { get; private set; }
    public Flight14LandingWitness? EndWitness { get; private set; }
    public bool HasDeliveredThreeSeaLevelEngines { get; private set; }
    public double MaximumAngularRateRadPerSecond { get; private set; }
    public double MaximumClimbRateMps { get; private set; }
    public int MinimumSelectedEngineCount { get; private set; } = 3;

    public Flight14LandingBurn(Vessel ship, CelestialBody body, Flight14LandingDefinition definition,
        string seaLevelModelId, double liftoffEpoch)
    {
        definition.Validate();
        if (!double.IsFinite(liftoffEpoch) || string.IsNullOrWhiteSpace(seaLevelModelId))
            throw new ArgumentException("Invalid Flight 14 landing identity.");
        _ship = ship; _body = body; _definition = definition; _seaLevelModelId = seaLevelModelId;
        _liftoffEpoch = liftoffEpoch;
        _engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
    }

    public void Advance(Universe universe, double interval)
    {
        if (!double.IsFinite(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        if (Phase is Flight14LandingPhase.Blocked or Flight14LandingPhase.TerminalReached) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (_ship.IsDestroyed || _ship.StructuralControlLost || _ship.IsGroundHeld
            || _engines.IsBroken || _engines.FuelDepleted
            || _ship.Parts.Parts.Any(p => p.Definition.HasVehicleRole("booster")))
        { Block("landing-vehicle-unavailable"); return; }
        var healthy = _engines.EngineStates.Where(e => e.FailureCode == null
            && e.State != EngineLifecycleState.Failed).Take(3).ToArray();
        if (healthy.Length != 3 || healthy.Any(e => e.EngineModelId != _seaLevelModelId))
        { Block("three-healthy-sea-level-engines-required"); return; }
        if (!_engines.Definition.ResolvedEngineModels.TryGetValue(_seaLevelModelId, out var model))
        { Block("landing-engine-model-unavailable"); return; }
        int maximumStarts = model.RestartLimit + 1;
        // Generic runtime counts starts but does not enforce this metadata. Bound this
        // mission's ignition before requesting it, and reject any later budget overrun.
        if (healthy.Any(e => e.StartAttempts > maximumStarts
            || StartWitness == null && e.StartAttempts >= maximumStarts))
        { Block("landing-restart-budget-exhausted"); return; }
        var witness = Witness(universe);
        if (StartWitness == null)
        {
            if (witness.VerticalSpeedMps >= 0 || witness.GeodeticAltitudeM > _definition.FlipAltitudeM + 1)
            { Block("landing-handoff-envelope"); return; }
            StartWitness = witness; _startTime = universe.CurrentTime;
        }
        if (!double.IsFinite(witness.GeodeticAltitudeM) || !double.IsFinite(witness.AtmosphereRelativeSpeedMps)
            || universe.CurrentTime-_startTime > _definition.MaximumBurnSeconds)
        { Block("landing-duration-or-state-envelope"); return; }
        MaximumAngularRateRadPerSecond = System.Math.Max(MaximumAngularRateRadPerSecond, witness.AngularRateRadPerSecond);
        MaximumClimbRateMps = System.Math.Max(MaximumClimbRateMps, witness.VerticalSpeedMps);
        double pressure = _ship.GetAmbientPressure(_body);
        var delivered = _engines.GetEngineTelemetry(pressure).Where(e => e.ThrustN > 1).ToArray();
        if (delivered.Length == 3 && delivered.All(e => healthy.Any(h => h.InstanceId == e.InstanceId)))
            HasDeliveredThreeSeaLevelEngines = true;
        if (witness.GeodeticAltitudeM <= _definition.DiagnosticEndAltitudeM)
        {
            if (!HasDeliveredThreeSeaLevelEngines || Phase != Flight14LandingPhase.Braking
                || witness.VerticalSpeedMps >= 0 || witness.AtmosphereRelativeSpeedMps > _definition.MaximumEndAirspeedMps
                || witness.UprightAlignment < System.Math.Cos(10*MathUtils.DEG_TO_RAD))
            { Block("terminal-state-envelope"); return; }
            EndWitness = witness;
            _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
            Phase = Flight14LandingPhase.TerminalReached; return;
        }
        var up = _body.GetGeodeticUp(_ship.Position);
        var velocity = _ship.GetSurfaceVelocity(_body);
        var horizontal = velocity-up*witness.VerticalSpeedMps;
        double gravity = _body.GM / (_ship.Position-_body.Position).MagnitudeSquared;
        double tilt = System.Math.Min(_definition.LateralDampingPerSecond*horizontal.Magnitude / gravity,
            System.Math.Tan(_definition.MaximumTiltDegrees*MathUtils.DEG_TO_RAD));
        var aim = (up-horizontal.Normalized*tilt).Normalized;
        _ship.SASEnabled = false;
        _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(_ship.Orientation, Vector3d.Up,
            aim, _ship.AngularVelocity, _definition.ProportionalAttitudeGain, _definition.AttitudeDampingGain);
        if (Phase == Flight14LandingPhase.Flip)
        {
            _engines.SelectEngineCount(3);
            _ship.Throttle = _engines.ApplyThrottleFloor(_engines.Definition.MinThrottle);
            if (HasDeliveredThreeSeaLevelEngines && _ship.Orientation.Rotate(Vector3d.Up).Dot(aim)
                >= System.Math.Cos(15*MathUtils.DEG_TO_RAD)) Phase = Flight14LandingPhase.Braking;
            else return;
        }
        // Continuous burn with monotone count reduction avoids exhausting restart limits.
        // Never replace low demand with a fabricated sub-minimum throttle or extra relight.
        double targetDown = _definition.TargetFinalDescentSpeedMps
            + witness.GeodeticAltitudeM*_definition.DescentProfileSlopePerSecond;
        double thrustUp = System.Math.Max(0.2, witness.UprightAlignment);
        double acceleration = System.Math.Max(0, gravity
            + _definition.VerticalDampingPerSecond*(-witness.VerticalSpeedMps-targetDown)) / thrustUp;
        double force = acceleration*_ship.TotalMass;
        int selected = _engineCount;
        for (int count = 1; count <= _engineCount; count++)
        {
            _engines.SelectEngineCount(count);
            if (force <= _engines.GetFullThrottleThrustMagnitude(pressure)) { selected = count; break; }
        }
        _engineCount = selected; _engines.SelectEngineCount(selected);
        MinimumSelectedEngineCount = System.Math.Min(MinimumSelectedEngineCount, selected);
        double rated = _engines.GetFullThrottleThrustMagnitude(pressure);
        _ship.Throttle = _engines.ApplyThrottleFloor(System.Math.Max(_engines.Definition.MinThrottle,
            force/System.Math.Max(1, rated)));
    }

    private Flight14LandingWitness Witness(Universe universe)
    {
        _body.GetGeodeticCoordinatesAtTime(_ship.Position, universe.CurrentTime, out double lat, out double lon, out _);
        var up = _body.GetGeodeticUp(_ship.Position); var velocity = _ship.GetSurfaceVelocity(_body);
        return new(universe.CurrentTime-_liftoffEpoch, universe.CurrentTime, _ship.GetAltitude(_body),
            velocity.Magnitude, velocity.Dot(up), lat, lon,
            _ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer), _ship.Orientation.Rotate(Vector3d.Up).Dot(up),
            _ship.AngularVelocity.Magnitude);
    }
    private void Block(string reason)
    {
        _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14LandingPhase.Blocked; BlockReason = reason;
    }
}
