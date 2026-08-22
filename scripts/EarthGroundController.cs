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
    // ── Render scale ─────────────────────────────────────────────────────────
    private const float  MetresPerUnit = 2.8f;
    // Fallback only until the live sim body is available. Curvature of the patch
    // is weakly sensitive to the ~20 km ellipsoid vs mean-radius difference;
    // anchoring is not, and always reads the body through SimulationBridge.

    // ── Patch geometry (in render UNITS) ─────────────────────────────────────
    // ~900 km across → half-extent ~450 km. 450,000 m / 2.8 ≈ 160,700 units.
    // Curvature drop at the edge ≈ 160700² / (2·2.275e6) ≈ 5,680 units (~16 km),
    // so every coordinate stays comfortably float-precise (±~160k horiz).
    private const float PatchHalfUnits = 160_700f;        // half-width in units
    private const int   Grid           = 96;              // 55k vertices; curvature stays smooth

    // Vessel-altitude safety: hide the tangent patch once the rocket itself is
    // well into the scaled-space regime even if a chase camera is somehow low.
    // The visible pad→globe cross-fade is owned by FloatingOrigin.EarthGlobeAlpha.
    private const double FadeHi = 48_000.0;

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

        // Big patch — keep it from being frustum-culled at grazing angles.
        _mesh.CustomAabb = new Aabb(
            new Vector3(-PatchHalfUnits, -8000f, -PatchHalfUnits),
            new Vector3(2f * PatchHalfUnits, 16000f, 2f * PatchHalfUnits));
        _mesh.Transparency = 0f;
        _mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        AddChild(_mesh);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { Visible = false; return; }

        var earth = universe.GetBody("earth");
        if (earth == null) { Visible = false; return; }

        var dominant = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(earth);
        if (dominant.Id != "earth" || alt > FadeHi)
        {
            Visible = false;
            _groundShaderStateInitialized = false;
            return;
        }

        // Complementary to FloatingOrigin.EarthGlobeAlpha: the patch owns the
        // horizon on the pad, the globe owns it in space, and they share one
        // 18–42 km camera-altitude handoff so neither a double-Earth nor a gap.
        float fade = 1f - FloatingOrigin.EarthGlobeAlpha(FloatingOrigin.CameraAltOverEarth);
        if (fade <= 0.001f)
        {
            Visible = false;
            _groundShaderStateInitialized = false;
            return;
        }
        Visible = true;

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

        // metres → units for the render-space translation.
        var offsetU = new Vector3(
            (float)(offsetM.X / MetresPerUnit),
            (float)(offsetM.Y / MetresPerUnit),
            (float)(offsetM.Z / MetresPerUnit));
        GlobalTransform = new Transform3D(basis, offsetU);

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
    /// Flat tangent grid whose vertices drop by the TRUE sphere curvature
    /// <c>y = -(x²+z²)/(2R)</c>. UV2 carries the patch-local (x,z) in units so the
    /// shader can offset it by the world ground coordinate for scrolling.
    /// </summary>
    private static ArrayMesh BuildMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float step = (2f * PatchHalfUnits) / Grid;
        double radiusUnits = InitialEarthRadiusMetres() / MetresPerUnit;
        double invTwoR = 1.0 / (2.0 * radiusUnits);

        float Curve(float x, float z) => (float)(-(x * (double)x + z * (double)z) * invTwoR);

        Vector3 Vert(float x, float z) => new(x, Curve(x, z), z);

        for (int j = 0; j < Grid; j++)
        {
            float z0 = -PatchHalfUnits + j * step, z1 = z0 + step;
            for (int i = 0; i < Grid; i++)
            {
                float x0 = -PatchHalfUnits + i * step, x1 = x0 + step;

                Vector3 a = Vert(x0, z0), b = Vert(x1, z0);
                Vector3 c = Vert(x1, z1), d = Vert(x0, z1);

                AddTri(st, a, b, c);
                AddTri(st, a, c, d);
            }
        }

        st.GenerateNormals();
        return st.Commit();
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
