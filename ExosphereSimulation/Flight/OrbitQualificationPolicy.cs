namespace Exosphere.Simulation.Flight;

/// <summary>
/// Shared definition of a stable parking orbit.  Altitude and speed alone are not
/// sufficient: a fast vehicle at apoapsis may still have a periapsis inside the body.
/// </summary>
public static class OrbitQualificationPolicy
{
    public static bool HasSafePeriapsis(
        OrbitalElements trajectory,
        double bodyRadiusM,
        double minimumPeriapsisAltitudeM)
    {
        ArgumentNullException.ThrowIfNull(trajectory);

        return !trajectory.IsRadial
            && !trajectory.IsHyperbolic
            && double.IsFinite(trajectory.Periapsis)
            && trajectory.Periapsis
                >= bodyRadiusM + System.Math.Max(0.0, minimumPeriapsisAltitudeM);
    }
}
