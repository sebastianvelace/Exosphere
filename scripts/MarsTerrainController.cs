namespace Exosphere.Game;

using Godot;

/// <summary>
/// Procedural rust-red Martian terrain patch. Built once with OpenSimplex-displaced
/// geometry; each frame it is anchored to the surface point directly beneath the active
/// vessel (oriented so its surface normal follows the local "up"), and shown only when
/// the vessel is low over Mars. Analogous to <see cref="LaunchPadController"/> for Earth.
/// </summary>
public partial class MarsTerrainController : Node3D
{
    // A 6 km patch disappeared almost completely below the 10 km atmosphere
    // capture: the geometric horizon is hundreds of kilometres away. Use a
    // broad local relief patch so Mars reads as terrain rather than a smooth
    // red sphere, while the high-altitude scaled body still owns the view.
    private const float PatchSize = 120_000f; // metres across
    private const int   Grid      = 96;       // subdivisions per side
    private const float ShowAlt   = 12000f;  // m: terrain visible below this altitude

    private MeshInstance3D? _mesh;
    private ShaderMaterial? _material;
    private Vector3 _lastSunDir;
    private float _lastSolarVisibility = float.NaN;

    public override void _Ready()
    {
        // Earth is the normal entry body. Building a 96x96 procedural mesh here would
        // spend the first visible frame doing synchronous terrain work for a planet the
        // player has not reached. Keep the node inert until Mars is actually near.
        Visible = false;
    }

    private void EnsureMesh()
    {
        if (_mesh != null) return;

        _mesh = new MeshInstance3D { Name = "MarsSurface", Mesh = BuildMesh() };
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/mars_terrain.gdshader")
        };
        _material.SetShaderParameter("solar_visibility", 1.0f);
        _material.SetShaderParameter("night_floor", 0.006f);
        _mesh.SetSurfaceOverrideMaterial(0, _material);
        AddChild(_mesh);
        GD.Print($"PERF_RENDER stage=mars_terrain_build grid={Grid} vertices={Grid * Grid * 6}");
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { Visible = false; return; }

        var mars = universe.GetBody("mars");
        if (mars == null) { Visible = false; return; }

        var body = universe.GetDominantBody(vessel.Position);
        double alt = vessel.GetAltitude(mars);
        if (body.Id != "mars" || alt > ShowAlt) { Visible = false; return; }

        EnsureMesh();
        Visible = true;

        if (_material != null)
        {
            float visibility = SunController.SolarVisibility;
            if (float.IsNaN(_lastSolarVisibility)
                || Mathf.Abs(visibility - _lastSolarVisibility) > 1e-4f)
            {
                _material.SetShaderParameter("solar_visibility", visibility);
                _lastSolarVisibility = visibility;
            }

            var sun = universe.GetBody("sun");
            if (sun != null)
            {
                var physicalDirection = (sun.Position - vessel.Position).Normalized;
                var visualDirection = SunController.Instance != null
                    ? SunController.Instance.GetVisualSunDirection(
                        mars, vessel.Position, physicalDirection)
                    : physicalDirection;
                var sunDir = new Vector3(
                    (float)visualDirection.X,
                    (float)visualDirection.Y,
                    (float)visualDirection.Z).Normalized();
                if (_lastSunDir == Vector3.Zero
                    || _lastSunDir.DistanceSquaredTo(sunDir) > 1e-10f)
                {
                    _material.SetShaderParameter("sun_dir", sunDir);
                    _lastSunDir = sunDir;
                }
            }
        }

        // Surface point directly beneath the vessel, expressed relative to the vessel
        // (which sits at the render origin under FloatingOrigin).
        var up      = (vessel.Position - mars.Position).Normalized;
        var surfacePos = mars.Position + up * mars.Radius;
        var offset  = surfacePos - vessel.Position;
        var renderUp = new Vector3((float)up.X, (float)up.Y, (float)up.Z);

        // Orient the patch (default +Y normal) so it lies tangent to the surface.
        var basis = AlignUp(renderUp);
        GlobalTransform = new Transform3D(basis,
            new Vector3((float)offset.X, (float)offset.Y, (float)offset.Z));
    }

    private static Basis AlignUp(Vector3 up)
    {
        up = up.Normalized();
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Right)) < 0.9f ? Vector3.Right : Vector3.Forward;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        return new Basis(x, up, z);
    }

    private static ArrayMesh BuildMesh()
    {
        var noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.00008f,
            FractalOctaves = 6,
        };
        var detail = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.0008f,
            FractalOctaves = 3,
        };

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float step = PatchSize / Grid;
        float half = PatchSize * 0.5f;

        float Height(float x, float z) =>
            noise.GetNoise2D(x, z) * 520f + detail.GetNoise2D(x, z) * 70f;

        for (int j = 0; j < Grid; j++)
        {
            for (int i = 0; i < Grid; i++)
            {
                float x0 = -half + i * step, x1 = x0 + step;
                float z0 = -half + j * step, z1 = z0 + step;

                Vector3 a = new(x0, Height(x0, z0), z0);
                Vector3 b = new(x1, Height(x1, z0), z0);
                Vector3 c = new(x1, Height(x1, z1), z1);
                Vector3 d = new(x0, Height(x0, z1), z1);

                AddTri(st, a, b, c);
                AddTri(st, a, c, d);
            }
        }

        st.GenerateNormals();
        return st.Commit();
    }

    private static void AddTri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        Tint(st, a); st.AddVertex(a);
        Tint(st, b); st.AddVertex(b);
        Tint(st, c); st.AddVertex(c);
    }

    private static void Tint(SurfaceTool st, Vector3 v)
    {
        // Subtle rust variation by elevation: darker in lows, lighter dusty ridges.
        float t = Mathf.Clamp(v.Y / 240f * 0.5f + 0.5f, 0f, 1f);
        st.SetColor(new Color(0.40f, 0.20f, 0.12f).Lerp(new Color(0.78f, 0.48f, 0.32f), t));
    }
}
