namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Visible-spectrum optical profile for a planetary atmosphere. Scattering and absorption
/// coefficients are RGB-band extinction coefficients at the surface in m⁻¹. Scale heights
/// describe the exponential vertical distributions used by the renderer and diagnostics.
/// </summary>
public sealed record AtmosphereOptics
{
    public Vector3d RayleighScattering { get; init; } = Vector3d.Zero;
    public Vector3d MieScattering { get; init; } = Vector3d.Zero;
    public Vector3d MieAbsorption { get; init; } = Vector3d.Zero;
    public Vector3d OzoneAbsorption { get; init; } = Vector3d.Zero;
    public double RayleighScaleHeight { get; init; } = 8_000.0;
    public double MieScaleHeight { get; init; } = 1_200.0;
    public double OzoneCenterAltitude { get; init; } = 25_000.0;
    public double OzoneHalfWidth { get; init; } = 15_000.0;
    /// <summary>
    /// Visible upper-atmosphere chemiluminescence at the layer peak, in relative radiance
    /// emitted per metre.  It is intentionally independent of solar scattering: airglow
    /// remains present on the night limb after direct sunlight has vanished.
    /// </summary>
    public Vector3d AirglowEmission { get; init; } = Vector3d.Zero;
    public double AirglowCenterAltitude { get; init; } = 97_000.0;
    public double AirglowScaleHeight { get; init; } = 6_000.0;
    public double MieAnisotropy { get; init; } = 0.80;
    public double SunIlluminanceScale { get; init; } = 20.0;
    /// <summary>Bounded isotropic second-order fill used by the realtime sky integrator.</summary>
    public double LowOrderDiffuseStrength { get; init; } = 0.25;
    public double CloudBaseAltitude { get; init; } = 0.0;
    public double CloudTopAltitude { get; init; } = 0.0;
    public double CloudExtinction { get; init; } = 0.0;
    public double CloudCoverage { get; init; } = 0.0;
    public double CloudWindRadiansPerSecond { get; init; } = 0.0;

    public bool HasCloudLayer => AreFinite(CloudBaseAltitude, CloudTopAltitude,
            CloudExtinction, CloudCoverage, CloudWindRadiansPerSecond)
        && CloudBaseAltitude >= 0.0 && CloudTopAltitude > CloudBaseAltitude
        && CloudExtinction > 0.0 && CloudCoverage is > 0.0 and <= 1.0;

    /// <summary>Normalised vertical density envelope with soft cloud-base and anvil fades.</summary>
    public double CloudVerticalDensity(double altitude)
    {
        if (!HasCloudLayer || altitude <= CloudBaseAltitude || altitude >= CloudTopAltitude)
            return 0.0;
        double fraction = (altitude - CloudBaseAltitude) /
            (CloudTopAltitude - CloudBaseAltitude);
        double baseFade = Smoothstep(0.0, 0.12, fraction);
        double topFade = 1.0 - Smoothstep(0.72, 1.0, fraction);
        return baseFade * topFade;
    }

    /// <summary>
    /// Converts a normalised weather-map sample into cloud occupancy. Coverage controls the
    /// occupied fraction for a uniformly distributed map; the soft edge avoids binary clouds.
    /// </summary>
    public double CloudWeatherDensity(double weatherSample)
    {
        if (!HasCloudLayer || !double.IsFinite(weatherSample)) return 0.0;
        double threshold = 1.0 - CloudCoverage;
        return Smoothstep(threshold - 0.12, threshold + 0.10,
            System.Math.Clamp(weatherSample, 0.0, 1.0));
    }

    public double CloudLocalExtinction(double altitude, double weatherSample) =>
        CloudExtinction * CloudVerticalDensity(altitude) * CloudWeatherDensity(weatherSample);

    public Vector3d MieExtinction => MieScattering + MieAbsorption;
    public bool IsEnabled => RayleighScattering.MagnitudeSquared > 0.0
        || MieExtinction.MagnitudeSquared > 0.0;

    public double RayleighDensity(double altitude) => ExponentialDensity(
        altitude, RayleighScaleHeight);

    public double MieDensity(double altitude) => ExponentialDensity(
        altitude, MieScaleHeight);

    /// <summary>
    /// Ozone layer density proxy: triangular distribution centred in the stratosphere.
    /// It integrates to <c>OzoneHalfWidth</c>, so the coefficient remains a local m⁻¹ value.
    /// </summary>
    public double OzoneDensity(double altitude)
    {
        if (OzoneHalfWidth <= 0.0) return 0.0;
        return System.Math.Max(0.0,
            1.0 - System.Math.Abs(altitude - OzoneCenterAltitude) / OzoneHalfWidth);
    }

    /// <summary>Gaussian mesospheric/lower-thermospheric airglow layer.</summary>
    public double AirglowDensity(double altitude)
    {
        if (AirglowScaleHeight <= 0.0 || !double.IsFinite(altitude)) return 0.0;
        double z = (altitude - AirglowCenterAltitude) / AirglowScaleHeight;
        return System.Math.Exp(-0.5 * z * z);
    }

    /// <summary>Vertical optical depth from altitude to space for the RGB bands.</summary>
    public Vector3d VerticalOpticalDepth(double altitude)
    {
        altitude = System.Math.Max(0.0, altitude);
        double rayleighColumn = RayleighScaleHeight > 0.0
            ? RayleighScaleHeight * System.Math.Exp(-altitude / RayleighScaleHeight)
            : 0.0;
        double mieColumn = MieScaleHeight > 0.0
            ? MieScaleHeight * System.Math.Exp(-altitude / MieScaleHeight)
            : 0.0;
        double ozoneColumn = OzoneColumnAbove(altitude);
        return RayleighScattering * rayleighColumn
            + MieExtinction * mieColumn
            + OzoneAbsorption * ozoneColumn;
    }

    public Vector3d VerticalTransmittance(double altitude)
    {
        var tau = VerticalOpticalDepth(altitude);
        return new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
    }

    /// <summary>
    /// Direct-sun RGB transmittance through a plane-parallel optical column. The Kasten–Young
    /// relative air-mass expression remains stable close to the horizon, unlike 1/cos(z).
    /// Below the geometric horizon the direct beam is zero (twilight remains sky scattering).
    /// </summary>
    public Vector3d DirectSolarTransmittance(double altitude, double sunElevationSin)
    {
        if (sunElevationSin <= 0.0) return Vector3d.Zero;
        double cosZenith = System.Math.Clamp(sunElevationSin, 0.0, 1.0);
        double zenithDegrees = System.Math.Acos(cosZenith) * 180.0 / System.Math.PI;
        double airMass = 1.0 / (cosZenith
            + 0.50572 * System.Math.Pow(96.07995 - zenithDegrees, -1.6364));
        var tau = VerticalOpticalDepth(altitude) * airMass;
        return new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
    }

    /// <summary>
    /// Local second-order source approximation. Light removed from the direct solar beam is
    /// redistributed isotropically, bounded per colour band by the single-scattering albedo.
    /// Solid planetary shadow is an explicit zero: an opaque planet is not a scattering event.
    /// </summary>
    public Vector3d LowOrderDiffuseSource(
        Vector3d density, Vector3d solarTransmittance, bool planetOccluded = false)
    {
        if (planetOccluded || LowOrderDiffuseStrength <= 0.0) return Vector3d.Zero;
        var scattering = RayleighScattering * density.X + MieScattering * density.Y;
        var ext = scattering + MieAbsorption * density.Y + OzoneAbsorption * density.Z;
        const double invFourPi = 1.0 / (4.0 * System.Math.PI);
        return new Vector3d(
            DiffuseBand(scattering.X, ext.X, solarTransmittance.X),
            DiffuseBand(scattering.Y, ext.Y, solarTransmittance.Y),
            DiffuseBand(scattering.Z, ext.Z, solarTransmittance.Z)) *
            (LowOrderDiffuseStrength * invFourPi);
    }

    private static double DiffuseBand(double scattering, double extinction, double solarT)
    {
        if (scattering <= 0.0 || extinction <= 0.0) return 0.0;
        double albedo = System.Math.Clamp(scattering / extinction, 0.0, 1.0);
        double removed = System.Math.Clamp(1.0 - solarT, 0.0, 1.0);
        return scattering * albedo * removed;
    }

    private static double Smoothstep(double low, double high, double value)
    {
        double t = System.Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static bool AreFinite(params double[] values) =>
        values.All(double.IsFinite);

    private double OzoneColumnAbove(double altitude)
    {
        if (OzoneHalfWidth <= 0.0) return 0.0;
        double low = OzoneCenterAltitude - OzoneHalfWidth;
        double high = OzoneCenterAltitude + OzoneHalfWidth;
        if (altitude >= high) return 0.0;
        if (altitude <= low) return OzoneHalfWidth;

        if (altitude < OzoneCenterAltitude)
        {
            double x = altitude - low;
            double risingArea = OzoneHalfWidth * 0.5
                - x * x / (2.0 * OzoneHalfWidth);
            return risingArea + OzoneHalfWidth * 0.5;
        }

        double remaining = high - altitude;
        return remaining * remaining / (2.0 * OzoneHalfWidth);
    }

    private static double ExponentialDensity(double altitude, double scaleHeight) =>
        scaleHeight > 0.0
            ? System.Math.Exp(-System.Math.Max(0.0, altitude) / scaleHeight)
            : 0.0;
}
