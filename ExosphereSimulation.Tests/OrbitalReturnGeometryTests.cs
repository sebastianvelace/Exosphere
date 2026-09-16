namespace ExosphereSimulation.Tests;

using System;
using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class OrbitalReturnGeometryTests
{
    [Fact]
    public void CircularReturnSeedReachesFutureSiteRadialAfterHalfPeriod()
    {
        var earth = CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", "earth.json"));
        var site = LaunchSite.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "launch_sites", "starbase.json"));

        const double now = 0.0;
        const double altitude = 1_200_000.0;
        const double expectedReturnSeconds = 3_300.0;
        const double latitudeBiasDegrees = -1.0;
        const double longitudeLeadDegrees = 30.0;
        double radius = earth.Radius + altitude;
        Vector3d siteNow = site.GetPosition(earth, now) - earth.Position;
        Vector3d returnSite = earth.GetSurfacePositionAtTime(
            site.Latitude + latitudeBiasDegrees, site.Longitude + longitudeLeadDegrees,
            expectedReturnSeconds, site.Altitude)
            - earth.Position;
        Vector3d siteNowUp = siteNow.Normalized;
        Vector3d returnUp = returnSite.Normalized;
        Vector3d orbitNormal = siteNowUp.Cross(returnUp).Normalized;
        Vector3d burnRadial = -returnUp;
        Vector3d tangent = orbitNormal.Cross(burnRadial).Normalized;
        Vector3d position = burnRadial * radius;
        Vector3d velocity = tangent * Math.Sqrt(earth.GM / radius);
        var orbit = OrbitalElements.FromStateVector(
            position, velocity, earth.GM, earth.Id, now);
        double halfPeriod = Math.PI * Math.Sqrt(radius * radius * radius / earth.GM);

        Vector3d propagated = orbit.GetStateAtTime(now + halfPeriod, earth.GM).position.Normalized;

        Assert.True(propagated.Dot(returnUp) > 1.0 - 1e-10,
            $"half-period radial miss: dot={propagated.Dot(returnUp):F12}");
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
