namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Low-cost contextual LOD for Starbase. The detailed pad is intentionally local;
/// this sibling root preserves the launch site's silhouette after the hero geometry
/// is hidden, without keeping every lattice brace alive into the orbital camera.
/// Its opacity is coupled to the Earth globe handoff so the site remains legible while
/// the detailed pad leaves the camera's local scale.
/// </summary>
public partial class LaunchPadController
{
    public bool FarFieldVisible => _starbaseFarFieldRoot?.Visible == true;
    public float FarFieldOpacity => float.IsNaN(_lastFarFieldOpacity) ? 0f : _lastFarFieldOpacity;

    private Node3D? _starbaseFarFieldRoot;
    private readonly List<MeshInstance3D> _starbaseFarFieldMeshes = new();
    private bool? _lastFarFieldVisible;
    private float _lastFarFieldOpacity = float.NaN;
    private bool _farFieldUsesMappedContext;

    private void BuildStarbaseFarField()
    {
        if (!IsStarbaseSite || GetParent() is not Node3D world)
            return;

        _starbaseFarFieldRoot = new Node3D { Name = "StarbaseFarField", Visible = false };
        world.AddChild(_starbaseFarFieldRoot);

        var land = Mat(new Color(0.18f, 0.25f, 0.16f), 0.98f, 0.0f);
        var hardstand = Mat(new Color(0.25f, 0.26f, 0.24f), 0.96f, 0.0f);
        var road = Mat(new Color(0.055f, 0.065f, 0.060f), 0.96f, 0.0f);
        var water = Mat(new Color(0.035f, 0.15f, 0.18f), 0.88f, 0.0f);
        var steel = Mat(new Color(0.34f, 0.37f, 0.37f), 0.72f, 0.55f);
        var roof = Mat(new Color(0.11f, 0.12f, 0.12f), 0.90f, 0.15f);

        AddFarMesh("Footprint", BuildFootprintMesh(new Vector2[]
        {
            new(-760f, -520f), new(-540f, -720f), new(80f, -690f),
            new(520f, -560f), new(760f, -230f), new(690f, 330f),
            new(360f, 610f), new(-210f, 650f), new(-690f, 430f),
        }), land, new Vector3(-20f * U, GradeY + 0.04f * U, 20f * U));

        // A hardstand-shaped polygon is more useful at altitude than a second rectangle:
        // it reads as the pad island while leaving the surrounding wetland visible.
        AddFarMesh("LaunchHardstand", BuildFootprintMesh(new Vector2[]
        {
            new(-160f, -125f), new(55f, -150f), new(180f, -82f),
            new(170f, 105f), new(45f, 145f), new(-175f, 110f),
        }), hardstand, new Vector3(0f, GradeY + 0.10f * U, 0f));

        AddFarMesh("CoastalWater", BuildFootprintMesh(new Vector2[]
        {
            new(690f, -560f), new(860f, -440f), new(860f, 500f),
            new(700f, 620f), new(635f, 280f), new(670f, -180f),
        }), water, new Vector3(0f, GradeY + 0.075f * U, 0f));

        _farFieldUsesMappedContext = BuildMappedFarFieldContext(road, water, land, steel, roof);
        if (!_farFieldUsesMappedContext)
        {
        AddFarRotated("Highway4", new BoxMesh { Size = new Vector3(18f * U, 0.08f * U, 1500f * U) },
            road, new Vector3(-470f * U, GradeY + 0.15f * U, 30f * U), new Vector3(0f, -5f, 0f));
        AddFarMesh("NorthServiceRoad", new BoxMesh { Size = new Vector3(760f * U, 0.08f * U, 14f * U) },
            road, new Vector3(-120f * U, GradeY + 0.16f * U, 270f * U));
        AddFarMesh("TankServiceRoad", new BoxMesh { Size = new Vector3(520f * U, 0.08f * U, 14f * U) },
            road, new Vector3(290f * U, GradeY + 0.16f * U, 80f * U));

        // One strong tower silhouette and a compact tank farm anchor the site from
        // 3–75 km. Their proportions are deliberately real-world, not billboard scale.
        float towerX = (float)Spec.OlitEast;
        float towerH = (float)Spec.OlitHeight;
        foreach (float dx in new[] { -7f, 7f })
        foreach (float dz in new[] { -7f, 7f })
            AddFarMesh("TowerColumn", new BoxMesh { Size = new Vector3(1.8f * U, towerH * U, 1.8f * U) },
                steel, new Vector3((towerX + dx) * U, GradeY + towerH * 0.5f * U, dz * U));
        foreach (float heightFraction in new[] { 0.30f, 0.58f, 0.84f })
            AddFarMesh("TowerCrossbar", new BoxMesh { Size = new Vector3(16f * U, 1.1f * U, 1.1f * U) },
                steel, new Vector3(towerX * U, GradeY + towerH * heightFraction * U, 0f));

        float tankRadius = 4.2f;
        float tankHeight = (float)Spec.CommodityTankMaxHeight;
        for (int i = 0; i < 6; i++)
        {
            // Keep the far-field tanks on the same local datum as LaunchPadController's
            // hero farm (58 m east, 48 m south). A previous synthetic cluster at
            // 205–273 m and 92 m high popped to a second, oversized tank farm on LOD swap.
            float x = 58f + (i % 3) * 14f;
            float z = 48f + (i / 3) * 14f;
            AddFarMesh("TankFarmTank", new CylinderMesh
                { TopRadius = tankRadius * U, BottomRadius = tankRadius * U,
                  Height = tankHeight * U, RadialSegments = 12 },
                steel, new Vector3(x * U, GradeY + tankHeight * 0.5f * U, z * U));
            AddFarMesh("TankFarmRoof", new SphereMesh
                { Radius = tankRadius * U, Height = tankRadius * U, IsHemisphere = true,
                  RadialSegments = 12, Rings = 4 },
                steel, new Vector3(x * U, GradeY + tankHeight * U, z * U));
        }

        foreach (var (x, z, width, depth, height) in new[]
        {
            (-315f, 230f, 110f, 58f, 10f), (-140f, 250f, 92f, 52f, 9f),
            (-260f, -250f, 140f, 70f, 12f), (360f, -190f, 135f, 76f, 12f),
        })
        {
            AddFarMesh("SupportBuilding", new BoxMesh
                { Size = new Vector3(width * U, height * U, depth * U) },
                roof, new Vector3(x * U, GradeY + height * 0.5f * U, z * U));
        }
        }
    }

    /// <summary>
    /// Builds a deliberately simplified copy of the mapped Starbase context. The hero
    /// scene remains responsible for close inspection; this copy keeps the same OSM
    /// footprints and 3DEP relief through the 12–40 km local-ground handoff instead of
    /// swapping to a separately authored road/tank layout.
    /// </summary>
    private bool BuildMappedFarFieldContext(StandardMaterial3D road,
        StandardMaterial3D water, StandardMaterial3D land,
        StandardMaterial3D steel, StandardMaterial3D roof)
    {
        if (!FileAccess.FileExists(StarbaseOpenMapPath))
            return false;

        var file = FileAccess.Open(StarbaseOpenMapPath, FileAccess.ModeFlags.Read);
        if (file == null)
            return false;

        string json = file.GetAsText();
        file.Close();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                return false;

            int built = 0;
            foreach (var feature in features.EnumerateArray())
            {
                string kind = StringValue(feature, "kind");
                switch (kind)
                {
                    case "road":
                        built += BuildMappedFarRoad(feature, road);
                        break;
                    case "coastline":
                        built += BuildMappedFarCoastline(feature, water, land);
                        break;
                    case "water":
                    case "wetland":
                    case "yard":
                        built += BuildMappedFarPolygon(feature, kind, water, land);
                        break;
                    case "building":
                        built += BuildMappedFarBuilding(feature, steel, roof);
                        break;
                    case "tank":
                        built += BuildMappedFarTank(feature, steel);
                        break;
                }
            }

            built += BuildMappedFarRelief(land);
            return built > 0;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[STARBASE_FAR] Invalid mapped context: {ex.Message}");
            return false;
        }
    }

    private int BuildMappedFarRoad(JsonElement feature, StandardMaterial3D material)
    {
        var points = ReadPoints(feature);
        float widthM = Mathf.Clamp(Number(feature, "widthM", 8f), 4f, 18f);
        int built = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float lengthM = a.DistanceTo(b);
            if (lengthM < 4f || lengthM > 800f || !NearRenderContext(a, b, 2_600f))
                continue;
            Vector2 mid = (a + b) * 0.5f;
            float yaw = -Mathf.RadToDeg(Mathf.Atan2(b.Y - a.Y, b.X - a.X));
            AddFarRotated($"MappedRoad_{StringValue(feature, "id")}_{i}",
                new BoxMesh { Size = new Vector3(lengthM * U, 0.08f * U, widthM * U) },
                material,
                new Vector3(mid.X * U, GradeY + 0.16f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    private int BuildMappedFarCoastline(JsonElement feature,
        StandardMaterial3D water, StandardMaterial3D shore)
    {
        var points = ReadPoints(feature);
        int built = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float lengthM = a.DistanceTo(b);
            if (lengthM < 4f || lengthM > 800f || !NearRenderContext(a, b, 2_600f))
                continue;
            Vector2 dir = (b - a).Normalized();
            Vector2 mid = (a + b) * 0.5f;
            float yaw = -Mathf.RadToDeg(Mathf.Atan2(dir.Y, dir.X));
            string id = StringValue(feature, "id");
            AddFarRotated($"MappedShore_{id}_{i}",
                new BoxMesh { Size = new Vector3(lengthM * U, 0.06f * U, 6f * U) },
                shore,
                new Vector3(mid.X * U, GradeY + 0.18f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            Vector2 seaMid = mid + new Vector2(70f, 0f);
            AddFarRotated($"MappedSea_{id}_{i}",
                new BoxMesh { Size = new Vector3(lengthM * U, 0.035f * U, 140f * U) },
                water,
                new Vector3(seaMid.X * U, GradeY + 0.07f * U, seaMid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    private int BuildMappedFarPolygon(JsonElement feature, string kind,
        StandardMaterial3D water, StandardMaterial3D land)
    {
        var points = ReadPoints(feature);
        var material = kind == "water" ? water : land;
        var mesh = BuildExtrudedPolygon(points, GradeY + 0.09f * U, 0.06f * U);
        if (mesh == null)
            return 0;
        AddFarMesh($"Mapped{kind}_{StringValue(feature, "id")}", mesh, material, Vector3.Zero)
            .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return 1;
    }

    private int BuildMappedFarBuilding(JsonElement feature,
        StandardMaterial3D steel, StandardMaterial3D roof)
    {
        string name = StringValue(feature, "name");
        if (name.Contains("Flame Trench", StringComparison.OrdinalIgnoreCase))
            return 0;
        float heightM = Mathf.Clamp(Number(feature, "heightM", 4f), 2f, 160f);
        float widthM = Mathf.Clamp(Number(feature, "widthM", 8f), 2f, 90f);
        float depthM = Mathf.Clamp(Number(feature, "depthM", 8f), 2f, 90f);
        float x = Number(feature, "x");
        float z = Number(feature, "z");
        string id = StringValue(feature, "id");
        if (heightM >= 100f && name.Contains("Integration Tower", StringComparison.OrdinalIgnoreCase))
        {
            float halfX = Mathf.Min(widthM, 18f) * 0.5f * U;
            float halfZ = Mathf.Min(depthM, 18f) * 0.5f * U;
            float height = heightM * U;
            foreach ((float dx, float dz) in new[]
            {
                (-halfX, -halfZ), (halfX, -halfZ), (halfX, halfZ), (-halfX, halfZ),
            })
                AddFarMesh($"MappedTower_{id}",
                    new BoxMesh { Size = new Vector3(0.65f * U, height, 0.65f * U) },
                    steel,
                    new Vector3(x * U + dx, GradeY + height * 0.5f, z * U + dz));
            for (int level = 1; level <= 4; level++)
                AddFarMesh($"MappedTowerRail_{id}_{level}",
                    new BoxMesh { Size = new Vector3(widthM * U, 0.26f * U, depthM * U) },
                    steel,
                    new Vector3(x * U, GradeY + height * level / 5f, z * U));
            return 1;
        }

        AddFarMesh($"MappedBuilding_{id}",
            new BoxMesh { Size = new Vector3(widthM * U, heightM * U, depthM * U) },
            steel,
            new Vector3(x * U, GradeY + heightM * 0.5f * U, z * U));
        AddFarMesh($"MappedRoof_{id}",
            new BoxMesh { Size = new Vector3((widthM + 0.6f) * U, 0.22f * U, (depthM + 0.6f) * U) },
            roof,
            new Vector3(x * U, GradeY + (heightM + 0.11f) * U, z * U));
        return 1;
    }

    private int BuildMappedFarTank(JsonElement feature, StandardMaterial3D material)
    {
        float x = Number(feature, "x");
        float z = Number(feature, "z");
        float lengthM = Mathf.Clamp(Number(feature, "lengthM", 48f), 20f, 70f);
        float diameterM = Mathf.Clamp(Number(feature, "diameterM", 8f), 5.5f, 7.5f);
        float radius = diameterM * 0.5f * U;
        float yaw = Number(feature, "yawDeg", -14f);
        AddFarRotated($"MappedTank_{StringValue(feature, "id")}",
            new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = lengthM * U,
                RadialSegments = 12,
            },
            material,
            new Vector3(x * U, GradeY + radius + 0.35f * U, z * U),
            new Vector3(0f, yaw, 90f));
        return 1;
    }

    private int BuildMappedFarRelief(StandardMaterial3D material)
    {
        if (!FileAccess.FileExists(StarbaseReliefPath))
            return 0;
        var file = FileAccess.Open(StarbaseReliefPath, FileAccess.ModeFlags.Read);
        if (file == null)
            return 0;
        string json = file.GetAsText();
        file.Close();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var grid = doc.RootElement.GetProperty("grid");
            int columns = grid.GetProperty("columns").GetInt32();
            int rows = grid.GetProperty("rows").GetInt32();
            float stepX = grid.GetProperty("stepM")[0].GetSingle();
            float stepZ = grid.GetProperty("stepM")[1].GetSingle();
            var values = doc.RootElement.GetProperty("valuesM");
            const float centreX = 67f;
            const float centreZ = 0f;
            const float reliefScale = 0.55f;
            float baseY = GradeY + 0.10f * U;
            Vector3 Vertex(int row, int column)
            {
                float x = centreX + (column - (columns - 1) * 0.5f) * stepX;
                float z = centreZ + ((rows - 1) * 0.5f - row) * stepZ;
                float y = baseY + values[row][column].GetSingle() * reliefScale * U;
                return new Vector3(x * U, y, z * U);
            }
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            for (int row = 0; row < rows - 1; row++)
            for (int column = 0; column < columns - 1; column++)
            {
                Vector3 a = Vertex(row, column);
                Vector3 b = Vertex(row, column + 1);
                Vector3 c = Vertex(row + 1, column + 1);
                Vector3 d = Vertex(row + 1, column);
                AddReliefTriangle(st, a, b, c);
                AddReliefTriangle(st, a, c, d);
            }
            st.GenerateNormals();
            var mesh = st.Commit();
            if (mesh == null)
                return 0;
            AddFarMesh("Mapped3DepRelief", mesh, material, Vector3.Zero)
                .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            return 1;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[STARBASE_FAR] Invalid mapped relief: {ex.Message}");
            return 0;
        }
    }

    private void UpdateStarbaseFarField()
    {
        if (_starbaseFarFieldRoot == null)
            return;

        // This root is a sibling of the detailed pad, so parent visibility can hide
        // the hero geometry at 12 km without hiding this contextual LOD as well.
        _starbaseFarFieldRoot.GlobalTransform = GlobalTransform;
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var earth = bridge?.Universe?.GetBody("earth");
        bool activeEarth = vessel != null && earth != null && vessel.ReferenceBodyId == earth.Id;
        double vesselAlt = activeEarth ? vessel!.GetAltitude(earth!) : double.PositiveInfinity;
        double cameraAlt = FloatingOrigin.CameraAltOverEarth;
        double altitude = System.Math.Max(vesselAlt, cameraAlt);
        float opacity = activeEarth && double.IsFinite(altitude)
            ? FarSmoothstep(1_600f, 3_500f, (float)altitude)
                * (1f - FloatingOrigin.EarthGlobeAlpha(altitude))
            : 0f;
        // SimulationBridge owns the hero-pad visibility. Keep the contextual LOD
        // mutually exclusive with it; otherwise a zoomed-out low-altitude shot can
        // render two differently authored Starbases at once.
        bool heroVisible = Visible;
        if (heroVisible)
            opacity = 0f;

        bool visible = opacity > 0.005f;
        _starbaseFarFieldRoot.Visible = visible;
        if (_lastFarFieldVisible != visible)
        {
            _lastFarFieldVisible = visible;
              string source = _farFieldUsesMappedContext ? "OSM+3DEP" : "fallback";
              GD.Print($"[STARBASE_FAR] visible={visible} heroVisible={heroVisible} " +
                  $"source={source} altitude={altitude:F0} opacity={opacity:F2}");
        }
        if (!float.IsNaN(_lastFarFieldOpacity) && Mathf.Abs(_lastFarFieldOpacity - opacity) < 0.01f)
            return;

        _lastFarFieldOpacity = opacity;
        foreach (var mesh in _starbaseFarFieldMeshes)
        {
            if (mesh == null || !IsInstanceValid(mesh)) continue;
            mesh.Transparency = 1f - opacity;
        }
    }

    private MeshInstance3D AddFarMesh(string name, Mesh mesh, StandardMaterial3D material, Vector3 position)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        node.SetSurfaceOverrideMaterial(0, material);
        _starbaseFarFieldRoot!.AddChild(node);
        _starbaseFarFieldMeshes.Add(node);
        return node;
    }

    private MeshInstance3D AddFarRotated(string name, Mesh mesh, StandardMaterial3D material,
        Vector3 position, Vector3 rotationDegrees)
    {
        var node = AddFarMesh(name, mesh, material, position);
        node.RotationDegrees = rotationDegrees;
        return node;
    }

    private static ArrayMesh BuildFootprintMesh(IReadOnlyList<Vector2> points)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 1; i + 1 < points.Count; i++)
        {
            Vector3 a = new(points[0].X * U, 0f, points[0].Y * U);
            Vector3 b = new(points[i].X * U, 0f, points[i].Y * U);
            Vector3 c = new(points[i + 1].X * U, 0f, points[i + 1].Y * U);
            if ((b - a).Cross(c - a).Y < 0f) (b, c) = (c, b);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        }
        st.GenerateNormals();
        return st.Commit()!;
    }

    private static float FarSmoothstep(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
