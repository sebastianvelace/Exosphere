namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Global, isotropic multiple-scattering atmospheric radiance lookup.
///
/// Each texel integrates the bounded local source through the complete vertical column
/// above an observer.  It is not the final Bruneton 4D solution (there is no view-angle
/// dimension yet), but unlike the old per-ray S₂ closure it transports photons through the
/// whole column and is shared by every sky direction.  A second deterministic pass adds one
/// additional isotropic scattering event (order three) without introducing a shader loop.
/// Values are linear radiance per unit <see cref="AtmosphereOptics.SunIlluminanceScale"/>.
/// </summary>
public sealed class AtmosphereMultipleScatteringLut
{
    private readonly Vector3d[] _values;

    public int Width { get; }
    public int Height { get; }
    public double PlanetRadius { get; }
    public double AtmosphereTopAltitude { get; }

    private AtmosphereMultipleScatteringLut(
        int width,
        int height,
        double planetRadius,
        double atmosphereTopAltitude,
        Vector3d[] values)
    {
        Width = width;
        Height = height;
        PlanetRadius = planetRadius;
        AtmosphereTopAltitude = atmosphereTopAltitude;
        _values = values;
    }

    /// <summary>
    /// Integrates orders two and three from the observer to space.  The view transmittance
    /// uses the exact vertical optical-depth difference and the solar term uses the spherical
    /// direct-transmittance oracle, so changing the planet radius changes both transport legs.
    /// </summary>
    public static AtmosphereMultipleScatteringLut Build(
        AtmosphereOptics optics,
        double planetRadius,
        double atmosphereTopAltitude,
        int width = 64,
        int height = 48,
        int integrationSteps = 48,
        int solarSampleCount = 32)
    {
        ArgumentNullException.ThrowIfNull(optics);
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (integrationSteps < 4) throw new ArgumentOutOfRangeException(nameof(integrationSteps));
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadius));
        if (!double.IsFinite(atmosphereTopAltitude) || atmosphereTopAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(atmosphereTopAltitude));

        var values = new Vector3d[width * height];
        for (int y = 0; y < height; y++)
        {
            double solarSin = AtmosphereTransmittanceLut.SolarElevationSin(y, height);
            for (int x = 0; x < width; x++)
            {
                double observerAltitude = atmosphereTopAltitude
                    * AtmosphereTransmittanceLut.CoordinateValue(x, width);
                values[y * width + x] = IntegrateColumn(
                    optics,
                    observerAltitude,
                    solarSin,
                    planetRadius,
                    atmosphereTopAltitude,
                    integrationSteps,
                    solarSampleCount);
            }
        }

        return new AtmosphereMultipleScatteringLut(
            width, height, planetRadius, atmosphereTopAltitude, values);
    }

    public Vector3d GetTexel(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return _values[y * Width + x];
    }

    /// <summary>Samples global order-two radiance in physical altitude and solar elevation.</summary>
    public Vector3d Sample(double altitude, double solarElevationSin)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(solarElevationSin)
            || solarElevationSin < AtmosphereTransmittanceLut.MinimumSolarElevationSin)
            return Vector3d.Zero;

        double u = System.Math.Sqrt(System.Math.Clamp(altitude, 0.0,
            AtmosphereTopAltitude) / AtmosphereTopAltitude);
        double solarNormalized = (System.Math.Clamp(solarElevationSin,
            AtmosphereTransmittanceLut.MinimumSolarElevationSin, 1.0)
            - AtmosphereTransmittanceLut.MinimumSolarElevationSin)
            / (1.0 - AtmosphereTransmittanceLut.MinimumSolarElevationSin);
        double v = System.Math.Sqrt(System.Math.Clamp(solarNormalized, 0.0, 1.0));
        double px = u * (Width - 1);
        double py = v * (Height - 1);
        int x0 = System.Math.Clamp((int)System.Math.Floor(px), 0, Width - 1);
        int y0 = System.Math.Clamp((int)System.Math.Floor(py), 0, Height - 1);
        int x1 = System.Math.Min(x0 + 1, Width - 1);
        int y1 = System.Math.Min(y0 + 1, Height - 1);
        double tx = px - x0;
        double ty = py - y0;
        var a = GetTexel(x0, y0).Lerp(GetTexel(x1, y0), tx);
        var b = GetTexel(x0, y1).Lerp(GetTexel(x1, y1), tx);
        return a.Lerp(b, ty);
    }

    private static Vector3d IntegrateColumn(
        AtmosphereOptics optics,
        double observerAltitude,
        double solarSin,
        double planetRadius,
        double atmosphereTopAltitude,
        int integrationSteps,
        int solarSampleCount)
    {
        if (solarSin < AtmosphereTransmittanceLut.MinimumSolarElevationSin
            || observerAltitude >= atmosphereTopAltitude)
            return Vector3d.Zero;

        double span = atmosphereTopAltitude - observerAltitude;
        double step = span / integrationSteps;
        Vector3d observerTau = optics.VerticalOpticalDepth(observerAltitude);
        var sources = new Vector3d[integrationSteps];
        var scattering = new Vector3d[integrationSteps];
        var absoluteTau = new Vector3d[integrationSteps];
        var viewThroughput = new Vector3d[integrationSteps];
        for (int i = 0; i < integrationSteps; i++)
        {
            double altitude = observerAltitude + (i + 0.5) * step;
            var density = new Vector3d(
                optics.RayleighDensity(altitude),
                optics.MieDensity(altitude),
                optics.OzoneDensity(altitude));
            var solar = optics.DirectSolarTransmittance(
                altitude,
                solarSin,
                planetRadius,
                atmosphereTopAltitude,
                solarSampleCount);
            sources[i] = optics.LowOrderDiffuseSource(density, solar);
            scattering[i] = optics.RayleighScattering * density.X
                + optics.MieScattering * density.Y;
            absoluteTau[i] = optics.VerticalOpticalDepth(altitude);
            var layerTau = observerTau - absoluteTau[i];
            viewThroughput[i] = new Vector3d(
                System.Math.Exp(-System.Math.Max(layerTau.X, 0.0)),
                System.Math.Exp(-System.Math.Max(layerTau.Y, 0.0)),
                System.Math.Exp(-System.Math.Max(layerTau.Z, 0.0)));
        }

        // Order two: direct sunlight scattered once more and transported from each
        // layer to the observer. The source is already bounded by the per-band
        // single-scattering albedo in LowOrderDiffuseSource.
        Vector3d orderTwo = Vector3d.Zero;
        for (int i = 0; i < integrationSteps; i++)
            orderTwo += Multiply(sources[i], viewThroughput[i]) * step;

        // Order three: transport the isotropic order-two field at each layer and
        // scatter it once again. The nested column integral is intentional: this
        // table is built once per body, while the explicit path remains deterministic
        // and keeps planet-radius dependence visible to tests.
        Vector3d orderThree = Vector3d.Zero;
        for (int i = 0; i < integrationSteps; i++)
        {
            Vector3d orderTwoAtLayer = Vector3d.Zero;
            for (int j = i; j < integrationSteps; j++)
            {
                var tau = absoluteTau[i] - absoluteTau[j];
                var transport = new Vector3d(
                    System.Math.Exp(-System.Math.Max(tau.X, 0.0)),
                    System.Math.Exp(-System.Math.Max(tau.Y, 0.0)),
                    System.Math.Exp(-System.Math.Max(tau.Z, 0.0)));
                orderTwoAtLayer += Multiply(sources[j], transport) * step;
            }

            var thirdSource = Multiply(scattering[i], orderTwoAtLayer);
            orderThree += Multiply(thirdSource, viewThroughput[i]) * step;
        }

        return orderTwo + orderThree;
    }

    private static Vector3d Multiply(Vector3d left, Vector3d right) => new(
        left.X * right.X, left.Y * right.Y, left.Z * right.Z);
}
