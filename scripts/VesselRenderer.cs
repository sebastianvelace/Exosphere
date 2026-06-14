namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public partial class VesselRenderer : Node3D
{
    public Vessel? TargetVessel { get; set; }

    private readonly Dictionary<string, Node3D> _partNodes = new();
    private MeshInstance3D? _hullMesh;
    private PlumeSystem?    _plumes;

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

        // Hot-stage interstage ring (y=20 → y=22). Sooty steel, vented look.
        var ringSteel = Mat(new Color(0.40f, 0.39f, 0.40f), 0.86f, 0.45f);
        var ventMat   = Mat(new Color(0.10f, 0.10f, 0.11f), 0.70f, 0.55f);
        AddMesh("Interstage", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 2f, RadialSegments = 48 },
            ringSteel, new Vector3(0, 21f, 0));

        // Vertical vent slots around the hot-stage ring (dark recesses).
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Pi / 12f;
            AddMesh($"Vent{i}", new BoxMesh { Size = new Vector3(0.10f, 1.4f, 0.16f) },
                ventMat, new Vector3(1.14f * Mathf.Cos(a), 21f, 1.14f * Mathf.Sin(a)));
        }
        // Lip rings top and bottom of the interstage.
        AddWeldRing("InterLipB", 1.155f, 20.1f);
        AddWeldRing("InterLipT", 1.155f, 21.9f);

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
        // Super Heavy steel reads a touch warmer/duller than Starship's brighter
        // upper-stage steel (more soot/handling on the booster).
        var steel     = Mat(new Color(0.78f, 0.78f, 0.80f), 0.93f, 0.22f);
        var darkSteel = Mat(new Color(0.46f, 0.46f, 0.49f), 0.88f, 0.34f);
        // Soot-darkened steel for the engine skirt: heat/exhaust staining.
        var sootSteel = Mat(new Color(0.20f, 0.19f, 0.19f), 0.70f, 0.62f);

        // Main body (y=2 → y=20). Split into a few barrel bands with slightly
        // varied roughness so the bare 304L catches light unevenly (real steel
        // never reads as one flat tube) and gets sootier toward the engines.
        _hullMesh = AddMesh("SHBody", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 18f, RadialSegments = 48 },
            steel, new Vector3(0, 11f, 0));

        // Faint mill-grain / band variation overlaid as very thin, marginally
        // duller rings — breaks up the long cylinder so highlights vary by band.
        var bandMat = Mat(new Color(0.74f, 0.74f, 0.77f), 0.92f, 0.30f);
        for (int i = 0; i < 5; i++)
        {
            float by = Mathf.Lerp(3.4f, 18.6f, (i + 0.5f) / 5f);
            AddMesh($"SHBand{i}", new CylinderMesh
                { TopRadius = 1.152f, BottomRadius = 1.152f, Height = 1.4f, RadialSegments = 48 },
                bandMat, new Vector3(0, by, 0));
        }

        // Soot gradient near the engines (lower booster darkens from exhaust).
        AddMesh("SHSoot", new CylinderMesh
            { TopRadius = 1.153f, BottomRadius = 1.20f, Height = 2.4f, RadialSegments = 48 },
            sootSteel, new Vector3(0, 3.0f, 0));

        // Weld seams down the booster barrel sections.
        AddWeldRings("SHWeld", 1.151f, 3f, 19.5f, 14);

        // Raceway / conduit running up one side of the booster (real SH detail).
        AddMesh("SHRaceway", new BoxMesh { Size = new Vector3(0.20f, 16.5f, 0.34f) },
            darkSteel, new Vector3(1.16f, 11f, 0f));

        // Engine skirt (y=0 → y=2) — sooty, exhaust-stained near the bells.
        AddMesh("SHSkirt", new CylinderMesh
            { TopRadius = 1.15f, BottomRadius = 1.22f, Height = 2f, RadialSegments = 48 },
            sootSteel, new Vector3(0, 1f, 0));

        // 33 Raptor engine bells in 3 rings (tips at y≈-0.6)
        const float shInnerR = 0.30f;
        const float shMidR   = 0.68f;
        const float shOuterR = 1.04f;
        const float shBellY  = -0.6f;

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddRaptor($"SHRapC{i}",
                new Vector3(shInnerR * Mathf.Cos(a), shBellY, shInnerR * Mathf.Sin(a)),
                exitR: 0.27f, throatR: 0.13f, bellLen: 1.3f);
        }
        for (int i = 0; i < 10; i++)
        {
            float a = i * 0.628319f + 0.314159f;
            AddRaptor($"SHRapM{i}",
                new Vector3(shMidR * Mathf.Cos(a), shBellY + 0.05f, shMidR * Mathf.Sin(a)),
                exitR: 0.23f, throatR: 0.11f, bellLen: 1.15f);
        }
        for (int i = 0; i < 20; i++)
        {
            float a = i * 0.314159f;
            AddRaptor($"SHRapO{i}",
                new Vector3(shOuterR * Mathf.Cos(a), shBellY + 0.1f, shOuterR * Mathf.Sin(a)),
                exitR: 0.21f, throatR: 0.10f, bellLen: 1.05f);
        }

        // Optional separation scar when SH is standalone (after staging)
        if (includeSepCap)
        {
            var scarMat = Mat(new Color(0.28f, 0.28f, 0.30f), 0.92f, 0.10f);
            AddMesh("SepScar", new CylinderMesh
                { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 0.35f, RadialSegments = 48 },
                scarMat, new Vector3(0, 22.17f, 0));
        }

        // GPU plumes
        if (_plumes == null) { _plumes = new PlumeSystem { Name = "Plumes" }; AddChild(_plumes); }
        _plumes.SetupSH(shInnerR, shMidR, shOuterR, shBellY);
    }

    // ── 4 grid fins near top of Super Heavy ──────────────────────────────

    private void AddSHGridFins()
    {
        // Real SH grid fins: 4 near the top, offset ~90° apart, slightly
        // forward of the body. Built from a mount arm + a thin lattice slab.
        var finMat   = Mat(new Color(0.40f, 0.40f, 0.43f), 0.90f, 0.34f);
        var mountMat = Mat(new Color(0.34f, 0.34f, 0.37f), 0.86f, 0.42f);

        for (int i = 0; i < 4; i++)
        {
            float a   = i * Mathf.Pi * 0.5f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);

            // Mount hinge/arm against the hull.
            AddMesh($"GridFinMount{i}", new BoxMesh { Size = new Vector3(0.55f, 1.3f, 0.70f) },
                mountMat, new Vector3(1.18f * cos, 18.6f, 1.18f * sin));

            // The lattice slab itself, projecting outward (the recognisable fin).
            var fin = new MeshInstance3D
            {
                Name            = $"GridFin{i}",
                Mesh            = new BoxMesh { Size = new Vector3(1.45f, 1.65f, 0.16f) },
                Position        = new Vector3(1.78f * cos, 18.8f, 1.78f * sin),
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
        // Starship steel is the brightest, cleanest bare 304L in the stack.
        var steel     = Mat(new Color(0.87f, 0.87f, 0.89f), 0.93f, 0.16f);
        var tiles     = TileMat();
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);

        float o = yOffset;

        // ── Body barrel (steel) y=o+24 → o+38 ─────────────────────────────
        // The windward (one) side is black heat-shield tiles; the leeward side
        // stays bare steel. We model this as a full steel barrel plus a tile
        // "shell" half wrapping the windward (-X / forward) side.
        _hullMesh = AddMesh("BodyUpper",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            steel, new Vector3(0, o + 34.5f, 0));

        AddMesh("BodyLower",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            steel, new Vector3(0, o + 27.5f, 0));

        // Weld seams down the ship barrel.
        AddWeldRings("ShipWeld", 1.151f, o + 25f, o + 37.5f, 8);

        // Windward black-tile band: a slightly larger half-cylinder shell on the
        // -X side, running the full body height. Built from short tile staves so
        // the dark heat-shield reads clearly on one side only.
        AddTileBand(o + 24f, o + 38f);

        // ── Ogive nosecone (smooth multi-segment taper) ───────────────────
        // Real Starship nose is a smooth ogive. Build it from many short frusta
        // whose radii follow a tangent-ogive curve so the profile reads round,
        // not faceted. Base y=o+38, tip y≈o+43.5 (5.5 units ≈ 15.4 m).
        const int   noseSeg  = 12;
        const float noseBase = 38f;          // body-relative base
        const float noseLen  = 5.2f;
        const float noseR    = 1.15f;
        for (int i = 0; i < noseSeg; i++)
        {
            // Tangent-ogive radius as a function of normalised height u∈[0,1].
            float u0 = (float)i       / noseSeg;
            float u1 = (float)(i + 1) / noseSeg;
            float rBot = noseR * Mathf.Sqrt(Mathf.Max(0f, 1f - u0 * u0));
            float rTop = noseR * Mathf.Sqrt(Mathf.Max(0f, 1f - u1 * u1));
            float segH = noseLen / noseSeg;
            float yMid = o + noseBase + (u0 + u1) * 0.5f * noseLen;
            AddMesh($"Nose{i}",
                new CylinderMesh { TopRadius = rTop, BottomRadius = rBot, Height = segH * 1.04f, RadialSegments = 48 },
                steel, new Vector3(0, yMid, 0));
        }

        // Tile coverage continues up the windward side of the nose.
        AddTileBand(o + 38f, o + 41.5f, topRadius: 0.83f, botRadius: 1.16f);

        // Dome cap: small hemisphere rounding off the ogive tip at y≈o+43.2
        var noseDome = new MeshInstance3D
        {
            Name     = "NoseDome",
            Mesh     = new SphereMesh
            {
                Radius         = 0.18f,
                Height         = 0.36f,
                IsHemisphere   = true,
                RadialSegments = 32,
                Rings          = 12,
            },
            Position = new Vector3(0, o + 43.18f, 0),
        };
        noseDome.SetSurfaceOverrideMaterial(0, steel);
        AddChild(noseDome);

        // Engine skirt (y=o+22 → o+24)
        AddMesh("Skirt",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.08f, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, o + 23f, 0));
        AddWeldRing("SkirtLip", 1.155f, o + 24f);

        // ── Forward flaps (2 small, high on the body, windward -X side) ────
        // Real V2 forward flaps are small and shifted toward the leeward edge
        // of the windward face; tile-covered.
        AddFlap("FwdFlapL", o + 37.0f, 3.0f, 2.0f, -0.62f, tiles);
        AddFlap("FwdFlapR", o + 37.0f, 3.0f, 2.0f,  0.62f, tiles);

        // ── Aft flaps (2 large, low on the body) ──────────────────────────
        AddFlap("AftFlapL", o + 26.2f, 5.6f, 3.4f, -0.55f, tiles);
        AddFlap("AftFlapR", o + 26.2f, 5.6f, 3.4f,  0.55f, tiles);

        // Sooty engine-bay roof above the bells so the cluster sits in shadow.
        var sootSteel = Mat(new Color(0.20f, 0.19f, 0.19f), 0.70f, 0.62f);
        AddMesh("ShipBaySoot", new CylinderMesh
            { TopRadius = 1.08f, BottomRadius = 1.10f, Height = 0.9f, RadialSegments = 48 },
            sootSteel, new Vector3(0, o + 22.4f, 0));

        // 3 vacuum Raptors (inner, long fixed bell — large exit area)
        const float vacR = 0.38f;
        const float slR  = 0.72f;
        float bellY = o + 22f - 1.05f;

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddRaptor($"RapVac{i}",
                new Vector3(vacR * Mathf.Cos(a), bellY - 0.45f, vacR * Mathf.Sin(a)),
                exitR: 0.46f, throatR: 0.16f, bellLen: 2.2f);
        }

        // 3 sea-level Raptors (outer, shorter gimballing bell)
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            AddRaptor($"RapSL{i}",
                new Vector3(slR * Mathf.Cos(a), bellY + 0.35f, slR * Mathf.Sin(a)),
                exitR: 0.34f, throatR: 0.14f, bellLen: 1.5f);
        }

        // GPU plumes for Starship engines
        if (_plumes == null) { _plumes = new PlumeSystem { Name = "Plumes" }; AddChild(_plumes); }
        _plumes.SetupStarship(vacR, slR, o + 22f);

        if (yOffset == -22f)
        {
            foreach (var part in vessel.Parts.Parts)
                _partNodes[part.InstanceId] = _hullMesh!;
        }
    }

    // ── Per-frame visual updates ──────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (TargetVessel == null) return;

        float  throttle  = (float)TargetVessel.Throttle;
        bool   shPresent = TargetVessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        var    earth     = SimulationBridge.Instance?.Universe.GetBody("earth");
        double alt       = earth != null ? TargetVessel.GetAltitude(earth) : 0.0;

        _plumes?.Update(throttle, shPresent, alt);

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

    // Bare 304L stainless: high metallic, low-ish roughness, full environment
    // reflection with a faint specular tint. Used for all steel surfaces.
    private static StandardMaterial3D Mat(Color albedo, float metallic, float roughness)
        => new()
        {
            AlbedoColor      = albedo,
            Metallic         = metallic,
            MetallicSpecular = 0.55f,
            Roughness        = roughness,
            // Brighter steels read as bare metal; pick up the sky/IBL strongly.
            RimEnabled       = false,
        };

    // Black hexagonal heat-shield tiles: dark, matte, dielectric (non-metal),
    // with a touch of micro-specular so panel edges still catch light.
    private static StandardMaterial3D TileMat()
        => new()
        {
            AlbedoColor      = new Color(0.045f, 0.045f, 0.055f),
            Metallic         = 0.0f,
            MetallicSpecular = 0.18f,
            Roughness        = 0.92f,
        };

    private MeshInstance3D AddMesh(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    // Shared material for thin darker weld/panel lines so the steel reads as
    // a real, built vehicle made of stacked, welded rings.
    private StandardMaterial3D? _weldMat;
    private StandardMaterial3D WeldMat =>
        _weldMat ??= Mat(new Color(0.36f, 0.36f, 0.39f), 0.85f, 0.45f);

    // A thin ring proud of the hull marking a weld seam between barrel sections.
    private void AddWeldRing(string name, float radius, float y)
    {
        AddMesh(name, new CylinderMesh
            { TopRadius = radius, BottomRadius = radius, Height = 0.06f, RadialSegments = 48 },
            WeldMat, new Vector3(0, y, 0));
    }

    // A stack of evenly spaced weld rings between two heights.
    private void AddWeldRings(string prefix, float radius, float yStart, float yEnd, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);
            AddWeldRing($"{prefix}{i}", radius, Mathf.Lerp(yStart, yEnd, t));
        }
    }

    // Black heat-shield tile coverage over the windward (-X) half of a body
    // section. Built from short tile staves spanning ~200° of the circumference
    // so the dark side reads clearly while the leeward side stays bare steel.
    private void AddTileBand(float yBottom, float yTop, float topRadius = 1.165f, float botRadius = 1.165f)
    {
        var tiles  = TileMat();
        float yMid = (yBottom + yTop) * 0.5f;
        float h    = yTop - yBottom;

        // Windward arc centred on -X (angle = π), spanning ~200°.
        const int   staves = 14;
        const float arc    = 3.49f;            // ~200°
        for (int i = 0; i < staves; i++)
        {
            float a = Mathf.Pi - arc * 0.5f + arc * (i + 0.5f) / staves;
            float r = (topRadius + botRadius) * 0.5f;
            // Thin curved-ish stave (a flat slat) sitting just proud of the hull.
            var stave = new MeshInstance3D
            {
                Name            = $"Tile_{(int)(yMid * 10)}_{i}",
                Mesh            = new BoxMesh { Size = new Vector3(0.52f, h, 0.10f) },
                Position        = new Vector3(r * Mathf.Cos(a), yMid, r * Mathf.Sin(a)),
                RotationDegrees = new Vector3(0, -Mathf.RadToDeg(a) + 90f, 0),
            };
            stave.SetSurfaceOverrideMaterial(0, tiles);
            AddChild(stave);
        }
    }

    // A Starship aerodynamic flap: a tile-covered slab plus a steel root, both
    // mounted on the windward (-X) side and offset around the body by `angOff`
    // radians from the -X axis.
    private void AddFlap(string name, float y, float length, float chord, float angOff, StandardMaterial3D mat)
    {
        float a   = Mathf.Pi + angOff;
        float cos = Mathf.Cos(a);
        float sin = Mathf.Sin(a);
        float deg = -Mathf.RadToDeg(a) + 90f;

        // Flap blade, projecting radially outward.
        var blade = new MeshInstance3D
        {
            Name            = name,
            Mesh            = new BoxMesh { Size = new Vector3(chord, length, 0.16f) },
            Position        = new Vector3(1.55f * cos, y, 1.55f * sin),
            RotationDegrees = new Vector3(0, deg, 0),
        };
        blade.SetSurfaceOverrideMaterial(0, mat);
        AddChild(blade);

        // Root fairing where the flap meets the hull.
        var root = new MeshInstance3D
        {
            Name            = name + "Root",
            Mesh            = new BoxMesh { Size = new Vector3(0.55f, length, 0.20f) },
            Position        = new Vector3(1.17f * cos, y, 1.17f * sin),
            RotationDegrees = new Vector3(0, deg, 0),
        };
        root.SetSurfaceOverrideMaterial(0, mat);
        AddChild(root);
    }

    // ── Raptor engine ─────────────────────────────────────────────────────
    // A real Raptor reads, from below, as: a flared regeneratively-cooled bell
    // (warm copper/inconel metallic), a darker recessed throat up inside it, and
    // a cluster of powerhead/turbopump plumbing on top. `pos` is the bell-exit
    // (lowest) point; the engine is built pointing down (-Y).
    private StandardMaterial3D? _bellMat, _throatMat, _powerMat;

    private void AddRaptor(string name, Vector3 pos, float exitR, float throatR, float bellLen)
    {
        // Warm copper/inconel bell — metallic with a coppery albedo so it catches
        // light as real engine hardware rather than a flat dark cone.
        _bellMat   ??= Mat(new Color(0.42f, 0.27f, 0.18f), 0.95f, 0.34f);
        // Recessed throat: very dark, slightly rough (soot + shadow up the bell).
        _throatMat ??= Mat(new Color(0.06f, 0.055f, 0.05f), 0.55f, 0.70f);
        // Powerhead plumbing: greenish-grey inconel/steel.
        _powerMat  ??= Mat(new Color(0.34f, 0.35f, 0.33f), 0.88f, 0.40f);

        float topR = throatR * 1.25f;        // bell radius at its top (near throat)

        // Bell skirt — flared cone, wide at the exit (bottom), narrow at top.
        AddMesh($"{name}Bell",
            new CylinderMesh { TopRadius = topR, BottomRadius = exitR, Height = bellLen, RadialSegments = 24 },
            _bellMat, new Vector3(pos.X, pos.Y + bellLen * 0.5f, pos.Z));

        // Recessed throat plug just inside the top of the bell — gives the dark
        // hollow the eye expects when looking up a nozzle from below.
        AddMesh($"{name}Throat",
            new CylinderMesh { TopRadius = throatR * 0.7f, BottomRadius = throatR, Height = bellLen * 0.45f, RadialSegments = 18 },
            _throatMat, new Vector3(pos.X, pos.Y + bellLen * 0.78f, pos.Z));

        // Powerhead / turbopump hint: a short wider drum on top of the throat.
        AddMesh($"{name}Power",
            new CylinderMesh { TopRadius = topR * 1.15f, BottomRadius = topR * 1.05f, Height = bellLen * 0.30f, RadialSegments = 16 },
            _powerMat, new Vector3(pos.X, pos.Y + bellLen + bellLen * 0.10f, pos.Z));

        // Two small plumbing nubs flanking the powerhead (turbopumps).
        float nub = topR * 0.9f;
        AddMesh($"{name}Pump0",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 10, Rings = 6 },
            _powerMat, new Vector3(pos.X + nub, pos.Y + bellLen * 1.05f, pos.Z));
        AddMesh($"{name}Pump1",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 10, Rings = 6 },
            _powerMat, new Vector3(pos.X - nub, pos.Y + bellLen * 1.05f, pos.Z));
    }

    private void ClearNodes()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _partNodes.Clear();
        _plumes   = null;
        _hullMesh = null;
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
