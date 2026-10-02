namespace Exosphere.Game;

using Godot;

public enum GraphicsPreset { Automatic, IntegratedGpu, Quality }

/// <summary>Persistent presentation policy, independent of simulation state and clocks.</summary>
public static class GraphicsSettings
{
    private const string SettingsPath = "user://graphics.cfg";
    private static bool _loaded;
    public static GraphicsPreset Selected { get; private set; } = GraphicsPreset.Automatic;
    public static GraphicsPreset Resolved { get; private set; } = GraphicsPreset.Quality;
    public static bool IsIntegrated => Resolved == GraphicsPreset.IntegratedGpu;
    public static float RenderScale => IsIntegrated ? 0.75f : 1.0f;
    public static int CloudLightSamples => IsIntegrated ? 2 : 4;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var config = new ConfigFile();
        if (config.Load(SettingsPath) == Error.Ok)
        {
            var stored = config.GetValue("graphics", "preset", (int)GraphicsPreset.Automatic);
            int value = stored.VariantType == Variant.Type.Int
                ? stored.AsInt32() : (int)GraphicsPreset.Automatic;
            Selected = Enum.IsDefined(typeof(GraphicsPreset), value)
                ? (GraphicsPreset)value : GraphicsPreset.Automatic;
        }
        // Reproducible GPU A/B runs must not overwrite the player's preference.
        string diagnostic = OS.GetEnvironment("EXOSPHERE_GRAPHICS_PRESET").Trim().ToLowerInvariant();
        Selected = diagnostic switch
        {
            "integrated" => GraphicsPreset.IntegratedGpu,
            "quality" => GraphicsPreset.Quality,
            "automatic" => GraphicsPreset.Automatic,
            _ => Selected,
        };
        Resolve();
    }

    public static void SetPreset(GraphicsPreset preset)
    {
        Load();
        Selected = Enum.IsDefined(preset) ? preset : GraphicsPreset.Automatic;
        Resolve();
        var config = new ConfigFile();
        config.SetValue("graphics", "preset", (int)Selected);
        if (config.Save(SettingsPath) != Error.Ok)
            GD.PushWarning("Graphics preference could not be saved; the current session still uses it.");
    }

    private static void Resolve()
    {
        Resolved = Selected == GraphicsPreset.Automatic
            ? RenderingServer.GetVideoAdapterType() == RenderingDevice.DeviceType.IntegratedGpu
                ? GraphicsPreset.IntegratedGpu : GraphicsPreset.Quality
            : Selected;
    }

    public static void ApplyViewport(Viewport viewport)
    {
        Load();
        // Scaling the 3D buffer leaves CanvasLayer telemetry and menu text native.
        viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = RenderScale;
        viewport.Msaa3D = IsIntegrated ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X;
        string renderer = RenderingServer.GetCurrentRenderingMethod().ToString();
        bool supportsScreenSpaceAA = renderer is "forward_plus" or "mobile";
        viewport.ScreenSpaceAA = IsIntegrated && supportsScreenSpaceAA
            ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        GD.Print($"GRAPHICS_PROFILE selected={Selected} resolved={Resolved} "
            + $"scale3d={RenderScale:F2} msaa={viewport.Msaa3D} "
            + $"cloudLightSamples={CloudLightSamples} uiScaleUnchanged=True physicsAuthority=False");
    }

    internal static void BindCloudQuality(ShaderMaterial material)
    {
        Load();
        if (material.GetShaderParameter("cloud_light_sample_budget").AsInt32() != CloudLightSamples)
            material.SetShaderParameter("cloud_light_sample_budget", CloudLightSamples);
    }
}
