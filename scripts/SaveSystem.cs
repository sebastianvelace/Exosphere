namespace Exosphere.Game;

using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Persistence;

/// <summary>
/// Godot-facing save/load wrapper around <see cref="MissionSaveSerializer"/>.
/// File I/O + MissionManager / warp restore live here; pure serialize/restore is testable.
/// </summary>
public static class SaveSystem
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented       = true,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// When set (e.g. MainMenu Continue), <see cref="SimulationBridge"/> loads this slot
    /// after spawning the default pad stack, then clears the flag.
    /// </summary>
    public static string? PendingLoadSlot { get; set; }

    /// <summary>
    /// Mission phase to apply once <see cref="MissionManager"/> is ready (Continue flow
    /// loads before the deferred MissionManager child exists).
    /// </summary>
    public static MissionPhase? PendingMissionPhase { get; set; }

    private static string DefaultSaveDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".local", "share", "Exosphere", "saves");

    public static void SaveGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;
        SaveGame(bridge.Universe, slotName);
    }

    /// <summary>
    /// Saves without requiring Godot Instance (unit tests / path overrides).
    /// </summary>
    public static void SaveGame(
        Universe universe,
        string slotName = "quicksave",
        string? saveDirectory = null,
        string? missionPhase = null,
        int? warpIndex = null)
    {
        ArgumentNullException.ThrowIfNull(universe);

        string dir = saveDirectory ?? DefaultSaveDirectory;
        System.IO.Directory.CreateDirectory(dir);

        string phase = missionPhase
            ?? MissionManager.Instance?.Phase.ToString()
            ?? "PRE_LAUNCH";
        int warp = warpIndex ?? SimulationBridge.Instance?.WarpIndex ?? 0;

        var state = MissionSaveSerializer.Capture(universe, phase, warp);
        var path  = System.IO.Path.Combine(dir, $"{slotName}.json");
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(state, Opts));
        GD.Print($"[SaveSystem] Saved to {path}");
    }

    public static bool LoadGame(string slotName = "quicksave")
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return false;
        return LoadGame(bridge.Universe, slotName);
    }

    /// <summary>
    /// Loads without requiring Godot Instance. When a bridge exists, also restores
    /// mission phase, warp, and rebuilds the vessel renderer.
    /// </summary>
    public static bool LoadGame(
        Universe universe,
        string slotName = "quicksave",
        string? saveDirectory = null,
        string? partsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(universe);

        string dir  = saveDirectory ?? DefaultSaveDirectory;
        var path = System.IO.Path.Combine(dir, $"{slotName}.json");
        if (!System.IO.File.Exists(path)) return false;

        var text  = System.IO.File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<MissionSaveState>(text, Opts);
        if (state == null) return false;

        string partsDir = partsDirectory ?? ResolvePartsDirectory();
        MissionSaveSerializer.RestoreFromPartsDirectory(universe, state, partsDir);

        ApplyGameLayerRestore(state);
        GD.Print($"[SaveSystem] Loaded from {path}");
        return true;
    }

    /// <summary>
    /// Pure JSON helpers for tests that already hold a <see cref="MissionSaveState"/>.
    /// </summary>
    public static string SerializeToJson(MissionSaveState state) =>
        JsonSerializer.Serialize(state, Opts);

    public static MissionSaveState? DeserializeFromJson(string json) =>
        JsonSerializer.Deserialize<MissionSaveState>(json, Opts);

    public static string[] ListSaveSlots(string? saveDirectory = null)
    {
        string dir = saveDirectory ?? DefaultSaveDirectory;
        if (!System.IO.Directory.Exists(dir)) return [];
        return System.IO.Directory.GetFiles(dir, "*.json")
            .Select(p => System.IO.Path.GetFileNameWithoutExtension(p)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasSaveSlots(string? saveDirectory = null) =>
        ListSaveSlots(saveDirectory).Length > 0;

    private static void ApplyGameLayerRestore(MissionSaveState state)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge != null)
        {
            bridge.SetWarpIndex(state.WarpIndex);
            bridge.RebuildActiveVesselRenderer();
        }

        if (!string.IsNullOrEmpty(state.MissionPhase)
            && Enum.TryParse(state.MissionPhase, ignoreCase: true, out MissionPhase phase))
        {
            if (MissionManager.Instance != null)
                MissionManager.Instance.EnterPhase(phase);
            else
                PendingMissionPhase = phase;
        }
    }

    private static string ResolvePartsDirectory()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge != null)
        {
            var dataPath = ProjectSettings.GlobalizePath(bridge.DataDirectory);
            return System.IO.Path.Combine(dataPath, "parts");
        }

        // Fallback for headless/unit contexts without a bridge instance.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var parts = System.IO.Path.Combine(dir.FullName, "data", "parts");
            if (Directory.Exists(parts)) return parts;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not resolve data/parts directory for save load.");
    }
}
