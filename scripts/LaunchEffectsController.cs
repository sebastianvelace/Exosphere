namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation.Math;

/// <summary>
/// Ground-interaction launch VFX: the iconic Super Heavy deluge cloud.
/// When the booster ignites on the pad, a massive billowing wall of white-grey
/// steam/smoke (water-deluge vaporisation + exhaust) erupts at y≈0 and rolls
/// OUTWARD horizontally, then boils upward — accompanied by darker dust kicked
/// up low and outward.
///
/// This is purely the ground cloud; the engine flame itself is owned by
/// <c>PlumeSystem</c>. We anchor the cloud to the ground point directly under
/// the vessel. Because the active vessel sits at the render origin and the
/// floating-origin scheme keeps it there, the ground recedes downward as the
/// rocket climbs: we place the emitters at <c>-up * (altitude / MetresPerUnit)</c>
/// so the cloud is "left behind" on the pad while the booster ascends away.
///
/// Self-wiring: drop in as a child of the World Node3D. It finds the vessel and
/// the dominant body each frame through <see cref="SimulationBridge"/>,
/// null-guarding everything, and only toggles <c>Emitting</c> / transform per
/// frame — all heavy objects are built once in <see cref="_Ready"/>.
/// </summary>
public partial class LaunchEffectsController : Node3D
{
    // ── Tuning ───────────────────────────────────────────────────────────────
    // Render scale: 1 unit ≈ 2.8 m. The whole cloud is sized in render units.
    private const float MetresPerUnit = 2.8f;

    // Altitude band (metres) over which the cloud is active and fades out.
    private const float TriggerCeilingM = 600f;   // above this: fully off
    private const float FullIntensityM  = 120f;   // at/under this: full force
    private const float MinThrottle     = 0.02f;  // throttle floor to count as "lit"

    // Pivot we rotate to align local +Y with the planet's "up" at the vessel,
    // and translate down to the receding ground point.
    private Node3D _pivot = null!;

    // The three layers of the deluge cloud.
    private GpuParticles3D _steamCore   = null!;  // dense, fast outward billow
    private GpuParticles3D _steamBoil   = null!;  // taller lingering boil-up
    private GpuParticles3D _dust        = null!;  // low dark debris/dust

    // Shared soft-round billboard texture for all layers.
    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();

    // Smoothed intensity so ignition/cutoff ramps instead of popping.
    private float _intensity;

    // Cached Earth body id check result is cheap; we just re-resolve each frame.

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "DelugePivot" };
        AddChild(_pivot);

        _steamCore = BuildSteamCore();
        _steamBoil = BuildSteamBoil();
        _dust      = BuildDust();

        _pivot.AddChild(_dust);       // add dust first so it sits under the steam
        _pivot.AddChild(_steamBoil);
        _pivot.AddChild(_steamCore);

        SetEmitting(false);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null)
        {
            FadeOut(delta);
            return;
        }

        // Dominant body must be Earth for a pad launch.
        var body = bridge.Universe?.GetDominantBody(vessel.Position);
        if (body == null || body.Id != "earth")
        {
            FadeOut(delta);
            return;
        }

        // Engines must be lit (firing engines present) AND throttle open.
        bool lit = vessel.Throttle > MinThrottle && vessel.Parts.ActiveEngines.Any();

        double altitude = vessel.GetAltitude(body); // metres
        bool onPad = altitude < TriggerCeilingM;

        // Target intensity: full near the deck, easing to zero by the ceiling.
        float target = 0f;
        if (lit && onPad)
        {
            float t = ((float)altitude - FullIntensityM) /
                      (TriggerCeilingM - FullIntensityM);
            target = 1f - Mathf.Clamp(t, 0f, 1f); // 1 at/under FullIntensityM → 0 at ceiling
        }

        // Asymmetric smoothing: erupt fast at ignition, linger/fade slower so the
        // cloud reads as a self-sustaining body of vapour, not a switch.
        float rate = target > _intensity ? 8f : 1.6f;
        _intensity = Mathf.Lerp(_intensity, target, Mathf.Clamp((float)delta * rate, 0f, 1f));

        if (_intensity < 0.01f)
        {
            if (Visible) SetEmitting(false);
            Visible = false;
            return;
        }

        Visible = true;

        // ── Anchor to the receding ground point under the vessel ──────────────
        // Up direction = radial from planet centre to vessel, in render space the
        // floating origin keeps the vessel at (0,0,0), so the ground sits at
        // -up * altitudeUnits below us.
        Vector3 up = ToGodot((vessel.Position - body.Position).Normalized);
        if (up.LengthSquared() < 1e-6f) up = Vector3.Up;

        float altUnits = (float)(altitude / MetresPerUnit);
        _pivot.Position = -up * altUnits;

        // Orient pivot so its local +Y aligns with planet up (cloud rolls "out"
        // in the local XZ plane and boils up along +Y).
        AlignUp(_pivot, up);

        // ── Drive the layers ──────────────────────────────────────────────────
        SetEmitting(true);
        DriveAmounts(_intensity);
    }

    // ── Per-frame intensity → emission amount (no per-frame allocations) ──────
    private void DriveAmounts(float k)
    {
        // Amounts are pre-budgeted maxima; scale the *fraction* emitted via the
        // process material's emission interpolation by toggling Amount only when
        // it changes meaningfully. GPUParticles3D re-allocates on Amount change,
        // so we quantise to a few discrete steps to avoid churn.
        int coreMax = 160, boilMax = 90, dustMax = 70;

        int core = QuantiseAmount(coreMax, k);
        int boil = QuantiseAmount(boilMax, k);
        int dust = QuantiseAmount(dustMax, k);

        if (_steamCore.Amount != core) _steamCore.Amount = core;
        if (_steamBoil.Amount != boil) _steamBoil.Amount = boil;
        if (_dust.Amount     != dust) _dust.Amount       = dust;
    }

    // Quantise to 1/4 steps so Amount only changes a handful of times.
    private static int QuantiseAmount(int max, float k)
    {
        int steps = 4;
        int q = Mathf.RoundToInt(k * steps);
        int amt = max * q / steps;
        return Mathf.Max(1, amt);
    }

    private void SetEmitting(bool on)
    {
        _steamCore.Emitting = on;
        _steamBoil.Emitting = on;
        _dust.Emitting      = on;
    }

    private void FadeOut(double delta)
    {
        if (_intensity <= 0f)
        {
            if (Visible) { SetEmitting(false); Visible = false; }
            return;
        }
        _intensity = Mathf.Lerp(_intensity, 0f, Mathf.Clamp((float)delta * 1.6f, 0f, 1f));
        if (_intensity < 0.01f)
        {
            _intensity = 0f;
            SetEmitting(false);
            Visible = false;
        }
    }

    // ── Layer builders (called once) ─────────────────────────────────────────

    /// <summary>
    /// Dense, fast deluge billow: a wide ring of vapour shot OUTWARD low to the
    /// ground with strong damping so it spreads sideways and rolls up. Big soft
    /// white billboards that grow over their lifetime — the iconic cloud body.
    /// </summary>
    private GpuParticles3D BuildSteamCore()
    {
        // White-grey steam ramp: bright vapour → cooler grey → soft fade.
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(1.00f, 1.00f, 1.00f, 0.00f), // born transparent (eases the spawn pop)
                new Color(0.97f, 0.97f, 0.99f, 0.85f), // bright steam
                new Color(0.82f, 0.83f, 0.86f, 0.70f), // cooling grey
                new Color(0.70f, 0.71f, 0.75f, 0.00f), // dissipate
            },
            Offsets = new[] { 0f, 0.12f, 0.6f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            // Emit from a broad ground ring around the pad base.
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 3.0f,   // ~8.4 m radius ring
            EmissionRingInnerRadius = 0.4f,
            EmissionRingHeight      = 0.6f,

            // Fire mostly OUTWARD & slightly up; wide spread so it fans across pad.
            Direction          = new Vector3(0f, 0.35f, 1f).Normalized(),
            Spread             = 75f,
            Flatness           = 0.65f,        // bias toward horizontal sheeting
            InitialVelocityMin = 14f,
            InitialVelocityMax = 30f,

            // Heavy damping so it decelerates and balloons rather than streaking.
            DampingMin = 6f,
            DampingMax = 12f,

            // Gentle buoyancy: once it slows, it boils upward.
            Gravity = new Vector3(0f, 2.0f, 0f),

            // Turbulent, slow drift for that churning volume.
            TurbulenceEnabled               = true,
            TurbulenceNoiseStrength         = 2.2f,
            TurbulenceNoiseScale            = 1.4f,
            TurbulenceInfluenceMin          = 0.1f,
            TurbulenceInfluenceMax          = 0.5f,

            AngularVelocityMin = -40f,
            AngularVelocityMax = 40f,

            // Big and growing — voluminous billows.
            ScaleMin = 2.2f,
            ScaleMax = 4.2f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.4f, 1.0f); // grow over lifetime

        var quad = new QuadMesh { Size = new Vector2(3.6f, 3.6f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 1.25f));

        return new GpuParticles3D
        {
            Name            = "DelugeSteamCore",
            Amount          = 160,
            Lifetime        = 4.5f,
            Preprocess      = 0.5f,           // start mid-billow so ignition isn't empty
            Explosiveness   = 0.06f,
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,          // world coords: cloud stays put as pivot recedes
            VisibilityAabb  = new Aabb(new Vector3(-120f, -8f, -120f), new Vector3(240f, 160f, 240f)),
        };
    }

    /// <summary>
    /// Taller, slower boil-up column behind the core — fills in the vertical
    /// mushrooming as the cloud climbs. Larger, dimmer, longer-lived puffs.
    /// </summary>
    private GpuParticles3D BuildSteamBoil()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.95f, 0.95f, 0.98f, 0.00f),
                new Color(0.90f, 0.91f, 0.94f, 0.60f),
                new Color(0.74f, 0.75f, 0.79f, 0.45f),
                new Color(0.62f, 0.63f, 0.68f, 0.00f),
            },
            Offsets = new[] { 0f, 0.18f, 0.65f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 6.0f,
            EmissionRingInnerRadius = 1.5f,
            EmissionRingHeight      = 1.0f,

            Direction          = new Vector3(0f, 1f, 0.4f).Normalized(),
            Spread             = 55f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 16f,

            DampingMin = 3f,
            DampingMax = 7f,

            Gravity = new Vector3(0f, 4.5f, 0f), // stronger buoyant rise

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 2.8f,
            TurbulenceNoiseScale    = 1.1f,
            TurbulenceInfluenceMin  = 0.15f,
            TurbulenceInfluenceMax  = 0.55f,

            AngularVelocityMin = -25f,
            AngularVelocityMax = 25f,

            ScaleMin = 3.0f,
            ScaleMax = 6.0f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.5f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(5.0f, 5.0f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 1.0f));

        return new GpuParticles3D
        {
            Name            = "DelugeSteamBoil",
            Amount          = 90,
            Lifetime        = 6.5f,
            Preprocess      = 1.0f,
            Explosiveness   = 0.0f,
            Randomness      = 0.6f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,
            VisibilityAabb  = new Aabb(new Vector3(-140f, -8f, -140f), new Vector3(280f, 220f, 280f)),
        };
    }

    /// <summary>
    /// Low, dark dust/debris kicked outward across the deck. Smaller, faster,
    /// hugs the ground, browner and more opaque than steam.
    /// </summary>
    private GpuParticles3D BuildDust()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.42f, 0.38f, 0.33f, 0.00f),
                new Color(0.40f, 0.36f, 0.31f, 0.65f), // dark dust
                new Color(0.50f, 0.47f, 0.43f, 0.40f), // lightening as it spreads
                new Color(0.55f, 0.53f, 0.50f, 0.00f),
            },
            Offsets = new[] { 0f, 0.15f, 0.6f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 2.0f,
            EmissionRingInnerRadius = 0.2f,
            EmissionRingHeight      = 0.2f,

            // Almost flat — blasts sideways across the pad.
            Direction          = new Vector3(0f, 0.08f, 1f).Normalized(),
            Spread             = 85f,
            Flatness           = 0.9f,
            InitialVelocityMin = 18f,
            InitialVelocityMax = 36f,

            DampingMin = 8f,
            DampingMax = 16f,

            Gravity = new Vector3(0f, -1.0f, 0f), // dust settles, doesn't rise

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.4f,
            TurbulenceNoiseScale    = 1.6f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.3f,

            ScaleMin = 1.2f,
            ScaleMax = 2.6f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.6f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(2.4f, 2.4f) };
        // Dust is lit-ish but still unshaded soft; alpha blend (not additive) so
        // it reads as dark, occluding debris rather than glowing vapour.
        var drawMat = new StandardMaterial3D
        {
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode          = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture          = SoftCircle,
            AlbedoColor            = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = "DelugeDust",
            Amount          = 70,
            Lifetime        = 3.5f,
            Preprocess      = 0.3f,
            Explosiveness   = 0.1f,
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,
            VisibilityAabb  = new Aabb(new Vector3(-120f, -8f, -10f), new Vector3(240f, 40f, 240f)),
        };
    }

    // ── Shared material / texture helpers ────────────────────────────────────

    /// <summary>
    /// Soft, slightly self-illuminated steam billboard. Additive so the cloud
    /// glows softly against the daylit pad while staying voluminous (the alpha
    /// ramp keeps cores soft, not hot).
    /// </summary>
    private static StandardMaterial3D SteamDrawMaterial(float energy)
    {
        return new StandardMaterial3D
        {
            BillboardMode            = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Soft additive keeps overlapping puffs from hard-edging while reading
            // bright against daylight; alpha ramp prevents blowout to pure white.
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode            = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture            = SoftCircle,
            AlbedoColor              = Colors.White,
            EmissionEnabled          = true,
            EmissionEnergyMultiplier = energy,
            VertexColorUseAsAlbedo   = true,
        };
    }

    /// <summary>Sets a scale-over-lifetime curve so billboards grow as they age.</summary>
    private static void SetGrowCurve(ParticleProcessMaterial pm, float start, float end)
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, start));
        curve.AddPoint(new Vector2(1f, end));
        pm.ScaleCurve = new CurveTexture { Curve = curve };
    }

    /// <summary>
    /// Procedural soft round billboard (radial alpha falloff). Shared by all
    /// layers; built once and cached statically.
    /// </summary>
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
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp(1f - r, 0f, 1f);
            a = a * a; // soft shoulder
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ── Math helpers ─────────────────────────────────────────────────────────

    private static Vector3 ToGodot(Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// Rotate <paramref name="node"/> so its local +Y axis points along
    /// <paramref name="up"/>, with a stable arbitrary roll.
    /// </summary>
    private static void AlignUp(Node3D node, Vector3 up)
    {
        up = up.Normalized();
        // Pick a reference not parallel to up to build an orthonormal basis.
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        var basis = new Basis(x, up, z);
        node.Transform = new Transform3D(basis, node.Position);
    }
}
