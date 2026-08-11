namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class HoldDownReleasePolicyTests
{
    [Fact]
    public void RequiresBothTwrAndNearFullThrottle()
    {
        Assert.False(HoldDownReleasePolicy.CanRelease(1.06, 0.90));
        Assert.False(HoldDownReleasePolicy.CanRelease(1.04, 1.0));
        Assert.True(HoldDownReleasePolicy.CanRelease(1.06, 0.95));
        Assert.True(HoldDownReleasePolicy.CanRelease(1.58, 1.0));
    }

    [Fact]
    public void RejectsNonFiniteInputs()
    {
        Assert.False(HoldDownReleasePolicy.CanRelease(double.NaN, 1.0));
        Assert.False(HoldDownReleasePolicy.CanRelease(1.2, double.PositiveInfinity));
    }
}
