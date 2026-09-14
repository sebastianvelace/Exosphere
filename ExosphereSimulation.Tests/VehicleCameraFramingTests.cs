namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Visual;
using Xunit;

public sealed class VehicleCameraFramingTests
{
    [Theory]
    [InlineData(5.0, 0.1)]
    [InlineData(4200.0, 42.0)]
    [InlineData(400000.0, 1000.0)]
    [InlineData(double.NaN, 0.1)]
    public void ExternalNearPlanePreservesCloseViewsAndDistantDepthPrecision(double distance, double expected)
    {
        Assert.Equal(expected, VehicleCameraFraming.ExternalNearPlane(distance, 0.1), 8);
    }

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

    [Fact]
    public void HistoricalSmallLauncherUsesCloserPadFramingThanStarship()
    {
        double mercury = VehicleCameraFraming.PadTrackingDistance(
            25.3 / 2.8, -4.0 / 2.8);
        double starship = VehicleCameraFraming.PadTrackingDistance(
            121.0 / 2.8, -4.0 / 2.8);

        // ~3× vehicle height floor keeps both stacks readable; Starship still much farther.
        Assert.InRange(mercury, 25.0, 32.0);
        Assert.True(starship > mercury * 3.5);
        Assert.True(starship >= (121.0 / 2.8) * 3.0 - 0.1);
    }

    [Fact]
    public void EarlyFlightFrameMovesSmoothlyFromTowerContextToVehicle()
    {
        const double height = 121.0 / 2.8;

        var opening = VehicleCameraFraming.EarlyFlightFrame(height, 0.0);
        var towerClear = VehicleCameraFraming.EarlyFlightFrame(height, -height * 1.2);
        var handoff = VehicleCameraFraming.EarlyFlightFrame(height, -height * 1.5);
        var afterHandoff = VehicleCameraFraming.EarlyFlightFrame(height, -height * 3.0);

        Assert.Equal(0.0, opening.VehicleFocus, 8);
        Assert.Equal(height * 3.0, opening.CameraDistance, 8);
        Assert.InRange(towerClear.VehicleFocus, 0.85, 0.90);
        Assert.InRange(towerClear.CameraDistance, height * 1.8, height * 2.0);
        Assert.Equal(1.0, handoff.VehicleFocus, 8);
        Assert.Equal(height * 1.65, handoff.CameraDistance, 8);
        Assert.Equal(handoff, afterHandoff);
        Assert.Equal(height * 4.5,
            VehicleCameraFraming.PadTrackingDistance(height, -height * 100.0), 8);
        Assert.True(VehicleCameraFraming.PadTrackingDistance(
            height, -height * 100.0) < 850.0);

        double previousFocus = opening.VehicleFocus;
        double previousDistance = opening.CameraDistance;
        for (int step = 1; step <= 150; step++)
        {
            double altitude = height * step / 100.0;
            var frame = VehicleCameraFraming.EarlyFlightFrame(height, -altitude);
            Assert.True(frame.VehicleFocus >= previousFocus);
            Assert.True(frame.CameraDistance <= previousDistance + 1e-10);
            Assert.InRange(previousDistance - frame.CameraDistance, 0.0, height * 0.02);
            previousFocus = frame.VehicleFocus;
            previousDistance = frame.CameraDistance;
        }
    }
}
