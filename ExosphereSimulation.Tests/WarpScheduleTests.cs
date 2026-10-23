namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class WarpScheduleTests
{
    private static readonly double[] Levels = { 1, 2, 3, 5, 10, 50, 100, 200, 1000, 10000, 100000 };

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(-5.0, 0)]
    [InlineData(3.0, 0)]
    [InlineData(60.0, 4)]
    [InlineData(3_600.0, 8)]
    [InlineData(9.0 * 3_600.0, 9)]
    [InlineData(30.0 * 86_400.0, 10)]
    public void WarpStepsDownAsTheEventNears(double remaining, int expected)
    {
        int index = WarpSchedule.IndexFor(remaining, Levels);
        Assert.Equal(expected, index);
        // A frame never swallows more than a quarter of what is left.
        if (index > 0) Assert.True(Levels[index] * WarpSchedule.FrameSecondsAtUnitWarp < 0.25 * remaining);
    }
}
