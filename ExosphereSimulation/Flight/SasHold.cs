namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>Attitude the SAS holds: rate damping only, or pointing the nose along a vector.</summary>
public enum SasHoldMode
{
    Stability,
    Prograde,
    Retrograde,
    Normal,
    AntiNormal,
    RadialOut,
    RadialIn,
    Target,
    AntiTarget,
    Maneuver,
}

/// <summary>
/// Pilot SAS hold modes. Inside the atmosphere the velocity vectors are surface-relative
/// (what drag and heating see); above it they are orbital (what a burn changes). Normal is
/// the orbit's angular-momentum direction r × v and radial-out lies in the orbit plane, away
/// from the body. The command only points the nose; the vehicle's real control authority
/// (RCS, reaction control, gimbal) decides how fast it gets there.
/// </summary>
public static class SasHold
{
    public const double ProportionalGain = 2.3;

    /// <summary>
    /// World direction the nose should point for <paramref name="mode"/>, or null when it
    /// is undefined (stability mode, no target, no maneuver, no relative motion).
    /// </summary>
    public static Vector3d? Direction(Vessel vessel, CelestialBody body, SasHoldMode mode,
        Vessel? target = null, Vector3d? maneuverDeltaV = null)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        var radial = vessel.Position - body.Position;
        if (radial.MagnitudeSquared < 1e-6) return null;
        var velocity = UsesSurfaceFrame(vessel, body)
            ? vessel.GetSurfaceVelocity(body)
            : vessel.Velocity - body.Velocity;
        var orbitalVelocity = vessel.Velocity - body.Velocity;
        var normal = radial.Cross(orbitalVelocity);

        Vector3d? direction = mode switch
        {
            SasHoldMode.Prograde => velocity,
            SasHoldMode.Retrograde => -velocity,
            SasHoldMode.Normal => normal,
            SasHoldMode.AntiNormal => -normal,
            // v × (r × v) = r v² − v (v·r): in the orbit plane, away from the body.
            SasHoldMode.RadialOut => orbitalVelocity.Cross(normal),
            SasHoldMode.RadialIn => -orbitalVelocity.Cross(normal),
            SasHoldMode.Target when target != null => target.Position - vessel.Position,
            SasHoldMode.AntiTarget when target != null => vessel.Position - target.Position,
            SasHoldMode.Maneuver => maneuverDeltaV,
            _ => null,
        };
        return direction is { } d && d.MagnitudeSquared > 1e-9 ? d.Normalized : null;
    }

    /// <summary>Surface frame inside the atmosphere (or within 1 km of an airless surface).</summary>
    public static bool UsesSurfaceFrame(Vessel vessel, CelestialBody body)
    {
        double altitude = vessel.GetAltitude(body);
        double frameTop = body.Atmosphere?.MaxAltitude ?? 1_000.0;
        return altitude < frameTop;
    }

    /// <summary>
    /// Pitch/yaw/roll command that turns the nose onto <paramref name="direction"/>, damped to
    /// the vehicle's angular authority so a weak-RCS capsule does not overshoot.
    /// </summary>
    public static Vector3d Command(Vessel vessel, CelestialBody body, Vector3d direction)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        return AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation, Vector3d.Up, direction,
            vessel.AngularVelocity, proportionalGain: ProportionalGain,
            dampingGain: AttitudeGuidance.DampingGainFor(ProportionalGain, vessel.GetPitchYawAngularAuthority(body)));
    }
}
