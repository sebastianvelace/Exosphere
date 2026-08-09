namespace Exosphere.Simulation.Persistence;

/// <summary>
/// The small, deterministic part of save-slot selection shared by the game layer and
/// tests. File enumeration order is intentionally not part of the contract: a slot with
/// the newest serialized timestamp wins, and equal timestamps use a stable name tie-break.
/// </summary>
public sealed record SaveSlotMetadata(string SlotName, DateTimeOffset SavedAtUtc);

public static class SaveSlotOrdering
{
    /// <summary>
    /// Selects the most recently saved valid slot. Empty names are ignored so a malformed
    /// directory entry cannot become a launch intent. Equal timestamps are ordered by the
    /// ordinal slot name to keep Continue reproducible across filesystems.
    /// </summary>
    public static string? SelectMostRecent(
        IEnumerable<SaveSlotMetadata> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SlotName))
            .OrderByDescending(candidate => candidate.SavedAtUtc)
            .ThenBy(candidate => candidate.SlotName, StringComparer.Ordinal)
            .Select(candidate => candidate.SlotName)
            .FirstOrDefault();
    }
}
