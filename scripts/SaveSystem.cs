namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Persistence;
using Godot;

/// <summary>
/// Disk adapter for the authoritative, simulation-layer SaveGameV2 codec.
/// Writes are atomic and legacy partial saves are migrated on read.
/// </summary>
public static class SaveSystem
{
    private static string DefaultSaveDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.UserProfile),
            ".local", "share", "Exosphere", "saves");

    public static SaveGameV2? LastLoadedMetadata { get; private set; }

    /// <summary>
    /// Mission phase to apply once the deferred MissionManager child exists.
    /// </summary>
    public static MissionPhase? PendingMissionPhase { get; set; }

    public static void SaveGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        string safeSlot = NormalizeSlotName(slotName);
        System.IO.Directory.CreateDirectory(DefaultSaveDirectory);
        var save = SaveGameV2Codec.Capture(
            bridge.Universe, LastLoadedMetadata);
        if (CampaignRuntime.Instance?.CaptureMissionState() is { } mission)
            save.Mission = mission;
        if (CampaignRuntime.Instance?.Campaign?.State is { } campaign)
            save.Campaign = campaign;
        save.Mission.Phase =
            MissionManager.Instance?.Phase.ToString()
            ?? save.Mission.Phase;
        string path = System.IO.Path.Combine(
            DefaultSaveDirectory, $"{safeSlot}.json");
        string temporary = path + ".tmp";
        System.IO.File.WriteAllText(
            temporary, SaveGameV2Json.Serialize(save));
        System.IO.File.Move(temporary, path, overwrite: true);
        LastLoadedMetadata = save;
        GD.Print($"[SaveSystem] Saved V2 to {path}");
    }

    public static bool LoadGame(string slotName = "quicksave")
    {
        string safeSlot = NormalizeSlotName(slotName);
        string path = System.IO.Path.Combine(
            DefaultSaveDirectory, $"{safeSlot}.json");
        if (!System.IO.File.Exists(path)) return false;

        var bridge = SimulationBridge.Instance;
        if (bridge == null) return false;

        try
        {
            string text = System.IO.File.ReadAllText(path);
            var save = SaveGameV2Json.DeserializeOrMigrate(text);
            string partsPath =
                ProjectSettings.GlobalizePath("res://data/parts");
            var catalog = PartCatalog.LoadFromDirectory(partsPath);
            SaveGameV2Codec.Restore(bridge.Universe, save, catalog);
            LastLoadedMetadata = save;

            int warpIndex = Array.FindIndex(
                SimulationBridge.WarpLevels,
                value => System.Math.Abs(value - save.TimeScale) < 1e-9);
            bridge.SetWarpIndex(warpIndex >= 0 ? warpIndex : 0);
            bridge.RebuildActiveVesselRenderer();

            if (!string.IsNullOrWhiteSpace(save.Mission.Phase)
                && Enum.TryParse(
                    save.Mission.Phase,
                    ignoreCase: true,
                    out MissionPhase phase))
            {
                if (MissionManager.Instance != null)
                    MissionManager.Instance.EnterPhase(phase);
                else
                    PendingMissionPhase = phase;
            }

            GD.Print(
                $"[SaveSystem] Loaded schema {save.SchemaVersion} from {path}");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PushError(
                $"[SaveSystem] Could not load '{path}': {ex.Message}");
            return false;
        }
    }

    public static string[] ListSaveSlots(
        string? saveDirectory = null)
    {
        string directory = saveDirectory ?? DefaultSaveDirectory;
        if (!System.IO.Directory.Exists(directory)) return [];
        return System.IO.Directory.GetFiles(directory, "*.json")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(
                name => name,
                System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Reads valid V2 (or migratable legacy) headers and returns the slot selected by
    /// Continue. Invalid and empty files are ignored; they must not prevent a valid save
    /// from being resumed.
    /// </summary>
    public static string? FindMostRecentSaveSlot(
        string? saveDirectory = null)
    {
        return SaveSlotOrdering.SelectMostRecent(
            ReadSaveSlotMetadata(saveDirectory));
    }

    /// <summary>
    /// Returns the presentation-safe dossier for the same slot Continue selects.
    /// The returned view is derived from SaveGameV2 and does not add fields to the
    /// persisted schema. Invalid slots are ignored using the same rules as Continue.
    /// </summary>
    public static SaveDossierView? ReadMostRecentDossier(
        string? saveDirectory = null)
    {
        string directory = saveDirectory ?? DefaultSaveDirectory;
        if (!System.IO.Directory.Exists(directory)) return null;

        var candidates = new List<(SaveSlotMetadata Metadata, SaveGameV2 Save)>();
        foreach (string path in System.IO.Directory.GetFiles(directory, "*.json"))
        {
            string? slotName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(slotName)) continue;

            try
            {
                SaveGameV2 save = SaveGameV2Json.DeserializeOrMigrate(
                    System.IO.File.ReadAllText(path));
                candidates.Add((
                    new SaveSlotMetadata(slotName, save.SavedAtUtc),
                    save));
            }
            catch
            {
                // Continue already skips malformed slots; the dossier must do the same.
            }
        }

        string? selected = SaveSlotOrdering.SelectMostRecent(
            candidates.Select(candidate => candidate.Metadata));
        if (selected == null) return null;

        var match = candidates.First(candidate =>
            string.Equals(
                candidate.Metadata.SlotName,
                selected,
                StringComparison.Ordinal));
        return SaveDossierView.FromSave(match.Metadata.SlotName, match.Save);
    }

    private static IEnumerable<SaveSlotMetadata> ReadSaveSlotMetadata(
        string? saveDirectory)
    {
        string directory = saveDirectory ?? DefaultSaveDirectory;
        if (!System.IO.Directory.Exists(directory)) yield break;

        foreach (string path in System.IO.Directory.GetFiles(directory, "*.json"))
        {
            string? slotName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(slotName)) continue;

            SaveGameV2 save;
            try
            {
                string text = System.IO.File.ReadAllText(path);
                save = SaveGameV2Json.DeserializeOrMigrate(text);
            }
            catch
            {
                // Continue should skip corrupt/empty slots and still offer the newest
                // valid save. LoadGame reports the detailed error when chosen directly.
                continue;
            }

            yield return new SaveSlotMetadata(slotName, save.SavedAtUtc);
        }
    }

    public static bool HasSaveSlots(string? saveDirectory = null) =>
        FindMostRecentSaveSlot(saveDirectory) != null;

    public static CampaignSaveV2 ReadMostRecentCampaignState(
        string? saveDirectory = null)
    {
        string directory = saveDirectory ?? DefaultSaveDirectory;
        if (!System.IO.Directory.Exists(directory))
            return new CampaignSaveV2();
        foreach (string path in System.IO.Directory.GetFiles(
                     directory, "*.json")
                 .OrderByDescending(System.IO.File.GetLastWriteTimeUtc))
        {
            try
            {
                var save = SaveGameV2Json.DeserializeOrMigrate(
                    System.IO.File.ReadAllText(path));
                return save.Campaign;
            }
            catch
            {
                // A corrupt unrelated slot must not hide a valid campaign profile.
            }
        }
        return new CampaignSaveV2();
    }

    private static string NormalizeSlotName(string slotName)
    {
        string result =
            System.IO.Path.GetFileNameWithoutExtension(slotName.Trim());
        if (string.IsNullOrWhiteSpace(result)
            || result.IndexOfAny(
                System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new System.ArgumentException(
                "Invalid save slot name.", nameof(slotName));
        return result;
    }
}
