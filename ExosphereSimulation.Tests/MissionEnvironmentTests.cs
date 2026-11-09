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

/// <summary>Environment plan E5: a save records its ephemeris and restores only onto the same one.</summary>
public sealed class DatedSaveTests
{
    [Fact]
    public void SavesRestoreOnlyOntoTheirOwnEphemeris()
    {
        var data = Path.Combine(new DirectoryInfo(Directory.GetCurrentDirectory()).EnumerateParents()
            .First(d => File.Exists(Path.Combine(d.FullName, "ExosphereSimulation.sln"))).FullName, "data");
        var catalog = Exosphere.Simulation.Construction.PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        var mission = MissionDefinition.LoadFromJson(Path.Combine(data, "missions", "apollo11_1969.json"));
        var dated = Universe.LoadFromDataDirectory(data);
        MissionEnvironment.Apply(dated, mission, data);
        for (int i = 0; i < 7; i++) dated.Tick(1.0 / 60.0); // carry some clock rounding
        var save = Exosphere.Simulation.Persistence.SaveGameV2Codec.Capture(dated);
        Assert.Equal("de440", save.EphemerisId);

        var kepler = Universe.LoadFromDataDirectory(data);
        Assert.Throws<InvalidDataException>(() => Exosphere.Simulation.Persistence.SaveGameV2Codec.Restore(kepler, save, catalog));
        var again = Universe.LoadFromDataDirectory(data);
        MissionEnvironment.Apply(again, mission, data, setLaunchEpoch: false);
        Exosphere.Simulation.Persistence.SaveGameV2Codec.Restore(again, save, catalog);
        Assert.Equal(dated.CurrentTime, again.CurrentTime);
        Assert.Equal(dated.GetBody("moon")!.Position, again.GetBody("moon")!.Position);
        // The resumed clock continues bit for bit with the uninterrupted one.
        for (int i = 0; i < 600; i++) { dated.Tick(1.0 / 60.0); again.Tick(1.0 / 60.0); }
        Assert.Equal(dated.CurrentTime, again.CurrentTime);

        // Pre-E5 saves carry no ephemeris: they were Kepler.
        save.EphemerisId = null;
        Exosphere.Simulation.Persistence.SaveGameV2Codec.Restore(Universe.LoadFromDataDirectory(data), save, catalog);
    }
}

internal static class DirectoryInfoExtensions
{
    public static IEnumerable<DirectoryInfo> EnumerateParents(this DirectoryInfo directory)
    {
        for (var d = directory; d != null; d = d.Parent) yield return d;
    }
}

/// <summary>Environment plan E5: the clock at dated (1969) epochs.</summary>
public sealed class DatedClockTests
{
    [Fact]
    public void ClockDoesNotDriftAtDatedEpochs()
    {
        // One hour of 60 Hz ticks at Apollo 11's epoch: a plain double sum drifted 3.4 ms.
        var universe = new Universe();
        const double start = -961_280_840.2528561;
        universe.SetCurrentTime(start);
        const int steps = 216_000;
        for (int i = 0; i < steps; i++) universe.Tick(1.0 / 60.0);
        double expected = (double)((decimal)start + steps * (1m / 60m));
        Assert.True(System.Math.Abs(universe.CurrentTime - expected) < 1e-6,
            $"{(universe.CurrentTime - expected) * 1e3:F4} ms");
    }

    [Fact]
    public void EpochToleranceScalesWithTheEpoch()
    {
        Assert.Equal(1e-7, SimulationClock.EpochTolerance(0.0));
        Assert.Equal(1e-7, SimulationClock.EpochTolerance(1.0e5));
        Assert.InRange(SimulationClock.EpochTolerance(-961_280_840.0), 1.5e-6, 2.5e-6);
        Assert.True(SimulationClock.SameEpoch(-961_280_840.0928561, -961_280_840.0928563));
        Assert.False(SimulationClock.SameEpoch(-961_280_840.0, -961_280_840.0 + 1.0 / 60.0));
    }
}
