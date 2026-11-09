namespace Exosphere.Simulation.Flight;

/// <summary>
/// Apollo 11 schedule: the AS-506 ascent and TLI event times from the Saturn V flight
/// evaluation (AS-506) and the Apollo 11 mission report, including its own lunar orbit
/// insertion and circularization targets. After CSM sep the profile extracts LM-5 Eagle, docks it to CSM-107 on
/// RCS, flies the docked stack into lunar orbit and lands Eagle.
/// </summary>
public static class Apollo11FlightProfile
{
    public const string Id = "apollo11-lunar-landing-return";

    public const string EagleVesselId = "apollo11-lm5-eagle";
    public const string DockingConnectionId = "apollo11-columbia-eagle-docking";

    // ── Saturn V ascent / TLI (AS-506 as flown) ────────────────────────────
    /// <summary>
    /// S-IC pitch-over elevation; see <see cref="SaturnVAscentSchedule.PitchOverDegrees"/>.
    /// 80° gives the flown S-IC cutoff (67 km, ~2.8 km/s); AS-503's 76° left AS-506's
    /// stronger S-IC at 47 km and cost the S-II ~1 km/s of gravity loss.
    /// </summary>
    public const double PitchOverElevationDegrees = 80.0;
    public const double SicCenterEngineCutoffSeconds = 135.20;
    public const double SicOutboardCutoffSeconds = 161.63;
    public const double SicSeparationSeconds = 162.30;
    /// <summary>S-IC propellant in the tanks at OECO, 18,041 kg LOX + 13,954 kg RP-1 (MPR-SAT-FE-69-9 Table 5-2).</summary>
    public const double SicCutoffResidualKg = 31_995.0;
    public const double EscapeTowerJettisonSeconds = 197.88;
    /// <summary>S-II centre engine cut early (from AS-505 on) to avoid pogo.</summary>
    public const double SiiCenterEngineCutoffSeconds = 460.62;
    public const double SiiCutoffSeconds = 548.22;
    public const double SiiSeparationSeconds = 549.20;
    public const double SivbFirstCutoffSeconds = 699.33;
    public const double ParkingOrbitInsertionSeconds = 709.33;
    public const double TliIgnitionSeconds = 9_856.20;
    public const double TliCutoffSeconds = 10_203.03;
    public const double CsmSivbSeparationSeconds = 11_824.6;
    public const double ParkingOrbitAltitudeM =
        Apollo8FlightProfile.ParkingOrbitAltitudeM;

    // ── TD&E: flown on SM RCS after CSM/S-IVB sep; docking at 3:24:03 GET is the
    // historical reference the mission objectives are scored against.
    public const double DockingSeconds = 12_243.1;

    // ── Docked LOI / circularization, Apollo 11 as flown ────────────────────
    // NASA SP-238 (Apollo 11 Mission Report, 1971): table 7-II (event times), table 8-V
    // (velocity to be gained) and section 7 "Lunar orbit insertion and circularization"
    // (printed pp. 71-72). The report's "miles" are nautical miles (1852 m); 1 ft = 0.3048 m.
    private const double NauticalMileM = 1_852.0;
    private const double FootM = 0.3048;
    /// <summary>LOI velocity to be gained, 2917.4 ft/s (table 8-V).</summary>
    public const double LoiDeltaVMps = 2_917.4 * FootM;
    /// <summary>LOI ignition 075:49:50.37 GET (SP-238 table 7-II; NASA SP-4029 Apollo 11 summary).</summary>
    public const double LoiIgnitionSeconds = 75 * 3_600.0 + 49 * 60.0 + 50.37;
    /// <summary>Flight azimuth 72.058° at the opening of the launch window (NASA SP-4029).</summary>
    public const double FlightAzimuthDegrees = 72.058;
    /// <summary>Circularization velocity to be gained, 159.3 ft/s (table 8-V).</summary>
    public const double CircularizationDeltaVMps = 159.3 * FootM;
    /// <summary>LOI orbit achieved: 169.7 by 60.0 n mi.</summary>
    public const double InitialLunarPeriluneAltitudeM = 60.0 * NauticalMileM;
    public const double InitialLunarApoluneAltitudeM = 169.7 * NauticalMileM;
    /// <summary>
    /// Circularization target: a deliberately non-circular 65.7 by 53.7 n mi orbit (achieved
    /// 65.7 by 53.8), chosen so lunar gravity would round it out by the rendezvous.
    /// </summary>
    public const double LunarOrbitPericynthionAltitudeM = 53.7 * NauticalMileM;
    public const double LunarOrbitApocynthionAltitudeM = 65.7 * NauticalMileM;
    /// <summary>LOI ignition 75:49:50.4 to circularization ignition 80:11:36.8 GET.</summary>
    public const double CircularizationDelaySeconds = (80 * 3600 + 11 * 60 + 36.8) - (75 * 3600 + 49 * 60 + 50.4);

    // ── Lunar landing (compressed: undocking follows circularization) ───────
    /// <summary>Eagle checkout in lunar orbit before undocking.</summary>
    public const double UndockDelaySeconds = 1_800.0;
    /// <summary>Docking-spring separation speed between Columbia and Eagle (m/s).</summary>
    public const double UndockingSeparationSpeedMps = 0.3;
    /// <summary>Stand-off coast after undocking before the DOI burn.</summary>
    public const double DoiStandoffSeconds = 60.0;
    /// <summary>The crew who fly Eagle to the surface; Collins stays in Columbia.</summary>
    public static readonly IReadOnlyList<string> LunarModuleCrewIds =
        ["neil-a-armstrong", "edwin-e-buzz-aldrin-jr"];

    /// <summary>Published LM-5 wet mass (kg) — also the SLA envelope carve-out.</summary>
    public const double EagleWetMassKg = 15_061.53464585;

    /// <summary>Opaque SLA+LM launch-envelope dry mass before extract (kg).</summary>
    public const double SlaWithLmDryMassKg = 16_879.98645718;

    /// <summary>SLA shell remaining after Eagle is extracted (kg).</summary>
    public const double EmptySlaDryMassKg =
        SlaWithLmDryMassKg - EagleWetMassKg;

    /// <summary>CM half-length + LM ascent half-length (m) — port centre offset.</summary>
    public const double DockingCentreToPortsM = 1.695 + 1.88;

    public static double ElevationDegrees(double elapsedSeconds) =>
        Apollo8FlightProfile.ElevationDegrees(elapsedSeconds);
}
