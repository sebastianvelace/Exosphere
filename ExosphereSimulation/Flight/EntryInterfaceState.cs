namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Body-centred entry-interface velocity. The declared speed is inertial relative to
/// the planet, matching the orbital-energy gate. Stacking Earth rotation on top of an
/// already orbital eastward speed puts the vehicle above circular velocity and it skips
/// out of the atmosphere before drag can capture it.
/// </summary>
public static class EntryInterfaceState
{
    public readonly record struct Velocity(
        Vector3d InertialRelativeToBody,
        Vector3d AtmosphereRelative,
        double InertialSpeedMps,
        double AtmosphereRelativeSpeedMps,
        double AtmosphereRelativeFlightPathRad);

    public static Velocity Compose(
        CelestialBody body,
        Vector3d position,
        Vector3d horizontalTrack,
        double inertialSpeedMps,
        double inertialFlightPathRad)
    {
        ArgumentNullException.ThrowIfNull(body);
        var up = body.GetGeodeticUp(position);
        if (up.MagnitudeSquared < 1e-12)
            up = Vector3d.Up;
        up = up.Normalized;

        var track = horizontalTrack - up * horizontalTrack.Dot(up);
        if (track.MagnitudeSquared < 1e-12)
            track = body.GetEastDirection(position);
        if (track.MagnitudeSquared < 1e-12)
            track = Vector3d.Right;
        track = track.Normalized;

        var inertialDirection = (
            track * System.Math.Cos(inertialFlightPathRad)
            + up * System.Math.Sin(inertialFlightPathRad)).Normalized;
        var inertial = inertialDirection * inertialSpeedMps;
        var air = inertial - body.GetSurfaceVelocity(position);
        double airSpeed = air.Magnitude;
        double airGamma = airSpeed > 1.0
            ? System.Math.Asin(System.Math.Clamp(air.Normalized.Dot(up), -1.0, 1.0))
            : inertialFlightPathRad;

        return new Velocity(
            inertial,
            air,
            inertial.Magnitude,
            airSpeed,
            airGamma);
    }

    public static double CircularSpeedMps(CelestialBody body, Vector3d position)
    {
        ArgumentNullException.ThrowIfNull(body);
        double radius = (position - body.Position).Magnitude;
        if (radius <= 1.0 || body.GM <= 0.0)
            return double.NaN;
        return System.Math.Sqrt(body.GM / radius);
    }
}
