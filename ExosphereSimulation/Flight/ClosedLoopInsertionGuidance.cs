namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Closed-loop upper-stage insertion steering ("PEG-lite"): the vertical thrust component
/// supports gravity minus centrifugal relief and drives the vertical speed toward a
/// profile that flattens at the target altitude, while the rest of the thrust builds
/// horizontal speed. It replaces open-loop pitch programs that cannot react to the actual
/// stage performance. Pure function: it returns an aim direction and never writes state.
/// </summary>
public static class ClosedLoopInsertionGuidance
{
    /// <summary>Seconds over which the altitude error is converted into a climb rate.</summary>
    public const double AltitudeTimeConstantSeconds = 60.0;

    /// <summary>Seconds over which the vertical-speed error is corrected.</summary>
    public const double ClimbRateTimeConstantSeconds = 25.0;

    /// <summary>Largest commanded climb rate (m/s) while below the target altitude.</summary>
    public const double MaximumClimbRateMps = 300.0;

    /// <summary>Largest sine of the thrust elevation above the local horizontal.</summary>
    public const double MaximumPitchSine = 0.6;

    /// <param name="vessel">Thrusting vessel.</param>
    /// <param name="body">Central body.</param>
    /// <param name="targetAltitudeM">Insertion altitude above the reference surface.</param>
    /// <param name="downrange">Fallback horizontal direction when horizontal speed is ~0.</param>
    public static Vector3d ComputeAim(Vessel vessel, CelestialBody body, double targetAltitudeM,
        Vector3d downrange)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        var relative = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        double radius = relative.Magnitude;
        var up = relative / radius;
        double verticalSpeed = velocity.Dot(up);
        var horizontalVelocity = velocity - up * verticalSpeed;
        var horizontal = horizontalVelocity.MagnitudeSquared > 1.0
            ? horizontalVelocity.Normalized
            : (downrange - up * downrange.Dot(up)).Normalized;

        double gravity = body.GM / (radius * radius);
        double centrifugal = horizontalVelocity.MagnitudeSquared / radius;
        double acceleration = vessel.GetMaximumThrust(body) / System.Math.Max(1.0, vessel.TotalMass);
        if (acceleration <= 0.0) return horizontal;

        double altitude = body.GetAltitude(vessel.Position);
        double targetClimbRate = System.Math.Clamp(
            (targetAltitudeM - altitude) / AltitudeTimeConstantSeconds, -60.0, MaximumClimbRateMps);
        double verticalAcceleration = gravity - centrifugal
            + (targetClimbRate - verticalSpeed) / ClimbRateTimeConstantSeconds;
        // Cap the upward pitch: a low-thrust upper stage should let the inherited climb rate
        // decay while it builds horizontal speed, not fight gravity to hold altitude early.
        double sine = System.Math.Clamp(verticalAcceleration / acceleration, -0.5, MaximumPitchSine);
        return (horizontal * System.Math.Sqrt(1.0 - sine * sine) + up * sine).Normalized;
    }

    /// <summary>True once the osculating periapsis clears <paramref name="minimumPeriapsisAltitudeM"/>.</summary>
    public static bool IsInserted(Vessel vessel, CelestialBody body, double minimumPeriapsisAltitudeM,
        double epoch)
    {
        var orbit = OrbitalElements.FromStateVector(vessel.Position - body.Position,
            vessel.Velocity - body.Velocity, body.GM, body.Id, epoch);
        return orbit.Eccentricity < 1.0 && orbit.Periapsis - body.Radius >= minimumPeriapsisAltitudeM;
    }
}
