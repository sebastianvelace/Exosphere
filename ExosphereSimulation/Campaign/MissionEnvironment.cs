namespace Exosphere.Simulation.Campaign;

using Exosphere.Simulation.Environment;

/// <summary>
/// Puts a historical mission in its dated environment (environment plan E5): the IERS Earth
/// orientation and the DE440 ephemeris, the clock at the launch epoch (TDB), and the launch-day
/// wind and thermosphere products the mission declares. A mission without
/// <see cref="MissionDefinition.DatedEnvironment"/> keeps the default environment.
/// </summary>
public static class MissionEnvironment
{
    /// <param name="setLaunchEpoch">False when a save restores the clock itself.</param>
    /// <returns>True when the dated environment was applied.</returns>
    public static bool Apply(Universe universe, MissionDefinition mission, string dataDirectory,
        bool setLaunchEpoch = true)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.DatedEnvironment is not { } dated) return false;
        universe.UseIersEarthOrientation(dataDirectory);
        universe.UseDatedEphemeris(dataDirectory);
        if (!string.IsNullOrWhiteSpace(dated.WindProfileId))
            universe.UseWindProfile(dataDirectory, dated.WindProfileId,
                string.IsNullOrWhiteSpace(dated.UpperWindProfileId) ? null : dated.UpperWindProfileId);
        if (!string.IsNullOrWhiteSpace(dated.ThermosphereId))
            universe.UseThermosphere(dataDirectory, dated.ThermosphereId);
        if (setLaunchEpoch)
            universe.SetCurrentTime(mission.LaunchEpochTdbSeconds(TimeScales.LoadFromDataDirectory(dataDirectory)));
        return true;
    }
}
