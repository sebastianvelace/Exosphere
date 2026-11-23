namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public enum TowerCatchBurnPhase { Flip, Braking, Cutoff, Caught, Blocked }

/// <summary>Engine count selected from <see cref="TimeS"/> seconds after ignition.</summary>
public readonly record struct TowerCatchEngineStep(double TimeS, int EngineCount);

/// <summary>
/// The single landing burn of a tower catch, as flown on Flight 15: three sea-level Raptors
/// light out of the belly flop and turn the ship upright, the count falls monotonically as the
/// demand drops (three, two, one), and the last engine cuts when the pins can take the drop.
/// No engine relights. It reuses the powered-descent vector law of
/// <see cref="Flight14LandingBurn"/>, with the clearance measured to the cradle and a
/// horizontal channel that homes onto the cradle instead of only damping drift.
/// </summary>
public sealed class TowerCatchBurn
{
    private const double MissedCatchTimeoutS = 10.0;
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Part _engines;
    private readonly TowerCatchBurnDefinition _definition;
    private readonly EngineModelDefinition _model;
    private readonly List<TowerCatchEngineStep> _engineSteps = [];
    private readonly Dictionary<string, int> _startsAtIgnition = [];
    private int _engineCount = 3;
    private double _ignitionTime = double.NaN;
    private Vector3d _lateralIntegral;
    private Vector3d _thrustTrim;
    private Vector3d _previousHorizontalVelocity;
    private Vector3d _lateralAcceleration;
    private bool _hasPreviousHorizontalVelocity;
    private const double LateralAccelerationFilterS = 0.5;
    private bool _settlingOnPins;
    /// <summary>Miss, as a multiple of the alignment tolerance, that abandons the settle onto the pins.</summary>
    private const double SettleReleaseFactor = 1.5;
    private const double ThrustTrimTimeConstantS = 1.0;

    public TowerCatchBurnPhase Phase { get; private set; } = TowerCatchBurnPhase.Flip;
    public string? BlockReason { get; private set; }
    public IReadOnlyList<TowerCatchEngineStep> EngineSteps => _engineSteps;
    public double CutoffTimeS { get; private set; } = double.NaN;
    public double CaughtTimeS { get; private set; } = double.NaN;
    public double IgnitionClearanceM { get; private set; } = double.NaN;
    public double IgnitionDescentSpeedMps { get; private set; } = double.NaN;
    public TowerCatchCutoffDecision LastCutoffDecision { get; private set; }
    public double MaximumHorizontalMissM { get; private set; }
    public double MaximumAngularRateRadPerSecond { get; private set; }
    /// <summary>Seconds from ignition to the end of the flip.</summary>
    public double FlipCompletedS { get; private set; } = double.NaN;
    /// <summary>Horizontal datum offset from the cradle at the end of the flip, in the cradle frame.</summary>
    public Vector3d FlipEndHorizontalOffset { get; private set; }
    public Vector3d FlipEndHorizontalVelocity { get; private set; }
    /// <summary>Roll the pins still need to face the arms, folded to ±90° (rad).</summary>
    public double RollErrorRad { get; private set; }
    /// <summary>Thrust axis the last step commanded, in world axes.</summary>
    public Vector3d LastAimWorld { get; private set; }
    /// <summary>Velocity-integral term of the last step (m/s²).</summary>
    public Vector3d LastLateralIntegral => _lateralIntegral;
    /// <summary>Horizontal acceleration the last step asked for (m/s²).</summary>
    public Vector3d LastHorizontalAccelerationCommand { get; private set; }

    public TowerCatchBurn(Vessel ship, CelestialBody body, TowerCatchBurnDefinition definition,
        string seaLevelModelId)
    {
        definition.Validate();
        if (!ship.HasCatchPins) throw new ArgumentException("The ship carries no catch pins.", nameof(ship));
        _ship = ship; _body = body; _definition = definition;
        _engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        if (!_engines.Definition.ResolvedEngineModels.TryGetValue(seaLevelModelId, out var model))
            throw new ArgumentException($"Unknown sea-level engine model '{seaLevelModelId}'.", nameof(seaLevelModelId));
        _model = model;
    }

    /// <summary>Engine starts beyond the single ignition of each engine since the burn began.</summary>
    public int Relights => _engines.EngineStates.Sum(e =>
        _startsAtIgnition.TryGetValue(e.InstanceId, out int start) ? System.Math.Max(0, e.StartAttempts - start - 1) : 0);

    public double ElapsedS(Universe universe) =>
        double.IsNaN(_ignitionTime) ? 0.0 : universe.CurrentTime - _ignitionTime;

    /// <summary>Pin clearance above the cradle if the ship stood upright at its current centre of mass.</summary>
    public static double UprightPinClearanceM(Vessel ship, double simulationTime)
    {
        var up = ship.CatchTargetUpWorld.Normalized;
        var cradle = ship.GetCatchTargetPositionAt(simulationTime);
        return (ship.CenterOfMass - cradle).Dot(up)
            - ship.CenterOfMassOffsetLocal.Y + ship.CatchPinContactHeightAboveDatumM;
    }

    public void Advance(Universe universe, double interval)
    {
        if (!double.IsFinite(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        if (Phase is TowerCatchBurnPhase.Blocked or TowerCatchBurnPhase.Caught) return;
        if (_ship.IsDestroyed || _ship.StructuralControlLost) { Block("vehicle-lost"); return; }

        if (double.IsNaN(_ignitionTime))
        {
            _ignitionTime = universe.CurrentTime;
            foreach (var engine in _engines.EngineStates)
                _startsAtIgnition[engine.InstanceId] = engine.StartAttempts;
            IgnitionClearanceM = UprightPinClearanceM(_ship, universe.CurrentTime);
            IgnitionDescentSpeedMps = -(_ship.Velocity - _ship.CatchTargetVelocityWorld).Dot(_ship.CatchTargetUpWorld.Normalized);
            RecordEngines(universe, 3);
        }

        if (Phase == TowerCatchBurnPhase.Cutoff)
        {
            CutEngines();
            if (_ship.IsCaught) { Phase = TowerCatchBurnPhase.Caught; CaughtTimeS = ElapsedS(universe); }
            else if (ElapsedS(universe) - CutoffTimeS > MissedCatchTimeoutS) Block("pins-did-not-settle");
            return;
        }
        if (ElapsedS(universe) > _definition.MaximumBurnSeconds) { Block("burn-duration"); return; }
        if (_engines.IsBroken || _engines.FuelDepleted) { Block("engines-unavailable"); return; }

        var up = _ship.CatchTargetUpWorld.Normalized;
        var cradle = _ship.GetCatchTargetPositionAt(universe.CurrentTime);
        var relativeVelocity = _ship.Velocity - _ship.CatchTargetVelocityWorld;
        double verticalSpeed = relativeVelocity.Dot(up);
        var horizontalVelocity = relativeVelocity - up * verticalSpeed;
        var offset = _ship.Position - cradle;
        var horizontalOffset = offset - up * offset.Dot(up);
        MaximumHorizontalMissM = System.Math.Max(MaximumHorizontalMissM, horizontalOffset.Magnitude);
        MaximumAngularRateRadPerSecond = System.Math.Max(MaximumAngularRateRadPerSecond, _ship.AngularVelocity.Magnitude);
        double gravity = _body.GM / (_ship.Position - _body.Position).MagnitudeSquared;
        double pressure = _ship.GetAmbientPressure(_body);

        if (Phase == TowerCatchBurnPhase.Braking)
        {
            var geometry = TowerCatchPinGeometry.Measure(_ship.CatchContactPoints, offset, _ship.Orientation, up,
                _ship.CatchTargetRails, 0.5 * _ship.MaximumDiameter);
            if (geometry.EnabledPins == 2 && geometry.LastPinGapM <= TowerCatchCutoff.MaxGapM)
            {
                var (transverseRate, rollRate) = SplitAngularRate(_ship);
                LastCutoffDecision = TowerCatchCutoff.Decide(geometry, _ship.CatchContactPoints, _ship.TotalMass,
                    gravity, verticalSpeed, horizontalVelocity.Magnitude, transverseRate, rollRate);
                if (LastCutoffDecision.Commit)
                {
                    Phase = TowerCatchBurnPhase.Cutoff;
                    CutoffTimeS = ElapsedS(universe);
                    CutEngines();
                    RecordEngines(universe, 0);
                    return;
                }
            }
        }

        double clearance = UprightPinClearanceM(_ship, universe.CurrentTime);
        // The burn flies to the pins, not to the ground below them. Far from the tower and low
        // over the cradle, hand over to a leg landing while there is height to do it.
        if (Phase == TowerCatchBurnPhase.Braking
            && clearance < _definition.MissedApproachClearanceM
            && horizontalOffset.Magnitude > _definition.MissedApproachRadiusM)
        { Block("missed-approach"); return; }
        // With rails, "centred enough to descend" is where both fittings would land on their rails
        // with margin, not a fixed radius: the catch zone runs ±9.5 m along the arms.
        double missForHold = horizontalOffset.Magnitude;
        if (_ship.CatchTargetRails.IsDefined && Phase == TowerCatchBurnPhase.Braking)
        {
            var railGeometry = TowerCatchPinGeometry.Measure(_ship.CatchContactPoints, offset, _ship.Orientation, up,
                _ship.CatchTargetRails, 0.5 * _ship.MaximumDiameter);
            if (railGeometry.UsesRails && railGeometry.RailSlackM >= _definition.RailDescentSlackM)
                missForHold = 0.0;
        }
        // Hysteresis: once the ship is centred and below the hold clearance it keeps settling
        // onto the pins unless the miss grows well past the tolerance. Without it a drift that
        // crosses the threshold in the last half metre sent the ship back up to the hold.
        if (Phase == TowerCatchBurnPhase.Braking)
        {
            if (missForHold <= _definition.AlignedMissM && clearance < _definition.AlignmentHoldClearanceM)
                _settlingOnPins = true;
            else if (horizontalOffset.Magnitude > _definition.AlignedMissM * SettleReleaseFactor)
                _settlingOnPins = false;
            if (_settlingOnPins) missForHold = 0.0;
        }
        double targetDown = _definition.TargetDescentSpeedMps(clearance, missForHold);
        double feedForward = targetDown > 0.0
            ? _definition.TargetDecelerationMps2(clearance)
                * System.Math.Max(0, -verticalSpeed) / System.Math.Max(1, targetDown)
            : 0.0;
        double verticalAcceleration = System.Math.Max(0, gravity + feedForward
            + _definition.VerticalDampingPerSecond * (-verticalSpeed - targetDown));
        double tiltLimit = Phase == TowerCatchBurnPhase.Flip
            ? _definition.FlipMaximumTiltDegrees : _definition.MaximumTiltDegrees;
        double tanTilt = System.Math.Tan(tiltLimit * MathUtils.DEG_TO_RAD);
        // Command a bounded closing speed toward the cradle and track it. A position-gain law
        // saturates the tilt cone on a large miss and overshoots; a speed command does not.
        // Close on a constant-deceleration profile, linear near the cradle so the command has
        // a finite slope there, and capped far out.
        double miss = horizontalOffset.Magnitude;
        double closingSpeed = System.Math.Min(_definition.MaximumClosingSpeedMps, System.Math.Min(
            System.Math.Sqrt(2.0 * _definition.ClosingDecelerationMps2 * miss),
            _definition.LateralClosingGainPerSecond(Phase == TowerCatchBurnPhase.Flip ? 3 : _engineCount) * miss));
        var closing = miss > 1e-6 ? horizontalOffset * (-closingSpeed / miss) : Vector3d.Zero;
        // Attitude authority grows with the lit engines, and the lateral loop must stay several
        // times slower than the attitude loop: one Raptor lags the commanded tilt by seconds.
        int lit = Phase == TowerCatchBurnPhase.Flip ? 3 : _engineCount;
        double lateralGain = _definition.LateralGainPerSecond(lit);
        // The tilt, and with it the sideways thrust, follows the command only after the attitude
        // lag. Feed back the velocity that the thrust acting now will have produced one lag
        // ahead, so the loop stops leaning toward the cradle before it overshoots it.
        // Use the measured sideways acceleration, not the thrust: in a steady hold the thrust
        // still balances the off-axis engine and aero, while the acceleration is zero.
        if (_hasPreviousHorizontalVelocity)
        {
            var measured = (horizontalVelocity - _previousHorizontalVelocity) / interval;
            double blend = System.Math.Min(1.0, interval / LateralAccelerationFilterS);
            _lateralAcceleration += (measured - _lateralAcceleration) * blend;
        }
        _previousHorizontalVelocity = horizontalVelocity;
        _hasPreviousHorizontalVelocity = true;
        var predictedVelocity = Phase == TowerCatchBurnPhase.Braking
            ? horizontalVelocity + _lateralAcceleration * _definition.LateralLagSeconds(lit)
            : horizontalVelocity;
        var homing = (closing - predictedVelocity) * lateralGain;
        // Integral on the velocity error: one or two of the three ring Raptors put the thrust
        // off the axis and push sideways steadily; a velocity integral cancels that without
        // winding up on the position error. It restarts at each engine change.
        if (Phase == TowerCatchBurnPhase.Braking && _engineCount <= 2
            && horizontalOffset.Magnitude <= _definition.LateralIntegralRadiusM)
        {
            _lateralIntegral += (closing - horizontalVelocity) * (_definition.LateralIntegralGainPerS3 * interval);
            if (_lateralIntegral.Magnitude > _definition.LateralIntegralLimitMps2)
                _lateralIntegral = _lateralIntegral.Normalized * _definition.LateralIntegralLimitMps2;
            homing += _lateralIntegral;
        }
        var horizontalAcceleration = homing;
        if (horizontalAcceleration.Magnitude > verticalAcceleration * tanTilt)
            horizontalAcceleration = horizontalAcceleration.Normalized * (verticalAcceleration * tanTilt);
        double demandAcceleration = System.Math.Sqrt(verticalAcceleration * verticalAcceleration
            + horizontalAcceleration.MagnitudeSquared);

        if (Phase == TowerCatchBurnPhase.Flip)
            _engines.SelectEngineCount(3);
        else
        {
            // Shutdown is irreversible: keep enough engines to stop the current descent in the
            // clearance that remains, not only to meet this instant's demand.
            double descent = System.Math.Max(0.0, -verticalSpeed);
            double room = System.Math.Max(1.0, clearance - _definition.AlignmentHoldClearanceM);
            double arrestForce = _ship.TotalMass * (gravity + descent * descent / (2.0 * room))
                * _definition.ThrottleHeadroom / _definition.ArrestThrottleHeadroom;
            int selected = MonotoneLandingEngineSelection.Select(_ship, _body, _engines, _engineCount,
                System.Math.Max(demandAcceleration * _ship.TotalMass, arrestForce), pressure,
                _model.GimbalRangeDeg, _definition.ThrottleHeadroom, _definition.ControlAuthorityMargin);
            // The last engine settles the ship; it cannot also fly it across. Keep the second
            // engine until the ship is moving slowly and close over the cradle, as on Flight 15
            // where two engines carried it between the arms and one ran for the final second.
            // Two engines at their floor out-thrust the weight, so this only holds while the
            // ship is still coming down; once it stops descending the second engine must go.
            if (_engineCount == 2 && selected == 1 && verticalSpeed < -0.5
                && (horizontalVelocity.Magnitude > _definition.SingleEngineMaximumLateralSpeedMps
                    || horizontalOffset.Magnitude > _definition.SingleEngineMaximumMissM))
            {
                selected = 2;
                _engines.SelectEngineCount(2);
            }
            if (selected != _engineCount)
            {
                RecordEngines(universe, selected);
                _lateralIntegral = Vector3d.Zero;
            }
            _engineCount = selected;
        }

        // The lit engines cannot throttle below their floor. Steer that surplus into the homing
        // direction inside the tilt cone instead of spending it all as extra climb.
        double floorAcceleration = _engines.GetFullThrottleThrustMagnitude(pressure)
            * _engines.Definition.MinThrottle / System.Math.Max(1, _ship.TotalMass);
        if (tanTilt > 1e-9 && homing.MagnitudeSquared > 1e-6 && floorAcceleration > demandAcceleration)
        {
            double sinTilt = System.Math.Sin(tiltLimit * MathUtils.DEG_TO_RAD);
            double lateral = System.Math.Min(System.Math.Sqrt(System.Math.Max(0,
                    floorAcceleration * floorAcceleration - verticalAcceleration * verticalAcceleration)),
                System.Math.Min(floorAcceleration * sinTilt, homing.Magnitude));
            if (lateral > horizontalAcceleration.Magnitude)
                horizontalAcceleration = homing.Normalized * lateral;
            verticalAcceleration = System.Math.Max(verticalAcceleration,
                System.Math.Sqrt(System.Math.Max(0, floorAcceleration * floorAcceleration - lateral * lateral)));
        }

        var thrustAcceleration = up * verticalAcceleration + horizontalAcceleration;
        var aim = thrustAcceleration.MagnitudeSquared > 1e-12 ? thrustAcceleration.Normalized : up;
        // Ring Raptors sit off the axis, so the gimbals that hold attitude also turn the thrust
        // off the axis. Point the axis so the measured thrust, not the axis, lands on the aim.
        var bodyThrust = _ship.Parts.GetTotalThrust(pressure);
        if (Phase == TowerCatchBurnPhase.Braking && bodyThrust.MagnitudeSquared > 1.0)
        {
            var axisWorld = _ship.Orientation.Rotate(Vector3d.Up).Normalized;
            var offAxis = _ship.Orientation.Rotate(bodyThrust).Normalized - axisWorld;
            offAxis -= up * offAxis.Dot(up);
            double blend = System.Math.Min(1.0, interval / ThrustTrimTimeConstantS);
            _thrustTrim += (offAxis - _thrustTrim) * blend;
            aim = (aim - _thrustTrim).Normalized;
        }
        LastAimWorld = aim;
        LastHorizontalAccelerationCommand = horizontalAcceleration;
        _ship.SASEnabled = false;
        _ship.PilotRateEnvelopeEnabled = false;
        _ship.FlapCommandOverride = null;
        _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(_ship.Orientation, Vector3d.Up,
            aim, _ship.AngularVelocity, _definition.ProportionalAttitudeGain,
            Phase == TowerCatchBurnPhase.Flip ? _definition.FlipAttitudeDampingGain : _definition.AttitudeDampingGain,
            // An off-axis Raptor rolls the ship; a free roll turns its steady side force around
            // the vertical and the homing then orbits the cradle instead of closing on it.
            rollDampingGain: Phase == TowerCatchBurnPhase.Flip ? 0.0 : _definition.RollDampingGain);
        if (Phase == TowerCatchBurnPhase.Braking && _ship.CatchTargetRails.IsDefined)
        {
            double rollError = PinRollErrorRad(_ship.Orientation, up, _ship.CatchTargetRails.ArmAxisWorld);
            RollErrorRad = rollError;
            // Roll only once the ship is nearly upright and close to the arms. Rolling a ship
            // tilted 25-36 degrees spins its tilt around the vertical and throws it sideways.
            double tilt = System.Math.Acos(System.Math.Clamp(
                _ship.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up), -1.0, 1.0));
            if (tilt <= _definition.RollAlignmentMaximumTiltDegrees * MathUtils.DEG_TO_RAD
                && clearance <= _definition.RollAlignmentClearanceM)
            {
                var command = _ship.PitchYawRoll;
                double roll = System.Math.Clamp(_definition.RollAlignmentGain * rollError,
                    -_definition.RollAlignmentCommandLimit, _definition.RollAlignmentCommandLimit);
                _ship.PitchYawRoll = new Vector3d(command.X, command.Y,
                    System.Math.Clamp(command.Z + roll, -1.0, 1.0));
            }
        }
        if (Phase == TowerCatchBurnPhase.Flip)
        {
            _ship.Throttle = _engines.ApplyThrottleFloor(_engines.Definition.MinThrottle);
            if (_ship.Orientation.Rotate(Vector3d.Up).Dot(aim) >= System.Math.Cos(15 * MathUtils.DEG_TO_RAD))
            {
                Phase = TowerCatchBurnPhase.Braking;
                FlipCompletedS = ElapsedS(universe);
                FlipEndHorizontalOffset = horizontalOffset;
                FlipEndHorizontalVelocity = horizontalVelocity;
            }
            return;
        }
        double alignment = System.Math.Max(0.2, _ship.Orientation.Rotate(Vector3d.Up).Dot(aim));
        double rated = _engines.GetFullThrottleThrustMagnitude(pressure);
        _ship.Throttle = _engines.ApplyThrottleFloor(System.Math.Max(_engines.Definition.MinThrottle,
            demandAcceleration * _ship.TotalMass / alignment / System.Math.Max(1, rated)));
    }

    /// <summary>
    /// Roll about the vehicle axis that turns the pin direction (body ±Z) across the arms, so
    /// each fitting faces a rail. Either pin may take either arm, so the error folds to ±90°.
    /// </summary>
    public static double PinRollErrorRad(Quaterniond orientation, Vector3d upWorld, Vector3d armAxisWorld)
    {
        var axis = orientation.Rotate(Vector3d.Up).Normalized;
        var pins = orientation.Rotate(Vector3d.Forward);
        var across = upWorld.Normalized.Cross(armAxisWorld).Normalized;
        var pinsPlane = (pins - axis * pins.Dot(axis)).Normalized;
        var acrossPlane = (across - axis * across.Dot(axis)).Normalized;
        double angle = System.Math.Atan2(pinsPlane.Cross(acrossPlane).Dot(axis), pinsPlane.Dot(acrossPlane));
        if (angle > 0.5 * System.Math.PI) angle -= System.Math.PI;
        if (angle < -0.5 * System.Math.PI) angle += System.Math.PI;
        return angle;
    }

    /// <summary>Angular rate across the vehicle axis (pitch and yaw) and about it (roll).</summary>
    public static (double Transverse, double Roll) SplitAngularRate(Vessel ship)
    {
        var axis = ship.Orientation.Rotate(Vector3d.Up).Normalized;
        double roll = ship.AngularVelocity.Dot(axis);
        return ((ship.AngularVelocity - axis * roll).Magnitude, roll);
    }

    private void CutEngines()
    {
        _engines.SelectEngineCount(0);
        _ship.Throttle = 0.0;
        _ship.PitchYawRoll = Vector3d.Zero;
        _ship.FlapCommandOverride = null;
    }

    private void RecordEngines(Universe universe, int count) =>
        _engineSteps.Add(new TowerCatchEngineStep(ElapsedS(universe), count));

    private void Block(string reason)
    {
        BlockReason = reason;
        Phase = TowerCatchBurnPhase.Blocked;
        CutEngines();
    }
}
