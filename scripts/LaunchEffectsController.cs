namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Presentation;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Math;

/// <summary>
/// Ground-interaction launch VFX: the iconic Super Heavy deluge cloud.
/// When the booster ignites on the pad, a massive billowing wall of white-grey
/// steam/smoke (water-deluge vaporisation + exhaust) erupts at y≈0 and rolls
/// OUTWARD horizontally, then boils upward — accompanied by darker dust kicked
/// up low and outward.
///
/// This controller owns the ground cloud; PlumeSystem owns engine exhaust.
/// Condensed water uses ray-integrated noisy ellipsoids in both renderers, with
/// local exhaust lighting and self-extinction. Growth uses simulation seconds;
/// the cloud stays at the fixed geodetic pad after the vehicle moves away.
/// The optical proxy is intentionally separate from authoritative flight physics.
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

    // Altitude band (metres) for active exhaust impingement; steam then disperses.
    private const float TriggerCeilingM = 550f;   // above this: fully off (smoke column lingers longer)
    private const float FullIntensityM  = 140f;   // at/under this: full force (huge cloud through the first seconds)
    private const float MinThrottle     = 0.02f;  // throttle floor to count as "lit"

    // Local pad frame shared with the fixed geodetic launch complex.
    private Node3D _pivot = null!;

    // Independent dust beneath the optical deluge volumes.
    private GpuParticles3D _dust        = null!;  // low dark debris/dust
    private GpuParticles3D _haze        = null!;  // faint lingering ground dust haze
    // N5: second dust emitter — radial blast wave at ground level.
    private GpuParticles3D _dustRadial  = null!;  // fast flat dust ring expanding radially
    private PadSteamCloud _steamCloud = null!;
    private readonly List<EngineReadout> _engineReadouts = new(39);
    private double _lastCloudSimulationTime = double.NaN;
    private Vector3 _sunDirection = Vector3.Up;
    public float DeliveredSourcePower => _sampledTarget;
    public float CloudAgeSeconds => _ignitionAge;
    public float CloudOpticalWeight => _steamCloud.OpticalWeight;
    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();
    // Smoothed intensity so ignition/cutoff ramps instead of popping.
    private float _intensity;
    private bool _emitting;
    private bool _launchCloudArmed;
    private float _ignitionAge;
    private const double PhysicsSamplePeriodSeconds = 1.0 / 20.0;
    private double _physicsSampleTimer;
    private Vessel? _sampledVessel;
    private Universe? _sampledUniverse;
    private bool _sampledStateValid;
    private float _sampledTarget;
    private double _sampledAltitude;
    private Vector3 _sampledUp = Vector3.Up;

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "DelugePivot" };
        AddChild(_pivot);

        _dust       = BuildDust();
        _haze       = BuildHaze();
        _dustRadial = BuildDustRadial();  // N5: ground-level radial blast ring
        _steamCloud = new PadSteamCloud { Name = "PadSteamCloud", Visible = false };

        _pivot.AddChild(_haze);        // faint ground haze underneath everything
        _pivot.AddChild(_dustRadial);  // N5: radial blast wave at pad deck level
        _pivot.AddChild(_dust);        // dust sits under the steam
        _pivot.AddChild(_steamCloud);

        SetEmitting(false);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        _physicsSampleTimer -= System.Math.Max(0.0, delta);
        if (_physicsSampleTimer <= 0.0
            || !ReferenceEquals(vessel, _sampledVessel)
            || !ReferenceEquals(universe, _sampledUniverse))
        {
            _physicsSampleTimer = PhysicsSamplePeriodSeconds;
            if (!ReferenceEquals(vessel, _sampledVessel) || !ReferenceEquals(universe, _sampledUniverse))
            {
                _launchCloudArmed = false;
                _intensity = 0f;
                SetEmitting(false);
                _lastCloudSimulationTime = double.NaN;
            }
            _sampledVessel = vessel;
            _sampledUniverse = universe;
            SampleLaunchState(vessel, universe);
        }

        if (!_sampledStateValid)
        {
            FadeOut(delta);
            return;
        }

        float target = _sampledTarget;
        double simTime = universe!.CurrentTime;
        float elapsed = double.IsNaN(_lastCloudSimulationTime) ? 0f
            : (float)System.Math.Max(0.0, simTime - _lastCloudSimulationTime);
        _lastCloudSimulationTime = simTime;

        if (target > 0f && _intensity < 0.01f)
            _ignitionAge = 0f;

        // Asymmetric smoothing: erupt fast at ignition, linger/fade slower so the
        // cloud reads as a self-sustaining body of vapour, not a switch.
        float rate = target > _intensity ? 8f : 0.12f;
        _intensity = Mathf.Lerp(_intensity, target, 1f - Mathf.Exp(-elapsed * rate));

        if (_intensity < 0.01f)
        {
            if (Visible) SetEmitting(false);
            Visible = false;
            _steamCloud.Visible = false;
            return;
        }

        Visible = true;

        // Use the same fixed geodetic launch-site transform as the actual pad.
        // A cloud under the moving rocket incorrectly slides across the wetlands.
        var pad = LaunchPadController.Instance;
        if (pad != null)
            _pivot.GlobalTransform = pad.GlobalTransform;
        else
        {
            _pivot.Position = -_sampledUp * (float)(_sampledAltitude / MetresPerUnit);
            AlignUp(_pivot, _sampledUp);
        }

        // ── Drive the layers ──────────────────────────────────────────────────
        SetEmitting(target > MinThrottle);
        DriveAmounts(_intensity);
        _ignitionAge += elapsed;
        DriveSteamCloud(_intensity, _ignitionAge);
    }

    private void SampleLaunchState(Vessel? vessel, Universe? universe)
    {
        _sampledStateValid = false;
        _sampledTarget = 0f;
        _sampledAltitude = 0.0;
        _sampledUp = Vector3.Up;

        if (vessel == null || universe == null) return;

        // Dominant body must be Earth for a pad launch.
        var body = universe.GetDominantBody(vessel.Position);
        if (body == null || body.Id != "earth") return;

        double altitude = vessel.GetAltitude(body); // metres
        vessel.FillEngineReadoutsAtPressure(_engineReadouts, vessel.GetAmbientPressure(body));
        float delivered = (float)EngineHudPresentation.DeliveredThrottle(_engineReadouts);
        bool lit = delivered > MinThrottle && vessel.HasActiveEngineParts;
        if (vessel.IsGroundHeld) _launchCloudArmed = true;
        bool onPad = altitude < TriggerCeilingM;
        if (!onPad) _launchCloudArmed = false;

        // Target intensity: full near the deck, easing to zero by the ceiling.
        float target = 0f;
        if (lit && onPad && _launchCloudArmed)
        {
            float t = ((float)altitude - FullIntensityM) /
                      (TriggerCeilingM - FullIntensityM);
            target = delivered * (1f - Mathf.Clamp(t, 0f, 1f)); // 1 at/under FullIntensityM → 0 at ceiling
        }

        var sun = universe.GetBody("sun");
        if (sun != null)
        {
            Vector3d direction = (sun.Position - vessel.Position).Normalized;
            direction = SunController.Instance?.GetVisualSunDirection(body, vessel.Position, direction)
                ?? direction;
            _sunDirection = ToGodot(direction);
        }
        Vector3 up = ToGodot((vessel.Position - body.Position).Normalized);
        if (up.LengthSquared() < 1e-6f) up = Vector3.Up;

        _sampledStateValid = true;
        _sampledTarget = target;
        _sampledAltitude = altitude;
        _sampledUp = up;
    }

    // ── Per-frame intensity → emission amount (no per-frame allocations) ──────
    private void DriveAmounts(float k)
    {
        // Keep Amount fixed after construction. Mutating it rebuilt GPU buffers
        // during spool-up and repeatedly discarded the initial steam cloud.
        // Dust remains subordinate to the condensed-water volumes.
        float ratio = Mathf.Clamp(k, 0.02f, 1f);
        _dust.AmountRatio       = Mathf.Lerp(0.20f, 0.70f, ratio);
        _haze.AmountRatio       = Mathf.Lerp(0.18f, 0.55f, ratio);
        _dustRadial.AmountRatio = Mathf.Lerp(0.30f, 0.65f, ratio);
    }

    private void SetEmitting(bool on)
    {
        if (_emitting == on) return;
        _emitting = on;
        _dust.Emitting       = on;
        _haze.Emitting       = on;
        _dustRadial.Emitting = on;  // N5
        if (on)
        {
            _dust.Restart(true);
            _haze.Restart(true);
            _dustRadial.Restart(true);
        }
    }

    private void DriveSteamCloud(float intensity, float age)
    {
        // Condensed water scatters sunlight and localized exhaust radiance.
        float daylight = Mathf.SmoothStep(-0.08f, 0.15f, _sunDirection.Dot(_sampledUp))
            * SunController.SolarVisibility;
        _steamCloud.UpdateCloud(intensity, _sampledTarget, age, _sunDirection, daylight);
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
        DriveSteamCloud(_intensity, _ignitionAge);
    }

    // ── Layer builders (called once) ─────────────────────────────────────────

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

        var quad = new QuadMesh { Size = new Vector2(9.5f, 3.4f) };
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

        var quad = new QuadMesh { Size = new Vector2(14.0f, 4.2f) };
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
