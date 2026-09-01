namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Local TRUE-scale Earth ground patch for low-altitude flight.
///
/// The scaled-space backdrop (see <see cref="FloatingOrigin"/>) draws Earth as a
/// 50,000-unit sphere; up close that curves FAR too hard and the planet looks like
/// a small ball. This controller instead lays down a large tangent-plane mesh
/// directly under the active vessel, with each vertex dropped by the TRUE sphere
/// curvature <c>y = -(x²+z²)/(2R)</c>. At 4–10 km altitude the horizon is then far
/// and essentially flat — exactly as in reality — while coordinates stay float-safe.
///
/// The surface look (ocean / land / coastlines / clouds) is procedural and sampled
/// from a WORLD-SPACE ground coordinate: as the vessel translates over the planet,
/// that coordinate scrolls, so features glide across the patch and you can clearly
/// SEE the rocket moving. The whole patch cross-fades into the backdrop on ascent.
///
/// Anchored each frame like <see cref="MarsTerrainController"/>; add as a child of
/// the "World" Node3D. Render scale: 1 unit = <see cref="MetresPerUnit"/> metres.
/// </summary>
public partial class EarthGroundController : Node3D
{
    /// <summary>Runtime compositor state consumed by deterministic visual captures.</summary>
    public float LocalPatchOpacity { get; private set; }
    public bool LocalPatchVisible => Visible && LocalPatchOpacity > 0.001f;

    // ── Render scale ─────────────────────────────────────────────────────────
    private const float  MetresPerUnit = 2.8f;
    // Fallback only until the live sim body is available. Curvature of the patch
    // is weakly sensitive to the ~20 km ellipsoid vs mean-radius difference;
    // anchoring is not, and always reads the body through SimulationBridge.

    // ── Patch geometry (in render UNITS) ─────────────────────────────────────
    // Circular disc to the geometric horizon at ~50 km (√(2 R h) ≈ 800 km).
    // 800,000 m / 2.8 ≈ 286,000 units. A square of any size reads as a cookie
    // from the play camera; the silhouette is a disc with a shader rim fade.
    private const float PatchRadiusUnits = 280_000f;
    private const int   DiscRings        = 48;
    private const int   DiscSegments     = 72;

    // Vessel-altitude safety: hide the tangent patch once the rocket itself is
    // well into the scaled-space regime even if a chase camera is somehow low.
    // The visible pad→globe cross-fade is owned by FloatingOrigin.EarthGlobeAlpha.
    // Safety cutoff after the shared 40–75 km handoff. The shader fade reaches zero
    // first; this guard only handles unusual camera/origin states.
    private const double FadeHi = 90_000.0;

    // Local-ground calibration: a small blue-biased indirect floor keeps the
    // surface readable at night/twilight without changing the direct solar path.
    // The first .032 and .08 calibrations remained crushed in the real 1280x720
    // sunrise/night framebuffer. .12 is the shader's bounded ceiling and is still
    // earthshine only; direct sunlight remains visibility-gated in the shader.
    private const float NightFloor = 0.12f;
    private const float NightFloorTintR = 0.72f;
    private const float NightFloorTintG = 0.84f;
    private const float NightFloorTintB = 1.00f;
    private const float EarthshineGain = 2.80f;
    private const float EarthshineMinReflectance = 0.055f;
    private const float DetailStrength = 0.18f;
    private const float CoastalGrade = 0.28f;
    private const float TerrainReliefStrength = 0.18f;
    private const float NightCityGain = 0.34f;
    private const float TerminatorWidth = 0.16f;
    private const float HorizonHazeStrength = 0.92f;

    private MeshInstance3D  _mesh = null!;
    private ShaderMaterial  _mat  = null!;
    private bool _groundShaderStateInitialized;
    private float _lastFade = float.NaN;
    private float _lastHorizonDistance = float.NaN;
    private float _lastEarthRadius = float.NaN;
    private Color _lastHazeColor;
    private Vector3 _lastSunDirection;
    private MeshInstance3D? _civilGround;
    private readonly List<MeshInstance3D> _civilMeshes = new();

    public override void _Ready()
    {
        _mesh = new MeshInstance3D { Name = "EarthGround", Mesh = BuildMesh() };

        var shader = GD.Load<Shader>("res://assets/shaders/earth_ground.gdshader");
        if (shader != null)
        {
            _mat = new ShaderMaterial { Shader = shader };
            _mat.SetShaderParameter("fade", 1.0f);
            _mat.SetShaderParameter("earth_radius", (float)InitialEarthRadiusMetres());
            _mat.SetShaderParameter("metres_per_unit", MetresPerUnit);
            var opticalDepth = AtmosphereModel.Earth().Optics.VerticalOpticalDepth(0.0);
            _mat.SetShaderParameter("vertical_optical_depth", new Vector3(
                (float)opticalDepth.X, (float)opticalDepth.Y, (float)opticalDepth.Z));
            _mat.SetShaderParameter("night_floor", NightFloor);
            _mat.SetShaderParameter("night_floor_tint", new Vector3(
                NightFloorTintR, NightFloorTintG, NightFloorTintB));
            _mat.SetShaderParameter("earthshine_gain", EarthshineGain);
            _mat.SetShaderParameter("earthshine_min_reflectance", EarthshineMinReflectance);
            _mat.SetShaderParameter("detail_strength", DetailStrength);
            _mat.SetShaderParameter("coastal_grade", CoastalGrade);
            _mat.SetShaderParameter("terrain_relief_strength", TerrainReliefStrength);
            _mat.SetShaderParameter("night_city_gain", NightCityGain);
            _mat.SetShaderParameter("terminator_width", TerminatorWidth);
            _mat.SetShaderParameter("horizon_haze_strength", HorizonHazeStrength);
            var dayTexture = GD.Load<Texture2D>("res://assets/textures/earth_day.jpg");
            if (dayTexture != null) _mat.SetShaderParameter("day_tex", dayTexture);
            var nightTexture = GD.Load<Texture2D>("res://assets/textures/earth_night.jpg");
            if (nightTexture != null) _mat.SetShaderParameter("night_tex", nightTexture);
            _mesh.SetSurfaceOverrideMaterial(0, _mat);
        }
        else
        {
            // Defensive fallback: a plain blue patch if the shader is missing.
            var fallback = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.06f, 0.28f, 0.5f),
                Roughness   = 0.9f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            _mesh.SetSurfaceOverrideMaterial(0, fallback);
        }

        // Big disc — keep it from being frustum-culled at grazing angles.
        _mesh.CustomAabb = new Aabb(
            new Vector3(-PatchRadiusUnits, -12000f, -PatchRadiusUnits),
            new Vector3(2f * PatchRadiusUnits, 24000f, 2f * PatchRadiusUnits));
        _mesh.Transparency = 0f;
        // The planetary disc is too large for a useful shadow map. Pad concrete
        // (launch_surface) still receives DirectionalLight shadows.
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _mesh.SortingOffset = -2f;

        AddChild(_mesh);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null)
        {
            Visible = false;
            LocalPatchOpacity = 0f;
            return;
        }

        var earth = universe.GetBody("earth");
        if (earth == null)
        {
            Visible = false;
            LocalPatchOpacity = 0f;
            return;
        }

        var dominant = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(earth);
        if (dominant.Id != "earth" || alt > FadeHi)
        {
            Visible = false;
            LocalPatchOpacity = 0f;
            _groundShaderStateInitialized = false;
            return;
        }

        // Complementary to FloatingOrigin.EarthGlobeAlpha: the patch owns the
        // horizon on the pad, the globe owns it in space, and they share one
        // 40–75 km camera-altitude handoff so neither a double-Earth nor a gap.
        float fade = 1f - FloatingOrigin.EarthGlobeAlpha(FloatingOrigin.CameraAltOverEarth);
        if (fade <= 0.001f)
        {
            Visible = false;
            LocalPatchOpacity = 0f;
            _groundShaderStateInitialized = false;
            return;
        }
        Visible = true;
        LocalPatchOpacity = fade;

        // Anchor to the live ellipsoid (or sphere), never centre + R_mean · r̂.
        // A mean-radius sphere buries Kennedy/Starbase by kilometres relative to
        // the geodetic pad frame SimulationBridge already uses.
        var up         = earth.GetGeodeticUp(vessel.Position);
        var surfacePos = earth.GetSurfacePoint(vessel.Position, 0.0);
        var offsetM    = surfacePos - vessel.Position;                    // metres
        var renderUp   = new Vector3((float)up.X, (float)up.Y, (float)up.Z);
        var rotationAxis = ToGodot(earth.RotationAxis).Normalized();
        var east = rotationAxis.Cross(renderUp).Normalized();
        if (east.LengthSquared() < 1e-6f)
            east = Vector3.Forward.Cross(renderUp).Normalized();
        var north = renderUp.Cross(east).Normalized();
        var basis = new Basis(east, renderUp, north);

        // metres → units for the render-space translation. Sit the unshaded
        // disc ~1.5 m below civil concrete/wetland so DirectionalLight shadows
        // land on the shaded apron instead of being overwritten by this mesh.
        var offsetU = new Vector3(
            (float)(offsetM.X / MetresPerUnit),
            (float)(offsetM.Y / MetresPerUnit),
            (float)(offsetM.Z / MetresPerUnit));
        GlobalTransform = new Transform3D(basis, offsetU - renderUp * 0.55f);

        if (_mat != null)
        {
            var hazeColor = SkyController.CurrentHorizonColor;
            if (!_groundShaderStateInitialized || FloatDiffers(_lastFade, fade))
            {
                _mat.SetShaderParameter("fade", fade);
                _lastFade = fade;
            }
            if (!_groundShaderStateInitialized || ColorDiffers(_lastHazeColor, hazeColor))
            {
                _mat.SetShaderParameter("haze_color", hazeColor);
                _lastHazeColor = hazeColor;
            }
            var sun = universe.GetBody("sun");
            if (sun != null)
            {
                var physicalDirection = (sun.Position - vessel.Position).Normalized;
                var sunDirection = ToGodot(SunController.Instance != null
                    ? SunController.Instance.GetVisualSunDirection(
                        earth, vessel.Position, physicalDirection)
                    : physicalDirection);
                if (!_groundShaderStateInitialized
                    || _lastSunDirection.DistanceSquaredTo(sunDirection) > 1e-10f)
                {
                    _mat.SetShaderParameter("sun_dir", sunDirection);
                    _lastSunDirection = sunDirection;
                }
            }

            // Map the patch to the real Earth texture: the sub-vessel point and the patch's
            // east/north axes, expressed in the texture/mesh-local frame (undo the planet
            // tilt that the backdrop uses), so the ground shows the real launch-site terrain.
            var tiltInv  = FloatingOrigin.PlanetOrientation.Inverse();
            var subP     = tiltInv * renderUp;
            var eastL    = tiltInv * basis.X;    // patch +X (east)  in texture space
            var northL   = tiltInv * basis.Z;    // patch +Z (north) in texture space
            _mat.SetShaderParameter("sub_p", subP);
            _mat.SetShaderParameter("east_local", eastL);
            _mat.SetShaderParameter("north_local", northL);

            // True geometric horizon distance d = sqrt(2·R·h), in render units. Ground
            // beyond this hazes into the sky so the far curvature reads as a flat horizon.
            double localRadius = FloatingOrigin.VisualSurfaceRadiusMetres(earth, vessel.Position);
            if (!_groundShaderStateInitialized
                || FloatDiffers(_lastEarthRadius, (float)localRadius))
            {
                _mat.SetShaderParameter("earth_radius", (float)localRadius);
                _lastEarthRadius = (float)localRadius;
            }
            double cameraAltitude = System.Math.Max(FloatingOrigin.CameraAltOverEarth, 50.0);
            double hMetres = System.Math.Sqrt(2.0 * localRadius * cameraAltitude);
            float horizonDistance = (float)(hMetres / MetresPerUnit);
            if (!_groundShaderStateInitialized
                || FloatDiffers(_lastHorizonDistance, horizonDistance))
            {
                _mat.SetShaderParameter("horizon_dist", horizonDistance);
                _lastHorizonDistance = horizonDistance;
            }
            _groundShaderStateInitialized = true;
        }

        FadeCivilGroundBox(FloatingOrigin.CameraAltOverEarth);
    }

    /// <summary>
    /// Civil apron and OLM foundation read as cookies from a few kilometres up.
    /// Fade only those local civil meshes; StarbaseFarField owns the contextual
    /// complex and follows the Earth globe handoff independently.
    /// </summary>
    private void FadeCivilGroundBox(double cameraAltitudeM)
    {
        if (_civilMeshes.Count == 0)
            CollectCivilGroundMeshes();
        if (_civilMeshes.Count == 0) return;

        float hide = Smoothstep(150f, 700f, (float)cameraAltitudeM);
        for (int i = _civilMeshes.Count - 1; i >= 0; i--)
        {
            var mesh = _civilMeshes[i];
            if (mesh == null || !IsInstanceValid(mesh))
            {
                _civilMeshes.RemoveAt(i);
                continue;
            }
            mesh.Transparency = hide;
            mesh.Visible = hide < 0.97f;
        }
    }

    private void CollectCivilGroundMeshes()
    {
        _civilMeshes.Clear();
        Node? pad = LaunchPadController.Instance
            ?? GetTree().Root.FindChild("LaunchPadController", true, false);
        if (pad == null) return;
        foreach (string name in new[]
                 {
                     "Ground",
                     "OrbitalPadApron",
                     "OlmFoundationMat",
                 })
        {
            if (pad.FindChild(name, true, false) is MeshInstance3D mesh)
                _civilMeshes.Add(mesh);
        }
        _civilGround = _civilMeshes.Count > 0 ? _civilMeshes[0] : null;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Orient the patch (default +Y normal) so it lies tangent to the surface.
    private static Basis AlignUp(Vector3 up)
    {
        up = up.Normalized();
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Right)) < 0.9f ? Vector3.Right : Vector3.Forward;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        return new Basis(x, up, z);
    }

    private static double InitialEarthRadiusMetres()
    {
        var earth = SimulationBridge.Instance?.Universe?.GetBody("earth");
        if (earth == null) return 6_371_000.0;
        return earth.MaximumRadius > 1.0 ? earth.MaximumRadius : earth.Radius;
    }

    private static Vector3 ToGodot(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    private static bool FloatDiffers(float a, float b) =>
        float.IsNaN(a) || float.IsNaN(b) || Mathf.Abs(a - b) > 1e-4f;

    private static bool ColorDiffers(Color a, Color b) =>
        FloatDiffers(a.R, b.R)
        || FloatDiffers(a.G, b.G)
        || FloatDiffers(a.B, b.B)
        || FloatDiffers(a.A, b.A);

    /// <summary>
    /// Geodetic disc whose vertices drop by ellipsoid sagitta
    /// <c>y = -(x²+z²)/(2R)</c>. Polar rings keep a circular silhouette so the
    /// play camera never sees a square cookie. UV2 carries patch-local (x,z) in metres.
    /// </summary>
    private static ArrayMesh BuildMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        double radiusUnits = InitialEarthRadiusMetres() / MetresPerUnit;
        double invTwoR = 1.0 / (2.0 * radiusUnits);

        float Curve(float x, float z) => (float)(-(x * (double)x + z * (double)z) * invTwoR);
        Vector3 Vert(float x, float z) => new(x, Curve(x, z), z);

        Vector3 centre = Vert(0f, 0f);
        for (int ring = 0; ring < DiscRings; ring++)
        {
            float t0 = ring / (float)DiscRings;
            float t1 = (ring + 1) / (float)DiscRings;
            float r0 = RingRadius(t0);
            float r1 = RingRadius(t1);
            for (int s = 0; s < DiscSegments; s++)
            {
                float a0 = s * Mathf.Tau / DiscSegments;
                float a1 = (s + 1) * Mathf.Tau / DiscSegments;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                Vector3 inner0 = Vert(r0 * c0, r0 * s0);
                Vector3 outer0 = Vert(r1 * c0, r1 * s0);
                Vector3 outer1 = Vert(r1 * c1, r1 * s1);
                Vector3 inner1 = Vert(r0 * c1, r0 * s1);
                if (ring == 0)
                {
                    AddTri(st, centre, outer0, outer1);
                }
                else
                {
                    AddTri(st, inner0, outer0, outer1);
                    AddTri(st, inner0, outer1, inner1);
                }
            }
        }

        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// Pack rings so the inner ~30 km (play-camera nadir at 6–20 km) has small
    /// triangles instead of a 5 km centre fan that under-samples the coast.
    /// </summary>
    private static float RingRadius(float t)
    {
        const float innerFrac = 0.42f;
        float innerR = 30_000f / MetresPerUnit;
        if (t <= innerFrac)
            return innerR * (t / innerFrac);
        float u = (t - innerFrac) / (1f - innerFrac);
        return Mathf.Lerp(innerR, PatchRadiusUnits, Mathf.Pow(u, 1.20f));
    }

    private static void AddTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        EmitVert(st, a); EmitVert(st, b); EmitVert(st, c);
    }

    private static void EmitVert(SurfaceTool st, Vector3 v)
    {
        // Stash the patch-local horizontal coords in METRES in UV2 for the shader,
        // matching `ground_offset` (also metres) so the two add at the same scale.
        st.SetUV2(new Vector2(v.X * MetresPerUnit, v.Z * MetresPerUnit));
        st.AddVertex(v);
    }
}
