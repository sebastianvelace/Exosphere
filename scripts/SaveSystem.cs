namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Systems;
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
    public static VesselSystemsState? PendingSystemsState { get; set; }
    public static MissionCallbackQueueState? PendingCallbackState { get; set; }

    public static void SaveGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        string safeSlot = NormalizeSlotName(slotName);
        System.IO.Directory.CreateDirectory(DefaultSaveDirectory);
        var save = SaveGameV2Codec.Capture(
            bridge.Universe, LastLoadedMetadata);
        save.VesselSystems.Clear();
        save.Mission.NextCallbackSequence = 1;
        save.Mission.CallbackEvents = new();
        if (bridge.ActiveVessel is { } activeVessel
            && SystemsController.Instance is { } systemsController)
        {
            save.VesselSystems[activeVessel.Id] = systemsController.CaptureSaveState(
                activeVessel.Id, bridge.Universe.CurrentTime);
        }
        if (CampaignRuntime.Instance?.CaptureMissionState() is { } mission)
            save.Mission = mission;
        if (CampaignRuntime.Instance?.Campaign?.State is { } campaign)
            save.Campaign = campaign;
        save.Mission.Phase =
            MissionManager.Instance?.Phase.ToString()
            ?? save.Mission.Phase;
        if (MissionManager.Instance is { } missionManager)
        {
            MissionCallbackQueueState callbackState =
                missionManager.CaptureCallbackState();
            save.Mission.NextCallbackSequence = callbackState.NextSequence;
            save.Mission.CallbackEvents = callbackState.Events;
        }
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
        PendingSystemsState = null;
        PendingCallbackState = null;
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
            PendingSystemsState = null;
            PendingCallbackState = null;

            if (save.ActiveVesselId is { } activeId
                && save.VesselSystems.TryGetValue(activeId, out var systemsState))
            {
                if (SystemsController.Instance is { } systemsController)
                    systemsController.RestoreSaveState(
                        systemsState, activeId, save.SimulationTime);
                else
                    PendingSystemsState = systemsState;
            }
            else
            {
                PendingSystemsState = null;
                SystemsController.Instance?.ResetForLoadedSimulation();
            }

            var callbackState = new MissionCallbackQueueState
            {
                NextSequence = save.Mission.NextCallbackSequence,
                Events = save.Mission.CallbackEvents,
            };
            callbackState.Validate();
            if (MissionManager.Instance is { } missionManager)
                missionManager.RestoreCallbackState(callbackState);
            else
                PendingCallbackState = callbackState;

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

    public static bool HasSaveSlots(string? saveDirectory = null) =>
        ListSaveSlots(saveDirectory).Length > 0;

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
