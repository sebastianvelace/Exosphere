namespace Exosphere.Simulation.Flight;

/// <summary>
/// Event times (s from liftoff) that sequence a Saturn V ascent. Each vehicle flies its own:
/// AS-506 burned its S-IC eight seconds longer than AS-503 (heavier payload, more propellant),
/// and flying it on the AS-503 clock threw ~160 t of S-IC propellant away, which the S-IVB
/// then paid for and arrived at TLI dry. Where the flight evaluation gives it, the S-IC burns
/// down to its flown cutoff residual (its OECO was LOX depletion). The S-II separates at its scheduled time or as soon
/// as it burns out (its real cutoff was LOX depletion), whichever comes first, so the stack
/// never coasts and falls between stages.
/// </summary>
public sealed record SaturnVAscentSchedule(
    double SicCenterEngineCutoffSeconds,
    double SicOutboardCutoffSeconds,
    double SicSeparationSeconds,
    double EscapeTowerJettisonSeconds,
    double SiiCenterEngineCutoffSeconds,
    double SiiCutoffSeconds,
    double SiiSeparationSeconds,
    double PitchOverElevationDegrees,
    double SicCutoffResidualKg = 0.0)
{
    /// <summary>
    /// S-IC outboard cutoff. With a flown residual (AS-506: LOX-depletion cutoff at 32.0 t
    /// in the tanks) the F-1s burn down to it; otherwise they stop on the clock. A residual
    /// cutoff is bounded 15 s past the scheduled time in case the residual is never reached.
    /// </summary>
    public bool IsSicCutoff(Vessel vessel, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (SicCutoffResidualKg <= 0.0) return elapsedSeconds >= SicOutboardCutoffSeconds;
        return SicPropellantKg(vessel) <= SicCutoffResidualKg
            || elapsedSeconds >= SicOutboardCutoffSeconds + 15.0;
    }

    /// <summary>Seconds from S-IC outboard cutoff to S-IC/S-II separation.</summary>
    public double SicSeparationDelaySeconds => SicSeparationSeconds - SicOutboardCutoffSeconds;

    public static double SicPropellantKg(Vessel vessel) =>
        vessel.Parts.Parts.Where(part => part.Definition.HasVehicleRole("sic_tank"))
            .Sum(part => part.LiquidFuel + part.Oxidizer);

    /// <summary>
    /// S-IC pitch-over: vertical rise to T+12 s, then a linear kick to
    /// <see cref="PitchOverElevationDegrees"/> at <see cref="Apollo8FlightProfile.PitchOverEndSeconds"/>;
    /// the zero-AoA gravity turn that follows is calibrated per vehicle to the flown S-IC
    /// cutoff altitude (a stronger S-IC turns flatter on the same kick).
    /// </summary>
    public double PitchOverDegrees(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds))
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (elapsedSeconds <= 12.0) return 90.0;
        double fraction = System.Math.Clamp(
            (elapsedSeconds - 12.0) / (Apollo8FlightProfile.PitchOverEndSeconds - 12.0), 0.0, 1.0);
        return 90.0 - (90.0 - PitchOverElevationDegrees) * fraction;
    }

    /// <summary>Engines lit on the S-II cluster at this time (five, four after its CECO).</summary>
    public int SiiEngineCount(double elapsedSeconds) => elapsedSeconds >= SiiCenterEngineCutoffSeconds ? 4 : 5;

    /// <summary>True once the S-II is due to separate: on schedule, or burned out early.</summary>
    public bool IsSiiSeparationDue(Vessel vessel, CelestialBody body, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        return elapsedSeconds >= SiiSeparationSeconds
            || elapsedSeconds > SicSeparationSeconds + 10.0 && vessel.ComputeThrust(body).Magnitude < 1.0;
    }

    /// <summary>Apollo 8 / AS-503.</summary>
    public static SaturnVAscentSchedule As503 { get; } = new(
        Apollo8FlightProfile.SicCenterEngineCutoffSeconds,
        Apollo8FlightProfile.SicOutboardCutoffSeconds,
        Apollo8FlightProfile.SicSeparationSeconds,
        Apollo8FlightProfile.EscapeTowerJettisonSeconds,
        double.PositiveInfinity,
        Apollo8FlightProfile.SiiCutoffSeconds,
        Apollo8FlightProfile.SiiSeparationSeconds,
        Apollo8FlightProfile.PitchOverElevationDegrees);

    /// <summary>Apollo 11 / AS-506.</summary>
    public static SaturnVAscentSchedule As506 { get; } = new(
        Apollo11FlightProfile.SicCenterEngineCutoffSeconds,
        Apollo11FlightProfile.SicOutboardCutoffSeconds,
        Apollo11FlightProfile.SicSeparationSeconds,
        Apollo11FlightProfile.EscapeTowerJettisonSeconds,
        Apollo11FlightProfile.SiiCenterEngineCutoffSeconds,
        Apollo11FlightProfile.SiiCutoffSeconds,
        Apollo11FlightProfile.SiiSeparationSeconds,
        Apollo11FlightProfile.PitchOverElevationDegrees,
        Apollo11FlightProfile.SicCutoffResidualKg);
}
