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
    private const float TriggerCeilingM = 1000f;  // above this: fully off
    private const float FullIntensityM  = 150f;   // at/under this: full force
    private const float MinThrottle     = 0.02f;  // throttle floor to count as "lit"

    // Pivot we rotate to align local +Y with the planet's "up" at the vessel,
    // and translate down to the receding ground point.
    private Node3D _pivot = null!;

    // The four layers of the deluge cloud.
    private GpuParticles3D _steamCore   = null!;  // dense, fast outward billow
    private GpuParticles3D _steamBoil   = null!;  // taller lingering boil-up
    private GpuParticles3D _dust        = null!;  // low dark debris/dust
    private GpuParticles3D _haze        = null!;  // faint lingering ground dust haze

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
        _haze      = BuildHaze();

        _pivot.AddChild(_haze);       // faint ground haze underneath everything
        _pivot.AddChild(_dust);       // dust sits under the steam
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
        int coreMax = 200, boilMax = 130, dustMax = 90, hazeMax = 60;

        int core = QuantiseAmount(coreMax, k);
        int boil = QuantiseAmount(boilMax, k);
        int dust = QuantiseAmount(dustMax, k);
        int haze = QuantiseAmount(hazeMax, k);

        if (_steamCore.Amount != core) _steamCore.Amount = core;
        if (_steamBoil.Amount != boil) _steamBoil.Amount = boil;
        if (_dust.Amount     != dust) _dust.Amount       = dust;
        if (_haze.Amount     != haze) _haze.Amount       = haze;
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
        _haze.Emitting      = on;
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
            EmissionRingRadius      = 6.0f,   // ~17 m radius ring
            EmissionRingInnerRadius = 0.8f,
            EmissionRingHeight      = 1.0f,

            // Fire mostly OUTWARD & slightly up; wide spread so it fans across pad.
            // Lower the vertical bias so the FIRST motion is a ground-hugging surge.
            Direction          = new Vector3(0f, 0.18f, 1f).Normalized(),
            Spread             = 82f,
            Flatness           = 0.8f,         // strong bias toward horizontal sheeting
            InitialVelocityMin = 26f,
            InitialVelocityMax = 52f,

            // Heavy damping so it decelerates and balloons rather than streaking.
            DampingMin = 5f,
            DampingMax = 11f,

            // Buoyancy: once the outward surge slows, it mushrooms upward.
            Gravity = new Vector3(0f, 3.0f, 0f),

            // Turbulent, slow drift for that churning volume.
            TurbulenceEnabled               = true,
            TurbulenceNoiseStrength         = 3.0f,
            TurbulenceNoiseScale            = 1.2f,
            TurbulenceInfluenceMin          = 0.12f,
            TurbulenceInfluenceMax          = 0.55f,

            AngularVelocityMin = -45f,
            AngularVelocityMax = 45f,

            // Big and growing — voluminous billows.
            ScaleMin = 4.5f,
            ScaleMax = 8.5f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.35f, 1.0f); // grow strongly over lifetime

        var quad = new QuadMesh { Size = new Vector2(5.0f, 5.0f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 1.15f));

        return new GpuParticles3D
        {
            Name            = "DelugeSteamCore",
            Amount          = 200,
            Lifetime        = 6.0f,
            Preprocess      = 0.6f,           // start mid-billow so ignition isn't empty
            Explosiveness   = 0.08f,
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,          // world coords: cloud stays put as pivot recedes
            VisibilityAabb  = new Aabb(new Vector3(-260f, -12f, -260f), new Vector3(520f, 320f, 520f)),
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
            EmissionRingRadius      = 11.0f,
            EmissionRingInnerRadius = 2.5f,
            EmissionRingHeight      = 1.5f,

            Direction          = new Vector3(0f, 1f, 0.45f).Normalized(),
            Spread             = 58f,
            InitialVelocityMin = 9f,
            InitialVelocityMax = 22f,

            DampingMin = 2.5f,
            DampingMax = 6f,

            Gravity = new Vector3(0f, 6.5f, 0f), // strong buoyant rise → tall wall

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 3.4f,
            TurbulenceNoiseScale    = 0.9f,
            TurbulenceInfluenceMin  = 0.18f,
            TurbulenceInfluenceMax  = 0.6f,

            AngularVelocityMin = -28f,
            AngularVelocityMax = 28f,

            ScaleMin = 6.0f,
            ScaleMax = 11.0f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.45f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(7.0f, 7.0f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 0.9f));

        return new GpuParticles3D
        {
            Name            = "DelugeSteamBoil",
            Amount          = 130,
            Lifetime        = 9.0f,
            Preprocess      = 1.2f,
            Explosiveness   = 0.0f,
            Randomness      = 0.6f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,
            VisibilityAabb  = new Aabb(new Vector3(-320f, -12f, -320f), new Vector3(640f, 480f, 640f)),
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
            EmissionRingRadius      = 4.0f,
            EmissionRingInnerRadius = 0.4f,
            EmissionRingHeight      = 0.3f,

            // Almost flat — blasts sideways across the pad, fast and far.
            Direction          = new Vector3(0f, 0.06f, 1f).Normalized(),
            Spread             = 88f,
            Flatness           = 0.95f,
            InitialVelocityMin = 30f,
            InitialVelocityMax = 60f,

            DampingMin = 7f,
            DampingMax = 15f,

            Gravity = new Vector3(0f, -1.0f, 0f), // dust settles, doesn't rise

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.6f,
            TurbulenceNoiseScale    = 1.4f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.32f,

            ScaleMin = 2.2f,
            ScaleMax = 4.5f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.55f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(4.0f, 4.0f) };
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
            Amount          = 90,
            Lifetime        = 4.5f,
            Preprocess      = 0.3f,
            Explosiveness   = 0.12f,
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,
            VisibilityAabb  = new Aabb(new Vector3(-280f, -10f, -280f), new Vector3(560f, 60f, 560f)),
        };
    }

    /// <summary>
    /// Faint, very slow-drifting ground dust haze that lingers across the deck
    /// after the initial blast — large, near-transparent low puffs that read as a
    /// settling pall of vapour/dust hanging over the pad. Cheap and long-lived.
    /// </summary>
    private GpuParticles3D BuildHaze()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.80f, 0.80f, 0.82f, 0.00f),
                new Color(0.78f, 0.78f, 0.80f, 0.22f), // faint pale haze
                new Color(0.72f, 0.72f, 0.75f, 0.16f),
                new Color(0.68f, 0.68f, 0.71f, 0.00f),
            },
            Offsets = new[] { 0f, 0.2f, 0.7f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 14.0f,
            EmissionRingInnerRadius = 2.0f,
            EmissionRingHeight      = 0.5f,

            // Slow, near-flat outward creep — the haze just hangs and spreads.
            Direction          = new Vector3(0f, 0.05f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.92f,
            InitialVelocityMin = 3f,
            InitialVelocityMax = 9f,

            DampingMin = 1.5f,
            DampingMax = 4f,

            Gravity = new Vector3(0f, 0.4f, 0f), // barely rises

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.2f,
            TurbulenceNoiseScale    = 0.7f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.25f,

            ScaleMin = 8.0f,
            ScaleMax = 14.0f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.6f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(9.0f, 9.0f) };
        // Soft alpha-blended haze — not additive, so it reads as a dim pall.
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
            Name            = "DelugeHaze",
            Amount          = 60,
            Lifetime        = 12.0f,
            Preprocess      = 2.0f,           // start with haze already settled
            Explosiveness   = 0.0f,
            Randomness      = 0.7f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = false,
            VisibilityAabb  = new Aabb(new Vector3(-360f, -10f, -360f), new Vector3(720f, 120f, 720f)),
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
