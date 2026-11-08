namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E2: dated DE440 states against SPICE (de440s.bsp, read independently of the
/// Chebyshev product) at 70 instants 1960–2050 including both sides of every span length's
/// boundaries, and the geocentric Moon against JPL Horizons (DE441) at the Apollo 11 launch.
/// Tolerances: geocentric Moon 1 mm; Earth, Sun and the Earth-Moon barycentre 1 cm; planetary
/// barycentres 2 cm; velocities 2 µm/s (the measured interpolation error is below each).
/// </summary>
public sealed class DatedEphemerisTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E2")]
    [Trait("GateKind", "analytic")]
    public void ChebyshevStatesMatchSpiceFrom1960To2050()
    {
        var ephemeris = ChebyshevEphemeris.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        using var fixture = Fixture();
        var worst = new Dictionary<string, (double P, double V)>();
        foreach (var instant in fixture.RootElement.GetProperty("instants").EnumerateArray())
        {
            double t = instant.GetProperty("tdb_s").GetDouble();
            foreach (var body in instant.GetProperty("states").EnumerateObject())
            {
                Vector3d position, velocity;
                if (body.Name == "moon_geocentric")
                {
                    Assert.True(ephemeris.TryState("moon", t, out var moon, out var moonV));
                    Assert.True(ephemeris.TryState("earth", t, out var earth, out var earthV));
                    position = moon - earth;
                    velocity = moonV - earthV;
                }
                else
                {
                    Assert.True(ephemeris.TryState(body.Name, t, out position, out velocity), body.Name);
                }
                double p = (position - ReadVector(body.Value.GetProperty("position_m"))).Magnitude;
                double v = (velocity - ReadVector(body.Value.GetProperty("velocity_mps"))).Magnitude;
                double tolerance = body.Name switch
                {
                    "moon_geocentric" => 0.001,
                    "earth" or "moon" or "sun" => 0.01,
                    _ => 0.02,
                };
                string note = instant.GetProperty("note").GetString()!;
                Assert.True(p <= tolerance, $"{body.Name} at {note} ({t}): {p * 1000:F3} mm");
                Assert.True(v <= 2e-6, $"{body.Name} at {note} ({t}): {v * 1e6:F3} µm/s");
                var (wp, wv) = worst.GetValueOrDefault(body.Name);
                worst[body.Name] = (System.Math.Max(wp, p), System.Math.Max(wv, v));
            }
        }
        foreach (var (name, (p, v)) in worst.OrderBy(kv => kv.Key))
            output.WriteLine($"{name}: {p * 1000:F3} mm, {v * 1e6:F3} µm/s");
    }

    [Fact]
    public void GeocentricMoonAtTheApollo11LaunchAgreesWithHorizons()
    {
        // Horizons runs DE441 through JPL's own software: an independent check of the data path.
        var ephemeris = ChebyshevEphemeris.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        using var fixture = Fixture();
        var horizons = fixture.RootElement.GetProperty("horizons_moon_apollo11");
        double t = horizons.GetProperty("tdb_s").GetDouble();
        Assert.True(ephemeris.TryState("moon", t, out var moon, out var moonV));
        Assert.True(ephemeris.TryState("earth", t, out var earth, out var earthV));
        double p = (moon - earth - ReadVector(horizons.GetProperty("position_m"))).Magnitude;
        double v = (moonV - earthV - ReadVector(horizons.GetProperty("velocity_mps"))).Magnitude;
        output.WriteLine($"Moon vs Horizons DE441 at the Apollo 11 launch: {p:F3} m, {v * 1000:F4} mm/s");
        Assert.True(p < 1.0, $"{p} m");
        Assert.True(v < 1e-4, $"{v} m/s");
    }

    [Fact]
    public void UniverseRunsOnTheDatedEphemerisAndFallsBackToKeplerOutsideIt()
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        universe.UseDatedEphemeris(Path.Combine(Root(), "data"));
        Assert.Equal("de440", universe.Environment.Ephemeris.Id);
        var bodies = universe.Bodies.ToList();
        double t = -962_536_000.0;
        universe.SetCurrentTime(t);
        var ephemeris = (ChebyshevEphemeris)universe.Environment.Ephemeris;
        foreach (var body in bodies)
        {
            Assert.True(ephemeris.TryState(body.Id, t, out var p, out var v), body.Id);
            Assert.Equal(p, body.Position);
            Assert.Equal(v, body.Velocity);
            Assert.Equal(t, body.StateTime);
            var (sp, sv) = ephemeris.StateAt(body, t + 3600.0, id => bodies.FirstOrDefault(b => b.Id == id));
            Assert.True(ephemeris.TryState(body.Id, t + 3600.0, out var p2, out var v2));
            Assert.Equal(p2, sp);
            Assert.Equal(v2, sv);
        }
        // Earth-Moon distance on 1969-07-16 (~369 000 km near perigee per DE440).
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        Assert.InRange((moon.Position - earth.Position).Magnitude, 3.5e8, 4.1e8);

        // Outside 1960–2051 the whole system rides Kepler consistently.
        universe.SetCurrentTime(-1.6e9);
        Assert.False(ephemeris.Covers(-1.6e9));
        Assert.InRange((universe.GetBody("moon")!.Position - universe.GetBody("earth")!.Position).Magnitude, 3.5e8, 4.1e8);
    }

    [Fact]
    public void SpanBoundariesAreContinuous()
    {
        // Lobatto fits share the DE440 value at every span end (1 µm in the product itself). Here the
        // geocentric Moon is a difference of barycentric vectors of ~1.5e11 m, whose double
        // resolution is ~30 µm, so a few units of rounding remain.
        var ephemeris = ChebyshevEphemeris.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        double first = (2436934.5 - 2451545.0) * 86400.0;
        double worst = 0.0;
        for (int i = 1; i < 400; i++)
        {
            double boundary = first + i * 8 * 86400.0;
            ephemeris.TryState("moon", boundary - 1e-6, out var a, out var va);
            ephemeris.TryState("moon", boundary + 1e-6, out var b, out _);
            ephemeris.TryState("earth", boundary - 1e-6, out var ea, out var vea);
            ephemeris.TryState("earth", boundary + 1e-6, out var eb, out _);
            // Remove the 2 µs of motion between the two samples (~2 mm at lunar speed).
            var jump = (b - eb) - (a - ea) - (va - vea) * 2e-6;
            worst = System.Math.Max(worst, jump.Magnitude);
        }
        output.WriteLine($"Largest geocentric Moon jump across 8-day boundaries: {worst * 1000:F3} mm");
        Assert.True(worst < 5e-4, $"{worst} m");
    }

    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
        "ExosphereSimulation.Tests", "Fixtures", "environment", "ephemeris.json")));

    private static Vector3d ReadVector(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
