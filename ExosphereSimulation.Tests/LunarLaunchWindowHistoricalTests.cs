namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Navigation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E5.3, historical gate E2 (with E1): on its own date, the dated environment
/// must open Apollo 11's lunar launch window where history opened it. Apollo 11 lifted off at
/// the window's opening, Range Zero 13:32:00 GMT on 1969-07-16, on a 72.058° flight azimuth
/// (NASA SP-4029; KSC: "the next launch window ... opened at 9:32 AM EDT"). The window opens
/// when the plane through LC-39A and the Moon at LOI (075:49:50.37 GET, DE440) swings to the
/// 72° range-safety limit as the IERS Earth turns. At J2000 the same search returned an arbitrary
/// time. Band, fixed before running: ±20 min, the time the window takes to sweep ~2.7° of azimuth.
/// </summary>
public sealed class LunarLaunchWindowHistoricalTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E2")]
    [Trait("GateKind", "historical")]
    public void Apollo11WindowOpensAtTheHistoricalLiftoff()
    {
        var data = DataDirectory();
        var mission = MissionDefinition.LoadFromJson(Path.Combine(data, "missions", "apollo11_1969.json"));
        var universe = Universe.LoadFromDataDirectory(data);
        MissionEnvironment.Apply(universe, mission, data);
        double rangeZero = universe.CurrentTime;
        double searchFrom = rangeZero - 2.0 * 3_600.0;
        universe.SetCurrentTime(searchFrom);
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        var site = LaunchSite.LoadAllFromDirectory(Path.Combine(data, "launch_sites"))[mission.LaunchSiteId];
        var window = LunarLaunchWindow.FindNext(earth,
            moon.PlanningElementsAt(rangeZero + Apollo11FlightProfile.LoiIgnitionSeconds)!, site, searchFrom,
            Apollo11FlightProfile.LoiIgnitionSeconds, Apollo8FlightProfile.MinimumLaunchAzimuthDegrees,
            Apollo8FlightProfile.MaximumLaunchAzimuthDegrees, searchSpanSeconds: 6.0 * 3_600.0, stepSeconds: 10.0);
        Assert.NotNull(window);
        double offsetMinutes = (window!.Value.LiftoffTime - rangeZero) / 60.0;
        var atRangeZero = LunarLaunchWindow.Evaluate(earth, moon.PlanningElementsAt(rangeZero + Apollo11FlightProfile.LoiIgnitionSeconds)!,
            site, rangeZero, Apollo11FlightProfile.LoiIgnitionSeconds)!.Value;
        output.WriteLine($"window opens {offsetMinutes:+0.0;-0.0} min from 13:32:00 GMT at {window.Value.AzimuthDegrees:F2}°; "
            + $"required azimuth at Range Zero {atRangeZero.AzimuthDegrees:F2}° (flown {Apollo11FlightProfile.FlightAzimuthDegrees}°)");
        Assert.InRange(offsetMinutes, -20.0, 20.0);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
