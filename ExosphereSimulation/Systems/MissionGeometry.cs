namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// Geometry helpers for mission systems (eclipse, comms range).
/// V1: Earth umbra via cone occlusion; no penumbra or multi-body shadows.
/// </summary>
public static class MissionGeometry
{
    /// <summary>
    /// Fraction of the apparent solar disc visible after occultation by a spherical body.
    /// Exact overlap area of the two apparent discs resolves full light, penumbra, totality
    /// and annular eclipses continuously.
    /// </summary>
    public static double SolarDiscVisibility(
        Vector3d observerPos,
        Vector3d occluderPos,
        double occluderRadius,
        Vector3d sunPos,
        double sunRadius)
    {
        var toSun = sunPos - observerPos;
        var toOcc = occluderPos - observerPos;
        double sunDist = toSun.Magnitude;
        double occDist = toOcc.Magnitude;
        if (sunDist <= sunRadius || occDist <= 0.0 || sunRadius <= 0.0 || occluderRadius <= 0.0)
            return 1.0;
        if (toSun.Dot(toOcc) <= 0.0 || occDist >= sunDist) return 1.0;

        double rs = System.Math.Asin(System.Math.Clamp(sunRadius / sunDist, 0.0, 1.0));
        double ro = System.Math.Asin(System.Math.Clamp(occluderRadius / occDist, 0.0, 1.0));
        double sep = System.Math.Acos(System.Math.Clamp(
            toSun.Normalized.Dot(toOcc.Normalized), -1.0, 1.0));

        if (sep >= rs + ro) return 1.0;
        if (ro >= sep + rs) return 0.0;
        if (rs >= sep + ro)
            return System.Math.Clamp(1.0 - ro * ro / (rs * rs), 0.0, 1.0);

        double x1 = System.Math.Clamp((sep * sep + rs * rs - ro * ro) / (2.0 * sep * rs), -1.0, 1.0);
        double x2 = System.Math.Clamp((sep * sep + ro * ro - rs * rs) / (2.0 * sep * ro), -1.0, 1.0);
        double lens = rs * rs * System.Math.Acos(x1)
                    + ro * ro * System.Math.Acos(x2)
                    - 0.5 * System.Math.Sqrt(System.Math.Max(0.0,
                        (-sep + rs + ro) * (sep + rs - ro)
                      * (sep - rs + ro) * (sep + rs + ro)));
        return System.Math.Clamp(1.0 - lens / (System.Math.PI * rs * rs), 0.0, 1.0);
    }

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
