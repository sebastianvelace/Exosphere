namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public partial class VesselRenderer : Node3D
{
    public Vessel? TargetVessel { get; set; }

    private readonly Dictionary<string, Node3D> _partNodes  = new();
    private MeshInstance3D? _hullMesh;                        // for reentry heat glow
    private readonly List<MeshInstance3D> _shPlumes   = new();  // Super Heavy exhaust
    private readonly List<MeshInstance3D> _shipPlumes = new();  // Starship exhaust

    // ── Build entry point ─────────────────────────────────────────────────

    public void BuildFromVessel(Vessel vessel)
    {
        TargetVessel = vessel;
        ClearNodes();

        bool hasFullStack = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        bool hasEngine    = vessel.Parts.Parts.Any(p => p.Definition.Category == PartCategory.Engine);

        if (hasFullStack)
            BuildFullStack(vessel);
        else if (hasEngine)
            BuildStarshipSection(vessel, yOffset: 0f);
        else
            BuildGenericVessel(vessel);
    }

    // ── Full Starship + Super Heavy stack ─────────────────────────────────
    //
    // Y-up layout (y=0 = bottom of SH engine bells, resting on the OLM):
    //   y=  0         SH engine bell tips (pointing down)
    //   y=  0 → +2    SH engine skirt
    //   y= +2 → +20   SH main body (18 units tall, steel)
    //   y= +20 → +22  interstage adapter (dark steel, slightly tapered)
    //   y= +22        stage separation plane
    //   y= +22 → +24  Starship engine bay / skirt
    //   y= +24 → +31  Starship lower body (heat-shield tiles, 7 units)
    //   y= +31 → +38  Starship upper body (steel, 7 units)
    //   y= +38 → +43  Starship nosecone (tapered, 5 units)
    //   y= +43         nose tip
    //   canards:  around y=+39–41
    //   aft flaps: around y=+25–30

    private void BuildFullStack(Vessel vessel)
    {
        var steel     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.92f, 0.18f);
        var tiles     = Mat(new Color(0.09f, 0.09f, 0.11f), 0.04f, 0.94f);
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);
        var engineMat = Mat(new Color(0.18f, 0.18f, 0.20f), 0.82f, 0.38f);

        // ── Super Heavy ────────────────────────────────────────────────────

        // SH main body
        AddMesh("SHBody", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 18f, RadialSegments = 48 },
            steel, new Vector3(0, 2f + 9f, 0));   // base=2, top=20, centre=11

        // SH engine skirt
        AddMesh("SHSkirt", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.22f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, 1f, 0));     // base=0, top=2, centre=1

        // Interstage adapter
        AddMesh("Interstage", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, 21f, 0));    // base=20, top=22, centre=21

        // 33 Raptor 2 engine bells in 3 rings
        const float shInnerR  = 0.30f;
        const float shMidR    = 0.68f;
        const float shOuterR  = 1.04f;
        const float shBellY   = -0.6f;  // bell tips below y=0

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddMesh($"SHRapC{i}", new CylinderMesh
                { TopRadius = 0.17f, BottomRadius = 0.26f, Height = 1.2f, RadialSegments = 14 },
                engineMat, new Vector3(shInnerR * Mathf.Cos(a), shBellY, shInnerR * Mathf.Sin(a)));
        }
        for (int i = 0; i < 10; i++)
        {
            float a = i * 0.628319f + 0.314159f;
            AddMesh($"SHRapM{i}", new CylinderMesh
                { TopRadius = 0.14f, BottomRadius = 0.22f, Height = 1.1f, RadialSegments = 12 },
                engineMat, new Vector3(shMidR * Mathf.Cos(a), shBellY + 0.05f, shMidR * Mathf.Sin(a)));
        }
        for (int i = 0; i < 20; i++)
        {
            float a = i * 0.314159f;
            AddMesh($"SHRapO{i}", new CylinderMesh
                { TopRadius = 0.12f, BottomRadius = 0.20f, Height = 1.0f, RadialSegments = 10 },
                engineMat, new Vector3(shOuterR * Mathf.Cos(a), shBellY + 0.1f, shOuterR * Mathf.Sin(a)));
        }

        // SH plumes — 6 representative plumes (inner-3 + mid-3)
        SpawnSHPlumes(shInnerR, shMidR, shOuterR);

        // ── Starship section (offset +22 above SH) ────────────────────────
        BuildStarshipSection(vessel, yOffset: 22f);

        // Map all parts to hull mesh for heat glow
        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = _hullMesh!;
    }

    // ── Standalone Starship (after staging, or initial spawn) ─────────────
    // yOffset shifts the whole model upward to stack atop Super Heavy.

    private void BuildStarshipSection(Vessel vessel, float yOffset)
    {
        var steel     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.92f, 0.18f);
        var tiles     = Mat(new Color(0.09f, 0.09f, 0.11f), 0.04f, 0.94f);
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);
        var engineMat = Mat(new Color(0.18f, 0.18f, 0.20f), 0.82f, 0.38f);

        float o = yOffset;  // shorthand

        // Body: lower half (tiles) + upper half (steel)
        _hullMesh = AddMesh("BodyUpper",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            steel, new Vector3(0, o + 31f + 3.5f, 0));   // 31 → 38

        AddMesh("BodyLower",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            tiles, new Vector3(0, o + 24f + 3.5f, 0));   // 24 → 31

        // Nosecone
        AddMesh("Nose",
            new CylinderMesh { TopRadius = 0.04f, BottomRadius = 1.15f, Height = 5f, RadialSegments = 48 },
            steel, new Vector3(0, o + 38f + 2.5f, 0));   // 38 → 43

        // Engine skirt
        AddMesh("Skirt",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.08f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, o + 22f + 1f, 0)); // 22 → 24

        // Forward canards
        AddMesh("CanardL", new BoxMesh { Size = new Vector3(0.12f, 1.6f, 2.6f) },
            darkSteel, new Vector3(-1.23f, o + 40f, 0));
        AddMesh("CanardR", new BoxMesh { Size = new Vector3(0.12f, 1.6f, 2.6f) },
            darkSteel, new Vector3( 1.23f, o + 40f, 0));
        AddMesh("CanardRootL", new BoxMesh { Size = new Vector3(0.18f, 2.0f, 1.0f) },
            steel, new Vector3(-1.16f, o + 40f, 0));
        AddMesh("CanardRootR", new BoxMesh { Size = new Vector3(0.18f, 2.0f, 1.0f) },
            steel, new Vector3( 1.16f, o + 40f, 0));

        // Aft body flaps
        AddMesh("FlapL", new BoxMesh { Size = new Vector3(0.14f, 5.5f, 4.6f) },
            tiles, new Vector3(-1.23f, o + 26.5f, 0));
        AddMesh("FlapR", new BoxMesh { Size = new Vector3(0.14f, 5.5f, 4.6f) },
            tiles, new Vector3( 1.23f, o + 26.5f, 0));
        AddMesh("FlapRootL", new BoxMesh { Size = new Vector3(0.20f, 5.5f, 1.2f) },
            tiles, new Vector3(-1.16f, o + 26.5f, 0));
        AddMesh("FlapRootR", new BoxMesh { Size = new Vector3(0.20f, 5.5f, 1.2f) },
            tiles, new Vector3( 1.16f, o + 26.5f, 0));

        // 3 vacuum Raptors (inner ring, longer bell)
        const float vacR = 0.38f;
        const float slR  = 0.72f;
        float bellY = o + 22f - 1.05f;  // centre of bell at base of skirt

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddMesh($"RapVac{i}",
                new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.44f, Height = 2.1f, RadialSegments = 20 },
                engineMat, new Vector3(vacR * Mathf.Cos(a), bellY, vacR * Mathf.Sin(a)));
        }

        // 3 sea-level Raptors (outer ring, shorter bell)
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            AddMesh($"RapSL{i}",
                new CylinderMesh { TopRadius = 0.21f, BottomRadius = 0.33f, Height = 1.4f, RadialSegments = 20 },
                engineMat, new Vector3(slR * Mathf.Cos(a), bellY + 0.35f, slR * Mathf.Sin(a)));
        }

        // Starship plumes
        SpawnShipPlumes(vacR, slR, o + 22f);

        if (yOffset == 0f)  // standalone build: map parts to hull
        {
            foreach (var part in vessel.Parts.Parts)
                _partNodes[part.InstanceId] = _hullMesh!;
        }
    }

    // ── Plumes ────────────────────────────────────────────────────────────

    private void SpawnSHPlumes(float innerR, float midR, float outerR)
    {
        var mat = PlumeMat(new Color(1f, 0.75f, 0.3f));

        // 3 inner plumes
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            var node = PlumeCone($"SHPlumeC{i}", 5.5f, 0.50f, mat);
            node.Position = new Vector3(innerR * Mathf.Cos(a), -3.0f, innerR * Mathf.Sin(a));
            _shPlumes.Add(node);
        }
        // 3 representative mid plumes
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            var node = PlumeCone($"SHPlumeM{i}", 4.5f, 0.42f, mat);
            node.Position = new Vector3(midR * Mathf.Cos(a), -2.8f, midR * Mathf.Sin(a));
            _shPlumes.Add(node);
        }
        // 3 representative outer plumes
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 0.523599f;
            var node = PlumeCone($"SHPlumeO{i}", 4.0f, 0.38f, mat);
            node.Position = new Vector3(outerR * Mathf.Cos(a), -2.6f, outerR * Mathf.Sin(a));
            _shPlumes.Add(node);
        }
    }

    private void SpawnShipPlumes(float vacR, float slR, float baseY)
    {
        var vacMat = PlumeMat(new Color(1f, 0.55f, 0.1f));
        var slMat  = PlumeMat(new Color(1f, 0.70f, 0.2f));

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            var node = PlumeCone($"PlVac{i}", 5.0f, 0.55f, (StandardMaterial3D)vacMat.Duplicate());
            node.Position = new Vector3(vacR * Mathf.Cos(a), baseY - 2.55f, vacR * Mathf.Sin(a));
            _shipPlumes.Add(node);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            var node = PlumeCone($"PlSL{i}", 3.5f, 0.42f, (StandardMaterial3D)slMat.Duplicate());
            node.Position = new Vector3(slR * Mathf.Cos(a), baseY - 1.75f, slR * Mathf.Sin(a));
            _shipPlumes.Add(node);
        }
    }

    private StandardMaterial3D PlumeMat(Color emission)
    {
        return new StandardMaterial3D
        {
            AlbedoColor              = new Color(1f, 0.85f, 0.5f, 0f),
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled          = true,
            Emission                 = emission,
            EmissionEnergyMultiplier = 3.0f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }

    private MeshInstance3D PlumeCone(string name, float height, float radius, StandardMaterial3D mat)
    {
        var node = new MeshInstance3D
        {
            Name    = name,
            Mesh    = new CylinderMesh { TopRadius = 0f, BottomRadius = radius, Height = height, RadialSegments = 10 },
            Visible = false,
        };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    // ── Visual updates ────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (TargetVessel == null) return;

        float throttle  = (float)TargetVessel.Throttle;
        bool  firing    = throttle > 0.01f;
        bool  shPresent = TargetVessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");

        // Update SH plumes
        foreach (var p in _shPlumes)
        {
            p.Visible = firing && shPresent;
            if (p.Visible) AnimatePlume(p, throttle);
        }

        // Update Starship plumes (only when SH is gone or this is standalone Starship)
        foreach (var p in _shipPlumes)
        {
            p.Visible = firing && !shPresent;
            if (p.Visible) AnimatePlume(p, throttle);
        }

        // Reentry heat glow on hull
        if (_hullMesh == null) return;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!_partNodes.TryGetValue(part.InstanceId, out var node)) continue;
            if (node is not MeshInstance3D mesh) continue;
            if (mesh.GetSurfaceOverrideMaterial(0) is not StandardMaterial3D mat) continue;

            float t = (float)System.Math.Clamp((part.Temperature - 290.0) / 2000.0, 0.0, 1.0);
            mat.EmissionEnabled = t > 0.05f;
            if (t > 0.05f)
                mat.Emission = new Color(t, t * 0.35f, 0f) * t;
        }
    }

    private static void AnimatePlume(MeshInstance3D plume, float throttle)
    {
        float flicker = 0.9f + (float)(GD.Randf() * 0.2f);
        plume.Scale = Vector3.One * (throttle * flicker);
        if (plume.GetSurfaceOverrideMaterial(0) is StandardMaterial3D pm)
            pm.EmissionEnergyMultiplier = 2.5f + throttle * 2.0f;
    }

    // ── Generic vessel fallback ───────────────────────────────────────────

    private void BuildGenericVessel(Vessel vessel)
    {
        var positions = vessel.Parts.ComputePartLocalPositions();
        foreach (var (part, localPos) in positions)
        {
            var node = CreateGenericPartNode(part);
            node.Position = ToV3(localPos);
            AddChild(node);
            _partNodes[part.InstanceId] = node;
        }
    }

    private Node3D CreateGenericPartNode(Part part)
    {
        var node = new MeshInstance3D { Name = part.Definition.Name.Replace(" ", "_") };
        Mesh mesh = part.Definition.Category switch
        {
            PartCategory.Engine   => new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.8f, Height = 2f },
            PartCategory.FuelTank => new CylinderMesh { TopRadius = 0.625f, BottomRadius = 0.625f, Height = 1.875f },
            PartCategory.Command  => new SphereMesh   { Radius = 0.625f, Height = 1.25f },
            _                     => (Mesh)new BoxMesh { Size = new Godot.Vector3(0.5f, 0.5f, 0.5f) },
        };
        node.Mesh = mesh;
        var mat = new StandardMaterial3D { AlbedoColor = GetCategoryColor(part.Definition.Category) };
        node.SetSurfaceOverrideMaterial(0, mat);
        return node;
    }

    private static Color GetCategoryColor(PartCategory cat) => cat switch
    {
        PartCategory.Command   => new Color(0.9f, 0.9f, 0.9f),
        PartCategory.Engine    => new Color(0.6f, 0.6f, 0.65f),
        PartCategory.FuelTank  => new Color(0.8f, 0.85f, 0.9f),
        PartCategory.Decoupler => new Color(1f, 0.8f, 0.2f),
        _                      => new Color(0.7f, 0.7f, 0.7f),
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    private static StandardMaterial3D Mat(Color albedo, float metallic, float roughness)
        => new() { AlbedoColor = albedo, Metallic = metallic, Roughness = roughness };

    private MeshInstance3D AddMesh(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    private void ClearNodes()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _partNodes.Clear();
        _shPlumes.Clear();
        _shipPlumes.Clear();
        _hullMesh = null;
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
