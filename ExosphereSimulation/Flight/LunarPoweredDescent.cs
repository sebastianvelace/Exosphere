namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

public enum LunarPoweredDescentStatus { DescentOrbitInsertion, Coasting, Aligning, Braking, Terminal, Landed, Blocked }

/// <summary>
/// Lunar module descent from lunar orbit to touchdown. Descent orbit insertion (DOI) first
/// lowers the perilune to <see cref="DescentOrbitPeriluneAltitudeM"/> with a retrograde burn
/// at low throttle; powered descent initiation (PDI) then fires at that perilune. The braking phase flies explicit linear-acceleration
/// guidance (the Apollo LGC's E-guidance form) to a low gate above the surface: altitude and
/// vertical speed are both targeted, the horizontal velocity is nulled and the downrange
/// distance is left free. Time-to-go is re-solved every step so the commanded thrust stays
/// at <see cref="BrakingThrustFraction"/> of the engine. Below the gate a terminal phase
/// descends vertically at a speed proportional to altitude down to contact. Only throttle
/// and attitude commands are written; the trajectory is whatever the integrated engine,
/// propellant and lunar gravity produce.
/// </summary>
public sealed class LunarPoweredDescent
{
    /// <summary>Apollo 11 descent-orbit perilune: 50,000 ft.</summary>
    public const double DescentOrbitPeriluneAltitudeM = 15_000.0;
    /// <summary>DPS throttle for the DOI burn (flown at 10–40 % on Apollo 11).</summary>
    public const double DescentOrbitInsertionThrottle = 0.15;
    public const double LowGateAltitudeM = 150.0;
    public const double LowGateDescentRateMps = 3.0;
    public const double TouchdownDescentRateMps = 1.0;
    public const double BrakingThrustFraction = 0.85;
    private const double AlignmentToleranceRad = 10.0 * MathUtils.DEG_TO_RAD;
    private const double AlignmentLeadSeconds = 180.0;
    private const double TerminalTimeToGoSeconds = 3.0;
    private const double MaximumTerminalTilt = 0.3;

    private readonly double _maximumBurnSeconds;
    private double _burnSeconds;
    private bool _descentOrbitInserted;

    public LunarPoweredDescentStatus Status { get; private set; } = LunarPoweredDescentStatus.DescentOrbitInsertion;
    public string? BlockReason { get; private set; }
    public double BurnSeconds => _burnSeconds;
    public double TimeToGoSeconds { get; private set; } = double.NaN;
    public double AltitudeM { get; private set; } = double.NaN;

    /// <summary>Surface speed (m/s) when the descent engine was cut at contact.</summary>
    public double TouchdownSpeedMps { get; private set; } = double.NaN;

    public bool IsFinished => Status is LunarPoweredDescentStatus.Landed or LunarPoweredDescentStatus.Blocked;

    public LunarPoweredDescent(double maximumBurnSeconds = 900.0)
    {
        if (!double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumBurnSeconds));
        _maximumBurnSeconds = maximumBurnSeconds;
    }

    /// <summary>Call before each committed physics step.</summary>
    public void Advance(Vessel vessel, CelestialBody moon, double deltaSeconds, double epoch)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(moon);
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

        var relative = vessel.Position - moon.Position;
        var up = relative.Normalized;
        var surfaceVelocity = vessel.GetSurfaceVelocity(moon);
        AltitudeM = vessel.GetAltitude(moon);
        if (vessel.IsSurfaceSettled
            || Status is LunarPoweredDescentStatus.Terminal && AltitudeM <= 0.05)
        {
            // Contact: the crew shut the descent engine down on the probe contact light.
            TouchdownSpeedMps = surfaceVelocity.Magnitude;
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = LunarPoweredDescentStatus.Landed;
            return;
        }

        double maximumThrust = vessel.GetMaximumThrust(moon);
        if (!double.IsFinite(maximumThrust) || maximumThrust <= 0.0)
        {
            Block(vessel, "no-available-thrust");
            return;
        }
        if (!_descentOrbitInserted && FlyDescentOrbitInsertion(vessel, moon, relative, epoch)) return;
        double mass = vessel.TotalMass;
        double maximumAcceleration = maximumThrust / mass;

        double verticalSpeed = surfaceVelocity.Dot(up);
        var horizontalVelocity = surfaceVelocity - up * verticalSpeed;
        // Gravity net of the centrifugal relief the remaining horizontal speed still gives.
        double netGravity = moon.GM / relative.MagnitudeSquared
            - horizontalVelocity.MagnitudeSquared / relative.Magnitude;

        if (Status is LunarPoweredDescentStatus.Braking
            && (AltitudeM <= LowGateAltitudeM || TimeToGoSeconds <= TerminalTimeToGoSeconds))
            Status = LunarPoweredDescentStatus.Terminal;

        Vector3d thrustAcceleration = Status == LunarPoweredDescentStatus.Terminal
            ? TerminalAcceleration(up, verticalSpeed, horizontalVelocity, netGravity)
            : BrakingAcceleration(up, verticalSpeed, horizontalVelocity, netGravity, maximumAcceleration);
        var thrustDirection = thrustAcceleration.Normalized;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, thrustDirection, vessel.AngularVelocity,
            proportionalGain: 2.3,
            dampingGain: AttitudeGuidance.DampingGainFor(2.3, vessel.GetPitchYawAngularAuthority(moon)));
        double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(thrustDirection);

        if (Status is LunarPoweredDescentStatus.Coasting or LunarPoweredDescentStatus.Aligning)
        {
            double timeToPerilune = PeriapsisCaptureBurn.TimeToPeriapsis(relative,
                vessel.Velocity - moon.Velocity, moon.GM);
            vessel.Throttle = 0.0;
            if (double.IsFinite(timeToPerilune) && timeToPerilune > AlignmentLeadSeconds)
            {
                Status = LunarPoweredDescentStatus.Coasting;
                return;
            }
            Status = LunarPoweredDescentStatus.Aligning;
            // PDI at perilune, once the descent engine points along the guidance vector.
            if (double.IsFinite(timeToPerilune) && timeToPerilune > System.Math.Max(deltaSeconds, 1.0)
                || alignment < System.Math.Cos(AlignmentToleranceRad))
                return;
            Status = LunarPoweredDescentStatus.Braking;
        }

        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        // Thrust off-axis is wasted: scale the demand by the pointing cosine.
        double throttle = thrustAcceleration.Magnitude * mass / maximumThrust
            * System.Math.Max(0.0, alignment);
        vessel.Throttle = System.Math.Clamp(throttle, 0.0, 1.0);
    }

    /// <summary>
    /// Retrograde DOI burn until the perilune reaches the descent-orbit altitude; true while
    /// it still owns the vehicle.
    /// </summary>
    private bool FlyDescentOrbitInsertion(Vessel vessel, CelestialBody moon, Vector3d relative, double epoch)
    {
        var velocity = vessel.Velocity - moon.Velocity;
        var orbit = OrbitalElements.FromStateVector(relative, velocity, moon.GM, moon.Id, epoch);
        if (orbit.Periapsis - moon.Radius <= DescentOrbitPeriluneAltitudeM + 100.0)
        {
            _descentOrbitInserted = true;
            vessel.Throttle = 0.0;
            Status = LunarPoweredDescentStatus.Coasting;
            return false;
        }
        Status = LunarPoweredDescentStatus.DescentOrbitInsertion;
        var retrograde = -velocity.Normalized;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, retrograde, vessel.AngularVelocity,
            proportionalGain: 2.3,
            dampingGain: AttitudeGuidance.DampingGainFor(2.3, vessel.GetPitchYawAngularAuthority(moon)));
        bool aligned = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(retrograde)
            >= System.Math.Cos(AlignmentToleranceRad);
        vessel.Throttle = aligned ? DescentOrbitInsertionThrottle : 0.0;
        return true;
    }

    /// <summary>
    /// Thrust acceleration from linear-acceleration guidance to the low gate. Vertically
    /// a = 6(h_t − h − v·T)/T² − 2(v_t − v)/T reaches both the gate altitude and descent rate
    /// at T; horizontally a constant deceleration nulls the speed over T. T is the time-to-go
    /// that makes the demand <see cref="BrakingThrustFraction"/> of the engine.
    /// </summary>
    private Vector3d BrakingAcceleration(Vector3d up, double verticalSpeed, Vector3d horizontalVelocity,
        double netGravity, double maximumAcceleration)
    {
        double target = BrakingThrustFraction * maximumAcceleration;
        double low = 1.0, high = 3_000.0;
        if (Demand(high).Magnitude > target)
        {
            TimeToGoSeconds = high;
            return Demand(high);
        }
        for (int i = 0; i < 60; i++)
        {
            double middle = 0.5 * (low + high);
            if (Demand(middle).Magnitude > target) low = middle;
            else high = middle;
        }
        TimeToGoSeconds = high;
        return Demand(high);

        Vector3d Demand(double timeToGo)
        {
            double vertical = 6.0 * (LowGateAltitudeM - AltitudeM - verticalSpeed * timeToGo) / (timeToGo * timeToGo)
                - 2.0 * (-LowGateDescentRateMps - verticalSpeed) / timeToGo
                + netGravity;
            return up * vertical - horizontalVelocity / timeToGo;
        }
    }

    /// <summary>
    /// Vertical descent at a rate proportional to altitude (3 m/s at the gate, 1 m/s at
    /// contact) while the residual horizontal speed is nulled, tilt-limited so the vertical
    /// channel always keeps authority.
    /// </summary>
    private Vector3d TerminalAcceleration(Vector3d up, double verticalSpeed, Vector3d horizontalVelocity,
        double netGravity)
    {
        TimeToGoSeconds = 0.0;
        double descentRate = System.Math.Clamp(AltitudeM / LowGateAltitudeM * LowGateDescentRateMps,
            TouchdownDescentRateMps, LowGateDescentRateMps);
        double vertical = netGravity + (-descentRate - verticalSpeed) / 2.0;
        vertical = System.Math.Max(0.1 * netGravity, vertical);
        var horizontal = -horizontalVelocity / 3.0;
        double limit = MaximumTerminalTilt * vertical;
        if (horizontal.Magnitude > limit) horizontal = horizontal.Normalized * limit;
        return up * vertical + horizontal;
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = LunarPoweredDescentStatus.Blocked;
        BlockReason = reason;
    }
}
