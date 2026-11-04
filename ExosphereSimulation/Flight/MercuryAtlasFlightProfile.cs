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
    /// <summary>
    /// Aggregate high-altitude thrust of the lumped LR-105+verniers model. Not the
    /// published YLR-105 vacuum rating (~365 kN). Recalibrated on the WGS84 sea-level
    /// Cape so the BECO/insertion tape closes periapsis; 610 kN was the
    /// spherical mean-radius value.
    /// </summary>
    public const double EffectiveSustainerVacuumThrustN = 625_000.0;

    /// <summary>Tape elevation at BECO (calibrated reconstruction, not the Atlas analog tape).</summary>
    public const double BoosterCutoffElevationDegrees = 23.2;

    /// <summary>Final sustainer-phase tape elevation (calibrated reconstruction).</summary>
    public const double InsertionElevationDegrees = -17.9;

    /// <summary>Commanded elevation above the local geodetic horizon during powered flight.</summary>
    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        // Calibrated reconstruction on WGS84 sea-level Cape, not the original Atlas
        // analog tape. Published MA-6 anchors: BECO 82.6 km / 2.00 km/s, SECO
        // 185.8 km / 7.84 km/s, 161 × 261 km.
        // Stay vertical through the dense column. In-flight |r| − R_mean used to
        // overstate geodetic height by ~2.3 km at Cape latitude, so a 17° BECO
        // pitch flew a thinner ISA column than WGS84. After BECO pitch onto the
        // horizon and a few degrees below while the sustainer still has propellant
        // — holding loft spent that Δv on radial climb and dry-holed the Atlas
        // ~60 s early, so the Kepler coast skipped through the Earth.
        // Re-closed on distributed (per-part) aerodynamics: the aggregate model's
        // broadside-blended drag at the tape's 4–6° angle of attack cost ~40 m/s more by
        // BECO. 23.2° BECO / −17.9° gives 159.5 × 267.8 km (aggregate fit: 157 × 276).
        if (elapsedSeconds <= 18.0) return 90.0;
        if (elapsedSeconds <= BoosterEngineCutoffSeconds)
            return SmoothStep(
                90.0, BoosterCutoffElevationDegrees,
                18.0, BoosterEngineCutoffSeconds,
                elapsedSeconds);
        if (elapsedSeconds <= 155.0)
            return SmoothStep(
                BoosterCutoffElevationDegrees, 2.0,
                BoosterEngineCutoffSeconds, 155.0,
                elapsedSeconds);
        if (elapsedSeconds <= 215.0)
            return SmoothStep(
                2.0, InsertionElevationDegrees,
                155.0, 215.0,
                elapsedSeconds);
        return InsertionElevationDegrees;
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
