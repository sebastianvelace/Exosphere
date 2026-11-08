namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// A finite burn near periapsis that reshapes an ellipse to a target periapsis and apoapsis,
/// both of which may differ from the current orbit's. Apollo 11's lunar orbit
/// circularization was such a burn: it took the 60.0 by 169.7 n mi insertion orbit to a
/// deliberately non-circular 53.7 by 65.7 n mi target (NASA SP-238, section 7, "Lunar orbit
/// insertion and circularization").
/// <para>At the vessel's radius r the target conic fixes the speed (vis-viva) and the
/// horizontal component (angular momentum h = sqrt(2 mu ra rp / (ra + rp)), v_h = h / r), so
/// the target velocity is v_h along the current horizontal and the remaining radial speed on
/// the side of the current radial motion. The burn holds the impulsive difference at periapsis as a
/// fixed inertial attitude, acquired before ignition, and cuts off when the remaining
/// difference along it is below one step of acceleration. It only writes throttle and attitude; the orbit is what the integrated
/// engine, propellant and gravity produce.</para>
/// </summary>
public sealed class ApsidesTargetBurn
{
    private const double AlignmentToleranceRad = 3.0 * MathUtils.DEG_TO_RAD;
    private const double SettledAngularRateRadPerS = 0.02;

    private readonly double _targetPeriapsisAltitudeM;
    private readonly double _targetApoapsisAltitudeM;
    private readonly double _maximumBurnSeconds;
    private double _burnSeconds;
    private Vector3d _burnDirection;
    private double _radialSign;
    private readonly double _preAlignmentLeadSeconds;

    public PeriapsisCaptureBurnStatus Status { get; private set; } = PeriapsisCaptureBurnStatus.Coasting;
    public string? BlockReason { get; private set; }
    public double BurnSeconds => _burnSeconds;
    public double PeriapsisAltitudeM { get; private set; } = double.NaN;
    public double ApoapsisAltitudeM { get; private set; } = double.NaN;
    /// <summary>Remaining velocity difference to the target conic at the last step (m/s).</summary>
    public double RemainingDeltaVMps { get; private set; } = double.NaN;
    /// <summary>Inertial burn attitude (unit vector), once predicted.</summary>
    public Vector3d BurnDirection => _burnDirection;
    /// <summary>Angle between the thrust axis and the commanded burn direction (rad).</summary>
    public double PointingErrorRad { get; private set; } = double.NaN;
    public bool IsFinished => Status is PeriapsisCaptureBurnStatus.Completed or PeriapsisCaptureBurnStatus.Blocked;

    public ApsidesTargetBurn(double targetPeriapsisAltitudeM, double targetApoapsisAltitudeM,
        double maximumBurnSeconds, double preAlignmentLeadSeconds = 600.0)
    {
        if (!double.IsFinite(preAlignmentLeadSeconds) || preAlignmentLeadSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(preAlignmentLeadSeconds));
        _preAlignmentLeadSeconds = preAlignmentLeadSeconds;
        if (!double.IsFinite(targetPeriapsisAltitudeM) || targetPeriapsisAltitudeM <= 0.0
            || !double.IsFinite(targetApoapsisAltitudeM) || targetApoapsisAltitudeM < targetPeriapsisAltitudeM
            || !double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0)
            throw new ArgumentException("Invalid apsides target envelope.");
        _targetPeriapsisAltitudeM = targetPeriapsisAltitudeM;
        _targetApoapsisAltitudeM = targetApoapsisAltitudeM;
        _maximumBurnSeconds = maximumBurnSeconds;
    }

    /// <summary>
    /// Velocity on the target conic at <paramref name="position"/>, in the plane of
    /// <paramref name="velocity"/>, on the branch whose radial speed has the sign of
    /// <paramref name="radialSign"/>. The branch is a choice wherever the current radial speed is
    /// near zero (at periapsis both reach the target); it must stay fixed for the whole burn. Returns false when the radius lies outside
    /// [rp, ra], where no in-plane burn reaches the target.
    /// </summary>
    public static bool TryTargetVelocity(Vector3d position, Vector3d velocity, double gm,
        double targetPeriapsisRadiusM, double targetApoapsisRadiusM, double radialSign, out Vector3d target)
    {
        target = Vector3d.Zero;
        double r = position.Magnitude;
        if (!(r > 0.0) || r < targetPeriapsisRadiusM || r > targetApoapsisRadiusM) return false;
        var radial = position / r;
        var horizontal = velocity - radial * velocity.Dot(radial);
        if (horizontal.Magnitude <= 0.0) return false;
        horizontal = horizontal.Normalized;
        double sum = targetPeriapsisRadiusM + targetApoapsisRadiusM;
        double speedSquared = gm * (2.0 / r - 2.0 / sum);
        double h = System.Math.Sqrt(2.0 * gm * targetApoapsisRadiusM * targetPeriapsisRadiusM / sum);
        double vh = h / r;
        double vr = System.Math.Sqrt(System.Math.Max(0.0, speedSquared - vh * vh));
        if (radialSign < 0.0) vr = -vr;
        target = horizontal * vh + radial * vr;
        return true;
    }

    /// <summary>Call before each committed physics step.</summary>
    public void Advance(Vessel vessel, CelestialBody body, double deltaSeconds, double epoch)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0 || !double.IsFinite(epoch))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (IsFinished)
        {
            vessel.Throttle = 0.0;
            return;
        }
        if (vessel.IsDestroyed || vessel.StructuralControlLost)
        {
            Block(vessel, "vessel-unavailable");
            return;
        }

        var relative = vessel.CenterOfMass - body.Position;
        var velocity = vessel.CenterOfMassVelocity - body.Velocity;
        var orbit = OrbitalElements.FromStateVector(relative, velocity, body.GM, body.Id, epoch);
        PeriapsisAltitudeM = orbit.Periapsis - body.Radius;
        ApoapsisAltitudeM = orbit.Apoapsis - body.Radius;
        if (orbit.Eccentricity >= 1.0)
        {
            Block(vessel, "open-orbit");
            return;
        }

        double thrust = vessel.GetMaximumThrust(body);
        if (!double.IsFinite(thrust) || thrust <= 0.0)
        {
            Block(vessel, "no-available-thrust");
            return;
        }

        if (Status != PeriapsisCaptureBurnStatus.Burning)
        {
            double timeToPeriapsis = PeriapsisCaptureBurn.TimeToPeriapsis(relative, velocity, body.GM);
            // Estimate the burn from the difference at periapsis, where it is centred.
            var periapsisDirection = OrbitalElementsDirection(relative, velocity, body.GM);
            double rp = orbit.Periapsis;
            double energy = 0.5 * velocity.MagnitudeSquared - body.GM / relative.Magnitude;
            double periapsisSpeed = System.Math.Sqrt(System.Math.Max(0.0, 2.0 * (energy + body.GM / rp)));
            var periapsisVelocity = periapsisDirection.Tangent * periapsisSpeed;
            // The burn starts before periapsis, descending: keep the target on the descending branch.
            if (_radialSign == 0.0) _radialSign = velocity.Dot(relative) < 0.0 ? -1.0 : 1.0;
            double estimate = TryTargetVelocity(periapsisDirection.Radial * rp, periapsisVelocity, body.GM,
                body.Radius + _targetPeriapsisAltitudeM, body.Radius + _targetApoapsisAltitudeM, _radialSign,
                out var atPeriapsis)
                ? (atPeriapsis - periapsisVelocity).Magnitude : double.NaN;
            if (double.IsNaN(estimate))
            {
                Block(vessel, "periapsis-outside-target-orbit");
                return;
            }
            double halfBurn = 0.5 * vessel.TranslationalMassKg * estimate / thrust;
            // The window is centred on periapsis: open from half a burn before it until half a
            // burn after it, so an alignment that runs past periapsis does not wait a revolution.
            double period = 2.0 * System.Math.PI * System.Math.Sqrt(
                System.Math.Pow(orbit.SemiMajorAxis, 3) / body.GM);
            bool window = double.IsNaN(timeToPeriapsis) || timeToPeriapsis <= halfBurn
                || period - timeToPeriapsis <= halfBurn;
            // The burn attitude is the impulsive difference at periapsis, fixed in inertial
            // space. Acquire it in the last minutes before the window (a heavy docked stack
            // slews at well under 1 deg/s on RCS) and hold it; earlier, command nothing.
            _burnDirection = (atPeriapsis - periapsisVelocity).Normalized;
            if (!window)
            {
                vessel.Throttle = 0.0;
                if (!double.IsNaN(timeToPeriapsis) && timeToPeriapsis <= halfBurn + _preAlignmentLeadSeconds)
                    PointAlong(vessel, _burnDirection);
                else
                    vessel.PitchYawRoll = Vector3d.Zero;
                Status = PeriapsisCaptureBurnStatus.Coasting;
                return;
            }
        }

        if (!TryTargetVelocity(relative, velocity, body.GM, body.Radius + _targetPeriapsisAltitudeM,
                body.Radius + _targetApoapsisAltitudeM, _radialSign, out var targetVelocity))
        {
            Block(vessel, "radius-outside-target-orbit");
            return;
        }
        var difference = targetVelocity - velocity;
        RemainingDeltaVMps = difference.Magnitude;
        var axis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
        double stepDeltaV = thrust / vessel.TranslationalMassKg * deltaSeconds;
        // The thrust direction stays the inertial attitude acquired before ignition, as a short
        // burn's should: steering onto the instantaneous difference
        // would chase its radial remainder through 90 degrees as the tangential part is spent.
        // Cut off once the remaining difference along it is below one step of acceleration.
        if (Status == PeriapsisCaptureBurnStatus.Burning && difference.Dot(_burnDirection) <= stepDeltaV)
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = PeriapsisCaptureBurnStatus.Completed;
            return;
        }
        PointAlong(vessel, _burnDirection);
        if (Status != PeriapsisCaptureBurnStatus.Burning)
        {
            if (axis.Dot(_burnDirection) < System.Math.Cos(AlignmentToleranceRad)
                || vessel.AngularVelocity.Magnitude > SettledAngularRateRadPerS)
            {
                vessel.Throttle = 0.0;
                Status = PeriapsisCaptureBurnStatus.Aligning;
                return;
            }
        }
        PointingErrorRad = System.Math.Acos(System.Math.Clamp(axis.Dot(_burnDirection), -1.0, 1.0));
        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        Status = PeriapsisCaptureBurnStatus.Burning;
        vessel.Throttle = 1.0;
    }

    private static void PointAlong(Vessel vessel, Vector3d direction)
    {
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, direction, vessel.AngularVelocity,
            proportionalGain: 2.0, dampingGain: 25.0);
    }

    /// <summary>Unit radial and horizontal directions at periapsis of the osculating ellipse.</summary>
    private static (Vector3d Radial, Vector3d Tangent) OrbitalElementsDirection(Vector3d position,
        Vector3d velocity, double gm)
    {
        var h = position.Cross(velocity);
        var eVector = velocity.Cross(h) / gm - position / position.Magnitude;
        var radial = eVector.Magnitude > 1e-9 ? eVector.Normalized : position.Normalized;
        var tangent = h.Cross(radial).Normalized;
        return (radial, tangent);
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = PeriapsisCaptureBurnStatus.Blocked;
        BlockReason = reason;
    }
}
