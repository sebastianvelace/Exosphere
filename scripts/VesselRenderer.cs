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
    private MeshInstance3D? _hullMesh;
    private readonly List<MeshInstance3D> _shPlumes   = new();
    private readonly List<MeshInstance3D> _shipPlumes = new();

    // ── Layout constants (all in render units, y=0 = SH engine bell tips) ──
    //
    //  Full stack:
    //   y = 0           SH engine bell tips (pointing down)
    //   y = 0 → +2      SH engine skirt
    //   y = +2 → +20    SH main body
    //   y = +18 → +22   SH grid fins (overlap with interstage is realistic)
    //   y = +20 → +22   interstage
    //   y = +22         separation plane
    //   y = +22 → +24   Starship engine bay / skirt  (BuildStarshipSection o=0)
    //   y = +24 → +31   Starship lower body (tiles)
    //   y = +31 → +38   Starship upper body (steel)
    //   y = +38 → +43   Starship nosecone (ogive + dome)
    //   y ≈ +43.25      nose tip
    //
    //  Standalone Starship (BuildStarshipSection o=-22):
    //   y = -1.05       engine bell tips
    //   y = 0 → +2      engine bay / skirt
    //   y = +2 → +9     lower body (tiles)
    //   y = +9 → +16    upper body (steel)
    //   y = +16 → +21   nosecone
    //   y ≈ +21.25      nose tip
    //
    //  Standalone SH (BuildSuperHeavyOnly):
    //   Same SH body as full stack, separation scar at top

    // ── Build entry point ─────────────────────────────────────────────────

    public void BuildFromVessel(Vessel vessel)
    {
        TargetVessel = vessel;
        ClearNodes();

        bool hasSH       = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        bool hasStarship  = vessel.Parts.Parts.Any(p =>
            p.Definition.Id == "starship_engines" || p.Definition.Id == "starship_command");
        bool hasAnyEngine = vessel.Parts.Parts.Any(p => p.Definition.Category == PartCategory.Engine);

        if      (hasSH && hasStarship) BuildFullStack(vessel);
        else if (hasSH)                BuildSuperHeavyOnly(vessel);
        else if (hasAnyEngine)         BuildStarshipSection(vessel, yOffset: -22f);
        else                           BuildGenericVessel(vessel);
    }

    // ── Full Starship + Super Heavy stack ─────────────────────────────────

    private void BuildFullStack(Vessel vessel)
    {
        BuildSuperHeavyGeometry(includeSepCap: false);
        AddSHGridFins();

        // Interstage adapter (y=20 → y=22)
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);
        AddMesh("Interstage", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, 21f, 0));

        // Starship section sits at separation plane y=22
        BuildStarshipSection(vessel, yOffset: 0f);

        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = _hullMesh!;
    }

    // ── Standalone Super Heavy (after staging, debris vessel) ─────────────

    private void BuildSuperHeavyOnly(Vessel vessel)
    {
        BuildSuperHeavyGeometry(includeSepCap: true);
        AddSHGridFins();

        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = _hullMesh!;
    }

    // ── Shared Super Heavy geometry ───────────────────────────────────────

    private void BuildSuperHeavyGeometry(bool includeSepCap)
    {
        var steel     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.92f, 0.18f);
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);
        var engineMat = Mat(new Color(0.18f, 0.18f, 0.20f), 0.82f, 0.38f);

        // Main body (y=2 → y=20)
        _hullMesh = AddMesh("SHBody", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 18f, RadialSegments = 48 },
            steel, new Vector3(0, 11f, 0));

        // Engine skirt (y=0 → y=2)
        AddMesh("SHSkirt", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.22f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, 1f, 0));

        // 33 Raptor engine bells in 3 rings (tips at y≈-0.6)
        const float shInnerR = 0.30f;
        const float shMidR   = 0.68f;
        const float shOuterR = 1.04f;
        const float shBellY  = -0.6f;

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

        // Optional separation scar when SH is standalone (after staging)
        if (includeSepCap)
        {
            var scarMat = Mat(new Color(0.28f, 0.28f, 0.30f), 0.92f, 0.10f);
            AddMesh("SepScar", new CylinderMesh
                { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 0.35f, RadialSegments = 48 },
                scarMat, new Vector3(0, 22.17f, 0));
        }

        // SH plumes (9 representative, inner + mid + outer sample)
        SpawnSHPlumes(shInnerR, shMidR, shOuterR);
    }

    // ── 4 grid fins near top of Super Heavy ──────────────────────────────

    private void AddSHGridFins()
    {
        var finMat = Mat(new Color(0.42f, 0.42f, 0.45f), 0.90f, 0.28f);
        for (int i = 0; i < 4; i++)
        {
            float a    = i * Mathf.Pi * 0.5f;
            float posX = 1.18f * Mathf.Cos(a);
            float posZ = 1.18f * Mathf.Sin(a);
            var fin = new MeshInstance3D
            {
                Name            = $"GridFin{i}",
                Mesh            = new BoxMesh { Size = new Vector3(0.18f, 4.0f, 1.55f) },
                Position        = new Vector3(posX, 19.5f, posZ),
                RotationDegrees = new Vector3(0, -i * 90f, 0),
            };
            fin.SetSurfaceOverrideMaterial(0, finMat);
            AddChild(fin);
        }
    }

    // ── Starship section (standalone or stacked above SH) ─────────────────
    //
    //  o = yOffset. When called with:
    //    o =  0    → skirt base at y=22  (full stack, Starship sits atop SH)
    //    o = -22   → skirt base at y=0   (standalone Starship after separation)

    private void BuildStarshipSection(Vessel vessel, float yOffset)
    {
        var steel     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.92f, 0.18f);
        var tiles     = Mat(new Color(0.09f, 0.09f, 0.11f), 0.04f, 0.94f);
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);
        var engineMat = Mat(new Color(0.18f, 0.18f, 0.20f), 0.82f, 0.38f);

        float o = yOffset;

        // Body: lower (tiles) y=o+24→o+31; upper (steel) y=o+31→o+38
        _hullMesh = AddMesh("BodyUpper",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            steel, new Vector3(0, o + 34.5f, 0));

        AddMesh("BodyLower",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            tiles, new Vector3(0, o + 27.5f, 0));

        // ── Ogive nosecone (replaces old sharp cone) ──────────────────────
        // Section 1: y=o+38 → o+41, taper r=1.15→0.72
        AddMesh("Nose1",
            new CylinderMesh { TopRadius = 0.72f, BottomRadius = 1.15f, Height = 3f, RadialSegments = 48 },
            steel, new Vector3(0, o + 39.5f, 0));

        // Section 2: y=o+41 → o+43, taper r=0.72→0.25
        AddMesh("Nose2",
            new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.72f, Height = 2f, RadialSegments = 48 },
            steel, new Vector3(0, o + 42.0f, 0));

        // Dome cap: hemisphere r=0.25 sitting on top of Nose2 at y=o+43
        var noseDome = new MeshInstance3D
        {
            Name     = "NoseDome",
            Mesh     = new SphereMesh
            {
                Radius         = 0.25f,
                Height         = 0.5f,
                IsHemisphere   = true,
                RadialSegments = 20,
                Rings          = 8,
            },
            Position = new Vector3(0, o + 43f, 0),
        };
        noseDome.SetSurfaceOverrideMaterial(0, steel);
        AddChild(noseDome);

        // Engine skirt (y=o+22 → o+24)
        AddMesh("Skirt",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.08f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, o + 23f, 0));

        // Forward canards (small, near top of upper body)
        AddMesh("CanardL",     new BoxMesh { Size = new Vector3(0.12f, 1.6f, 2.6f) },
            darkSteel, new Vector3(-1.23f, o + 37f, 0));
        AddMesh("CanardR",     new BoxMesh { Size = new Vector3(0.12f, 1.6f, 2.6f) },
            darkSteel, new Vector3( 1.23f, o + 37f, 0));
        AddMesh("CanardRootL", new BoxMesh { Size = new Vector3(0.18f, 2.0f, 1.0f) },
            steel, new Vector3(-1.16f, o + 37f, 0));
        AddMesh("CanardRootR", new BoxMesh { Size = new Vector3(0.18f, 2.0f, 1.0f) },
            steel, new Vector3( 1.16f, o + 37f, 0));

        // Aft body flaps (large, lower body area)
        AddMesh("FlapL",     new BoxMesh { Size = new Vector3(0.14f, 5.5f, 4.6f) },
            tiles, new Vector3(-1.23f, o + 26.5f, 0));
        AddMesh("FlapR",     new BoxMesh { Size = new Vector3(0.14f, 5.5f, 4.6f) },
            tiles, new Vector3( 1.23f, o + 26.5f, 0));
        AddMesh("FlapRootL", new BoxMesh { Size = new Vector3(0.20f, 5.5f, 1.2f) },
            tiles, new Vector3(-1.16f, o + 26.5f, 0));
        AddMesh("FlapRootR", new BoxMesh { Size = new Vector3(0.20f, 5.5f, 1.2f) },
            tiles, new Vector3( 1.16f, o + 26.5f, 0));

        // 3 vacuum Raptors (inner, longer bell)
        const float vacR = 0.38f;
        const float slR  = 0.72f;
        float bellY = o + 22f - 1.05f;

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddMesh($"RapVac{i}",
                new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.44f, Height = 2.1f, RadialSegments = 20 },
                engineMat, new Vector3(vacR * Mathf.Cos(a), bellY, vacR * Mathf.Sin(a)));
        }

        // 3 sea-level Raptors (outer, shorter bell)
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            AddMesh($"RapSL{i}",
                new CylinderMesh { TopRadius = 0.21f, BottomRadius = 0.33f, Height = 1.4f, RadialSegments = 20 },
                engineMat, new Vector3(slR * Mathf.Cos(a), bellY + 0.35f, slR * Mathf.Sin(a)));
        }

        // Starship plumes
        SpawnShipPlumes(vacR, slR, o + 22f);

        if (yOffset == -22f)
        {
            foreach (var part in vessel.Parts.Parts)
                _partNodes[part.InstanceId] = _hullMesh!;
        }
    }

    // ── Plumes ────────────────────────────────────────────────────────────

    private void SpawnSHPlumes(float innerR, float midR, float outerR)
    {
        var mat = PlumeMat(new Color(1f, 0.75f, 0.3f));

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            var node = PlumeCone($"SHPlumeC{i}", 5.5f, 0.50f, mat);
            node.Position = new Vector3(innerR * Mathf.Cos(a), -3.0f, innerR * Mathf.Sin(a));
            _shPlumes.Add(node);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            var node = PlumeCone($"SHPlumeM{i}", 4.5f, 0.42f, mat);
            node.Position = new Vector3(midR * Mathf.Cos(a), -2.8f, midR * Mathf.Sin(a));
            _shPlumes.Add(node);
        }
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
        => new()
        {
            AlbedoColor              = new Color(1f, 0.85f, 0.5f, 0f),
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled          = true,
            Emission                 = emission,
            EmissionEnergyMultiplier = 3.0f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
        };

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

    // ── Per-frame visual updates ──────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (TargetVessel == null) return;

        float throttle  = (float)TargetVessel.Throttle;
        bool  firing    = throttle > 0.01f;
        bool  shPresent = TargetVessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");

        foreach (var p in _shPlumes)
        {
            p.Visible = firing && shPresent;
            if (p.Visible) AnimatePlume(p, throttle);
        }

        foreach (var p in _shipPlumes)
        {
            p.Visible = firing && !shPresent;
            if (p.Visible) AnimatePlume(p, throttle);
        }

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
        node.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            { AlbedoColor = GetCategoryColor(part.Definition.Category) });
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
