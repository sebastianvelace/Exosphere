namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class BodyFixedGeodeticCoordinatesTests
{
    [Theory]
    [InlineData("earth", 25.996, -97.15, 0, 20)]
    [InlineData("earth", 0, 179.99, 23000, 275000)]
    [InlineData("earth", -45.6, -179.99, -23000, 90000)]
    [InlineData("earth", 60, 170, 900000000, 120000)]
    [InlineData("venus", -33, 179.99, 10000000, 95000)]
    [InlineData("moon", 30, -120, 2500000, 10000)]
    public void RotatingCoordinatesRecoverTheSameSurfaceSiteOnTranslatedBodies(
        string bodyId, double latitude, double longitude, double time, double altitude)
    {
        var body = LoadBody(bodyId);
        body.Position = new Vector3d(1.2e11, -4e10, 8e10);
        var originalPosition = body.Position;
        var point = body.GetSurfacePositionAtTime(latitude, longitude, time, altitude);
        body.GetGeodeticCoordinatesAtTime(point, time, out double actualLatitude,
            out double actualLongitude, out double actualAltitude);

        Assert.InRange(System.Math.Abs(actualLatitude - latitude), 0, 1e-6);
        double longitudeError = System.Math.IEEERemainder(actualLongitude - longitude, 360);
        Assert.InRange(System.Math.Abs(longitudeError), 0, 1e-6);
        Assert.InRange(System.Math.Abs(actualAltitude - altitude), 0, 0.01);
        Assert.Equal(originalPosition, body.Position);
    }

    [Fact]
    public void InertiallyFixedEquatorialPointMovesWestOnTheRotatingEarth()
    {
        var earth = LoadBody("earth");
        var point = earth.GetSurfacePosition(0, 0, 95000);
        earth.GetGeodeticCoordinatesAtTime(point, earth.RotationalPeriod/4,
            out double latitude, out double longitude, out double altitude);

        Assert.Equal(0, latitude, 6);
        Assert.Equal(-90, longitude, 6);
        Assert.Equal(95000, altitude, 3);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    public void PolarLatitudeAndHeightRemainFinite(double latitude)
    {
        var earth = LoadBody("earth");
        var point = earth.GetSurfacePositionAtTime(latitude, 170, 12345, 95000);
        earth.GetGeodeticCoordinatesAtTime(point, 12345, out double actualLatitude,
            out double longitude, out double altitude);

        Assert.Equal(latitude, actualLatitude, 6);
        Assert.Equal(95000, altitude, 3);
        Assert.True(double.IsFinite(longitude)); // Longitude has no unique meaning at a pole.
    }

    [Fact]
    public void NonFiniteRotationEpochIsRejected()
    {
        var earth = LoadBody("earth");
        Assert.Throws<ArgumentOutOfRangeException>(() => earth.GetGeodeticCoordinatesAtTime(
            earth.GetSurfacePosition(0, 0), double.NaN, out _, out _, out _));
    }

    private static CelestialBody LoadBody(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data")))
            directory = directory.Parent;
        return CelestialBody.LoadFromJson(Path.Combine(directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository data not found."), "data", "bodies", id+".json"));
    }
}
