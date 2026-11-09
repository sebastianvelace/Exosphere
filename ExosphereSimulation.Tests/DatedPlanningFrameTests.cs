namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E5.1: planners and integration frames on the dated ephemeris. Planners keep
/// their conic interface and read elements osculating at the instant that matters to them; the
/// frame a vessel is integrated in accelerates with the pull of every other body, so it follows
/// DE440's own motion (Earth around the Earth–Moon barycentre). With the Kepler ephemeris nothing
/// changes: the planning elements are the body's own <see cref="CelestialBody.OrbitalElements"/>.
/// </summary>
public sealed class DatedPlanningFrameTests(ITestOutputHelper output)
{
    private static readonly double Apollo11LaunchTdb =
        TimeScales.LoadFromDataDirectory(DataDirectory()).UtcToTdb(TimeScales.UtcSecondsFromCalendar(1969, 7, 16, 13, 32, 0.0));

    [Fact]
    public void KeplerPlanningElementsAreTheBodysOwn()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var moon = universe.GetBody("moon")!;
        Assert.Same(moon.OrbitalElements, moon.PlanningElementsAt(3.0e5));
    }

    [Fact]
    public void DatedPlanningElementsOsculateAtTheirInstant()
    {
        var universe = DatedUniverse();
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        var dated = ChebyshevEphemeris.LoadFromDataDirectory(DataDirectory());
        double arrival = Apollo11LaunchTdb + 75.83 * 3_600.0;
        var elements = moon.PlanningElementsAt(arrival)!;
        Assert.Equal("earth", elements.ReferenceBodyId);
        dated.TryState("moon", arrival, out var moonP, out var moonV);
        dated.TryState("earth", arrival, out var earthP, out var earthV);
        var (p, v) = elements.GetStateAtTime(arrival, earth.GM);
        Assert.True((p - (moonP - earthP)).Magnitude < 1e-3, $"{(p - (moonP - earthP)).Magnitude} m");
        Assert.True((v - (moonV - earthV)).Magnitude < 1e-6);
        // Three days earlier the osculating conic of the arrival is off by hundreds of km: why
        // planners must osculate where the encounter is decided, not at launch.
        dated.TryState("moon", Apollo11LaunchTdb, out var m0, out _);
        dated.TryState("earth", Apollo11LaunchTdb, out var e0, out _);
        double drift = (elements.GetStateAtTime(Apollo11LaunchTdb, earth.GM).position - (m0 - e0)).Magnitude;
        output.WriteLine($"arrival-osculating Moon at launch: {drift / 1000:F0} km from DE440");
        Assert.InRange(drift, 100_000.0, 2_000_000.0);
    }

    [Fact]
    [Trait("FidelityGate", "E2")]
    [Trait("GateKind", "analytic")]
    public void IntegrationFramesAccelerateAsTheEphemeris()
    {
        // The frame acceleration of Earth and of the Moon against DE440's own second derivative
        // (central difference of its analytic velocity, ±60 s). The pre-E5 parent chain left Earth
        // without the Moon's pull (3.3e-5 m/s²); the band is 1 % of that pull.
        var universe = DatedUniverse();
        var dated = ChebyshevEphemeris.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        double lunarPullOnEarth = moon.GM / (moon.Position - earth.Position).MagnitudeSquared;
        foreach (var body in new[] { earth, moon })
        {
            dated.TryState(body.Id, Apollo11LaunchTdb + 60.0, out _, out var after);
            dated.TryState(body.Id, Apollo11LaunchTdb - 60.0, out _, out var before);
            var ephemerisAcceleration = (after - before) / 120.0;
            var frame = universe.GetReferenceFrameAcceleration(body);
            double error = (frame - ephemerisAcceleration).Magnitude;
            output.WriteLine($"{body.Id}: frame {frame.Magnitude:E4}, DE440 {ephemerisAcceleration.Magnitude:E4} m/s², "
                + $"difference {error:E2} ({error / lunarPullOnEarth:P3} of the lunar pull on Earth)");
            Assert.True(error < 0.01 * lunarPullOnEarth, $"{body.Id}: {error:E3} m/s²");
        }
    }

    private static Universe DatedUniverse()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        universe.UseDatedEphemeris(DataDirectory());
        universe.SetCurrentTime(Apollo11LaunchTdb);
        return universe;
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
