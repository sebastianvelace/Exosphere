namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Persistence;
using Godot;
using System.Linq;

/// <summary>
/// Disk adapter for the authoritative, simulation-layer SaveGameV2 codec.
/// Writes are atomic and legacy partial saves are migrated on read.
/// </summary>
public static class SaveSystem
{
    private static string SaveDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".local", "share", "Exosphere", "saves");

    public static SaveGameV2? LastLoadedMetadata { get; private set; }

    public static void SaveGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        string safeSlot = NormalizeSlotName(slotName);
        System.IO.Directory.CreateDirectory(SaveDirectory);
        var save = SaveGameV2Codec.Capture(bridge.Universe, LastLoadedMetadata);
        string path = System.IO.Path.Combine(SaveDirectory, $"{safeSlot}.json");
        string temporary = path + ".tmp";
        System.IO.File.WriteAllText(temporary, SaveGameV2Json.Serialize(save));
        System.IO.File.Move(temporary, path, overwrite: true);
        LastLoadedMetadata = save;
        GD.Print($"[SaveSystem] Saved V2 to {path}");
    }

    public static bool LoadGame(string slotName = "quicksave")
    {
        string safeSlot = NormalizeSlotName(slotName);
        string path = System.IO.Path.Combine(SaveDirectory, $"{safeSlot}.json");
        if (!System.IO.File.Exists(path)) return false;

        var bridge = SimulationBridge.Instance;
        if (bridge == null) return false;

        try
        {
            string text = System.IO.File.ReadAllText(path);
            var save = SaveGameV2Json.DeserializeOrMigrate(text);
            string partsPath = ProjectSettings.GlobalizePath("res://data/parts");
            var catalog = PartCatalog.LoadFromDirectory(partsPath);
            SaveGameV2Codec.Restore(bridge.Universe, save, catalog);
            LastLoadedMetadata = save;
            GD.Print($"[SaveSystem] Loaded schema {save.SchemaVersion} from {path}");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PushError($"[SaveSystem] Could not load '{path}': {ex.Message}");
            return false;
        }
    }

    public static string[] ListSaveSlots()
    {
        if (!System.IO.Directory.Exists(SaveDirectory)) return [];
        return System.IO.Directory.GetFiles(SaveDirectory, "*.json")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(name => name, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeSlotName(string slotName)
    {
        string result = System.IO.Path.GetFileNameWithoutExtension(slotName.Trim());
        if (string.IsNullOrWhiteSpace(result)
            || result.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new System.ArgumentException("Invalid save slot name.", nameof(slotName));
        return result;
    }
}
