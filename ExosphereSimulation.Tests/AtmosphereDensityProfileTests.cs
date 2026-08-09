namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereDensityProfileTests
{
    [Fact]
    public void CapturesThermosphereDomainWithoutChangingTheAerodynamicBoundary()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);

        Assert.Equal(atmosphere.ThermosphereTopAltitude, profile.AtmosphereTopAltitude);
        Assert.Equal(profile.AtmosphereTopAltitude, profile.TopAltitude);
        Assert.Equal(atmosphere.MaxAltitude, atmosphere.MaxAltitude);
        Assert.True(profile.AtmosphereTopAltitude > atmosphere.MaxAltitude);
    }

    [Fact]
    public void SurfaceStateIsNormalisedAndFinite()
    {
        var profile = AtmosphereDensityProfile.Create(AtmosphereModel.Earth());
        var surface = profile.Sample(0.0);

        Assert.InRange(surface.X, 0.999, 1.001);
        Assert.InRange(surface.Y, 0.999, 1.001);
        Assert.Equal(0.0, surface.Z, 12);
        AssertFiniteAndBounded(surface);
    }

    [Fact]
    public void RayleighChannelUsesThermodynamicPressureOverTemperature()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);
        const double altitude = 25_000.0;

        double seaNumber = atmosphere.GetPressure(0.0) / atmosphere.GetTemperature(0.0);
        double expected = (atmosphere.GetPressure(altitude) / atmosphere.GetTemperature(altitude))
            / seaNumber;

        Assert.InRange(profile.Sample(altitude).X, expected - 1e-12, expected + 1e-12);
    }

    [Fact]
    public void MieAndOzoneChannelsFollowTheirConfiguredSpeciesProfiles()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);

        var atOzonePeak = profile.Sample(atmosphere.Optics.OzoneCenterAltitude);
        var aboveOzone = profile.Sample(atmosphere.Optics.OzoneCenterAltitude
            + atmosphere.Optics.OzoneHalfWidth);

        Assert.InRange(atOzonePeak.Z, 0.999, 1.001);
        Assert.Equal(0.0, aboveOzone.Z, 12);
        Assert.True(profile.Sample(10_000.0).Y < profile.Sample(0.0).Y);
    }

    [Fact]
    public void ThermosphereTailRemainsFiniteUntilItsConfiguredTop()
    {
        var profile = new AtmosphereDensityProfile(AtmosphereModel.Earth());

        var boundary = profile.Sample(140_000.0);
        var leo = profile.Sample(400_000.0);
        var top = profile.Sample(profile.AtmosphereTopAltitude);

        Assert.True(boundary.X > 0.0 && boundary.Y > 0.0);
        Assert.True(leo.X > 0.0 && leo.X < boundary.X);
        Assert.True(leo.Y > 0.0 && leo.Y < boundary.Y);
        Assert.Equal(Vector3d.Zero, top);
        Assert.Equal(Vector3d.Zero, profile.Sample(profile.AtmosphereTopAltitude + 1.0));
    }

    [Fact]
    public void InvalidAndOutOfDomainAltitudesAreVacuum()
    {
        var profile = new AtmosphereDensityProfile(AtmosphereModel.Earth());

        Assert.Equal(Vector3d.Zero, profile.Sample(-1.0));
        Assert.Equal(Vector3d.Zero, profile.Sample(double.NaN));
        Assert.Equal(Vector3d.Zero, profile.Sample(double.PositiveInfinity));
        Assert.Equal(0.0, profile.Refractivity(-1.0));
        Assert.Equal(0.0, profile.Refractivity(double.NaN));
    }

    [Fact]
    public void NoLayerAtmospheresUseTheirExponentialMassFallback()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 20_000.0,
            SeaLevelDensity = 1.0,
            SeaLevelPressure = 80_000.0,
            SeaLevelTemperature = 280.0,
            ScaleHeight = 7_500.0,
            Layers = new List<AtmosphereLayer>(),
        };
        var profile = new AtmosphereDensityProfile(atmosphere);

        Assert.Equal(atmosphere.MaxAltitude, profile.AtmosphereTopAltitude);
        Assert.InRange(profile.Sample(0.0).X, 0.999, 1.001);
        Assert.InRange(profile.Sample(10_000.0).X, 0.20, 0.35);
        Assert.Equal(Vector3d.Zero, profile.Sample(atmosphere.MaxAltitude));
    }

    [Fact]
    public void RefractivityTracksTheMolecularChannel()
    {
        var profile = new AtmosphereDensityProfile(AtmosphereModel.Earth());
        double surface = profile.Refractivity(0.0);
        double high = profile.Refractivity(80_000.0);

        Assert.InRange(surface, 2.76e-4, 2.78e-4);
        Assert.True(high > 0.0 && high < surface);
        Assert.Equal(0.0, profile.Refractivity(profile.AtmosphereTopAltitude), 12);
    }

    private static void AssertFiniteAndBounded(Vector3d value)
    {
        Assert.True(double.IsFinite(value.X) && double.IsFinite(value.Y)
            && double.IsFinite(value.Z));
        Assert.InRange(value.X, 0.0, 1.0);
        Assert.InRange(value.Y, 0.0, 1.0);
        Assert.InRange(value.Z, 0.0, 1.0);
    }
}
