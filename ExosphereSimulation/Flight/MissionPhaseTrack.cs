namespace Exosphere.Simulation.Flight;

/// <summary>
/// Pure mission-phase progress track used by the flight HUD (C3).
/// Phase names match <c>Exosphere.Game.MissionPhase</c> enum member names so the
/// Godot layer can call <c>phase.ToString()</c> without importing Godot here.
/// </summary>
public static class MissionPhaseTrack
{
    /// <summary>
    /// Player-facing progression order. <c>RETRO_BURN</c> sits after <c>COAST</c>
    /// for the deorbit burn. Landing-burn <c>RETRO_BURN</c> (after ENTRY) is remapped
    /// via <see cref="IndexOf"/>.
    /// </summary>
    public static readonly string[] Sequence =
    {
        "COUNTDOWN", "LIFTOFF", "ASCENT_SH",
        "MAX_Q", "MECO", "SEPARATION",
        "ASCENT_SHIP", "ORBIT", "COAST",
        "RETRO_BURN", "ENTRY", "PEAK_HEATING", "AERO_DESCENT",
        "FINAL_DESCENT", "LANDED", "CRASHED",
    };

    /// <summary>
    /// Dot-track index for <paramref name="phaseName"/>, or -1 when the phase is
    /// pre-track (e.g. PRE_LAUNCH). When <paramref name="afterEntryInterface"/> is
    /// true and the phase is RETRO_BURN, maps to FINAL_DESCENT (landing burn slot).
    /// </summary>
    public static int IndexOf(string phaseName, bool afterEntryInterface = false)
    {
        if (string.IsNullOrEmpty(phaseName))
            return -1;

        if (phaseName == "PRE_LAUNCH")
            return -1;

        if (phaseName == "IGNITION")
            return IndexOf("COUNTDOWN");

        if (phaseName == "RETRO_BURN" && afterEntryInterface)
            return System.Array.IndexOf(Sequence, "FINAL_DESCENT");

        return System.Array.IndexOf(Sequence, phaseName);
    }

    /// <summary>Name of the next phase after <paramref name="phaseName"/> on the track, or null at end/unknown.</summary>
    public static string? NextPhase(string phaseName, bool afterEntryInterface = false)
    {
        int idx = IndexOf(phaseName, afterEntryInterface);
        if (idx < 0 || idx + 1 >= Sequence.Length)
            return null;
        return Sequence[idx + 1];
    }

    /// <summary>
    /// True when periapsis altitude (m above surface) sits inside the atmosphere column
    /// (including surface-intersecting impact trajectories).
    /// </summary>
    public static bool PeriapsisInAtmosphere(double periapsisAltitudeM, double atmosphereMaxAltitudeM)
    {
        if (double.IsNaN(periapsisAltitudeM) || double.IsNaN(atmosphereMaxAltitudeM))
            return false;
        if (atmosphereMaxAltitudeM <= 0.0)
            return false;
        return periapsisAltitudeM < atmosphereMaxAltitudeM;
    }

    /// <summary>
    /// Approximate seconds until next periapsis for a bound elliptic conic.
    /// Returns NaN when timing is unavailable (radial, hyperbolic, degenerate).
    /// </summary>
    public static double ApproximateTimeToPeriapsisSec(
        double semiMajorAxisM,
        double eccentricity,
        double meanAnomalyRad,
        double gm)
    {
        if (gm <= 0.0 || semiMajorAxisM <= 0.0 || eccentricity >= 1.0)
            return double.NaN;
        if (double.IsNaN(meanAnomalyRad) || double.IsInfinity(meanAnomalyRad))
            return double.NaN;

        double a3 = semiMajorAxisM * semiMajorAxisM * semiMajorAxisM;
        double n = System.Math.Sqrt(gm / a3);
        if (n <= 0.0 || double.IsNaN(n))
            return double.NaN;

        double M = meanAnomalyRad % (2.0 * System.Math.PI);
        if (M < 0.0) M += 2.0 * System.Math.PI;

        // Periapsis at M = 0; remaining mean-anomaly arc.
        double dM = (2.0 * System.Math.PI - M) % (2.0 * System.Math.PI);
        if (dM < 1e-9)
            return 0.0;
        return dM / n;
    }

    /// <summary>
    /// Actionable deorbit/EDL cue line for the phase banner secondary caption.
    /// Returns null when no cue should be shown (keep pad/ascent copy alone).
    /// Does not touch THERMAL / EDL overlay content.
    /// </summary>
    public static string? FormatActionableCue(
        string phaseName,
        bool periapsisInAtmosphere,
        double timeToPeriapsisSec,
        bool afterEntryInterface = false)
    {
        if (string.IsNullOrEmpty(phaseName))
            return null;

        // Landing RETRO after ENTRY: EDLController owns that banner — no deorbit cue.
        if (phaseName == "RETRO_BURN" && afterEntryInterface)
            return null;

        if (phaseName == "RETRO_BURN")
            return "DEORBIT BURN";

        if (phaseName == "ENTRY")
            return "ENTRY INTERFACE";

        bool coastOrOrbitHint = phaseName is "COAST"
            || (phaseName == "ORBIT" && periapsisInAtmosphere);

        if (!coastOrOrbitHint)
            return null;

        if (periapsisInAtmosphere
            && !double.IsNaN(timeToPeriapsisSec)
            && !double.IsInfinity(timeToPeriapsisSec)
            && timeToPeriapsisSec >= 0.0)
        {
            return FormatEntryInterfaceEta(timeToPeriapsisSec);
        }

        if (periapsisInAtmosphere || phaseName == "COAST")
            return "ENTRY INTERFACE";

        return null;
    }

    /// <summary>Formats "ENTRY INTERFACE in ~Xm" / "~Xs".</summary>
    public static string FormatEntryInterfaceEta(double seconds)
    {
        if (seconds < 60.0)
        {
            int secs = System.Math.Max(0, (int)System.Math.Ceiling(seconds));
            return $"ENTRY INTERFACE in ~{secs}s";
        }

        int mins = System.Math.Max(1, (int)System.Math.Ceiling(seconds / 60.0));
        return $"ENTRY INTERFACE in ~{mins}m";
    }
}
