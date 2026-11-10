namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E4.3: the tabulated NRLMSIS 2.1 thermosphere of the Apollo 11 days against
/// the model itself at 300 free points (instant, latitude, longitude, altitude 120–950 km; fixture
/// from tools/envdata/fixtures/msis.py). The interpolation must stay well inside NRLMSIS's own
/// ~15 % density uncertainty: density within 5 %, temperature within 2 %, pressure within 6 %.
/// Below 80 km the body's profile answers bit for bit, and the 80–120 km blend is continuous.
/// </summary>
public sealed class NrlmsisAtmosphereTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E4")]
    [Trait("GateKind", "analytic")]
    public void TabulatedThermosphereFollowsNrlmsis()
    {
        var field = NrlmsisAtmosphereField.LoadFromDataDirectory(DataDirectory(), "msis_as506");
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
            "ExosphereSimulation.Tests", "Fixtures", "environment", "msis_as506.json")));
        double worstRho = 0, worstT = 0, worstP = 0, sumRho = 0;
        string worstAt = "";
        int count = 0;
        foreach (var p in fixture.RootElement.GetProperty("points").EnumerateArray())
        {
            var u = p.GetProperty("utc").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double utc = TimeScales.UtcSecondsFromCalendar((int)u[0], (int)u[1], (int)u[2], (int)u[3], (int)u[4], u[5]);
            field.Sample(utc, p.GetProperty("latitude_deg").GetDouble(), p.GetProperty("longitude_deg").GetDouble(),
                p.GetProperty("altitude_m").GetDouble(), out double rho, out double temperature, out double pressure);
            double eRho = System.Math.Abs(rho / p.GetProperty("density").GetDouble() - 1);
            double eT = System.Math.Abs(temperature / p.GetProperty("temperature").GetDouble() - 1);
            double eP = System.Math.Abs(pressure / p.GetProperty("pressure").GetDouble() - 1);
            if (eRho > worstRho)
                worstAt = $"{string.Join('-', u.Take(5))} lat {p.GetProperty("latitude_deg").GetDouble():F0} lon {p.GetProperty("longitude_deg").GetDouble():F0} alt {p.GetProperty("altitude_m").GetDouble() / 1000:F0} km";
            worstRho = System.Math.Max(worstRho, eRho);
            worstT = System.Math.Max(worstT, eT);
            worstP = System.Math.Max(worstP, eP);
            sumRho += eRho;
            count++;
        }
        output.WriteLine($"{count} points: density worst {worstRho:P2} at {worstAt} (mean {sumRho / count:P2}), temperature {worstT:P2}, pressure {worstP:P2}");
        Assert.True(worstRho < 0.05, $"density {worstRho:P2}");
        Assert.True(worstT < 0.02, $"temperature {worstT:P2}");
        Assert.True(worstP < 0.06, $"pressure {worstP:P2}");
    }

    [Fact]
    public void BelowTheBlendTheProfileAnswersAndTheBlendIsContinuous()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        universe.UseIersEarthOrientation(DataDirectory());
        double utc = TimeScales.UtcSecondsFromCalendar(1969, 7, 16, 15, 0, 0.0);
        universe.SetCurrentTime(TimeScales.LoadFromDataDirectory(DataDirectory()).UtcToTdb(utc));
        var earth = universe.GetBody("earth")!;
        double Density(double altitude) => earth.AtmosphericDensityAt(
            earth.GetSurfacePositionAtTime(28.6, -80.6, earth.StateTime, altitude), altitude);
        double before = Density(60_000.0), beforeBlend = Density(79_999.0);
        universe.UseThermosphere(DataDirectory(), "msis_as506");
        Assert.Equal(before, Density(60_000.0));
        Assert.Equal(beforeBlend, Density(79_999.0));
        // Continuity across the blend edges (relative jump over 1 m).
        foreach (double edge in new[] { NrlmsisAtmosphereField.BlendBottomM, NrlmsisAtmosphereField.BlendTopM })
        {
            double below = Density(edge - 0.5), above = Density(edge + 0.5);
            Assert.True(System.Math.Abs(above / below - 1) < 1e-3, $"{edge}: {below} vs {above}");
        }
        // The dated thermosphere differs from the profile's tail where it matters for decay.
        double tail = earth.Atmosphere!.GetDensity(300_000.0);
        double dated = Density(300_000.0);
        output.WriteLine($"300 km over the Cape, 1969-07-16 15 UT: profile {tail:E3}, NRLMSIS {dated:E3} kg/m³");
        Assert.True(dated > 0 && double.IsFinite(dated));
    }

    /// <summary>
    /// IV&amp;V audit A-17. Engine back-pressure is an atmosphere reading like any other and must
    /// come through the body's single atmosphere point, so a dated field reaches the engines too.
    /// </summary>
    [Fact]
    public void EngineBackPressureReadsTheAtmosphereField()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = new Vessel
        {
            ReferenceBodyId = earth.Id,
            Position = earth.GetSurfacePosition(28.5, -80.6, 30_000.0),
        };
        double profile = earth.Atmosphere!.GetPressure(vessel.GetAltitude(earth));
        Assert.Equal(profile, vessel.GetAmbientPressure(earth));

        earth.AtmosphereField = new DoubledPressureField();
        Assert.Equal(2.0 * profile, vessel.GetAmbientPressure(earth));
    }

    private sealed class DoubledPressureField : IAtmosphereField
    {
        public string Id => "doubled-pressure";
        public double Density(CelestialBody body, Vector3d worldPosition, double altitude) =>
            body.Atmosphere!.GetDensity(altitude);
        public double Temperature(CelestialBody body, Vector3d worldPosition, double altitude) =>
            body.Atmosphere!.GetTemperature(altitude);
        public double Pressure(CelestialBody body, Vector3d worldPosition, double altitude) =>
            2.0 * body.Atmosphere!.GetPressure(altitude);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private static string DataDirectory() => Path.Combine(Root(), "data");
}
