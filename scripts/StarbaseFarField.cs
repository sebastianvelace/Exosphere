namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Low-cost contextual LOD for Starbase. The detailed pad is intentionally local;
/// this sibling root preserves the launch site's silhouette after the hero geometry
/// is hidden, without keeping every lattice brace alive into the orbital camera.
/// Campus OSM/3DEP fades in as the hero geospatial overlay fades out (1–8 km).
/// Civil silhouettes stay exclusive with the hero pad until 10 km so two authored
/// Starbases never stack. Opacity also follows the Earth globe handoff.
/// </summary>
public partial class LaunchPadController
{
    public bool FarFieldVisible => _starbaseFarFieldRoot?.Visible == true;
    public float FarFieldOpacity => float.IsNaN(_lastFarFieldOpacity) ? 0f : _lastFarFieldOpacity;
    public float FarFieldContextOpacity => float.IsNaN(_lastFarFieldContextOpacity) ? 0f : _lastFarFieldContextOpacity;
    public float FarFieldSilhouetteOpacity => float.IsNaN(_lastFarFieldSilhouetteOpacity) ? 0f : _lastFarFieldSilhouetteOpacity;
    public string FarFieldSource => _farFieldUsesMappedContext ? "OSM+EarthGround" : "fallback";

    private Node3D? _starbaseFarFieldRoot;
    private readonly List<MeshInstance3D> _starbaseFarFieldMeshes = new();
    private readonly HashSet<MeshInstance3D> _starbaseFarFieldContextMeshes = new();
    private readonly LaunchSurfaceFade _farSurfaceFade = new();
    private bool? _lastFarFieldVisible;
    private float _lastFarFieldOpacity = float.NaN;
    private float _lastFarFieldContextOpacity = float.NaN;
    private float _lastFarFieldSilhouetteOpacity = float.NaN;
    private bool _farFieldUsesMappedContext;

    private void BuildStarbaseFarField()
    {
        if (!IsStarbaseSite || GetParent() is not Node3D world)
            return;

        _starbaseFarFieldRoot = new Node3D { Name = "StarbaseFarField", Visible = false };
        world.AddChild(_starbaseFarFieldRoot);

        // The local EarthGround patch is unshaded and owns the broad terrain radiance.
        // Far-field vector layers still use StandardMaterial3D so they can cast the
        // tower/tank silhouettes, but a bounded emission floor keeps them readable when
        // the real sun is near the horizon or the capture is in twilight.
        var land = FarMat(new Color(0.24f, 0.28f, 0.20f), 0.98f, 0.0f, 0.04f);
        var hardstand = FarMat(new Color(0.25f, 0.26f, 0.24f), 0.96f, 0.0f, 0.10f);
        // These are contextual land-cover cues. Keep them close to the EarthGround
        // palette so the mapped OSM lines do not become a floating map overlay when
        // the camera crosses the 12–40 km handoff.
        var road = FarContextMat(new Color(0.13f, 0.14f, 0.12f), 0.99f, 0.0f, 0.008f, 0.46f);
        var shore = FarContextMat(new Color(0.20f, 0.22f, 0.18f), 0.99f, 0.0f, 0.004f, 0.30f);
        var water = FarContextMat(new Color(0.055f, 0.14f, 0.16f), 0.98f, 0.0f, 0.025f, 0.58f);
        var wetland = FarContextMat(new Color(0.20f, 0.24f, 0.17f), 0.99f, 0.0f, 0.012f, 0.36f);
        var yard = FarContextMat(new Color(0.29f, 0.28f, 0.23f), 0.98f, 0.0f, 0.025f, 0.54f);
        // Far-field steel is a silhouette cue, not a reflective hero material. A high
        // metallic value mirrored the blue sky and turned the mapped tower cyan.
        var steel = FarMat(new Color(0.42f, 0.43f, 0.40f), 0.88f, 0.08f, 0.24f);
        var roof = FarMat(new Color(0.11f, 0.12f, 0.12f), 0.90f, 0.15f, 0.12f);
        var relief = CreateDepReliefMaterial();

        _farFieldUsesMappedContext = BuildMappedFarFieldContext(
            road, shore, water, wetland, yard, land, steel, roof);
        if (_farFieldUsesMappedContext)
            BuildMappedFarRelief(relief);
        if (!_farFieldUsesMappedContext)
        {
            AddFarContextMesh("Footprint", BuildFootprintMesh(new Vector2[]
            {
                new(-760f, -520f), new(-540f, -720f), new(80f, -690f),
                new(520f, -560f), new(760f, -230f), new(690f, 330f),
                new(360f, 610f), new(-210f, 650f), new(-690f, 430f),
            }), land, new Vector3(-20f * U, GradeY + 0.04f * U, 20f * U));

            // Fallback-only hardstand and water keep non-Starbase/custom builds readable.
            AddFarContextMesh("LaunchHardstand", BuildFootprintMesh(new Vector2[]
            {
                new(-160f, -125f), new(55f, -150f), new(180f, -82f),
                new(170f, 105f), new(45f, 145f), new(-175f, 110f),
            }), hardstand, new Vector3(0f, GradeY + 0.10f * U, 0f));
            AddFarContextMesh("CoastalWater", BuildFootprintMesh(new Vector2[]
            {
                new(690f, -560f), new(860f, -440f), new(860f, 500f),
                new(700f, 620f), new(635f, 280f), new(670f, -180f),
            }), water, new Vector3(0f, GradeY + 0.075f * U, 0f));

            AddFarContextRotated("Highway4", new BoxMesh { Size = new Vector3(18f * U, 0.08f * U, 1500f * U) },
                road, new Vector3(-470f * U, GradeY + 0.15f * U, 30f * U), new Vector3(0f, -5f, 0f));
            AddFarContextMesh("NorthServiceRoad", new BoxMesh { Size = new Vector3(760f * U, 0.08f * U, 14f * U) },
                road, new Vector3(-120f * U, GradeY + 0.16f * U, 270f * U));
            AddFarContextMesh("TankServiceRoad", new BoxMesh { Size = new Vector3(520f * U, 0.08f * U, 14f * U) },
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
    /// footprints and continuous EarthGround context through the 12–40 km local-ground handoff instead of
    /// swapping to a separately authored road/tank layout.
    /// </summary>
    private bool BuildMappedFarFieldContext(StandardMaterial3D road,
        StandardMaterial3D shore, StandardMaterial3D water,
        StandardMaterial3D wetland, StandardMaterial3D yard,
        StandardMaterial3D land,
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
                        built += BuildMappedFarCoastline(feature, shore);
                        break;
                    case "water":
                    case "wetland":
                    case "yard":
                        built += BuildMappedFarPolygon(feature, kind, water, wetland, yard, land);
                        break;
                    case "building":
                        built += BuildMappedFarBuilding(feature, steel, roof);
                        break;
                    case "tank":
                        built += BuildMappedFarTank(feature, steel);
                        break;
                }
            }

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
            AddFarContextRotated($"MappedRoad_{StringValue(feature, "id")}_{i}",
                new BoxMesh { Size = new Vector3(lengthM * U, 0.08f * U, widthM * U) },
                material,
                new Vector3(mid.X * U, GradeY + 0.16f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    private int BuildMappedFarCoastline(JsonElement feature, StandardMaterial3D shore)
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
            AddFarContextRotated($"MappedShore_{id}_{i}",
                // Coastline is a datum cue, not a second elevated seawall. Its narrow
                // footprint prevents the long OSM shoreline from reading as a neon rail.
                new BoxMesh { Size = new Vector3(lengthM * U, 0.025f * U, 2.2f * U) },
                shore,
                new Vector3(mid.X * U, GradeY + 0.12f * U, mid.Y * U),
                new Vector3(0f, yaw, 0f));
            built++;
        }
        return built;
    }

    /// <summary>
    /// Adds the source-derived 3DEP elevation tile underneath the mapped vector
    /// context. Hero and far-field share <see cref="BuildFaded3DepReliefMesh"/> so
    /// the source raster never exposes a square boundary at either LOD.
    /// </summary>
    private int BuildMappedFarRelief(StandardMaterial3D material)
    {
        var mesh = BuildFaded3DepReliefMesh(DepReliefPeakAlpha);
        if (mesh == null)
            return 0;

        var node = AddFarContextMesh("Mapped3DepRelief", mesh, material, Vector3.Zero);
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return 1;
    }

    private int BuildMappedFarPolygon(JsonElement feature, string kind,
        StandardMaterial3D water, StandardMaterial3D wetland,
        StandardMaterial3D yard, StandardMaterial3D land)
    {
        var points = ReadPoints(feature);
        var material = kind switch
        {
            "water" => water,
            "wetland" => wetland,
            "yard" => yard,
            _ => land,
        };
        var mesh = BuildExtrudedPolygon(points, GradeY + 0.09f * U, 0.06f * U);
        if (mesh == null)
            return 0;
        AddFarContextMesh($"Mapped{kind}_{StringValue(feature, "id")}", mesh, material, Vector3.Zero)
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
                AddFarShadowMesh($"MappedTower_{id}",
                    new BoxMesh { Size = new Vector3(0.65f * U, height, 0.65f * U) },
                    steel,
                    new Vector3(x * U + dx, GradeY + height * 0.5f, z * U + dz));
            for (int level = 1; level <= 4; level++)
                AddFarShadowMesh($"MappedTowerRail_{id}_{level}",
                    new BoxMesh { Size = new Vector3(widthM * U, 0.26f * U, depthM * U) },
                    steel,
                    new Vector3(x * U, GradeY + height * level / 5f, z * U));
            return 1;
        }

        AddFarShadowMesh($"MappedBuilding_{id}",
            new BoxMesh { Size = new Vector3(widthM * U, heightM * U, depthM * U) },
            steel,
            new Vector3(x * U, GradeY + heightM * 0.5f * U, z * U));
        AddFarShadowMesh($"MappedRoof_{id}",
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
        var tank = AddFarRotated($"MappedTank_{StringValue(feature, "id")}",
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
        tank.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        return 1;
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
        float globeWeight = activeEarth && double.IsFinite(altitude)
            ? 1f - FloatingOrigin.EarthGlobeAlpha(altitude)
            : 0f;
        // Regional relief, land cover and roads bridge the detailed pad to
        // EarthGround. They may overlap the hero because they carry context, not a
        // second copy of the launch site's civil structures.
        float contextOpacity = activeEarth && double.IsFinite(altitude)
            ? FarSmoothstep(1_000f, 8_000f, (float)altitude) * globeWeight
            : 0f;
        bool heroVisible = Visible;
        // Civil silhouettes remain exclusive with the hero and fade up only after
        // its 10 km retirement, avoiding a duplicate tower or tank farm.
        float silhouetteOpacity = !heroVisible && activeEarth && double.IsFinite(altitude)
            ? FarSmoothstep(10_000f, 14_000f, (float)altitude) * globeWeight
            : 0f;
        float opacity = Mathf.Max(contextOpacity, silhouetteOpacity);

        bool visible = opacity > 0.005f;
        _starbaseFarFieldRoot.Visible = visible;
        if (_lastFarFieldVisible != visible)
        {
            _lastFarFieldVisible = visible;
              string source = _farFieldUsesMappedContext ? "OSM+EarthGround" : "fallback";
              GD.Print($"[STARBASE_FAR] visible={visible} heroVisible={heroVisible} " +
                  $"source={source} altitude={altitude:F0} opacity={opacity:F2} " +
                  $"context={contextOpacity:F2} silhouettes={silhouetteOpacity:F2}");
        }
        if (_lastFarFieldContextOpacity == contextOpacity
            && _lastFarFieldSilhouetteOpacity == silhouetteOpacity)
            return;

        _lastFarFieldOpacity = opacity;
        _lastFarFieldContextOpacity = contextOpacity;
        _lastFarFieldSilhouetteOpacity = silhouetteOpacity;
        foreach (var mesh in _starbaseFarFieldMeshes)
        {
            if (mesh == null || !IsInstanceValid(mesh)) continue;
            float meshOpacity = _starbaseFarFieldContextMeshes.Contains(mesh)
                ? contextOpacity
                : silhouetteOpacity;
            _farSurfaceFade.Apply(mesh, meshOpacity);
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

    private MeshInstance3D AddFarContextMesh(string name, Mesh mesh,
        StandardMaterial3D material, Vector3 position)
    {
        var node = AddFarMesh(name, mesh, material, position);
        _starbaseFarFieldContextMeshes.Add(node);
        return node;
    }

    private MeshInstance3D AddFarContextRotated(string name, Mesh mesh,
        StandardMaterial3D material, Vector3 position, Vector3 rotationDegrees)
    {
        var node = AddFarContextMesh(name, mesh, material, position);
        node.RotationDegrees = rotationDegrees;
        return node;
    }

    private MeshInstance3D AddFarShadowMesh(string name, Mesh mesh,
        StandardMaterial3D material, Vector3 position)
    {
        var node = AddFarMesh(name, mesh, material, position);
        node.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        return node;
    }

    private static StandardMaterial3D CreateDepReliefMaterial()
    {
        var material = FarMat(new Color(0.58f, 0.49f, 0.34f), 0.99f, 0.0f, 0.012f);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.AlbedoColor = Colors.White;
        material.VertexColorUseAsAlbedo = true;
        return material;
    }

    private static StandardMaterial3D FarMat(Color albedo, float roughness,
        float metallic, float emissionEnergy)
    {
        var material = Mat(albedo, roughness, metallic);
        material.EmissionEnabled = emissionEnergy > 0.0f;
        material.Emission = albedo;
        material.EmissionEnergyMultiplier = emissionEnergy;
        return material;
    }

    private static StandardMaterial3D FarContextMat(Color albedo, float roughness,
        float metallic, float emissionEnergy, float alpha)
    {
        var material = FarMat(albedo, roughness, metallic, emissionEnergy);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        var color = material.AlbedoColor;
        color.A = alpha;
        material.AlbedoColor = color;
        return material;
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
            AddReliefTriangle(st, a, b, c);
        }
        st.GenerateNormals();
        return st.Commit()!;
    }

    private static void AddFadedReliefTriangle(SurfaceTool st,
        Vector3 a, Vector3 b, Vector3 c,
        Color colorA, Color colorB, Color colorC)
    {
        // Godot front faces are clockwise. Keep vertex colors paired with their
        // vertices when correcting the winding for an upward-facing surface.
        if ((b - a).Cross(c - a).Y > 0f)
        {
            (b, c) = (c, b);
            (colorB, colorC) = (colorC, colorB);
        }
        st.SetColor(colorA);
        st.AddVertex(a);
        st.SetColor(colorB);
        st.AddVertex(b);
        st.SetColor(colorC);
        st.AddVertex(c);
    }

    private static float FarSmoothstep(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
