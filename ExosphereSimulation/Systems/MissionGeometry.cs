namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// Geometry helpers for mission systems (eclipse, comms range).
/// V1: Earth umbra via cone occlusion; no penumbra or multi-body shadows.
/// </summary>
public static class MissionGeometry
{
    /// <summary>Apparent angular radius of a sphere, in radians.</summary>
    public static double ApparentAngularRadius(double physicalRadius, double distance)
    {
        if (!double.IsFinite(physicalRadius) || !double.IsFinite(distance)
            || physicalRadius <= 0.0 || distance <= 0.0)
            return 0.0;
        return System.Math.Asin(System.Math.Clamp(physicalRadius / distance, 0.0, 1.0));
    }

    /// <summary>Visible fraction of a circular source after overlap by one circular occluder.</summary>
    public static double DiscVisibility(
        double sourceAngularRadius, double occluderAngularRadius, double separation)
    {
        if (!double.IsFinite(sourceAngularRadius) || !double.IsFinite(occluderAngularRadius)
            || !double.IsFinite(separation) || sourceAngularRadius <= 0.0
            || occluderAngularRadius < 0.0 || separation < 0.0)
            return 1.0;
        double rs = sourceAngularRadius;
        double ro = occluderAngularRadius;
        if (ro == 0.0 || separation >= rs + ro) return 1.0;
        if (ro >= separation + rs) return 0.0;
        if (rs >= separation + ro)
            return System.Math.Clamp(1.0 - ro * ro / (rs * rs), 0.0, 1.0);

        // Normalise by the source radius before evaluating the lens. This avoids loss of
        // precision for astronomical discs whose angular radii are only milliradians.
        double q = ro / rs;
        double s = separation / rs;
        double x1 = System.Math.Clamp((s * s + 1.0 - q * q) / (2.0 * s), -1.0, 1.0);
        double x2 = System.Math.Clamp((s * s + q * q - 1.0) / (2.0 * s * q), -1.0, 1.0);
        double lens = System.Math.Acos(x1) + q * q * System.Math.Acos(x2)
            - 0.5 * System.Math.Sqrt(System.Math.Max(0.0,
                (-s + 1.0 + q) * (s + 1.0 - q)
              * (s - 1.0 + q) * (s + 1.0 + q)));
        return System.Math.Clamp(1.0 - lens / System.Math.PI, 0.0, 1.0);
    }

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

        double rs = ApparentAngularRadius(sunRadius, sunDist);
        double ro = ApparentAngularRadius(occluderRadius, occDist);
        var sunDir = toSun.Normalized;
        var occDir = toOcc.Normalized;
        double sep = System.Math.Atan2(sunDir.Cross(occDir).Magnitude,
            System.Math.Clamp(sunDir.Dot(occDir), -1.0, 1.0));
        return DiscVisibility(rs, ro, sep);
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
