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
    public Flight14LandingDefinition Definition => _definition;
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
                    _definition.MinimumWaterLongitudeDegrees, _definition.MaximumWaterLongitudeDegrees, bottom,
                    _definition.AdditionalWaterRegions.ToArray());
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
        // Powered-descent vector guidance: the vertical channel tracks the descent profile and
        // the horizontal channel damps drift; both set one thrust vector whose direction is
        // the attitude target and whose magnitude selects the engines.
        double clearance = witness.GeodeticAltitudeM;
        if (_definition.ContinueToWaterContact)
            clearance = System.Math.Max(0, clearance + _definition.LowestPointYM*witness.UprightAlignment);
        double targetDown = _definition.TargetDescentSpeedMps(clearance);
        // Feed-forward of the profile's own deceleration (dv/dt = a·|v_z|/v_target along the
        // profile), so the proportional channel only corrects deviations instead of lagging.
        double feedForward = _definition.TargetDecelerationMps2(clearance)
            * System.Math.Max(0, -witness.VerticalSpeedMps)/System.Math.Max(1, targetDown);
        double verticalAcceleration = System.Math.Max(0, gravity + feedForward
            + _definition.VerticalDampingPerSecond*(-witness.VerticalSpeedMps-targetDown));
        double tanTilt = System.Math.Tan(tiltLimit*MathUtils.DEG_TO_RAD);
        var horizontalAcceleration = tanTilt > 1e-9 ? horizontal*(-_definition.LateralDampingPerSecond) : Vector3d.Zero;
        // Brake drift inside the tilt cone of the vertical demand: the descent profile keeps
        // priority, so drift braking grows as the profile tightens instead of stalling the fall.
        if (horizontalAcceleration.Magnitude > verticalAcceleration*tanTilt)
            horizontalAcceleration = horizontalAcceleration.Normalized*(verticalAcceleration*tanTilt);
        double demandAcceleration = System.Math.Sqrt(verticalAcceleration*verticalAcceleration
            + horizontalAcceleration.MagnitudeSquared);
        // Engine selection from the demand (before any minimum-thrust surplus): continuous
        // burn with monotone count reduction avoids exhausting restart limits, never a
        // fabricated sub-minimum throttle or extra relight. Three engines fly the flip.
        if (Phase == Flight14LandingPhase.Flip)
            _engines.SelectEngineCount(3);
        else
            SelectEngines(demandAcceleration*_ship.TotalMass, pressure, model.GimbalRangeDeg);
        // Raptors cannot throttle below their minimum: when the lit engines' minimum thrust
        // exceeds the demand, that surplus is steered into drift braking (within the cone, at
        // the same cascade-safe drift gain) instead of being spent as extra vertical braking.
        double floorAcceleration = _engines.GetFullThrottleThrustMagnitude(pressure)
            *_engines.Definition.MinThrottle/System.Math.Max(1, _ship.TotalMass);
        if (tanTilt > 1e-9 && horizontal.MagnitudeSquared > 1e-6 && floorAcceleration > demandAcceleration)
        {
            double sinTilt = System.Math.Sin(tiltLimit*MathUtils.DEG_TO_RAD);
            double lateral = System.Math.Min(System.Math.Sqrt(System.Math.Max(0,
                    floorAcceleration*floorAcceleration - verticalAcceleration*verticalAcceleration)),
                System.Math.Min(floorAcceleration*sinTilt, horizontal.Magnitude*_definition.LateralDampingPerSecond));
            if (lateral > horizontalAcceleration.Magnitude)
                horizontalAcceleration = horizontal.Normalized*(-lateral);
            verticalAcceleration = System.Math.Max(verticalAcceleration,
                System.Math.Sqrt(System.Math.Max(0, floorAcceleration*floorAcceleration - lateral*lateral)));
        }
        var thrustAcceleration = up*verticalAcceleration + horizontalAcceleration;
        var aim = thrustAcceleration.MagnitudeSquared > 1e-12 ? thrustAcceleration.Normalized : up;
        _ship.SASEnabled = false;
        _ship.PilotRateEnvelopeEnabled = false;
        _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(_ship.Orientation, Vector3d.Up,
            aim, _ship.AngularVelocity, _definition.ProportionalAttitudeGain,
            Phase == Flight14LandingPhase.Flip ? _definition.FlipAttitudeDampingGain : _definition.AttitudeDampingGain);
        if (Phase == Flight14LandingPhase.Flip)
        {
            _ship.Throttle = _engines.ApplyThrottleFloor(_engines.Definition.MinThrottle);
            if (HasDeliveredThreeSeaLevelEngines && _ship.Orientation.Rotate(Vector3d.Up).Dot(aim)
                >= System.Math.Cos(15*MathUtils.DEG_TO_RAD)) Phase = Flight14LandingPhase.Braking;
            return;
        }
        double alignment = System.Math.Max(0.2, _ship.Orientation.Rotate(Vector3d.Up).Dot(aim));
        double rated = _engines.GetFullThrottleThrustMagnitude(pressure);
        _ship.Throttle = _engines.ApplyThrottleFloor(System.Math.Max(_engines.Definition.MinThrottle,
            demandAcceleration*_ship.TotalMass/alignment/System.Math.Max(1, rated)));
    }

    /// <summary>
    /// Monotone engine-count selection: the fewest engines that keep the demanded thrust within
    /// <see cref="ThrottleHeadroom"/> of rated and whose minimum-throttle gimbal moment exceeds
    /// the predicted aerodynamic moment by <see cref="ControlAuthorityMargin"/>.
    /// </summary>
    private void SelectEngines(double force, double pressure, double gimbalRangeDeg)
    {
        int authorityCount = 1;
        _engines.SelectEngineCount(1);
        double perEngineMinimumThrust = _engines.GetFullThrottleThrustMagnitude(pressure)*_engines.Definition.MinThrottle;
        if (_ship.Parts.TryGetPartLocalPosition(_engines, out var engineStation))
        {
            double arm = System.Math.Abs(_ship.CenterOfMassOffsetLocal.Y - engineStation.Y);
            double perEngineMoment = perEngineMinimumThrust*System.Math.Sin(gimbalRangeDeg*MathUtils.DEG_TO_RAD)*arm;
            var aero = _ship.PredictStaticAerodynamicTorqueBody(_body);
            double transverse = System.Math.Sqrt(aero.X*aero.X + aero.Z*aero.Z);
            if (perEngineMoment > 0)
                authorityCount = (int)System.Math.Ceiling(ControlAuthorityMargin*transverse/perEngineMoment);
        }
        int selected = _engineCount;
        for (int count = 1; count <= _engineCount; count++)
        {
            _engines.SelectEngineCount(count);
            if (count >= authorityCount && force <= ThrottleHeadroom*_engines.GetFullThrottleThrustMagnitude(pressure))
            { selected = count; break; }
        }
        _engineCount = selected; _engines.SelectEngineCount(selected);
        MinimumSelectedEngineCount = System.Math.Min(MinimumSelectedEngineCount, selected);
    }

    /// <summary>
    /// Throttle fraction a selected engine set may need; the rest is control margin (why the
    /// real ship brakes on two engines and lands on one).
    /// </summary>
    private const double ThrottleHeadroom = 0.8;

    /// <summary>Gimbal moment margin over the predicted aerodynamic moment when selecting engines.</summary>
    private const double ControlAuthorityMargin = 2.0;

    private Flight14LandingWitness Witness(Universe universe)
    {
        _body.GetGeodeticCoordinatesAtTime(_ship.Position, universe.CurrentTime, out double lat, out double lon, out _);
        var up = _body.GetGeodeticUp(_ship.Position); var velocity = _ship.GetSurfaceVelocity(_body);
        return new(universe.CurrentTime-_liftoffEpoch, universe.CurrentTime, _ship.GetAltitude(_body),
            velocity.Magnitude, velocity.Dot(up), lat, lon,
            _ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer+p.HeaderLiquidFuel+p.HeaderOxidizer), _ship.Orientation.Rotate(Vector3d.Up).Dot(up),
            _ship.AngularVelocity.Magnitude);
    }
    private void Block(string reason)
    {
        _ship.Throttle = 0; _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14LandingPhase.Blocked; BlockReason = reason;
    }
}
