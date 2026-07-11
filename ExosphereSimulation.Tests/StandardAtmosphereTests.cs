namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;

/// <summary>
/// RF-06 acceptance — every planet gets its own gas and its own gravity, and Earth
/// reproduces the US Standard Atmosphere 1976 it claims to model.
///
/// Two regressions these exist to prevent:
///
///  1. Mars and Venus declared no molar mass and no gravity, so their columns were held up
///     by EARTH gravity and made of EARTH air. The hydrostatic exponent −g·M/(R·L) came out
///     34.9 instead of 20.1, leaving the Martian atmosphere ~50× too thin at altitude.
///
///  2. The layer table is tabulated against GEOPOTENTIAL altitude but was being fed
///     GEOMETRIC altitude, and Earth's layers stopped at 71 km and extrapolated a negative
///     lapse rate above it — which froze the upper atmosphere toward absolute zero and
///     starved the thermosphere anchor by orders of magnitude.
/// </summary>
public class StandardAtmosphereTests
{
    // USSA-1976 at its layer boundaries — the points the standard tabulates unambiguously.
    // (geopotential altitude m, T K, P Pa, rho kg/m³)
    public static TheoryData<double, double, double, double> Ussa76Boundaries => new()
    {
        {      0.0, 288.15, 101_325.0,  1.2250     },
        { 11_000.0, 216.65,  22_632.1,  0.363918   },
        { 20_000.0, 216.65,   5_474.89, 0.0880349  },
        { 32_000.0, 228.65,     868.019, 0.0132250 },
        { 47_000.0, 270.65,     110.906, 0.00142753 },
        { 51_000.0, 270.65,      66.9389, 0.000861600 },
        { 71_000.0, 214.65,       3.95642, 6.42110e-5 },
        { 84_852.0, 186.87,       0.373384, 6.95788e-6 },
    };

    [Theory]
    [MemberData(nameof(Ussa76Boundaries))]
    public void Earth_ReproducesUssa76_WithinOnePercent(
        double geopotentialAlt, double tRef, double pRef, double rhoRef)
    {
        var atmo = LoadAtmosphere("earth");

        // The model takes geometric altitude; the standard tabulates geopotential.
        double z = GeometricFromGeopotential(geopotentialAlt, atmo.GeopotentialRadius);

        AssertRelative(tRef,   atmo.GetTemperature(z), 0.01, "temperature");
        AssertRelative(pRef,   atmo.GetPressure(z),    0.01, "pressure");
        AssertRelative(rhoRef, atmo.GetDensity(z),     0.01, "density");
    }

    [Fact]
    public void Earth_ThermosphereAnchorIsNotStarved()
    {
        var atmo = LoadAtmosphere("earth");

        // USSA-76 at 140 km geometric. The old model returned ~7.8e-10 here (−80%) because
        // its layers stopped at 71 km, which left every LEO drag calculation anchored on
        // a density that was orders of magnitude too small.
        double rho = atmo.GetDensity(140_000.0);

        Assert.InRange(rho, 3.0e-9, 4.7e-9);
    }

    // NRLMSISE-00 mean densities (kg/m³) across the LEO band, at geometric altitude.
    [Theory]
    [InlineData(150_000.0, 2.000e-9)]
    [InlineData(200_000.0, 2.541e-10)]
    [InlineData(250_000.0, 6.073e-11)]
    [InlineData(300_000.0, 1.916e-11)]
    [InlineData(400_000.0, 2.803e-12)]
    [InlineData(500_000.0, 5.215e-13)]
    public void Earth_ThermosphereTracksNrlmsise_WithinAFactorOfTwo(double alt, double rhoRef)
    {
        var atmo = LoadAtmosphere("earth");

        // The scale height grows with altitude, so a single exponential cannot span this
        // band — it lands within ~3x at best. The growing-H tail holds ~1.15x.
        double ratio = atmo.GetDensity(alt) / rhoRef;

        Assert.InRange(ratio, 0.5, 2.0);
    }

    [Fact]
    public void ThermosphereGrowthOfZero_CollapsesToAPlainExponential()
    {
        AtmosphereModel Build(double growth) => new()
        {
            MaxAltitude = 100_000.0,
            SeaLevelPressure = 101_325.0,
            SeaLevelTemperature = 288.15,
            MolarMass = 0.0289644,
            ThermosphereScaleHeight = 20_000.0,
            ThermosphereScaleHeightGrowth = growth,
            ThermosphereTopAltitude = 500_000.0,
            Layers = new List<AtmosphereLayer> { new(0.0, 100_000.0, 288.15, 0.0) },
        };

        var flat = Build(0.0);
        double anchor = flat.GetDensity(100_000.0);

        // k = 0 must reproduce exp(-dz/H) exactly.
        double expected = anchor * System.Math.Exp(-50_000.0 / 20_000.0);
        AssertRelative(expected, flat.GetDensity(150_000.0), 1e-9, "k=0 exponential");

        // A positive k must decay more slowly (the scale height is growing).
        Assert.True(Build(0.16).GetDensity(150_000.0) > flat.GetDensity(150_000.0));
    }

    [Fact]
    public void Earth_UpperAtmosphereWarms_ItDoesNotFreeze()
    {
        var atmo = LoadAtmosphere("earth");

        // Real thermosphere: ~187 K at the mesopause, climbing past 500 K by 140 km.
        Assert.InRange(atmo.GetTemperature(86_000.0),  180.0, 195.0);
        Assert.InRange(atmo.GetTemperature(120_000.0), 340.0, 380.0);
        Assert.InRange(atmo.GetTemperature(140_000.0), 520.0, 600.0);

        // Monotonic warming through the thermosphere — never a collapse toward 0 K.
        Assert.True(atmo.GetTemperature(140_000.0) > atmo.GetTemperature(100_000.0));
        Assert.True(atmo.GetTemperature(100_000.0) > atmo.GetTemperature(86_000.0));
    }

    [Theory]
    [InlineData("mars",  0.04401, 3.72076)]
    [InlineData("venus", 0.04401, 8.87)]
    public void CO2Planets_UseTheirOwnGasAndGravity(string id, double molarMass, double gravity)
    {
        var atmo = LoadAtmosphere(id);

        Assert.Equal(molarMass, atmo.MolarMass, 5);
        Assert.Equal(gravity,   atmo.SurfaceGravity, 3);
    }

    [Fact]
    public void Mars_ColumnIsHeldUpByMarsGravity_NotEarths()
    {
        var mars = LoadAtmosphere("mars");

        // With Earth's gravity and Earth's air the hydrostatic exponent nearly doubles and
        // the column collapses: pressure at 50 km came out ~0.06 Pa instead of ~3 Pa.
        double p50 = mars.GetPressure(50_000.0);

        Assert.InRange(p50, 1.5, 6.0);
    }

    [Fact]
    public void SurfaceGravity_ChangesTheColumn()
    {
        // Same gas, same thermal profile — only gravity differs. Heavier gravity must pull
        // the column down harder, leaving less pressure aloft.
        AtmosphereModel Build(double g) => new()
        {
            MaxAltitude = 100_000.0,
            SeaLevelPressure = 100_000.0,
            SeaLevelTemperature = 250.0,
            MolarMass = 0.044,
            SurfaceGravity = g,
            GeopotentialRadius = 3_389_500.0,
            Layers = new List<AtmosphereLayer> { new(0.0, 100_000.0, 250.0, 0.0) },
        };

        double weak   = Build(3.72).GetPressure(20_000.0);
        double strong = Build(9.81).GetPressure(20_000.0);

        Assert.True(strong < weak,
            $"stronger gravity must thin the column faster ({strong:F1} Pa vs {weak:F1} Pa)");
    }

    [Fact]
    public void GeopotentialConversion_ShrinksWithAltitude_AndVanishesAtTheSurface()
    {
        var atmo = LoadAtmosphere("earth");

        Assert.Equal(0.0, atmo.ToGeopotential(0.0), 6);

        // 86 km geometric is the standard's 84 852 m geopotential — the ~1.1 km of offset
        // that the old geometric-altitude lookup silently threw away.
        Assert.Equal(84_852.0, atmo.ToGeopotential(86_000.0), 0);

        Assert.True(atmo.ToGeopotential(50_000.0) < 50_000.0);
    }

    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("venus")]
    public void JsonAndCodePreset_DoNotDrift(string id)
    {
        var json = LoadAtmosphere(id);
        var preset = id switch
        {
            "earth" => AtmosphereModel.Earth(),
            "mars"  => AtmosphereModel.Mars(),
            "venus" => AtmosphereModel.Venus(),
            _       => throw new System.ArgumentOutOfRangeException(nameof(id)),
        };

        // The game flies the JSON; the tests used to exercise the preset. When those two
        // disagree, the tests are validating an atmosphere nobody flies.
        for (double frac = 0.0; frac < 1.0; frac += 0.1)
        {
            double alt = json.MaxAltitude * frac;
            AssertRelative(preset.GetTemperature(alt), json.GetTemperature(alt), 1e-6, $"T at {alt:F0} m");
            AssertRelative(preset.GetDensity(alt),     json.GetDensity(alt),     1e-6, $"rho at {alt:F0} m");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Asserts <paramref name="actual"/> is within a relative <paramref name="fraction"/> of <paramref name="expected"/>.</summary>
    private static void AssertRelative(double expected, double actual, double fraction, string what)
    {
        double error = expected == 0.0
            ? System.Math.Abs(actual)
            : System.Math.Abs(actual - expected) / System.Math.Abs(expected);

        Assert.True(error <= fraction,
            $"{what}: expected {expected:G6}, got {actual:G6} ({error * 100.0:F3}% off, limit {fraction * 100.0:F3}%)");
    }

    private static double GeometricFromGeopotential(double h, double radius) =>
        h <= 0.0 ? 0.0 : radius * h / (radius - h);

    private static AtmosphereModel LoadAtmosphere(string id)
    {
        var body = CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));
        Assert.NotNull(body.Atmosphere);
        return body.Atmosphere!;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found");
    }
}
