namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Presentation-only reconstruction of the Boca Chica surroundings from public
/// geospatial data. The simulation datum and the hero OLM remain owned by
/// LaunchPadController; this layer supplies the surrounding roads, water,
/// wetlands, buildings, storage tanks and a restrained 3DEP relief mesh.
/// </summary>
public partial class LaunchPadController
{
    private const string StarbaseOpenMapPath = "res://data/launch_sites/starbase_openmap.json";
    private const string StarbaseReliefPath = "res://data/launch_sites/starbase_3dep_relief.json";

    private const float DepReliefCentreX = 67f;
    private const float DepReliefCentreZ = 0f;
    private const float DepReliefScale = 0.55f;
    // Far-field rim/alpha: do not drop the rim. Raise HeroDepReliefPeakAlpha only
    // if T-0 loses too much centre relief; keep DepReliefPeakAlpha at 0.08.
    private const float DepReliefRimStart = 0.62f;
    private const float DepReliefPeakAlpha = 0.08f;
    private const float HeroDepReliefPeakAlpha = DepReliefPeakAlpha;

    /// <summary>
    /// Hero 3DEP / OSM land-cover overlays read as a cookie once the chase
    /// camera leaves the pad. Civil apron already fades 150–700 m; this wider
    /// band retires the mapped campus before the 10 km pad ceiling. Far-field
    /// keeps its own exclusive LOD after that.
    /// </summary>
    public const float HeroGeospatialFadeLowM = 1_000f;
    public const float HeroGeospatialFadeHighM = 8_000f;

    private readonly List<MeshInstance3D> _heroGeospatialFadeMeshes = new();
    private bool _heroGeospatialFadeCollected;
    private float _lastHeroGeospatialHide = float.NaN;

    private void BuildStarbaseGeospatialContext()
    {
        if (!FileAccess.FileExists(StarbaseOpenMapPath))
        {
            GD.PushWarning($"[STARBASE_GEO] Missing {StarbaseOpenMapPath}");
            return;
        }

        var file = FileAccess.Open(StarbaseOpenMapPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"[STARBASE_GEO] Could not open {StarbaseOpenMapPath}");
            return;
        }

        string json = file.GetAsText();
        file.Close();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
                return;

            var road = FarContextMat(new Color(0.075f, 0.080f, 0.075f), 0.92f, 0.0f, 0.008f, 0.46f);
            var wetSand = FarContextMat(new Color(0.29f, 0.27f, 0.20f), 0.96f, 0.0f, 0.004f, 0.30f);
            var wetland = FarContextMat(new Color(0.20f, 0.24f, 0.17f), 0.99f, 0.0f, 0.012f, 0.36f);
            var water = FarContextMat(new Color(0.035f, 0.16f, 0.20f), 0.98f, 0.0f, 0.025f, 0.58f);
            var building = Mat(new Color(0.22f, 0.21f, 0.19f), 0.92f, 0.08f);
            var roof = Mat(new Color(0.10f, 0.11f, 0.11f), 0.88f, 0.22f);
            var tank = Mat(new Color(0.47f, 0.48f, 0.47f), 0.52f, 0.78f);
            var tower = Mat(new Color(0.42f, 0.44f, 0.46f), 0.58f, 0.82f);

            int roads = 0, polygons = 0, buildings = 0, tanks = 0;
            foreach (var feature in features.EnumerateArray())
            {
                string kind = StringValue(feature, "kind");
                string id = StringValue(feature, "id");
                switch (kind)
                {
                    case "road":
                        roads += BuildGeospatialRoad(feature, road);
                        break;
                    case "coastline":
                        roads += BuildGeospatialCoastline(feature, wetSand);
                        break;
                    case "water":
                        polygons += BuildGeospatialPolygon(feature, "GeoWater_", water,
                            GradeY + 0.075f * U, 0.08f);
                        break;
                    case "wetland":
                        polygons += BuildGeospatialPolygon(feature, "GeoWetland_", wetland,
                            GradeY + 0.105f * U, 0.05f);
                        break;
                    case "yard":
                        // The expansion yard is intentionally excluded from this first
                        // visual layer: it covers the hero apron in OSM and would hide
                        // the authored launch-surface detail. The tank-farm yard remains
                        // a useful, non-overlapping site-scale cue.
                        if (StringValue(feature, "name").Contains("Tank Farm", StringComparison.OrdinalIgnoreCase))
                            polygons += BuildGeospatialPolygon(feature, "GeoYard_", wetSand,
                                GradeY + 0.105f * U, 0.06f);
                        break;
                    case "building":
                        if (BuildGeospatialBuilding(feature, building, roof, tower))
                            buildings++;
                        break;
                    case "tank":
                        if (BuildGeospatialTank(feature, tank, tower))
                            tanks++;
                        break;
                }
            }

            BuildStarbase3DepRelief();
            Vector2 originOffsetM = ApplyActiveSiteGeoOriginOffset();
            string originSite = SimulationBridge.Instance?.LaunchSiteOrNull?.Id ?? "unknown";
            GD.Print($"[STARBASE_GEO] roads={roads} polygons={polygons} buildings={buildings} tanks={tanks} " +
                $"source=OSM+3DEP originSite={originSite} offsetX={originOffsetM.X:F1} offsetZ={originOffsetM.Y:F1}");
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[STARBASE_GEO] Invalid geospatial data: {ex.Message}");
        }
    }

    /// <summary>
    /// OSM/3DEP features are exported relative to the orbital-site origin. Pad-2 is a
    /// separate WGS84 launch datum, so translate the whole presentation layer into the
    /// active site's ENU frame after construction. The physics launch position remains
    /// owned by LaunchSite and is never modified here.
    /// </summary>
    private static Vector2 ActiveSiteGeoOriginOffsetM()
    {
        var site = SimulationBridge.Instance?.LaunchSiteOrNull;
        if (site == null)
            return Vector2.Zero;

        const double sourceLatitude = 25.9972;
        const double sourceLongitude = -97.1566;
        const double metresPerDegreeLatitude = 111_132.92;
        double metresPerDegreeLongitude = 111_412.84
            * System.Math.Cos(sourceLatitude * System.Math.PI / 180.0);

        float eastM = (float)((site.Longitude - sourceLongitude) * metresPerDegreeLongitude);
        // Local +Z is south, hence northward latitude deltas become negative Z.
        float southM = (float)(-(site.Latitude - sourceLatitude) * metresPerDegreeLatitude);
        return new Vector2(eastM, southM);
    }

    private Vector2 ApplyActiveSiteGeoOriginOffset()
    {
        Vector2 offsetM = ActiveSiteGeoOriginOffsetM();
        if (offsetM == Vector2.Zero)
            return offsetM;

        Vector3 offset = new(offsetM.X * U, 0f, offsetM.Y * U);
        foreach (Node child in GetChildren())
        {
            string name = child.Name.ToString();
            if (child is Node3D node
                && (name.StartsWith("Geo", StringComparison.Ordinal)
                    || name == "Starbase3DepRelief"))
                node.Position += offset;
        }
        return offsetM;
    }

    private int BuildGeospatialRoad(JsonElement feature, StandardMaterial3D material)
    {
        var points = ReadPoints(feature);
        float widthM = Mathf.Clamp(Number(feature, "widthM", 8f), 4f, 18f);
        int built = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float lengthM = a.DistanceTo(b);
            if (lengthM < 1f || lengthM > 500f || !NearRenderContext(a, b, 900f))
                continue;

            Vector2 mid = (a + b) * 0.5f;
            float yaw = -Mathf.RadToDeg(Mathf.Atan2(b.Y - a.Y, b.X - a.X));
            SpawnRot($"GeoRoad_{StringValue(feature, "id")}_{i}",
                new BoxMesh { Size = new Vector3(lengthM * U, 0.11f * U, widthM * U) },
                material,
                new Vector3(mid.X * U, GradeY + 0.16f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    private int BuildGeospatialCoastline(JsonElement feature,
        StandardMaterial3D shoreMaterial)
    {
        var points = ReadPoints(feature);
        int built = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i], b = points[i + 1];
            float lengthM = a.DistanceTo(b);
            if (lengthM < 1f || lengthM > 600f || !NearRenderContext(a, b, 1100f))
                continue;

            Vector2 dir = (b - a).Normalized();
            Vector2 mid = (a + b) * 0.5f;
            float yaw = -Mathf.RadToDeg(Mathf.Atan2(dir.Y, dir.X));
            SpawnRot($"GeoShore_{StringValue(feature, "id")}_{i}",
                // The mapped coastline is a reference line, not an elevated seawall.
                // EarthGround already owns the broad ocean/coast radiance and the OSM
                // water polygons provide the measured local water footprints.
                new BoxMesh { Size = new Vector3(lengthM * U, 0.025f * U, 2.2f * U) },
                shoreMaterial,
                new Vector3(mid.X * U, GradeY + 0.12f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    private int BuildGeospatialPolygon(JsonElement feature, string prefix,
        StandardMaterial3D material, float topY, float depthM)
    {
        var points = ReadPoints(feature);
        if (points.Count < 3)
            return 0;

        var mesh = BuildExtrudedPolygon(points, topY, depthM * U);
        if (mesh == null)
            return 0;

        var node = Spawn(prefix + StringValue(feature, "id"), mesh, material, Vector3.Zero);
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return 1;
    }

    private bool BuildGeospatialBuilding(JsonElement feature,
        StandardMaterial3D material, StandardMaterial3D roof, StandardMaterial3D towerMaterial)
    {
        float heightM = Mathf.Clamp(Number(feature, "heightM", 4f), 2f, 160f);
        float widthM = Mathf.Clamp(Number(feature, "widthM", 8f), 2f, 90f);
        float depthM = Mathf.Clamp(Number(feature, "depthM", 8f), 2f, 90f);
        float x = Number(feature, "x");
        float z = Number(feature, "z");
        string id = StringValue(feature, "id");
        string name = StringValue(feature, "name");

        // OSM has one historical/semantic Flame Trench way carrying a bogus building
        // height. It is already authored by LaunchPadController and is not a building.
        if (name.Contains("Flame Trench", StringComparison.OrdinalIgnoreCase))
            return false;

        if (heightM >= 100f && name.Contains("Integration Tower", StringComparison.OrdinalIgnoreCase))
        {
            BuildGeospatialTower(id, x, z, widthM, depthM, heightM, towerMaterial);
            return true;
        }

        Spawn($"GeoBuilding_{id}",
            new BoxMesh { Size = new Vector3(widthM * U, heightM * U, depthM * U) },
            material,
            new Vector3(x * U, GradeY + heightM * 0.5f * U, z * U));
        Spawn($"GeoBuildingRoof_{id}",
            new BoxMesh { Size = new Vector3((widthM + 0.6f) * U, 0.22f * U, (depthM + 0.6f) * U) },
            roof,
            new Vector3(x * U, GradeY + (heightM + 0.11f) * U, z * U));
        return true;
    }

    private void BuildGeospatialTower(string id, float x, float z,
        float widthM, float depthM, float heightM, StandardMaterial3D material)
    {
        float halfX = Mathf.Min(widthM, 18f) * 0.5f * U;
        float halfZ = Mathf.Min(depthM, 18f) * 0.5f * U;
        float height = heightM * U;
        float column = 0.65f * U;
        float[] cx = { -halfX, halfX, halfX, -halfX };
        float[] cz = { -halfZ, -halfZ, halfZ, halfZ };
        for (int i = 0; i < 4; i++)
            Spawn($"GeoTower_{id}_Column{i}",
                new BoxMesh { Size = new Vector3(column, height, column) }, material,
                new Vector3(x * U + cx[i], GradeY + height * 0.5f, z * U + cz[i]));

        int levels = Mathf.Clamp(Mathf.RoundToInt(heightM / 12f), 8, 16);
        for (int level = 0; level <= levels; level++)
        {
            float y = GradeY + height * level / levels;
            Spawn($"GeoTower_{id}_RailX{level}",
                new BoxMesh { Size = new Vector3(halfX * 2f, 0.26f * U, 0.26f * U) },
                material, new Vector3(x * U, y, z * U - halfZ));
            Spawn($"GeoTower_{id}_RailZ{level}",
                new BoxMesh { Size = new Vector3(0.26f * U, 0.26f * U, halfZ * 2f) },
                material, new Vector3(x * U - halfX, y, z * U));
        }
    }

    private bool BuildGeospatialTank(JsonElement feature,
        StandardMaterial3D bodyMaterial, StandardMaterial3D supportMaterial)
    {
        float x = Number(feature, "x");
        float z = Number(feature, "z");
        float lengthM = Mathf.Clamp(Number(feature, "lengthM", 48f), 20f, 70f);
        // The mapped minor footprint axis includes bund/orthophoto tolerance. Cap the
        // visual shell to the observed cryogenic bullet-tank scale instead of making a
        // 16 m diameter tank that would dwarf the OLM.
        float diameterM = Mathf.Clamp(Number(feature, "diameterM", 8f), 5.5f, 7.5f);
        float radius = diameterM * 0.5f * U;
        float length = lengthM * U;
        string id = StringValue(feature, "id");
        float yaw = Number(feature, "yawDeg", -14f);

        SpawnRot($"GeoTank_{id}",
            new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 18,
            },
            bodyMaterial,
            new Vector3(x * U, GradeY + radius + 0.35f * U, z * U),
            new Vector3(0f, yaw, 90f));

        // Two low saddles make the horizontal orientation legible from the pad camera.
        for (int i = -1; i <= 1; i += 2)
            Spawn($"GeoTank_{id}_Saddle{i}",
                new BoxMesh { Size = new Vector3(1.2f * U, 2.0f * U, 2.2f * U) },
                supportMaterial,
                new Vector3((x + i * lengthM * 0.27f) * U, GradeY + 1.0f * U, z * U));
        return true;
    }

    private void BuildStarbase3DepRelief()
    {
        var mesh = BuildFaded3DepReliefMesh(HeroDepReliefPeakAlpha);
        if (mesh == null)
            return;
        var node = Spawn("Starbase3DepRelief", mesh, CreateDepReliefMaterial(), Vector3.Zero);
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    /// <summary>
    /// Shared USGS 3DEP tile for hero and far-field. Vertex colours carry the
    /// radial rim and the restrained sand/olive alpha so the source raster
    /// never reads as an opaque wetland square.
    /// </summary>
    private ArrayMesh? BuildFaded3DepReliefMesh(float peakAlpha)
    {
        if (!FileAccess.FileExists(StarbaseReliefPath))
            return null;

        var file = FileAccess.Open(StarbaseReliefPath, FileAccess.ModeFlags.Read);
        if (file == null)
            return null;

        string json = file.GetAsText();
        file.Close();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var grid = root.GetProperty("grid");
            int columns = grid.GetProperty("columns").GetInt32();
            int rows = grid.GetProperty("rows").GetInt32();
            if (columns < 2 || rows < 2)
                return null;

            float stepX = grid.GetProperty("stepM")[0].GetSingle();
            float stepZ = grid.GetProperty("stepM")[1].GetSingle();
            var values = root.GetProperty("valuesM");
            float baseY = GradeY + 0.10f * U;
            float halfColumns = (columns - 1) * 0.5f;
            float halfRows = (rows - 1) * 0.5f;

            Vector3 Vertex(int row, int column)
            {
                float x = DepReliefCentreX + (column - halfColumns) * stepX;
                float z = DepReliefCentreZ + (halfRows - row) * stepZ;
                float y = baseY + values[row][column].GetSingle() * DepReliefScale * U;
                return new Vector3(x * U, y, z * U);
            }

            Color VertexColor(int row, int column)
            {
                float edgeX = Mathf.Abs(column - halfColumns) / Mathf.Max(halfColumns, 1f);
                float edgeZ = Mathf.Abs(row - halfRows) / Mathf.Max(halfRows, 1f);
                float edge = 1f - FarSmoothstep(DepReliefRimStart, 1.0f, Mathf.Max(edgeX, edgeZ));
                float elevation = values[row][column].GetSingle();
                float tone = Mathf.Clamp((elevation + 0.85f) / 1.70f, 0f, 1f);
                return new Color(
                    Mathf.Lerp(0.48f, 0.66f, tone),
                    Mathf.Lerp(0.38f, 0.52f, tone),
                    Mathf.Lerp(0.25f, 0.38f, tone),
                    // The regional DEM is a restrained grade cue over EarthGround,
                    // not an opaque replacement surface.
                    edge * peakAlpha);
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
                AddFadedReliefTriangle(st, a, b, c,
                    VertexColor(row, column), VertexColor(row, column + 1),
                    VertexColor(row + 1, column + 1));
                AddFadedReliefTriangle(st, a, c, d,
                    VertexColor(row, column), VertexColor(row + 1, column + 1),
                    VertexColor(row + 1, column));
            }

            st.GenerateNormals();
            return st.Commit();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[STARBASE_GEO] Invalid 3DEP relief: {ex.Message}");
            return null;
        }
    }

    private readonly LaunchSurfaceFade _heroSurfaceFade = new();

    private void UpdateHeroGeospatialFade()
    {
        if (!IsStarbaseSite)
            return;
        if (!_heroGeospatialFadeCollected)
        {
            CollectHeroGeospatialFadeMeshes();
            _heroGeospatialFadeCollected = true;
        }
        if (_heroGeospatialFadeMeshes.Count == 0)
            return;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var earth = bridge?.Universe?.GetBody("earth");
        bool activeEarth = vessel != null && earth != null && vessel.ReferenceBodyId == earth.Id;
        double vesselAlt = activeEarth ? vessel!.GetAltitude(earth!) : double.PositiveInfinity;
        double altitude = activeEarth
            ? System.Math.Max(vesselAlt, FloatingOrigin.CameraAltOverEarth)
            : FloatingOrigin.CameraAltOverEarth;
        float hide = FarSmoothstep(HeroGeospatialFadeLowM, HeroGeospatialFadeHighM, (float)altitude);
        if (_lastHeroGeospatialHide == hide)
            return;

        _lastHeroGeospatialHide = hide;
        for (int i = _heroGeospatialFadeMeshes.Count - 1; i >= 0; i--)
        {
            var mesh = _heroGeospatialFadeMeshes[i];
            if (mesh == null || !IsInstanceValid(mesh))
            {
                _heroGeospatialFadeMeshes.RemoveAt(i);
                continue;
            }
            _heroSurfaceFade.Apply(mesh, 1f - hide);
        }
    }

    private void CollectHeroGeospatialFadeMeshes()
    {
        _heroGeospatialFadeMeshes.Clear();
        foreach (Node child in GetChildren())
        {
            if (child is not MeshInstance3D mesh)
                continue;
            string name = mesh.Name.ToString();
            if (name == "Starbase3DepRelief"
                || name.StartsWith("GeoRoad_", StringComparison.Ordinal)
                || name.StartsWith("GeoShore_", StringComparison.Ordinal)
                || name.StartsWith("GeoWetland_", StringComparison.Ordinal)
                || name.StartsWith("GeoWater_", StringComparison.Ordinal)
                || name.StartsWith("GeoYard_", StringComparison.Ordinal))
                _heroGeospatialFadeMeshes.Add(mesh);
        }
    }

    private static void AddReliefTriangle(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c)
    {
        // Godot front faces are clockwise. A positive conventional cross product
        // points up but makes this surface back-facing when viewed from above.
        if ((b - a).Cross(c - a).Y > 0f)
            (b, c) = (c, b);
        st.AddVertex(a);
        st.AddVertex(b);
        st.AddVertex(c);
    }

    private static ArrayMesh? BuildExtrudedPolygon(IReadOnlyList<Vector2> source,
        float topY, float depth)
    {
        var points = new List<Vector2>(source.Count);
        foreach (var point in source)
        {
            if (points.Count == 0 || points[^1].DistanceTo(point) > 0.05f)
                points.Add(point);
        }
        if (points.Count > 2 && points[0].DistanceTo(points[^1]) < 0.05f)
            points.RemoveAt(points.Count - 1);
        if (points.Count < 3)
            return null;

        var polygon = points.ToArray();
        var indices = Geometry2D.TriangulatePolygon(polygon);
        if (indices.Length < 3)
            return null;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Top(Vector2 p) => new(p.X * U, topY, p.Y * U);
        Vector3 Bottom(Vector2 p) => new(p.X * U, topY - depth, p.Y * U);

        for (int i = 0; i + 2 < indices.Length; i += 3)
            AddReliefTriangle(st, Top(points[indices[i]]), Top(points[indices[i + 1]]), Top(points[indices[i + 2]]));

        for (int i = 0; i < points.Count; i++)
        {
            int next = (i + 1) % points.Count;
            Vector3 a = Top(points[i]), b = Top(points[next]);
            Vector3 c = Bottom(points[next]), d = Bottom(points[i]);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        }

        st.GenerateNormals();
        return st.Commit();
    }

    private static List<Vector2> ReadPoints(JsonElement feature)
    {
        var points = new List<Vector2>();
        if (!feature.TryGetProperty("points", out var array)
            || array.ValueKind != JsonValueKind.Array)
            return points;
        foreach (var pair in array.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                continue;
            points.Add(new Vector2(pair[0].GetSingle(), pair[1].GetSingle()));
        }
        return points;
    }

    private static bool NearRenderContext(Vector2 a, Vector2 b, float limitM)
    {
        return Mathf.Max(a.Abs().X, a.Abs().Y) <= limitM
            || Mathf.Max(b.Abs().X, b.Abs().Y) <= limitM;
    }

    private static float Number(JsonElement element, string name, float fallback = 0f)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetSingle(out float number)
            ? number
            : fallback;
    }

    private static string StringValue(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "unknown"
            : "unknown";
    }
}
