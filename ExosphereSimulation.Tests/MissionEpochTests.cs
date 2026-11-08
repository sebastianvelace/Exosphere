namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Environment;
using Xunit;

/// <summary>
/// Environment plan E1.3: every mission states its launch epoch to the second, on its
/// historical date, citing one of its own sources, and converts once to TDB.
/// </summary>
public sealed class MissionEpochTests
{
    [Theory]
    [InlineData("freedom7_1961", 1961, 5, 5, 14, 34, 13.0)]
    [InlineData("friendship7_1962", 1962, 2, 20, 14, 47, 39.0)]
    [InlineData("gemini8_1966", 1966, 3, 16, 16, 41, 2.0)]
    [InlineData("apollo8_1968", 1968, 12, 21, 12, 51, 0.0)]
    [InlineData("apollo11_1969", 1969, 7, 16, 13, 32, 0.0)]
    public void MissionsCarryTheirPublishedLaunchEpoch(string file, int year, int month, int day, int hour, int minute, double second)
    {
        var mission = MissionDefinition.LoadFromJson(Path.Combine(DataDirectory(), "missions", file + ".json"));
        Assert.Equal(TimeScales.UtcSecondsFromCalendar(year, month, day, hour, minute, second), mission.LaunchEpochUtcSeconds());
        Assert.Contains(mission.Sources, s => s.Id == mission.LaunchEpochSourceId);
        Assert.False(string.IsNullOrWhiteSpace(mission.LaunchEpochEvidence));
    }

    [Fact]
    public void Apollo11LaunchEpochInTdb()
    {
        var scales = TimeScales.LoadFromDataDirectory(DataDirectory());
        var mission = MissionDefinition.LoadFromJson(Path.Combine(DataDirectory(), "missions", "apollo11_1969.json"));
        double utc = mission.LaunchEpochUtcSeconds();
        double tdb = mission.LaunchEpochTdbSeconds(scales);
        // TDB - UTC = (TAI - UTC) + 32.184 s + (TDB - TT): 7.5635 + 32.184 + ~0.0016 s.
        Assert.InRange(tdb - utc, 39.745, 39.752);
        Assert.Equal(utc, scales.TdbToUtc(tdb), 6);
    }

    [Fact]
    public void LaunchEpochMustFallOnTheHistoricalDateAndCiteASource()
    {
        string json = File.ReadAllText(Path.Combine(DataDirectory(), "missions", "apollo11_1969.json"));
        var wrongDay = Path.GetTempFileName();
        var unknownSource = Path.GetTempFileName();
        try
        {
            File.WriteAllText(wrongDay, json.Replace("\"1969-07-16T13:32:00Z\"", "\"1969-07-17T13:32:00Z\""));
            Assert.Throws<InvalidDataException>(() => MissionDefinition.LoadFromJson(wrongDay));
            File.WriteAllText(unknownSource, json.Replace("\"launchEpochSourceId\": \"apollo11-mission-report\"",
                "\"launchEpochSourceId\": \"not-a-source\""));
            Assert.Throws<InvalidDataException>(() => MissionDefinition.LoadFromJson(unknownSource));
        }
        finally
        {
            File.Delete(wrongDay);
            File.Delete(unknownSource);
        }
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
