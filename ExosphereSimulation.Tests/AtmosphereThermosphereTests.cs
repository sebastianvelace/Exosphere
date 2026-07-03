namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Xunit;

/// <summary>
/// R7 — residual thermosphere above the ISA layers so low LEO decays slowly
/// instead of hitting a hard vacuum cut at <see cref="AtmosphereModel.MaxAltitude"/>.
/// </summary>
public sealed class AtmosphereThermosphereTests
{
    [Fact]
    public void DensityIsPositiveInLowLeoAboveTheIsaLayers()
    {
        var atmo = AtmosphereModel.Earth();

        Assert.True(atmo.GetDensity(150_000.0) > 0.0, "150 km should have residual density");
        Assert.True(atmo.GetDensity(200_000.0) > 0.0, "200 km should have residual density");
        Assert.True(atmo.GetDensity(400_000.0) > 0.0, "400 km should have residual density");
    }

    [Fact]
    public void DensityDecaysMonotonicallyThroughTheThermosphere()
    {
        var atmo = AtmosphereModel.Earth();

        double d100 = atmo.GetDensity(100_000.0);
        double d150 = atmo.GetDensity(150_000.0);
        double d200 = atmo.GetDensity(200_000.0);
        double d400 = atmo.GetDensity(400_000.0);

        Assert.True(d100 > d150, "density must fall crossing the ISA/thermosphere boundary");
        Assert.True(d150 > d200, "density must fall from 150 to 200 km");
        Assert.True(d200 > d400, "density must fall from 200 to 400 km");
    }

    [Fact]
    public void DensityIsContinuousAtTheBoundary()
    {
        // Use the JSON-loaded model — its ISA layers reach the 140 km boundary,
        // which is the profile that actually runs in-game.
        var atmo = LoadBody("earth").Atmosphere!;

        double justBelow = atmo.GetDensity(atmo.MaxAltitude - 1.0);
        double atBoundary = atmo.GetDensity(atmo.MaxAltitude);

        // The tail is anchored at the boundary density, so there is no jump.
        double scale = System.Math.Max(justBelow, atBoundary);
        Assert.True(System.Math.Abs(justBelow - atBoundary) <= scale * 1e-3,
            $"boundary density jumped: {justBelow:R} vs {atBoundary:R}");
    }

    [Fact]
    public void DensityIsVacuumAboveTheThermosphereTop()
    {
        var atmo = AtmosphereModel.Earth();

        Assert.Equal(0.0, atmo.GetDensity(atmo.ThermosphereTopAltitude));
        Assert.Equal(0.0, atmo.GetDensity(atmo.ThermosphereTopAltitude + 100_000.0));
    }

    [Fact]
    public void TailIsDisabledWhenScaleHeightIsZero()
    {
        var atmo = new AtmosphereModel
        {
            MaxAltitude = 140_000.0,
            Layers = AtmosphereModel.Earth().Layers,
            // ThermosphereScaleHeight defaults to 0 → disabled.
        };

        Assert.Equal(0.0, atmo.GetDensity(150_000.0));
        Assert.Equal(0.0, atmo.GetDensity(200_000.0));
    }

    [Fact]
    public void PressureStaysVacuumAboveMaxAltitude()
    {
        // MaxAltitude remains the aerodynamically significant boundary that the
        // flight controllers reason about; only density gets a residual tail.
        var atmo = AtmosphereModel.Earth();

        Assert.Equal(0.0, atmo.GetPressure(150_000.0));
        Assert.Equal(0.0, atmo.GetPressure(200_000.0));
    }

    [Fact]
    public void EarthJsonEnablesTheThermosphere()
    {
        var earth = LoadBody("earth");
        Assert.NotNull(earth.Atmosphere);

        Assert.True(earth.Atmosphere!.ThermosphereScaleHeight > 0.0);
        Assert.True(earth.Atmosphere.GetDensity(200_000.0) > 0.0,
            "earth.json should produce residual LEO density");
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

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

        throw new System.InvalidOperationException("Could not locate repository root.");
    }
}
