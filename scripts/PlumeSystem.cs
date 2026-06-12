namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;

/// <summary>
/// GPU-particle engine plume system for Starship + Super Heavy.
/// Create one PlumeSystem per vessel renderer; call Update() every frame.
/// Each ring of engines is represented by a single GPUParticles3D emitter
/// using ring-shape emission (matching the engine bell ring radius).
/// </summary>
public partial class PlumeSystem : Node3D
{
    private readonly List<GpuParticles3D> _shEmitters   = new();
    private readonly List<GpuParticles3D> _shipEmitters = new();

    // ── Setup calls (called once from VesselRenderer after building geometry) ─

    /// <summary>Sets up 3 ring emitters + 1 central core emitter for Super Heavy.</summary>
    public void SetupSH(float innerR, float midR, float outerR, float bellY)
    {
        // Bright core burst — point emitter at centre for the white-hot plume column
        _shEmitters.Add(BuildRingEmitter("SH_Core", bellY, ringRadius: 0.05f,
            amount: 200, lifetime: 1.1f,
            vMin: 18f, vMax: 45f,
            coreColor:  new Color(1.00f, 1.00f, 1.00f),
            trailColor: new Color(1.00f, 0.90f, 0.60f)));

        // Inner ring: 3 engines
        _shEmitters.Add(BuildRingEmitter("SH_Inner", bellY, ringRadius: innerR,
            amount: 180, lifetime: 1.0f,
            vMin: 15f, vMax: 40f,
            coreColor:  new Color(1.00f, 1.00f, 0.90f),
            trailColor: new Color(1.00f, 0.55f, 0.05f)));

        // Mid ring: 10 engines
        _shEmitters.Add(BuildRingEmitter("SH_Mid", bellY, ringRadius: midR,
            amount: 280, lifetime: 0.90f,
            vMin: 12f, vMax: 35f,
            coreColor:  new Color(1.00f, 0.85f, 0.55f),
            trailColor: new Color(1.00f, 0.40f, 0.02f)));

        // Outer ring: 20 engines — wider fireball cloud
        _shEmitters.Add(BuildRingEmitter("SH_Outer", bellY, ringRadius: outerR,
            amount: 360, lifetime: 0.80f,
            vMin: 10f, vMax: 30f,
            coreColor:  new Color(1.00f, 0.75f, 0.35f),
            trailColor: new Color(0.80f, 0.25f, 0.00f)));
    }

    /// <summary>Sets up 2 ring emitters for Starship (3 vac + 3 SL Raptors).</summary>
    public void SetupStarship(float vacR, float slR, float baseY)
    {
        // Core column
        _shipEmitters.Add(BuildRingEmitter("Ship_Core", baseY - 2.0f, ringRadius: 0.04f,
            amount: 120, lifetime: 1.2f,
            vMin: 20f, vMax: 50f,
            coreColor:  new Color(1.00f, 1.00f, 1.00f),
            trailColor: new Color(0.80f, 0.90f, 1.00f)));

        // Vacuum Raptors — long, narrow, bright blue-white plume
        _shipEmitters.Add(BuildRingEmitter("Ship_Vac", baseY - 2.55f, ringRadius: vacR,
            amount: 120, lifetime: 1.4f,
            vMin: 18f, vMax: 45f,
            coreColor:  new Color(0.85f, 0.92f, 1.00f),
            trailColor: new Color(0.55f, 0.18f, 0.75f)));

        // Sea-level Raptors — wider, shorter, orange
        _shipEmitters.Add(BuildRingEmitter("Ship_SL", baseY - 1.75f, ringRadius: slR,
            amount: 100, lifetime: 0.8f,
            vMin: 15f, vMax: 38f,
            coreColor:  new Color(1.00f, 0.95f, 0.80f),
            trailColor: new Color(1.00f, 0.50f, 0.05f)));
    }

    // ── Per-frame update ──────────────────────────────────────────────────

    /// <summary>Update emitters from vessel state. Call in VesselRenderer._Process().</summary>
    public void Update(float throttle, bool shPresent, double altitude)
    {
        bool  firing  = throttle > 0.01f;
        float spread  = altitude < 25_000.0 ? 14f : 3f;   // wider at SL, narrow in vac
        float lifetime = altitude < 25_000.0 ? 1.0f : 2.5f;

        UpdateGroup(_shEmitters,   firing && shPresent,   spread, lifetime, throttle, altitude);
        UpdateGroup(_shipEmitters, firing && !shPresent, spread * 0.4f, lifetime, throttle, altitude);
    }

    private static void UpdateGroup(List<GpuParticles3D> emitters,
        bool emitting, float spread, float lifetimeScale, float throttle, double altitude)
    {
        // Below ~100m: particles blast outward/upward (deflected by launch mount)
        // Above ~500m: particles stream down in a tight exhaust plume
        float altT    = (float)System.Math.Clamp((altitude - 50.0) / 450.0, 0.0, 1.0);
        var   dirLow  = new Vector3(0,  0.60f, 0);   // upward burst at ground
        var   dirHigh = new Vector3(0, -1.00f, 0);   // downward plume in flight
        var   dir     = dirLow.Lerp(dirHigh, altT).Normalized();

        // Wider spread at low alt (ground fireball), narrower in vacuum
        float finalSpread = Mathf.Lerp(45f, spread, altT);

        foreach (var e in emitters)
        {
            e.Emitting = emitting;
            if (!emitting) continue;

            e.AmountRatio = throttle;

            if (e.ProcessMaterial is ParticleProcessMaterial pm)
            {
                pm.Direction = dir;
                pm.Spread    = finalSpread;
            }

            // Flicker: combustion turbulence
            e.SpeedScale = 0.85f + throttle * 0.3f + (float)GD.Randf() * 0.1f;
        }
    }

    // ── Factory helper ────────────────────────────────────────────────────

    // Soft circular gradient: white centre → transparent edge (makes particles look like glowing blobs)
    private static ImageTexture BuildSoftCircleTexture()
    {
        const int S = 64;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float r  = Mathf.Sqrt(dx * dx + dy * dy);
            // Smooth falloff: 1 at centre → 0 at edge
            float a  = Mathf.Clamp(1f - r * r, 0f, 1f);
            a = a * a; // quadratic falloff for softer edge
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // Cached so all emitters share the same texture object
    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();

    private GpuParticles3D BuildRingEmitter(string name, float yPos, float ringRadius,
        int amount, float lifetime, float vMin, float vMax,
        Color coreColor, Color trailColor)
    {
        // Color ramp: core → mid-trail → transparent tail
        var grad = new Gradient
        {
            Colors  = new[] { coreColor, trailColor, new Color(trailColor.R * 0.3f, 0f, 0f, 0f) },
            Offsets = new[] { 0f, 0.45f, 1.0f },
        };
        var gradTex = new GradientTexture1D { Gradient = grad };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius      = ringRadius,
            EmissionRingInnerRadius = Mathf.Max(0f, ringRadius - 0.08f),
            EmissionRingAxis        = Vector3.Up,
            EmissionRingHeight      = 0.02f,

            Direction          = new Vector3(0, -1, 0),
            Spread             = 14f,
            InitialVelocityMin = vMin,
            InitialVelocityMax = vMax,

            DampingMin = vMin * 0.15f,
            DampingMax = vMin * 0.25f,

            ScaleMin = 0.50f,
            ScaleMax = 1.80f,

            ColorRamp = gradTex,
        };

        // Particle mesh: soft-circle billboard for smooth glowing blob look
        var quad = new QuadMesh { Size = new Vector2(1.2f, 1.2f) };
        var drawMat = new StandardMaterial3D
        {
            BillboardMode            = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoTexture            = SoftCircle,   // radial fade → no hard square edges
            AlbedoColor              = Colors.White,
            EmissionEnabled          = true,
            EmissionEnergyMultiplier = 2.8f,
            VertexColorUseAsAlbedo   = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        var emitter = new GpuParticles3D
        {
            Name             = name,
            Position         = new Vector3(0, yPos, 0),
            Amount           = amount,
            Lifetime         = lifetime,
            ProcessMaterial  = pm,
            DrawPass1        = quad,
            Emitting         = false,
            LocalCoords      = true,
            OneShot          = false,
            Preprocess       = 0.3f,
            SpeedScale       = 1.0f,
            // AABB covers upward ground blast (+140 up) and downward vacuum plume (-340 down)
            VisibilityAabb   = new Aabb(new Vector3(-20f, -340f, -20f), new Vector3(40f, 480f, 40f)),
        };
        AddChild(emitter);
        return emitter;
    }

}
