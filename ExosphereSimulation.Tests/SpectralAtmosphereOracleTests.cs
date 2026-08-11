namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class SpectralAtmosphereOracleTests
{
    [Fact]
    public void VisibleBandsAreOrderedAndInsideTheConfiguredRange()
    {
        var oracle = SpectralAtmosphereOracle.Build(AtmosphereModel.Earth(),
            maxOrder: 4, sampleCount: 16);

        Assert.Equal(9, oracle.BandCentersNm.Count);
        Assert.Equal(400.0, oracle.BandCentersNm[0]);
        Assert.Equal(700.0, oracle.BandCentersNm[^1]);
        for (int i = 1; i < oracle.BandCentersNm.Count; i++)
            Assert.True(oracle.BandCentersNm[i] > oracle.BandCentersNm[i - 1]);
        Assert.All(oracle.Bands, band => Assert.InRange(band.WavelengthNm, 400.0, 700.0));
    }

    [Fact]
    public void RGBProfilesReconstructFiniteNonNegativeBands()
    {
        foreach (string id in new[] { "earth", "mars", "venus" })
        {
            var oracle = SpectralAtmosphereOracle.Build(LoadBody(id), maxOrder: 4, sampleCount: 16);
            Assert.Equal("reconstructed", oracle.DataProvenance);
            foreach (var band in oracle.Bands)
            {
                Assert.True(double.IsFinite(band.RayleighScattering));
                Assert.True(double.IsFinite(band.MieScattering));
                Assert.True(double.IsFinite(band.MieAbsorption));
                Assert.True(double.IsFinite(band.OzoneAbsorption));
                Assert.True(band.RayleighScattering >= 0.0);
                Assert.True(band.MieScattering >= 0.0);
                Assert.True(band.MieAbsorption >= 0.0);
                Assert.True(band.OzoneAbsorption >= 0.0);
            }
        }
    }

    [Fact]
    public void LogLinearReconstructionMatchesTheRGBAnchors()
    {
        var rgb = new Vector3d(0.7, 0.2, 0.05);
        Assert.Equal(rgb.Z, SpectralAtmosphereOracle.ReconstructAt(rgb, 440.0), 14);
        Assert.Equal(rgb.Y, SpectralAtmosphereOracle.ReconstructAt(rgb, 550.0), 14);
        Assert.Equal(rgb.X, SpectralAtmosphereOracle.ReconstructAt(rgb, 680.0), 14);

        var earth = SpectralAtmosphereOracle.Build(AtmosphereModel.Earth(), sampleCount: 16);
        Assert.True(earth.Bands[1].RayleighScattering > earth.Bands[7].RayleighScattering);
        Assert.True(earth.Bands[4].OzoneAbsorption > earth.Bands[0].OzoneAbsorption);
        Assert.True(earth.Bands[4].OzoneAbsorption > earth.Bands[^1].OzoneAbsorption);
    }

    [Fact]
    public void VerticalTransmittanceIsFiniteAndBounded()
    {
        var oracle = SpectralAtmosphereOracle.Build(AtmosphereModel.Earth(), sampleCount: 16);
        foreach (double altitude in new[] { 0.0, 10_000.0, 70_000.0, 120_000.0 })
        {
            var transmittance = oracle.VerticalTransmittance(altitude);
            Assert.All(transmittance, value => Assert.InRange(value, 0.0, 1.0));
        }
    }

    [Fact]
    public void SpectralToLinearRgbIsStableAndFinite()
    {
        var spectrum = new SpectralVector(Enumerable.Range(0, 9)
            .Select(index => 0.1 + index * 0.02));
        var xyz = spectrum.ToXyz();
        var rgb = spectrum.ToLinearRgb();
        Assert.True(double.IsFinite(xyz.X) && double.IsFinite(xyz.Y) && double.IsFinite(xyz.Z));
        Assert.True(double.IsFinite(rgb.X) && double.IsFinite(rgb.Y) && double.IsFinite(rgb.Z));
        Assert.True(rgb.X >= 0.0 && rgb.Y >= 0.0 && rgb.Z >= 0.0);
    }

    [Fact]
    public void OrdersTwoThroughFiveRemainFiniteNonNegativeAndConverge()
    {
        var oracle = SpectralAtmosphereOracle.Build(AtmosphereModel.Earth(),
            maxOrder: 5, sampleCount: 24);
        double order2 = oracle.EnergyByOrder(2);
        double order3 = oracle.EnergyByOrder(3);
        double order4 = oracle.EnergyByOrder(4);
        double order5 = oracle.EnergyByOrder(5);

        Assert.True(double.IsFinite(order2) && double.IsFinite(order3)
            && double.IsFinite(order4) && double.IsFinite(order5));
        Assert.True(order2 >= 0.0 && order3 >= order2 && order4 >= order3 && order5 >= order4,
            $"order energy was not monotonic: {order2}, {order3}, {order4}, {order5}");
        Assert.True(order5 - order4 <= order4 - order3 + 1e-12,
            $"order 5 did not converge: Δ34={order4 - order3}, Δ45={order5 - order4}");
    }

    [Fact]
    public void PlanetaryShadowRemovesDirectScatteringEnergy()
    {
        var oracle = SpectralAtmosphereOracle.Build(LoadBody("earth"),
            maxOrder: 5, sampleCount: 16);
        var night = oracle.Evaluate(0.0, -0.5, 1.0, 1.0);
        var eclipse = oracle.Evaluate(0.0, System.Math.PI / 4.0, 1.0, 1.0, solarVisibility: 0.0);

        Assert.True(night.Energy >= 0.0 && night.Energy < 1e-10,
            $"geometric night unexpectedly carried direct scattering energy: {night.Energy}");
        Assert.True(eclipse.Energy >= 0.0 && eclipse.Energy < 1e-10,
            $"total eclipse unexpectedly carried direct scattering energy: {eclipse.Energy}");
    }

    [Fact]
    public void VisibilityIsMonotonicAcrossDayTerminatorAndEclipse()
    {
        var oracle = SpectralAtmosphereOracle.Build(AtmosphereModel.Earth(),
            maxOrder: 4, sampleCount: 16);
        double day = oracle.Evaluate(0.0, System.Math.PI / 4.0, 1.0, 1.0, 1.0).Energy;
        double partial = oracle.Evaluate(0.0, System.Math.PI / 4.0, 1.0, 1.0, 0.5).Energy;
        double total = oracle.Evaluate(0.0, System.Math.PI / 4.0, 1.0, 1.0, 0.0).Energy;

        Assert.True(day >= partial && partial >= total,
            $"visibility was not monotonic: day={day}, partial={partial}, total={total}");
    }

    [Fact]
    public void ComparatorReportsGlobalAndAngularLutMetrics()
    {
        var body = LoadBody("earth");
        var report = SpectralAtmosphereComparator.Compare(
            body,
            new[]
            {
                new SpectralEvaluationCoordinate(0.0, System.Math.PI / 4.0, 1.0, 0.8, "day"),
                new SpectralEvaluationCoordinate(70_000.0, -0.01, 0.6, -0.2, "terminator"),
            },
            new SpectralComparisonOptions
            {
                OracleSampleCount = 12,
                LutWidth = 6,
                LutHeight = 6,
                LutIntegrationSteps = 8,
                LutSolarSamples = 8,
                AngularWidth = 4,
                AngularSolarHeight = 4,
                AngularViewHeight = 4,
                AngularMuWidth = 4,
                AngularOpticalDepthSamples = 8,
            });

        Assert.Equal("earth", report.BodyId);
        Assert.Equal("reconstructed", report.DataProvenance);
        Assert.Equal(2, report.Samples.Count);
        Assert.True(report.AllFiniteAndNonNegative);
        Assert.True(report.AllOrdersMonotonic);
        Assert.Contains("oracle_energy", report.ToCsv());
    }

    private static CelestialBody LoadBody(string id) => CelestialBody.LoadFromJson(
        Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
