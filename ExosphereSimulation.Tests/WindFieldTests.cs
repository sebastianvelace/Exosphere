namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E4.1: the measured AS-506 launch-day wind (IGRA rawinsonde 1969-07-16 14Z)
/// enters through the single air point. Analytic checks: the meteorological convention, the
/// profile at its own levels, the horizontal and vertical fades, and Galilean invariance of the
/// aerodynamics (a vehicle resting in a wind W feels exactly what a vehicle moving at −W through
/// still air feels).
/// </summary>
public sealed class WindFieldTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E4")]
    [Trait("GateKind", "analytic")]
    public void AerodynamicsAreGalileanUnderWind()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        universe.UseWindProfile(DataDirectory(), "wind_as506");
        var earth = universe.GetBody("earth")!;
        var site = LaunchSite.LoadAllFromDirectory(Path.Combine(DataDirectory(), "launch_sites"))["kennedy"];
        var vessel = TestVessel();
        var position = site.GetPosition(earth, universe.CurrentTime) + earth.GetGeodeticUp(site.GetPosition(earth)) * 11_000.0;
        var ground = earth.Velocity + earth.GetSurfaceVelocity(position);
        var wind = earth.Wind!.WindAt(earth, position);
        Assert.InRange(wind.Magnitude, 5.0, 8.0); // ~7 m/s from the WNW near 11 km

        // At rest on the ground frame in the wind vs moving at −W in still air.
        var inWind = vessel.ComputeDragAt(position, ground, earth);
        earth.Wind = null;
        var stillAir = vessel.ComputeDragAt(position, ground - wind, earth);
        output.WriteLine($"wind {wind.Magnitude:F2} m/s, drag {inWind.Magnitude:F2} N vs {stillAir.Magnitude:F2} N");
        Assert.True((inWind - stillAir).Magnitude <= 1e-9 * inWind.Magnitude + 1e-12,
            $"{(inWind - stillAir).Magnitude} N");
        Assert.True(inWind.Magnitude > 0.0);
    }

    [Fact]
    public void ProfileFollowsTheSoundingAndItsConvention()
    {
        var wind = ProfileWind.LoadFromDataDirectory(DataDirectory(), "wind_as506");
        // Surface: from 180° at 1.0 m/s, i.e. blowing toward the north.
        var (east, north) = wind.ProfileAt(0.0);
        Assert.Equal(0.0, east, 9);
        Assert.Equal(1.0, north, 9);
        // 250 hPa level (10 987 m geometric): from 295° at 7.0 m/s.
        var (e2, n2) = wind.ProfileAt(10_968.0 * 6356766.0 / (6356766.0 - 10_968.0));
        Assert.Equal(7.0, System.Math.Sqrt(e2 * e2 + n2 * n2), 9);
        double toward = System.Math.Atan2(e2, n2) * 180.0 / System.Math.PI;
        Assert.Equal(115.0, toward, 6); // from 295° blows toward 115°
        // Faded to zero 5 km above the top level (34 km).
        var (e3, n3) = wind.ProfileAt(40_000.0);
        Assert.Equal(0.0, e3);
        Assert.Equal(0.0, n3);
    }

    [Fact]
    public void Hwm14ContinuesTheSoundingAboveItsTop()
    {
        var sounding = ProfileWind.LoadFromDataDirectory(DataDirectory(), "wind_as506");
        var hwm = ProfileWind.LoadFromDataDirectory(DataDirectory(), "hwm_as506");
        sounding.WithUpper(hwm);
        double top = 33_861.0 * 6356766.0 / (6356766.0 - 33_861.0);
        // At the sounding top: the sounding; 5 km above: HWM14 alone; continuous in between.
        var (e0, n0) = sounding.ProfileAt(top);
        Assert.Equal(32.0, System.Math.Sqrt(e0 * e0 + n0 * n0), 9);
        var (e1, n1) = sounding.ProfileAt(top + ProfileWind.TopFadeM + 1.0);
        var (he, hn) = hwm.ProfileAt(top + ProfileWind.TopFadeM + 1.0);
        Assert.Equal(he, e1, 12);
        Assert.Equal(hn, n1, 12);
        var (eMid, nMid) = sounding.ProfileAt(top + 1.0);
        Assert.True(System.Math.Abs(eMid - e0) < 0.05 && System.Math.Abs(nMid - n0) < 0.05);
        // The two independent sources agree at the seam: summer stratospheric easterlies.
        var (hTopE, hTopN) = hwm.ProfileAt(top);
        double hwmFrom = (System.Math.Atan2(-hTopE, -hTopN) * 180.0 / System.Math.PI + 360.0) % 360.0;
        output.WriteLine($"seam {top / 1000:F1} km: sounding 32.0 m/s from 95°, HWM14 {System.Math.Sqrt(hTopE * hTopE + hTopN * hTopN):F1} m/s from {hwmFrom:F0}°");
        Assert.InRange(hwmFrom, 75.0, 115.0);
        // 100 km: lower-thermosphere winds of tens of m/s, absent before E4.2.
        var (e100, n100) = sounding.ProfileAt(100_000.0);
        Assert.True(System.Math.Sqrt(e100 * e100 + n100 * n100) > 10.0);
    }

    [Fact]
    public void WindIsLocalToItsStationAndAbsentByDefault()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        Assert.Null(earth.Wind);
        universe.UseWindProfile(DataDirectory(), "wind_as506");
        // 1000 km away (Starbase is ~1500 km from the Cape) the profile no longer blows.
        var far = earth.GetSurfacePositionAtTime(25.997, -97.157, universe.CurrentTime, 11_000.0);
        Assert.Equal(Vector3d.Zero, earth.Wind!.WindAt(earth, far));
        var near = earth.GetSurfacePositionAtTime(28.6, -80.6, universe.CurrentTime, 11_000.0);
        Assert.True(earth.Wind.WindAt(earth, near).Magnitude > 5.0);
        // The air point is surface + wind; the ground is untouched.
        Assert.Equal(earth.GetSurfaceVelocity(near) + earth.Wind.WindAt(earth, near), earth.GetAirVelocity(near));
    }

    private static Vessel TestVessel()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(DataDirectory(), "parts"));
        return VehicleVariantDefinition.LoadFromJson(Path.Combine(DataDirectory(), "vehicles",
            "starship_flight12_v3_2026.json")).Build(catalog).ToVessel("Wind fixture");
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
