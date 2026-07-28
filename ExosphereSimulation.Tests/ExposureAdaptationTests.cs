namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Visual;
using Xunit;

public sealed class ExposureAdaptationTests
{
    [Fact]
    public void BrightAdaptationIsFasterThanDarkAdaptation()
    {
        var bright = new ExposureAdaptation(4.0);
        var dark = new ExposureAdaptation(1.0);

        double brightFraction = (4.0 - bright.Update(1.0, 1.0)) / 3.0;
        double darkFraction = (dark.Update(4.0, 1.0) - 1.0) / 3.0;

        Assert.True(brightFraction > darkFraction * 5.0);
    }

    [Fact]
    public void AdaptationIsMonotonicAndDoesNotOvershoot()
    {
        var model = new ExposureAdaptation(1.0);
        double previous = model.CurrentExposure;
        for (int i = 0; i < 600; i++)
        {
            double next = model.Update(5.0, 1.0 / 60.0);
            Assert.InRange(next, previous, 5.0);
            previous = next;
        }
    }

    [Fact]
    public void ExponentialUpdateIsIndependentOfFramePartition()
    {
        var oneStep = new ExposureAdaptation(1.0);
        var manySteps = new ExposureAdaptation(1.0);

        double once = oneStep.Update(5.0, 2.0);
        for (int i = 0; i < 120; i++) manySteps.Update(5.0, 1.0 / 60.0);

        Assert.Equal(once, manySteps.CurrentExposure, 10);
    }

    [Fact]
    public void LinearPreTonemapExposureIsReciprocalToLuminance()
    {
        double halfGrey = ExposureAdaptation.TargetForLuminance(
            ExposureAdaptation.MiddleGreyLuminance * 0.5);
        double middleGrey = ExposureAdaptation.TargetForLuminance(
            ExposureAdaptation.MiddleGreyLuminance);
        double doubleGrey = ExposureAdaptation.TargetForLuminance(
            ExposureAdaptation.MiddleGreyLuminance * 2.0);

        Assert.Equal(2.0, halfGrey, 12);
        Assert.Equal(1.0, middleGrey, 12);
        Assert.Equal(0.5, doubleGrey, 12);
    }

    [Fact]
    public void EqualStopChangesRemainGeometricallyCentered()
    {
        var model = new ExposureAdaptation(4.0);

        double halfwayTime = ExposureAdaptation.BrightAdaptationSeconds
            * System.Math.Log(2.0);
        double exposure = model.Update(1.0, halfwayTime);

        // Halfway in perceptual/log space between 4x and 1x is 2x, not 2.5x.
        Assert.Equal(2.0, exposure, 12);
    }

    [Fact]
    public void LargeDaylightStepProtectsHighlightsWithinTwoSeconds()
    {
        var model = new ExposureAdaptation(ExposureAdaptation.MaximumExposure);
        double daylightTarget = ExposureAdaptation.TargetForLuminance(0.36);

        double exposure = model.Update(daylightTarget, 2.0);

        Assert.InRange(exposure, daylightTarget, daylightTarget * 1.15);
    }

    [Theory]
    [InlineData(1e9, ExposureAdaptation.MinimumExposure)]
    [InlineData(0.0, ExposureAdaptation.MaximumExposure)]
    public void LuminanceMappingRespectsExposureLimits(double luminance, double expected)
    {
        Assert.Equal(expected, ExposureAdaptation.TargetForLuminance(luminance), 12);
    }
}
