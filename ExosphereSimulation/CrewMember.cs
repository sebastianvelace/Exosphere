namespace Exosphere.Simulation;

public enum CrewStatus { Active, OnEVA, Injured, Dead }

/// <summary>
/// Represents a crew member aboard a vessel or performing an EVA.
/// </summary>
public class CrewMember
{
    /// <summary>Unique identifier (auto-generated GUID by default).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string FirstName { get; set; } = "";
    public string LastName  { get; set; } = "";

    /// <summary>Trimmed full name derived from <see cref="FirstName"/> and <see cref="LastName"/>.</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();

    // ── Skill levels [0-5] ────────────────────────────────────────────────
    public int PilotLevel     { get; set; }   // 0-5
    public int EngineerLevel  { get; set; }   // 0-5
    public int ScientistLevel { get; set; }   // 0-5

    // ── Experience and history ────────────────────────────────────────────
    /// <summary>Total accumulated EVA-hours.</summary>
    public double EVAExperience   { get; set; }

    /// <summary>Number of missions completed.</summary>
    public int MissionsCompleted  { get; set; }

    // ── Status ────────────────────────────────────────────────────────────
    public CrewStatus Status { get; set; } = CrewStatus.Active;

    /// <summary>True when the crew member is currently outside the vessel.</summary>
    public bool IsOnEVA => Status == CrewStatus.OnEVA;

    // ── EVA suit state ────────────────────────────────────────────────────
    /// <summary>Suit battery charge (Electric Charge units). Starts at 100.</summary>
    public double EVASuitEC { get; set; } = 100.0;

    /// <summary>Suit oxygen fraction [0, 1]. Starts at 1 (full).</summary>
    public double EVASuitO2 { get; set; } = 1.0;

    // ── Computed ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a risk factor in [0, 1] for this crew member performing an EVA.
    /// 0 = safe; 1 = maximum risk.
    /// Risk decreases with accumulated EVA experience and engineer skill.
    /// </summary>
    public double ComputeEVARisk() =>
        System.Math.Max(0.0, 1.0 - (EVAExperience / 100.0) - (EngineerLevel * 0.1));

    // ── Simulation tick ───────────────────────────────────────────────────

    /// <summary>
    /// Advances the crew member's EVA suit state by <paramref name="dt"/> seconds.
    /// Drains suit EC at 0.1 EC/s while on EVA.
    /// Returns <c>false</c> and sets status to <see cref="CrewStatus.Injured"/>
    /// if the battery is depleted.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <returns><c>true</c> if the EVA can continue; <c>false</c> if power ran out.</returns>
    public bool TickEVA(double dt)
    {
        if (!IsOnEVA) return true;

        EVASuitEC -= 0.1 * dt;
        if (EVASuitEC <= 0.0)
        {
            EVASuitEC = 0.0;
            Status    = CrewStatus.Injured;
            return false;
        }

        return true;
    }
}
