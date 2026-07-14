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
    // Earth radius in render units: 6,371,000 m / 2.8 ≈ 2.275e6.
    private const double EarthRadiusUnits = 6_371_000.0 / MetresPerUnit;

    // ── Patch geometry (in render UNITS) ─────────────────────────────────────
    // ~900 km across → half-extent ~450 km. 450,000 m / 2.8 ≈ 160,700 units.
    // Curvature drop at the edge ≈ 160700² / (2·2.275e6) ≈ 5,680 units (~16 km),
    // so every coordinate stays comfortably float-precise (±~160k horiz).
    private const float PatchHalfUnits = 160_700f;        // half-width in units
    private const int   Grid           = 96;              // 55k vertices; curvature stays smooth

    // ── Altitude fade bands (metres) ─────────────────────────────────────────
    private const double FullAlt = 10_000.0;   // fully opaque at/below this
    private const double FadeLo  = 14_000.0;   // start fading out here
    private const double FadeHi  = 26_000.0;   // fully gone above here (backdrop takes over)

    private MeshInstance3D  _mesh = null!;
    private ShaderMaterial  _mat  = null!;

    public override void _Ready()
    {
        _mesh = new MeshInstance3D { Name = "EarthGround", Mesh = BuildMesh() };

        var shader = GD.Load<Shader>("res://assets/shaders/earth_ground.gdshader");
        if (shader != null)
        {
            _mat = new ShaderMaterial { Shader = shader };
            _mat.SetShaderParameter("fade", 1.0f);
            _mat.SetShaderParameter("earth_radius", 6_371_000.0f);
            var opticalDepth = AtmosphereModel.Earth().Optics.VerticalOpticalDepth(0.0);
            _mat.SetShaderParameter("vertical_optical_depth", new Vector3(
                (float)opticalDepth.X, (float)opticalDepth.Y, (float)opticalDepth.Z));
            var dayTexture = GD.Load<Texture2D>("res://assets/textures/earth_day.jpg");
            if (dayTexture != null) _mat.SetShaderParameter("day_tex", dayTexture);
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
        if (dominant.Id != "earth" || alt > FadeHi) { Visible = false; return; }

        // Cross-fade: 1 below FullAlt → 0 above FadeHi (hold 1 across FullAlt..FadeLo).
        float fade = (float)(1.0 - Smoothstep(FadeLo, FadeHi, alt));
        if (alt <= FullAlt) fade = 1f;
        // Also fade out by CAMERA altitude (e.g. zooming far out at the pad) so the local patch
        // and the distant-Earth backdrop never overlap into a seam — the backdrop takes over.
        float camFade = (float)(1.0 - Smoothstep(20_000.0, 36_000.0, FloatingOrigin.CameraAltOverEarth));
        fade = System.Math.Min(fade, camFade);
        if (fade <= 0.001f) { Visible = false; return; }
        Visible = true;

        // ── Anchor under the vessel (vessel sits at the render origin) ────────
        var up         = (vessel.Position - earth.Position).Normalized;   // local up
        var surfacePos = earth.Position + up * earth.Radius;              // metres
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
            _mat.SetShaderParameter("fade", fade);
            _mat.SetShaderParameter("haze_color", SkyController.CurrentHorizonColor);
            var sun = universe.GetBody("sun");
            if (sun != null)
                _mat.SetShaderParameter("sun_dir", ToGodot(
                    (sun.Position - vessel.Position).Normalized));

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
            double cameraAltitude = System.Math.Max(FloatingOrigin.CameraAltOverEarth, 50.0);
            double hMetres = System.Math.Sqrt(2.0 * earth.Radius * cameraAltitude);
            _mat.SetShaderParameter("horizon_dist", (float)(hMetres / MetresPerUnit));
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

    private static Vector3 ToGodot(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    private static double Smoothstep(double a, double b, double x)
    {
        if (b <= a) return x >= b ? 1.0 : 0.0;
        double t = System.Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

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
        double invTwoR = 1.0 / (2.0 * EarthRadiusUnits);

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
