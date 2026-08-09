namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereAngularMultipleScatteringLutTests
{
    [Fact]
    public void AngularAtlasRejectsGroundwardViews()
    {
        var lut = BuildEarthAtlas();

        Assert.Equal(Vector3d.Zero, lut.Sample(0.0, 1.0, -0.25, 0.0));
        Assert.Equal(Vector3d.Zero, lut.Sample(0.0, 1.0, 0.0, 0.0));
        var zenith = lut.Sample(0.0, 1.0, 1.0, 0.0);
        Assert.True(zenith.X > 0.0 && zenith.Y > 0.0 && zenith.Z > 0.0);
    }

    [Fact]
    public void AngularAtlasPreservesForwardMieLobe()
    {
        var lut = BuildEarthAtlas();
        var forward = lut.Sample(0.0, 1.0, 0.8, 1.0);
        var backward = lut.Sample(0.0, 1.0, 0.8, -1.0);

        Assert.True(forward.X > backward.X,
            $"forward Mie lobe was lost: forward={forward}, backward={backward}");
        Assert.True(forward.Y > backward.Y);
        Assert.True(forward.Z > backward.Z);
    }

    [Fact]
    public void AngularAtlasSuppressesLongGrazingEscapePaths()
    {
        var lut = BuildEarthAtlas();
        var zenith = lut.Sample(0.0, 1.0, 1.0, 0.0);
        var horizon = lut.Sample(0.0, 1.0, 0.08, 0.0);

        Assert.True(horizon.X < zenith.X && horizon.Y < zenith.Y && horizon.Z < zenith.Z,
            $"spherical escape ratio did not dim the limb: zenith={zenith}, horizon={horizon}");
    }

    [Fact]
    public void AngularAtlasRetainsTheRefractedNearHorizonSource()
    {
        var lut = BuildEarthAtlas();
        var nearHorizon = lut.Sample(0.0, -0.01, 1.0, 0.0);
        var deepNight = lut.Sample(0.0, -0.08, 1.0, 0.0);

        Assert.True(nearHorizon.X > 0.0 && nearHorizon.Y > 0.0 && nearHorizon.Z > 0.0);
        Assert.Equal(Vector3d.Zero, deepNight);
    }

    [Fact]
    public void AngularAtlasTexelsRemainFiniteAndNonNegative()
    {
        var lut = BuildEarthAtlas();
        for (int mu = 0; mu < lut.MuWidth; mu++)
        for (int view = 0; view < lut.ViewHeight; view++)
        for (int solar = 0; solar < lut.SolarHeight; solar++)
        for (int altitude = 0; altitude < lut.Width; altitude++)
        {
            var value = lut.GetTexel(altitude, solar, view, mu);
            Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y)
                && double.IsFinite(value.Z));
            Assert.True(value.X >= 0.0 && value.Y >= 0.0 && value.Z >= 0.0);
        }
    }

    private static AtmosphereAngularMultipleScatteringLut BuildEarthAtlas()
    {
        var body = CelestialBody.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "bodies", "earth.json"));
        var optics = body.Atmosphere!.Optics;
        var seed = AtmosphereMultipleScatteringLut.Build(
            optics, body.Radius, body.Atmosphere.MaxAltitude,
            width: 12, height: 8, integrationSteps: 10, solarSampleCount: 12);
        return AtmosphereAngularMultipleScatteringLut.Build(
            optics, seed, body.Radius, body.Atmosphere.MaxAltitude,
            width: 8, solarHeight: 8, viewHeight: 8, muWidth: 8,
            opticalDepthSamples: 16);
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
