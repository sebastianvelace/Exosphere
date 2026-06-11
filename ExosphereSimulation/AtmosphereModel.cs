namespace Exosphere.Simulation;

/// <summary>
/// Exponential-scale-height atmosphere model.
/// Sufficient for drag and re-entry heating calculations.
/// </summary>
public partial class AtmosphereModel
{
    /// <summary>Altitude above which the atmosphere is considered absent (m).</summary>
    public double MaxAltitude { get; init; }

    /// <summary>Sea-level atmospheric density (kg/m³).</summary>
    public double SeaLevelDensity { get; init; } = 1.225;   // Earth default

    /// <summary>Scale height — altitude at which density drops by 1/e (m).</summary>
    public double ScaleHeight { get; init; } = 8500.0;      // Earth default (~8.5 km)

    /// <summary>Sea-level pressure (Pa).</summary>
    public double SeaLevelPressure { get; init; } = 101_325.0;

    /// <summary>Molar mass of atmosphere (kg/mol). Used for derived properties.</summary>
    public double MolarMass { get; init; } = 0.0289644;     // Earth air

    /// <summary>
    /// Returns atmospheric density (kg/m³) at a given altitude above the surface (m).
    /// Uses an exponential scale-height model:  ρ = ρ₀ · exp(−h / H)
    /// </summary>
    public double GetDensity(double altitude)
    {
        if (altitude >= MaxAltitude || altitude < 0.0) return 0.0;
        return SeaLevelDensity * System.Math.Exp(-altitude / ScaleHeight);
    }

    /// <summary>
    /// Returns atmospheric pressure (Pa) at altitude (m).
    /// </summary>
    public double GetPressure(double altitude)
    {
        if (altitude >= MaxAltitude || altitude < 0.0) return 0.0;
        return SeaLevelPressure * System.Math.Exp(-altitude / ScaleHeight);
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Standard Earth-like atmosphere.</summary>
    public static AtmosphereModel Earth() => new()
    {
        MaxAltitude      = 140_000.0,    // 140 km
        SeaLevelDensity  = 1.225,
        ScaleHeight      = 8500.0,
        SeaLevelPressure = 101_325.0,
        MolarMass        = 0.0289644,
    };

    /// <summary>Thin Martian CO₂ atmosphere.</summary>
    public static AtmosphereModel Mars() => new()
    {
        MaxAltitude      = 100_000.0,    // 100 km
        SeaLevelDensity  = 0.020,
        ScaleHeight      = 11_100.0,
        SeaLevelPressure = 610.0,
        MolarMass        = 0.04401,
    };

    /// <summary>Thick Venusian CO₂ atmosphere.</summary>
    public static AtmosphereModel Venus() => new()
    {
        MaxAltitude      = 250_000.0,
        SeaLevelDensity  = 65.0,
        ScaleHeight      = 15_900.0,
        SeaLevelPressure = 9_200_000.0,
        MolarMass        = 0.04401,
    };
}
