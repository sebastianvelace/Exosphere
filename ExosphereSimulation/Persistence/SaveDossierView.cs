namespace Exosphere.Simulation.Persistence;

/// <summary>
/// Read-only, presentation-safe summary of a persisted flight.
///
/// This is deliberately an adapter over <see cref="SaveGameV2"/> rather than a
/// new persistence schema.  The main menu needs a small amount of context for
/// Continue, while the simulation save remains the source of truth.  IDs are
/// flattened and bounded here so an old or hand-edited save cannot inject line
/// breaks or an unbounded label into the menu dossier.
/// </summary>
public sealed record SaveDossierView(
    string SlotName,
    DateTimeOffset SavedAtUtc,
    string MissionId,
    string MissionPhase,
    string ActiveVesselName,
    string ActiveVesselId,
    double SimulationTime,
    double TimeScale,
    int VesselCount,
    int CompletedObjectiveCount,
    int CompletedCampaignMissionCount,
    int ReachedPhaseCount)
{
    private const int MaxDisplayLength = 64;

    /// <summary>Builds the menu-facing summary from already validated save data.</summary>
    public static SaveDossierView FromSave(string slotName, SaveGameV2 save)
    {
        ArgumentNullException.ThrowIfNull(save);

        VesselSaveV2? activeVessel = save.ActiveVesselId == null
            ? null
            : save.Vessels.FirstOrDefault(
                vessel => string.Equals(
                    vessel.Id,
                    save.ActiveVesselId,
                    StringComparison.Ordinal));

        return new SaveDossierView(
            Normalize(slotName, "UNKNOWN"),
            save.SavedAtUtc,
            Normalize(save.Mission.MissionId, "SANDBOX FLIGHT"),
            Normalize(save.Mission.Phase, "SAVED"),
            Normalize(activeVessel?.Name, "NO ACTIVE VESSEL"),
            Normalize(save.ActiveVesselId, "NONE"),
            save.SimulationTime,
            save.TimeScale,
            save.Vessels.Count,
            save.Mission.CompletedObjectives.Count,
            save.Campaign.CompletedMissionIds.Count,
            save.Mission.ReachedPhases.Count);
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        string singleLine = value.Trim()
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return singleLine.Length <= MaxDisplayLength
            ? singleLine
            : singleLine[..MaxDisplayLength];
    }
}
