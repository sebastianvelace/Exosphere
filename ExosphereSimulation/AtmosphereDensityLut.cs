namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// A one-dimensional, physically normalised atmospheric density lookup.
///
/// The three channels are, in order, molecular/Rayleigh number density, aerosol/Mie
/// density and the ozone-layer profile.  Rayleigh density is derived from the atmosphere
/// model's thermodynamic state (<c>P/T</c>) rather than from a renderer-only exponential;
/// the mass-density fallback keeps the residual thermosphere continuous because
/// <see cref="AtmosphereModel.GetPressure"/> intentionally stops at <see
/// cref="AtmosphereModel.MaxAltitude"/>.  Mie density is the optical aerosol envelope
/// limited by the available mass density, and ozone retains the normalised profile from
/// <see cref="AtmosphereOptics.OzoneDensity"/>.
///
/// Altitude is warped with a square-root coordinate so the dense lower atmosphere receives
/// more texels while the residual thermosphere remains represented through the configured
/// thermosphere top.  Values are linear, dimensionless ratios relative to sea level.
/// </summary>
public sealed class AtmosphereDensityLut
{
    /// <summary>Altitude warp used by both building and sampling the table.</summary>
    public const double CoordinateExponent = 2.0;

    private readonly Vector3d[] _values;

    /// <summary>Number of packed altitude texels.</summary>
    public int Width { get; }

    /// <summary>The table is one-dimensional but is exposed as a one-row texture.</summary>
    public int Height => 1;

    /// <summary>Atmospheric upper boundary represented by this table (m).</summary>
    public double AtmosphereTopAltitude { get; }

    /// <summary>The source model used to generate the table.</summary>
    public AtmosphereModel Atmosphere { get; }

    /// <summary>
    /// Exact CPU density provider used to populate this table. Keeping it with the LUT
    /// lets optical transport and rendering share one thermodynamic profile.
    /// </summary>
    public AtmosphereDensityProfile Profile { get; }

    private AtmosphereDensityLut(
        AtmosphereDensityProfile profile,
        int width,
        double atmosphereTopAltitude,
        Vector3d[] values)
    {
        Profile = profile;
        Atmosphere = profile.Atmosphere;
        Width = width;
        AtmosphereTopAltitude = atmosphereTopAltitude;
        _values = values;
    }

    /// <summary>
    /// Builds a normalised density table from the model's pressure, temperature and mass
    /// density together with its optical species profiles.
    /// </summary>
    public static AtmosphereDensityLut Build(
        AtmosphereModel atmosphere,
        int width = 256)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        if (width < 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(atmosphere.MaxAltitude) || atmosphere.MaxAltitude <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(atmosphere),
                "The atmosphere must have a positive MaxAltitude.");

        var profile = AtmosphereDensityProfile.Create(atmosphere);
        double top = profile.AtmosphereTopAltitude;
        var values = new Vector3d[width];

        for (int i = 0; i < width; i++)
        {
            double altitude = top * CoordinateValue(i, width);
            values[i] = profile.Sample(altitude);
        }

        return new AtmosphereDensityLut(profile, width, top, values);
    }

    /// <summary>Returns the exact unfiltered texel at an altitude index.</summary>
    public Vector3d GetTexel(int index)
    {
        if ((uint)index >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(index));
        return _values[index];
    }

    /// <summary>
    /// Returns a texel using the conventional two-dimensional texture signature.  This LUT
    /// has one row, so any row other than zero is rejected rather than silently aliasing data.
    /// </summary>
    public Vector3d GetTexel(int x, int y)
    {
        if (y != 0) throw new ArgumentOutOfRangeException(nameof(y));
        return GetTexel(x);
    }

    /// <summary>
    /// Samples normalised Rayleigh, Mie and ozone density at a physical altitude in metres.
    /// Outside the represented atmosphere the result is vacuum, not a clamped copy of the
    /// top texel; this matters when a camera crosses into orbital space.
    /// </summary>
    public Vector3d Sample(double altitude)
    {
        if (!double.IsFinite(altitude) || altitude < 0.0
            || altitude > AtmosphereTopAltitude)
            return Vector3d.Zero;

        double coordinate = System.Math.Pow(
            System.Math.Clamp(altitude / AtmosphereTopAltitude, 0.0, 1.0),
            1.0 / CoordinateExponent) * (Width - 1);
        int x0 = System.Math.Clamp((int)System.Math.Floor(coordinate), 0, Width - 1);
        int x1 = System.Math.Min(x0 + 1, Width - 1);
        double t = coordinate - x0;
        return GetTexel(x0).Lerp(GetTexel(x1), t);
    }

    /// <summary>Maps an altitude index to its warped physical altitude fraction.</summary>
    public static double CoordinateValue(int index, int size)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        double normalized = System.Math.Clamp((double)index / (size - 1), 0.0, 1.0);
        return System.Math.Pow(normalized, CoordinateExponent);
    }

}
