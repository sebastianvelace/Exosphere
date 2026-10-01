namespace Exosphere.Game;

using System.Text.Json;
using Godot;
using Exosphere.Simulation;

/// <summary>Source-labelled local weather appearance; never writes physical atmosphere state.</summary>
internal static class CloudWeatherPresentation
{
    private static JsonDocument? _starbaseProfile;

    public static void Bind(ShaderMaterial material, CelestialBody body)
    {
        var bridge = SimulationBridge.Instance;
        bool starbase = body.Id == "earth"
            && bridge?.LaunchSiteId.StartsWith("starbase", StringComparison.OrdinalIgnoreCase) == true;
        material.SetShaderParameter("cloud_local_weather_enabled", starbase);
        if (!starbase || bridge?.LaunchSiteOrNull is not { } site) return;
        if (_starbaseProfile == null)
        {
            using var file = Godot.FileAccess.Open("res://data/launch_sites/starbase_weather.json", Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                material.SetShaderParameter("cloud_local_weather_enabled", false);
                GD.PushError("Starbase visual weather profile is unavailable.");
                return;
            }
            _starbaseProfile = JsonDocument.Parse(file.GetAsText());
            GD.Print("CLOUD_WEATHER profile=starbase-coastal-cumulus-visual status=appearance_calibration physicsAuthority=False");
        }
        var profile = _starbaseProfile.RootElement;
        var siteDirection = (site.GetPosition(body, bridge.Universe.CurrentTime) - body.Position).Normalized;
        var direction = new Vector3((float)siteDirection.X, (float)siteDirection.Y, (float)siteDirection.Z);
        material.SetShaderParameter("cloud_local_center", (FloatingOrigin.EarthTextureBasis.Inverse() * direction).Normalized());
        material.SetShaderParameter("cloud_local_density_multiplier", profile.GetProperty("optical_density_multiplier").GetSingle());
        material.SetShaderParameter("cloud_local_radius_m", profile.GetProperty("region_radius_m").GetSingle());
        material.SetShaderParameter("cloud_local_scale_m", profile.GetProperty("horizontal_scale_m").GetSingle());
        material.SetShaderParameter("cloud_local_base_m", profile.GetProperty("cloud_base_m").GetSingle());
        material.SetShaderParameter("cloud_local_top_m", profile.GetProperty("cloud_top_m").GetSingle());
    }
}
