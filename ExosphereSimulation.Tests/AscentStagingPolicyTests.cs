namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class AscentStagingPolicyTests
{
    [Fact]
    public void ShouldHotStage_WhenSpeedAndAltitudeCriteriaMet()
    {
        Assert.True(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: true,
            surfaceSpeedMps: 2400.0,
            altitudeMeters: 60_000.0,
            remainingFuelFraction: 0.20));
    }

    [Fact]
    public void ShouldHotStage_WhenBoosterReserveReached_EvenBelowStagingSpeed()
    {
        Assert.True(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: true,
            surfaceSpeedMps: 1800.0,
            altitudeMeters: 50_000.0,
            remainingFuelFraction: 0.05));
    }

    [Fact]
    public void ShouldNotHotStage_WhenAlreadyStagedOrBoosterGone()
    {
        Assert.False(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: true,
            boosterStillAttached: true,
            surfaceSpeedMps: 2500.0,
            altitudeMeters: 65_000.0,
            remainingFuelFraction: 0.10));

        Assert.False(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: false,
            surfaceSpeedMps: 2500.0,
            altitudeMeters: 65_000.0,
            remainingFuelFraction: 0.10));
    }

    [Fact]
    public void ShouldNotHotStage_WhenSpeedLowAndReserveAboveThreshold()
    {
        Assert.False(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: true,
            surfaceSpeedMps: 2000.0,
            altitudeMeters: 60_000.0,
            remainingFuelFraction: 0.20));
    }

    [Fact]
    public void ShouldNotHotStage_WhenSpeedHighButBelowMinAltitude()
    {
        Assert.False(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: true,
            surfaceSpeedMps: 2500.0,
            altitudeMeters: 40_000.0,
            remainingFuelFraction: 0.20));
    }

    [Fact]
    public void SoftLandingSpeed_MatchesEdlTouchdownTarget()
    {
        // EDLController.TouchdownVel is 3.0 m/s — Universe must use the same contract.
        Assert.Equal(3.0, AscentStagingPolicy.SoftLandingSpeedMps, precision: 6);
    }
}
