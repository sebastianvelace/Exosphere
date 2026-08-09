namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Xunit;

public sealed class AerosolClimateStateTests
{
    [Fact]
    public void NormalizationMakesAodExponentAndSpeciesFiniteAndBounded()
    {
        var normalized = new AerosolClimateState
        {
            Aod550 = double.NaN,
            AngstromExponent = double.PositiveInfinity,
            DustFraction = double.PositiveInfinity,
            MistFraction = -5.0,
            LatitudeWidthDegrees = double.NaN,
            AltitudeScaleHeightMeters = double.NegativeInfinity,
            TemporalPeriodSeconds = double.NaN,
            SeasonalPeriodSeconds = double.PositiveInfinity,
        }.Normalize();

        Assert.True(double.IsFinite(normalized.Aod550));
        Assert.InRange(normalized.Aod550, 0.0, AerosolClimateState.MaximumAod550);
        Assert.True(double.IsFinite(normalized.AngstromExponent));
        Assert.InRange(normalized.AngstromExponent, 0.0, 4.0);
        Assert.InRange(normalized.DustFraction, 0.0, 1.0);
        Assert.InRange(normalized.MistFraction, 0.0, 1.0);
        Assert.Equal(1.0, normalized.DustFraction + normalized.MistFraction, 12);
        Assert.True(normalized.LatitudeWidthDegrees > 0.0);
        Assert.True(normalized.AltitudeScaleHeightMeters > 0.0);
        Assert.True(normalized.TemporalPeriodSeconds > 0.0);
        Assert.True(normalized.SeasonalPeriodSeconds > 0.0);
    }

    [Fact]
    public void MissingSpeciesUseNeutralNormalisedMixture()
    {
        var normalized = new AerosolClimateState
        {
            DustFraction = 0.0,
            MistFraction = 0.0,
        }.Normalize();

        Assert.Equal(0.5, normalized.DustFraction, 12);
        Assert.Equal(0.5, normalized.MistFraction, 12);
    }

    [Fact]
    public void AngstromLawPreservesReferenceAndIncreasesTowardShorterWavelengths()
    {
        var state = new AerosolClimateState
        {
            Aod550 = 0.4,
            AngstromExponent = 1.2,
            TemporalModulation = 0.0,
            SeasonalModulation = 0.0,
            LatitudeModulation = 0.0,
        };

        var sample = state.Sample(0.0, 0.0, 0.0, 550.0);
        Assert.Equal(sample.Aod550, sample.OpticalDepth, 12);
        Assert.True(sample.OpticalDepthAt(450.0) > sample.OpticalDepth);
        Assert.True(sample.OpticalDepthAt(800.0) < sample.OpticalDepth);
        Assert.Equal(sample.OpticalDepth, state.OpticalDepthAt(sample.Aod550, 550.0), 12);
    }

    [Fact]
    public void SpatialFactorsAreSymmetricMonotonicAndBounded()
    {
        var state = new AerosolClimateState
        {
            LatitudeModulation = 0.5,
            LatitudePeakDegrees = 25.0,
            LatitudeWidthDegrees = 20.0,
            AltitudeScaleHeightMeters = 1_500.0,
        };

        Assert.Equal(state.LatitudeFactor(25.0), state.LatitudeFactor(-25.0), 12);
        Assert.True(state.LatitudeFactor(25.0) > state.LatitudeFactor(0.0));
        Assert.True(state.AltitudeFactor(0.0) >= state.AltitudeFactor(2_000.0));
        Assert.True(state.AltitudeFactor(2_000.0) >= state.AltitudeFactor(10_000.0));
        Assert.InRange(state.AltitudeFactor(-100.0), 0.05, 1.0);
    }

    [Fact]
    public void TemporalAndSeasonalFactorsRepeatAtTheirPeriods()
    {
        var state = new AerosolClimateState
        {
            TemporalModulation = 0.3,
            TemporalPeriodSeconds = 100.0,
            SeasonalModulation = 0.2,
            SeasonalPeriodSeconds = 1_000.0,
        };

        Assert.Equal(state.TemporalFactor(12.0), state.TemporalFactor(112.0), 12);
        Assert.Equal(state.SeasonalFactor(12.0), state.SeasonalFactor(1_012.0), 12);
        Assert.InRange(state.TemporalFactor(double.NaN), 0.05, 5.0);
        Assert.InRange(state.SeasonalFactor(double.PositiveInfinity), 0.05, 5.0);
    }

    [Fact]
    public void SampleSplitsEffectiveAodBetweenDustAndMist()
    {
        var state = new AerosolClimateState
        {
            Aod550 = 0.2,
            DustFraction = 3.0,
            MistFraction = 1.0,
            LatitudeModulation = 0.0,
            TemporalModulation = 0.0,
            SeasonalModulation = 0.0,
        };

        var sample = state.Sample(0.0, 0.0, 0.0);
        Assert.Equal(0.75, sample.DustAod550 / sample.Aod550, 12);
        Assert.Equal(0.25, sample.MistAod550 / sample.Aod550, 12);
        Assert.Equal(sample.Aod550,
            sample.Aod550For(AerosolSpecies.Dust) + sample.Aod550For(AerosolSpecies.Mist),
            12);
    }

    [Fact]
    public void HostileInputsNeverProduceNegativeOrNonFiniteSample()
    {
        var state = new AerosolClimateState
        {
            Aod550 = double.MaxValue,
            AngstromExponent = double.MaxValue,
            DustFraction = double.NaN,
            MistFraction = double.NegativeInfinity,
            LatitudeModulation = double.MaxValue,
            LatitudePeakDegrees = double.NaN,
            LatitudeWidthDegrees = double.PositiveInfinity,
            AltitudeScaleHeightMeters = double.NaN,
            TemporalModulation = double.PositiveInfinity,
            TemporalPeriodSeconds = double.NaN,
            SeasonalModulation = double.NegativeInfinity,
            SeasonalPeriodSeconds = double.PositiveInfinity,
        };

        foreach (double latitude in new[] { double.NaN, -90.0, 0.0, 90.0, double.PositiveInfinity })
        foreach (double altitude in new[] { double.NaN, -100.0, 0.0, 1_000_000.0 })
        {
            var sample = state.Sample(latitude, altitude, double.NaN, 0.0);
            Assert.True(sample.IsFiniteNonNegative, $"invalid sample: {sample}");
        }
    }

    [Fact]
    public void SampleWavelengthIsClampedToAStablePhysicalRange()
    {
        var sample = AerosolClimateState.EarthLike.Sample(
            25.0, 0.0, 0.0, double.PositiveInfinity);

        Assert.Equal(AerosolClimateState.ReferenceWavelengthNanometers,
            sample.WavelengthNanometers, 12);
        Assert.True(sample.IsFiniteNonNegative);
    }
}
