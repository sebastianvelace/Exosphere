namespace Exosphere.Simulation.Flight;

/// <summary>
/// Severity bands for the deceleration load felt by crew and structure. The boundaries are
/// the ones a human actually notices during an entry, not arbitrary UI steps:
/// a shallow lifting entry sits near 1–2 g, an orbital ballistic entry peaks at 4–5 g
/// (Mercury/Gemini/Soyuz nominal), and a Soyuz-style ballistic abort reaches 8–9 g.
/// </summary>
public enum GLoadBand
{
    /// <summary>Below <see cref="EntryLoadTracker.ElevatedG"/> — unremarkable.</summary>
    Nominal,

    /// <summary>The load is now obvious to the crew, but well inside tolerance.</summary>
    Elevated,

    /// <summary>The 4–5 g wall of a real orbital entry: hard to move, hard to breathe.</summary>
    High,

    /// <summary>Ballistic-abort territory; sustained exposure risks grey-out and injury.</summary>
    Severe,
}

/// <summary>
/// Tracks the instantaneous and peak non-gravitational (proper) acceleration of an entry,
/// expressed in g. Feed it <c>Vessel.GetProperAcceleration(body).Magnitude / 9.80665</c>.
///
/// This is pure instrumentation: it holds no reference to the vessel, applies no force and
/// is deliberately Godot-free so the peak-g contract is unit-testable and can be persisted
/// alongside the rest of the flight state.
/// </summary>
public sealed class EntryLoadTracker
{
    /// <summary>Standard gravity used to convert m/s² to g (m/s²).</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>Lower edge of <see cref="GLoadBand.Elevated"/> (g).</summary>
    public const double ElevatedG = 2.0;

    /// <summary>Lower edge of <see cref="GLoadBand.High"/> (g) — the real reentry wall.</summary>
    public const double HighG = 4.0;

    /// <summary>Lower edge of <see cref="GLoadBand.Severe"/> (g).</summary>
    public const double SevereG = 8.0;

    /// <summary>Most recent load (g).</summary>
    public double CurrentG { get; private set; }

    /// <summary>Highest load seen since the last <see cref="Reset"/> (g).</summary>
    public double PeakG { get; private set; }

    /// <summary>Seconds elapsed since <see cref="PeakG"/> was last raised.</summary>
    public double SecondsSincePeak { get; private set; }

    /// <summary>True once any load has been recorded.</summary>
    public bool HasSample { get; private set; }

    /// <summary>Band of the instantaneous load.</summary>
    public GLoadBand Band => Classify(CurrentG);

    /// <summary>Band of the peak load — this is the one worth holding on screen.</summary>
    public GLoadBand PeakBand => Classify(PeakG);

    /// <summary>Records one sample. Non-finite or negative values are ignored.</summary>
    public void Update(double dt, double gNow)
    {
        if (!double.IsFinite(dt) || dt < 0.0) dt = 0.0;
        if (!double.IsFinite(gNow) || gNow < 0.0) return;

        CurrentG = gNow;
        HasSample = true;

        if (gNow > PeakG)
        {
            PeakG = gNow;
            SecondsSincePeak = 0.0;
        }
        else
        {
            SecondsSincePeak += dt;
        }
    }

    /// <summary>Clears the tracked peak — call when a new entry is armed.</summary>
    public void Reset()
    {
        CurrentG = 0.0;
        PeakG = 0.0;
        SecondsSincePeak = 0.0;
        HasSample = false;
    }

    /// <summary>Restores a previously recorded peak (save/load, mission debrief replay).</summary>
    public void RestorePeak(double peakG)
    {
        if (!double.IsFinite(peakG) || peakG <= 0.0) return;
        if (peakG > PeakG) PeakG = peakG;
        HasSample = true;
    }

    /// <summary>Maps a load in g to its severity band.</summary>
    public static GLoadBand Classify(double g)
    {
        if (!double.IsFinite(g)) return GLoadBand.Nominal;
        if (g >= SevereG) return GLoadBand.Severe;
        if (g >= HighG) return GLoadBand.High;
        if (g >= ElevatedG) return GLoadBand.Elevated;
        return GLoadBand.Nominal;
    }
}
