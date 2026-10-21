namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>A liftoff time and the parking-orbit plane that contains the Moon at arrival.</summary>
/// <param name="LiftoffTime">Simulation time (s) of liftoff.</param>
/// <param name="AzimuthDegrees">Inertial launch azimuth at the pad, clockwise from north.</param>
/// <param name="PlaneNormal">Unit angular-momentum direction of the target parking orbit.</param>
public readonly record struct LunarLaunchWindowSolution(
    double LiftoffTime, double AzimuthDegrees, Vector3d PlaneNormal);

/// <summary>
/// Apollo-style lunar launch window. A coplanar translunar injection needs a parking orbit
/// whose plane contains the Moon's position at arrival. At liftoff that plane must also
/// contain the pad, so it is fixed by the pad and the Moon directions; Earth's rotation then
/// sweeps the required azimuth through the day and the window is open while it stays inside
/// the range-safety limits (Apollo: 72°–108°). Searches forward from the earliest liftoff.
/// </summary>
public static class LunarLaunchWindow
{
    public static LunarLaunchWindowSolution? FindNext(
        CelestialBody earth,
        OrbitalElements moonEphemeris,
        LaunchSite site,
        double earliestLiftoffTime,
        double liftoffToArrivalSeconds,
        double minimumAzimuthDegrees,
        double maximumAzimuthDegrees,
        double searchSpanSeconds = 86_400.0,
        double stepSeconds = 30.0)
    {
        ArgumentNullException.ThrowIfNull(earth);
        ArgumentNullException.ThrowIfNull(moonEphemeris);
        ArgumentNullException.ThrowIfNull(site);
        if (!double.IsFinite(earliestLiftoffTime) || !(liftoffToArrivalSeconds > 0.0)
            || !(stepSeconds > 0.0) || !(searchSpanSeconds > 0.0)
            || !(minimumAzimuthDegrees < maximumAzimuthDegrees))
            throw new ArgumentOutOfRangeException(nameof(liftoffToArrivalSeconds));

        for (double t = earliestLiftoffTime; t <= earliestLiftoffTime + searchSpanSeconds; t += stepSeconds)
        {
            var solution = Evaluate(earth, moonEphemeris, site, t, liftoffToArrivalSeconds);
            if (solution is { } s
                && s.AzimuthDegrees >= minimumAzimuthDegrees
                && s.AzimuthDegrees <= maximumAzimuthDegrees)
                return s;
        }
        return null;
    }

    /// <summary>
    /// Plane through the pad and the Moon's arrival position for a liftoff at
    /// <paramref name="liftoffTime"/>, oriented so the vehicle departs eastward.
    /// </summary>
    public static LunarLaunchWindowSolution? Evaluate(
        CelestialBody earth,
        OrbitalElements moonEphemeris,
        LaunchSite site,
        double liftoffTime,
        double liftoffToArrivalSeconds)
    {
        var pad = (site.GetPosition(earth, liftoffTime) - earth.Position).Normalized;
        var (moonPosition, _) = moonEphemeris.GetStateAtTime(liftoffTime + liftoffToArrivalSeconds, earth.GM);
        var normal = pad.Cross(moonPosition.Normalized);
        if (normal.MagnitudeSquared < 1e-12) return null;
        normal = normal.Normalized;

        var frame = site.GetLocalFrame(earth, liftoffTime);
        var departure = normal.Cross(pad);
        if (departure.Dot(frame.East) < 0.0)
        {
            normal = -normal;
            departure = -departure;
        }
        double azimuth = System.Math.Atan2(departure.Dot(frame.East), departure.Dot(frame.North))
            * MathUtils.RAD_TO_DEG;
        if (azimuth < 0.0) azimuth += 360.0;
        return new LunarLaunchWindowSolution(liftoffTime, azimuth, normal);
    }
}
