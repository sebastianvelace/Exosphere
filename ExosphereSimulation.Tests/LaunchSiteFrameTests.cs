namespace ExosphereSimulation.Tests;

using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// RF-01: the launch frame. A pad's latitude fixes the free eastward velocity the body's
/// rotation hands the vehicle (ω·R·cos φ). These tests pin that boost to the real number
/// for Kennedy and guard the frame invariants the ascent guidance depends on.
///
/// Regression they exist to prevent: the pad used to spawn along the inertial +Y axis,
/// which — because the spin axis is +Y tilted by the 23.44° axial tilt — sat at latitude
/// 66.6°, near the arctic circle. It inherited 185 m/s instead of 408 m/s.
/// </summary>
public class LaunchSiteFrameTests
{
    private const double EarthEquatorialSpeed = 464.6;  // ω·R at the equator (m/s)

    [Fact]
    public void Kennedy_InheritsRealEastwardBoost()
    {
        var earth  = LoadBody("earth");
        var kennedy = LoadSite("kennedy");

        double boost = kennedy.GetRotationalBoost(earth);

        // Acceptance criterion from the astronaut realism master plan (RF-01).
        Assert.InRange(boost, 405.0, 411.0);
    }

    [Fact]
    public void Kennedy_BoostMatchesClosedFormOmegaRCosLat()
    {
        var earth   = LoadBody("earth");
        var kennedy = LoadSite("kennedy");

        double expected = earth.AngularSpeed * earth.Radius
                        * System.Math.Cos(kennedy.Latitude * MathUtils.DEG_TO_RAD);

        Assert.Equal(expected, kennedy.GetRotationalBoost(earth), 1);
    }

    [Fact]
    public void PadSpawn_SitsAtTheLatitudeTheDataClaims()
    {
        var earth   = LoadBody("earth");
        var kennedy = LoadSite("kennedy");

        double actualLat = earth.GetLatitude(kennedy.GetPosition(earth));

        // The old +Y spawn reported 66.56° here while the data said 28.61°.
        Assert.Equal(kennedy.Latitude, actualLat, 3);
    }

    [Fact]
    public void RotationalBoost_PointsDueEast()
    {
        var earth   = LoadBody("earth");
        var kennedy = LoadSite("kennedy");

        var pos  = kennedy.GetPosition(earth);
        var vel  = earth.GetSurfaceVelocity(pos);
        var east = earth.GetEastDirection(pos);
        var up   = kennedy.GetUpDirection(earth);

        // The boost is purely eastward: fully aligned with east, nothing radial.
        Assert.Equal(1.0, vel.Normalized.Dot(east), 6);
        Assert.Equal(0.0, vel.Normalized.Dot(up),   6);
    }

    [Fact]
    public void EastIsPerpendicularToUpAndToTheSpinAxis()
    {
        var earth = LoadBody("earth");
        var pos   = earth.GetSurfacePosition(28.6, -80.6);
        var east  = earth.GetEastDirection(pos);
        var up    = (pos - earth.Position).Normalized;

        Assert.Equal(0.0, east.Dot(up),                 6);
        Assert.Equal(0.0, east.Dot(earth.RotationAxis), 6);
        Assert.Equal(1.0, east.Magnitude,               6);
    }

    [Theory]
    [InlineData(0.0)]     // equator: the full ride
    [InlineData(28.6)]    // Kennedy
    [InlineData(45.9)]    // Baikonur
    [InlineData(-33.0)]   // southern hemisphere: same boost, still eastward
    public void BoostFollowsCosineOfLatitude(double latitude)
    {
        var earth = LoadBody("earth");
        var pos   = earth.GetSurfacePosition(latitude, 0.0);

        double expected = EarthEquatorialSpeed * System.Math.Cos(latitude * MathUtils.DEG_TO_RAD);
        double actual   = earth.GetSurfaceVelocity(pos).Magnitude;

        Assert.Equal(expected, actual, 0);
        Assert.True(earth.GetSurfaceVelocity(pos).Dot(earth.GetEastDirection(pos)) > 0.0,
            "rotation carries the surface east in both hemispheres");
    }

    [Fact]
    public void PolarPad_InheritsNothing()
    {
        var earth = LoadBody("earth");
        var pole  = earth.Position + earth.RotationAxis * earth.Radius;

        Assert.Equal(0.0, earth.GetSurfaceVelocity(pole).Magnitude, 3);
    }

    [Fact]
    public void SurfacePosition_RoundTripsThroughLatitude()
    {
        var earth = LoadBody("earth");

        foreach (double lat in new[] { -60.0, -28.6, 0.0, 28.6, 60.0 })
        foreach (double lon in new[] { -170.0, -80.6, 0.0, 90.0, 175.0 })
        {
            var pos = earth.GetSurfacePosition(lat, lon);
            Assert.Equal(lat, earth.GetLatitude(pos), 6);
        }
    }

    [Fact]
    public void SurfacePosition_HonoursPadAltitude()
    {
        var earth   = LoadBody("earth");
        var kennedy = LoadSite("kennedy");

        double r = (kennedy.GetPosition(earth) - earth.Position).Magnitude;

        Assert.Equal(earth.Radius + kennedy.Altitude, r, 3);
    }

    [Fact]
    public void LaunchSiteData_LoadsFromDisk()
    {
        var sites = LaunchSite.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "launch_sites"));

        Assert.True(sites.ContainsKey("kennedy"), "kennedy.json should load");
        Assert.Equal("earth", sites["kennedy"].BodyId);
        Assert.Equal(28.608389, sites["kennedy"].Latitude, 6);
    }

    [Fact]
    public void KennedyRenderFrameIsOrthonormalRightHandedAndRadiallyUpright()
    {
        var earth = LoadBody("earth");
        var frame = LoadSite("kennedy").GetLocalFrame(earth);

        Assert.Equal(1.0, frame.East.Magnitude, 12);
        Assert.Equal(1.0, frame.Up.Magnitude, 12);
        Assert.Equal(1.0, frame.South.Magnitude, 12);
        Assert.Equal(0.0, frame.East.Dot(frame.Up), 12);
        Assert.Equal(0.0, frame.Up.Dot(frame.South), 12);
        Assert.Equal(0.0, frame.South.Dot(frame.East), 12);
        Assert.Equal(1.0, frame.Determinant, 12);
        Assert.Equal(frame.North, frame.Up.Cross(frame.East));
    }

    [Fact]
    public void PadOriginIsDirectlyBelowGroundHeldVesselInLocalFrame()
    {
        var earth = LoadBody("earth");
        var site = LoadSite("kennedy");
        var frame = site.GetLocalFrame(earth);
        var surface = site.GetPosition(earth);
        var vessel = surface + frame.Up * 12.0;
        var offset = surface - vessel;

        Assert.Equal(-12.0, offset.Dot(frame.Up), 8);
        Assert.Equal(0.0, offset.Dot(frame.East), 8);
        Assert.Equal(0.0, offset.Dot(frame.North), 8);
    }

    [Theory]
    [InlineData(-60.0, -170.0)]
    [InlineData(-28.6, 90.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(28.6, -80.6)]
    [InlineData(60.0, 175.0)]
    public void SurfaceFramesRemainRightHandedAcrossTheGlobe(double latitude, double longitude)
    {
        var earth = LoadBody("earth");
        var site = new LaunchSite { Latitude = latitude, Longitude = longitude };
        var frame = site.GetLocalFrame(earth);

        Assert.Equal(1.0, frame.Determinant, 10);
        Assert.Equal(0.0, frame.East.Dot(frame.Up), 10);
        Assert.Equal(0.0, frame.Up.Dot(frame.South), 10);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static LaunchSite LoadSite(string id) =>
        LaunchSite.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "launch_sites", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("repo root not found");
    }
}
