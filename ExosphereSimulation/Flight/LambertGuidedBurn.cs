namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

public enum LambertGuidedBurnStatus { WaitingForIgnition, Aligning, Burning, Completed, Blocked }

/// <summary>
/// Velocity-to-be-gained guidance for an injection or a trajectory correction. Every
/// step re-solves a two-body Lambert arc from the vessel's actual position to a fixed
/// aim point at a fixed arrival time, steers the thrust along the remaining velocity
/// difference and cuts off when it falls below a threshold. Finite-burn and gravity
/// losses are therefore absorbed by the guidance instead of by a state assignment.
/// Only throttle and attitude commands are written.
/// </summary>
public sealed class LambertGuidedBurn
{
    private const double AlignmentToleranceRad = 5.0 * MathUtils.DEG_TO_RAD;
    private const double SettledAngularRateRadPerS = 0.03;

    private readonly Vector3d _aimPosition;
    private readonly double _arrivalTime;
    private readonly double _ignitionTime;
    private readonly double _cutoffDeltaVMps;
    private readonly double _maximumBurnSeconds;
    private readonly Vector3d _planeNormal;
    private readonly Func<Vector3d, Vector3d, double, Vector3d?>? _requiredVelocity;
    private double _burnSeconds;
    private Vector3d? _terminalAim;

    /// <summary>Seconds of thrust left at which the thrust direction is frozen.</summary>
    private const double TerminalPhaseSeconds = 5.0;

    public LambertGuidedBurnStatus Status { get; private set; } = LambertGuidedBurnStatus.WaitingForIgnition;
    public string? BlockReason { get; private set; }
    public double BurnSeconds => _burnSeconds;
    public double RemainingDeltaVMps { get; private set; } = double.NaN;
    public bool IsFinished => Status is LambertGuidedBurnStatus.Completed or LambertGuidedBurnStatus.Blocked;

    /// <param name="aimPosition">Arrival point relative to the central body (m).</param>
    /// <param name="arrivalTime">Simulation time at which the arc must reach the aim point.</param>
    /// <param name="ignitionTime">Earliest simulation time at which the engine may light.</param>
    /// <param name="planeNormal">Preferred angular-momentum direction of the arc.</param>
    public LambertGuidedBurn(Vector3d aimPosition, double arrivalTime, double ignitionTime,
        Vector3d planeNormal, double cutoffDeltaVMps, double maximumBurnSeconds)
    {
        if (!double.IsFinite(arrivalTime) || !double.IsFinite(ignitionTime) || arrivalTime <= ignitionTime
            || !double.IsFinite(cutoffDeltaVMps) || cutoffDeltaVMps <= 0.0
            || !double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0
            || planeNormal.MagnitudeSquared < 1e-12 || aimPosition.MagnitudeSquared < 1.0)
            throw new ArgumentException("Invalid Lambert-guided burn envelope.");
        _aimPosition = aimPosition;
        _arrivalTime = arrivalTime;
        _ignitionTime = ignitionTime;
        _planeNormal = planeNormal.Normalized;
        _cutoffDeltaVMps = cutoffDeltaVMps;
        _maximumBurnSeconds = maximumBurnSeconds;
    }

    /// <summary>
    /// The same velocity-to-be-gained guidance toward a required velocity that is not a
    /// two-body Lambert arc (a departure hyperbola, for instance).
    /// </summary>
    /// <param name="requiredVelocity">Body-relative velocity needed now, from the body-relative
    /// position, velocity and epoch; null blocks the burn.</param>
    /// <param name="deadline">Epoch after which the burn is pointless and blocks.</param>
    public LambertGuidedBurn(Func<Vector3d, Vector3d, double, Vector3d?> requiredVelocity, double deadline,
        double ignitionTime, double cutoffDeltaVMps, double maximumBurnSeconds)
    {
        ArgumentNullException.ThrowIfNull(requiredVelocity);
        if (!double.IsFinite(deadline) || !double.IsFinite(ignitionTime) || deadline <= ignitionTime
            || !double.IsFinite(cutoffDeltaVMps) || cutoffDeltaVMps <= 0.0
            || !double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0)
            throw new ArgumentException("Invalid guided burn envelope.");
        _requiredVelocity = requiredVelocity;
        _arrivalTime = deadline;
        _ignitionTime = ignitionTime;
        _planeNormal = Vector3d.Up;
        _aimPosition = Vector3d.Up;
        _cutoffDeltaVMps = cutoffDeltaVMps;
        _maximumBurnSeconds = maximumBurnSeconds;
    }

    /// <summary>
    /// Velocity the vessel needs now to reach the aim point on time (body-relative). The arc
    /// keeps the direction of motion: a translunar aim point starts more than 180° ahead and
    /// sweeps through 180° during the burn, so the long/short choice follows the signed
    /// transfer angle instead of always forcing the short way, which would select the
    /// retrograde branch for the first part of the burn.
    /// </summary>
    public Vector3d RequiredVelocity(Vector3d relativePosition, double epoch, double gm) =>
        RequiredVelocity(_aimPosition, _arrivalTime, _planeNormal, relativePosition, epoch, gm);

    /// <summary>Shared form of <see cref="RequiredVelocity(Vector3d, double, double)"/>.</summary>
    public static Vector3d RequiredVelocity(Vector3d aimPosition, double arrivalTime, Vector3d planeNormal,
        Vector3d relativePosition, double epoch, double gm)
    {
        double angle = System.Math.Atan2(
            relativePosition.Cross(aimPosition).Dot(planeNormal),
            relativePosition.Dot(aimPosition));
        if (angle < 0.0) angle += 2.0 * System.Math.PI;
        return LambertSolver.Solve(gm, relativePosition, aimPosition, arrivalTime - epoch,
            planeNormal, longWay: angle > System.Math.PI).DepartureVelocity;
    }

    /// <summary>
    /// Half-width of the band about a 180° transfer angle in which the aim point is pulled into
    /// the vessel's instantaneous orbital plane.
    /// </summary>
    public const double HalfRevolutionBandRad = 10.0 * MathUtils.DEG_TO_RAD;

    /// <summary>
    /// The aim point the Lambert arc is solved to this step. Two boundary points 180° apart do
    /// not fix a plane: an aim ε out of the vessel's plane at a transfer angle π − η asks for
    /// an out-of-plane Δv ≈ v·sin ε / sin η, which diverges as η → 0. A translunar injection
    /// sweeps through that angle mid-burn (the TLI asked for 4 km/s of out-of-plane velocity
    /// at 179.7°). Inside <see cref="HalfRevolutionBandRad"/> of a half revolution the aim's
    /// out-of-plane component is removed with weight w = 1 − (sin η / sin η₀)², so the request
    /// stays finite (∝ sin η) and the plane change resumes, well posed, once past the band.
    /// </summary>
    internal Vector3d ConditionedAim(Vector3d relativePosition, Vector3d relativeVelocity)
    {
        var plane = relativePosition.Cross(relativeVelocity);
        if (plane.MagnitudeSquared < 1e-12) return _aimPosition;
        double r = relativePosition.Magnitude, aim = _aimPosition.Magnitude;
        if (relativePosition.Dot(_aimPosition) >= 0.0) return _aimPosition; // Not near a half revolution.
        double sinEta = relativePosition.Cross(_aimPosition).Magnitude / (r * aim);
        double sinBand = System.Math.Sin(HalfRevolutionBandRad);
        if (sinEta >= sinBand) return _aimPosition;
        double ratio = sinEta / sinBand;
        double weight = 1.0 - ratio * ratio;
        var normal = plane.Normalized;
        return _aimPosition - normal * (weight * normal.Dot(_aimPosition));
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
        if (epoch >= _arrivalTime)
        {
            Block(vessel, "arrival-time-passed");
            return;
        }

        var relative = vessel.CenterOfMass - body.Position;
        var velocity = vessel.CenterOfMassVelocity - body.Velocity;
        Vector3d velocityToGain;
        try
        {
            if (_requiredVelocity != null)
            {
                if (_requiredVelocity(relative, velocity, epoch) is not { } required)
                {
                    Block(vessel, "no-required-velocity");
                    return;
                }
                velocityToGain = required - velocity;
            }
            else
                velocityToGain = RequiredVelocity(ConditionedAim(relative, velocity), _arrivalTime, _planeNormal,
                    relative, epoch, body.GM) - velocity;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Block(vessel, "lambert-unsolvable");
            return;
        }
        RemainingDeltaVMps = velocityToGain.Magnitude;
        double acceleration = vessel.GetMaximumThrust(body) / System.Math.Max(1.0, vessel.TotalMass);
        // Terminal phase: near cutoff the velocity-to-be-gained direction swings quickly, and
        // chasing it at full thrust only excites the attitude loop. Freeze the direction and
        // cut off on the remaining component along it, as cross-product steering does.
        if (Status == LambertGuidedBurnStatus.Burning && _terminalAim == null
            && acceleration > 0.0 && RemainingDeltaVMps < acceleration * TerminalPhaseSeconds)
            _terminalAim = velocityToGain / RemainingDeltaVMps;
        double alongAim = _terminalAim is { } frozen ? velocityToGain.Dot(frozen) : RemainingDeltaVMps;
        if (alongAim <= _cutoffDeltaVMps)
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = LambertGuidedBurnStatus.Completed;
            return;
        }

        var aim = _terminalAim ?? velocityToGain / RemainingDeltaVMps;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, aim, vessel.AngularVelocity,
            proportionalGain: 2.0, dampingGain: 25.0);
        if (Status != LambertGuidedBurnStatus.Burning)
        {
            if (epoch < _ignitionTime)
            {
                vessel.Throttle = 0.0;
                Status = LambertGuidedBurnStatus.WaitingForIgnition;
                return;
            }
            double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(aim);
            if (alignment < System.Math.Cos(AlignmentToleranceRad)
                || vessel.AngularVelocity.Magnitude > SettledAngularRateRadPerS)
            {
                vessel.Throttle = 0.0;
                Status = LambertGuidedBurnStatus.Aligning;
                return;
            }
        }

        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        Status = LambertGuidedBurnStatus.Burning;
        // Throttle down over the last second of thrust so the cutoff resolution stays
        // below the threshold even for a high-thrust stage.
        double neededThrottle = acceleration > 0.0 ? alongAim / acceleration : 1.0;
        vessel.Throttle = System.Math.Clamp(neededThrottle, 0.05, 1.0);
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = LambertGuidedBurnStatus.Blocked;
        BlockReason = reason;
    }
}
