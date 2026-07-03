namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// Geometry helpers for mission systems (eclipse, comms range).
/// V1: Earth umbra via cone occlusion; no penumbra or multi-body shadows.
/// </summary>
public static class MissionGeometry
{
    /// <summary>
    /// True when the vessel lies inside Earth's umbral cone (Sun blocked by Earth).
    /// Approximation: treats Sun as a point source; ignores Moon shadow.
    /// </summary>
    public static bool IsInEarthUmbra(Vector3d vesselPos, Vector3d earthPos, Vector3d sunPos, double earthRadius)
    {
        var toSun   = sunPos - vesselPos;
        var toEarth = earthPos - vesselPos;
        double toEarthMag = toEarth.Magnitude;
        if (toEarthMag < 0.1) return false;

        double cosAngle = System.Math.Clamp(toSun.Normalized.Dot(toEarth.Normalized), -1.0, 1.0);
        double angle    = System.Math.Acos(cosAngle);
        double shadowHalfAngle = System.Math.Asin(
            System.Math.Clamp(earthRadius / toEarthMag, 0.0, 1.0));
        return angle < shadowHalfAngle;
    }

    /// <summary>
    /// One-way signal delay (seconds) from vessel to Earth at speed of light.
    /// When earthRadius is provided, uses slant range to the surface (nadir link).
    /// </summary>
    public static double SignalDelaySeconds(Vector3d vesselPos, Vector3d earthPos,
        double speedOfLight = 3e8, double earthRadius = 0.0)
    {
        double dist = (vesselPos - earthPos).Magnitude;
        if (earthRadius > 0.0)
            dist = System.Math.Max(dist - earthRadius, 0.0);
        return dist / speedOfLight;
    }
}
