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
    /// <summary>Universal gas constant (J/(mol·K)).</summary>
    private const double R = 8.31446;

    /// <summary>
    /// Surface gravitational acceleration used by the hydrostatic integration (m/s²).
    /// Per body: an atmospheric column is held up by that planet's gravity, not Earth's.
    /// </summary>
    public double SurfaceGravity { get; init; } = 9.80665;   // Earth default

    /// <summary>
    /// Effective radius (m) used to convert geometric altitude into geopotential altitude.
    ///
    /// Layered standard atmospheres (USSA-1976 and its planetary equivalents) are tabulated
    /// against GEOPOTENTIAL altitude, which is what lets the hydrostatic integration use a
    /// constant surface gravity: the geopotential coordinate already absorbs gravity falling
    /// off with height. Feeding it geometric altitude instead silently over-weighs the
    /// column — the error grows with altitude (~1.1 km of offset by 86 km on Earth).
    /// </summary>
    public double GeopotentialRadius { get; init; } = 6_356_766.0;   // Earth default

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
    /// Growth rate of the thermosphere scale height with altitude (dimensionless):
    /// H(z) = H₀ + k·(z − MaxAltitude).
    ///
    /// The thermosphere heats with altitude, so its scale height grows — on Earth from
    /// ~19 km at 140 km to ~60 km at 400 km. A single exponential cannot span that: it is
    /// either too dense high up or too thin low down. Integrating dρ/ρ = −dz/H(z) with H
    /// linear gives the closed form used in <see cref="GetDensity"/>, which collapses back
    /// to the plain exponential as k → 0 (the default).
    /// </summary>
    public double ThermosphereScaleHeightGrowth { get; init; } = 0.0;

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
    /// Geometric altitude (m above the surface) → geopotential altitude (m), the coordinate
    /// the layer table is defined against: h = R·z / (R + z).
    /// </summary>
    public double ToGeopotential(double geometricAltitude) =>
        GeopotentialRadius * geometricAltitude / (GeopotentialRadius + geometricAltitude);

    /// <summary>
    /// Returns atmospheric temperature (K) at a given geometric altitude above the surface (m).
    /// Layered ISA model when layers are present, otherwise constant sea-level temperature.
    /// </summary>
    public double GetTemperature(double altitude)
    {
        if (Layers.Count == 0) return SeaLevelTemperature;

        if (altitude < 0.0) altitude = 0.0;
        double h = ToGeopotential(altitude);

        // Find the layer containing this altitude; above the top layer,
        // extrapolate from the last layer's profile.
        AtmosphereLayer layer = Layers[^1];
        foreach (var l in Layers)
        {
            if (h < l.AltMax) { layer = l; break; }
        }

        double t = layer.LapseRate == 0.0
            ? layer.TempBase
            : layer.TempBase + layer.LapseRate * (h - layer.AltMin);

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

        double h = ToGeopotential(altitude);

        // Locate the layer containing this altitude (clamp into last layer above the top).
        int idx = Layers.Count - 1;
        for (int i = 0; i < Layers.Count; i++)
        {
            if (h < Layers[i].AltMax) { idx = i; break; }
        }

        return PressureWithinLayer(Layers[idx], _layerBasePressures![idx], h);
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

        // Above the ISA layers, decay from the boundary density so low LEO experiences
        // slow orbital decay instead of a hard cut to vacuum.
        if (altitude >= MaxAltitude)
        {
            if (ThermosphereScaleHeight <= 0.0 || altitude >= ThermosphereTopAltitude)
                return 0.0;

            double baseRho = LayeredDensity(MaxAltitude);
            double dz      = altitude - MaxAltitude;
            double k       = ThermosphereScaleHeightGrowth;

            // H grows with altitude: ρ = ρ₀ · (1 + k·Δz/H₀)^(−1/k). k → 0 is the exponential.
            if (k <= 1e-9)
                return baseRho * System.Math.Exp(-dz / ThermosphereScaleHeight);

            return baseRho * System.Math.Pow(1.0 + k * dz / ThermosphereScaleHeight, -1.0 / k);
        }

        return LayeredDensity(altitude);
    }

    // ── Hydrostatic helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Pressure at <paramref name="geopotentialAltitude"/> inside (or extrapolated above)
    /// <paramref name="layer"/>, given the pressure at the layer's base. Both the layer
    /// bounds and the argument are geopotential, which is what makes a constant
    /// <see cref="SurfaceGravity"/> the correct gravity to integrate with.
    /// </summary>
    private double PressureWithinLayer(AtmosphereLayer layer, double basePressure, double geopotentialAltitude)
    {
        double dh = geopotentialAltitude - layer.AltMin;
        double gm = SurfaceGravity * MolarMass;

        if (layer.LapseRate == 0.0)
        {
            // Isothermal: P = P_b · exp(−g·M·Δh / (R·T))
            return basePressure * System.Math.Exp(-gm * dh / (R * layer.TempBase));
        }

        // Gradient: P = P_b · (T/T_b)^(−g·M / (R·L))
        double t = layer.TempBase + layer.LapseRate * dh;
        if (t <= 0.0) return 0.0;
        double exponent = -gm / (R * layer.LapseRate);
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
    /// <summary>
    /// US Standard Atmosphere 1976. Layer bounds and lapse rates are the published table,
    /// in GEOPOTENTIAL metres (see <see cref="GeopotentialRadius"/>).
    ///
    /// The first seven layers are USSA-76 proper, which assumes a constant mean molar mass
    /// and is therefore reproduced essentially exactly by this ideal-gas formulation, up to
    /// 84 852 m geopotential (86 km geometric).
    ///
    /// Above that, USSA-76 switches to a different formulation in which the mean molar mass
    /// falls as the gases dissociate. The last four layers approximate its temperature
    /// profile (rising to ~560 K by 140 km) while still holding molar mass constant, so
    /// density up there is an approximation, not a reproduction. It is good to a few tens
    /// of percent — far better than an isothermal cap, which starves the column of the
    /// thermal expansion that actually holds the thermosphere up.
    /// </summary>
    public static AtmosphereModel Earth() => new()
    {
        MaxAltitude             = 140_000.0,    // 140 km — aerodynamically significant boundary
        SeaLevelDensity         = 1.225,
        ScaleHeight             = 8500.0,
        SeaLevelPressure        = 101_325.0,
        SeaLevelTemperature     = 288.15,
        MolarMass               = 0.0289644,
        SurfaceGravity          = 9.80665,
        GeopotentialRadius      = 6_356_766.0,
        // Fitted to NRLMSISE-00 from 140 to 500 km on the corrected anchor: within 1.14x
        // across the whole band (the best single exponential manages only 3.0x).
        ThermosphereScaleHeight       = 18_750.0,   // H₀ at 140 km
        ThermosphereScaleHeightGrowth = 0.160,      // → ~60 km by 400 km
        ThermosphereTopAltitude       = 1_000_000.0,
        Layers                  = new List<AtmosphereLayer>
        {
            new(       0.0,  11_000.0, 288.15, -0.0065),    // troposphere
            new(  11_000.0,  20_000.0, 216.65,  0.0),       // tropopause
            new(  20_000.0,  32_000.0, 216.65,  0.001),     // stratosphere
            new(  32_000.0,  47_000.0, 228.65,  0.0028),    // stratosphere
            new(  47_000.0,  51_000.0, 270.65,  0.0),       // stratopause
            new(  51_000.0,  71_000.0, 270.65, -0.0028),    // mesosphere
            new(  71_000.0,  84_852.0, 214.65, -0.002),     // mesopause
            // Above 84 852 m geopotential: approximated USSA-76 upper temperature profile.
            new(  84_852.0,  89_715.0, 186.87,  0.0),
            new(  89_715.0, 108_130.0, 186.87,  0.0028852),
            new( 108_130.0, 117_777.0, 240.00,  0.0124390),
            new( 117_777.0, 136_985.0, 360.00,  0.0103931),
        },
    };

    /// <summary>
    /// Thin Martian CO₂ atmosphere. Mirrors <c>data/bodies/mars.json</c> — the molar mass is
    /// CO₂ and the gravity is Mars's, not Earth air held up by Earth gravity.
    /// </summary>
    public static AtmosphereModel Mars() => new()
    {
        MaxAltitude         = 100_000.0,    // 100 km
        SeaLevelDensity     = 0.020,
        ScaleHeight         = 11_100.0,
        SeaLevelPressure    = 636.0,
        SeaLevelTemperature = 210.0,
        MolarMass           = 0.04401,      // CO₂
        SurfaceGravity      = 3.72076,
        GeopotentialRadius  = 3_389_500.0,
        Layers              = new List<AtmosphereLayer>
        {
            new(0.0, 100_000.0, 210.0, -0.00098),
        },
    };

    /// <summary>Thick Venusian CO₂ atmosphere. Mirrors <c>data/bodies/venus.json</c>.</summary>
    public static AtmosphereModel Venus() => new()
    {
        MaxAltitude         = 250_000.0,
        SeaLevelDensity     = 65.0,
        ScaleHeight         = 15_000.0,
        SeaLevelPressure    = 9_200_000.0,
        SeaLevelTemperature = 737.0,
        MolarMass           = 0.04401,      // CO₂
        SurfaceGravity      = 8.87,
        GeopotentialRadius  = 6_051_800.0,
        Layers              = new List<AtmosphereLayer>
        {
            new(0.0, 250_000.0, 737.0, -0.0075),
        },
    };
}
