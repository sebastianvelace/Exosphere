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
    /// Velocity the vessel needs now to reach the aim point on time (body-relative). The arc
    /// keeps the direction of motion: a translunar aim point starts more than 180° ahead and
    /// sweeps through 180° during the burn, so the long/short choice follows the signed
    /// transfer angle instead of always forcing the short way, which would select the
    /// retrograde branch for the first part of the burn.
    /// </summary>
    public Vector3d RequiredVelocity(Vector3d relativePosition, double epoch, double gm)
    {
        double angle = System.Math.Atan2(
            relativePosition.Cross(_aimPosition).Dot(_planeNormal),
            relativePosition.Dot(_aimPosition));
        if (angle < 0.0) angle += 2.0 * System.Math.PI;
        return LambertSolver.Solve(gm, relativePosition, _aimPosition, _arrivalTime - epoch,
            _planeNormal, longWay: angle > System.Math.PI).DepartureVelocity;
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

        var relative = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        Vector3d velocityToGain;
        try
        {
            velocityToGain = RequiredVelocity(relative, epoch, body.GM) - velocity;
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
