namespace Exosphere.Game;

using Godot;

/// <summary>
/// Renders a dense starfield fixed in inertial space, providing a visual
/// reference for rotation and orbital motion.
///
/// The active vessel is always pinned to the render origin by the
/// <see cref="FloatingOrigin"/> system, so the rocket never visually moves.
/// The camera, however, orbits that origin and the vessel's orientation
/// changes as it rotates/pitches/flips. This starfield is built once as a
/// single mesh (one draw call) and every frame is RE-CENTRED on the camera
/// but kept at IDENTITY ROTATION. Because the stars hold a fixed inertial
/// attitude while the camera sweeps around the spinning vessel, the whole sky
/// sweeps across the view — giving a strong, convincing motion reference.
///
/// Stars are drawn "infinitely far": the mesh sits at a huge radius, its
/// material disables depth test/write and uses a low render priority so it
/// always renders behind planets and the vessel.
///
/// The field fades in with altitude — invisible in the daylit lower
/// atmosphere, fully visible above ~80 km.
///
/// Self-wiring: add this as a child of the World Node3D. It finds the camera
/// via the scene tree in _Ready and guards every lookup, so it never crashes
/// even if SimulationBridge has not finished loading.
/// </summary>
public partial class StarfieldController : Node3D
{
    // ── Tuning ────────────────────────────────────────────────────────────
    private const int   StarCount   = 3500;     // total stars
    private const float SphereRadius = 900_000f; // render distance (< camera Far 2e6)

    // Altitude fade (metres above dominant body surface).
    private const double FadeLow  = 30_000.0;   // start fading in
    private const double FadeHigh = 80_000.0;   // fully visible

    // Optional ascent air-streak velocity cue thresholds.
    private const double StreakMinDensity = 0.02;   // kg/m³ — only in dense air
    private const double StreakMinSpeed   = 120.0;  // m/s surface speed to activate

    private MeshInstance3D?       _starMesh;
    private ShaderMaterial?       _starMat;
    private Camera3D?             _camera;
    private GpuParticles3D?       _streaks;
    private StandardMaterial3D?   _streakMat;

    private float _currentAlpha = -1f;   // cached to avoid redundant shader writes

    public override void _Ready()
    {
        // Locate the camera anywhere under the scene root (CameraController/Camera3D).
        _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

        BuildStarfield();
        BuildAirStreaks();

        // Stars are inertial: hold identity rotation regardless of any parent.
        TopLevel = true;                 // ignore parent transform
        GlobalBasis = Basis.Identity;
    }

    public override void _Process(double delta)
    {
        // Late-bind the camera in case it spawned after us.
        if (_camera == null || !IsInstanceValid(_camera))
            _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

        // ── Recenter on the camera, keep inertial (identity) rotation ──────
        // The stars translate with the viewer (so they never clip) but never
        // rotate — that fixed attitude is what makes the sky sweep past as the
        // vessel/camera rotates, conveying spin and orbital motion.
        if (_camera != null && IsInstanceValid(_camera))
            GlobalPosition = _camera.GlobalPosition;
        GlobalBasis = Basis.Identity;

        // Air-streaks track the camera's FULL transform (pos + orientation) so
        // streaks fly downward through the field of view.
        if (_streaks != null && _camera != null && IsInstanceValid(_camera))
            _streaks.GlobalTransform = _camera.GlobalTransform;

        // ── Altitude fade + air-streak cue ─────────────────────────────────
        UpdateFromSimulation();
    }

    private void UpdateFromSimulation()
    {
        float targetAlpha = 1f;          // default fully visible if sim not ready
        bool  streaksOn   = false;

        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;

        if (vessel != null && universe != null)
        {
            var body = universe.GetDominantBody(vessel.Position);
            if (body != null)
            {
                double alt = vessel.GetAltitude(body);
                double t   = System.Math.Clamp(
                    (alt - FadeLow) / (FadeHigh - FadeLow), 0.0, 1.0);
                targetAlpha = (float)(t * t * (3.0 - 2.0 * t)); // smoothstep

                // Air-streak cue: only in dense atmosphere at high surface speed.
                if (_streaks != null && body.Atmosphere != null)
                {
                    double density = body.GetAtmosphericDensity(vessel.Position);
                    double speed   = vessel.GetSurfaceVelocity(body).Magnitude;
                    streaksOn = density > StreakMinDensity && speed > StreakMinSpeed;
                }
            }
        }

        if (!Mathf.IsEqualApprox(targetAlpha, _currentAlpha))
        {
            _currentAlpha = targetAlpha;
            _starMat?.SetShaderParameter("alpha", targetAlpha);
            if (_starMesh != null)
                _starMesh.Visible = targetAlpha > 0.001f;
        }

        if (_streaks != null && _streaks.Emitting != streaksOn)
            _streaks.Emitting = streaksOn;
    }

    // ── Build the starfield as a single point-cloud mesh ──────────────────
    private void BuildStarfield()
    {
        var rng = new RandomNumberGenerator { Seed = 0xC0FFEE };

        var verts  = new Vector3[StarCount];
        var colors = new Color[StarCount];

        // A faint Milky-Way band: a great circle of extra-bright, denser stars.
        var bandNormal = new Vector3(0.3f, 1f, 0.2f).Normalized();

        for (int i = 0; i < StarCount; i++)
        {
            // Uniform direction on the unit sphere.
            Vector3 dir;
            do
            {
                dir = new Vector3(
                    rng.RandfRange(-1f, 1f),
                    rng.RandfRange(-1f, 1f),
                    rng.RandfRange(-1f, 1f));
            } while (dir.LengthSquared() < 0.0001f);
            dir = dir.Normalized();

            verts[i] = dir * SphereRadius;

            // Brightness: mostly faint, a few bright. Boost near the band.
            float band   = 1f - Mathf.Abs(dir.Dot(bandNormal));   // 1 on the band plane
            float bandHi = Mathf.Pow(Mathf.Clamp(band, 0f, 1f), 6f) * 0.35f;
            float bright = Mathf.Clamp(
                Mathf.Pow(rng.Randf(), 3.2f) * 0.9f + 0.1f + bandHi, 0.05f, 1f);

            // Colour: most white, some blue-white, a few faint yellow/red.
            float hueRoll = rng.Randf();
            Color c;
            if (hueRoll < 0.62f)        c = new Color(1.00f, 1.00f, 1.00f); // white
            else if (hueRoll < 0.85f)   c = new Color(0.70f, 0.80f, 1.00f); // blue-white
            else if (hueRoll < 0.95f)   c = new Color(1.00f, 0.95f, 0.78f); // faint yellow
            else                        c = new Color(1.00f, 0.78f, 0.66f); // faint red

            // Per-vertex point size is smuggled through the alpha channel.
            float size = Mathf.Lerp(1.4f, 4.5f, Mathf.Pow(bright, 1.5f));
            colors[i]  = new Color(c.R * bright, c.G * bright, c.B * bright, size);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color]  = colors;

        var arrMesh = new ArrayMesh();
        arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Points, arrays);

        _starMat = new ShaderMaterial { Shader = BuildStarShader() };
        _starMat.SetShaderParameter("alpha", 0f);
        arrMesh.SurfaceSetMaterial(0, _starMat);

        _starMesh = new MeshInstance3D
        {
            Name = "StarfieldMesh",
            Mesh = arrMesh,
            // Never frustum-cull; we move it to the camera every frame.
            CustomAabb = new Aabb(
                new Vector3(-SphereRadius, -SphereRadius, -SphereRadius),
                new Vector3(SphereRadius * 2f, SphereRadius * 2f, SphereRadius * 2f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible    = false,
        };
        AddChild(_starMesh);
    }

    // Point-sprite shader: depth disabled, additive, screen-constant point size.
    private static Shader BuildStarShader()
    {
        var shader = new Shader();
        shader.Code = @"
shader_type spatial;
render_mode unshaded, depth_draw_never, depth_test_disabled, cull_disabled, blend_add, shadows_disabled;

uniform float alpha : hint_range(0.0, 1.0) = 1.0;

varying float v_size;

void vertex() {
    // Point size travels in COLOR.a (set per-vertex on the CPU).
    v_size = COLOR.a;
    POINT_SIZE = COLOR.a;
}

void fragment() {
    // Soft round falloff so points read as stars, not squares.
    vec2 d = POINT_COORD - vec2(0.5);
    float r = length(d) * 2.0;
    float glow = clamp(1.0 - r, 0.0, 1.0);
    glow = pow(glow, 1.6);
    ALBEDO = COLOR.rgb;
    ALPHA = glow * alpha;
}
";
        return shader;
    }

    // ── Optional faint air-streak velocity cue (dense-atmosphere ascent) ──
    private void BuildAirStreaks()
    {
        // Thin, elongated quads streaking downward past the camera in local
        // camera space. Cheap: a small particle budget, additive, no shadows.
        var streakMesh = new QuadMesh
        {
            Size = new Vector2(0.06f, 3.2f),   // thin vertical streak
        };

        _streakMat = new StandardMaterial3D
        {
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor              = new Color(0.8f, 0.85f, 1.0f, 0.25f),
            BillboardMode            = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale       = true,
            DisableReceiveShadows    = true,
            VertexColorUseAsAlbedo   = true,
        };
        streakMesh.Material = _streakMat;

        var pm = new ParticleProcessMaterial
        {
            EmissionShape       = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents  = new Vector3(60f, 4f, 60f),
            Direction           = new Vector3(0f, -1f, 0f),
            Spread              = 6f,
            Gravity             = Vector3.Zero,
            InitialVelocityMin  = 180f,
            InitialVelocityMax  = 320f,
            ScaleMin            = 0.6f,
            ScaleMax            = 1.4f,
        };

        _streaks = new GpuParticles3D
        {
            Name             = "AscentAirStreaks",
            Amount           = 120,
            Lifetime         = 0.45,
            DrawPass1        = streakMesh,
            ProcessMaterial  = pm,
            Emitting         = false,
            // Particles live in front of the camera; recentred each frame.
            LocalCoords      = false,
            Visible          = true,
            CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel         = true,   // own world transform (driven to the camera)
        };
        AddChild(_streaks);
    }
}
