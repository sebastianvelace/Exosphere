namespace Exosphere.Simulation.Flight;

/// <summary>
/// Published MA-6 event sequence plus the simulator's smooth Atlas guidance program.
/// Event times come from the NASA MA-6 flight report; guidance between those points is
/// explicitly a calibrated reconstruction, not a claim about the original guidance tape.
/// </summary>
public static class MercuryAtlasFlightProfile
{
    public const string Id = "mercury-atlas6-three-orbit";
    public const double BoosterEngineCutoffSeconds = 129.6;
    public const double TowerJettisonSeconds = 153.3;
    public const double SustainerEngineCutoffSeconds = 301.4;
    public const double TailoffCompleteSeconds = 302.0;
    public const double SpacecraftSeparationSeconds = 303.6;
    public const double RetroSequenceSeconds = 16_388.0;
    public const double RetroMotorIntervalSeconds = 5.0;
    public const double RetroBurnDurationSeconds = 20.0;
    public const double EntryInterfaceAltitudeM = 100_000.0;
    public const double DrogueDeployAltitudeM = 8_534.4;
    public const double MainDeployAltitudeM = 3_291.84;
    public const double HistoricalSplashdownSeconds = 17_723.0;
    public const double HistoricalOrbitalPeriodSeconds = 5_309.0;
    public const double HistoricalApogeeM = 261_140.0;
    public const double HistoricalPerigeeM = 161_000.0;
    public const double MaximumAscentG = 7.5;
    public const double EffectiveSustainerVacuumThrustN = 610_000.0;

    /// <summary>Commanded elevation above the local horizon during powered flight.</summary>
    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        if (elapsedSeconds <= BoosterEngineCutoffSeconds)
            return SmoothStep(
                90.0, 17.0,
                12.0, BoosterEngineCutoffSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= 170.0)
            return SmoothStep(
                17.0, 6.0,
                BoosterEngineCutoffSeconds, 170.0,
                elapsedSeconds);
        if (elapsedSeconds <= 190.0)
            return SmoothStep(
                6.0, -20.0,
                170.0, 190.0,
                elapsedSeconds);
        return -20.0;
    }

    /// <summary>
    /// Calibrated late-burn acceleration limiter. Atlas guidance was fixed-thrust; this
    /// aggregate model uses a throttle equivalent because it does not model the separate
    /// verniers and turbopump exhaust. The cap preserves the published MA-6 crew-load band.
    /// </summary>
    public static double SustainerThrottle(double totalMassKg)
    {
        if (!double.IsFinite(totalMassKg) || totalMassKg <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(totalMassKg));
        double command = MaximumAscentG * 9.80665 * totalMassKg
            / EffectiveSustainerVacuumThrustN;
        return System.Math.Clamp(command, 0.5, 1.0);
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
