namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// The spin axis must be tilted from the ecliptic normal, not from an axis lying in the
/// orbital plane. These scenarios measure observable consequences: seasonal solar
/// declination, daily solar elevation at a real launch site and the Moon's inclination
/// to Earth's equator.
/// </summary>
public sealed class PlanetaryAxisOrientationTests
{
    private const double SecondsPerDay = 86_400.0;

    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("jupiter")]
    [InlineData("venus")]
    public void SpinAxisMakesItsAxialTiltWithTheEclipticNormal(string bodyId)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var body = universe.GetBody(bodyId)!;
        var eclipticNormal = new Vector3d(0.0, 0.0, 1.0);
        double angle = System.Math.Acos(System.Math.Clamp(
            body.RotationAxis.Dot(eclipticNormal), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
        Assert.Equal(body.AxialTilt, angle, 6);
    }

    [Theory]
    [InlineData(0.0, -23.0)]     // 2000-01-01 12:00 TT
    [InlineData(79.5, 0.0)]      // March equinox
    [InlineData(172.0, 23.44)]   // June solstice
    [InlineData(265.5, 0.0)]     // September equinox
    [InlineData(355.0, -23.44)]  // December solstice
    public void SolarDeclinationFollowsTheSeasons(double days, double expectedDegrees)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        universe.SetSimulationTime(days * SecondsPerDay);
        var earth = universe.GetBody("earth")!;
        var sun = universe.GetBody("sun")!;
        var toSun = (sun.Position - earth.Position).Normalized;
        double declination = System.Math.Asin(toSun.Dot(earth.RotationAxis)) * MathUtils.RAD_TO_DEG;
        Assert.InRange(declination, expectedDegrees - 0.7, expectedDegrees + 0.7);
    }

    [Theory]
    [InlineData(172.0, 87.5)]  // June solstice at 26 N: Sun almost overhead
    [InlineData(355.0, 40.6)]  // December solstice: 90 - 26 - 23.44
    public void StarbaseNoonSolarElevationMatchesTheSeason(double days, double expectedMaximum)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var sun = universe.GetBody("sun")!;
        double maximum = double.NegativeInfinity, minimum = double.PositiveInfinity;
        for (int sample = 0; sample < 24 * 6; sample++)
        {
            universe.SetSimulationTime(days * SecondsPerDay + sample * 600.0);
            var site = earth.GetSurfacePositionAtTime(25.997, -97.157, universe.CurrentTime);
            var up = earth.GetGeodeticUp(site);
            double elevation = System.Math.Asin((sun.Position - site).Normalized.Dot(up))
                * MathUtils.RAD_TO_DEG;
            maximum = System.Math.Max(maximum, elevation);
            minimum = System.Math.Min(minimum, elevation);
        }

        Assert.InRange(maximum, expectedMaximum - 1.5, expectedMaximum + 1.5);
        Assert.True(minimum < -15.0, $"The Sun never set (minimum elevation {minimum:F1} deg).");
    }

    [Fact]
    public void MoonOrbitIsInclinedToEarthsEquatorByTheRealRange()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        var normal = (moon.Position - earth.Position).Cross(moon.Velocity - earth.Velocity).Normalized;
        double inclination = System.Math.Acos(normal.Dot(earth.RotationAxis)) * MathUtils.RAD_TO_DEG;
        Assert.InRange(inclination, 18.2, 28.7);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
