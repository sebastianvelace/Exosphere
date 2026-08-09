namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using System.Text.Json;
using Xunit;

public sealed class AtmosphereAerosolJsonTests
{
    [Fact]
    public void AtmospheresWithoutClimateKeepTheLegacyNullOptIn()
    {
        var atmosphere = AtmosphereModel.FromJson(Parse("""
        {
          "max_altitude": 100000,
          "optics": {
            "mie_scattering": [0.000001, 0.000001, 0.000001]
          }
        }
        """));

        Assert.Null(atmosphere.AerosolClimate);
    }

    [Fact]
    public void JsonClimateIsNormalisedAndCarriesSpatialTemporalInputs()
    {
        var atmosphere = AtmosphereModel.FromJson(Parse("""
        {
          "max_altitude": 100000,
          "aerosol_climate": {
            "aod550": 0.24,
            "angstrom_exponent": 1.35,
            "dust_fraction": 3,
            "mist_fraction": 1,
            "latitude_modulation": 0.4,
            "latitude_peak_degrees": 30,
            "latitude_width_degrees": 18,
            "altitude_scale_height_m": 2200,
            "temporal_modulation": 0.15,
            "temporal_period_s": 86400,
            "seasonal_modulation": 0.2,
            "seasonal_period_s": 31557600
          }
        }
        """));

        var state = Assert.IsType<AerosolClimateState>(atmosphere.AerosolClimate);
        Assert.Equal(0.75, state.DustFraction, 12);
        Assert.Equal(0.25, state.MistFraction, 12);
        var sample = state.Sample(30.0, 0.0, 0.0);
        Assert.True(sample.IsFiniteNonNegative);
        Assert.InRange(sample.OpticalDepth, 0.38, 0.43);
    }

    [Fact]
    public void MalformedClimateInputsAreBoundedAtTheJsonBoundary()
    {
        var atmosphere = AtmosphereModel.FromJson(Parse("""
        {
          "max_altitude": 100000,
          "aerosol_climate": {
            "aod550": 999999,
            "angstrom_exponent": -5,
            "dust_fraction": 0,
            "mist_fraction": 0,
            "altitude_scale_height_m": -1
          }
        }
        """));

        var state = Assert.IsType<AerosolClimateState>(atmosphere.AerosolClimate);
        Assert.Equal(AerosolClimateState.MaximumAod550, state.Aod550);
        Assert.Equal(0.0, state.AngstromExponent);
        Assert.Equal(0.5, state.DustFraction, 12);
        Assert.Equal(0.5, state.MistFraction, 12);
        Assert.Equal(1.0, state.AltitudeScaleHeightMeters, 12);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
