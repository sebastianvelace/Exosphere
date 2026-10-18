namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

public enum ProgradeEnergyBurnStatus { WaitingForIgnition, Aligning, Burning, Completed, Blocked }

/// <summary>
/// A finite prograde injection that cuts off when the osculating specific orbital energy
/// reaches a target, e.g. the energy of a planned translunar conic. Thrust follows the
/// instantaneous velocity, which is close to optimal for a departure from a near-circular
/// orbit and has no Lambert 180° singularity; the remaining aim error is left to a later
/// trajectory correction. Only throttle and attitude commands are written.
/// </summary>
public sealed class ProgradeEnergyBurn
{
    private const double AlignmentToleranceRad = 3.0 * MathUtils.DEG_TO_RAD;
    private const double SettledAngularRateRadPerS = 0.02;

    private readonly double _targetSpecificEnergy;
    private readonly double _ignitionTime;
    private readonly double _maximumBurnSeconds;
    private double _burnSeconds;

    public ProgradeEnergyBurnStatus Status { get; private set; } = ProgradeEnergyBurnStatus.WaitingForIgnition;
    public string? BlockReason { get; private set; }
    public double BurnSeconds => _burnSeconds;
    public double SpecificEnergy { get; private set; } = double.NaN;
    public bool IsFinished => Status is ProgradeEnergyBurnStatus.Completed or ProgradeEnergyBurnStatus.Blocked;

    public ProgradeEnergyBurn(double targetSpecificEnergy, double ignitionTime, double maximumBurnSeconds)
    {
        if (!double.IsFinite(targetSpecificEnergy) || !double.IsFinite(ignitionTime)
            || !double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0)
            throw new ArgumentException("Invalid prograde injection envelope.");
        _targetSpecificEnergy = targetSpecificEnergy;
        _ignitionTime = ignitionTime;
        _maximumBurnSeconds = maximumBurnSeconds;
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

        var relative = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        SpecificEnergy = 0.5 * velocity.MagnitudeSquared - body.GM / relative.Magnitude;
        if (SpecificEnergy >= _targetSpecificEnergy)
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = ProgradeEnergyBurnStatus.Completed;
            return;
        }

        var prograde = velocity.Normalized;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, prograde, vessel.AngularVelocity,
            proportionalGain: 2.0, dampingGain: 25.0);
        if (Status != ProgradeEnergyBurnStatus.Burning)
        {
            double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(prograde);
            bool aligned = alignment >= System.Math.Cos(AlignmentToleranceRad)
                && vessel.AngularVelocity.Magnitude <= SettledAngularRateRadPerS;
            vessel.Throttle = 0.0;
            Status = epoch < _ignitionTime ? ProgradeEnergyBurnStatus.WaitingForIgnition
                : aligned ? ProgradeEnergyBurnStatus.Burning
                : ProgradeEnergyBurnStatus.Aligning;
            if (Status != ProgradeEnergyBurnStatus.Burning) return;
        }

        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        vessel.Throttle = 1.0;
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = ProgradeEnergyBurnStatus.Blocked;
        BlockReason = reason;
    }
}
