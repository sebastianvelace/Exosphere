namespace Exosphere.Simulation.Flight;

/// <summary>
/// Traceable event schedule and pitch program for the Freedom 7 reconstruction. The schedule
/// is separate from the Godot adapter so it can be tested and replayed headless.
/// </summary>
public static class MercuryRedstoneFlightProfile
{
    public const string Id = "mercury-redstone3-suborbital";
    public const double MainEngineCutoffSeconds = 142.0;
    public const double SpacecraftSeparationSeconds = 152.5;
    public const double EscapeTowerJettisonSeconds = 154.0;
    public const double RetroSequenceSeconds = 284.0;
    public const double RetroBurnDurationSeconds = 10.0;
    public const double DrogueDeployAltitudeM = 6_705.6;
    public const double MainDeployAltitudeM = 3_048.0;
    public const double HistoricalSplashdownSeconds = 922.0;

    /// <summary>Commanded elevation above the local horizon during the A-7 burn.</summary>
    public static double ElevationDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        double fraction = System.Math.Clamp(
            (elapsedSeconds - 12.0) / (MainEngineCutoffSeconds - 12.0),
            0.0,
            1.0);
        double smooth = fraction * fraction * (3.0 - 2.0 * fraction);
        return 90.0 - 40.0 * smooth;
    }
}
