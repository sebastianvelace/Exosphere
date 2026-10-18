namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

public enum PeriapsisCaptureBurnStatus { Coasting, Aligning, Burning, Completed, Blocked }

/// <summary>
/// A finite retrograde burn centred on periapsis that lowers the apoapsis to a target
/// altitude: lunar orbit insertion from a hyperbolic approach, or circularization of an
/// existing ellipse. It only writes throttle and attitude commands; the resulting orbit is
/// whatever the integrated engine, propellant and gravity produce. No position, velocity
/// or orbit is ever assigned.
/// </summary>
public sealed class PeriapsisCaptureBurn
{
    private const double AlignmentToleranceRad = 3.0 * MathUtils.DEG_TO_RAD;
    private const double SettledAngularRateRadPerS = 0.02;
    private const double CircularToleranceM = 1_000.0;

    private readonly double _targetApoapsisAltitudeM;
    private readonly double _maximumBurnSeconds;
    private double _burnSeconds;

    public PeriapsisCaptureBurnStatus Status { get; private set; } = PeriapsisCaptureBurnStatus.Coasting;
    public string? BlockReason { get; private set; }
    public double BurnSeconds => _burnSeconds;
    public double PeriapsisAltitudeM { get; private set; } = double.NaN;
    public double ApoapsisAltitudeM { get; private set; } = double.NaN;
    public bool IsFinished => Status is PeriapsisCaptureBurnStatus.Completed or PeriapsisCaptureBurnStatus.Blocked;

    public PeriapsisCaptureBurn(double targetApoapsisAltitudeM, double maximumBurnSeconds)
    {
        if (!double.IsFinite(targetApoapsisAltitudeM) || targetApoapsisAltitudeM <= 0.0
            || !double.IsFinite(maximumBurnSeconds) || maximumBurnSeconds <= 0.0)
            throw new ArgumentException("Invalid periapsis capture envelope.");
        _targetApoapsisAltitudeM = targetApoapsisAltitudeM;
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
        var orbit = OrbitalElements.FromStateVector(relative, velocity, body.GM, body.Id, epoch);
        PeriapsisAltitudeM = orbit.Periapsis - body.Radius;
        ApoapsisAltitudeM = orbit.Apoapsis - body.Radius;
        // A burn at altitude h cannot lower the apoapsis below h: once they meet, the orbit
        // is circular about this point and further thrust would only lower the periapsis.
        double altitude = relative.Magnitude - body.Radius;
        if (orbit.Eccentricity < 1.0
            && (ApoapsisAltitudeM <= _targetApoapsisAltitudeM
                || _burnSeconds > 0.0 && ApoapsisAltitudeM - altitude <= CircularToleranceM))
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            Status = PeriapsisCaptureBurnStatus.Completed;
            return;
        }

        double thrust = vessel.GetMaximumThrust(body);
        if (!double.IsFinite(thrust) || thrust <= 0.0)
        {
            Block(vessel, "no-available-thrust");
            return;
        }

        var retrograde = -velocity.Normalized;
        vessel.SASEnabled = false;
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation, Vector3d.Up, retrograde, vessel.AngularVelocity,
            proportionalGain: 2.0, dampingGain: 25.0);

        if (Status != PeriapsisCaptureBurnStatus.Burning)
        {
            double timeToPeriapsis = TimeToPeriapsis(relative, velocity, body.GM);
            double halfBurn = 0.5 * EstimateBurnSeconds(vessel, body, orbit, thrust);
            bool ignitionWindow = double.IsNaN(timeToPeriapsis) || timeToPeriapsis <= halfBurn;
            if (!ignitionWindow)
            {
                vessel.Throttle = 0.0;
                Status = PeriapsisCaptureBurnStatus.Coasting;
                return;
            }
            double alignment = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(retrograde);
            if (alignment < System.Math.Cos(AlignmentToleranceRad)
                || vessel.AngularVelocity.Magnitude > SettledAngularRateRadPerS)
            {
                vessel.Throttle = 0.0;
                Status = PeriapsisCaptureBurnStatus.Aligning;
                return;
            }
        }

        _burnSeconds += deltaSeconds;
        if (_burnSeconds > _maximumBurnSeconds)
        {
            Block(vessel, "burn-duration-envelope-exceeded");
            return;
        }
        Status = PeriapsisCaptureBurnStatus.Burning;
        vessel.Throttle = 1.0;
    }

    /// <summary>
    /// Seconds until the next periapsis passage on the osculating conic. An ellipse always
    /// has a next passage; an outbound open conic has none and returns NaN, where the burn
    /// should not wait any longer.
    /// </summary>
    public static double TimeToPeriapsis(Vector3d position, Vector3d velocity, double gm)
    {
        double r = position.Magnitude;
        if (r <= 0.0) return double.NaN;
        double radialVelocity = position.Dot(velocity);
        double energy = 0.5 * velocity.MagnitudeSquared - gm / r;
        var h = position.Cross(velocity);
        var eVector = velocity.Cross(h) / gm - position / r;
        double e = eVector.Magnitude;
        if (System.Math.Abs(energy) < 1e-9 || e < 1e-9) return 0.0;
        double a = -gm / (2.0 * energy);
        if (e < 1.0)
        {
            double meanMotion = System.Math.Sqrt(gm / (a * a * a));
            double cosE = System.Math.Clamp((1.0 - r / a) / e, -1.0, 1.0);
            double eccentric = System.Math.Acos(cosE);
            if (radialVelocity < 0.0) eccentric = -eccentric;
            double mean = eccentric - e * System.Math.Sin(eccentric);
            double sincePeriapsis = mean / meanMotion;
            return sincePeriapsis <= 0.0
                ? -sincePeriapsis
                : 2.0 * System.Math.PI / meanMotion - sincePeriapsis;
        }
        if (radialVelocity >= 0.0) return double.NaN;
        double coshH = System.Math.Max(1.0, (1.0 - r / a) / e);
        double hyperbolic = -System.Math.Log(coshH + System.Math.Sqrt(coshH * coshH - 1.0));
        double hyperbolicMean = e * System.Math.Sinh(hyperbolic) - hyperbolic;
        return -hyperbolicMean / System.Math.Sqrt(gm / (-a * a * a));
    }

    private double EstimateBurnSeconds(Vessel vessel, CelestialBody body, OrbitalElements orbit, double thrust)
    {
        double rp = orbit.Periapsis;
        var velocity = vessel.Velocity - body.Velocity;
        double r = (vessel.Position - body.Position).Magnitude;
        double energy = 0.5 * velocity.MagnitudeSquared - body.GM / r;
        double periapsisSpeed = System.Math.Sqrt(System.Math.Max(0.0, 2.0 * (energy + body.GM / rp)));
        double targetApoapsis = body.Radius + _targetApoapsisAltitudeM;
        double targetSpeed = System.Math.Sqrt(body.GM * (2.0 / rp - 2.0 / (rp + targetApoapsis)));
        double deltaV = System.Math.Max(0.0, periapsisSpeed - targetSpeed);
        // Constant-mass estimate: slightly long, which centres the burn conservatively.
        return vessel.TotalMass * deltaV / thrust;
    }

    private void Block(Vessel vessel, string reason)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        Status = PeriapsisCaptureBurnStatus.Blocked;
        BlockReason = reason;
    }
}
