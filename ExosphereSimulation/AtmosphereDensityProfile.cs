namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Immutable view of the atmospheric species densities shared by optical transport.
///
/// The values are dimensionless ratios to the sea-level state and are ordered as
/// molecular/Rayleigh number density, aerosol/Mie density and ozone density.  The
/// molecular channel comes from the thermodynamic atmosphere model (<c>P/T</c>), with
/// the mass-density ratio retained above <see cref="AtmosphereModel.MaxAltitude"/>
/// where the model deliberately exposes a residual thermosphere while pressure is zero.
/// </summary>
/// <remarks>
/// This type is intentionally independent of the GPU lookup table.  CPU transmittance and
/// multiple-scattering oracles can use the exact profile while the renderer can sample a
/// filtered <see cref="AtmosphereDensityLut"/> generated from the same profile.  The source
/// model should be treated as immutable after construction; this class exposes no mutators
/// and all normalisation constants are captured at construction time.
/// </remarks>
public sealed class AtmosphereDensityProfile
{
    private readonly double _seaPressure;
    private readonly double _seaTemperature;
    private readonly double _seaMassDensity;
    private readonly double _seaNumberProxy;

    /// <summary>Thermodynamic atmosphere used to evaluate this profile.</summary>
    public AtmosphereModel Atmosphere { get; }

    /// <summary>Optical species and scattering parameters associated with the source.</summary>
    public AtmosphereOptics Optics { get; }

    /// <summary>Upper altitude represented by the profile in metres.</summary>
    public double AtmosphereTopAltitude { get; }

    /// <summary>Alias used by transport callers that refer to the profile domain as a top.</summary>
    public double TopAltitude => AtmosphereTopAltitude;

    /// <summary>
    /// Creates a profile for an atmospheric model.  A configured thermosphere tail determines
    /// the domain; otherwise the aerodynamic <see cref="AtmosphereModel.MaxAltitude"/> is used.
    /// </summary>
    public AtmosphereDensityProfile(AtmosphereModel atmosphere)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        if (!double.IsFinite(atmosphere.MaxAltitude) || atmosphere.MaxAltitude <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(atmosphere),
                "The atmosphere must have a positive MaxAltitude.");
        }

        Atmosphere = atmosphere;
        Optics = atmosphere.Optics ?? throw new ArgumentException(
            "The atmosphere must provide optical parameters.", nameof(atmosphere));
        AtmosphereTopAltitude = ResolveTopAltitude(atmosphere);

        // Capture the sea-level normalisation once.  This makes every transport consumer
        // of a profile use identical reference values instead of recomputing them with a
        // subtly different fallback at every ray sample.
        _seaPressure = SafeNonNegative(atmosphere.GetPressure(0.0));
        _seaTemperature = SafePositive(atmosphere.GetTemperature(0.0));
        _seaMassDensity = SafeNonNegative(atmosphere.GetDensity(0.0));
        _seaNumberProxy = _seaPressure > 0.0 && _seaTemperature > 0.0
            ? _seaPressure / _seaTemperature
            : 0.0;
    }

    /// <summary>Factory form for call sites that prefer explicit construction semantics.</summary>
    public static AtmosphereDensityProfile Create(AtmosphereModel atmosphere) =>
        new(atmosphere);

    /// <summary>
    /// Samples the exact thermodynamic species profile at a physical altitude in metres.
    /// Outside the represented domain the result is vacuum, never a clamped top sample.
    /// </summary>
    public Vector3d Sample(double altitude)
    {
        if (!double.IsFinite(altitude) || altitude < 0.0
            || altitude > AtmosphereTopAltitude)
            return Vector3d.Zero;

        double massDensity = SafeNonNegative(Atmosphere.GetDensity(altitude));
        double massRatio = _seaMassDensity > 0.0
            ? ClampUnit(massDensity / _seaMassDensity)
            : 0.0;

        double pressure = SafeNonNegative(Atmosphere.GetPressure(altitude));
        double temperature = SafePositive(Atmosphere.GetTemperature(altitude));
        double numberRatio = _seaNumberProxy > 0.0 && pressure > 0.0 && temperature > 0.0
            ? ClampUnit((pressure / temperature) / _seaNumberProxy)
            : massRatio;

        // Rayleigh follows molecular number density.  The mass fallback preserves the
        // residual thermosphere at/above MaxAltitude where pressure is intentionally zero.
        double rayleigh = numberRatio;

        // Aerosols cannot exceed the available bulk atmosphere.  The configured Mie
        // envelope controls the near-surface falloff while massRatio gates true vacuum.
        double mieEnvelope = SafeUnit(Optics.MieDensity(altitude));
        double mie = System.Math.Min(mieEnvelope, massRatio);

        // Ozone is already a normalised species profile.  Do not scale its peak by the
        // much smaller bulk density; only remove it once the atmosphere is true vacuum.
        double ozone = massDensity > 0.0 ? SafeUnit(Optics.OzoneDensity(altitude)) : 0.0;
        return new Vector3d(rayleigh, mie, ozone);
    }

    /// <summary>
    /// Returns the dimensionless refractivity ratio at altitude.  Multiplying this by
    /// <see cref="AtmosphereOptics.SurfaceRefractivity"/> yields <c>n − 1</c>.
    /// </summary>
    public double Refractivity(double altitude)
    {
        double surface = SafeNonNegative(Optics.SurfaceRefractivity);
        return surface * Sample(altitude).X;
    }

    private static double ResolveTopAltitude(AtmosphereModel atmosphere)
    {
        // A disabled/invalid thermosphere tail must not create a large table of vacuum.
        if (atmosphere.ThermosphereScaleHeight > 0.0
            && double.IsFinite(atmosphere.ThermosphereTopAltitude)
            && atmosphere.ThermosphereTopAltitude > atmosphere.MaxAltitude)
            return atmosphere.ThermosphereTopAltitude;
        return atmosphere.MaxAltitude;
    }

    private static double SafeNonNegative(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static double SafePositive(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static double ClampUnit(double value) =>
        double.IsFinite(value) ? System.Math.Clamp(value, 0.0, 1.0) : 0.0;

    private static double SafeUnit(double value) => ClampUnit(value);
}
