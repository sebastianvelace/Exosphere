namespace Exosphere.Simulation.Flight;

/// <summary>
/// Published Gemini VIII events and a smooth Titan II guidance reconstruction.
/// The event schedule is historical; commands between events are calibrated.
/// </summary>
public static class Gemini8FlightProfile
{
    public const string Id = "gemini8-rendezvous-emergency-return";
    public const double StageOneCutoffSeconds = 154.61;
    public const double StageSeparationSeconds = 155.29;
    public const double StageTwoCutoffSeconds = 337.54;
    public const double SpacecraftSeparationSeconds = 365.66;
    public const double RendezvousCompleteSeconds = 21_480.0;
    public const double DockingSeconds = 23_596.0;
    public const double RigidizationSeconds = 23_602.0;
    public const double ThrusterAnomalySeconds = 25_226.7;
    public const double UndockingSeconds = 26_112.3;
    public const double ReentryControlActivationSeconds = 26_185.1;
    public const double ThrusterIsolationSeconds = 26_295.7;
    public const double ControlRecoveredSeconds = 26_730.0;
    public const double AdapterSeparationSeconds = 36_228.0;
    public const double RetrofireSeconds = 36_287.0;
    public const double DrogueDeploySeconds = 38_207.0;
    public const double MainDeploySeconds = 38_288.0;
    public const double SplashdownSeconds = 38_486.0;
    public const double HistoricalInsertionPerigeeM = 159_900.0;
    public const double HistoricalInsertionApogeeM = 271_900.0;
    public const double HistoricalTargetOrbitM = 298_730.0;
    public const double HistoricalPeakAngularRateDegPerS = 296.0;
    public const double DockedTumbleRateDegPerS = 20.0;
    public const double CalibratedEmergencyRetroDeltaVMps = 70.0;

    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        if (elapsedSeconds <= StageOneCutoffSeconds)
            return SmoothStep(
                90.0, 14.0,
                12.0, StageOneCutoffSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= 220.0)
            return SmoothStep(
                14.0, 2.0,
                StageOneCutoffSeconds, 220.0,
                elapsedSeconds);
        return SmoothStep(
            2.0, -10.0,
            220.0, StageTwoCutoffSeconds,
            elapsedSeconds);
    }

    /// <summary>
    /// Calibrated reconstruction of the OAMS-8 failure telemetry. This is an
    /// externally forced rate, not a pilot-command envelope: the docked stack begins
    /// tumbling, Spacecraft 8 accelerates after undocking, then the reentry-control
    /// system arrests the motion after the failed circuit is isolated.
    /// </summary>
    public static double AnomalyAngularRateDegPerS(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= ThrusterAnomalySeconds) return 0.0;
        if (elapsedSeconds <= UndockingSeconds)
            return Linear(
                0.0,
                DockedTumbleRateDegPerS,
                ThrusterAnomalySeconds,
                UndockingSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= ThrusterIsolationSeconds)
            return Linear(
                DockedTumbleRateDegPerS,
                HistoricalPeakAngularRateDegPerS,
                UndockingSeconds,
                ThrusterIsolationSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= ControlRecoveredSeconds)
            return Linear(
                HistoricalPeakAngularRateDegPerS,
                0.0,
                ThrusterIsolationSeconds,
                ControlRecoveredSeconds,
                elapsedSeconds);
        return 0.0;
    }

    private static double Linear(
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        double time)
    {
        double fraction = System.Math.Clamp(
            (time - startTime) / (endTime - startTime), 0.0, 1.0);
        return startValue + (endValue - startValue) * fraction;
    }

    private static double SmoothStep(
        double startValue,
        double endValue,
        double startTime,
        double endTime,
        double time)
    {
        double fraction = System.Math.Clamp(
            (time - startTime) / (endTime - startTime), 0.0, 1.0);
        double smooth = fraction * fraction * (3.0 - 2.0 * fraction);
        return startValue + (endValue - startValue) * smooth;
    }
}
