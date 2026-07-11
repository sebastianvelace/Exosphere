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

    [Theory]
    [InlineData(1e9, ExposureAdaptation.MinimumExposure)]
    [InlineData(0.0, ExposureAdaptation.MaximumExposure)]
    public void LuminanceMappingRespectsExposureLimits(double luminance, double expected)
    {
        Assert.Equal(expected, ExposureAdaptation.TargetForLuminance(luminance), 12);
    }
}
