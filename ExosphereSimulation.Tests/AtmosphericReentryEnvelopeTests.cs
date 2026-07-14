namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Pure atmosphere-to-reentry contracts. These tests cover the continuous profile consumed by
/// drag/heating rather than a renderer or a seeded Godot scenario, so a visually plausible frame
/// cannot hide a discontinuity or an unphysical thermosphere slope.
/// </summary>
public sealed class AtmosphericReentryEnvelopeTests
{
    [Fact]
    public void EarthDensityIsFiniteNonNegativeAndStrictlyDecreasingToTheThermosphereTop()
    {
        var atmosphere = LoadEarthAtmosphere();
        double previous = atmosphere.GetDensity(0.0);

        Assert.True(double.IsFinite(previous) && previous > 0.0);
        for (double altitude = 1_000.0;
             altitude < atmosphere.ThermosphereTopAltitude;
             altitude += 1_000.0)
        {
            double density = atmosphere.GetDensity(altitude);
            Assert.True(double.IsFinite(density) && density > 0.0,
                $"density must remain finite and positive below the thermosphere top at {altitude / 1000.0:F0} km");
            Assert.True(density < previous,
                $"density must decrease with altitude at {altitude / 1000.0:F0} km ({density:R} >= {previous:R})");
            previous = density;
        }

        Assert.Equal(0.0, atmosphere.GetDensity(atmosphere.ThermosphereTopAltitude));
    }

    [Fact]
    public void EveryEarthIsaLayerInterfaceIsContinuous()
    {
        var atmosphere = LoadEarthAtmosphere();

        // AltMin/AltMax are geopotential metres. Public queries take geometric metres.
        foreach (var boundary in atmosphere.Layers.Select(layer => layer.AltMax).SkipLast(1))
        {
            double geometric = GeometricFromGeopotential(boundary, atmosphere.GeopotentialRadius);
            const double epsilonM = 0.01;

            AssertContinuous(
                atmosphere.GetTemperature(geometric - epsilonM),
                atmosphere.GetTemperature(geometric + epsilonM),
                1e-3,
                $"temperature at {boundary:F0} geopotential m");
            AssertContinuous(
                atmosphere.GetPressure(geometric - epsilonM),
                atmosphere.GetPressure(geometric + epsilonM),
                2e-5,
                $"pressure at {boundary:F0} geopotential m");
            AssertContinuous(
                atmosphere.GetDensity(geometric - epsilonM),
                atmosphere.GetDensity(geometric + epsilonM),
                1e-3,
                $"density at {boundary:F0} geopotential m");
        }
    }

    [Theory]
    [InlineData(200_000.0)]
    [InlineData(400_000.0)]
    [InlineData(700_000.0)]
    public void ThermosphereLogDensitySlopeMatchesDeclaredGrowingScaleHeight(double altitude)
    {
        var atmosphere = LoadEarthAtmosphere();
        const double halfWindowM = 100.0;

        double below = atmosphere.GetDensity(altitude - halfWindowM);
        double above = atmosphere.GetDensity(altitude + halfWindowM);
        double measuredScaleHeight = -(2.0 * halfWindowM) / System.Math.Log(above / below);
        double declaredScaleHeight = atmosphere.ThermosphereScaleHeight
            + atmosphere.ThermosphereScaleHeightGrowth * (altitude - atmosphere.MaxAltitude);

        AssertRelative(declaredScaleHeight, measuredScaleHeight, 2e-5,
            $"effective scale height at {altitude / 1000.0:F0} km");
    }

    [Fact]
    public void OrbitalSpeedHeatingIsWeakInLowLeoAndRisesIntoTheVisibleEntryBand()
    {
        var atmosphere = LoadEarthAtmosphere();
        const double orbitalEntrySpeed = 7_600.0;
        const double starshipNoseRadius = 4.5;

        double flux200 = ThermalModel.ComputeHeatFlux(
            atmosphere.GetDensity(200_000.0), orbitalEntrySpeed, starshipNoseRadius);
        double flux120 = ThermalModel.ComputeHeatFlux(
            atmosphere.GetDensity(120_000.0), orbitalEntrySpeed, starshipNoseRadius);
        double flux80 = ThermalModel.ComputeHeatFlux(
            atmosphere.GetDensity(80_000.0), orbitalEntrySpeed, starshipNoseRadius);

        Assert.True(flux200 < flux120 && flux120 < flux80,
            $"entry heating must grow continuously with the atmospheric column: {flux200:F0}, {flux120:F0}, {flux80:F0} W/m²");
        Assert.InRange(flux200, 50.0, 2_000.0);
        Assert.True(flux80 > 50_000.0,
            $"an orbital-speed vehicle at 80 km must be in a visibly hot entry regime, got {flux80:F0} W/m²");
    }

    [Fact]
    public void SlowVehicleAtTwoHundredKilometresCannotGenerateFreshReentryPlasma()
    {
        var atmosphere = LoadEarthAtmosphere();

        // Matches the order of magnitude in the reported post-skip CRASHED frame (~130 km/h).
        // Any failure there must be residual thermal state/damage, not new convective heating.
        double flux = ThermalModel.ComputeHeatFlux(
            atmosphere.GetDensity(200_000.0), 130.0 / 3.6, noseRadius: 4.5);

        Assert.InRange(flux, 0.0, 0.001);
    }

    private static AtmosphereModel LoadEarthAtmosphere()
    {
        var body = CelestialBody.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "bodies", "earth.json"));
        Assert.NotNull(body.Atmosphere);
        return body.Atmosphere!;
    }

    private static double GeometricFromGeopotential(double altitude, double radius) =>
        radius * altitude / (radius - altitude);

    private static void AssertContinuous(double left, double right, double relativeTolerance,
        string quantity)
    {
        double scale = System.Math.Max(System.Math.Abs(left), System.Math.Abs(right));
        double relative = scale > 0.0 ? System.Math.Abs(left - right) / scale : 0.0;
        Assert.True(relative <= relativeTolerance,
            $"{quantity} is discontinuous: {left:R} vs {right:R} ({relative:E3})");
    }

    private static void AssertRelative(double expected, double actual, double tolerance,
        string quantity)
    {
        double relative = System.Math.Abs(actual - expected) / System.Math.Abs(expected);
        Assert.True(relative <= tolerance,
            $"{quantity}: expected {expected:R}, got {actual:R} ({relative:E3})");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
