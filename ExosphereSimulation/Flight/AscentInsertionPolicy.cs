namespace Exosphere.Simulation.Flight;

/// <summary>
/// Decides when a launch vehicle should leave its ballistic coast and begin the
/// orbital-insertion burn.  The lead time gives attitude guidance and engine startup
/// enough margin to establish radial support before apoapsis.
/// </summary>
public static class AscentInsertionPolicy
{
    public const double GuidanceLeadTimeSeconds = 20.0;

    public static bool ShouldBeginInsertion(
        OrbitalElements trajectory,
        double currentTime,
        double gravitationalParameter,
        double verticalSpeedMps,
        double leadTimeSeconds = GuidanceLeadTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(trajectory);

        if (verticalSpeedMps <= 0.0)
            return true;

        double timeToApoapsis = TimeToApoapsisSeconds(
            trajectory,
            currentTime,
            gravitationalParameter);
        return !double.IsFinite(timeToApoapsis)
            || timeToApoapsis <= System.Math.Max(0.0, leadTimeSeconds);
    }

    public static double TimeToApoapsisSeconds(
        OrbitalElements trajectory,
        double currentTime,
        double gravitationalParameter)
    {
        ArgumentNullException.ThrowIfNull(trajectory);

        if (trajectory.IsRadial
            || trajectory.IsHyperbolic
            || trajectory.SemiMajorAxis <= 0.0
            || gravitationalParameter <= 0.0)
            return double.NaN;

        double a = trajectory.SemiMajorAxis;
        double meanMotion = System.Math.Sqrt(
            gravitationalParameter / (a * a * a));
        if (!double.IsFinite(meanMotion) || meanMotion <= 0.0)
            return double.NaN;

        double meanAnomaly = trajectory.GetMeanAnomaly(
            currentTime,
            gravitationalParameter);
        double delta = System.Math.PI - meanAnomaly;
        if (delta < 0.0)
            delta += 2.0 * System.Math.PI;
        return delta / meanMotion;
    }
}
