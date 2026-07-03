namespace Exosphere.Simulation;

/// <summary>
/// A single ISA-style atmospheric layer.
/// Temperature varies linearly with altitude within the layer:
///   T(h) = TempBase + LapseRate · (h − AltMin)
/// </summary>
/// <param name="AltMin">Lower altitude bound of the layer (m).</param>
/// <param name="AltMax">Upper altitude bound of the layer (m).</param>
/// <param name="TempBase">Temperature at the bottom of the layer (K).</param>
/// <param name="LapseRate">Temperature lapse rate (K/m). 0 ⇒ isothermal layer.</param>
public record AtmosphereLayer(double AltMin, double AltMax, double TempBase, double LapseRate);

/// <summary>
/// Atmosphere model.
/// When <see cref="Layers"/> is populated, uses the ISA standard-atmosphere
/// formulation (per-layer hydrostatic integration + ideal gas law).
/// When no layers are defined, falls back to a simple exponential
/// scale-height model — sufficient for thin/unknown atmospheres.
/// </summary>
public partial class AtmosphereModel
{
    /// <summary>Standard gravitational acceleration (m/s²).</summary>
    private const double G0 = 9.80665;

    /// <summary>Universal gas constant (J/(mol·K)).</summary>
    private const double R = 8.31446;

    /// <summary>Altitude above which the atmosphere is considered absent (m).</summary>
    public double MaxAltitude { get; init; }

    /// <summary>Sea-level atmospheric density (kg/m³).</summary>
    public double SeaLevelDensity { get; init; } = 1.225;   // Earth default

    /// <summary>Scale height — altitude at which density drops by 1/e (m).</summary>
    public double ScaleHeight { get; init; } = 8500.0;      // Earth default (~8.5 km)

    /// <summary>Sea-level pressure (Pa).</summary>
    public double SeaLevelPressure { get; init; } = 101_325.0;

    /// <summary>Sea-level temperature (K). Used as a constant fallback when no layers exist.</summary>
    public double SeaLevelTemperature { get; init; } = 288.15;

    /// <summary>Molar mass of atmosphere (kg/mol). Used for derived properties.</summary>
    public double MolarMass { get; init; } = 0.0289644;     // Earth air

    /// <summary>
    /// Scale height of the residual thermosphere above <see cref="MaxAltitude"/> (m).
    /// 0 disables the tail (density hard-cuts to vacuum at the top of the ISA layers).
    /// When positive, density decays exponentially from the boundary density so low
    /// LEO experiences slow orbital decay. This is a documented single-exponential
    /// approximation; the real Earth thermosphere effective scale height is ~40-60 km
    /// and grows with altitude. Only <see cref="GetDensity"/> uses this tail —
    /// <see cref="MaxAltitude"/> still marks the aerodynamically significant boundary
    /// that flight controllers (ascent, EDL, systems) reason about.
    /// </summary>
    public double ThermosphereScaleHeight { get; init; } = 0.0;

    /// <summary>
    /// Altitude above which the residual thermosphere density is treated as vacuum (m).
    /// Only relevant when <see cref="ThermosphereScaleHeight"/> is positive.
    /// </summary>
    public double ThermosphereTopAltitude { get; init; } = 0.0;

    /// <summary>
    /// ISA temperature layers, ordered by ascending altitude.
    /// Empty list ⇒ exponential fallback model.
    /// </summary>
    public List<AtmosphereLayer> Layers { get; init; } = new();

    // Lazily computed pressure at the base of each layer (hydrostatic walk-up).
    private double[]? _layerBasePressures;

    /// <summary>
    /// Returns atmospheric temperature (K) at a given altitude above the surface (m).
    /// Layered ISA model when layers are present, otherwise constant sea-level temperature.
    /// </summary>
    public double GetTemperature(double altitude)
    {
        if (Layers.Count == 0) return SeaLevelTemperature;

        if (altitude < 0.0) altitude = 0.0;

        // Find the layer containing this altitude; above the top layer,
        // extrapolate from the last layer's profile.
        AtmosphereLayer layer = Layers[^1];
        foreach (var l in Layers)
        {
            if (altitude < l.AltMax) { layer = l; break; }
        }

        double t = layer.LapseRate == 0.0
            ? layer.TempBase
            : layer.TempBase + layer.LapseRate * (altitude - layer.AltMin);

        return System.Math.Max(t, 0.0);
    }

    /// <summary>
    /// Returns atmospheric pressure (Pa) at altitude (m).
    /// Layered model: hydrostatic equation integrated per layer.
    /// No layers: exponential scale-height model.
    /// </summary>
    public double GetPressure(double altitude)
    {
        if (altitude >= MaxAltitude || altitude < 0.0) return 0.0;

        if (Layers.Count == 0)
            return SeaLevelPressure * System.Math.Exp(-altitude / ScaleHeight);

        return LayeredPressure(altitude);
    }

    /// <summary>
    /// Layered ISA pressure (Pa) without the <see cref="MaxAltitude"/> cutoff.
    /// Used internally so the thermosphere tail can anchor to the boundary density.
    /// </summary>
    private double LayeredPressure(double altitude)
    {
        EnsureLayerBasePressures();

        // Locate the layer containing this altitude (clamp into last layer above the top).
        int idx = Layers.Count - 1;
        for (int i = 0; i < Layers.Count; i++)
        {
            if (altitude < Layers[i].AltMax) { idx = i; break; }
        }

        return PressureWithinLayer(Layers[idx], _layerBasePressures![idx], altitude);
    }

    /// <summary>
    /// Layered ISA density (kg/m³) via the ideal gas law, without the
    /// <see cref="MaxAltitude"/> cutoff. Used internally.
    /// </summary>
    private double LayeredDensity(double altitude)
    {
        double t = GetTemperature(altitude);
        if (t <= 0.0) return 0.0;

        double p = LayeredPressure(altitude);
        double rho = p * MolarMass / (R * t);
        return (double.IsNaN(rho) || rho < 0.0) ? 0.0 : rho;
    }

    /// <summary>
    /// Returns atmospheric density (kg/m³) at a given altitude above the surface (m).
    /// Layered model: ideal gas law  ρ = P·M / (R·T).
    /// No layers: exponential scale-height model  ρ = ρ₀ · exp(−h / H).
    /// </summary>
    public double GetDensity(double altitude)
    {
        if (altitude < 0.0) return 0.0;

        if (Layers.Count == 0)
            return altitude >= MaxAltitude
                ? 0.0
                : SeaLevelDensity * System.Math.Exp(-altitude / ScaleHeight);

        // Above the ISA layers, decay exponentially from the boundary density so low
        // LEO experiences slow orbital decay instead of a hard cut to vacuum.
        if (altitude >= MaxAltitude)
        {
            if (ThermosphereScaleHeight <= 0.0 || altitude >= ThermosphereTopAltitude)
                return 0.0;

            double baseRho = LayeredDensity(MaxAltitude);
            return baseRho * System.Math.Exp(-(altitude - MaxAltitude) / ThermosphereScaleHeight);
        }

        return LayeredDensity(altitude);
    }

    // ── Hydrostatic helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Pressure at <paramref name="altitude"/> inside (or extrapolated above)
    /// <paramref name="layer"/>, given the pressure at the layer's base.
    /// </summary>
    private double PressureWithinLayer(AtmosphereLayer layer, double basePressure, double altitude)
    {
        double dh = altitude - layer.AltMin;

        if (layer.LapseRate == 0.0)
        {
            // Isothermal: P = P_b · exp(−g·M·Δh / (R·T))
            return basePressure * System.Math.Exp(-G0 * MolarMass * dh / (R * layer.TempBase));
        }

        // Gradient: P = P_b · (T/T_b)^(−g·M / (R·L))
        double t = layer.TempBase + layer.LapseRate * dh;
        if (t <= 0.0) return 0.0;
        double exponent = -G0 * MolarMass / (R * layer.LapseRate);
        return basePressure * System.Math.Pow(t / layer.TempBase, exponent);
    }

    /// <summary>
    /// Walks up through the layers from sea level, computing the pressure at
    /// the base of each layer. Cached after first call.
    /// </summary>
    private void EnsureLayerBasePressures()
    {
        if (_layerBasePressures != null) return;

        var basePressures = new double[Layers.Count];
        double p = SeaLevelPressure;
        for (int i = 0; i < Layers.Count; i++)
        {
            basePressures[i] = p;
            // Pressure at the top of this layer = base pressure of the next.
            p = PressureWithinLayer(Layers[i], p, Layers[i].AltMax);
        }
        _layerBasePressures = basePressures;
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Standard Earth-like atmosphere (ISA layers).</summary>
    public static AtmosphereModel Earth() => new()
    {
        MaxAltitude             = 140_000.0,    // 140 km — aerodynamically significant boundary
        SeaLevelDensity         = 1.225,
        ScaleHeight             = 8500.0,
        SeaLevelPressure        = 101_325.0,
        SeaLevelTemperature     = 288.15,
        MolarMass               = 0.0289644,
        ThermosphereScaleHeight = 45_000.0,     // residual LEO drag → slow orbital decay
        ThermosphereTopAltitude = 1_000_000.0,  // 1000 km
        Layers                  = new List<AtmosphereLayer>
        {
            new(     0.0,  11_000.0, 288.15, -0.0065),
            new(11_000.0,  20_000.0, 216.65,  0.0),
            new(20_000.0,  32_000.0, 216.65,  0.001),
            new(32_000.0,  47_000.0, 228.65,  0.0028),
            new(47_000.0,  51_000.0, 270.65,  0.0),
            new(51_000.0,  71_000.0, 270.65, -0.0028),
        },
    };

    /// <summary>Thin Martian CO₂ atmosphere.</summary>
    public static AtmosphereModel Mars() => new()
    {
        MaxAltitude         = 100_000.0,    // 100 km
        SeaLevelDensity     = 0.020,
        ScaleHeight         = 11_100.0,
        SeaLevelPressure    = 610.0,
        SeaLevelTemperature = 210.0,
        MolarMass           = 0.04401,
    };

    /// <summary>Thick Venusian CO₂ atmosphere.</summary>
    public static AtmosphereModel Venus() => new()
    {
        MaxAltitude         = 250_000.0,
        SeaLevelDensity     = 65.0,
        ScaleHeight         = 15_900.0,
        SeaLevelPressure    = 9_200_000.0,
        SeaLevelTemperature = 737.0,
        MolarMass           = 0.04401,
    };
}
