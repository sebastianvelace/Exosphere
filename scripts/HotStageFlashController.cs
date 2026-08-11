namespace Exosphere.Game;

using Godot;
using System.Linq;

/// <summary>
/// Short-lived hot-staging flash at Starship/Super Heavy separation.
/// It is visual only: the sim staging path remains owned by SimulationBridge/Vessel.
/// </summary>
[GlobalClass]
public partial class HotStageFlashController : Node3D
{
    // Slightly longer so a single state-gated capture during IsHotStageOverlapping
    // still shows interstage flash + soot rather than catching only the fade tail.
    private const float Duration = 1.75f;

    private MeshInstance3D? _plume;
    private MeshInstance3D? _shockRing;
    private GpuParticles3D? _soot;
    private OmniLight3D? _flashLight;
    private StandardMaterial3D? _plumeMat;
    private StandardMaterial3D? _ringMat;
    private float _age = Duration + 1f;
    private bool _wired;

    public override void _Ready()
    {
        Name = "HotStageFlashController";
        BuildVisuals();
        TryWireSignal();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_wired)
            TryWireSignal();

        if (_age > Duration)
            return;

        _age += (float)delta;
        float t = Mathf.Clamp(_age / Duration, 0f, 1f);
        float fade = 1f - t;
        float hot = Mathf.Pow(fade, 0.55f);

        Visible = true;

        if (_plume != null && _plumeMat != null)
        {
            _plume.Visible = true;
            _plume.Scale = new Vector3(1.15f + 0.90f * t, 9.0f + 6.0f * t, 1.15f + 0.90f * t);
            _plumeMat.AlbedoColor = new Color(1.0f, 0.64f, 0.22f, 0.78f * hot);
            _plumeMat.EmissionEnergyMultiplier = 7.6f * hot;
        }

        if (_shockRing != null && _ringMat != null)
        {
            _shockRing.Visible = true;
            float ringScale = 1.35f + 4.4f * t;
            _shockRing.Scale = new Vector3(ringScale, ringScale, ringScale);
            _ringMat.AlbedoColor = new Color(1.0f, 0.86f, 0.52f, 0.88f * fade);
            _ringMat.EmissionEnergyMultiplier = 4.6f * fade;
        }

        if (_flashLight != null)
        {
            _flashLight.Visible = true;
            _flashLight.LightEnergy = 20f * hot;
            _flashLight.OmniRange = 34f + 22f * fade;
        }

        if (_age >= Duration)
        {
            Visible = false;
            if (_plume != null) _plume.Visible = false;
            if (_shockRing != null) _shockRing.Visible = false;
            if (_flashLight != null) _flashLight.Visible = false;
        }
    }

    private void TryWireSignal()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null)
            return;

        bridge.VesselStaged += OnVesselStaged;
        _wired = true;
    }

    private void OnVesselStaged(string detachedVesselId)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null)
            return;

        var debris = bridge.Universe.Vessels.FirstOrDefault(v => v.Id == detachedVesselId);
        bool detachedBooster = debris?.Parts.Parts.Any(
            p => p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster")) ?? false;
        bool activeShip = bridge.ActiveVessel?.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily
            && (p.Definition.HasVehicleRole("ship_engines")
                || p.Definition.HasVehicleRole("command"))) ?? false;

        if (!detachedBooster || !activeShip)
            return;

        StartBurst();
    }

    private void StartBurst()
    {
        _age = 0f;
        Visible = true;

        if (_plume != null)
        {
            _plume.Visible = true;
            _plume.Scale = new Vector3(1.0f, 8.0f, 1.0f);
        }

        if (_shockRing != null)
        {
            _shockRing.Visible = true;
            _shockRing.Scale = Vector3.One;
        }

        if (_flashLight != null)
        {
            _flashLight.Visible = true;
            _flashLight.LightEnergy = 20f;
        }

        if (_soot != null)
            _soot.Restart();
    }

    private void BuildVisuals()
    {
        Position = new Vector3(0f, -0.15f, 0f);

        _plumeMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(1.0f, 0.60f, 0.20f, 0.78f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.70f, 0.28f),
            EmissionEnergyMultiplier = 7.6f,
        };

        var plumeMesh = new CylinderMesh
        {
            TopRadius = 0.30f,
            BottomRadius = 0.92f,
            Height = 1.0f,
            RadialSegments = 32,
            Rings = 18,
            CapTop = false,
            CapBottom = false,
        };

        _plume = new MeshInstance3D
        {
            Name = "HotStagePlume",
            Mesh = plumeMesh,
            Position = new Vector3(0f, -0.55f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            CustomAabb = new Aabb(new Vector3(-7f, -14f, -7f), new Vector3(14f, 16f, 14f)),
            MaterialOverride = _plumeMat,
        };
        AddChild(_plume);

        _ringMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(1.0f, 0.86f, 0.52f, 0.88f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.78f, 0.38f),
            EmissionEnergyMultiplier = 4.6f,
        };

        _shockRing = new MeshInstance3D
        {
            Name = "HotStageShockRing",
            Mesh = new TorusMesh { InnerRadius = 1.45f, OuterRadius = 1.92f, Rings = 64, RingSegments = 12 },
            Position = new Vector3(0f, -0.25f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            MaterialOverride = _ringMat,
        };
        AddChild(_shockRing);

        _soot = BuildSootParticles();
        AddChild(_soot);

        _flashLight = new OmniLight3D
        {
            Name = "HotStageFlashLight",
            Position = new Vector3(0f, -0.35f, 0f),
            LightColor = new Color(1.0f, 0.76f, 0.44f),
            OmniRange = 52f,
            LightEnergy = 0f,
            ShadowEnabled = false,
            LightSpecular = 0.65f,
            Visible = false,
        };
        AddChild(_flashLight);
    }

    private static GpuParticles3D BuildSootParticles()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(1.0f, 0.63f, 0.24f, 0.72f),
                new Color(0.42f, 0.36f, 0.31f, 0.56f),
                new Color(0.10f, 0.10f, 0.11f, 0.0f),
            },
            Offsets = new[] { 0f, 0.28f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 1.45f,
            EmissionRingInnerRadius = 0.35f,
            EmissionRingHeight = 0.2f,
            Direction = new Vector3(0f, -0.72f, 0f),
            Spread = 78f,
            InitialVelocityMin = 8f,
            InitialVelocityMax = 28f,
            DampingMin = 1.8f,
            DampingMax = 5.5f,
            Gravity = new Vector3(0f, -0.5f, 0f),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 2.2f,
            TurbulenceNoiseScale = 1.2f,
            TurbulenceInfluenceMin = 0.18f,
            TurbulenceInfluenceMax = 0.75f,
            ScaleMin = 0.9f,
            ScaleMax = 3.4f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };

        var quad = new QuadMesh { Size = new Vector2(2.2f, 2.2f) };
        quad.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture = SoftCircle,
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
        });

        return new GpuParticles3D
        {
            Name = "HotStageSoot",
            Amount = 190,
            Lifetime = 1.55f,
            OneShot = true,
            Explosiveness = 0.86f,
            Randomness = 0.5f,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static ImageTexture BuildSoftCircleTexture()
    {
        const int size = 64;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp(1f - r, 0f, 1f);
            a *= a;
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }

        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();
}
