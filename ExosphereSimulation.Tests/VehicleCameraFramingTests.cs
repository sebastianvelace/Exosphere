namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Visual;
using Xunit;

public sealed class VehicleCameraFramingTests
{
    [Fact]
    public void SeparatedShipCannotBeZoomedInsideItsVisualEnvelope()
    {
        double minimum = VehicleCameraFraming.MinimumOrbitDistance(50.0, 9.0, 75.0);
        Assert.True(minimum > 14.0);
    }

    [Fact]
    public void FullStackRequiresMoreDistanceThanSeparatedShip()
    {
        double stack = VehicleCameraFraming.MinimumOrbitDistance(121.0, 9.0, 75.0);
        double ship = VehicleCameraFraming.MinimumOrbitDistance(50.0, 9.0, 75.0);
        Assert.True(stack > ship * 2.0);
    }

    [Fact]
    public void NarrowerFieldOfViewRequiresMoreDistance()
    {
        double narrow = VehicleCameraFraming.MinimumOrbitDistance(50.0, 9.0, 45.0);
        double wide = VehicleCameraFraming.MinimumOrbitDistance(50.0, 9.0, 90.0);
        Assert.True(narrow > wide);
    }
}
