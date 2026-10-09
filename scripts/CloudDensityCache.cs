namespace Exosphere.Game;

using Godot;

/// <summary>
/// Presentation-only, double-buffered local density atlas. One bounded slice is
/// rendered per frame; no readback, worker or simulation state mutation occurs.
/// The distant weather field retains its procedural path.
/// </summary>
public partial class CloudDensityCache : Node
{
    private const int Layers = 32, Columns = 16;
    private const float HalfExtentM = 16000;
    private int _side;
    public static CloudDensityCache? Instance { get; private set; }
    private readonly SubViewport[] _buffers = new SubViewport[2];
    private readonly ColorRect[] _tiles = new ColorRect[2];
    private readonly ShaderMaterial[] _materials = new ShaderMaterial[2];
    private int _front = -1, _back, _slice;
    private bool _building, _slicePending, _sliceRendered;
    private float _frontWind, _buildingWind, _bottom, _height;
    private float _frontBottom, _frontHeight, _frontHorizontalScale;
    private double _windRate;
    private float _horizontalScale;
    private Vector3 _centre, _frontCentre;
    public bool PresentationEnabled { get; set; } = true;

    public override void _Ready()
    {
        Instance = this;
        ProcessPriority = 30;
        // Keep even the Quality path within the small-VRAM framebuffer budget.
        // A larger atlas also allocates renderer intermediates, not just its pixels.
        _side = 256;
        RenderingServer.FramePostDraw += OnFramePostDraw;
        var shader = GD.Load<Shader>("res://assets/shaders/cloud_density_bake.gdshader");
        for (int i = 0; i < 2; i++)
        {
            _buffers[i] = new SubViewport
            {
                Name = "DensityAtlas" + i, Size = new Vector2I(_side * Columns, _side * Layers / Columns),
                Disable3D = true, TransparentBg = true,
                RenderTargetClearMode = SubViewport.ClearMode.Never,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(_buffers[i]);
            _materials[i] = new ShaderMaterial { Shader = shader };
            _materials[i].SetShaderParameter("cache_side", _side);
            _materials[i].SetShaderParameter("cache_half_extent_m", HalfExtentM);
            _tiles[i] = new ColorRect { Size = new Vector2(_side, _side), Material = _materials[i] };
            _buffers[i].AddChild(_tiles[i]);
        }
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || GetViewport().Disable3D) return;
        var body = bridge.Universe.GetDominantBody(vessel.Position);
        if (body.Id != "earth" || !bridge.LaunchSiteId.StartsWith("starbase", StringComparison.OrdinalIgnoreCase)
            || body.Atmosphere?.Optics is not { HasCloudLayer: true } optics) return;
        // The previous slice was submitted before this frame. Swap only after
        // all layers are complete, so callers never see an incomplete volume.
        if (_slicePending && !_sliceRendered) return;
        if (_slicePending)
        {
            _slicePending = false;
            _sliceRendered = false;
            if (++_slice == Layers)
            {
                _front = _back;
                _back = 1 - _front;
                _frontWind = _buildingWind;
                _frontCentre = _centre;
                _frontBottom = _bottom;
                _frontHeight = _height;
                _frontHorizontalScale = _horizontalScale;
                _building = false;
            }
        }
        float wind = (float)(bridge.Universe.CurrentTime * optics.CloudWindRadiansPerSecond / Mathf.Tau);
        _windRate = optics.CloudWindRadiansPerSecond;
        if (!_building)
        {
            // Less than four metres of horizontal advection between refreshes.
            if (_front >= 0 && System.Math.Abs(wind - _frontWind) * 64 * _horizontalScale < 4) return;
            if (FloatingOrigin.CameraAltOverEarth > 6000) return;
            _slice = 0;
            _buildingWind = wind;
            _building = true;
            CloudWeatherPresentation.Bind(_materials[_back], body, false);
            _horizontalScale = _materials[_back].GetShaderParameter("cloud_local_scale_m").AsSingle();
            _centre = _materials[_back].GetShaderParameter("cloud_local_center").AsVector3();
            _bottom = _materials[_back].GetShaderParameter("cloud_local_base_m").AsSingle() - 100;
            _height = _materials[_back].GetShaderParameter("cloud_local_top_m").AsSingle() + 100 - _bottom;
            _materials[_back].SetShaderParameter("cloud_planet_radius_m", (float)FloatingOrigin.CameraEarthRadiusM);
            _materials[_back].SetShaderParameter("cloud_world_to_texture", FloatingOrigin.EarthTextureBasis.Inverse());
            _materials[_back].SetShaderParameter("cloud_longitude_offset", _buildingWind);
            _materials[_back].SetShaderParameter("cache_bottom_m", _bottom);
            _materials[_back].SetShaderParameter("cache_height_m", _height);
        }
        _tiles[_back].Position = new Vector2(_slice % Columns * _side, _slice / Columns * _side);
        _materials[_back].SetShaderParameter("cache_slice", _slice);
        _buffers[_back].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _slicePending = true;
    }

    private void OnFramePostDraw()
    {
        if (_slicePending) _sliceRendered = true;
    }

    internal static void Bind(ShaderMaterial material)
    {
        var cache = Instance;
        bool ready = cache is { _front: >= 0, PresentationEnabled: true };
        if (ready && SimulationBridge.Instance is { } bridge)
        {
            float wind = (float)(bridge.Universe.CurrentTime * cache!._windRate / Mathf.Tau);
            // Fail closed after accelerated coast or a scene-clock jump. Density
            // drift is bounded to half a grid cell while the next atlas builds.
            ready = System.Math.Abs(wind - cache._frontWind) * 64 * cache._frontHorizontalScale
                <= HalfExtentM / cache._side;
        }
        material.SetShaderParameter("cloud_density_cache_enabled", ready);
        if (!ready) return;
        material.SetShaderParameter("cloud_density_cache_tex", cache!._buffers[cache._front].GetTexture());
        material.SetShaderParameter("cloud_density_cache_center", cache._frontCentre);
        material.SetShaderParameter("cloud_density_cache_bottom_m", cache._frontBottom);
        material.SetShaderParameter("cloud_density_cache_height_m", cache._frontHeight);
        material.SetShaderParameter("cloud_density_cache_side", cache._side);
        material.SetShaderParameter("cloud_density_cache_half_extent_m", HalfExtentM);
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= OnFramePostDraw;
        if (Instance == this) Instance = null;
    }
}
