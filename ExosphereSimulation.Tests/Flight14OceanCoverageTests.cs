namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Xunit;

public sealed class Flight14OceanCoverageTests
{
    [Fact]
    public void WaterDomainMatchesTheCompleteNativeGridOceanScreen()
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var profile = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles/starship_flight14_landing_estimate.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "..", "docs", "research",
            "FLIGHT14_OCEAN_COVERAGE_2026-11-06.json")));
        var evidence = document.RootElement;
        var bounds = evidence.GetProperty("bounds");
        Assert.Equal(profile.MinimumWaterLatitudeDegrees, bounds.GetProperty("minimumLatitudeDegrees").GetDouble());
        Assert.Equal(profile.MaximumWaterLatitudeDegrees, bounds.GetProperty("maximumLatitudeDegrees").GetDouble());
        Assert.Equal(profile.MinimumWaterLongitudeDegrees, bounds.GetProperty("minimumLongitudeDegrees").GetDouble());
        Assert.Equal(profile.MaximumWaterLongitudeDegrees, bounds.GetProperty("maximumLongitudeDegrees").GetDouble());
        double cellsPerDegree = 3_600 / evidence.GetProperty("cellSizeArcSeconds").GetDouble();
        long expectedCells = (long)System.Math.Round(
            (profile.MaximumWaterLatitudeDegrees - profile.MinimumWaterLatitudeDegrees)
            * (profile.MaximumWaterLongitudeDegrees - profile.MinimumWaterLongitudeDegrees)
            * cellsPerDegree * cellsPerDegree);
        var statistics = evidence.GetProperty("statistics");
        Assert.Equal(expectedCells, statistics.GetProperty("count").GetInt64());
        Assert.Equal(1, statistics.GetProperty("skipX").GetInt32());
        Assert.Equal(1, statistics.GetProperty("skipY").GetInt32());
        Assert.True(statistics.GetProperty("max").GetDouble() < 0, "Every sampled cell must be below sea level.");
        var registry = DataProvenanceRegistry.LoadFromDirectory(Path.Combine(data, "provenance"));
        Assert.Equal(ProvenanceStatus.Derived, registry.Require("starship-flight14-landing", "waterContactRegion").Status);
        var additional = evidence.GetProperty("additionalRegions").EnumerateArray().ToArray();
        Assert.Equal(profile.AdditionalWaterRegions.Length, additional.Length);
        for (int i = 0; i < additional.Length; i++)
        {
            var region = profile.AdditionalWaterRegions[i];
            var source = additional[i];
            var rectangle = source.GetProperty("bounds");
            Assert.True(region.IsValid);
            Assert.Equal(region.MinimumLatitudeDegrees, rectangle.GetProperty("minimumLatitudeDegrees").GetDouble());
            Assert.Equal(region.MaximumLatitudeDegrees, rectangle.GetProperty("maximumLatitudeDegrees").GetDouble());
            Assert.Equal(region.MinimumLongitudeDegrees, rectangle.GetProperty("minimumLongitudeDegrees").GetDouble());
            Assert.Equal(region.MaximumLongitudeDegrees, rectangle.GetProperty("maximumLongitudeDegrees").GetDouble());
            long cells = (long)System.Math.Round((region.MaximumLatitudeDegrees - region.MinimumLatitudeDegrees)
                * (region.MaximumLongitudeDegrees - region.MinimumLongitudeDegrees) * cellsPerDegree * cellsPerDegree);
            var sample = source.GetProperty("statistics");
            Assert.Equal(cells, sample.GetProperty("count").GetInt64());
            Assert.Equal(1, sample.GetProperty("skipX").GetInt32());
            Assert.Equal(1, sample.GetProperty("skipY").GetInt32());
            Assert.True(sample.GetProperty("max").GetDouble() < 0);
        }
        Assert.Equal(ProvenanceStatus.Derived, registry.Require(
            "starship-flight14-landing", "additionalWaterRegions").Status);
    }

    [Theory]
    [InlineData(23.136989, -157.900773, true)] // Isolated-Earth contact.
    [InlineData(22.527910445469917, -160.29244503385752, true)] // Moving-Earth contact.
    [InlineData(22.473102, -155.403348, true)] // F4 surface crossing, inside verified ocean.
    [InlineData(21.504359, -163.085303, true)] // Original-profile F4 westward crossing.
    [InlineData(22.131289, -158.401513, true)] // Moving world after momentum-conserving split.
    [InlineData(22.176061, -158.100198, true)] // Isolated Earth after the same repair.
    [InlineData(22.15, -161.5, false)] // Gap between verified rectangles is not implicitly water.
    [InlineData(22.2, -159.5, false)] // Kauai.
    [InlineData(23.06, -161.92, false)] // Nihoa.
    [InlineData(21.3, -157.8, false)] // Oahu.
    public void CoverageContainsBothPropagatedContactsAndExcludesNearbyIslands(
        double latitude, double longitude, bool expected)
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var profile = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles/starship_flight14_landing_estimate.json"));
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        var water = new WaterContactDefinition(4.5, 52, -3.78, 22, profile.WaterDensityKgPerM3,
            profile.WaterDragCoefficient, profile.WettingDepthM, profile.MinimumWaterLatitudeDegrees,
            profile.MaximumWaterLatitudeDegrees, profile.MinimumWaterLongitudeDegrees,
            profile.MaximumWaterLongitudeDegrees, AdditionalRegions: profile.AdditionalWaterRegions);
        var position = earth.GetSurfacePositionAtTime(latitude, longitude, 0, 0);
        Assert.Equal(expected, water.Covers(earth, position, 0));
    }

    [Fact]
    public void NonFiniteAdditionalCoverageCannotEnableWater()
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var profile = Flight14LandingDefinition.LoadFromJson(Path.Combine(data,
            "flight_profiles/starship_flight14_landing_estimate.json"));
        profile.AdditionalWaterRegions = [new(double.NegativeInfinity, 90, -180, 180)];
        Assert.Throws<InvalidDataException>(profile.Validate);
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        var water = new WaterContactDefinition(4.5, 52, -3.78, 22, 1025, 0.8, 0.5,
            22.3, 35, -161, -140, AdditionalRegions: profile.AdditionalWaterRegions);
        Assert.False(water.Covers(earth, earth.GetSurfacePositionAtTime(0, 0, 0, 0), 0));
    }

}
