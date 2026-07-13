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

    // N5: cloud dominates 0–3 s; ceiling lowered so it dissipates by ~300 m.
    // Altitude band (metres) over which the cloud is active and fades out.
    private const float TriggerCeilingM = 550f;   // above this: fully off (smoke column lingers longer)
    private const float FullIntensityM  = 140f;   // at/under this: full force (huge cloud through the first seconds)
    private const float MinThrottle     = 0.02f;  // throttle floor to count as "lit"

    // Pivot we rotate to align local +Y with the planet's "up" at the vessel,
    // and translate down to the receding ground point.
    private Node3D _pivot = null!;

    // The five layers of the deluge cloud.
    private GpuParticles3D _steamCore   = null!;  // dense, fast outward billow
    private GpuParticles3D _steamBoil   = null!;  // taller lingering boil-up
    private GpuParticles3D _dust        = null!;  // low dark debris/dust
    private GpuParticles3D _haze        = null!;  // faint lingering ground dust haze
    // N5: second dust emitter — radial blast wave at ground level.
    private GpuParticles3D _dustRadial  = null!;  // fast flat dust ring expanding radially
    private MultiMeshInstance3D _instantSteam = null!; // guaranteed ignition cloud bank
    private Node3D _billowBank = null!;
    private StandardMaterial3D _billowMaterial = null!;

    // Shared soft-round billboard texture for all layers.
    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();

    // Smoothed intensity so ignition/cutoff ramps instead of popping.
    private float _intensity;
    private bool _emitting;
    private float _ignitionAge;

    // Cached Earth body id check result is cheap; we just re-resolve each frame.

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "DelugePivot" };
        AddChild(_pivot);

        _steamCore  = BuildSteamCore();
        _steamBoil  = BuildSteamBoil();
        _dust       = BuildDust();
        _haze       = BuildHaze();
        _dustRadial = BuildDustRadial();  // N5: ground-level radial blast ring
        _instantSteam = BuildImmediateSteamBank();
        _billowBank = BuildBillowBank();

        _pivot.AddChild(_haze);        // faint ground haze underneath everything
        _pivot.AddChild(_dustRadial);  // N5: radial blast wave at pad deck level
        _pivot.AddChild(_dust);        // dust sits under the steam
        _pivot.AddChild(_steamBoil);
        _pivot.AddChild(_steamCore);
        _pivot.AddChild(_instantSteam);
        _pivot.AddChild(_billowBank);

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

        // Starbase's deluge is already flowing when the Raptors light. Seed a
        // substantial cloud on the ignition edge instead of visually ramping
        // from an empty particle buffer.
        if (target > 0f && _intensity < 0.01f)
            _intensity = 0.38f;

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
        _ignitionAge += (float)delta;
        DriveImmediateSteam(_intensity, _ignitionAge);
    }

    // ── Per-frame intensity → emission amount (no per-frame allocations) ──────
    private void DriveAmounts(float k)
    {
        // Keep Amount fixed after construction. Mutating it rebuilt GPU buffers
        // during spool-up and repeatedly discarded the initial steam cloud.
        float ratio = Mathf.Clamp(k, 0.02f, 1f);
        _steamCore.AmountRatio  = Mathf.Lerp(0.48f, 1.00f, ratio);
        _steamBoil.AmountRatio  = Mathf.Lerp(0.34f, 1.00f, ratio);
        _dust.AmountRatio       = ratio;
        _haze.AmountRatio       = Mathf.Lerp(0.25f, 1.00f, ratio);
        _dustRadial.AmountRatio = Mathf.Lerp(0.40f, 1.00f, ratio);
    }

    private void SetEmitting(bool on)
    {
        if (_emitting == on) return;
        _emitting = on;
        _steamCore.Emitting  = on;
        _steamBoil.Emitting  = on;
        _dust.Emitting       = on;
        _haze.Emitting       = on;
        _dustRadial.Emitting = on;  // N5
        _instantSteam.Visible = on;
        _billowBank.Visible = on;
        if (on)
        {
            _ignitionAge = 0f;
            _steamCore.Restart(true);
            _steamBoil.Restart(true);
            _dust.Restart(true);
            _haze.Restart(true);
            _dustRadial.Restart(true);
        }
    }

    private MultiMeshInstance3D BuildImmediateSteamBank()
    {
        var mat = SteamDrawMaterial(energy: 1.18f);
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        var quad = new QuadMesh { Size = new Vector2(5.8f, 5.8f) };
        quad.SurfaceSetMaterial(0, mat);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = 160,
        };
        var bank = new MultiMeshInstance3D
        {
            Name = "ImmediateDelugeBank",
            Multimesh = mm,
            Visible = false,
            CustomAabb = new Aabb(new Vector3(-90f, -4f, -90f), new Vector3(180f, 70f, 180f)),
        };
        DriveImmediateSteam(bank, 0.45f, 0.0f);
        return bank;
    }

    private Node3D BuildBillowBank()
    {
        var bank = new Node3D { Name = "ImmediateDelugeBillows", Visible = false };
        _billowMaterial = SteamDrawMaterial(energy: 0.0f);
        // A sphere cannot use the radial billboard atlas without producing
        // UV seams.  Its own silhouette supplies the soft lobe; restrained
        // emission lets the exhaust light the cloud without clipping white.
        _billowMaterial.AlbedoTexture = null;
        _billowMaterial.AlbedoColor = new Color(0.46f, 0.49f, 0.52f, 0.34f);
        _billowMaterial.EmissionEnabled = false;
        for (int i = 0; i < 28; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            float angle = i * 2.399963f;
            float radius = 7f + (i % 9) * 4.2f;
            float lobeRadius = 2.8f + phase * 2.6f;
            var puff = new MeshInstance3D
            {
                Name = $"DelugeBillow{i}",
                // A low-poly volumetric lobe remains visible from every pad
                // camera angle.  The former camera-facing quads disappeared
                // intermittently on the compatibility renderer during the
                // exact ignition frames this bank exists to guarantee.
                Mesh = new SphereMesh
                {
                    Radius = lobeRadius,
                    Height = lobeRadius * (1.45f + phase * 0.35f),
                    RadialSegments = 12,
                    Rings = 6,
                },
                Position = new Vector3(Mathf.Cos(angle) * radius,
                    4f + (i % 7) * 3.0f, Mathf.Sin(angle) * radius),
                Scale = new Vector3(1.15f + phase * 0.45f, 0.80f + phase * 0.25f,
                    1.05f + (1f - phase) * 0.35f),
                MaterialOverride = _billowMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            bank.AddChild(puff);
        }
        return bank;
    }

    private void DriveImmediateSteam(float intensity, float age) =>
        DriveImmediateSteamLayers(intensity, age);

    private void DriveImmediateSteamLayers(float intensity, float age)
    {
        DriveImmediateSteam(_instantSteam, intensity, age);
        float life = Mathf.Clamp(1f - Mathf.Max(0f, age - 5f) / 7f, 0f, 1f);
        Color color = _billowMaterial.AlbedoColor;
        color.A = Mathf.Clamp(Mathf.Lerp(0.24f, 0.44f, intensity) * life, 0f, 0.44f);
        _billowMaterial.AlbedoColor = color;
    }

    private static void DriveImmediateSteam(MultiMeshInstance3D bank, float intensity, float age)
    {
        var mm = bank.Multimesh;
        if (mm == null) return;
        float life = Mathf.Clamp(1f - Mathf.Max(0f, age - 5f) / 7f, 0f, 1f);
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            float angle = i * 2.399963f + phase * 0.35f;
            float speed = 1.2f + phase * 2.2f;
            float radius = 5.0f + (i % 19) * 2.7f + Mathf.Min(age, 7f) * speed;
            float height = 2.0f + (i % 13) * 1.75f + Mathf.Min(age, 7f) * (0.55f + phase * 0.70f);
            float size = (1.9f + phase * 2.30f) * (1f + Mathf.Min(age, 6f) * 0.12f);
            var basis = Basis.Identity.Scaled(new Vector3(size * (1.15f + phase * 0.25f), size, 1f));
            var origin = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            mm.SetInstanceTransform(i, new Transform3D(basis, origin));
            mm.SetInstanceColor(i, new Color(1.0f, 0.88f + phase * 0.11f, 0.80f + phase * 0.18f,
                Mathf.Clamp(Mathf.Lerp(0.68f, 0.96f, intensity) * life, 0f, 0.96f)));
        }
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
                new Color(1.00f, 0.34f, 0.08f, 0.68f), // exhaust-lit steam at the source
                new Color(1.00f, 0.66f, 0.30f, 0.88f), // orange inner cloud
                new Color(0.96f, 0.95f, 0.96f, 0.84f), // dense white steam
                new Color(0.82f, 0.83f, 0.86f, 0.70f), // cooling grey
                new Color(0.70f, 0.71f, 0.75f, 0.00f), // dissipate
            },
            Offsets = new[] { 0f, 0.16f, 0.42f, 0.76f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            // N5: wider emission ring — the deluge arc spans the full pad diameter.
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 9.0f,   // N5: ~25 m radius ring (was 6 m)
            EmissionRingInnerRadius = 1.2f,
            EmissionRingHeight      = 1.5f,   // N5: taller emission band (was 1.0)

            // Fire mostly OUTWARD & slightly up; wide spread so it fans across pad.
            // Lower the vertical bias so the FIRST motion is a ground-hugging surge.
            Direction          = new Vector3(0f, 0.22f, 1f).Normalized(),
            Spread             = 88f,
            Flatness           = 0.78f,        // strong bias toward horizontal sheeting
            // N5: faster initial burst so the cloud DOMINATES the screen at 0-3 s.
            // Godot values are render units/s: 11–26 corresponds to roughly
            // 31–73 m/s, consistent with a violent but ground-bound deluge front.
            InitialVelocityMin = 11f,
            InitialVelocityMax = 26f,

            // Heavy damping so it decelerates and balloons rather than streaking.
            DampingMin = 1.3f,
            DampingMax = 3.4f,

            // Buoyancy: once the outward surge slows, it mushrooms upward.
            Gravity = new Vector3(0f, 0.75f, 0f),

            // Turbulent, slow drift for that churning volume.
            TurbulenceEnabled               = true,
            TurbulenceNoiseStrength         = 4.0f,   // N5: chunkier turbulence (was 3.4)
            TurbulenceNoiseScale            = 1.1f,
            TurbulenceInfluenceMin          = 0.14f,
            TurbulenceInfluenceMax          = 0.60f,

            AngularVelocityMin = -55f,
            AngularVelocityMax = 55f,

            // N5: bigger initial scale — the billows dominate the frame from birth.
            ScaleMin = 1.3f,
            ScaleMax = 3.4f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.30f, 1.0f); // N5: start smaller and grow more aggressively

        // N5: larger quad mesh — each billboard covers more screen area.
        var quad = new QuadMesh { Size = new Vector2(4.0f, 4.0f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 1.30f)); // N5: brighter (was 1.15)

        return new GpuParticles3D
        {
            Name            = "DelugeSteamCore",
            Amount          = 560,
            Lifetime        = 7.5f,           // N5: longer-lived (was 6.5)
            Preprocess      = 1.25f,          // dense, already-developed ignition frame
            Explosiveness   = 0.12f,          // N5: more burst-like at ignition (was 0.08)
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,           // follows the explicitly body-fixed ground pivot
            VisibilityAabb  = new Aabb(new Vector3(-320f, -12f, -320f), new Vector3(640f, 380f, 640f)),
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
                new Color(1.00f, 0.48f, 0.18f, 0.40f),
                new Color(0.98f, 0.86f, 0.78f, 0.70f),
                new Color(0.74f, 0.75f, 0.79f, 0.45f),
                new Color(0.62f, 0.63f, 0.68f, 0.00f),
            },
            Offsets = new[] { 0f, 0.08f, 0.65f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 16.0f,   // N5: wider boil column (was 13)
            EmissionRingInnerRadius = 3.0f,
            EmissionRingHeight      = 2.0f,    // N5: taller emission band (was 1.5)

            Direction          = new Vector3(0f, 1f, 0.45f).Normalized(),
            Spread             = 62f,
            // N5: faster vertical surge during 0-3 s — tower of steam above the pad.
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 9.0f,

            DampingMin = 0.8f,
            DampingMax = 2.2f,

            Gravity = new Vector3(0f, 1.1f, 0f),

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 4.2f,     // N5: chunkier (was 3.8)
            TurbulenceNoiseScale    = 0.85f,
            TurbulenceInfluenceMin  = 0.18f,
            TurbulenceInfluenceMax  = 0.65f,

            AngularVelocityMin = -32f,
            AngularVelocityMax = 32f,

            // N5: larger puffs — the boil column fills the sky above the pad.
            ScaleMin = 1.6f,
            ScaleMax = 4.2f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.40f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(4.5f, 4.5f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 1.05f));   // N5: slightly brighter (was 0.9)

        return new GpuParticles3D
        {
            Name            = "DelugeSteamBoil",
            Amount          = 300,
            Lifetime        = 11.0f,           // N5: slightly longer (was 10.0)
            Preprocess      = 1.0f,
            Explosiveness   = 0.05f,           // N5: slight burst at ignition (was 0.0)
            Randomness      = 0.6f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-360f, -12f, -360f), new Vector3(720f, 560f, 720f)),
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
            EmissionRingRadius      = 6.0f,    // N5: wider source ring (was 4.0)
            EmissionRingInnerRadius = 0.6f,
            EmissionRingHeight      = 0.4f,

            // Almost flat — blasts sideways across the pad, fast and far.
            Direction          = new Vector3(0f, 0.06f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.95f,
            // N5: faster blast for a dramatic ground-level sweep.
            InitialVelocityMin = 16f,
            InitialVelocityMax = 34f,

            DampingMin = 2.5f,
            DampingMax = 6.0f,

            Gravity = new Vector3(0f, -1.2f, 0f), // dust settles, doesn't rise

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 2.0f,
            TurbulenceNoiseScale    = 1.3f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.35f,

            // N5: larger dust chunks — more visible on screen.
            ScaleMin = 4.0f,   // was 3.0
            ScaleMax = 8.0f,   // was 6.0
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.50f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(6.0f, 6.0f) };  // N5: was 5x5
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
            Amount          = 110,             // N5: was 90
            Lifetime        = 5.5f,            // N5: longer-lived (was 4.5)
            Preprocess      = 0.3f,
            Explosiveness   = 0.18f,           // N5: more burst (was 0.12)
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-320f, -10f, -320f), new Vector3(640f, 80f, 640f)),
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
            EmissionRingRadius      = 18.0f,
            EmissionRingInnerRadius = 2.0f,
            EmissionRingHeight      = 0.5f,

            // Slow, near-flat outward creep — the haze just hangs and spreads.
            Direction          = new Vector3(0f, 0.05f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.92f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 3.0f,

            DampingMin = 1.5f,
            DampingMax = 4f,

            Gravity = new Vector3(0f, 0.4f, 0f), // barely rises

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.2f,
            TurbulenceNoiseScale    = 0.7f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.25f,

            ScaleMin = 10.0f,
            ScaleMax = 17.0f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.6f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(11.0f, 11.0f) };
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
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-360f, -10f, -360f), new Vector3(720f, 120f, 720f)),
        };
    }

    /// <summary>
    /// N5: Fast, very flat radial dust blast ring at deck level.
    /// A short-lived, high-explosiveness emitter that fires at ignition and
    /// expands RADIALLY outward in a thin disk, like a pressure wave sweeping
    /// the pad surface. This is the "shockwave of dust" seen 0–2 s after ignition
    /// in real Super Heavy launches — it spreads 50–100 m from the mount in
    /// the first two seconds and then quickly fades.
    /// </summary>
    private GpuParticles3D BuildDustRadial()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.55f, 0.50f, 0.42f, 0.00f), // born transparent
                new Color(0.50f, 0.46f, 0.38f, 0.80f), // opaque tan dust
                new Color(0.60f, 0.57f, 0.52f, 0.50f), // fading lighter
                new Color(0.65f, 0.63f, 0.60f, 0.00f), // dissipate
            },
            Offsets = new[] { 0f, 0.10f, 0.55f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            // Emit from a tight ring right at the pad-arm radius.
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 5.5f,
            EmissionRingInnerRadius = 0.5f,
            EmissionRingHeight      = 0.15f,   // very thin — hugs the deck

            // Purely radial outward blast: no vertical component.
            Direction          = new Vector3(0f, 0.03f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.98f,        // nearly perfectly horizontal
            InitialVelocityMin = 24f,
            InitialVelocityMax = 48f,

            // Strong deceleration — the wave front hits air resistance quickly.
            DampingMin = 5f,
            DampingMax = 11f,

            Gravity = new Vector3(0f, -2.5f, 0f), // dust falls back to deck

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.5f,
            TurbulenceNoiseScale    = 2.0f,
            TurbulenceInfluenceMin  = 0.04f,
            TurbulenceInfluenceMax  = 0.20f,

            // Medium scale — flat slabs of kicked concrete dust.
            ScaleMin = 2.5f,
            ScaleMax = 5.5f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.45f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(4.5f, 4.5f) };
        var drawMat = new StandardMaterial3D
        {
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Mix,   // opaque occlusion
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode          = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture          = SoftCircle,
            AlbedoColor            = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = "DelugeRadialDust",
            Amount          = 100,
            Lifetime        = 3.0f,            // short-lived: gone by ~3 s
            Preprocess      = 0.0f,            // no preprocess — this IS the initial blast
            Explosiveness   = 0.65f,           // very burst-like at ignition
            Randomness      = 0.4f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-380f, -10f, -380f), new Vector3(760f, 30f, 760f)),
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
            // Alpha mixing lets hundreds of overlapping billows become an
            // optically dense wall instead of isolated glowing discs.
            BlendMode                = BaseMaterial3D.BlendModeEnum.Mix,
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
        const int S = 96;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            // Overlapping anisotropic lobes form an irregular billow rather
            // than the former perfect circular "particle ball".
            float l0 = Mathf.Exp(-(dx * dx * 2.4f + dy * dy * 3.0f));
            float l1 = Mathf.Exp(-((dx + 0.34f) * (dx + 0.34f) * 5.2f
                + (dy - 0.05f) * (dy - 0.05f) * 6.0f));
            float l2 = Mathf.Exp(-((dx - 0.30f) * (dx - 0.30f) * 5.8f
                + (dy + 0.12f) * (dy + 0.12f) * 4.8f));
            float l3 = Mathf.Exp(-((dx + 0.05f) * (dx + 0.05f) * 7.0f
                + (dy + 0.38f) * (dy + 0.38f) * 7.5f));
            float edge = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy) * 0.86f, 0f, 1f);
            float breakup = 0.86f + 0.14f * Mathf.Sin(x * 0.51f + y * 0.37f)
                * Mathf.Sin(x * 0.19f - y * 0.43f);
            float density = Mathf.Clamp(l0 * 0.72f + l1 * 0.42f + l2 * 0.40f + l3 * 0.28f,
                0f, 1f);
            float a = Mathf.SmoothStep(0f, 1f, density * edge * breakup);
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
