namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Physical belly-flop attitude for the single-burn tower catch, from the entry interface to
/// ignition, writing only control commands. Above the terminal attitude speed it flies the
/// Flight 14/15 descent attitude stack (trim-feasible angle of attack, slew- and lead-limited
/// belly-first reference, flap command with static-moment inversion).
///
/// Below it, a terminal attitude loop designed on the plant identified on the Flight 15 catch
/// bench (2026-11-24) tracks the angle of attack and heading on time-optimal reference profiles
/// inside the flaps' measured margins, with the roll held belly-down. Below the terminal heading
/// speed a drift guidance uses the angle of attack as an acceleration along the nose
/// (a_tail = c (α − α₀) − v_tail g/V, α₀ ≈ 89°, c ≈ 0.175 m/s² per degree) to reach the ignition
/// point on the lead circle with the nose toward the cradle. The zero-drift angle lies inside
/// the 85–95° where flown and computed belly flops reach C_L ≈ 0 (Li 2022; Chen 2025).
/// </summary>
public sealed class TowerCatchPhysicalGlide
{
    private Quaterniond _reference;
    private bool _initialized;
    private double _angleOffset;
    private Vector3d _approachAxis;
    private Vector3d _noseHeading;

    public double LastAngleOfAttackDegrees { get; private set; }
    public double LastAngleOffsetDegrees { get; private set; }
    public Vector3d LastLiftReference { get; private set; }
    /// <summary>Horizontal acceleration (m/s², world) the terminal guidance asked for last.</summary>
    public Vector3d LastWantedAcceleration { get; private set; }
    /// <summary>Time to the aim altitude (s) the terminal guidance used last.</summary>
    public double LastTimeToGoS { get; private set; }
    public Quaterniond Reference => _reference;

    /// <summary>Identification hook: holds the nose on this horizontal heading below the terminal speed.</summary>
    public Vector3d? IdentificationNoseHeading { get; set; }
    /// <summary>Identification hook: commands this angle of attack (deg) below the terminal speed.</summary>
    public double? IdentificationAngleOfAttackDegrees { get; set; }

    private double _angleErrorIntegral;
    private double _shapedAngle = double.NaN;
    private double _shapedAngleRate;
    private Vector3d _headingFrame;
    private double _shapedHeading = double.NaN;
    private double _shapedHeadingRate;
    private Vector3d _heldHeading;

    public void Reset() { _initialized = false; _angleOffset = 0.0; _approachAxis = Vector3d.Zero; _noseHeading = Vector3d.Zero;
        _angleErrorIntegral = 0.0; _shapedAngle = double.NaN; _shapedAngleRate = 0.0;
        _headingFrame = Vector3d.Zero; _shapedHeading = double.NaN; _shapedHeadingRate = 0.0;
        _heldHeading = Vector3d.Zero; }

    /// <summary>Tracking error integral (deg) of the terminal attitude loop.</summary>
    public double LastAngleTrackingIntegralDegrees => _angleErrorIntegral;

    /// <summary>Horizontal approach axis toward the cradle, held fixed near it.</summary>
    public Vector3d ApproachAxis => _approachAxis;

    /// <param name="guidanceLift">Lift direction the footprint guidance asks for.</param>
    /// <param name="cradlePosition">Cradle position, world frame.</param>
    public Vector3d Command(Vessel ship, CelestialBody body, Flight14DescentDefinition descent,
        TowerCatchBurnDefinition catchDefinition, Vector3d guidanceLift, Vector3d cradlePosition,
        double controlIntervalSeconds)
    {
        var surfaceVelocity = ship.GetSurfaceVelocity(body);
        double speed = surfaceVelocity.Magnitude;
        if (speed < 1.0) return Vector3d.Zero;
        var flow = surfaceVelocity / speed;
        double altitude = body.GetAltitude(ship.Position);
        if (!_initialized) { _reference = ship.Orientation; _initialized = true; }

        var liftReference = guidanceLift;
        double angleOffset = 0.0;
        if (speed <= catchDefinition.GlideTerminalHeadingSpeedMps)
        {
            var up = body.GetGeodeticUp(ship.Position);
            var toCradle = cradlePosition - ship.Position;
            toCradle -= up * toCradle.Dot(up);
            var horizontal = surfaceVelocity - up * surfaceVelocity.Dot(up);
            // Identified on the physical terminal glide (2026-11-24): the angle of attack sets an
            // acceleration along the nose, a_tail = c (α − α₀) − v_tail g/V. The second term is the
            // hull's horizontal drag, so a held angle settles to a drift speed K (α − α₀) with
            // K = c V/g after a lag τ = V/g of about 13 s. The guidance asks for a horizontal
            // acceleration that brings the ship to the ignition point by the aim altitude (zero-
            // effort miss, optionally weighted toward arriving at rest), points the nose along it
            // within a cone about the approach axis (the drift acts along the nose, both ways),
            // and inverts the plant for the angle.
            if (_approachAxis.MagnitudeSquared < 0.5)
            {
                var nose = ship.Orientation.Rotate(Vector3d.Up);
                nose -= up * nose.Dot(up);
                _approachAxis = toCradle.Magnitude > catchDefinition.GlideAxisMinimumDistanceM ? toCradle.Normalized
                    : nose.Magnitude > 1e-3 ? nose.Normalized : up.Cross(Vector3d.Up).Normalized;
                // A nose pointing away from the cradle would need a 180° turn at a few degrees a
                // second, with the drift lost while it turns. Approach from the far side instead:
                // tail first (the strong drift direction) over the cradle to the lead circle beyond
                // it, arriving with the nose already toward the cradle.
                if (catchDefinition.GlideRadialApproach && nose.MagnitudeSquared > 1e-6
                    && toCradle.Magnitude > catchDefinition.GlideAxisMinimumDistanceM
                    && FarSideIsFaster(catchDefinition, nose.Normalized, toCradle))
                    _approachAxis = -_approachAxis;
                _noseHeading = _approachAxis;
            }
            // The axis follows the bearing to the cradle (the ignition point is anywhere on the
            // lead circle, nose toward the cradle) until the final alignment, then holds.
            // Near the cradle the bearing swings fast as the ship passes over it, so there the axis
            // only turns toward it at a bounded rate.
            if (catchDefinition.GlideRadialApproach && altitude > catchDefinition.GlideFinalAlignmentAltitudeM
                && toCradle.MagnitudeSquared > 1.0)
            {
                var bearing = toCradle.Normalized * (toCradle.Dot(_approachAxis) < 0.0 ? -1.0 : 1.0);
                _approachAxis = toCradle.Magnitude > catchDefinition.GlideAxisMinimumDistanceM ? bearing
                    : TurnToward(_approachAxis, bearing, up,
                        catchDefinition.GlideAxisTurnRateDegPerS * MathUtils.DEG_TO_RAD * controlIntervalSeconds);
            }
            _approachAxis = (_approachAxis - up * _approachAxis.Dot(up)).Normalized;
            var across = up.Cross(_approachAxis).Normalized;
            // Ignition point: the lead distance short of the cradle along the approach axis.
            var offset = -(toCradle - _approachAxis * catchDefinition.IgnitionLeadM);
            double sink = System.Math.Max(1.0, -surfaceVelocity.Dot(up));
            double timeToGo = System.Math.Max(catchDefinition.GlideMinimumTimeToGoS,
                (altitude - catchDefinition.GlideAimAltitudeM) / sink);
            // Zero-effort miss (and, weighted, zero-effort velocity) over the time to go.
            double zev = catchDefinition.GlideArrivalVelocityWeight;
            var wanted = -offset * ((3.0 + 3.0 * zev) / (timeToGo * timeToGo))
                - horizontal * ((3.0 + zev) / timeToGo);
            if (wanted.Magnitude > catchDefinition.GlideMaximumAccelerationMps2)
                wanted = wanted.Normalized * catchDefinition.GlideMaximumAccelerationMps2;
            // Nose along the wanted acceleration line, kept within the cone about the axis (and on
            // the axis itself below the final alignment altitude, for the flip).
            double cone = altitude > catchDefinition.GlideFinalAlignmentAltitudeM
                ? catchDefinition.GlideNoseConeDegrees * MathUtils.DEG_TO_RAD : 0.0;
            double lineAngle = System.Math.Atan(wanted.Dot(across) / System.Math.Max(1e-6, System.Math.Abs(wanted.Dot(_approachAxis))))
                * System.Math.Sign(wanted.Dot(_approachAxis) == 0.0 ? 1.0 : wanted.Dot(_approachAxis));
            var noseTarget = Quaterniond.FromAxisAngle(up, System.Math.Clamp(lineAngle, -cone, cone)).Rotate(_approachAxis);
            _noseHeading = TurnToward(_noseHeading, noseTarget, up,
                catchDefinition.GlideHeadingRateDegPerS * MathUtils.DEG_TO_RAD * controlIntervalSeconds);
            liftReference = _noseHeading;
            // Invert the plant along the nose as it actually points.
            var actualNose = ship.Orientation.Rotate(Vector3d.Up);
            actualNose -= up * actualNose.Dot(up);
            if (actualNose.MagnitudeSquared > 1e-6)
            {
                actualNose = actualNose.Normalized;
                double tailVelocity = -horizontal.Dot(actualNose);
                double tailAcceleration = -wanted.Dot(actualNose);
                double gravity = body.GetGravityAt(ship.Position).Magnitude;
                angleOffset = catchDefinition.GlideZeroDriftAngleDegrees - catchDefinition.GlideTerminalAngleOfAttackDegrees
                    + (tailAcceleration + tailVelocity * gravity / speed) / catchDefinition.GlideLiftPerDegreeMps2;
                angleOffset = System.Math.Clamp(angleOffset,
                    catchDefinition.GlideMinimumAngleOfAttackDegrees - catchDefinition.GlideTerminalAngleOfAttackDegrees,
                    catchDefinition.GlideMaximumAngleOfAttackDegrees - catchDefinition.GlideTerminalAngleOfAttackDegrees);
            }
            LastWantedAcceleration = wanted;
            LastTimeToGoS = timeToGo;
        }
        bool identifying = speed <= catchDefinition.GlideTerminalHeadingSpeedMps;
        if (identifying && IdentificationNoseHeading is { } heldHeading) liftReference = heldHeading;
        bool terminalAttitude = speed <= catchDefinition.GlideTerminalAttitudeSpeedMps;
        // The terminal attitude loop shapes the heading itself; keep the wanted horizontal heading.
        // Until the terminal heading guidance gives one, it holds the heading the nose had at
        // takeover: the footprint guidance's lift direction is near vertical and defines none.
        var wantedHeading = liftReference;
        if (terminalAttitude && !identifying)
        {
            if (_heldHeading.MagnitudeSquared < 0.5)
            {
                var upNow = body.GetGeodeticUp(ship.Position);
                var noseNow = ship.Orientation.Rotate(Vector3d.Up);
                noseNow -= upNow * noseNow.Dot(upNow);
                _heldHeading = noseNow.MagnitudeSquared > 1e-6 ? noseNow.Normalized : upNow.Cross(Vector3d.Right).Normalized;
            }
            wantedHeading = _heldHeading;
        }
        liftReference -= flow * liftReference.Dot(flow);
        if (liftReference.MagnitudeSquared < 1e-12) liftReference = LastLiftReference;
        if (liftReference.MagnitudeSquared < 1e-12) return Vector3d.Zero;
        liftReference = liftReference.Normalized;
        LastLiftReference = liftReference;

        double terminalFraction = System.Math.Clamp(
            (descent.AngleOfAttackTransitionSpeedMps - speed)
                / (descent.AngleOfAttackTransitionSpeedMps - descent.TerminalAngleOfAttackSpeedMps), 0.0, 1.0);
        // Once the glide holds its tail toward the cradle it flies the zero-lift belly flop: the
        // descent schedule's intermediate angles cannot be trimmed at catch mass, and flaps near
        // their stops turn the hinge-lean coupling into a heading moment.
        double angle = speed <= catchDefinition.GlideTerminalHeadingSpeedMps
            ? catchDefinition.GlideTerminalAngleOfAttackDegrees + angleOffset
            : descent.EntryAngleOfAttackDegrees + terminalFraction
                * (catchDefinition.GlideTerminalAngleOfAttackDegrees - descent.EntryAngleOfAttackDegrees);
        if (identifying && IdentificationAngleOfAttackDegrees is { } heldAngle) angle = heldAngle;
        angle = Flight14DescentController.TrimFeasibleAngleOfAttack(ship, body, altitude, surfaceVelocity,
            liftReference, angle, descent.TrimMarginDegrees * MathUtils.DEG_TO_RAD);
        LastAngleOfAttackDegrees = angle;
        LastAngleOffsetDegrees = angleOffset;
        _angleOffset = angleOffset;

        var axis = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, angle);
        var target = AerodynamicsModel.ComputeBellyFirstOrientation(axis, flow);
        if (terminalAttitude) return TerminalAttitudeCommand(ship, body, descent, catchDefinition, flow, wantedHeading, angle,
            controlIntervalSeconds);
        ship.BodyFlapAllocationAxisWeights = null;
        _reference = AttitudeGuidance.SlewQuaternion(_reference, target, controlIntervalSeconds,
            speed <= catchDefinition.GlideTerminalHeadingSpeedMps
                ? System.Math.Max(descent.ReferenceSlewRateRadPerSecond, catchDefinition.GlideReferenceSlewRateRadPerSecond)
                : descent.ReferenceSlewRateRadPerSecond);
        _reference = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(_reference, flow, angle);
        double lead = AttitudeGuidance.ErrorAngleRadians(ship.Orientation, _reference);
        // One lead limit covers every axis: a pitch error the flaps cannot close would use it
        // all and drag the heading reference along with the ship.
        double leadLimit = (speed <= catchDefinition.GlideTerminalHeadingSpeedMps
            ? System.Math.Max(descent.MaximumReferenceLeadDegrees, catchDefinition.GlideMaximumReferenceLeadDegrees)
            : descent.MaximumReferenceLeadDegrees) * MathUtils.DEG_TO_RAD;
        if (lead > leadLimit) _reference = ship.Orientation.Slerp(_reference, leadLimit / lead).Normalize();
        return Flight14AerodynamicAttitude.ComputeCommand(ship, body, _reference,
            descent.TrimOnsetDynamicPressurePa, descent.TrimFullDynamicPressurePa);
    }

    /// <summary>
    /// Estimated time to the near lead point (nose toward the cradle, the weak nose-ward drift)
    /// against the far one (tail first over the cradle, the strong tail-ward drift): the turn at
    /// the heading reference rate plus the distance at the steady drift of the angle limits.
    /// </summary>
    private static bool FarSideIsFaster(TowerCatchBurnDefinition d, Vector3d nose, Vector3d toCradle)
    {
        double distance = toCradle.Magnitude;
        double turnNear = System.Math.Acos(System.Math.Clamp(nose.Dot(toCradle / distance), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
        double noseDrift = System.Math.Max(1.0, d.GlideDriftPerDegreeMps * (d.GlideZeroDriftAngleDegrees - d.GlideMinimumAngleOfAttackDegrees));
        double tailDrift = System.Math.Max(1.0, d.GlideDriftPerDegreeMps * (d.GlideMaximumAngleOfAttackDegrees - d.GlideZeroDriftAngleDegrees));
        double near = turnNear / d.GlideHeadingReferenceRateDegPerS + System.Math.Max(0.0, distance - d.IgnitionLeadM) / noseDrift
            + System.Math.Max(0.0, d.IgnitionLeadM - distance) / tailDrift;
        double far = (180.0 - turnNear) / d.GlideHeadingReferenceRateDegPerS + (distance + d.IgnitionLeadM) / tailDrift;
        return far < near;
    }

    /// <summary>
    /// Terminal belly-flop attitude loop, designed on the identified plant (Flight 15 catch bench,
    /// 2026-11-24). Statically the flaps trim 80–104° with margins of about ±0.08–0.1 rad/s² in
    /// angle of attack, ±0.04 in heading and ±0.8 in roll at 12 km, but the servos slew 25°/s.
    /// The angle reference therefore moves on a time-optimal profile inside that margin
    /// (acceleration and rate limits), fed forward with its rate and acceleration; a
    /// conditional integral removes the static-inversion bias; heading is a slow loop on the
    /// weakest axis; and the flap allocation solves in angular acceleration with roll first,
    /// because a torque-space solve gives up roll (inertia about 25× smaller) whenever the
    /// angle saturates the flaps.
    /// </summary>
    private Vector3d TerminalAttitudeCommand(Vessel ship, CelestialBody body, Flight14DescentDefinition descent,
        TowerCatchBurnDefinition d, Vector3d flow, Vector3d wantedHeading, double angle, double dt)
    {
        var inertia = ship.GetMassProperties().InertiaBody;
        if (inertia.M11 > 0.0 && inertia.M22 > 0.0 && inertia.M33 > 0.0)
            ship.BodyFlapAllocationAxisWeights = new Vector3d(1.0, d.GlideAllocationRollPriority * inertia.M11 / inertia.M22,
                inertia.M11 / inertia.M33);
        var noseAxis = ship.Orientation.Rotate(Vector3d.Up);
        double measured = System.Math.Acos(System.Math.Clamp(noseAxis.Dot(flow), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
        if (double.IsNaN(_shapedAngle)) { _shapedAngle = measured; _shapedAngleRate = 0.0; }
        double acceleration = ShapeStep(angle, ref _shapedAngle, ref _shapedAngleRate,
            d.GlideAngleAccelerationDegPerS2, d.GlideAngleRateDegPerS, dt);
        // Integrate only while the reference holds still: during a ramp the error is tracking
        // lag, not a bias, and integrating it overshoots.
        if (System.Math.Abs(_shapedAngleRate) < d.GlideAngleIntegralHoldRateDegPerS)
            _angleErrorIntegral = System.Math.Clamp(_angleErrorIntegral
                    + d.GlideAngleTrackingIntegralGainPerSecond * (_shapedAngle - measured) * dt,
                -d.GlideAngleTrackingIntegralLimitDegrees, d.GlideAngleTrackingIntegralLimitDegrees);
        // Heading: a time-optimal profile about the local vertical inside the weak heading margin,
        // measured from a horizontal frame fixed at takeover.
        var up = body.GetGeodeticUp(ship.Position);
        var noseHorizontal = noseAxis - up * noseAxis.Dot(up);
        var wanted = wantedHeading - up * wantedHeading.Dot(up);
        if (_headingFrame.MagnitudeSquared < 0.5)
            _headingFrame = noseHorizontal.MagnitudeSquared > 1e-6 ? noseHorizontal.Normalized
                : wanted.MagnitudeSquared > 1e-6 ? wanted.Normalized : up.Cross(Vector3d.Right).Normalized;
        _headingFrame = (_headingFrame - up * _headingFrame.Dot(up)).Normalized;
        double HeadingOf(Vector3d v) => System.Math.Atan2(_headingFrame.Cross(v).Dot(up), _headingFrame.Dot(v)) * MathUtils.RAD_TO_DEG;
        if (double.IsNaN(_shapedHeading))
        {
            _shapedHeading = noseHorizontal.MagnitudeSquared > 1e-6 ? HeadingOf(noseHorizontal.Normalized) : 0.0;
            _shapedHeadingRate = 0.0;
        }
        double goal = wanted.MagnitudeSquared > 1e-6 ? HeadingOf(wanted.Normalized) : _shapedHeading;
        // Unwrap toward the shaped heading so the turn takes the short way.
        goal = _shapedHeading + System.Math.IEEERemainder(goal - _shapedHeading, 360.0);
        double headingAcceleration = ShapeStep(goal, ref _shapedHeading, ref _shapedHeadingRate,
            d.GlideHeadingAccelerationDegPerS2, d.GlideHeadingReferenceRateDegPerS, dt);
        var liftReference = Quaterniond.FromAxisAngle(up, _shapedHeading * MathUtils.DEG_TO_RAD).Rotate(_headingFrame);
        liftReference -= flow * liftReference.Dot(flow);
        if (liftReference.MagnitudeSquared < 1e-12) liftReference = LastLiftReference;
        liftReference = liftReference.Normalized;
        LastLiftReference = liftReference;
        var axis = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, _shapedAngle + _angleErrorIntegral);
        _reference = AerodynamicsModel.ComputeBellyFirstOrientation(axis, flow);
        // The angle grows as the nose turns away from the flow, about flow × nose.
        var rateWorld = up * (_shapedHeadingRate * MathUtils.DEG_TO_RAD);
        var accelerationWorld = up * (headingAcceleration * MathUtils.DEG_TO_RAD);
        var turn = flow.Cross(axis);
        if (turn.MagnitudeSquared > 1e-12)
        {
            turn = turn.Normalized;
            rateWorld += turn * (_shapedAngleRate * MathUtils.DEG_TO_RAD);
            accelerationWorld += turn * (acceleration * MathUtils.DEG_TO_RAD);
        }
        LastAngleOfAttackDegrees = _shapedAngle;
        var loop = (new Vector3d(d.GlideHeadingFrequencyRadPerS, d.GlideRollFrequencyRadPerS, d.GlideAngleFrequencyRadPerS),
            new Vector3d(d.GlideHeadingDampingRatio, d.GlideRollDampingRatio, d.GlideAngleDampingRatio));
        return Flight14AerodynamicAttitude.ComputeCommand(ship, body, _reference,
            descent.TrimOnsetDynamicPressurePa, descent.TrimFullDynamicPressurePa, loop, rateWorld, accelerationWorld);
    }

    /// <summary>
    /// One step of a time-optimal second-order reference toward <paramref name="goal"/> with
    /// bounded acceleration and rate; returns the acceleration used (units per s²).
    /// </summary>
    public static double ShapeStep(double goal, ref double value, ref double rate,
        double maximumAcceleration, double maximumRate, double dt)
    {
        double error = goal - value;
        // Brake on the discrete stopping curve (v = √(2 a |e|) less half a step of braking), and
        // land exactly on the goal within one step of it.
        double half = 0.5 * maximumAcceleration * dt;
        double brakingRate = System.Math.Sqrt(half * half + 2.0 * maximumAcceleration * System.Math.Abs(error)) - half;
        double linearRate = System.Math.Abs(error) / System.Math.Max(dt, 1e-6) * 0.5;
        double wanted = System.Math.CopySign(System.Math.Min(maximumRate, System.Math.Min(brakingRate, linearRate)), error);
        double acceleration = System.Math.Clamp((wanted - rate) / System.Math.Max(dt, 1e-6),
            -maximumAcceleration, maximumAcceleration);
        rate += acceleration * dt;
        double next = value + rate * dt;
        if ((goal - next) * error < 0.0 && System.Math.Abs(rate) <= maximumAcceleration * dt * 2.0)
        {
            acceleration = -rate / System.Math.Max(dt, 1e-6);
            value = goal;
            rate = 0.0;
            return System.Math.Clamp(acceleration, -maximumAcceleration, maximumAcceleration);
        }
        value = next;
        return acceleration;
    }

    /// <summary>Turns a horizontal heading toward a target about the local vertical by at most <paramref name="maximumRad"/>.</summary>
    private static Vector3d TurnToward(Vector3d heading, Vector3d target, Vector3d up, double maximumRad)
    {
        heading = (heading - up * heading.Dot(up)).Normalized;
        target = (target - up * target.Dot(up)).Normalized;
        double angle = System.Math.Atan2(heading.Cross(target).Dot(up), System.Math.Clamp(heading.Dot(target), -1.0, 1.0));
        double turn = System.Math.Clamp(angle, -maximumRad, maximumRad);
        return Quaterniond.FromAxisAngle(up, turn).Rotate(heading).Normalized;
    }
}
