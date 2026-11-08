namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E1.2b: with the IERS orientation, Earth's body frame is the ITRS of the
/// date. Historical gate: the inertial position and velocity of the Apollo 11, Friendship 7
/// and Starship Flight 4 pads at their launch epochs against SPICE (fixture from
/// tools/envdata/fixtures/dated_launch_sites.py). Position within 3σ of the epoch's published
/// C04 uncertainty, floored as in <see cref="IersEarthOrientationTests"/>; velocity within
/// 1 mm/s (ω×r against SPICE's full frame derivative).
/// </summary>
public sealed class DatedEarthOrientationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E1")]
    [Trait("GateKind", "historical")]
    public void LaunchPadsSitWhereSpicePutsThemOnTheDate()
    {
        var universe = DatedUniverse(out var provider);
        var earth = universe.GetBody("earth")!;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
            "ExosphereSimulation.Tests", "Fixtures", "environment", "dated_launch_sites.json")));
        var sites = LaunchSite.LoadAllFromDirectory(Path.Combine(Root(), "data", "launch_sites"));
        foreach (var c in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            string id = c.GetProperty("site").GetString()!;
            double tdb = c.GetProperty("tdb_s").GetDouble();
            universe.SetCurrentTime(tdb);
            Assert.Equal(tdb, earth.StateTime);
            var site = sites[id];
            var relative = site.GetPosition(earth, tdb) - earth.Position;
            var expected = ReadVector(c.GetProperty("position_m"));
            double error = (relative - expected).Magnitude;

            var sample = provider.SampleAt(tdb);
            double sigmaM = 6378137.0 * System.Math.Sqrt(sample.PoleSigmaRad * sample.PoleSigmaRad
                + System.Math.Pow(sample.Ut1SigmaSeconds * 7.292115e-5, 2));
            int year = c.GetProperty("utc")[0].GetInt32();
            double bound = System.Math.Max(year >= 1995 ? 0.5 : 1.5, 3.0 * sigmaM);
            Assert.True(error <= bound, $"{id}: {error:F3} m from SPICE (bound {bound:F3} m)");

            // The time-free pad position (the frame at StateTime) is the dated one.
            Assert.True((site.GetPosition(earth) - earth.Position - relative).Magnitude < 1e-6);

            var velocity = earth.GetSurfaceVelocity(site.GetPosition(earth, tdb));
            double velocityError = (velocity - ReadVector(c.GetProperty("velocity_mps"))).Magnitude;
            Assert.True(velocityError < 1e-3, $"{id}: surface velocity {velocityError * 1000:F4} mm/s from SPICE");
            output.WriteLine($"{c.GetProperty("note").GetString()}: {error:F3} m (bound {bound:F2} m), " +
                $"velocity {velocityError * 1000:F4} mm/s");
        }
    }

    [Fact]
    public void GeodeticCoordinatesRoundTripAndReadGreenwichLongitude()
    {
        var universe = DatedUniverse(out _);
        var earth = universe.GetBody("earth")!;
        double t = TimeScales.TtToTdb(-962_580_000.0);
        universe.SetCurrentTime(t);
        foreach (var (lat, lon, h) in new[] { (28.608389, -80.604333, 3.0), (-33.9, 151.2, 120.0), (0.0, 0.0, 0.0), (89.5, 45.0, 2000.0) })
        {
            var p = earth.GetSurfacePositionAtTime(lat, lon, t + 3600.0, h);
            earth.GetGeodeticCoordinatesAtTime(p, t + 3600.0, out double lat2, out double lon2, out double h2);
            // Metric round trip: angles near the pole are ill-conditioned, metres are not.
            // The single-step Bowring inversion leaves ~6 µm.
            var back = earth.GetSurfacePositionAtTime(lat2, lon2, t + 3600.0, h2);
            Assert.True((back - p).Magnitude < 1e-4, $"({lat}, {lon}, {h}): {(back - p).Magnitude} m");
            Assert.Equal(lat, lat2, 7);
            Assert.Equal(h, h2, 4);
        }
        // Greenwich (0°, 0°) on the equator lies in the ITRS x direction of the date.
        var greenwich = (earth.GetSurfacePositionAtTime(0.0, 0.0, t) - earth.Position).Normalized;
        var itrsX = earth.Orientation is IDatedBodyOrientation dated
            ? dated.BodyFixedToInertial(earth, t).Multiply(new Vector3d(1, 0, 0))
            : Vector3d.Zero;
        // World positions are heliocentric (~1.5e11 m), so subtracting the centre leaves ~1e-12.
        Assert.True((greenwich - itrsX).Magnitude < 1e-10, $"Greenwich off ITRS x by {(greenwich - itrsX).Magnitude:E3}");
    }

    [Fact]
    public void TheDatedSurfaceMovesAtTheSurfaceVelocity()
    {
        // d/dt of a pad's inertial position equals ω × r (the free ride launch physics uses).
        var universe = DatedUniverse(out _);
        var earth = universe.GetBody("earth")!;
        double t = TimeScales.TtToTdb(-962_580_000.0);
        universe.SetCurrentTime(t);
        var before = earth.GetSurfacePositionAtTime(28.608389, -80.604333, t - 0.5, 3.0);
        var after = earth.GetSurfacePositionAtTime(28.608389, -80.604333, t + 0.5, 3.0);
        var numeric = after - before;
        var analytic = earth.GetSurfaceVelocity(earth.GetSurfacePositionAtTime(28.608389, -80.604333, t, 3.0));
        Assert.True((numeric - analytic).Magnitude < 1e-3, $"{(numeric - analytic).Magnitude} m/s");
        Assert.InRange(analytic.Magnitude, 407.0, 409.0); // ω·a·cos φ at Kennedy
    }

    [Fact]
    public void FixedAxisEarthIsUntouchedWhenTheDatedModelIsNotSelected()
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        Assert.Same(FixedAxisRotation.Instance, universe.GetBody("earth")!.Orientation);
    }

    private static Universe DatedUniverse(out IersOrientationProvider provider)
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        universe.UseIersEarthOrientation(Path.Combine(Root(), "data"));
        provider = (IersOrientationProvider)universe.GetBody("earth")!.Orientation;
        return universe;
    }

    private static Vector3d ReadVector(JsonElement e) =>
        new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
