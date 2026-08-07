namespace Exosphere.Simulation.Flight;

/// <summary>
/// Apollo 11 playable schedule through transposition and docking (TD&amp;E).
/// Ascent/TLI event times reuse the AS-503 Saturn V proxy already validated by
/// Apollo 8 until a fully dated AS-506 reconstruction lands. Post-CSM-sep the
/// profile diverges: extract LM-5 Eagle from the SLA envelope and hard-dock to
/// CSM-107. Lunar landing remains a later slice.
/// </summary>
public static class Apollo11FlightProfile
{
    public const string Id = "apollo11-lunar-landing-return";

    public const string EagleVesselId = "apollo11-lm5-eagle";
    public const string DockingConnectionId = "apollo11-columbia-eagle-docking";

    // ── Saturn V ascent / TLI (AS-503 proxy) ───────────────────────────────
    public const double SicCenterEngineCutoffSeconds =
        Apollo8FlightProfile.SicCenterEngineCutoffSeconds;
    public const double SicOutboardCutoffSeconds =
        Apollo8FlightProfile.SicOutboardCutoffSeconds;
    public const double SicSeparationSeconds =
        Apollo8FlightProfile.SicSeparationSeconds;
    public const double EscapeTowerJettisonSeconds =
        Apollo8FlightProfile.EscapeTowerJettisonSeconds;
    public const double SiiCutoffSeconds = Apollo8FlightProfile.SiiCutoffSeconds;
    public const double SiiSeparationSeconds =
        Apollo8FlightProfile.SiiSeparationSeconds;
    public const double SivbFirstCutoffSeconds =
        Apollo8FlightProfile.SivbFirstCutoffSeconds;
    public const double ParkingOrbitInsertionSeconds =
        Apollo8FlightProfile.ParkingOrbitInsertionSeconds;
    public const double TliIgnitionSeconds =
        Apollo8FlightProfile.TliIgnitionSeconds;
    public const double TliCutoffSeconds = Apollo8FlightProfile.TliCutoffSeconds;
    public const double CsmSivbSeparationSeconds =
        Apollo8FlightProfile.CsmSivbSeparationSeconds;
    public const double ParkingOrbitAltitudeM =
        Apollo8FlightProfile.ParkingOrbitAltitudeM;

    // ── TD&E (compressed playable corridor after CSM/S-IVB sep) ────────────
    public const double EagleExtractSeconds = CsmSivbSeparationSeconds + 120.0;
    public const double DockingSeconds = CsmSivbSeparationSeconds + 600.0;

    /// <summary>Published LM-5 wet mass (kg) — also the SLA envelope carve-out.</summary>
    public const double EagleWetMassKg = 15_061.53464585;

    /// <summary>Opaque SLA+LM launch-envelope dry mass before extract (kg).</summary>
    public const double SlaWithLmDryMassKg = 21_227.45964503;

    /// <summary>SLA shell remaining after Eagle is extracted (kg).</summary>
    public const double EmptySlaDryMassKg =
        SlaWithLmDryMassKg - EagleWetMassKg;

    /// <summary>CM half-length + LM ascent half-length (m) — port centre offset.</summary>
    public const double DockingCentreToPortsM = 1.695 + 1.88;

    public static double ElevationDegrees(double elapsedSeconds) =>
        Apollo8FlightProfile.ElevationDegrees(elapsedSeconds);
}
