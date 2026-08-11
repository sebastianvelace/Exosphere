namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// One of the nine visible bands used by the CPU spectral reference.  The optical
/// coefficients are local extinction/scattering coefficients in m^-1 at the surface
/// reference state; the vertical species profile is supplied by
/// <see cref="AtmosphereDensityProfile"/>.
/// </summary>
public readonly record struct SpectralBand(
    double WavelengthNm,
    double RayleighScattering,
    double MieScattering,
    double MieAbsorption,
    double OzoneAbsorption);

/// <summary>Immutable vector of nine visible-band values.</summary>
public class SpectralVector : IReadOnlyList<double>
{
    private readonly double[] _values;

    public SpectralVector(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToArray();
        if (_values.Length != SpectralAtmosphereOracle.BandCount)
            throw new ArgumentException(
                $"A spectral vector requires {SpectralAtmosphereOracle.BandCount} values.",
                nameof(values));
        if (_values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Spectral vectors must contain finite values.", nameof(values));
    }

    public int Count => _values.Length;
    public int Length => _values.Length;
    public double this[int index] => _values[index];
    public IReadOnlyList<double> Values => Array.AsReadOnly(_values);

    public double Sum => _values.Sum();
    public double Maximum => _values.Max();
    public double Minimum => _values.Min();

    public Vector3d ToLinearRgb() => SpectralColorConverter.ToLinearRgb(_values);
    public Vector3d ToXyz() => SpectralColorConverter.ToXyz(_values);

    public IEnumerator<double> GetEnumerator() => ((IEnumerable<double>)_values).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        _values.GetEnumerator();
}

/// <summary>Radiance returned by <see cref="SpectralAtmosphereOracle.Evaluate"/>.</summary>
public sealed class SpectralRadiance : SpectralVector
{
    public SpectralRadiance(IEnumerable<double> values) : base(values) { }

    /// <summary>Band-integrated radiance using the fixed 37.5 nm quadrature.</summary>
    public double Energy => SpectralColorConverter.IntegratedEnergy(this);
}

/// <summary>
/// Fixed CIE 1931 to linear-sRGB conversion for the nine-band reference.  The table is
/// intentionally embedded in code: a validation oracle must not depend on GPU colour
/// management or on the current display profile.
/// </summary>
public static class SpectralColorConverter
{
    // Approximate CIE 1931 2-degree colour matching functions sampled at the same
    // 37.5 nm centres as the oracle.  The values are normalized so a flat spectrum
    // produces Y ~= 1 after quadrature.
    private static readonly Vector3d[] Cie1931 =
    {
        new(0.0143, 0.0004, 0.0679),
        new(0.3370, 0.0180, 1.6500),
        new(0.1400, 0.1150, 0.7200),
        new(0.0160, 0.5700, 0.1400),
        new(0.4335, 0.9950, 0.0088),
        new(0.9163, 0.8129, 0.0017),
        new(0.7330, 0.2650, 0.0000),
        new(0.1640, 0.0610, 0.0000),
        new(0.0041, 0.0040, 0.0000),
    };

    private const double BandWidthNm = 37.5;
    private static readonly double FlatSpectrumY =
        Cie1931.Sum(cmf => cmf.Y) * BandWidthNm;

    public static Vector3d ToXyz(IReadOnlyList<double> spectrum)
    {
        ValidateSpectrum(spectrum);
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        for (int i = 0; i < Cie1931.Length; i++)
        {
            double value = System.Math.Max(0.0, spectrum[i]);
            x += value * Cie1931[i].X;
            y += value * Cie1931[i].Y;
            z += value * Cie1931[i].Z;
        }

        double normalization = BandWidthNm / System.Math.Max(FlatSpectrumY, 1e-12);
        return new Vector3d(x * normalization, y * normalization, z * normalization);
    }

    /// <summary>
    /// Converts CIE XYZ to linear sRGB using the fixed D65 matrix. Negative display primaries
    /// are clipped only at this boundary; the spectral transport itself remains un-clipped.
    /// </summary>
    public static Vector3d ToLinearRgb(IReadOnlyList<double> spectrum)
    {
        var xyz = ToXyz(spectrum);
        return new Vector3d(
            ClampFinite(3.2406 * xyz.X - 1.5372 * xyz.Y - 0.4986 * xyz.Z),
            ClampFinite(-0.9689 * xyz.X + 1.8758 * xyz.Y + 0.0415 * xyz.Z),
            ClampFinite(0.0557 * xyz.X - 0.2040 * xyz.Y + 1.0570 * xyz.Z));
    }

    public static double IntegratedEnergy(IReadOnlyList<double> spectrum)
    {
        ValidateSpectrum(spectrum);
        return spectrum.Sum(value => System.Math.Max(0.0, value)) * BandWidthNm;
    }

    internal static Vector3d CieAt(int index) => Cie1931[index];
    internal static double BandWidth => BandWidthNm;

    private static double ClampFinite(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static void ValidateSpectrum(IReadOnlyList<double> spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (spectrum.Count != SpectralAtmosphereOracle.BandCount)
            throw new ArgumentException(
                $"A spectral vector requires {SpectralAtmosphereOracle.BandCount} values.",
                nameof(spectrum));
    }
}

/// <summary>
/// CPU reference for visible atmospheric scattering.  It reconstructs nine bands from the
/// RGB coefficients currently present in the body JSON, then performs deterministic spherical
/// transport and bounded successive scattering through orders 2..5.
///
/// This is an offline validation oracle, not a renderer replacement.  In particular, the
/// spectral coefficients are marked <c>reconstructed</c> until measured per-wavelength body
/// data is added to the JSON profiles.
/// </summary>
public sealed class SpectralAtmosphereOracle
{
    public const int BandCount = 9;
    public const int OfficialRendererOrder = 4;
    public const int ExperimentalOrder = 5;
    public const double DefaultPlanetRadius = 6_371_000.0;

    private static readonly double[] Centers =
    {
        400.0, 437.5, 475.0, 512.5, 550.0, 587.5, 625.0, 662.5, 700.0,
    };

    private readonly double[] _energyByOrder;
    private readonly AtmosphereDensityProfile _profile;
    private readonly AtmosphereOptics _optics;
    private readonly SpectralBand[] _bands;

    private SpectralAtmosphereOracle(
        AtmosphereDensityProfile profile,
        double planetRadius,
        int maxScatteringOrder,
        int sampleCount)
    {
        _profile = profile;
        _optics = profile.Optics;
        PlanetRadius = planetRadius;
        MaxScatteringOrder = maxScatteringOrder;
        SampleCount = sampleCount % 2 == 0 ? sampleCount : sampleCount + 1;
        _bands = BuildBands(_optics);
        _energyByOrder = new double[maxScatteringOrder + 1];

        // Cache the canonical full-sun zenith result. This makes EnergyByOrder deterministic
        // and cheap for comparators while keeping arbitrary Evaluate calls side-effect free.
        var canonical = EvaluateCore(0.0, System.Math.PI * 0.5, 1.0, 1.0, 1.0,
            includeAirglow: false);
        double accumulatedEnergy = 0.0;
        for (int order = 2; order <= maxScatteringOrder; order++)
        {
            accumulatedEnergy += canonical.OrderEnergy(order);
            _energyByOrder[order] = accumulatedEnergy;
        }
    }

    public AtmosphereDensityProfile Profile => _profile;
    public AtmosphereModel Atmosphere => _profile.Atmosphere;
    public double PlanetRadius { get; }
    public double AtmosphereTopAltitude => _profile.TopAltitude;
    public int MaxScatteringOrder { get; }
    public int SampleCount { get; }
    public string DataProvenance => _optics.SpectralDataStatus;
    public bool IsReconstructed => !string.Equals(
        _optics.SpectralDataStatus, "measured", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<double> BandCentersNm => Array.AsReadOnly(Centers);
    public IReadOnlyList<SpectralBand> Bands => Array.AsReadOnly(_bands);

    public static SpectralAtmosphereOracle Build(
        AtmosphereModel profile,
        int maxOrder = OfficialRendererOrder,
        int sampleCount = 32,
        double planetRadius = DefaultPlanetRadius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Build(new AtmosphereDensityProfile(profile), maxOrder, sampleCount, planetRadius);
    }

    public static SpectralAtmosphereOracle Build(
        AtmosphereDensityProfile profile,
        int maxOrder = OfficialRendererOrder,
        int sampleCount = 32,
        double planetRadius = DefaultPlanetRadius)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateBuildArguments(maxOrder, sampleCount, planetRadius);
        return new SpectralAtmosphereOracle(profile, planetRadius, maxOrder, sampleCount);
    }

    public static SpectralAtmosphereOracle Build(
        CelestialBody body,
        int maxOrder = OfficialRendererOrder,
        int sampleCount = 32)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Atmosphere is null)
            throw new ArgumentException("The body must provide an atmosphere.", nameof(body));
        return Build(new AtmosphereDensityProfile(body.Atmosphere), maxOrder, sampleCount,
            body.Radius);
    }

    /// <summary>
    /// Evaluates radiance. <paramref name="solarElevationRadians"/> is a geometric solar
    /// elevation in radians; a small negative value can still produce a refracted limb. The
    /// view cosine is relative to local up and the sun cosine is the scattering phase cosine.
    /// </summary>
    public SpectralRadiance Evaluate(
        double altitude,
        double solarElevationRadians,
        double viewCosine,
        double viewSunCosine) =>
        Evaluate(altitude, solarElevationRadians, viewCosine, viewSunCosine, 1.0);

    /// <summary>Same as <see cref="Evaluate(double,double,double,double)"/> with eclipse visibility.</summary>
    public SpectralRadiance Evaluate(
        double altitude,
        double solarElevationRadians,
        double viewCosine,
        double viewSunCosine,
        double solarVisibility)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(solarElevationRadians)
            || !double.IsFinite(viewCosine) || !double.IsFinite(viewSunCosine)
            || !double.IsFinite(solarVisibility))
            return ZeroRadiance();

        return EvaluateCore(altitude, solarElevationRadians, viewCosine,
            viewSunCosine, System.Math.Clamp(solarVisibility, 0.0, 1.0),
            includeAirglow: true).Radiance;
    }

    /// <summary>Explicit sine form for callers that already use the renderer's LUT coordinate.</summary>
    public SpectralRadiance EvaluateSine(
        double altitude,
        double solarElevationSin,
        double viewCosine,
        double viewSunCosine,
        double solarVisibility = 1.0) => Evaluate(
            altitude,
            System.Math.Asin(System.Math.Clamp(solarElevationSin, -1.0, 1.0)),
            viewCosine,
            viewSunCosine,
            solarVisibility);

    public SpectralVector VerticalOpticalDepth(double altitude) =>
        new(IntegrateVertical(altitude, includeAbsorption: true));

    public SpectralVector VerticalTransmittance(double altitude)
    {
        var tau = VerticalOpticalDepth(altitude);
        return new SpectralVector(tau.Select(value => System.Math.Exp(-value)));
    }

    /// <summary>
    /// Returns the cumulative band-integrated energy through the canonical ground/zenith
    /// full-sun sample up to the requested order. Order 5 is intentionally diagnostic only.
    /// </summary>
    public double EnergyByOrder(int order)
    {
        if (order < 2 || order > MaxScatteringOrder)
            throw new ArgumentOutOfRangeException(nameof(order));
        return _energyByOrder[order];
    }

    public SpectralVector EnergySpectrumByOrder(int order)
    {
        if (order < 2 || order > MaxScatteringOrder)
            throw new ArgumentOutOfRangeException(nameof(order));
        var result = EvaluateCore(0.0, System.Math.PI * 0.5, 1.0, 1.0, 1.0,
            includeAirglow: false).CumulativeAt(order);
        return new SpectralVector(result);
    }

    public double[] GetSpectralCoefficients(int order = 0)
    {
        if (order != 0)
            throw new ArgumentOutOfRangeException(nameof(order), "Coefficients are order independent.");
        var values = new double[BandCount * 4];
        for (int i = 0; i < BandCount; i++)
        {
            values[i * 4] = _bands[i].RayleighScattering;
            values[i * 4 + 1] = _bands[i].MieScattering;
            values[i * 4 + 2] = _bands[i].MieAbsorption;
            values[i * 4 + 3] = _bands[i].OzoneAbsorption;
        }
        return values;
    }

    private EvaluationResult EvaluateCore(
        double altitude,
        double solarElevationRadians,
        double viewCosine,
        double viewSunCosine,
        double solarVisibility,
        bool includeAirglow)
    {
        var zero = new double[BandCount];
        if (altitude < 0.0 || altitude >= AtmosphereTopAltitude || viewCosine <= 0.0)
            return new EvaluationResult(new SpectralRadiance(zero), zero, zero, zero, zero, zero);

        double solarSin = System.Math.Sin(System.Math.Clamp(
            solarElevationRadians, -System.Math.PI * 0.5, System.Math.PI * 0.5));
        double apparentSin = ApparentSolarElevationSin(altitude, solarSin);
        bool sunVisible = apparentSin > 0.0 && solarVisibility > 0.0;
        double viewDistance = RayDistanceToTop(altitude, viewCosine);
        if (viewDistance <= 0.0)
            return new EvaluationResult(new SpectralRadiance(zero), zero, zero, zero, zero, zero);

        int n = SampleCount;
        double step = viewDistance / n;
        var cumulative = new double[BandCount];
        var orderEnergy = new double[MaxScatteringOrder + 1];
        var delta = new double[BandCount];
        var viewTau = new double[BandCount];

        for (int i = 0; i < n; i++)
        {
            double distance = (i + 0.5) * step;
            double radius = RayRadius(altitude, viewCosine, distance);
            double localAltitude = radius - PlanetRadius;
            var density = _profile.Sample(localAltitude);
            var solar = sunVisible
                ? SolarTransmittance(localAltitude, apparentSin)
                : new double[BandCount];
            if (solarVisibility != 1.0)
                for (int band = 0; band < BandCount; band++)
                    solar[band] *= solarVisibility;

            double phaseRayleigh = 3.0 / (16.0 * System.Math.PI)
                * (1.0 + viewSunCosine * viewSunCosine);
            double g = System.Math.Clamp(_optics.MieAnisotropy, -0.95, 0.95);
            double phaseMie = (1.0 - g * g)
                / (4.0 * System.Math.PI * System.Math.Pow(
                    System.Math.Max(1.0 + g * g - 2.0 * g * viewSunCosine, 1e-6), 1.5));

            for (int band = 0; band < BandCount; band++)
            {
                var coefficient = _bands[band];
                double betaRayleigh = coefficient.RayleighScattering * density.X;
                double betaMie = coefficient.MieScattering * density.Y;
                double betaAbsorption = (coefficient.MieAbsorption * density.Y)
                    + coefficient.OzoneAbsorption * density.Z;
                double betaExtinction = betaRayleigh + betaMie + betaAbsorption;
                double viewThroughput = System.Math.Exp(-viewTau[band]);
                double source = solar[band] * (betaRayleigh * phaseRayleigh + betaMie * phaseMie);
                delta[band] = source * viewThroughput;

                for (int order = 2; order <= MaxScatteringOrder; order++)
                {
                    double q = SuccessiveOrderRatio(betaRayleigh + betaMie,
                        betaExtinction, step);
                    double contribution = delta[band] * System.Math.Pow(q, order - 2) * step;
                    cumulative[band] += contribution;
                    orderEnergy[order] += contribution;
                }

                viewTau[band] += betaExtinction * step;
            }
        }

        if (includeAirglow)
            AddAirglow(cumulative, altitude, viewCosine, solarSin);

        var radiance = new SpectralRadiance(cumulative);
        return new EvaluationResult(radiance, cumulative, orderEnergy,
            CopyCumulativeAt(orderEnergy, cumulative, 2),
            CopyCumulativeAt(orderEnergy, cumulative, 3),
            CopyCumulativeAt(orderEnergy, cumulative, 4));
    }

    private void AddAirglow(double[] radiance, double altitude, double viewCosine, double solarSin)
    {
        double visibility = _optics.AirglowSolarVisibility(solarSin);
        if (visibility <= 0.0 || viewCosine <= 0.0) return;
        double pathGain = 1.0 / System.Math.Max(viewCosine, 0.08);
        for (int i = 0; i < BandCount; i++)
        {
            double emission = ReconstructAt(_optics.AirglowEmission, Centers[i])
                * _optics.AirglowDensity(altitude)
                + ReconstructAt(_optics.AirglowSecondaryEmission, Centers[i])
                    * _optics.AirglowSecondaryDensity(altitude);
            radiance[i] += emission * visibility * pathGain * _optics.SunIlluminanceScale;
        }
    }

    private double[] SolarTransmittance(double altitude, double solarSin)
    {
        var result = new double[BandCount];
        double distance = RayDistanceToTop(altitude, solarSin);
        if (distance <= 0.0) return result;
        double step = distance / SampleCount;
        for (int band = 0; band < BandCount; band++)
        {
            double tau = 0.0;
            for (int i = 0; i <= SampleCount; i++)
            {
                double d = step * i;
                double radius = RayRadius(altitude, solarSin, d);
                double localAltitude = radius - PlanetRadius;
                double densityRayleigh = _profile.Sample(localAltitude).X;
                var density = _profile.Sample(localAltitude);
                var coefficient = _bands[band];
                double extinction = coefficient.RayleighScattering * densityRayleigh
                    + (coefficient.MieScattering + coefficient.MieAbsorption) * density.Y
                    + coefficient.OzoneAbsorption * density.Z;
                int weight = i == 0 || i == SampleCount ? 1 : (i % 2 == 0 ? 2 : 4);
                tau += weight * extinction;
            }
            result[band] = System.Math.Exp(-tau * step / 3.0);
        }
        return result;
    }

    private double[] IntegrateVertical(double altitude, bool includeAbsorption)
    {
        if (!double.IsFinite(altitude) || altitude >= AtmosphereTopAltitude)
            return new double[BandCount];
        double start = System.Math.Max(0.0, altitude);
        double span = AtmosphereTopAltitude - start;
        int n = SampleCount;
        var result = new double[BandCount];
        double du = 1.0 / n;
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            double localAltitude = start + span * u * u;
            double dhDu = 2.0 * span * u;
            var density = _profile.Sample(localAltitude);
            int weight = i == 0 || i == n ? 1 : (i % 2 == 0 ? 2 : 4);
            for (int band = 0; band < BandCount; band++)
            {
                var coefficient = _bands[band];
                result[band] += weight * dhDu * (
                    coefficient.RayleighScattering * density.X
                    + (coefficient.MieScattering + coefficient.MieAbsorption) * density.Y
                    + (includeAbsorption ? coefficient.OzoneAbsorption * density.Z : 0.0));
            }
        }
        for (int band = 0; band < BandCount; band++) result[band] *= du / 3.0;
        return result;
    }

    private double ApparentSolarElevationSin(double altitude, double solarSin)
    {
        if (solarSin > 0.0) return solarSin;
        double elevation = System.Math.Asin(System.Math.Clamp(solarSin, -1.0, 1.0));
        double refracted = elevation + _optics.HorizonRefractionRadians(altitude, PlanetRadius);
        return refracted > 0.0 ? System.Math.Sin(refracted) : 0.0;
    }

    private double RayDistanceToTop(double altitude, double radialCosine)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(radialCosine)
            || altitude < 0.0 || altitude >= AtmosphereTopAltitude)
            return 0.0;
        double observerRadius = PlanetRadius + altitude;
        double outerRadius = PlanetRadius + AtmosphereTopAltitude;
        double b = observerRadius * radialCosine;
        double discriminant = b * b + outerRadius * outerRadius
            - observerRadius * observerRadius;
        if (discriminant <= 0.0) return 0.0;
        double far = -b + System.Math.Sqrt(discriminant);
        if (far <= 0.0) return 0.0;

        // A ray through the solid body is a planetary shadow, not a long scattering path.
        double surfaceDiscriminant = b * b + PlanetRadius * PlanetRadius
            - observerRadius * observerRadius;
        if (surfaceDiscriminant >= 0.0)
        {
            double nearSurface = -b - System.Math.Sqrt(surfaceDiscriminant);
            double farSurface = -b + System.Math.Sqrt(surfaceDiscriminant);
            if (farSurface > 1e-6 && nearSurface < farSurface) return 0.0;
        }
        return far;
    }

    private double RayRadius(double altitude, double radialCosine, double distance)
    {
        double observerRadius = PlanetRadius + altitude;
        return System.Math.Sqrt(observerRadius * observerRadius
            + distance * distance + 2.0 * observerRadius * radialCosine * distance);
    }

    private static double SuccessiveOrderRatio(double scattering, double extinction, double step)
    {
        if (scattering <= 0.0 || extinction <= 0.0 || !double.IsFinite(step)) return 0.0;
        double albedo = System.Math.Clamp(scattering / extinction, 0.0, 1.0);
        double opticalStep = 1.0 - System.Math.Exp(-extinction * System.Math.Max(step, 0.0));
        return System.Math.Clamp(albedo * opticalStep * 2.0, 0.0, 0.65);
    }

    private static SpectralBand[] BuildBands(AtmosphereOptics optics)
    {
        return Centers.Select(wavelength => new SpectralBand(
            wavelength,
            ReconstructAt(optics.RayleighScattering, wavelength),
            ReconstructAt(optics.MieScattering, wavelength),
            ReconstructAt(optics.MieAbsorption, wavelength),
            ReconstructAt(optics.OzoneAbsorption, wavelength))).ToArray();
    }

    /// <summary>
    /// Log-linear interpolation through the RGB anchor wavelengths. The renderer stores
    /// RGB as R=680 nm, G=550 nm, B=440 nm. Zero channels remain zero; positive channels
    /// are extrapolated with the nearest segment and clamped to finite non-negative values.
    /// </summary>
    public static double ReconstructAt(Vector3d rgb, double wavelengthNm)
    {
        if (!double.IsFinite(wavelengthNm)) return 0.0;
        double blue = SafePositive(rgb.Z);
        double green = SafePositive(rgb.Y);
        double red = SafePositive(rgb.X);
        if (blue == 0.0 && green == 0.0 && red == 0.0) return 0.0;

        const double blueNm = 440.0;
        const double greenNm = 550.0;
        const double redNm = 680.0;
        if (wavelengthNm <= blueNm)
            return LogInterpolate(blue, green, wavelengthNm, blueNm, greenNm);
        if (wavelengthNm <= greenNm)
            return LogInterpolate(blue, green, wavelengthNm, blueNm, greenNm);
        return LogInterpolate(green, red, wavelengthNm, greenNm, redNm);
    }

    private static double LogInterpolate(
        double a, double b, double wavelength, double wavelengthA, double wavelengthB)
    {
        // A zero anchor means no measured channel information exists there. A linear
        // interpolation in coefficient space is the least surprising deterministic fallback.
        if (a <= 0.0 || b <= 0.0)
        {
            double t = System.Math.Clamp((wavelength - wavelengthA)
                / (wavelengthB - wavelengthA), 0.0, 1.0);
            return a + (b - a) * t;
        }
        double tLog = (wavelength - wavelengthA) / (wavelengthB - wavelengthA);
        double value = System.Math.Exp(System.Math.Log(a)
            + (System.Math.Log(b) - System.Math.Log(a)) * tLog);
        return double.IsFinite(value) && value >= 0.0 ? value : 0.0;
    }

    private static double SafePositive(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private SpectralRadiance ZeroRadiance() => new(new double[BandCount]);

    private static double[] CopyCumulativeAt(
        double[] orderEnergy, double[] cumulative, int order)
    {
        // The evaluation's order arrays are scalars per order. Keep the compatibility
        // snapshots as the current cumulative spectrum; callers should use CumulativeAt
        // only for the canonical energy diagnostic.
        return cumulative.ToArray();
    }

    private static void ValidateBuildArguments(int maxOrder, int sampleCount, double planetRadius)
    {
        if (maxOrder < 2 || maxOrder > ExperimentalOrder)
            throw new ArgumentOutOfRangeException(nameof(maxOrder),
                "The spectral oracle supports scattering orders 2 through 5.");
        if (sampleCount < 8) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadius));
    }

    private sealed class EvaluationResult
    {
        private readonly double[] _cumulative;
        private readonly double[] _orderEnergy;

        public EvaluationResult(
            SpectralRadiance radiance,
            double[] cumulative,
            double[] orderEnergy,
            double[] order2,
            double[] order3,
            double[] order4)
        {
            Radiance = radiance;
            _cumulative = cumulative;
            _orderEnergy = orderEnergy;
        }

        public SpectralRadiance Radiance { get; }

        public double OrderEnergy(int order) =>
            order >= 0 && order < _orderEnergy.Length ?
                _orderEnergy[order] * SpectralColorConverter.BandWidth : 0.0;

        public double[] CumulativeAt(int order) => _cumulative.ToArray();
    }
}
