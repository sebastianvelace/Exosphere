namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

public enum TranslationBurnStatus { WaitingForIgnition, Burning, Completed, Blocked }

/// <summary>
/// Velocity-to-be-gained Lambert burn flown on translation reaction control instead of a main
/// engine (Gemini OAMS rendezvous manoeuvres). Large burns point the nose along the velocity
/// to gain and fire the aft thrusters alone: thrusting several axes at once costs |x|+|y|+|z|
/// in propellant instead of |Δv| and emptied the OAMS on one TPI. The last few m/s are trimmed
/// on all axes without reorienting.
/// </summary>
public sealed class TranslationLambertBurn
{
    /// <summary>Time constant (s) of the proportional taper near cutoff.</summary>
    private const double TaperSeconds = 1.0;

    /// <summary>Below this velocity to gain (m/s) the burn trims on all axes without turning.</summary>
    public const double TrimThresholdMps = 3.0;

    private static readonly double AlignedCosine = System.Math.Cos(8.0 * MathUtils.DEG_TO_RAD);

    private readonly Vector3d _aimPosition;
    private readonly double _arrivalTime;
    private readonly double _ignitionTime;
    private readonly Vector3d _planeNormal;
    private readonly double _cutoffDeltaVMps;
    private readonly double _maximumBurnSeconds;

    public TranslationBurnStatus Status { get; private set; } = TranslationBurnStatus.WaitingForIgnition;
    public string? BlockReason { get; private set; }
    public double BurnSeconds { get; private set; }
    public double RemainingDeltaVMps { get; private set; } = double.NaN;
    public bool IsFinished => Status is TranslationBurnStatus.Completed or TranslationBurnStatus.Blocked;

    public TranslationLambertBurn(Vector3d aimPosition, double arrivalTime, double ignitionTime,
        Vector3d planeNormal, double cutoffDeltaVMps = 0.02, double maximumBurnSeconds = 600.0)
    {
        if (!double.IsFinite(arrivalTime) || !(arrivalTime > ignitionTime) || planeNormal.MagnitudeSquared < 1e-12
            || !(cutoffDeltaVMps > 0.0) || !(maximumBurnSeconds > 0.0))
            throw new ArgumentException("Invalid translation burn envelope.");
        _aimPosition = aimPosition;
        _arrivalTime = arrivalTime;
        _ignitionTime = ignitionTime;
        _planeNormal = planeNormal.Normalized;
        _cutoffDeltaVMps = cutoffDeltaVMps;
        _maximumBurnSeconds = maximumBurnSeconds;
    }

    /// <summary>Call before each committed physics step.</summary>
    public void Advance(Vessel vessel, CelestialBody body, double deltaSeconds, double epoch)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        if (IsFinished || epoch < _ignitionTime)
        {
            vessel.TranslationCommand = Vector3d.Zero;
            return;
        }
        if (vessel.IsDestroyed || vessel.RcsPropellantDepleted || epoch >= _arrivalTime)
        {
            Finish(vessel, TranslationBurnStatus.Blocked, vessel.RcsPropellantDepleted
                ? "rcs-propellant-depleted" : "vessel-unavailable-or-late");
            return;
        }
        Vector3d toGain;
        try
        {
            toGain = LambertGuidedBurn.RequiredVelocity(_aimPosition, _arrivalTime, _planeNormal,
                vessel.Position - body.Position, epoch, body.GM) - (vessel.Velocity - body.Velocity);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Finish(vessel, TranslationBurnStatus.Blocked, "lambert-unsolvable");
            return;
        }
        RemainingDeltaVMps = toGain.Magnitude;
        if (RemainingDeltaVMps <= _cutoffDeltaVMps)
        {
            Finish(vessel, TranslationBurnStatus.Completed, null);
            return;
        }
        BurnSeconds += deltaSeconds;
        if (BurnSeconds > _maximumBurnSeconds)
        {
            Finish(vessel, TranslationBurnStatus.Blocked, "burn-duration-envelope-exceeded");
            return;
        }
        // Burn time counts thrusting and turning alike: both are part of flying the manoeuvre.
        Status = TranslationBurnStatus.Burning;
        if (RemainingDeltaVMps <= TrimThresholdMps)
        {
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.TranslationCommand = TranslationCommandFor(vessel, toGain, TaperSeconds);
            return;
        }
        // Point the aft thrusters along the velocity to gain; thrust only once aligned.
        var direction = toGain / RemainingDeltaVMps;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation, Vector3d.Up,
            direction, vessel.AngularVelocity, proportionalGain: 2.3,
            dampingGain: AttitudeGuidance.DampingGainFor(2.3, vessel.GetPitchYawAngularAuthority(body)));
        var nose = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
        var command = TranslationCommandFor(vessel, toGain, TaperSeconds);
        vessel.TranslationCommand = nose.Dot(direction) >= AlignedCosine
            ? new Vector3d(0.0, System.Math.Max(0.0, command.Y), 0.0)
            : Vector3d.Zero;
    }

    /// <summary>
    /// Body-axis translation command that gains <paramref name="worldDeltaV"/> over about
    /// <paramref name="seconds"/>, saturating each axis at full thrust.
    /// </summary>
    public static Vector3d TranslationCommandFor(Vessel vessel, Vector3d worldDeltaV, double seconds)
    {
        double thrust = vessel.Parts.RcsTranslation.ThrustN;
        if (thrust <= 0.0) return Vector3d.Zero;
        double acceleration = thrust / System.Math.Max(1.0, vessel.TranslationalMassKg);
        var local = vessel.Orientation.Inverse().Rotate(worldDeltaV) / (acceleration * seconds);
        return new Vector3d(System.Math.Clamp(local.X, -1.0, 1.0), System.Math.Clamp(local.Y, -1.0, 1.0),
            System.Math.Clamp(local.Z, -1.0, 1.0));
    }

    private void Finish(Vessel vessel, TranslationBurnStatus status, string? reason)
    {
        vessel.TranslationCommand = Vector3d.Zero;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = status;
        BlockReason = reason;
    }
}
