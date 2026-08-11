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
    /// Visible irradiance fraction of a limb-darkened circular source after occultation.
    ///
    /// <para>The geometric overlap used by <see cref="SolarDiscVisibility"/> assumes a
    /// uniform photosphere.  The visible Sun is brighter toward its centre, so an opaque
    /// body covering the centre removes more irradiance than the same area at the limb.
    /// This deterministic polar quadrature integrates the linear limb-darkening law
    /// <c>I(mu) = 1 - u(1 - mu)</c> over the source disc.  Radii and separation are angular
    /// quantities, normalised by the source radius to remain well-conditioned for
    /// astronomical distances.</para>
    /// </summary>
    public static double LimbDarkenedDiscVisibility(
        double sourceAngularRadius,
        double occluderAngularRadius,
        double separation,
        double limbDarkening = 0.60,
        int radialSamples = 48,
        int angularSamples = 96)
    {
        if (!double.IsFinite(sourceAngularRadius) || !double.IsFinite(occluderAngularRadius)
            || !double.IsFinite(separation) || sourceAngularRadius <= 0.0
            || occluderAngularRadius < 0.0 || separation < 0.0)
            return 1.0;
        if (occluderAngularRadius == 0.0
            || separation >= sourceAngularRadius + occluderAngularRadius)
            return 1.0;
        if (occluderAngularRadius >= separation + sourceAngularRadius)
            return 0.0;

        int radial = System.Math.Max(8, radialSamples);
        int angular = System.Math.Max(16, angularSamples);
        double u = System.Math.Clamp(limbDarkening, 0.0, 1.0);
        double q = occluderAngularRadius / sourceAngularRadius;
        double s = separation / sourceAngularRadius;
        double visible = 0.0;
        double total = 0.0;
        for (int i = 0; i < radial; i++)
        {
            double r = (i + 0.5) / radial;
            double mu = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - r * r));
            double intensity = 1.0 - u * (1.0 - mu);
            double ringWeight = r * intensity;
            total += ringWeight;
            for (int j = 0; j < angular; j++)
            {
                double theta = (j + 0.5) * 2.0 * System.Math.PI / angular;
                double x = r * System.Math.Cos(theta) - s;
                double y = r * System.Math.Sin(theta);
                if (x * x + y * y > q * q)
                    visible += ringWeight;
            }
        }

        return System.Math.Clamp(visible / (total * angular), 0.0, 1.0);
    }

    /// <summary>
    /// Limb-darkened solar irradiance fraction for an observer and one spherical occluder.
    /// </summary>
    public static double LimbDarkenedSolarDiscVisibility(
        Vector3d observerPos,
        Vector3d occluderPos,
        double occluderRadius,
        Vector3d sunPos,
        double sunRadius,
        double limbDarkening = 0.60)
    {
        var toSun = sunPos - observerPos;
        var toOcc = occluderPos - observerPos;
        double sunDist = toSun.Magnitude;
        double occDist = toOcc.Magnitude;
        if (sunDist <= sunRadius || occDist <= 0.0 || sunRadius <= 0.0
            || occluderRadius <= 0.0)
            return 1.0;
        if (toSun.Dot(toOcc) <= 0.0 || occDist >= sunDist) return 1.0;

        double rs = ApparentAngularRadius(sunRadius, sunDist);
        double ro = ApparentAngularRadius(occluderRadius, occDist);
        var sunDir = toSun.Normalized;
        var occDir = toOcc.Normalized;
        double sep = System.Math.Atan2(sunDir.Cross(occDir).Magnitude,
            System.Math.Clamp(sunDir.Dot(occDir), -1.0, 1.0));
        return LimbDarkenedDiscVisibility(rs, ro, sep, limbDarkening);
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
