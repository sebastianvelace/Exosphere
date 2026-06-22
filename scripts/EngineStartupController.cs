namespace Exosphere.Game;

using Godot;
using System.Linq;

/// <summary>
/// Visual-only pre-liftoff engine startup: Raptor ignition glow, deck flare and
/// chill/steam while the hold-downs are still clamped.
/// </summary>
[GlobalClass]
public partial class EngineStartupController : Node3D
{
    private const float MetresPerUnit = 2.8f;
    private const float MaxStartupAltitudeM = 80f;

    private MeshInstance3D? _flareCone;
    private MeshInstance3D? _deckGlow;
    private GpuParticles3D? _steam;
    private GpuParticles3D? _sparks;
    private OmniLight3D? _light;
    private StandardMaterial3D? _flareMat;
    private StandardMaterial3D? _deckMat;
    private float _intensity;

    public override void _Ready()
    {
        Name = "EngineStartupController";
        BuildVisuals();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var body = vessel != null ? bridge?.Universe.GetDominantBody(vessel.Position) : null;
        if (bridge == null || vessel == null || body == null || body.Id != "earth")
        {
            Drive(0f, delta);
            return;
        }

        bool hasBooster = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        double altitude = vessel.GetAltitude(body);
        bool startup = hasBooster
            && vessel.IsGroundHeld
            && vessel.Parts.ActiveEngines.Any()
            && vessel.Throttle > 0.01
            && altitude < MaxStartupAltitudeM;

        float throttle = startup ? (float)vessel.Throttle : 0f;
        Drive(throttle, delta);

        if (_intensity <= 0.01f)
            return;

        Vector3 up = ToGodot((vessel.Position - body.Position).Normalized);
        if (up.LengthSquared() < 1e-6f)
            up = Vector3.Up;

        float altUnits = (float)(altitude / MetresPerUnit);
        Position = -up * altUnits;
        AlignUp(this, up);
    }

    private void Drive(float targetThrottle, double delta)
    {
        float target = targetThrottle <= 0.01f ? 0f : Mathf.Clamp(targetThrottle, 0f, 1f);
        target = Mathf.Pow(target, 0.65f);

        float rate = target > _intensity ? 5.5f : 2.2f;
        _intensity = Mathf.Lerp(_intensity, target, Mathf.Clamp((float)delta * rate, 0f, 1f));

        if (_intensity <= 0.01f)
        {
            Visible = false;
            SetEmitting(false);
            if (_flareCone != null) _flareCone.Visible = false;
            if (_deckGlow != null) _deckGlow.Visible = false;
            if (_light != null) _light.Visible = false;
            return;
        }

        Visible = true;
        SetEmitting(true);

        float flicker = 0.88f + GD.Randf() * 0.22f;
        float k = _intensity * flicker;

        if (_flareCone != null && _flareMat != null)
        {
            _flareCone.Visible = true;
            _flareCone.Scale = new Vector3(1.25f + 1.2f * k, 3.0f + 4.8f * k, 1.25f + 1.2f * k);
            _flareMat.AlbedoColor = new Color(0.95f, 0.56f, 0.18f, 0.16f + 0.36f * k);
            _flareMat.EmissionEnergyMultiplier = 2.8f + 5.2f * k;
        }

        if (_deckGlow != null && _deckMat != null)
        {
            _deckGlow.Visible = true;
            float ringScale = 1.0f + 1.2f * k;
            _deckGlow.Scale = new Vector3(ringScale, ringScale, ringScale);
            _deckMat.AlbedoColor = new Color(0.85f, 0.92f, 1.0f, 0.12f + 0.42f * k);
            _deckMat.EmissionEnergyMultiplier = 2.0f + 4.0f * k;
        }

        if (_light != null)
        {
            _light.Visible = true;
            _light.LightEnergy = 3.5f + 13.0f * k;
            _light.OmniRange = 16f + 26f * k;
        }

        if (_steam != null)
        {
            _steam.AmountRatio = Mathf.Clamp(0.25f + _intensity * 0.75f, 0f, 1f);
            _steam.SpeedScale = 0.65f + _intensity * 0.7f;
        }

        if (_sparks != null)
        {
            _sparks.AmountRatio = Mathf.Clamp((_intensity - 0.18f) / 0.82f, 0f, 1f);
            _sparks.SpeedScale = 0.8f + _intensity * 0.9f;
        }
    }

    private void SetEmitting(bool on)
    {
        if (_steam != null) _steam.Emitting = on;
        if (_sparks != null) _sparks.Emitting = on;
    }

    private void BuildVisuals()
    {
        _flareMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.95f, 0.56f, 0.18f, 0.35f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.55f, 0.18f),
            EmissionEnergyMultiplier = 4.5f,
        };

        _flareCone = new MeshInstance3D
        {
            Name = "StartupFlareCone",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.70f,
                BottomRadius = 1.18f,
                Height = 1.0f,
                RadialSegments = 32,
                Rings = 12,
                CapTop = false,
                CapBottom = false,
            },
            Position = new Vector3(0f, -1.0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            CustomAabb = new Aabb(new Vector3(-8f, -10f, -8f), new Vector3(16f, 12f, 16f)),
            MaterialOverride = _flareMat,
        };
        AddChild(_flareCone);

        _deckMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.85f, 0.92f, 1.0f, 0.35f),
            EmissionEnabled = true,
            Emission = new Color(0.75f, 0.88f, 1.0f),
            EmissionEnergyMultiplier = 3.5f,
        };

        _deckGlow = new MeshInstance3D
        {
            Name = "StartupDeckGlow",
            Mesh = new TorusMesh { InnerRadius = 1.9f, OuterRadius = 3.2f, Rings = 72, RingSegments = 8 },
            Position = new Vector3(0f, -0.08f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
            MaterialOverride = _deckMat,
        };
        AddChild(_deckGlow);

        _steam = BuildSteam();
        AddChild(_steam);

        _sparks = BuildSparks();
        AddChild(_sparks);

        _light = new OmniLight3D
        {
            Name = "StartupEngineLight",
            Position = new Vector3(0f, -0.5f, 0f),
            LightColor = new Color(1.0f, 0.72f, 0.42f),
            OmniRange = 24f,
            LightEnergy = 0f,
            LightSpecular = 0.55f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(_light);

        SetEmitting(false);
    }

    private static GpuParticles3D BuildSteam()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.92f, 0.96f, 1.0f, 0.58f),
                new Color(0.75f, 0.78f, 0.80f, 0.34f),
                new Color(0.55f, 0.56f, 0.58f, 0.0f),
            },
            Offsets = new[] { 0f, 0.42f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 2.2f,
            EmissionRingInnerRadius = 0.55f,
            EmissionRingHeight = 0.4f,
            Direction = new Vector3(0f, 0.25f, 1f).Normalized(),
            Spread = 86f,
            Flatness = 0.68f,
            InitialVelocityMin = 7f,
            InitialVelocityMax = 22f,
            DampingMin = 2.4f,
            DampingMax = 7.0f,
            Gravity = new Vector3(0f, 1.6f, 0f),
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 2.2f,
            TurbulenceNoiseScale = 1.0f,
            TurbulenceInfluenceMin = 0.2f,
            TurbulenceInfluenceMax = 0.7f,
            ScaleMin = 1.8f,
            ScaleMax = 5.2f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };

        var quad = new QuadMesh { Size = new Vector2(3.6f, 3.6f) };
        quad.SurfaceSetMaterial(0, ParticleDrawMaterial(BaseMaterial3D.BlendModeEnum.Mix, 0.9f));

        return new GpuParticles3D
        {
            Name = "StartupChillSteam",
            Amount = 120,
            Lifetime = 2.8f,
            Preprocess = 0.25f,
            Explosiveness = 0.05f,
            Randomness = 0.55f,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static GpuParticles3D BuildSparks()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(1.0f, 0.74f, 0.26f, 0.90f),
                new Color(0.90f, 0.30f, 0.08f, 0.55f),
                new Color(0.18f, 0.12f, 0.08f, 0.0f),
            },
            Offsets = new[] { 0f, 0.35f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = 1.55f,
            EmissionRingInnerRadius = 0.2f,
            EmissionRingHeight = 0.12f,
            Direction = new Vector3(0f, -0.55f, 0f),
            Spread = 62f,
            InitialVelocityMin = 4f,
            InitialVelocityMax = 18f,
            DampingMin = 0.8f,
            DampingMax = 2.4f,
            Gravity = new Vector3(0f, -1.8f, 0f),
            ScaleMin = 0.16f,
            ScaleMax = 0.48f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };

        var quad = new QuadMesh { Size = new Vector2(0.65f, 0.65f) };
        quad.SurfaceSetMaterial(0, ParticleDrawMaterial(BaseMaterial3D.BlendModeEnum.Add, 1.8f));

        return new GpuParticles3D
        {
            Name = "StartupIgnitionFlecks",
            Amount = 70,
            Lifetime = 0.75f,
            Explosiveness = 0.28f,
            Randomness = 0.65f,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static StandardMaterial3D ParticleDrawMaterial(BaseMaterial3D.BlendModeEnum blend, float energy)
    {
        return new StandardMaterial3D
        {
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = blend,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture = SoftCircle,
            AlbedoColor = Colors.White,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = energy,
            VertexColorUseAsAlbedo = true,
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

    private static Vector3 ToGodot(Exosphere.Simulation.Math.Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    private static void AlignUp(Node3D node, Vector3 up)
    {
        up = up.Normalized();
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        node.Transform = new Transform3D(new Basis(x, up, z), node.Position);
    }

    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();
}
