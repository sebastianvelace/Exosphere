namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Engine-plume VFX for Starship + Super Heavy Raptor engines.
///
/// Each engine ring is rendered as a layered plume:
///   • a shader-driven emissive cone (<see cref="assets/shaders/raptor_plume.gdshader"/>)
///     that paints the bright blue-white supersonic core, periodic Mach (shock)
///     diamonds, and a translucent incandescent outer sheath;
///   • a small GPU-particle emitter for turbulent soot/smoke that breaks up the
///     silhouette and reacts to the ground at liftoff;
///   • an <see cref="OmniLight3D"/> at the nozzle so the plume illuminates the
///     pad / vehicle on launch.
///
/// The plume EXPANDS in vacuum (longer, broader, sparser diamonds) and is tight
/// with closely-spaced diamonds at sea level, driven from vessel altitude.
///
/// Public surface used by VesselRenderer is preserved exactly:
///   SetupSH(float, float, float, float), SetupStarship(float, float, float),
///   Update(float, bool, double).
/// </summary>
public partial class PlumeSystem : Node3D
{
    // One render unit ≈ 2.8 metres in this project. Plume sizing below is in
    // render units, scaled relative to the engine bell radii VesselRenderer passes.

    /// <summary>A single engine ring's visual stack (cone + particles + light).</summary>
    private sealed class PlumeUnit
    {
        public Node3D           Pivot    = null!;   // anchored at the nozzle; scaled to stretch the plume
        public MeshInstance3D   Cone     = null!;   // shader-driven core + diamonds + sheath
        public ShaderMaterial   ConeMat  = null!;
        public GpuParticles3D   Smoke    = null!;   // turbulent soot/smoke
        public OmniLight3D?     Light;              // ground illumination (group leaders only)

        public float BaseLength;                    // sea-level plume length (render units)
        public float BaseRadius;                    // plume mouth radius at the nozzle
        public bool  IsSuperHeavy;
    }

    private readonly List<PlumeUnit> _shUnits   = new();
    private readonly List<PlumeUnit> _shipUnits = new();

    private static Shader? _plumeShader;
    private static Shader PlumeShader =>
        _plumeShader ??= GD.Load<Shader>("res://assets/shaders/raptor_plume.gdshader");

    // ── Setup calls (called once from VesselRenderer after building geometry) ─

    /// <summary>Sets up the 33-engine Super Heavy plume (3 concentric rings + core).</summary>
    public void SetupSH(float innerR, float midR, float outerR, float bellY)
    {
        // 33 Raptors firing together merge into one enormous incandescent plume.
        // We model it as a bright merged central column plus three concentric
        // rings; the column dominates and the rings broaden the silhouette. Base
        // lengths are generous — at sea level this is a short, fat, blinding flame;
        // altitude then stretches it far longer (see Update()).

        // Bright central column (the merged plume core of the densely-packed cluster).
        // Real Super Heavy liftoff is a SHORT but very WIDE, blinding flame disk — the 33
        // engines merge into one incandescent column. Fatter mouth + a touch more length.
        _shUnits.Add(BuildUnit("SH_Core", bellY, mouthR: 0.95f,
            length: 15.0f, count: 33,
            core: new Color(0.85f, 0.92f, 1.00f), withLight: true, sh: true));

        // Inner ring — 3 engines.
        _shUnits.Add(BuildUnit("SH_Inner", bellY, mouthR: innerR + 0.34f,
            length: 12.5f, count: 3,
            core: new Color(0.80f, 0.89f, 1.00f), withLight: false, sh: true));

        // Mid ring — 10 engines.
        _shUnits.Add(BuildUnit("SH_Mid", bellY + 0.05f, mouthR: midR + 0.42f,
            length: 11.7f, count: 10,
            core: new Color(0.78f, 0.88f, 1.00f), withLight: false, sh: true));

        // Outer ring — 20 engines, broadest cluster.
        _shUnits.Add(BuildUnit("SH_Outer", bellY + 0.10f, mouthR: outerR + 0.50f,
            length: 11.0f, count: 20,
            core: new Color(0.76f, 0.87f, 1.00f), withLight: true, sh: true));
    }

    /// <summary>Sets up the Starship plume (3 vacuum + 3 sea-level Raptors).</summary>
    public void SetupStarship(float vacR, float slR, float baseY)
    {
        // Vacuum Raptors — long, narrow, bright blue-white plume (big bell, sits lower).
        _shipUnits.Add(BuildUnit("Ship_Vac", baseY - 1.05f, mouthR: vacR + 0.26f,
            length: 11.0f, count: 3,
            core: new Color(0.66f, 0.80f, 1.00f), withLight: true, sh: false));

        // Sea-level Raptors — tighter, shorter plume (shorter bell, sits a touch higher).
        _shipUnits.Add(BuildUnit("Ship_SL", baseY - 0.70f, mouthR: slR + 0.20f,
            length: 8.5f, count: 3,
            core: new Color(0.74f, 0.86f, 1.00f), withLight: true, sh: false));
    }

    // ── Per-frame update ──────────────────────────────────────────────────

    /// <summary>Update emitters from vessel state. Call in VesselRenderer._Process().</summary>
    public void Update(float throttle, bool shPresent, double altitude)
    {
        bool firing = throttle > 0.01f;

        // Ambient-pressure proxy → expansion factor. Modelled on an exponential
        // atmosphere (~7 km scale height): the plume already starts visibly
        // expanding in the first kilometres and is fully underexpanded (broad,
        // very long, sparse diamonds) well before the Kármán line. At p≈p0 the
        // plume is tight/over-expanded; as p→0 it becomes the long vacuum plume.
        float pressRatio = (float)System.Math.Exp(-altitude / 7_000.0); // 1 at SL → 0 high up
        float expansion  = System.Math.Clamp(1f - pressRatio, 0f, 1f);
        expansion = expansion * expansion * (3f - 2f * expansion); // smoothstep

        UpdateGroup(_shUnits,   firing && shPresent,  throttle, expansion, altitude);
        UpdateGroup(_shipUnits, firing && !shPresent, throttle, expansion, altitude);
    }

    private static void UpdateGroup(List<PlumeUnit> units,
        bool firing, float throttle, float expansion, double altitude)
    {
        // Near the pad (<~100 m) exhaust is deflected up/outward by the mount;
        // higher up it streams straight down. Drives smoke direction only —
        // the bright core always points down the nozzle axis.
        float altT = (float)System.Math.Clamp((altitude - 50.0) / 450.0, 0.0, 1.0);
        var   dir  = new Vector3(0, 0.55f, 0).Lerp(new Vector3(0, -1f, 0), altT).Normalized();
        float smokeSpread = Mathf.Lerp(58f, 10f + expansion * 6f, altT);

        // Live flicker shared per group so the whole cluster pulses together.
        float flick = 0.92f + GD.Randf() * 0.10f;

        // N7: atmospheric-pressure proxy for the new shader uniforms.
        // atmo_pressure = exp(-alt/7000) already computed as (1 - expansion) before
        // the smoothstep, but we keep the simpler inverse relationship here.
        float atmoPressure = System.Math.Clamp(1f - expansion, 0f, 1f);

        foreach (var u in units)
        {
            // ── Shader-driven core cone ──────────────────────────────────────
            u.Pivot.Visible = firing;
            if (firing)
            {
                u.ConeMat.SetShaderParameter("throttle",       throttle);
                u.ConeMat.SetShaderParameter("expansion",      expansion);
                // N7: new uniforms — atmo_pressure and throttle_level.
                // The shader reconciles both control paths, so writing both is safe
                // and ensures the N7 plume_length / diamond / opacity logic fires.
                u.ConeMat.SetShaderParameter("atmo_pressure",  atmoPressure);
                u.ConeMat.SetShaderParameter("throttle_level", throttle);

                // Length grows with throttle and (strongly) with altitude;
                // mouth broadens in vacuum (underexpanded). Flicker jitters length.
                // The cone mesh is unit height (1.0) and unit-ish radius (0.5),
                // anchored at the nozzle via the pivot, so scaling the pivot's
                // Y stretches the plume downward while the mouth stays put.
                //
                // SL→vacuum: at sea level the plume is short & fat; in vacuum it
                // lengthens dramatically (up to ~4x) and widens (up to ~2.3x) into
                // the long faint underexpanded plume.
                float lenScale = (0.55f + 0.45f * throttle)
                               * (1.0f + expansion * 3.0f) * flick;
                float seaLevelBroadening = u.IsSuperHeavy ? 1.18f : 1.0f;
                float radScale = (0.85f + 0.30f * throttle)
                               * (1.0f + expansion * 1.3f)
                               * Mathf.Lerp(seaLevelBroadening, 1.0f, expansion);
                u.Pivot.Scale = new Vector3(
                    (u.BaseRadius / 0.5f) * radScale,
                    u.BaseLength * lenScale,
                    (u.BaseRadius / 0.5f) * radScale);
            }

            // ── Turbulent smoke particles ────────────────────────────────────
            // Methalox vacuum exhaust is optically thin: keep soot/smoke as a
            // low-atmosphere breakup layer, then hand the look over to the shader
            // core once expansion is high. This keeps orbit burns blue/clean
            // instead of pad-smoky.
            float smokePresence = Mathf.Clamp(1f - expansion * (u.IsSuperHeavy ? 0.92f : 1.12f), 0f, 1f);
            float smokeAmount = throttle * smokePresence * smokePresence;
            u.Smoke.Emitting = firing && smokeAmount > 0.02f;
            if (firing)
            {
                u.Smoke.AmountRatio = Mathf.Clamp(smokeAmount, 0.0f, 1f);
                u.Smoke.SpeedScale  = 0.70f + throttle * 0.28f + smokePresence * 0.25f + GD.Randf() * 0.08f;
                if (u.Smoke.ProcessMaterial is ParticleProcessMaterial pm)
                {
                    pm.Direction = dir;
                    pm.Spread    = smokeSpread;
                    // Big, billowing soot near the pad; smoke thins out in vacuum
                    // (no air to billow into) leaving just the bright core.
                    pm.ScaleMin  = (u.IsSuperHeavy ? 1.8f : 1.0f) * Mathf.Lerp(0.06f, 1.0f, smokePresence);
                    pm.ScaleMax  = (u.IsSuperHeavy ? 5.4f : 3.0f) * Mathf.Lerp(0.10f, 1.0f, smokePresence);
                }
            }

            // ── Nozzle glow light ────────────────────────────────────────────
            if (u.Light != null)
            {
                u.Light.Visible = firing;
                if (firing)
                {
                    // Strong at the pad for ground illumination, eases off with
                    // altitude (nothing to light up in vacuum), flickers alive.
                    float groundBoost = 1f - altT * 0.45f;
                    u.Light.LightEnergy = (u.IsSuperHeavy ? 9.0f : 5.0f)
                                        * throttle * groundBoost * flick;
                    u.Light.OmniRange   = u.BaseLength
                                        * (u.IsSuperHeavy ? 1.6f : 1.3f)
                                        * (0.7f + throttle * 0.5f);
                }
            }
        }
    }

    // ── Factory helpers ────────────────────────────────────────────────────

    private PlumeUnit BuildUnit(string name, float yPos, float mouthR,
        float length, int count, Color core, bool withLight, bool sh)
    {
        var unit = new PlumeUnit
        {
            BaseLength   = length,
            BaseRadius   = mouthR,
            IsSuperHeavy = sh,
        };

        // ── Shader cone ──────────────────────────────────────────────────────
        // A pivot Node3D is placed at the nozzle (yPos). The cone mesh is its
        // child, offset DOWN by half its unit height so the wide mouth sits at
        // the pivot origin. Scaling the pivot then stretches the plume downward
        // while the mouth stays anchored at the nozzle across all throttles.
        //
        // Cone mesh: unit height (1.0), mouth radius 0.5 toward +Y (nozzle),
        // tapering to a fine tip at -Y. Authored at fixed dims so the shader's
        // axial/radial UVs stay scale-independent.
        var coneMesh = new CylinderMesh
        {
            TopRadius      = 0.5f,
            BottomRadius   = 0.05f,
            Height         = 1.0f,
            RadialSegments = 20,
            Rings          = 24,
            CapTop         = false,
            CapBottom      = false,
        };

        var mat = new ShaderMaterial { Shader = PlumeShader };
        mat.SetShaderParameter("core_color",     core);
        mat.SetShaderParameter("edge_color",    new Color(1.0f, 0.45f, 0.12f));
        mat.SetShaderParameter("diamond_count", sh ? 8.0f : 9.0f);
        // Master brightness — the merged 33-engine column reads brighter against the daytime
        // sky than a single ring; bumped so liftoff/ascent exhaust actually glows.
        mat.SetShaderParameter("energy",        sh ? 4.6f : 4.0f);
        mat.SetShaderParameter("throttle",      0f);
        mat.SetShaderParameter("expansion",     0f);
        // N7: initialize the new atmospheric-pressure uniforms.
        mat.SetShaderParameter("atmo_pressure",  1f);  // sea level at start
        mat.SetShaderParameter("throttle_level", 0f);  // engines off at start

        var pivot = new Node3D
        {
            Name     = name + "_Pivot",
            Position = new Vector3(0, yPos, 0),
            Visible  = false,
        };
        AddChild(pivot);

        var cone = new MeshInstance3D
        {
            Name             = name + "_Cone",
            Mesh             = coneMesh,
            // Mesh is centred on its own origin; shift down by 0.5 so the +Y mouth
            // lands exactly at the pivot origin (the nozzle).
            Position         = new Vector3(0, -0.5f, 0),
            MaterialOverride = mat,
            CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off,
            // Generous AABB so the long vacuum plume isn't culled when off-screen.
            CustomAabb       = new Aabb(new Vector3(-1.5f, -3.5f, -1.5f),
                                        new Vector3(3f, 4f, 3f)),
        };
        pivot.AddChild(cone);
        unit.Pivot   = pivot;
        unit.Cone    = cone;
        unit.ConeMat = mat;

        // ── Turbulent smoke / soot particles ─────────────────────────────────
        unit.Smoke = BuildSmoke(name + "_Smoke", yPos, mouthR, count, sh);
        AddChild(unit.Smoke);

        // ── Nozzle glow light ────────────────────────────────────────────────
        if (withLight)
        {
            var light = new OmniLight3D
            {
                Name             = name + "_Glow",
                Position         = new Vector3(0, yPos - 0.3f, 0),
                LightColor       = new Color(0.85f, 0.92f, 1.0f),
                OmniRange        = length,
                LightEnergy      = 0f,
                ShadowEnabled    = false,
                Visible          = false,
                LightSpecular    = 0.4f,
            };
            AddChild(light);
            unit.Light = light;
        }

        return unit;
    }

    // Soft circular gradient: white centre → transparent edge (smoke puffs).
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
            float a  = Mathf.Clamp(1f - r * r, 0f, 1f);
            a = a * a;
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();

    private GpuParticles3D BuildSmoke(string name, float yPos, float mouthR,
        int engineCount, bool sh)
    {
        // Particle budget kept low: total stays in the low hundreds across all
        // rings. Soot/smoke is a translucent supporting layer, not the main show.
        int amount = sh
            ? Mathf.Clamp(58 + engineCount * 5, 80, 180)
            : Mathf.Clamp(24 + engineCount * 3, 24, 90);

        // Edge soot colour ramp: faint incandescent orange → dark smoke → fade.
        var grad = new Gradient
        {
            Colors  = new[]
            {
                sh
                    ? new Color(0.92f, 0.88f, 0.78f, 0.28f) // faint hot steam at the root
                    : new Color(1.00f, 0.55f, 0.20f, 0.55f), // incandescent near nozzle
                sh
                    ? new Color(0.58f, 0.58f, 0.55f, 0.46f) // dense grey-white deluge/soot
                    : new Color(0.48f, 0.42f, 0.36f, 0.35f),
                new Color(0.16f, 0.16f, 0.17f, 0.0f),       // smoke fade-out
            },
            Offsets = new[] { 0f, 0.4f, 1f },
        };
        var gradTex = new GradientTexture1D { Gradient = grad };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius      = mouthR,
            EmissionRingInnerRadius = Mathf.Max(0f, mouthR - 0.15f),
            EmissionRingAxis        = Vector3.Up,
            EmissionRingHeight      = 0.04f,

            Direction          = new Vector3(0, -1, 0),
            Spread             = 12f,
            InitialVelocityMin = sh ? 15f : 10f,
            InitialVelocityMax = sh ? 38f : 26f,

            DampingMin = 2f,
            DampingMax = 5f,

            ScaleMin = sh ? 2.0f : 1.0f,
            ScaleMax = sh ? 6.2f : 3.0f,

            ColorRamp = gradTex,
        };

        var quad = new QuadMesh { Size = new Vector2(2.6f, 2.6f) };
        var drawMat = new StandardMaterial3D
        {
            BillboardMode            = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode                = sh ? BaseMaterial3D.BlendModeEnum.Mix : BaseMaterial3D.BlendModeEnum.Add,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode            = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture            = SoftCircle,
            AlbedoColor              = Colors.White,
            EmissionEnabled          = !sh,
            EmissionEnergyMultiplier = sh ? 0.7f : 1.6f,
            VertexColorUseAsAlbedo   = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = name,
            Position        = new Vector3(0, yPos, 0),
            Amount          = amount,
            Lifetime        = sh ? 1.7f : 1.1f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            OneShot         = false,
            Preprocess      = 0.2f,
            SpeedScale      = 1.0f,
            VisibilityAabb  = new Aabb(new Vector3(-20f, -340f, -20f),
                                       new Vector3(40f, 480f, 40f)),
        };
    }
}
