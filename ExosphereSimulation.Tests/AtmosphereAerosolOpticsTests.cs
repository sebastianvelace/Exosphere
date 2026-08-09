namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Xunit;

public sealed class AtmosphereAerosolOpticsTests
{
    [Fact]
    public void NullClimateUsesTheLegacyBindingExactly()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 140_000.0,
            Optics = AtmosphereModel.Earth().Optics,
        };
        var binding = AtmosphereAerosolOptics.Resolve(atmosphere, 25.0, 0.0, 0.0);

        Assert.Equal(AtmosphereAerosolOptics.Legacy, binding);
        Assert.False(binding.Enabled);
        Assert.Equal(1.0, binding.MieScale);
    }

    [Fact]
    public void AodIsNormalisedAgainstTheConfiguredVisibleMieColumn()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 140_000.0,
            Optics = AtmosphereModel.Earth().Optics,
            AerosolClimate = new AerosolClimateState
            {
                Aod550 = 0.08,
                LatitudeModulation = 0.0,
                TemporalModulation = 0.0,
                SeasonalModulation = 0.0,
            },
        };

        var binding = AtmosphereAerosolOptics.Resolve(atmosphere, 0.0, 0.0, 0.0);
        double baseline = atmosphere.Optics.MieExtinction.Y * atmosphere.Optics.MieScaleHeight;

        Assert.True(binding.Enabled);
        Assert.InRange(binding.MieScale, 0.08 / baseline - 1e-9, 0.08 / baseline + 1e-9);
        Assert.Equal(0.08, binding.OpticalDepth550, 12);
        Assert.Equal(1.0, binding.AltitudeFactor, 12);
    }

    [Fact]
    public void ClimateFactorsChangeTheBindingButRemainFinite()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 140_000.0,
            Optics = AtmosphereModel.Earth().Optics,
            AerosolClimate = new AerosolClimateState
            {
                Aod550 = 0.05,
                LatitudeModulation = 0.8,
                TemporalModulation = 0.3,
                SeasonalModulation = 0.2,
                AltitudeScaleHeightMeters = 2_200.0,
            },
        };

        var surface = AtmosphereAerosolOptics.Resolve(atmosphere, 25.0, 0.0, 0.0);
        var high = AtmosphereAerosolOptics.Resolve(atmosphere, 25.0, 20_000.0, 0.0);

        Assert.True(surface.Enabled && high.Enabled);
        Assert.Equal(surface.MieScale, high.MieScale, 12);
        Assert.True(surface.AltitudeFactor > high.AltitudeFactor);
        Assert.Equal(2_200.0, surface.ScaleHeightMeters, 12);
        Assert.All(new[] { surface, high }, value =>
        {
            Assert.True(double.IsFinite(value.MieScale));
            Assert.InRange(value.MieScale, 0.0, AtmosphereAerosolOptics.MaximumMieScale);
            Assert.InRange(value.AngstromExponent, 0.0, 4.0);
        });
    }

    [Fact]
    public void ZeroConfiguredAodRemovesOnlyTheOptedInMieContribution()
    {
        var atmosphere = new AtmosphereModel
        {
            MaxAltitude = 140_000.0,
            Optics = AtmosphereModel.Earth().Optics,
            AerosolClimate = new AerosolClimateState
            {
                Aod550 = 0.0,
                LatitudeModulation = 0.0,
                TemporalModulation = 0.0,
                SeasonalModulation = 0.0,
            },
        };

        var binding = AtmosphereAerosolOptics.Resolve(atmosphere, 0.0, 0.0, 0.0);

        Assert.True(binding.Enabled);
        Assert.Equal(0.0, binding.MieScale);
        Assert.True(atmosphere.Optics.RayleighScattering.MagnitudeSquared > 0.0);
    }
}
