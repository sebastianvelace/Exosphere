namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Flight;
using Xunit;

public sealed class PoweredLandingGuidanceTests
{
    [Fact]
    public void CoastsWhenBelowDescentProfileAndNoLateralCorrectionIsNeeded()
    {
        double command = PoweredLandingGuidance.ComputeAccelerationCommand(
            gravity: 9.81,
            thrustUpComponent: 1.0,
            verticalError: -24.0,
            horizontalError: 0.0,
            verticalVelocityUp: 0.0);

        Assert.Equal(0.0, command);
    }

    [Fact]
    public void BrakesWhenVerticalSpeedExceedsProfile()
    {
        double command = PoweredLandingGuidance.ComputeAccelerationCommand(
            gravity: 9.81,
            thrustUpComponent: 1.0,
            verticalError: 8.0,
            horizontalError: 0.0,
            verticalVelocityUp: -4.0);

        Assert.True(command > 9.81);
    }

    [Fact]
    public void KeepsPoweredCorrectionForLateralErrorWhileDescendingSlowly()
    {
        double command = PoweredLandingGuidance.ComputeAccelerationCommand(
            gravity: 9.81,
            thrustUpComponent: 1.0,
            verticalError: -5.0,
            horizontalError: 3.0,
            verticalVelocityUp: 0.0);

        Assert.True(command > 0.0);
    }
}
