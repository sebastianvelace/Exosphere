namespace Exosphere.Game;

using System.IO;
using Godot;

/// <summary>
/// Resolves the on-disk <c>data/</c> tree for System.IO loaders
/// (<see cref="Exosphere.Simulation.Systems.Universe.LoadFromDataDirectory"/>,
/// part catalogs, vehicle variants, campaigns).
///
/// Editor builds keep using <c>res://data</c> via <see cref="ProjectSettings.GlobalizePath"/>.
/// Exported builds cannot feed those loaders from the PCK alone, so the export script
/// copies the repo <c>data/</c> tree beside the executable and this helper prefers that
/// folder when the process is not the editor.
///
/// Godot resource APIs (scenes, shaders, textures) and FileAccess <c>res://</c> paths
/// continue to read from the PCK; only System.IO callers should use this helper.
/// </summary>
public static class GameDataPath
{
    public const string ResourcePath = "res://data";

    /// <summary>
    /// Absolute filesystem path to the simulation data directory.
    /// </summary>
    /// <param name="configured">
    /// Optional override (for example SimulationBridge.DataDirectory).
    /// Non-default values are globalized first; if missing on disk, falls back
    /// to the exported-beside-exe / res://data resolution order.
    /// </param>
    public static string Resolve(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured, ResourcePath, System.StringComparison.Ordinal))
        {
            string custom = ProjectSettings.GlobalizePath(configured);
            if (Directory.Exists(custom))
                return custom;
        }

        // Exported private builds: loose data/ next to the binary (see tools/export_game.sh).
        if (!OS.HasFeature("editor"))
        {
            string? besideExe = BesideExecutable("data");
            if (besideExe != null)
                return besideExe;
        }

        string globalized = ProjectSettings.GlobalizePath(ResourcePath);
        if (Directory.Exists(globalized))
            return globalized;

        // Last resort for atypical GlobalizePath results in exported builds.
        string? fallback = BesideExecutable("data");
        return fallback ?? globalized;
    }

    public static string Combine(params string[] relativeParts)
    {
        string root = Resolve();
        if (relativeParts == null || relativeParts.Length == 0)
            return root;
        string[] parts = new string[relativeParts.Length + 1];
        parts[0] = root;
        System.Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return Path.Combine(parts);
    }

    private static string? BesideExecutable(string folderName)
    {
        string? exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        if (string.IsNullOrEmpty(exeDir))
            return null;
        string candidate = Path.Combine(exeDir, folderName);
        return Directory.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }
}
