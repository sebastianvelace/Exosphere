namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Environment;
using Xunit;

/// <summary>
/// Environment plan E5.2: every historical mission declares its dated environment, and applying
/// it sets the launch epoch, the IERS orientation, DE440 and the declared launch-day products.
/// </summary>
public sealed class MissionEnvironmentTests
{
    [Fact]
    public void EveryHistoricalMissionFliesItsDatedEnvironment()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(DataDirectory(), "missions"), "*.json"))
        {
            var mission = MissionDefinition.LoadFromJson(path);
            Assert.NotNull(mission.DatedEnvironment);
            foreach (var (folder, id) in new[]
                     {
                         ("winds", mission.DatedEnvironment!.WindProfileId),
                         ("winds", mission.DatedEnvironment.UpperWindProfileId),
                         ("atmosphere", mission.DatedEnvironment.ThermosphereId),
                     })
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                Assert.True(File.Exists(Path.Combine(DataDirectory(), "environment", folder, id + ".bin")),
                    $"{mission.Id}: {folder}/{id}.bin");
            }
        }
    }

    [Fact]
    public void Apollo11StartsAtRangeZeroInItsDatedEnvironment()
    {
        var mission = MissionDefinition.LoadFromJson(Path.Combine(DataDirectory(), "missions", "apollo11_1969.json"));
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        Assert.True(MissionEnvironment.Apply(universe, mission, DataDirectory()));
        var scales = TimeScales.LoadFromDataDirectory(DataDirectory());
        Assert.Equal(scales.UtcToTdb(TimeScales.UtcSecondsFromCalendar(1969, 7, 16, 13, 32, 0.0)), universe.CurrentTime);
        var earth = universe.GetBody("earth")!;
        Assert.IsType<IersOrientationProvider>(earth.Orientation);
        Assert.IsType<ChebyshevEphemeris>(universe.Environment.Ephemeris);
        Assert.IsType<ProfileWind>(earth.Wind);
        Assert.IsType<NrlmsisAtmosphereField>(earth.AtmosphereField);
        Assert.Equal(universe.CurrentTime, earth.StateTime);

        // A save restores the clock itself: the environment comes back, the time is untouched.
        var resumed = Universe.LoadFromDataDirectory(DataDirectory());
        resumed.SetCurrentTime(123.0);
        Assert.True(MissionEnvironment.Apply(resumed, mission, DataDirectory(), setLaunchEpoch: false));
        Assert.Equal(123.0, resumed.CurrentTime);
    }

    [Fact]
    public void MissionsWithoutLaunchDayProductsGetOnlyTheDatedFrames()
    {
        var mission = MissionDefinition.LoadFromJson(Path.Combine(DataDirectory(), "missions", "friendship7_1962.json"));
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        Assert.True(MissionEnvironment.Apply(universe, mission, DataDirectory()));
        var earth = universe.GetBody("earth")!;
        Assert.Null(earth.Wind);
        Assert.Null(earth.AtmosphereField);
        Assert.IsType<IersOrientationProvider>(earth.Orientation);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
