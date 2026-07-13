using Exosphere.Simulation;
using Xunit;

namespace ExosphereSimulation.Tests;

public sealed class VehicleVisualPhysicsTests
{
    [Fact]
    public void TangentOgiveRunsFromFullBaseToZeroTipMonotonically()
    {
        const double radius = 4.5, length = 32.2;
        Assert.Equal(radius, VehicleVisualPhysics.TangentOgiveRadius(0.0, radius, length), 9);
        Assert.Equal(0.0, VehicleVisualPhysics.TangentOgiveRadius(1.0, radius, length), 9);
        double previous = radius;
        for (int i = 1; i <= 100; i++)
        {
            double current = VehicleVisualPhysics.TangentOgiveRadius(i / 100.0, radius, length);
            Assert.InRange(current, 0.0, previous);
            previous = current;
        }
    }

    [Theory]
    [InlineData(658.0, 900_000.0, 1_400.0)] // hot ascent is not reentry
    [InlineData(-300.0, 20_000.0, 1_400.0)] // descent below flux threshold
    [InlineData(-300.0, 900_000.0, 500.0)]  // energetic reentry even before full soak
    public void GlowRequiresDescendingHighFluxFlight(double radial, double flux, double skinK)
    {
        double glow = VehicleVisualPhysics.ReentryGlow(radial, flux, skinK);
        if (radial < -20.0 && flux >= VehicleVisualPhysics.VisibleReentryFluxWm2) Assert.True(glow > 0.0);
        else Assert.Equal(0.0, glow);
    }
}
