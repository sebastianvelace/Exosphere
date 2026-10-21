namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Closed-loop upper-stage insertion steering with a time-to-go. The horizontal speed still
/// to gain to circular velocity, divided by the available acceleration, sets t_go; the
/// vertical acceleration is the linear profile that reaches the target altitude with zero
/// climb rate exactly at t_go, plus gravity minus centrifugal relief. Thrust left over
/// builds horizontal speed. This replaces open-loop pitch programs that cannot react to
/// the actual stage performance. Pure function: it returns an aim direction only.
/// </summary>
public static class ClosedLoopInsertionGuidance
{
    /// <summary>Shortest time-to-go used, so the law stays bounded near cutoff.</summary>
    public const double MinimumTimeToGoSeconds = 10.0;

    /// <summary>Largest sine of the thrust elevation above (or below) the local horizontal.</summary>
    public const double MaximumPitchSine = 0.95;

    /// <param name="vessel">Thrusting vessel.</param>
    /// <param name="body">Central body.</param>
    /// <param name="targetAltitudeM">Insertion altitude above the reference surface.</param>
    /// <param name="downrange">Fallback horizontal direction when horizontal speed is ~0.</param>
    /// <param name="targetPlaneNormal">
    /// Optional orbit-plane normal: the horizontal thrust then points along the horizontal
    /// velocity still to be gained toward circular speed in that plane, which also removes
    /// any cross-track velocity (for example the Earth-rotation share off a non-east azimuth).
    /// </param>
    public static Vector3d ComputeAim(Vessel vessel, CelestialBody body, double targetAltitudeM,
        Vector3d downrange, Vector3d? targetPlaneNormal = null)
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

        double acceleration = vessel.GetMaximumThrust(body) / System.Math.Max(1.0, vessel.TotalMass);
        if (acceleration <= 0.0) return horizontal;
        double gravity = body.GM / (radius * radius);
        double centrifugal = horizontalVelocity.MagnitudeSquared / radius;

        double targetRadius = body.Radius + targetAltitudeM;
        double circularSpeed = System.Math.Sqrt(body.GM / targetRadius);
        double horizontalDeficit = System.Math.Max(0.0, circularSpeed - horizontalVelocity.Magnitude);
        if (targetPlaneNormal is { } normal && normal.MagnitudeSquared > 1e-12)
        {
            var inPlane = normal.Normalized.Cross(up);
            if (inPlane.MagnitudeSquared > 1e-12)
            {
                var toGain = inPlane.Normalized * circularSpeed - horizontalVelocity;
                horizontalDeficit = toGain.Magnitude;
                horizontal = toGain.MagnitudeSquared > 1.0 ? toGain.Normalized : inPlane.Normalized;
            }
        }
        double timeToGo = System.Math.Max(MinimumTimeToGoSeconds, horizontalDeficit / acceleration);

        // Linear vertical acceleration a(τ) = A + Bτ reaching Δh with zero final climb rate.
        double altitudeError = targetAltitudeM - body.GetAltitude(vessel.Position);
        double commandedVertical = 6.0 * altitudeError / (timeToGo * timeToGo)
            - 4.0 * verticalSpeed / timeToGo;
        double sine = System.Math.Clamp(
            (commandedVertical + gravity - centrifugal) / acceleration,
            -MaximumPitchSine, MaximumPitchSine);
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
