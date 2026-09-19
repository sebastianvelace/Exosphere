namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Xunit;

/// <summary>
/// Data contracts for every atmosphere the simulator can fly, enforced against the JSON the
/// game actually loads rather than against a hand-built model.
///
/// <para><see cref="AtmosphereModel.FromJson"/> substitutes EARTH values for a missing
/// <c>molar_mass</c>, <c>surface_gravity</c> or <c>geopotential_radius</c>. That default is
/// invisible: the body loads, the column integrates, and nothing reports that a hydrogen
/// planet is being held up by Earth gravity and made of Earth air. Jupiter and Saturn were
/// in exactly that state — their scale height came out 4.8 km instead of 24.5 km (a factor
/// 5.1) and their surface density 2.11 kg/m³ instead of 0.162 kg/m³ (a factor 13), because
/// g·M was Earth's. These tests read the raw JSON so the default cannot hide the omission.</para>
///
/// <para>The second class of defect is a layer stack that stops being physical before the
/// boundary the same file declares. Venus ran a single 0 → 250 km layer at −0.0075 K/m,
/// which drives the temperature through 0 K at ~99.7 km geometric; above that
/// <see cref="AtmosphereModel.GetDensity"/> and <see cref="AtmosphereModel.GetPressure"/>
/// returned exactly zero for the top 150 km of a declared atmosphere. Jupiter and Saturn
/// carried a ~200 K and ~91 K temperature STEP at their layer boundary, which the
/// hydrostatic walk-up turns into a density discontinuity.</para>
/// </summary>
public sealed class AtmosphereDataContractTests
{
    /// <summary>Universal gas constant, J/(mol·K) — the value AtmosphereModel integrates with.</summary>
    private const double UniversalGasConstant = 8.31446;

    public static TheoryData<string> AtmosphericBodies
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string id in LayeredAtmosphereBodyIds())
                data.Add(id);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void EveryLayeredAtmosphereDeclaresItsOwnGasGravityAndGeopotentialRadius(string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(BodyPath(id)));
        var atmosphere = document.RootElement.GetProperty("atmosphere");

        foreach (string field in new[]
                 {
                     "molar_mass",
                     "surface_gravity",
                     "geopotential_radius",
                 })
        {
            Assert.True(
                atmosphere.TryGetProperty(field, out var value)
                    && value.ValueKind == JsonValueKind.Number
                    && double.IsFinite(value.GetDouble())
                    && value.GetDouble() > 0.0,
                $"{id}: atmosphere must declare a positive '{field}'. Omitting it makes "
                + "AtmosphereModel.FromJson silently substitute the Earth value, so the "
                + "column is integrated with the wrong gas or the wrong gravity.");
        }
    }

    /// <summary>
    /// The layer table must remain thermodynamically valid all the way to the boundary the
    /// body declares as its atmosphere. A layer stack that drives T to 0 K early makes both
    /// density and pressure identically zero above that point, which silently deletes drag
    /// and heating over the remainder of the declared envelope.
    /// </summary>
    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void EveryLayerStackStaysPhysicalUpToItsDeclaredBoundary(string id)
    {
        var atmosphere = LoadAtmosphere(id);

        foreach (double altitude in SampleAltitudes(atmosphere.MaxAltitude))
        {
            double temperature = atmosphere.GetTemperature(altitude);
            double density = atmosphere.GetDensity(altitude);
            double pressure = atmosphere.GetPressure(altitude);

            Assert.True(temperature > 1.0,
                $"{id}: temperature collapsed to {temperature:G6} K at {altitude / 1000.0:F1} km, "
                + $"below the declared {atmosphere.MaxAltitude / 1000.0:F0} km boundary.");
            Assert.True(density > 0.0 && double.IsFinite(density),
                $"{id}: density is {density:G6} kg/m³ at {altitude / 1000.0:F1} km, "
                + $"below the declared {atmosphere.MaxAltitude / 1000.0:F0} km boundary.");
            Assert.True(pressure > 0.0 && double.IsFinite(pressure),
                $"{id}: pressure is {pressure:G6} Pa at {altitude / 1000.0:F1} km, "
                + $"below the declared {atmosphere.MaxAltitude / 1000.0:F0} km boundary.");
        }
    }

    /// <summary>
    /// Density must fall monotonically through the declared atmosphere. A temperature step
    /// at a layer boundary, or a layer written out of order, shows up here as a column that
    /// gets denser with altitude.
    /// </summary>
    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void DensityFallsMonotonicallyThroughTheDeclaredAtmosphere(string id)
    {
        var atmosphere = LoadAtmosphere(id);
        double previousDensity = double.PositiveInfinity;
        double previousAltitude = double.NaN;

        foreach (double altitude in SampleAltitudes(atmosphere.MaxAltitude))
        {
            double density = atmosphere.GetDensity(altitude);
            Assert.True(density < previousDensity,
                $"{id}: density rose from {previousDensity:G6} kg/m³ at "
                + $"{previousAltitude / 1000.0:F1} km to {density:G6} kg/m³ at "
                + $"{altitude / 1000.0:F1} km.");
            previousDensity = density;
            previousAltitude = altitude;
        }
    }

    /// <summary>
    /// Temperature and density must be continuous across every layer boundary. The
    /// hydrostatic walk-up carries pressure across the seam, so a temperature step there
    /// becomes a density step of the same ratio — Jupiter's 165 → 65 K layer followed by a
    /// 265 K base was a factor-4 density cliff at 50 km.
    /// </summary>
    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void LayerBoundariesAreContinuousInTemperatureAndDensity(string id)
    {
        var atmosphere = LoadAtmosphere(id);
        Assert.NotEmpty(atmosphere.Layers);

        for (int i = 0; i < atmosphere.Layers.Count - 1; i++)
        {
            var lower = atmosphere.Layers[i];
            var upper = atmosphere.Layers[i + 1];

            Assert.Equal(lower.AltMax, upper.AltMin, 6);

            double topOfLower = lower.TempBase
                + lower.LapseRate * (lower.AltMax - lower.AltMin);
            // 0.1 K absorbs the rounding in published layer tables — USSA-76 itself closes
            // its 71 km layer at 186.946 K and tabulates the next base as 186.87 K.
            Assert.True(System.Math.Abs(topOfLower - upper.TempBase) <= 0.1,
                $"{id}: layer {i} ends at {topOfLower:F3} K but layer {i + 1} starts at "
                + $"{upper.TempBase:F3} K ({System.Math.Abs(topOfLower - upper.TempBase):F3} K step).");
        }

        // Geopotential seams map to geometric altitudes; probe each seam from both sides in
        // the coordinate the public API actually takes.
        foreach (var layer in atmosphere.Layers.Take(atmosphere.Layers.Count - 1))
        {
            double seam = GeometricFromGeopotential(
                layer.AltMax, atmosphere.GeopotentialRadius);
            if (seam <= 0.0 || seam >= atmosphere.MaxAltitude) continue;

            double below = atmosphere.GetDensity(seam - 1.0);
            double above = atmosphere.GetDensity(seam + 1.0);
            Assert.True(below > 0.0 && above > 0.0);
            Assert.True(System.Math.Abs(above / below - 1.0) < 0.01,
                $"{id}: density jumps {100.0 * (above / below - 1.0):F2}% across the seam at "
                + $"{seam / 1000.0:F1} km.");
        }
    }

    /// <summary>
    /// Inside the declared boundary the three state variables must satisfy the ideal gas law
    /// the model claims to use, ρ = p·M/(R·T), with the body's own molar mass.
    ///
    /// <para>This is deliberately scoped to below <see cref="AtmosphereModel.MaxAltitude"/>.
    /// Above it Earth keeps a residual density tail while <c>GetPressure</c> returns exactly
    /// zero, so ρ &gt; 0 coexists with p = 0. That inconsistency is a known open item, not
    /// something this contract should assert away.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void DensityMatchesTheIdealGasLawInsideTheDeclaredBoundary(string id)
    {
        var atmosphere = LoadAtmosphere(id);

        foreach (double altitude in SampleAltitudes(atmosphere.MaxAltitude))
        {
            double temperature = atmosphere.GetTemperature(altitude);
            double pressure = atmosphere.GetPressure(altitude);
            double expected = pressure * atmosphere.MolarMass
                / (UniversalGasConstant * temperature);
            double actual = atmosphere.GetDensity(altitude);

            Assert.True(System.Math.Abs(actual - expected) <= 1e-9 * expected,
                $"{id}: at {altitude / 1000.0:F1} km, ρ = {actual:G8} but p·M/(R·T) = "
                + $"{expected:G8} ({100.0 * (actual / expected - 1.0):F4}% off).");
        }
    }

    /// <summary>
    /// The declared scale height must be the one the body's own gas and gravity produce at
    /// the surface, H = R·T/(g·M). Jupiter declared 27 km while its Earth-defaulted gas and
    /// gravity delivered 4.8 km, so the declared value was documentation of an atmosphere
    /// the simulator was not flying.
    /// </summary>
    [Theory]
    [MemberData(nameof(AtmosphericBodies))]
    public void DeclaredScaleHeightMatchesTheBodysOwnGasAndGravity(string id)
    {
        var atmosphere = LoadAtmosphere(id);
        double expected = UniversalGasConstant * atmosphere.SeaLevelTemperature
            / (atmosphere.SurfaceGravity * atmosphere.MolarMass);

        Assert.True(System.Math.Abs(atmosphere.ScaleHeight / expected - 1.0) <= 0.10,
            $"{id}: declared scale_height {atmosphere.ScaleHeight / 1000.0:F1} km but "
            + $"R·T/(g·M) = {expected / 1000.0:F1} km "
            + $"({100.0 * (atmosphere.ScaleHeight / expected - 1.0):F1}% off).");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// 41 samples from the surface to just inside the declared boundary. Deterministic and
    /// dense enough to land inside every layer of every current body.
    /// </summary>
    private static IEnumerable<double> SampleAltitudes(double maxAltitude)
    {
        const int steps = 40;
        for (int i = 0; i <= steps; i++)
            yield return maxAltitude * i / (steps + 1.0);
    }

    private static double GeometricFromGeopotential(double h, double radius) =>
        h <= 0.0 ? 0.0 : radius * h / (radius - h);

    private static IEnumerable<string> LayeredAtmosphereBodyIds()
    {
        foreach (string path in Directory
                     .GetFiles(Path.Combine(RepoRoot(), "data", "bodies"), "*.json")
                     .Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("has_atmosphere", out var flag) || !flag.GetBoolean())
                continue;
            if (!root.TryGetProperty("atmosphere", out var atmosphere)
                || atmosphere.ValueKind != JsonValueKind.Object)
                continue;
            if (!atmosphere.TryGetProperty("layers", out var layers)
                || layers.ValueKind != JsonValueKind.Array
                || layers.GetArrayLength() == 0)
                continue;
            yield return root.GetProperty("id").GetString()!;
        }
    }

    private static string BodyPath(string id) =>
        Path.Combine(RepoRoot(), "data", "bodies", $"{id}.json");

    private static AtmosphereModel LoadAtmosphere(string id)
    {
        var body = CelestialBody.LoadFromJson(BodyPath(id));
        Assert.NotNull(body.Atmosphere);
        return body.Atmosphere!;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root.");
    }
}
