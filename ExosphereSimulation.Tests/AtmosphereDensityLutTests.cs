namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereDensityLutTests
{
    [Fact]
    public void TableUsesTheConfiguredThermosphereTopAndHasOneRow()
    {
        var atmosphere = AtmosphereModel.Earth();
        var lut = AtmosphereDensityLut.Build(atmosphere, width: 48);

        Assert.Equal(48, lut.Width);
        Assert.Equal(1, lut.Height);
        Assert.Equal(atmosphere.ThermosphereTopAltitude, lut.AtmosphereTopAltitude);
        Assert.Equal(Vector3d.Zero, lut.GetTexel(lut.Width - 1));
        Assert.Equal(lut.GetTexel(7), lut.GetTexel(7, 0));
    }

    [Fact]
    public void SamplesAreFiniteAndNormalisedAtTheSurface()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 64);
        var surface = lut.Sample(0.0);

        Assert.InRange(surface.X, 0.999, 1.001);
        Assert.InRange(surface.Y, 0.999, 1.001);
        Assert.Equal(0.0, surface.Z, 12);

        for (int i = 0; i < lut.Width; i++)
        {
            var value = lut.GetTexel(i);
            Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y)
                && double.IsFinite(value.Z), $"non-finite texel {i}: {value}");
            Assert.True(value.X >= 0.0 && value.Y >= 0.0 && value.Z >= 0.0,
                $"negative texel {i}: {value}");
            Assert.True(value.X <= 1.0 && value.Y <= 1.0 && value.Z <= 1.0,
                $"unnormalised texel {i}: {value}");
        }
    }

    [Fact]
    public void MolecularAndAerosolChannelsFallMonotonicallyThroughValidEarthProfile()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 96);
        var previous = lut.Sample(0.0);

        // Sampling in physical altitude (rather than texel order) also exercises the
        // inverse square-root warp used by the renderer.
        for (int i = 1; i <= 40; i++)
        {
            double altitude = 100_000.0 * i / 40.0;
            var current = lut.Sample(altitude);
            Assert.True(current.X <= previous.X + 1e-12,
                $"Rayleigh increased at {altitude}: {previous} -> {current}");
            Assert.True(current.Y <= previous.Y + 1e-12,
                $"Mie increased at {altitude}: {previous} -> {current}");
            previous = current;
        }
    }

    [Fact]
    public void ThermosphereTailRemainsRepresentedUntilItsConfiguredTop()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 96);

        var atBoundary = lut.Sample(140_000.0);
        var leo = lut.Sample(400_000.0);
        var top = lut.Sample(lut.AtmosphereTopAltitude);

        Assert.True(atBoundary.X > 0.0 && atBoundary.Y > 0.0,
            $"density vanished at the ISA boundary: {atBoundary}");
        Assert.True(leo.X > 0.0 && leo.X < atBoundary.X,
            $"Rayleigh tail was not represented: boundary={atBoundary}, leo={leo}");
        Assert.True(leo.Y > 0.0 && leo.Y < atBoundary.Y,
            $"Mie tail was not represented: boundary={atBoundary}, leo={leo}");
        Assert.Equal(Vector3d.Zero, top);
    }

    [Fact]
    public void OzoneChannelPeaksNearTheConfiguredLayerCenter()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 128);
        double below = lut.Sample(10_000.0).Z;
        double center = lut.Sample(25_000.0).Z;
        double above = lut.Sample(40_000.0).Z;

        Assert.True(center > below, $"ozone did not rise into the layer: {below} -> {center}");
        Assert.True(center > above, $"ozone did not fall above the layer: {center} -> {above}");
        Assert.InRange(center, 0.95, 1.0);
    }

    [Fact]
    public void PhysicalTexelCoordinatesRoundTripThroughSample()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 32);

        foreach (int index in new[] { 0, 1, 7, 15, 31 })
        {
            double altitude = lut.AtmosphereTopAltitude
                * AtmosphereDensityLut.CoordinateValue(index, lut.Width);
            var expected = lut.GetTexel(index);
            var actual = lut.Sample(altitude);

            Assert.InRange(actual.X, expected.X - 1e-12, expected.X + 1e-12);
            Assert.InRange(actual.Y, expected.Y - 1e-12, expected.Y + 1e-12);
            Assert.InRange(actual.Z, expected.Z - 1e-12, expected.Z + 1e-12);
        }
    }

    [Fact]
    public void LutTexelsUseTheSameProfileAsCpuTransport()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 64);

        Assert.Same(lut.Atmosphere, lut.Profile.Atmosphere);
        foreach (int index in new[] { 0, 3, 17, 41, 63 })
        {
            double altitude = lut.AtmosphereTopAltitude
                * AtmosphereDensityLut.CoordinateValue(index, lut.Width);
            var expected = lut.Profile.Sample(altitude);
            var actual = lut.GetTexel(index);

            Assert.Equal(expected.X, actual.X, 12);
            Assert.Equal(expected.Y, actual.Y, 12);
            Assert.Equal(expected.Z, actual.Z, 12);
        }
    }

    [Fact]
    public void OutsideTheAtmosphericDomainIsVacuum()
    {
        var lut = AtmosphereDensityLut.Build(AtmosphereModel.Earth(), width: 32);

        Assert.Equal(Vector3d.Zero, lut.Sample(-1.0));
        Assert.Equal(Vector3d.Zero, lut.Sample(double.NaN));
        Assert.Equal(Vector3d.Zero, lut.Sample(double.PositiveInfinity));
        Assert.Equal(Vector3d.Zero, lut.Sample(lut.AtmosphereTopAltitude + 1.0));
    }

    [Fact]
    public void PlanetaryProfilesRemainFiniteAndUseTheirOwnAtmosphericCeilings()
    {
        foreach (var atmosphere in new[]
        {
            AtmosphereModel.Earth(),
            AtmosphereModel.Mars(),
            AtmosphereModel.Venus(),
        })
        {
            var lut = AtmosphereDensityLut.Build(atmosphere, width: 48);

            double expectedTop = atmosphere.ThermosphereScaleHeight > 0.0
                ? atmosphere.ThermosphereTopAltitude
                : atmosphere.MaxAltitude;
            Assert.Equal(expectedTop, lut.AtmosphereTopAltitude);
            var surface = lut.Sample(0.0);
            Assert.InRange(surface.X, 0.999, 1.001);
            Assert.InRange(surface.Y, 0.999, 1.001);
            Assert.True(surface.Z >= 0.0 && surface.Z <= 1.0);

            for (int i = 0; i < lut.Width; i++)
            {
                var value = lut.GetTexel(i);
                Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y)
                    && double.IsFinite(value.Z), $"non-finite texel {i}: {value}");
                Assert.True(value.X >= 0.0 && value.Y >= 0.0 && value.Z >= 0.0,
                    $"negative texel {i}: {value}");
            }
        }
    }

    [Fact]
    public void ExponentialProfilesWithoutLayersStillProduceAValidTable()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 20_000.0,
            SeaLevelDensity = 1.0,
            SeaLevelPressure = 80_000.0,
            SeaLevelTemperature = 280.0,
            ScaleHeight = 7_500.0,
            ThermosphereScaleHeight = 0.0,
            ThermosphereTopAltitude = 0.0,
            Layers = new List<AtmosphereLayer>(),
        };

        var lut = AtmosphereDensityLut.Build(atmosphere, width: 32);

        Assert.Equal(atmosphere.MaxAltitude, lut.AtmosphereTopAltitude);
        Assert.InRange(lut.Sample(0.0).X, 0.999, 1.001);
        Assert.InRange(lut.Sample(10_000.0).X, 0.20, 0.35);
        Assert.Equal(Vector3d.Zero, lut.Sample(atmosphere.MaxAltitude));
    }
}
