namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// A compact, dimensionless transmittance lookup table for direct solar light.
///
/// The table is sampled in two warped coordinates: altitude uses u² so the dense
/// lower atmosphere receives more resolution, and solar elevation uses v² so the
/// grazing horizon (where optical depth changes fastest) is resolved without a
/// large texture.  Values are linear RGB transmittance, never display colours.
/// </summary>
public sealed class AtmosphereTransmittanceLut
{
    public const double CoordinateExponent = 2.0;

    private readonly Vector3d[] _values;

    public int Width { get; }
    public int Height { get; }
    public double PlanetRadius { get; }
    public double AtmosphereTopAltitude { get; }

    private AtmosphereTransmittanceLut(
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
    /// Builds a deterministic LUT from the same spherical optical-depth oracle used by
    /// the tests and exposure controller.  It is intentionally CPU-side and built once
    /// per body; the shader then replaces dozens of noisy solar-ray integrations with a
    /// single filtered lookup per view sample.
    /// </summary>
    public static AtmosphereTransmittanceLut Build(
        AtmosphereOptics optics,
        double planetRadius,
        double atmosphereTopAltitude,
        int width = 128,
        int height = 96,
        int sampleCount = 48)
    {
        ArgumentNullException.ThrowIfNull(optics);
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(planetRadius) || planetRadius <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadius));
        if (!double.IsFinite(atmosphereTopAltitude) || atmosphereTopAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(atmosphereTopAltitude));

        var values = new Vector3d[width * height];
        for (int y = 0; y < height; y++)
        {
            double solarSin = CoordinateValue(y, height);
            for (int x = 0; x < width; x++)
            {
                double altitude = atmosphereTopAltitude * CoordinateValue(x, width);
                values[y * width + x] = optics.DirectSolarTransmittance(
                    altitude,
                    solarSin,
                    planetRadius,
                    atmosphereTopAltitude,
                    sampleCount);
            }
        }

        return new AtmosphereTransmittanceLut(
            width, height, planetRadius, atmosphereTopAltitude, values);
    }

    /// <summary>Returns the exact, unfiltered texel in row-major order.</summary>
    public Vector3d GetTexel(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return _values[y * Width + x];
    }

    /// <summary>
    /// Bilinearly samples the table in physical altitude and sine solar elevation.
    /// Clamping keeps a camera just above the configured atmospheric edge stable.
    /// </summary>
    public Vector3d Sample(double altitude, double solarElevationSin)
    {
        if (!double.IsFinite(altitude) || !double.IsFinite(solarElevationSin)
            || solarElevationSin <= 0.0)
            return Vector3d.Zero;

        double u = System.Math.Sqrt(System.Math.Clamp(altitude, 0.0,
            AtmosphereTopAltitude) / AtmosphereTopAltitude);
        double v = System.Math.Sqrt(System.Math.Clamp(solarElevationSin, 0.0, 1.0));
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

    /// <summary>Maps a texel index to its physical warped coordinate.</summary>
    public static double CoordinateValue(int index, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        double normalized = System.Math.Clamp((double)index / (size - 1), 0.0, 1.0);
        return System.Math.Pow(normalized, CoordinateExponent);
    }
}
