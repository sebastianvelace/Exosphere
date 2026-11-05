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
            "FLIGHT14_OCEAN_COVERAGE_2026-11-04.json")));
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
    }

    [Theory]
    [InlineData(23.136989, -157.900773, true)] // Isolated-Earth contact.
    [InlineData(22.527910445469917, -160.29244503385752, true)] // Moving-Earth contact.
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
            profile.MaximumWaterLongitudeDegrees);
        var position = earth.GetSurfacePositionAtTime(latitude, longitude, 0, 0);
        Assert.Equal(expected, water.Covers(earth, position, 0));
    }

}
