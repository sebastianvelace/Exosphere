namespace Exosphere.Simulation;

/// <summary>
/// Aerosol species represented by the realtime climate envelope.
/// </summary>
public enum AerosolSpecies
{
    /// <summary>Coarse mineral dust, usually enhanced in the subtropical belts.</summary>
    Dust,

    /// <summary>Fine sea-salt/water mist, retained near the lower atmosphere.</summary>
    Mist,
}

/// <summary>
/// The evaluated aerosol optical state at one location, time and wavelength.
///
/// <para>
/// <see cref="Aod550"/> is the effective vertical aerosol optical depth at 550 nm.  The
/// wavelength conversion follows the Ångström power law.  The two species add to the
/// effective 550 nm depth, while the factors are exposed so diagnostics can explain why
/// a particular weather sample is brighter or dimmer than its reference state.
/// </para>
/// </summary>
public readonly record struct AerosolClimateSample(
    double Aod550,
    double DustAod550,
    double MistAod550,
    double OpticalDepth,
    double LatitudeFactor,
    double AltitudeFactor,
    double TemporalFactor,
    double SeasonalFactor,
    double WavelengthNanometers,
    double AngstromExponent)
{
    /// <summary>Returns the 550 nm optical depth for one species.</summary>
    public double Aod550For(AerosolSpecies species) => species switch
    {
        AerosolSpecies.Dust => DustAod550,
        AerosolSpecies.Mist => MistAod550,
        _ => throw new ArgumentOutOfRangeException(nameof(species), species, null),
    };

    /// <summary>
    /// Converts this sample's 550 nm depth to another wavelength in nanometres using the
    /// Ångström relation τ(λ) = τ(550) · (λ/550)^−α.  Non-positive or non-finite input
    /// uses the reference wavelength, keeping a diagnostic sample finite.
    /// </summary>
    public double OpticalDepthAt(double wavelengthNanometers)
    {
        double wavelength = AerosolClimateState.NormalizeWavelength(wavelengthNanometers);
        double depth = Aod550 * System.Math.Pow(
            AerosolClimateState.ReferenceWavelengthNanometers / wavelength,
            AngstromExponent);
        return AerosolClimateState.ClampOpticalDepth(depth);
    }

    /// <summary>True when every reported value is finite and non-negative.</summary>
    public bool IsFiniteNonNegative =>
        AerosolClimateState.IsFiniteNonNegative(Aod550)
        && AerosolClimateState.IsFiniteNonNegative(DustAod550)
        && AerosolClimateState.IsFiniteNonNegative(MistAod550)
        && AerosolClimateState.IsFiniteNonNegative(OpticalDepth)
        && AerosolClimateState.IsFiniteNonNegative(LatitudeFactor)
        && AerosolClimateState.IsFiniteNonNegative(AltitudeFactor)
        && AerosolClimateState.IsFiniteNonNegative(TemporalFactor)
        && AerosolClimateState.IsFiniteNonNegative(SeasonalFactor)
        && AerosolClimateState.IsFiniteNonNegative(WavelengthNanometers)
        && AerosolClimateState.IsFiniteNonNegative(AngstromExponent);
}

/// <summary>
/// Bounded, deterministic aerosol/climate state for an atmosphere renderer or weather
/// diagnostic.
///
/// <para>
/// The reference state is defined by <see cref="Aod550"/> and
/// <see cref="AngstromExponent"/>.  <see cref="DustFraction"/> and
/// <see cref="MistFraction"/> partition that depth.  The spatial and temporal modifiers
/// intentionally remain dimensionless and bounded, so a malformed save file or an
/// uninitialised telemetry value cannot create negative or non-finite optical depth.
/// </para>
///
/// <para>
/// This is an envelope rather than a weather forecast: latitude models a smooth
/// subtropical dust belt, altitude uses an exponential aerosol scale height, and the two
/// periodic terms provide a stable day/season weather phase until a real weather field is
/// available.  Call <see cref="Normalize"/> at a data boundary, or use
/// <see cref="Sample"/>, which normalises defensively on every evaluation.
/// </para>
/// </summary>
public sealed record AerosolClimateState
{
    /// <summary>Reference wavelength for aerosol optical depth, in nanometres.</summary>
    public const double ReferenceWavelengthNanometers = 550.0;

    /// <summary>Maximum accepted reference AOD; larger values are pathological for gameplay.</summary>
    public const double MaximumAod550 = 100.0;

    /// <summary>Maximum supported wavelength conversion range lower bound, in nanometres.</summary>
    public const double MinimumWavelengthNanometers = 1.0;

    /// <summary>Maximum supported wavelength conversion range upper bound, in nanometres.</summary>
    public const double MaximumWavelengthNanometers = 1_000_000.0;

    /// <summary>Reference aerosol optical depth at 550 nm.</summary>
    public double Aod550 { get; init; } = 0.08;

    /// <summary>
    /// Ångström exponent α in τ(λ) = τ(550) · (λ/550)^−α.  Fine mist normally has a
    /// larger exponent than coarse dust; the bounded state uses the configured effective
    /// mixture exponent.
    /// </summary>
    public double AngstromExponent { get; init; } = 1.0;

    /// <summary>Unnormalised fraction of the aerosol depth assigned to mineral dust.</summary>
    public double DustFraction { get; init; } = 0.60;

    /// <summary>Unnormalised fraction of the aerosol depth assigned to water/sea mist.</summary>
    public double MistFraction { get; init; } = 0.40;

    /// <summary>
    /// Amplitude of the smooth latitude envelope.  Positive values enhance the configured
    /// <see cref="LatitudePeakDegrees"/> and are bounded to keep the factor positive.
    /// </summary>
    public double LatitudeModulation { get; init; } = 0.30;

    /// <summary>Absolute latitude at the centre of the dust/climate envelope, in degrees.</summary>
    public double LatitudePeakDegrees { get; init; } = 25.0;

    /// <summary>Gaussian half-width of the latitude envelope, in degrees.</summary>
    public double LatitudeWidthDegrees { get; init; } = 22.0;

    /// <summary>
    /// Aerosol scale height in metres.  The altitude factor is exp(−max(h,0)/H).
    /// </summary>
    public double AltitudeScaleHeightMeters { get; init; } = 1_500.0;

    /// <summary>Short-period weather modulation amplitude.</summary>
    public double TemporalModulation { get; init; } = 0.12;

    /// <summary>Short-period weather modulation period in seconds.</summary>
    public double TemporalPeriodSeconds { get; init; } = 86_400.0;

    /// <summary>Phase offset for the short-period weather modulation in seconds.</summary>
    public double TemporalPhaseSeconds { get; init; }

    /// <summary>Seasonal modulation amplitude.</summary>
    public double SeasonalModulation { get; init; } = 0.10;

    /// <summary>Seasonal modulation period in seconds (default: Julian year).</summary>
    public double SeasonalPeriodSeconds { get; init; } = 31_557_600.0;

    /// <summary>Phase offset for the seasonal modulation in seconds.</summary>
    public double SeasonalPhaseSeconds { get; init; }

    /// <summary>A conservative Earth-like starting profile for gameplay and tests.</summary>
    public static AerosolClimateState EarthLike { get; } = new();

    /// <summary>
    /// Returns a finite, physically bounded copy of this state.  Species fractions are
    /// normalised to sum to one; when both are absent a neutral 50/50 mixture is selected.
    /// </summary>
    public AerosolClimateState Normalize()
    {
        double aod = ClampFinite(Aod550, EarthLike.Aod550, 0.0, MaximumAod550);
        double alpha = ClampFinite(AngstromExponent, EarthLike.AngstromExponent, 0.0, 4.0);

        // Do not clamp a positive input to one before normalising: callers commonly use
        // weights such as 3:1.  Scaling by the largest weight also avoids overflow for a
        // corrupt save containing double.MaxValue.
        double dust = FiniteNonNegative(DustFraction);
        double mist = FiniteNonNegative(MistFraction);
        double largestFraction = System.Math.Max(dust, mist);
        if (largestFraction <= 0.0)
        {
            dust = 0.5;
            mist = 0.5;
        }
        else
        {
            dust /= largestFraction;
            mist /= largestFraction;
            double fractionSum = dust + mist;
            dust /= fractionSum;
            mist /= fractionSum;
        }

        return this with
        {
            Aod550 = aod,
            AngstromExponent = alpha,
            DustFraction = dust,
            MistFraction = mist,
            LatitudeModulation = ClampFinite(LatitudeModulation, EarthLike.LatitudeModulation,
                -0.95, 4.0),
            LatitudePeakDegrees = ClampFinite(LatitudePeakDegrees,
                EarthLike.LatitudePeakDegrees, 0.0, 90.0),
            LatitudeWidthDegrees = ClampFinite(LatitudeWidthDegrees,
                EarthLike.LatitudeWidthDegrees, 0.001, 90.0),
            AltitudeScaleHeightMeters = ClampFinite(AltitudeScaleHeightMeters,
                EarthLike.AltitudeScaleHeightMeters, 1.0, 1_000_000.0),
            TemporalModulation = ClampFinite(TemporalModulation,
                EarthLike.TemporalModulation, -0.95, 4.0),
            TemporalPeriodSeconds = ClampFinite(TemporalPeriodSeconds,
                EarthLike.TemporalPeriodSeconds, 1.0, 1.0e12),
            TemporalPhaseSeconds = FiniteOr(TemporalPhaseSeconds, 0.0),
            SeasonalModulation = ClampFinite(SeasonalModulation,
                EarthLike.SeasonalModulation, -0.95, 4.0),
            SeasonalPeriodSeconds = ClampFinite(SeasonalPeriodSeconds,
                EarthLike.SeasonalPeriodSeconds, 1.0, 1.0e13),
            SeasonalPhaseSeconds = FiniteOr(SeasonalPhaseSeconds, 0.0),
        };
    }

    /// <summary>
    /// Evaluates the climate envelope at latitude (degrees), altitude (metres), simulation
    /// time (seconds), and wavelength (nanometres).
    /// </summary>
    public AerosolClimateSample Sample(
        double latitudeDegrees,
        double altitudeMeters,
        double timeSeconds,
        double wavelengthNanometers = ReferenceWavelengthNanometers)
    {
        var state = Normalize();
        double latitudeFactor = state.LatitudeFactorNormalized(latitudeDegrees);
        double altitudeFactor = state.AltitudeFactorNormalized(altitudeMeters);
        double temporalFactor = state.TemporalFactorNormalized(timeSeconds);
        double seasonalFactor = state.SeasonalFactorNormalized(timeSeconds);
        double effectiveAod = ClampOpticalDepth(state.Aod550 * latitudeFactor
            * altitudeFactor * temporalFactor * seasonalFactor);
        double wavelength = NormalizeWavelength(wavelengthNanometers);
        double opticalDepth = ClampOpticalDepth(effectiveAod * System.Math.Pow(
            ReferenceWavelengthNanometers / wavelength, state.AngstromExponent));

        return new AerosolClimateSample(
            effectiveAod,
            effectiveAod * state.DustFraction,
            effectiveAod * state.MistFraction,
            opticalDepth,
            latitudeFactor,
            altitudeFactor,
            temporalFactor,
            seasonalFactor,
            wavelength,
            state.AngstromExponent);
    }

    /// <summary>Returns the bounded latitude multiplier at a given latitude in degrees.</summary>
    public double LatitudeFactor(double latitudeDegrees) =>
        Normalize().LatitudeFactorNormalized(latitudeDegrees);

    /// <summary>Returns the bounded exponential aerosol multiplier at altitude in metres.</summary>
    public double AltitudeFactor(double altitudeMeters) =>
        Normalize().AltitudeFactorNormalized(altitudeMeters);

    /// <summary>Returns the bounded short-period weather multiplier at simulation time.</summary>
    public double TemporalFactor(double timeSeconds) =>
        Normalize().TemporalFactorNormalized(timeSeconds);

    /// <summary>Returns the bounded seasonal multiplier at simulation time.</summary>
    public double SeasonalFactor(double timeSeconds) =>
        Normalize().SeasonalFactorNormalized(timeSeconds);

    /// <summary>
    /// Converts a reference AOD to another wavelength using the state's normalised exponent.
    /// Invalid wavelengths fall back to 550 nm.
    /// </summary>
    public double OpticalDepthAt(double aod550, double wavelengthNanometers) =>
        ClampOpticalDepth(ClampFinite(aod550, 0.0, 0.0, MaximumAod550)
            * System.Math.Pow(ReferenceWavelengthNanometers / NormalizeWavelength(wavelengthNanometers),
                Normalize().AngstromExponent));

    private double LatitudeFactorNormalized(double latitudeDegrees)
    {
        double latitude = double.IsFinite(latitudeDegrees)
            ? System.Math.Clamp(System.Math.Abs(latitudeDegrees), 0.0, 90.0)
            : 0.0;
        double distance = latitude - LatitudePeakDegrees;
        double envelope = System.Math.Exp(-0.5 * distance * distance
            / (LatitudeWidthDegrees * LatitudeWidthDegrees));
        return ClampFactor(1.0 + LatitudeModulation * envelope);
    }

    private double AltitudeFactorNormalized(double altitudeMeters)
    {
        double altitude = double.IsFinite(altitudeMeters)
            ? System.Math.Max(0.0, altitudeMeters)
            : 0.0;
        return ClampFactor(System.Math.Exp(-altitude / AltitudeScaleHeightMeters));
    }

    private double TemporalFactorNormalized(double timeSeconds) =>
        PeriodicFactor(TemporalModulation, TemporalPeriodSeconds,
            TemporalPhaseSeconds, timeSeconds, useCosine: false);

    private double SeasonalFactorNormalized(double timeSeconds) =>
        PeriodicFactor(SeasonalModulation, SeasonalPeriodSeconds,
            SeasonalPhaseSeconds, timeSeconds, useCosine: true);

    private static double PeriodicFactor(
        double amplitude,
        double period,
        double phase,
        double time,
        bool useCosine)
    {
        double t = double.IsFinite(time) ? time : 0.0;
        double angle = 2.0 * System.Math.PI * ((t + phase) / period);
        double wave = useCosine ? System.Math.Cos(angle) : System.Math.Sin(angle);
        return ClampFactor(1.0 + amplitude * wave);
    }

    private static double ClampFinite(double value, double fallback, double minimum, double maximum)
    {
        if (!double.IsFinite(value)) return fallback;
        return System.Math.Clamp(value, minimum, maximum);
    }

    private static double FiniteOr(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static double FiniteNonNegative(double value) =>
        double.IsFinite(value) && value > 0.0 ? value : 0.0;

    private static double ClampFactor(double value) =>
        double.IsFinite(value) ? System.Math.Clamp(value, 0.05, 5.0) : 1.0;

    internal static double ClampOpticalDepth(double value) =>
        double.IsFinite(value) && value > 0.0 ? System.Math.Min(value, 1.0e30) : 0.0;

    internal static bool IsFiniteNonNegative(double value) =>
        double.IsFinite(value) && value >= 0.0;

    internal static double NormalizeWavelength(double wavelengthNanometers) =>
        !double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0.0
            ? ReferenceWavelengthNanometers
            : System.Math.Clamp(wavelengthNanometers,
                MinimumWavelengthNanometers, MaximumWavelengthNanometers);
}
