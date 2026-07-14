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

    [Fact]
    public void PlasmaPhaseScalePeaksAtPeakHeatingAndFadesThroughAero()
    {
        Assert.Equal(1.00, VehicleVisualPhysics.ReentryPlasmaPhaseScale("PEAK_HEATING"), 6);
        Assert.True(VehicleVisualPhysics.ReentryPlasmaPhaseScale("ENTRY")
            < VehicleVisualPhysics.ReentryPlasmaPhaseScale("PEAK_HEATING"));
        Assert.True(VehicleVisualPhysics.ReentryPlasmaPhaseScale("AERO_DESCENT")
            < VehicleVisualPhysics.ReentryPlasmaPhaseScale("ENTRY"));
        Assert.Equal(0.0, VehicleVisualPhysics.ReentryPlasmaPhaseScale("LANDED"), 6);
    }

    [Fact]
    public void PlasmaVisualIntensitySoftensEntryAndSurvivesAeroWithFlux()
    {
        double midFlux = 0.50;
        double entry = VehicleVisualPhysics.ReentryPlasmaVisualIntensity(midFlux, "ENTRY");
        double peak  = VehicleVisualPhysics.ReentryPlasmaVisualIntensity(midFlux, "PEAK_HEATING");
        double aero  = VehicleVisualPhysics.ReentryPlasmaVisualIntensity(midFlux, "AERO_DESCENT");

        Assert.True(entry < peak, $"entry {entry} should be softer than peak {peak}");
        Assert.True(aero < peak, $"aero {aero} should be below peak {peak}");
        Assert.True(entry > 0.05);
        Assert.InRange(peak, 0.49, 0.51);
    }
}
