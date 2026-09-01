namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using System.Collections.Generic;

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
            GD.Print($"[STARBASE_FAR] visible={visible} heroVisible={heroVisible} " +
                $"altitude={altitude:F0} opacity={opacity:F2}");
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
