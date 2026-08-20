namespace Exosphere.Game;

using System.Globalization;
using Godot;

/// <summary>
/// Optional in-process render probe. It is created only when
/// <c>EXOSPHERE_RENDER_PROBE=1</c> is present, so normal gameplay does not enable
/// renderer timing or emit telemetry. GPU time is deliberately reported as
/// NOT_MEASURED when the backend cannot provide a positive sample.
/// </summary>
public partial class RenderPerformanceProbe : Node
{
    private const double SamplePeriodSeconds = 0.5;
    private const int WarmupFrames = 3;

    private Rid _viewportRid;
    private double _sampleAccumulator;
    private int _warmupRemaining = WarmupFrames;
    private int _sampleIndex;
    private bool _measurementEnabled;
    private bool _abOverrideApplied;
    private readonly string _abOverride =
        OS.GetEnvironment("EXOSPHERE_RENDER_AB").Trim().ToLowerInvariant();

    public static bool IsRequested() =>
        string.Equals(OS.GetEnvironment("EXOSPHERE_RENDER_PROBE"), "1", StringComparison.Ordinal);

    public override void _Ready()
    {
        _viewportRid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);
        _measurementEnabled = true;

        GD.Print("PERF_GPU_CONFIG source=in_process_rendering_server " +
                 $"driver={Safe(RenderingServer.GetCurrentRenderingDriverName())} " +
                 $"method={Safe(RenderingServer.GetCurrentRenderingMethod())} " +
                 $"adapter={Safe(RenderingServer.GetVideoAdapterName())} " +
                 $"vendor={Safe(RenderingServer.GetVideoAdapterVendor())} " +
                 $"adapter_type={Safe(RenderingServer.GetVideoAdapterType().ToString())}");
    }

    public override void _Process(double delta)
    {
        ApplyAbOverrideIfRequested();

        if (_warmupRemaining > 0)
        {
            _warmupRemaining--;
            return;
        }

        _sampleAccumulator += System.Math.Max(0.0, delta);
        if (_sampleAccumulator < SamplePeriodSeconds) return;
        _sampleAccumulator = 0.0;

        double cpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewportRid);
        double gpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewportRid);
        ulong objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        ulong primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        ulong drawCalls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong videoMem = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);

        _sampleIndex++;
        GD.Print($"PERF_GPU sample={_sampleIndex} frame={Engine.GetProcessFrames()} " +
                 $"cpu_render_ms={Metric(cpuMs)} gpu_ms={Metric(gpuMs)} " +
                 $"objects={Count(objects)} primitives={Count(primitives)} " +
                 $"draw_calls={Count(drawCalls)} video_mem_bytes={Count(videoMem)} " +
                 "source=in_process_rendering_server");
    }

    public override void _ExitTree()
    {
        if (!_measurementEnabled) return;
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, false);
        _measurementEnabled = false;
    }

    private void ApplyAbOverrideIfRequested()
    {
        if (_abOverrideApplied || string.IsNullOrEmpty(_abOverride))
        {
            _abOverrideApplied = true;
            return;
        }

        Node root = GetTree().Root;
        bool applied = _abOverride switch
        {
            "hide_pad" => SetVisible(root.FindChild("LaunchPadController", true, false), false),
            "hide_launch_effects" => SetVisible(
                root.FindChild("LaunchEffectsController", true, false), false),
            "hide_vessel" => SetVisible(root.FindChild("VesselRenderer", true, false), false),
            "hide_hud" => SetVisible(root.FindChild("HUDController", true, false), false),
            "hide_starfield" => SetVisible(
                root.FindChild("StarfieldController", true, false), false),
            "hide_earth_ground" => SetVisible(
                root.FindChild("EarthGroundController", true, false), false),
            "hide_sky" => DisableSky(root),
            "sky_quality_low" => SetSkyQuality(root, 0.25f),
            "sky_quality_min" => SetSkyQuality(root, 0.0f),
            "no_directional_shadows" => DisableDirectionalShadows(root),
            // Isolated scaled-space Earth A/B profiles. These are deliberately opt-in
            // diagnostics; the official material values remain in PlanetMaterials.
            "earth_day_gain_090" => SetEarthShaderParameter(
                root, "day_gain", 0.90f, _abOverride),
            "earth_day_gain_075" => SetEarthShaderParameter(
                root, "day_gain", 0.75f, _abOverride),
            "earth_cloud_amount_065" => SetEarthShaderParameter(
                root, "cloud_amount", 0.65f, _abOverride),
            "earth_cloud_amount_040" => SetEarthShaderParameter(
                root, "cloud_amount", 0.40f, _abOverride),
            _ => false,
        };

        if (applied)
        {
            _abOverrideApplied = true;
            GD.Print($"PERF_GPU_AB mode={_abOverride} applied=true");
        }
    }

    private static bool SetVisible(Node? node, bool visible)
    {
        if (node is Node3D node3D)
        {
            node3D.Visible = visible;
            return true;
        }

        if (node is CanvasItem canvasItem)
        {
            canvasItem.Visible = visible;
            return true;
        }

        return false;
    }

    private static bool DisableDirectionalShadows(Node root)
    {
        if (root.FindChild("DirectionalLight3D", true, false) is not DirectionalLight3D light)
            return false;
        light.ShadowEnabled = false;
        return true;
    }

    private static bool DisableSky(Node root)
    {
        if (root.FindChild("WorldEnvironment", true, false) is not WorldEnvironment world
            || world.Environment == null)
            return false;
        world.Environment.Sky = null;
        return true;
    }

    private static bool SetSkyQuality(Node root, float quality)
    {
        if (root.FindChild("WorldEnvironment", true, false) is not WorldEnvironment world
            || world.Environment?.Sky?.SkyMaterial is not ShaderMaterial material)
            return false;
        material.SetShaderParameter("atmosphere_quality", quality);
        return true;
    }

    private static bool SetEarthShaderParameter(
        Node root, string parameter, float value, string mode)
    {
        if (root.FindChild("Earth_mesh", true, false) is not MeshInstance3D earth)
            return false;
        if ((earth.GetSurfaceOverrideMaterial(0) ?? earth.GetActiveMaterial(0))
            is not ShaderMaterial material)
            return false;

        material.SetShaderParameter(parameter, value);
        GD.Print($"PERF_GPU_AB mode={mode} applied=true parameter={parameter} value={value:F3}");
        return true;
    }

    private static string Metric(double value) =>
        double.IsFinite(value) && value > 0.0
            ? value.ToString("F3", CultureInfo.InvariantCulture)
            : "NOT_MEASURED";

    private static string Count(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "NOT_MEASURED"
            : value.Replace(' ', '_').Replace('\t', '_').Replace('\n', '_').Replace('\r', '_');
}
