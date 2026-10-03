namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public enum Flight14LandingPhase { Flip, Braking, TerminalReached, WaterEntry, SplashdownReached, Blocked }
public sealed record Flight14LandingWitness(double MissionElapsedSeconds, double SimulationTimeSeconds,
    double GeodeticAltitudeM, double AtmosphereRelativeSpeedMps, double VerticalSpeedMps,
    double BodyFixedLatitudeDegrees, double BodyFixedLongitudeDegrees, double PropellantKg,
    double UprightAlignment, double AngularRateRadPerSecond);

/// <summary>
/// Three-sea-level-engine flip and terminal burn, optionally continuing into estimated water entry.
/// The retained 100 m witness and bounded water entry do not assert exact geographic targeting.
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
    private double _waterContactTime = double.NaN;
    public Flight14LandingWitness? WaterEntryWitness { get; private set; }
    public Flight14LandingWitness? WaterMotionEndWitness { get; private set; }
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
        if (Phase is Flight14LandingPhase.Blocked or Flight14LandingPhase.TerminalReached or Flight14LandingPhase.SplashdownReached) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (Phase == Flight14LandingPhase.WaterEntry)
        {
            _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
            if (_ship.IsDestroyed || _ship.StructuralControlLost || _ship.IsGroundHeld)
            { Block("water-entry-vehicle-lost"); return; }
            if (_ship.WaterContact is not { } waterRegion
                || !waterRegion.Covers(_body, _ship.Position, universe.CurrentTime))
            { Block("water-motion-outside-declared-ocean-region"); return; }
            if (universe.CurrentTime-_waterContactTime >= _definition.WaterObservationSeconds)
            {
                WaterMotionEndWitness = Witness(universe);
                Phase = Flight14LandingPhase.SplashdownReached;
            }
            return;
        }
        if (_ship.IsDestroyed || _ship.StructuralControlLost || _ship.IsGroundHeld
            || _engines.IsBroken || _engines.FuelDepleted
            || _ship.Parts.Parts.Any(p => p.Definition.HasVehicleRole("booster")))
        { Block("landing-vehicle-unavailable"); return; }
        if (_definition.ContinueToWaterContact && universe.Coupled6DofIntegrationEnabled)
        { Block("water-entry-coupled-integrator-not-validated"); return; }
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
            if (_definition.ContinueToWaterContact)
            {
                var positions = _ship.Parts.ComputePartLocalPositions();
                double bottom = positions.Min(p => p.Value.Y-p.Key.Definition.LengthM*0.5);
                _ship.WaterContact = new Physics.WaterContactDefinition(_ship.MaximumDiameter*0.5,
                    _ship.VehicleLength, _definition.LowestPointYM, _ship.Parts.CenterOfMass.Y-bottom,
                    _definition.WaterDensityKgPerM3, _definition.WaterDragCoefficient, _definition.WettingDepthM,
                    _definition.MinimumWaterLatitudeDegrees, _definition.MaximumWaterLatitudeDegrees,
                    _definition.MinimumWaterLongitudeDegrees, _definition.MaximumWaterLongitudeDegrees, bottom);
            }
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
        if (EndWitness == null && witness.GeodeticAltitudeM <= _definition.DiagnosticEndAltitudeM)
        {
            if (!HasDeliveredThreeSeaLevelEngines || Phase != Flight14LandingPhase.Braking
                || witness.VerticalSpeedMps >= 0 || witness.AtmosphereRelativeSpeedMps > _definition.MaximumEndAirspeedMps
                || witness.UprightAlignment < System.Math.Cos(10*MathUtils.DEG_TO_RAD))
            { Block("terminal-state-envelope"); return; }
            EndWitness = witness;
            if (!_definition.ContinueToWaterContact)
            {
                _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
                Phase = Flight14LandingPhase.TerminalReached; return;
            }
        }
        if (_definition.ContinueToWaterContact && _ship.WaterContact is { } water)
        {
            var entry = Physics.WaterContactSolver.Evaluate(_ship, _body, water, _ship.Position, _ship.Velocity);
            if (entry.LowestPointAltitudeM <= 0)
            {
                if (!water.Covers(_body, _ship.Position, universe.CurrentTime))
                { Block("water-entry-outside-declared-ocean-region"); return; }
                if (Phase != Flight14LandingPhase.Braking || witness.VerticalSpeedMps >= 0
                    || entry.EntrySpeedMps > _definition.MaximumWaterEntrySpeedMps
                    || witness.UprightAlignment < System.Math.Cos(10*MathUtils.DEG_TO_RAD))
                { Block("water-entry-state-envelope"); return; }
                WaterEntryWitness = witness; _waterContactTime = universe.CurrentTime;
                _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
                _ship.WaterMotionEnabled = true;
                Phase = Flight14LandingPhase.WaterEntry; return;
            }
        }
        var up = _body.GetGeodeticUp(_ship.Position);
        var velocity = _ship.GetSurfaceVelocity(_body);
        var horizontal = velocity-up*witness.VerticalSpeedMps;
        double gravity = _body.GM / (_ship.Position-_body.Position).MagnitudeSquared;
        // Remove the lateral braking tilt progressively near the water. A sustained lateral
        // target through contact would deliberately drive a canted nozzle into the sea.
        double tiltLimit = _definition.MaximumTiltDegrees;
        if (_definition.ContinueToWaterContact)
            tiltLimit *= System.Math.Clamp((witness.GeodeticAltitudeM+_definition.LowestPointYM)/_definition.DiagnosticEndAltitudeM, 0, 1);
        double tilt = System.Math.Min(_definition.LateralDampingPerSecond*horizontal.Magnitude / gravity,
            System.Math.Tan(tiltLimit*MathUtils.DEG_TO_RAD));
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
        double clearance = witness.GeodeticAltitudeM;
        if (_definition.ContinueToWaterContact)
            clearance = System.Math.Max(0, clearance + _definition.LowestPointYM*witness.UprightAlignment);
        double targetDown = _definition.TargetFinalDescentSpeedMps
            + clearance*_definition.DescentProfileSlopePerSecond;
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
