namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Compact four-dimensional angular envelope for the global multiple-scattering field.
///
/// The atlas axes are observer altitude, geometric solar elevation, view zenith cosine and
/// view/sun cosine (mu).  It is packed as a 2D texture by the Godot layer, but retaining all
/// four physical coordinates removes the old isotropic ``0.55 + 0.45 * view_up`` closure.
/// The view axis is endpoint-warped toward the horizon/zenith and the mu axis is warped toward
/// +1, where spherical escape and the forward Mie lobe vary fastest.
/// The radiance seed comes from <see cref="AtmosphereMultipleScatteringLut"/> (orders two and
/// three); the angular transport applies the normalized Rayleigh/Mie phase and a spherical
/// escape ratio relative to the vertical column.  This is a deterministic low-resolution
/// approximation of the angular part of a Bruneton/Neyret LUT, not a display-space tint.
/// </summary>
public sealed class AtmosphereAngularMultipleScatteringLut
{
    private readonly Vector3d[] _values;

    public int Width { get; }
    public int SolarHeight { get; }
    public int ViewHeight { get; }
    public int MuWidth { get; }
    public double PlanetRadius { get; }
    public double AtmosphereTopAltitude { get; }

    private AtmosphereAngularMultipleScatteringLut(
        int width,
        int solarHeight,
        int viewHeight,
        int muWidth,
        double planetRadius,
        double atmosphereTopAltitude,
        Vector3d[] values)
    {
        Width = width;
        SolarHeight = solarHeight;
        ViewHeight = viewHeight;
        MuWidth = muWidth;
        PlanetRadius = planetRadius;
        AtmosphereTopAltitude = atmosphereTopAltitude;
        _values = values;
    }

    public int PackedHeight => SolarHeight * ViewHeight * MuWidth;

    /// <summary>
    /// Builds a four-coordinate angular atlas from the global multiple-scattering seed.
    /// Optical depth is evaluated only once for each altitude/view pair; solar and mu axes
    /// then reuse that transport while changing the phase function.  This keeps startup
    /// bounded while preserving the physically important horizon and forward-scatter trends.
    /// </summary>
    public static AtmosphereAngularMultipleScatteringLut Build(
        AtmosphereOptics optics,
        AtmosphereMultipleScatteringLut globalSeed,
        double planetRadius,
        double atmosphereTopAltitude,
        int width = 32,
        int solarHeight = 20,
        int viewHeight = 12,
        int muWidth = 12,
        int opticalDepthSamples = 32)
    {
        ArgumentNullException.ThrowIfNull(optics);
        ArgumentNullException.ThrowIfNull(globalSeed);
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (solarHeight < 2) throw new ArgumentOutOfRangeException(nameof(solarHeight));
        if (viewHeight < 2) throw new ArgumentOutOfRangeException(nameof(viewHeight));
        if (muWidth < 2) throw new ArgumentOutOfRangeException(nameof(muWidth));
        if (opticalDepthSamples < 8) throw new ArgumentOutOfRangeException(nameof(opticalDepthSamples));
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadius));
        if (!double.IsFinite(atmosphereTopAltitude) || atmosphereTopAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(atmosphereTopAltitude));

        var values = new Vector3d[width * solarHeight * viewHeight * muWidth];
        for (int x = 0; x < width; x++)
        {
            double altitude = atmosphereTopAltitude
                * AtmosphereTransmittanceLut.CoordinateValue(x, width);
            var verticalTau = optics.VerticalOpticalDepth(altitude);
            var rayleighBeta = optics.RayleighScattering * optics.RayleighDensity(altitude);
            var mieBeta = optics.MieScattering * optics.MieDensity(altitude);

            var viewEscape = new Vector3d[viewHeight];
            for (int viewIndex = 0; viewIndex < viewHeight; viewIndex++)
            {
                double viewCos = ViewCosine(viewIndex, viewHeight);
                viewEscape[viewIndex] = ViewEscapeRatio(
                    optics, verticalTau, altitude, viewCos,
                    planetRadius, atmosphereTopAltitude, opticalDepthSamples);
            }

            for (int muIndex = 0; muIndex < muWidth; muIndex++)
            {
                double mu = MuCosine(muIndex, muWidth);
                var phase = PhaseGain(optics, rayleighBeta, mieBeta, mu);
                for (int viewIndex = 0; viewIndex < viewHeight; viewIndex++)
                {
                    var directional = Multiply(phase, viewEscape[viewIndex]);
                    for (int solarIndex = 0; solarIndex < solarHeight; solarIndex++)
                    {
                        double solarSin = AtmosphereTransmittanceLut.SolarElevationSin(
                            solarIndex, solarHeight);
                        var seed = globalSeed.Sample(altitude, solarSin);
                        int offset = Index(x, solarIndex, viewIndex, muIndex,
                            width, solarHeight, viewHeight);
                        values[offset] = Multiply(seed, directional);
                    }
                }
            }
        }

        return new AtmosphereAngularMultipleScatteringLut(
            width, solarHeight, viewHeight, muWidth,
            planetRadius, atmosphereTopAltitude, values);
    }

    /// <summary>
    /// Profile-aware angular atlas.  The phase coefficients remain the configured optical
    /// species, while local beta, vertical tau and view escape all use the same P/T and
    /// residual-thermosphere samples as the global seed.
    /// </summary>
    public static AtmosphereAngularMultipleScatteringLut Build(
        AtmosphereDensityProfile profile,
        AtmosphereMultipleScatteringLut globalSeed,
        double planetRadius,
        double atmosphereTopAltitude,
        int width = 32,
        int solarHeight = 20,
        int viewHeight = 12,
        int muWidth = 12,
        int opticalDepthSamples = 32)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(globalSeed);
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (solarHeight < 2) throw new ArgumentOutOfRangeException(nameof(solarHeight));
        if (viewHeight < 2) throw new ArgumentOutOfRangeException(nameof(viewHeight));
        if (muWidth < 2) throw new ArgumentOutOfRangeException(nameof(muWidth));
        if (opticalDepthSamples < 8) throw new ArgumentOutOfRangeException(nameof(opticalDepthSamples));
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadius));
        if (!double.IsFinite(atmosphereTopAltitude) || atmosphereTopAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(atmosphereTopAltitude));

        var optics = profile.Optics;
        var values = new Vector3d[width * solarHeight * viewHeight * muWidth];
        for (int x = 0; x < width; x++)
        {
            double altitude = atmosphereTopAltitude
                * AtmosphereTransmittanceLut.CoordinateValue(x, width);
            var verticalTau = profile.VerticalOpticalDepth(altitude);
            var density = profile.Sample(altitude);
            var rayleighBeta = optics.RayleighScattering * density.X;
            var mieBeta = optics.MieScattering * density.Y;

            var viewEscape = new Vector3d[viewHeight];
            for (int viewIndex = 0; viewIndex < viewHeight; viewIndex++)
            {
                double viewCos = ViewCosine(viewIndex, viewHeight);
                viewEscape[viewIndex] = ViewEscapeRatio(
                    profile, verticalTau, altitude, viewCos,
                    planetRadius, atmosphereTopAltitude, opticalDepthSamples);
            }

            for (int muIndex = 0; muIndex < muWidth; muIndex++)
            {
                double mu = MuCosine(muIndex, muWidth);
                var phase = PhaseGain(optics, rayleighBeta, mieBeta, mu);
                for (int viewIndex = 0; viewIndex < viewHeight; viewIndex++)
                {
                    var directional = Multiply(phase, viewEscape[viewIndex]);
                    for (int solarIndex = 0; solarIndex < solarHeight; solarIndex++)
                    {
                        double solarSin = AtmosphereTransmittanceLut.SolarElevationSin(
                            solarIndex, solarHeight);
                        var seed = globalSeed.Sample(altitude, solarSin);
                        int offset = Index(x, solarIndex, viewIndex, muIndex,
                            width, solarHeight, viewHeight);
                        values[offset] = Multiply(seed, directional);
                    }
                }
            }
        }

        return new AtmosphereAngularMultipleScatteringLut(
            width, solarHeight, viewHeight, muWidth,
            planetRadius, atmosphereTopAltitude, values);
    }

    public Vector3d GetTexel(int altitudeIndex, int solarIndex, int viewIndex, int muIndex)
    {
        if ((uint)altitudeIndex >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(altitudeIndex));
        if ((uint)solarIndex >= (uint)SolarHeight)
            throw new ArgumentOutOfRangeException(nameof(solarIndex));
        if ((uint)viewIndex >= (uint)ViewHeight)
            throw new ArgumentOutOfRangeException(nameof(viewIndex));
        if ((uint)muIndex >= (uint)MuWidth)
            throw new ArgumentOutOfRangeException(nameof(muIndex));
        return _values[Index(altitudeIndex, solarIndex, viewIndex, muIndex,
            Width, SolarHeight, ViewHeight)];
    }

    /// <summary>Samples the packed physical atlas with trilinear angular interpolation.</summary>
    public Vector3d Sample(
        double altitude,
        double solarElevationSin,
        double viewCosine,
        double viewSunCosine)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(solarElevationSin)
            || !double.IsFinite(viewCosine) || !double.IsFinite(viewSunCosine)
            || solarElevationSin < AtmosphereTransmittanceLut.MinimumSolarElevationSin
            || viewCosine <= 0.0)
            return Vector3d.Zero;

        double px = System.Math.Sqrt(System.Math.Clamp(altitude, 0.0,
            AtmosphereTopAltitude) / AtmosphereTopAltitude) * (Width - 1);
        double solarNormalized = (System.Math.Clamp(solarElevationSin,
            AtmosphereTransmittanceLut.MinimumSolarElevationSin, 1.0)
            - AtmosphereTransmittanceLut.MinimumSolarElevationSin)
            / (1.0 - AtmosphereTransmittanceLut.MinimumSolarElevationSin);
        double py = System.Math.Sqrt(System.Math.Clamp(solarNormalized, 0.0, 1.0))
            * (SolarHeight - 1);
        // The atlas is non-uniform: escape radiance bends sharply around the geometric
        // horizon, while the Mie phase has its narrowest lobe at mu=+1.  Invert the same
        // piecewise mappings used by ViewCosine/MuCosine so interpolation spends texels
        // where the physical signal has the most curvature.
        double pView = InverseViewCoordinate(viewCosine) * (ViewHeight - 1);
        double pMu = InverseMuCoordinate(viewSunCosine) * (MuWidth - 1);

        int x0 = ClampIndex(px, Width, out double tx);
        int y0 = ClampIndex(py, SolarHeight, out double ty);
        int z0 = ClampIndex(pView, ViewHeight, out double tz);
        int w0 = ClampIndex(pMu, MuWidth, out double tw);
        int x1 = System.Math.Min(x0 + 1, Width - 1);
        int y1 = System.Math.Min(y0 + 1, SolarHeight - 1);
        int z1 = System.Math.Min(z0 + 1, ViewHeight - 1);
        int w1 = System.Math.Min(w0 + 1, MuWidth - 1);

        Vector3d value = Vector3d.Zero;
        for (int wi = 0; wi < 2; wi++)
        {
            double fw = wi == 0 ? 1.0 - tw : tw;
            int w = wi == 0 ? w0 : w1;
            for (int zi = 0; zi < 2; zi++)
            {
                double fz = zi == 0 ? 1.0 - tz : tz;
                int z = zi == 0 ? z0 : z1;
                for (int yi = 0; yi < 2; yi++)
                {
                    double fy = yi == 0 ? 1.0 - ty : ty;
                    int y = yi == 0 ? y0 : y1;
                    var altitudeSlice = GetTexel(x0, y, z, w).Lerp(
                        GetTexel(x1, y, z, w), tx);
                    value += altitudeSlice * (fw * fz * fy);
                }
            }
        }

        return value;
    }

    public static double ViewCosine(int index, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        double u = System.Math.Clamp((double)index / (size - 1), 0.0, 1.0);
        double t = u <= 0.5
            ? 2.0 * u * u
            : 1.0 - 2.0 * (1.0 - u) * (1.0 - u);
        return -1.0 + 2.0 * t;
    }

    /// <summary>
    /// Forward-scatter coordinate warped toward +1, where a terrestrial Mie lobe is narrow.
    /// </summary>
    public static double MuCosine(int index, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        double u = System.Math.Clamp((double)index / (size - 1), 0.0, 1.0);
        double t = 1.0 - (1.0 - u) * (1.0 - u);
        return -1.0 + 2.0 * t;
    }

    private static double InverseViewCoordinate(double cosine)
    {
        double t = (System.Math.Clamp(cosine, -1.0, 1.0) + 1.0) * 0.5;
        return t <= 0.5
            ? System.Math.Sqrt(t * 0.5)
            : 1.0 - System.Math.Sqrt((1.0 - t) * 0.5);
    }

    private static double InverseMuCoordinate(double cosine)
    {
        double t = (System.Math.Clamp(cosine, -1.0, 1.0) + 1.0) * 0.5;
        return 1.0 - System.Math.Sqrt(1.0 - t);
    }

    private static Vector3d ViewEscapeRatio(
        AtmosphereOptics optics,
        Vector3d verticalTau,
        double altitude,
        double viewCosine,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount)
    {
        if (viewCosine <= 0.0) return Vector3d.Zero;
        var viewTau = optics.OpticalDepthAlongRay(
            altitude, viewCosine, planetRadius, atmosphereTopAltitude, sampleCount);
        return new Vector3d(
            System.Math.Exp(-System.Math.Max(viewTau.X - verticalTau.X, 0.0)),
            System.Math.Exp(-System.Math.Max(viewTau.Y - verticalTau.Y, 0.0)),
            System.Math.Exp(-System.Math.Max(viewTau.Z - verticalTau.Z, 0.0)));
    }

    private static Vector3d ViewEscapeRatio(
        AtmosphereDensityProfile profile,
        Vector3d verticalTau,
        double altitude,
        double viewCosine,
        double planetRadius,
        double atmosphereTopAltitude,
        int sampleCount)
    {
        if (viewCosine <= 0.0) return Vector3d.Zero;
        var viewTau = profile.Optics.OpticalDepthAlongRay(
            profile, altitude, viewCosine, planetRadius,
            atmosphereTopAltitude, sampleCount);
        return new Vector3d(
            System.Math.Exp(-System.Math.Max(viewTau.X - verticalTau.X, 0.0)),
            System.Math.Exp(-System.Math.Max(viewTau.Y - verticalTau.Y, 0.0)),
            System.Math.Exp(-System.Math.Max(viewTau.Z - verticalTau.Z, 0.0)));
    }

    private static Vector3d PhaseGain(
        AtmosphereOptics optics,
        Vector3d rayleighBeta,
        Vector3d mieBeta,
        double mu)
    {
        double rayleigh = 0.75 * (1.0 + mu * mu);
        double g = System.Math.Clamp(optics.MieAnisotropy, -0.95, 0.95);
        double mie = (1.0 - g * g)
            / System.Math.Pow(System.Math.Max(1.0 + g * g - 2.0 * g * mu, 1e-6), 1.5);
        return new Vector3d(
            BandPhase(rayleighBeta.X, mieBeta.X, rayleigh, mie),
            BandPhase(rayleighBeta.Y, mieBeta.Y, rayleigh, mie),
            BandPhase(rayleighBeta.Z, mieBeta.Z, rayleigh, mie));
    }

    private static double BandPhase(double rayleigh, double mie, double rayleighPhase, double miePhase)
    {
        double total = rayleigh + mie;
        return total > 1e-20
            ? (rayleigh * rayleighPhase + mie * miePhase) / total
            : 1.0;
    }

    private static Vector3d Multiply(Vector3d left, Vector3d right) => new(
        left.X * right.X, left.Y * right.Y, left.Z * right.Z);

    private static int Index(
        int altitudeIndex,
        int solarIndex,
        int viewIndex,
        int muIndex,
        int width,
        int solarHeight,
        int viewHeight) =>
        (((muIndex * viewHeight + viewIndex) * solarHeight + solarIndex) * width)
        + altitudeIndex;

    private static int ClampIndex(double coordinate, int size, out double fraction)
    {
        double clamped = System.Math.Clamp(coordinate, 0.0, size - 1.0);
        int index = System.Math.Clamp((int)System.Math.Floor(clamped), 0, size - 1);
        fraction = clamped - index;
        return index;
    }
}
