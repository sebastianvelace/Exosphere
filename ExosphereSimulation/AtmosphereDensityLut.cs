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

    private AtmosphereDensityLut(
        AtmosphereModel atmosphere,
        int width,
        double atmosphereTopAltitude,
        Vector3d[] values)
    {
        Atmosphere = atmosphere;
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

        double top = ResolveTopAltitude(atmosphere);
        var values = new Vector3d[width];

        // P/T is the best available number-density proxy.  GetDensity is retained as a
        // deliberate fallback for the thermosphere, where pressure is aerodynamically
        // defined as zero even though a residual density is present.
        double seaPressure = SafeNonNegative(atmosphere.GetPressure(0.0));
        double seaTemperature = SafePositive(atmosphere.GetTemperature(0.0));
        double seaMassDensity = SafeNonNegative(atmosphere.GetDensity(0.0));
        double seaNumberProxy = seaPressure > 0.0 && seaTemperature > 0.0
            ? seaPressure / seaTemperature
            : 0.0;

        for (int i = 0; i < width; i++)
        {
            double altitude = top * CoordinateValue(i, width);
            values[i] = Evaluate(
                atmosphere, atmosphere.Optics, altitude,
                seaPressure, seaTemperature, seaMassDensity, seaNumberProxy);
        }

        return new AtmosphereDensityLut(atmosphere, width, top, values);
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

    private static double ResolveTopAltitude(AtmosphereModel atmosphere)
    {
        // ThermosphereTopAltitude is only meaningful when the residual tail is enabled.
        // A zero/disabled tail must not create a large table full of vacuum samples.
        if (atmosphere.ThermosphereScaleHeight > 0.0
            && double.IsFinite(atmosphere.ThermosphereTopAltitude)
            && atmosphere.ThermosphereTopAltitude > atmosphere.MaxAltitude)
            return atmosphere.ThermosphereTopAltitude;
        return atmosphere.MaxAltitude;
    }

    private static Vector3d Evaluate(
        AtmosphereModel atmosphere,
        AtmosphereOptics optics,
        double altitude,
        double seaPressure,
        double seaTemperature,
        double seaMassDensity,
        double seaNumberProxy)
    {
        double massDensity = SafeNonNegative(atmosphere.GetDensity(altitude));
        double massRatio = seaMassDensity > 0.0
            ? ClampUnit(massDensity / seaMassDensity)
            : 0.0;

        double pressure = SafeNonNegative(atmosphere.GetPressure(altitude));
        double temperature = SafePositive(atmosphere.GetTemperature(altitude));
        double numberRatio = seaNumberProxy > 0.0 && pressure > 0.0 && temperature > 0.0
            ? ClampUnit((pressure / temperature) / seaNumberProxy)
            : massRatio;

        // The Rayleigh channel follows molecular number density.  The fallback is what
        // preserves continuity across MaxAltitude, where pressure intentionally becomes 0
        // while GetDensity still exposes the residual thermosphere.
        double rayleigh = numberRatio;

        // Aerosols cannot outlive the bulk atmosphere.  The optical Mie scale height gives
        // the realistic near-surface aerosol falloff; massRatio prevents it reappearing in
        // a custom model whose pressure/density boundary has already become vacuum.
        double mieEnvelope = SafeUnit(optics.MieDensity(altitude));
        double mie = System.Math.Min(mieEnvelope, massRatio);

        // OzoneDensity is already a normalised species profile.  Gate it at a true vacuum
        // boundary without scaling its peak by the much smaller bulk number density.
        double ozone = massDensity > 0.0 ? SafeUnit(optics.OzoneDensity(altitude)) : 0.0;
        return new Vector3d(rayleigh, mie, ozone);
    }

    private static double SafeNonNegative(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static double SafePositive(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? System.Math.Clamp(value, 0.0, 1.0) : 0.0;

    private static double SafeUnit(double value) => ClampUnit(value);
}
