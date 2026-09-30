namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

public sealed class EntryPredictionRefreshTests
{
    private static readonly EntryCorridorGuidance.Prediction Sample =
        new(Vector3d.Up, 1000.0, 2000.0, 300.0, UsedEnergyPropagation: true);

    [Fact]
    public void HighEntryBoundsForecastAgeWhileKeepingEveryControlEpoch()
    {
        var refresh = new EntryPredictionRefresh();
        int controlEpochs = 0;
        double lastForecast = double.NaN;
        for (int tick = 0; tick < 500; tick++)
        {
            double time = tick * 0.02;
            // Body-centred orbital motion at 7.6 km/s stays below the displacement guard.
            var position = Vector3d.Forward * (time * 7600.0);
            if (refresh.NeedsRefresh(time, 80000.0, "ship", "earth", position))
            {
                refresh.Store(time, "ship", "earth", position, Sample);
                lastForecast = time;
            }
            Assert.InRange(time - lastForecast, 0.0, 0.2);
            Assert.Equal(Sample, refresh.Prediction);
            controlEpochs++;
        }
        Assert.Equal(500, controlEpochs);
        Assert.Equal(50UL, refresh.SampleCount);
    }

    [Theory]
    [InlineData(25000.0)]
    [InlineData(10000.0)]
    [InlineData(500.0)]
    public void TerminalEntryAlwaysUsesFreshForecast(double altitude)
    {
        var refresh = new EntryPredictionRefresh();
        for (int tick = 0; tick < 20; tick++)
        {
            double time = tick * 0.02;
            Assert.True(refresh.NeedsRefresh(time, altitude, "ship", "earth", Vector3d.Zero));
            refresh.Store(time, "ship", "earth", Vector3d.Zero, Sample);
        }
        Assert.Equal(20UL, refresh.SampleCount);
    }

    [Fact]
    public void ChangedContextTeleportAndRewoundTimeCannotReuseForecast()
    {
        var refresh = new EntryPredictionRefresh();
        refresh.Store(100.0, "ship", "earth", Vector3d.Zero, Sample);
        Assert.False(refresh.NeedsRefresh(100.02, 80000.0, "ship", "earth", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(100.02, 80000.0, "other", "earth", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(100.02, 80000.0, "ship", "mars", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(100.02, 80000.0, "ship", "earth", Vector3d.Right * 2001.0));
        Assert.True(refresh.NeedsRefresh(99.0, 80000.0, "ship", "earth", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(double.NaN, 80000.0, "ship", "earth", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(100.02, double.NaN, "ship", "earth", Vector3d.Zero));
        Assert.True(refresh.NeedsRefresh(100.02, 80000.0, "ship", "earth", new Vector3d(double.NaN, 0, 0)));
    }
}
