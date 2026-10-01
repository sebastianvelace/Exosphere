namespace Exosphere.Game;

using Godot;
using System.Text.Json;

/// <summary>Shared presentation georeference for local and orbital site imagery.</summary>
public static class LaunchTerrainImagery
{
    public static void Bind(ShaderMaterial material, string launchSiteId)
    {
        if (!launchSiteId.StartsWith("starbase", System.StringComparison.OrdinalIgnoreCase)) return;
        string json = FileAccess.GetFileAsString("res://data/launch_sites/starbase_terrain.json");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        static Vector2 Size(JsonElement entry)
        {
            var values = entry.GetProperty("runtime_size_m");
            return new Vector2(values[0].GetSingle(), values[1].GetSingle());
        }
        var regionalSize = Size(root.GetProperty("ortho"));
        material.SetShaderParameter("regional_size_m", regionalSize);
        material.SetShaderParameter("macro_size_m", Size(root.GetProperty("macro_ortho")));
        material.SetShaderParameter("precise_coverage_masks", true);
        var context = root.GetProperty("context_ortho");
        var texture = GD.Load<Texture2D>("res://" + context.GetProperty("file").GetString());
        material.SetShaderParameter("context_ortho_enabled", texture != null);
        if (texture != null) material.SetShaderParameter("context_ortho_tex", texture);
        material.SetShaderParameter("context_size_m", Size(context));
        // Pad 2 shares the same measured raster centre as Pad 1. Changing the
        // active pad must not drag roads/shorelines along with the launch origin.
        var site = SimulationBridge.Instance?.LaunchSiteOrNull;
        if (site != null)
        {
            var centre = root.GetProperty("center");
            var bounds = root.GetProperty("bbox_wgs84");
            material.SetShaderParameter("site_raster_offset_m", new Vector2(
                (float)((site.Longitude - centre.GetProperty("longitude").GetDouble())
                    / (bounds.GetProperty("east").GetDouble() - bounds.GetProperty("west").GetDouble()) * regionalSize.X),
                (float)((site.Latitude - centre.GetProperty("latitude").GetDouble())
                    / (bounds.GetProperty("north").GetDouble() - bounds.GetProperty("south").GetDouble()) * regionalSize.Y)));
        }
        GD.Print($"TERRAIN_COVERAGE site={launchSiteId} context={texture != null} " +
            $"regionalSizeM={regionalSize} macroSizeM={Size(root.GetProperty("macro_ortho"))} " +
            "mask=observed_footprint physicsAuthority=False");
    }
}
