namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public enum Flight14BurnKind { Insertion, Deorbit }
public enum Flight14BurnStatus { Aligning, Burning, Completed, TargetAlreadySatisfied, Blocked }

/// <summary>
/// A single sea-level Raptor burn controlled by propagated periapsis, not a video clock.
/// The caller owns tick scheduling, tank loading, burn-site targeting and mission sequencing.
/// This module is not a full Flight 14 reconstruction or an impulsive state assignment.
/// </summary>
public sealed class Flight14OrbitalBurn
{
    private readonly Flight14BurnKind _kind;
    private readonly double _targetPeriapsisAltitudeM;
    private readonly string _seaLevelModelId;
    private readonly int _requiredHealthySeaLevelEngines;
    private readonly double _minimumBurnAltitudeM;
    private readonly double _maximumBurnDurationSeconds;
    private double _burnSeconds;
    private double _alignmentSeconds;

    public Flight14BurnStatus Status { get; private set; } = Flight14BurnStatus.Aligning;
    public string? BlockReason { get; private set; }
    public bool HasDeliveredThrust { get; private set; }
    public double PeriapsisAltitudeM { get; private set; } = double.NaN;

    public Flight14OrbitalBurn(Flight14BurnKind kind, double targetPeriapsisAltitudeM,
        string seaLevelModelId, int requiredHealthySeaLevelEngines,
        double minimumBurnAltitudeM, double maximumBurnDurationSeconds)
    {
        if (!Enum.IsDefined(kind) || !double.IsFinite(targetPeriapsisAltitudeM)
            || targetPeriapsisAltitudeM <= 0 || string.IsNullOrWhiteSpace(seaLevelModelId)
            || requiredHealthySeaLevelEngines <= 0 || !double.IsFinite(minimumBurnAltitudeM)
            || minimumBurnAltitudeM <= targetPeriapsisAltitudeM
            || !double.IsFinite(maximumBurnDurationSeconds) || maximumBurnDurationSeconds <= 0)
            throw new ArgumentException("Invalid orbital burn envelope.");
        _kind = kind;
        _targetPeriapsisAltitudeM = targetPeriapsisAltitudeM;
        _seaLevelModelId = seaLevelModelId;
        _requiredHealthySeaLevelEngines = requiredHealthySeaLevelEngines;
        _minimumBurnAltitudeM = minimumBurnAltitudeM;
        _maximumBurnDurationSeconds = maximumBurnDurationSeconds;
    }

    /// <summary>Call before each committed physics step. No position, velocity or pose writes.</summary>
    public void Advance(Vessel vessel, CelestialBody body, double deltaSeconds, double epoch)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0 || !double.IsFinite(epoch))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (Status is Flight14BurnStatus.Completed or Flight14BurnStatus.TargetAlreadySatisfied
            or Flight14BurnStatus.Blocked)
        {
            vessel.Throttle = 0;
            return;
        }
        Part? engines = null;
        bool attachedBooster = false;
        int enginePartCount = 0;
        foreach (var part in vessel.Parts.Parts)
        {
            attachedBooster |= part.Definition.HasVehicleRole("booster");
            if (!part.Definition.HasVehicleRole("ship_engines")) continue;
            engines = part;
            enginePartCount++;
        }
        if (vessel.IsDestroyed || vessel.StructuralControlLost || vessel.IsGroundHeld
            || attachedBooster || enginePartCount != 1 || engines == null)
        {
            Block(vessel, "not-a-detached-operational-ship");
            return;
        }
        int healthySeaLevel = 0;
        string? firstHealthyModel = null;
        foreach (var state in engines.EngineStates)
        {
            if (state.FailureCode != null || state.State == EngineLifecycleState.Failed) continue;
            firstHealthyModel ??= state.EngineModelId;
            if (state.EngineModelId == _seaLevelModelId) healthySeaLevel++;
        }
        // Count selection chooses the first healthy mounts. Verify that topology before
        // using it: a failed centre cluster must never silently select a vacuum Raptor.
        if (engines.IsBroken || engines.FuelDepleted
            || healthySeaLevel < _requiredHealthySeaLevelEngines
            || firstHealthyModel != _seaLevelModelId)
        {
            Block(vessel, "sea-level-engine-health-or-selection");
            return;
        }
        double altitude = vessel.GetAltitude(body);
        var relative = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        if (!double.IsFinite(altitude) || altitude < _minimumBurnAltitudeM
            || !double.IsFinite(velocity.Magnitude) || velocity.Magnitude < 1)
        {
            Block(vessel, "outside-orbital-burn-envelope");
            return;
        }
        var orbit = OrbitalElements.FromStateVector(relative, velocity, body.GM, body.Id, epoch);
        PeriapsisAltitudeM = orbit.Periapsis - body.Radius;
        if (!double.IsFinite(PeriapsisAltitudeM) || orbit.Eccentricity >= 1)
        {
            Block(vessel, "unbound-or-invalid-conic");
            return;
        }
        engines.SelectEngineCount(1);
        // Completion cannot be credited from a requested engine count or elapsed time.
        if (Status == Flight14BurnStatus.Burning && engines.GetThrustMagnitude(0) > 0)
            HasDeliveredThrust = true;
        bool satisfied = _kind == Flight14BurnKind.Insertion
            ? PeriapsisAltitudeM >= _targetPeriapsisAltitudeM
            : PeriapsisAltitudeM <= _targetPeriapsisAltitudeM;
        if (satisfied)
        {
            vessel.Throttle = 0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = HasDeliveredThrust ? Flight14BurnStatus.Completed : Flight14BurnStatus.TargetAlreadySatisfied;
            return;
        }
        var aim = velocity.Normalized * (_kind == Flight14BurnKind.Insertion ? 1 : -1);
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, aim, vessel.AngularVelocity,
            proportionalGain: 2.0, dampingGain: 25.0);
        double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(aim);
        // Use the established orbital maneuver damping, rather than the fast TVC
        // defaults: an unpowered turn has much less angular authority. Wait for
        // angular settling as well as pointing so ignition does not amplify the turn.
        if (alignment < System.Math.Cos(5 * MathUtils.DEG_TO_RAD)
            || vessel.AngularVelocity.Magnitude > 0.03)
        {
            vessel.Throttle = 0;
            _alignmentSeconds += deltaSeconds;
            if (_alignmentSeconds > _maximumBurnDurationSeconds)
            {
                Block(vessel, "attitude-settling-envelope-exceeded");
                return;
            }
            Status = Flight14BurnStatus.Aligning;
            return;
        }
        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnDurationSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        Status = Flight14BurnStatus.Burning;
        vessel.Throttle = 1;
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = Flight14BurnStatus.Blocked;
        BlockReason = reason;
    }
}
