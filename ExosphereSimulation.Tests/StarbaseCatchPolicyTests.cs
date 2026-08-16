namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class StarbaseCatchPolicyTests
{
    [Fact]
    public void ValidEarthStarbaseEntryArmsOnlyForCatchEquippedShip()
    {
        Assert.True(StarbaseCatchPolicy.IsValidEntry(
            bodyId: "earth",
            launchSiteId: "starbase_pad2",
            isDestroyed: false,
            hasCatchPins: true,
            isStarshipShip: true,
            altitudeM: 70_000.0,
            atmosphereTopM: 140_000.0,
            verticalSpeedMps: -80.0,
            surfaceSpeedMps: 1_800.0));
    }

    [Theory]
    [InlineData("mars", "starbase", false, true, true, 70_000.0, 140_000.0, -80.0, 1_800.0)]
    [InlineData("earth", "kennedy", false, true, true, 70_000.0, 140_000.0, -80.0, 1_800.0)]
    [InlineData("earth", "starbase", false, false, true, 70_000.0, 140_000.0, -80.0, 1_800.0)]
    [InlineData("earth", "starbase", false, true, false, 70_000.0, 140_000.0, -80.0, 1_800.0)]
    [InlineData("earth", "starbase", false, true, true, 300_000.0, 140_000.0, -80.0, 1_800.0)]
    [InlineData("earth", "starbase", false, true, true, 70_000.0, 140_000.0, 15.0, 1_800.0)]
    [InlineData("earth", "starbase", false, true, true, 70_000.0, 140_000.0, -80.0, 900.0)]
    [InlineData("earth", "starbase", true, true, true, 70_000.0, 140_000.0, -80.0, 1_800.0)]
    public void InvalidEntryNeverArmsTowerCatch(
        string bodyId,
        string launchSiteId,
        bool isDestroyed,
        bool hasCatchPins,
        bool isStarshipShip,
        double altitudeM,
        double atmosphereTopM,
        double verticalSpeedMps,
        double surfaceSpeedMps)
    {
        Assert.False(StarbaseCatchPolicy.IsValidEntry(
            bodyId,
            launchSiteId,
            isDestroyed,
            hasCatchPins,
            isStarshipShip,
            altitudeM,
            atmosphereTopM,
            verticalSpeedMps,
            surfaceSpeedMps));
    }

    [Fact]
    public void NonFiniteTrajectoryFailsClosed()
    {
        Assert.False(StarbaseCatchPolicy.IsValidEntry(
            "earth", "starbase", false, true, true,
            double.NaN, 140_000.0, -80.0, 1_800.0));
        Assert.False(StarbaseCatchPolicy.IsValidEntry(
            "earth", "starbase", false, true, true,
            70_000.0, double.PositiveInfinity, -80.0, 1_800.0));
    }
}
