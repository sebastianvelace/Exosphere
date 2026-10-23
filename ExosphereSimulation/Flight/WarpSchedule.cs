namespace Exosphere.Simulation.Flight;

/// <summary>
/// Time warp that arrives at an event (a launch window, a maneuver node) without
/// overshooting it: the highest level whose step per frame stays under a quarter of the
/// time remaining, so the warp steps down on its own as the event nears.
/// </summary>
public static class WarpSchedule
{
    /// <summary>Simulated seconds a frame may advance at warp x1, conservatively.</summary>
    public const double FrameSecondsAtUnitWarp = 0.5;

    public static int IndexFor(double remainingSeconds, IReadOnlyList<double> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (!double.IsFinite(remainingSeconds) || remainingSeconds <= 0.0) return 0;
        int index = 0;
        while (index + 1 < levels.Count
               && levels[index + 1] * FrameSecondsAtUnitWarp < 0.25 * remainingSeconds)
            index++;
        return index;
    }
}
