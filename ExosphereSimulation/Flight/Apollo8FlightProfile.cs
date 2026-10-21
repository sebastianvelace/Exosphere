namespace Exosphere.Simulation.Flight;

/// <summary>
/// Published Apollo 8 event schedule and bounded guidance reconstruction.
/// Times are seconds from liftoff; distances, velocities and impulses are SI.
/// </summary>
public static class Apollo8FlightProfile
{
    public const string Id = "apollo8-lunar-orbit-return";

    public const double SicCenterEngineCutoffSeconds = 125.9;
    public const double SicOutboardCutoffSeconds = 153.8;
    public const double SicSeparationSeconds = 154.5;
    public const double SiiIgnitionSeconds = 155.2;
    public const double EscapeTowerJettisonSeconds = 188.6;
    public const double SiiCutoffSeconds = 524.0;
    public const double SiiSeparationSeconds = 524.9;
    public const double SivbFirstIgnitionSeconds = 525.0;
    public const double SivbFirstCutoffSeconds = 685.0;
    public const double ParkingOrbitInsertionSeconds = 695.0;
    public const double TliIgnitionSeconds = 10_237.1;
    public const double TliCutoffSeconds = 10_555.5;
    public const double CsmSivbSeparationSeconds = 12_059.3;

    public const double LoiIgnitionSeconds = 248_900.4;
    public const double LoiCutoffSeconds = 249_147.3;
    public const double CircularizationIgnitionSeconds = 264_907.0;
    public const double CircularizationCutoffSeconds = 264_916.2;
    public const double TeiIgnitionSeconds = 321_556.6;
    public const double TeiCutoffSeconds = 321_760.3;
    public const double CsmSeparationSeconds = 527_328.0;
    public const double EntryInterfaceSeconds = 528_372.8;
    public const double DrogueDeploySeconds = 528_887.8;
    public const double MainDeploySeconds = 528_938.9;
    public const double SplashdownSeconds = 529_242.0;

    public const double ParkingOrbitAltitudeM = 191_310.0;
    public const double InitialLunarPeriluneAltitudeM = 111_120.0;
    public const double InitialLunarApoluneAltitudeM = 312_062.0;
    public const double CircularLunarOrbitAltitudeM = 111_120.0;
    public const double LoiDeltaVMps = 913.4856;
    public const double CircularizationDeltaVMps = 41.148;
    public const double TeiDeltaVMps = 1_071.9816;
    public const double EntryInterfaceAltitudeM = 121_920.0;
    public const int HistoricalLunarRevolutions = 10;

    /// <summary>Historical TEI cutoff to entry interface (s).</summary>
    public const double TransearthTimeOfFlightSeconds = EntryInterfaceSeconds - TeiCutoffSeconds;

    /// <summary>Longest transearth coast the TEI planner may trade for a smaller burn (s).</summary>
    public const double MaximumTransearthTimeOfFlightSeconds = 1.5 * TransearthTimeOfFlightSeconds;

    /// <summary>
    /// Vacuum-perigee altitude of a lunar-return conic crossing the 121.92 km entry interface
    /// at ~11 km/s and −6.5° flight-path angle (the centre of the Apollo entry corridor).
    /// </summary>
    public const double EntryCorridorVacuumPerigeeAltitudeM = 38_000.0;

    /// <summary>End of the S-IC pitch-over; afterwards the boost flies a zero-AoA gravity turn.</summary>
    public const double PitchOverEndSeconds = 42.0;

    /// <summary>Elevation reached at <see cref="PitchOverEndSeconds"/> (degrees).</summary>
    public const double PitchOverElevationDegrees = 76.0;

    /// <summary>
    /// S-IC pitch-over: vertical rise to T+12 s, then a linear kick to
    /// <see cref="PitchOverElevationDegrees"/>. Simulator calibration for the gravity turn
    /// that follows (S-IC cutoff ~59 km / 2.56 km/s; flight: 67 km / 2.75 km/s).
    /// </summary>
    public static double PitchOverDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        double fraction = System.Math.Clamp(
            (elapsedSeconds - 12.0) / (PitchOverEndSeconds - 12.0), 0.0, 1.0);
        return 90.0 - (90.0 - PitchOverElevationDegrees) * fraction;
    }

    /// <summary>Apollo range-safety launch azimuth limits from LC-39 (degrees from north).</summary>
    public const double MinimumLaunchAzimuthDegrees = 72.0;
    public const double MaximumLaunchAzimuthDegrees = 108.0;

    /// <summary>
    /// S-II/S-IVB pointing damping for the live pitch/yaw authority (ζ ≈ 0.8 at Kp 2.3). The
    /// S-IC's 1.2 is kept as a floor; the S-IVB (~0.09 rad/s²) needs roughly 8.
    /// </summary>
    public static double UpperStageDampingGain(double pitchYawAuthorityRadPerS2) =>
        System.Math.Max(1.2, AttitudeGuidance.DampingGainFor(2.3, pitchYawAuthorityRadPerS2));

    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        if (elapsedSeconds <= SicOutboardCutoffSeconds)
            return SmoothStep(90.0, 18.0, 12.0,
                SicOutboardCutoffSeconds, elapsedSeconds);
        if (elapsedSeconds <= 360.0)
            return SmoothStep(18.0, 2.5,
                SicOutboardCutoffSeconds, 360.0, elapsedSeconds);
        return SmoothStep(2.5, -4.0, 360.0,
            SiiCutoffSeconds, elapsedSeconds);
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
