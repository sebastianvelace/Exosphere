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
