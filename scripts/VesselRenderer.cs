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

    // Windward heat-shield tile materials (one per tile band). Kept so the
    // per-frame update can char/blacken and faintly glow them as the ventral
    // parts accumulate ThermalDamage during re-entry.
    // Materiales de tiles windward: se guardan para quemarlos/oscurecerlos con el daño térmico.
    private readonly List<StandardMaterial3D> _tileMats = new();
    private static readonly Color TileBaseColor = new(0.045f, 0.045f, 0.055f);

    // ── Real-scale hull radius ────────────────────────────────────────────
    // Starship/Super Heavy are 9 m in diameter → 4.5 m radius. At the render
    // scale of 1 u = 2.8 m that is 4.5/2.8 ≈ 1.607 u. The body was previously
    // modelled at 1.15 u (≈6.4 m Ø, too thin). RScale lifts every RADIAL
    // dimension to the real 9 m hull WITHOUT touching the vertical layout, so
    // the stack stays ~43 u (≈121 m) tall and the camera/cockpit framing —
    // which keys off that height — is unaffected.
    // Starship/Super Heavy miden 9 m de diámetro → radio 4.5 m ≈ 1.607 u a la
    // escala de render. RScale escala SOLO lo radial; la altura no se toca
    // (cámara y cabina dependen de ella).
    private const float BodyR  = 1.607f;          // real 9 m-Ø hull radius (u)
    private const float OldR   = 1.15f;           // legacy modelling radius (u)
    private const float RScale = BodyR / OldR;    // ≈1.397 radial scale factor

    // Flight 7 vertical split @ 2.8 m/u — booster 71 m, ship 50 m, stack ~121 m.
    private const float MetresPerUnit = 2.8f;
    private const float SepPlaneY       = 71f / MetresPerUnit;
    private const float ShipSkirtH      = 2f;
    private const float ShipBodyH       = 11.5f;
    private const float ShipNoseH       = 50f / MetresPerUnit - ShipSkirtH - ShipBodyH;
    private const float ShipSkirtBase   = 22f;
    private const float ShipBodyBase    = ShipSkirtBase + ShipSkirtH;
    private const float ShipNoseBase    = ShipBodyBase + ShipBodyH;
    private const float StackShipOffset = SepPlaneY - ShipSkirtBase;
    private const float ShBodyBot       = 2f;
    private const float ShBodyTop       = SepPlaneY - 2f;
    private const float ShBodyH         = ShBodyTop - ShBodyBot;
    private const float ShGridFinY      = ShBodyTop - 2.0f;

    // ── Layout constants (all in render units, y=0 = SH engine bell tips) ──
    //
    //  Full stack (Flight 7 proportions):
    //   y = SepPlaneY   separation plane (71 m)
    //   stack tip ≈ SepPlaneY + 50 m
    //
    //  Standalone Starship (BuildStarshipSection o=-22):
    //   y = -1.05       engine bell tips
    //   y = 0 → +50m    ship section (skirt through nose)
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
        var ringSteel = SteelMat(new Color(0.42f, 0.41f, 0.42f), 0.86f, 0.45f,
            weldSpacing: 0.9f);
        var ventMat   = Mat(new Color(0.10f, 0.10f, 0.11f), 0.70f, 0.55f);
        AddMesh("Interstage", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = BodyR, Height = 2f, RadialSegments = 64 },
            ringSteel, new Vector3(0, SepPlaneY - 1f, 0));

        // Vertical vent slots around the hot-stage ring (dark recesses).
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Pi / 12f;
            AddMesh($"Vent{i}", new BoxMesh { Size = new Vector3(0.10f, 1.4f, 0.16f) },
                ventMat, new Vector3(1.14f * RScale * Mathf.Cos(a), SepPlaneY - 1f, 1.14f * RScale * Mathf.Sin(a)));
        }
        // Lip rings top and bottom of the interstage.
        AddWeldRing("InterLipB", 1.155f * RScale, ShBodyTop + 0.1f);
        AddWeldRing("InterLipT", 1.155f * RScale, SepPlaneY - 0.1f);

        // Starship section sits at separation plane
        BuildStarshipSection(vessel, yOffset: StackShipOffset);

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
        // upper-stage steel (more soot/handling on the booster). The procedural
        // steel shader supplies weld banding + a soot gradient toward the engines,
        // so the body is ONE continuous cylinder — no overlaid band/soot rings
        // that would create visible steps.
        var darkSteel = Mat(new Color(0.46f, 0.46f, 0.49f), 0.88f, 0.34f);
        // Soot-darkened steel for the engine skirt: heat/exhaust staining.
        var sootSteel = Mat(new Color(0.20f, 0.19f, 0.19f), 0.70f, 0.62f);

        // Main body (y=2 → y=20), a single tall barrel. Body-local y runs
        // [-9, +9]; soot fades in over the bottom ~3 units (toward the engines).
        var shSteel = SteelMat(new Color(0.80f, 0.80f, 0.82f), 0.93f, 0.22f,
            weldSpacing: 1.6f, sootBot: -ShBodyH * 0.5f, sootTop: -ShBodyH * 0.5f + 3.5f);
        _hullMesh = AddMesh("SHBody", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = BodyR, Height = ShBodyH, RadialSegments = 64 },
            shSteel, new Vector3(0, ShBodyBot + ShBodyH * 0.5f, 0));
        AddWeldRings("SHBarrelWeld", BodyR + 0.018f, ShBodyBot + 1.1f, ShBodyTop - 1.0f, 9);
        AddHullRing("SHFrostLOX", BodyR + 0.026f, ShBodyTop - 3.8f, 0.10f, FrostMat);

        // Raceway / conduit running up one side of the booster (real SH detail).
        AddMesh("SHRaceway", new BoxMesh { Size = new Vector3(0.20f, 16.5f, 0.34f) },
            darkSteel, new Vector3(BodyR + 0.01f, ShBodyBot + ShBodyH * 0.5f, 0f));
        AddBoosterLongitudinalSeams(darkSteel);

        // Engine skirt (y=0 → y=2) — sooty, blended into the body bottom so the
        // booster/engine transition has no hard cap. Slight outward flare.
        AddMesh("SHSkirt", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = 1.22f * RScale, Height = 2.2f, RadialSegments = 64 },
            sootSteel, new Vector3(0, 0.95f, 0));

        // 33 Raptor engine bells in 3 rings (tips at y≈-0.6). Ring radii scale
        // with the wider 9 m hull so the cluster stays spread under the skirt.
        const float shInnerR = 0.30f * RScale;
        const float shMidR   = 0.68f * RScale;
        const float shOuterR = 1.04f * RScale;
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
                exitR: 0.23f, throatR: 0.11f, bellLen: 1.15f, bellRings: 3);
        }
        for (int i = 0; i < 20; i++)
        {
            float a = i * 0.314159f;
            AddRaptor($"SHRapO{i}",
                new Vector3(shOuterR * Mathf.Cos(a), shBellY + 0.1f, shOuterR * Mathf.Sin(a)),
                exitR: 0.21f, throatR: 0.10f, bellLen: 1.05f, bellRings: 3);
        }

        // Exposed hot-stage interstage when SH is standalone (after staging).
        // In the full stack this vented ring is hidden under the Ship; once the
        // booster separates it's the SH's most recognisable feature, so we build
        // it out here: a sooty hot-stage ring scorched by the Ship's exhaust,
        // ringed with vent slots and capped by a scorched separation lip.
        // Anillo hot-stage expuesto tras el staging: en el stack queda oculto bajo
        // la Ship; al separarse es el rasgo más reconocible del SH, así que lo
        // mostramos chamuscado por el escape de la Ship, con vents y un labio quemado.
        if (includeSepCap)
            BuildExposedHotStageRing();

        // GPU plumes
        if (_plumes == null) { _plumes = new PlumeSystem { Name = "Plumes" }; AddChild(_plumes); }
        _plumes.SetupSH(shInnerR, shMidR, shOuterR, shBellY);
    }

    // ── Exposed hot-stage interstage (standalone SH after separation) ─────
    // The booster body tops out at y=20. The hot-stage ring sits y=20 → y=22
    // (same plane the full stack hides under the Ship). When the SH flies
    // alone we expose it: a scorched vented ring + a burnt separation lip.
    private void BuildExposedHotStageRing()
    {
        // Heavily sooted steel — the Ship's plume scorches this ring on staging.
        var scorched = SteelMat(new Color(0.30f, 0.29f, 0.30f), 0.84f, 0.55f,
            weldSpacing: 0.9f);
        var ventMat  = Mat(new Color(0.05f, 0.05f, 0.06f), 0.60f, 0.70f);
        var lipMat   = Mat(new Color(0.18f, 0.17f, 0.17f), 0.88f, 0.45f);

        // The vented hot-stage barrel itself (y=20 → y=22).
        AddMesh("HotStageRing", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = BodyR, Height = 2f, RadialSegments = 64 },
            scorched, new Vector3(0, ShBodyTop + 1f, 0));

        // Vertical vent slots around the ring — these are the open passages the
        // Ship's exhaust blew through during hot-staging.
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Pi / 12f;
            AddMesh($"HotVent{i}", new BoxMesh { Size = new Vector3(0.11f, 1.5f, 0.18f) },
                ventMat, new Vector3((BodyR - 0.02f) * Mathf.Cos(a), ShBodyTop + 1f, (BodyR - 0.02f) * Mathf.Sin(a)));
        }

        // Burnt separation lip capping the exposed ring (the torn separation plane).
        AddMesh("SepLip", new CylinderMesh
            { TopRadius = BodyR + 0.02f, BottomRadius = BodyR + 0.02f, Height = 0.22f, RadialSegments = 48 },
            lipMat, new Vector3(0, SepPlaneY + 0.05f, 0));
    }

    private void AddBoosterLongitudinalSeams(Material mat)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f + Mathf.Pi / 8f;
            AddSurfaceBox($"SHLongSeam{i}", a, ShBodyBot + ShBodyH * 0.5f, ShBodyH * 0.84f, 0.030f, 0.10f, mat, BodyR + 0.030f);
        }
    }

    // ── 4 grid fins near top of Super Heavy ──────────────────────────────

    private void AddSHGridFins()
    {
        // Real SH grid fins: 4 near the top, offset ~90° apart. They read as
        // thick cast lattice panels with a tapered outer silhouette, hinge drum
        // and diagonal webbing, not flat rectangular paddles.
        var finMat   = Mat(new Color(0.34f, 0.34f, 0.37f), 0.90f, 0.38f);
        var mountMat = Mat(new Color(0.34f, 0.34f, 0.37f), 0.86f, 0.42f);
        var gridMat  = Mat(new Color(0.13f, 0.13f, 0.15f), 0.86f, 0.52f);

        for (int i = 0; i < 4; i++)
        {
            float a   = i * Mathf.Pi * 0.5f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);
            float deg = -i * 90f;

            // Mount hinge/arm against the hull.
            AddMesh($"GridFinMount{i}", new BoxMesh { Size = new Vector3(0.55f, 1.3f, 0.70f) },
                mountMat, new Vector3((BodyR + 0.03f) * cos, ShGridFinY, (BodyR + 0.03f) * sin));

            var hinge = AddMesh($"GridFinHinge{i}",
                new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.18f, Height = 1.45f, RadialSegments = 18 },
                mountMat, new Vector3((BodyR + 0.34f) * cos, ShGridFinY + 0.15f, (BodyR + 0.34f) * sin));
            hinge.RotationDegrees = new Vector3(0f, deg, 90f);

            // Tapered lattice slab, canted slightly so it does not read as a flat square.
            var fin = new MeshInstance3D
            {
                Name            = $"GridFin{i}",
                Mesh            = BuildGridFinPlateMesh(rootChord: 1.62f, tipChord: 1.18f, height: 1.85f, thickness: 0.18f),
                Position        = new Vector3((BodyR + 0.78f) * cos, ShGridFinY + 0.25f, (BodyR + 0.78f) * sin),
                RotationDegrees = new Vector3(0f, deg + 6f, 4f),
            };
            fin.SetSurfaceOverrideMaterial(0, finMat);
            AddChild(fin);

            // Perimeter frame.
            foreach (float y in new[] { -0.78f, 0.78f })
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_FrameH{y}",
                    Mesh = new BoxMesh { Size = new Vector3(1.34f, 0.070f, 0.24f) },
                    Position = new Vector3(0f, y, -0.025f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
            foreach (float x in new[] { -0.60f, 0.60f })
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_FrameV{x}",
                    Mesh = new BoxMesh { Size = new Vector3(0.075f, 1.62f, 0.24f) },
                    Position = new Vector3(x, 0f, -0.025f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }

            // Dense open grid: small raised webs plus two diagonals. The actual
            // voids are not cut out, but the dark thin material and tapered plate
            // make the silhouette read as a real cast grid fin at game distance.
            for (int r = -2; r <= 2; r++)
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_RibH{r}",
                    Mesh = new BoxMesh { Size = new Vector3(1.12f, 0.040f, 0.25f) },
                    Position = new Vector3(0f, r * 0.27f, -0.04f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
            for (int c = -2; c <= 2; c++)
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_RibV{c}",
                    Mesh = new BoxMesh { Size = new Vector3(0.040f, 1.42f, 0.25f) },
                    Position = new Vector3(c * 0.25f, 0f, -0.02f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
            foreach (float d in new[] { -1f, 1f })
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_Diag{d}",
                    Mesh = new BoxMesh { Size = new Vector3(0.045f, 1.75f, 0.23f) },
                    Position = new Vector3(0f, 0f, -0.055f),
                    RotationDegrees = new Vector3(0f, 0f, d * 38f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
        }
    }

    // ── Starship section (standalone or stacked above SH) ─────────────────
    //
    //  o = StackShipOffset → skirt base at SepPlaneY (full stack)
    //  o = -22             → skirt base at y=0 (standalone Starship)

    private void BuildStarshipSection(Vessel vessel, float yOffset)
    {
        var tiles     = TileMat();
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);

        float o = yOffset;
        float bodyMid = o + ShipBodyBase + ShipBodyH * 0.5f;
        float bodyTop = o + ShipNoseBase;
        float bodyBot = o + ShipBodyBase;
        float fwdFlapY = o + ShipNoseBase - 1.5f;
        float aftFlapY = o + ShipBodyBase + 2.4f;
        float skirtMid = o + ShipSkirtBase + ShipSkirtH * 0.5f;
        float skirtTop = o + ShipBodyBase;

        var shipSteel = SteelMat(new Color(0.88f, 0.88f, 0.90f), 0.93f, 0.16f, weldSpacing: 1.55f);
        _hullMesh = AddMesh("Body",
            new CylinderMesh { TopRadius = BodyR, BottomRadius = BodyR, Height = ShipBodyH, RadialSegments = 64 },
            shipSteel, new Vector3(0, bodyMid, 0));
        AddWeldRings("ShipBarrelWeld", BodyR + 0.018f, bodyBot + 0.8f, bodyTop - 0.7f, 7);
        AddHullRing("ShipFrostLOX", BodyR + 0.026f, bodyTop - 1.3f, 0.08f, FrostMat);
        AddHullRing("ShipFrostCH4", BodyR + 0.026f, bodyMid - 1.75f, 0.07f, FrostMat);

        AddSurfaceBox("ShipRaceway", angle: 0f, y: bodyMid, height: ShipBodyH * 0.81f,
            width: 0.18f, depth: 0.18f, mat: darkSteel, radius: BodyR + 0.045f);
        AddPayloadDoorOutline(o, darkSteel);
        AddShipCloseupCues(o);

        AddTileBand(bodyBot, bodyTop);
        AddHeatShieldBorder(bodyBot, bodyTop, BodyR + 0.035f);

        const int   noseSeg  = 22;
        const float noseBase = ShipNoseBase;
        const float noseLen  = ShipNoseH;
        const float noseR    = BodyR;
        var noseSteel = SteelMat(new Color(0.88f, 0.88f, 0.90f), 0.93f, 0.18f,
            weldSpacing: 1.3f);
        // Ogive profile: a circular-arc shape. Using a near-tangent-ogive gives
        // a fuller, more realistic Starship nose than a simple sqrt curve.
        float OgiveR(float u)                // u in [0,1], 0=base 1=tip
        {
            // tangent ogive of fineness ~ noseLen/(2*noseR)
            float rho = (noseR * noseR + noseLen * noseLen) / (2f * noseR);
            float y   = u * noseLen;
            float val = rho * rho - (noseLen - y) * (noseLen - y);
            float r   = Mathf.Sqrt(Mathf.Max(0f, val)) - (rho - noseR);
            return Mathf.Clamp(r, 0f, noseR);
        }
        for (int i = 0; i < noseSeg; i++)
        {
            float u0 = (float)i       / noseSeg;
            float u1 = (float)(i + 1) / noseSeg;
            float rBot = OgiveR(u0);
            float rTop = OgiveR(u1);
            float segH = noseLen / noseSeg;
            float yMid = o + noseBase + (u0 + u1) * 0.5f * noseLen;
            AddMesh($"Nose{i}",
                new CylinderMesh { TopRadius = rTop, BottomRadius = rBot, Height = segH * 1.06f, RadialSegments = 64 },
                noseSteel, new Vector3(0, yMid, 0));
        }

        // Tile coverage continues up the windward side of the nose.
        AddTileBand(o + ShipNoseBase, o + ShipNoseBase + ShipNoseH * 0.67f,
            topRadius: 0.83f * RScale, botRadius: BodyR + 0.01f);
        AddHeatShieldBorder(o + ShipNoseBase, o + ShipNoseBase + ShipNoseH * 0.67f, BodyR + 0.030f);

        // Dome cap: small hemisphere rounding off the ogive tip at y≈o+43.0
        var noseDome = new MeshInstance3D
        {
            Name     = "NoseDome",
            Mesh     = new SphereMesh
            {
                Radius         = OgiveR((noseSeg - 1f) / noseSeg) + 0.02f,
                Height         = 0.30f,
                IsHemisphere   = true,
                RadialSegments = 48,
                Rings          = 16,
            },
            Position = new Vector3(0, o + noseBase + noseLen * (noseSeg - 1f) / noseSeg, 0),
        };
        noseDome.SetSurfaceOverrideMaterial(0, noseSteel);
        AddChild(noseDome);

        AddMesh("Skirt",
            new CylinderMesh { TopRadius = BodyR, BottomRadius = 1.08f * RScale, Height = ShipSkirtH, RadialSegments = 48 },
            darkSteel, new Vector3(0, skirtMid, 0));
        AddWeldRing("SkirtLip", 1.155f * RScale, skirtTop);

        AddFlap("FwdFlapL", fwdFlapY, 3.0f, 2.0f, -0.62f, tiles);
        AddFlap("FwdFlapR", fwdFlapY, 3.0f, 2.0f,  0.62f, tiles);
        AddFlap("AftFlapL", aftFlapY, 5.6f, 3.4f, -0.55f, tiles);
        AddFlap("AftFlapR", aftFlapY, 5.6f, 3.4f,  0.55f, tiles);

        var sootSteel = Mat(new Color(0.20f, 0.19f, 0.19f), 0.70f, 0.62f);
        AddMesh("ShipBaySoot", new CylinderMesh
            { TopRadius = 1.08f * RScale, BottomRadius = 1.10f * RScale, Height = 0.9f, RadialSegments = 48 },
            sootSteel, new Vector3(0, o + ShipSkirtBase + 0.4f, 0));
        AddAftShieldSkirt(o, sootSteel);

        const float vacR = 0.38f * RScale;
        const float slR  = 0.72f * RScale;
        float bellY = o + ShipSkirtBase - 1.05f;

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddRaptor($"RapVac{i}",
                new Vector3(vacR * Mathf.Cos(a), bellY - 0.45f, vacR * Mathf.Sin(a)),
                exitR: 0.46f, throatR: 0.16f, bellLen: 2.2f, bellRings: 6);
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
        _plumes.SetupStarship(vacR, slR, o + ShipSkirtBase);

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

        // Reentry heat glow on hull. The main hull now uses the procedural steel
        // ShaderMaterial (emit via the `emit_strength` uniform); small detail
        // parts may still carry a StandardMaterial3D, so handle both.
        if (_hullMesh == null) return;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!_partNodes.TryGetValue(part.InstanceId, out var node)) continue;
            if (node is not MeshInstance3D mesh) continue;

            float t = (float)System.Math.Clamp((part.Temperature - 290.0) / 2000.0, 0.0, 1.0);
            var surfMat = mesh.GetSurfaceOverrideMaterial(0);

            if (surfMat is ShaderMaterial sm)
            {
                sm.SetShaderParameter("emit_strength", t > 0.05f ? t * t * 2.5f : 0f);
            }
            else if (surfMat is StandardMaterial3D mat)
            {
                mat.EmissionEnabled = t > 0.05f;
                if (t > 0.05f)
                    mat.Emission = new Color(t, t * 0.35f, 0f) * t;
            }
        }

        UpdateTileCharring();
    }

    // ── Heat-shield tile charring ─────────────────────────────────────────
    // The windward black tiles char and discolour as the protected (heat-shield)
    // parts take ThermalDamage on re-entry. We drive every tile band off the worst
    // accumulated damage and peak temperature among shielded parts: charred tiles
    // go from black toward a scorched dark brown, and when truly hot they pick up a
    // faint ember glow. Purely cosmetic — destruction is still decided by the sim.
    //
    // Carbonizado de las tiles: usa el peor ThermalDamage/Temperature de las piezas
    // con escudo para oscurecer y, si está muy caliente, dar un leve brillo de ascua.
    private void UpdateTileCharring()
    {
        if (_tileMats.Count == 0 || TargetVessel == null) return;

        double worstDamage = 0.0;
        double peakTemp    = 0.0;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!part.Definition.HasHeatShield) continue;
            if (part.ThermalDamage > worstDamage) worstDamage = part.ThermalDamage;
            if (part.Temperature   > peakTemp)    peakTemp    = part.Temperature;
        }

        float dmg = (float)System.Math.Clamp(worstDamage, 0.0, 1.0);
        // Ember glow only once the shield runs genuinely hot (well past steel limits).
        float ember = (float)System.Math.Clamp((peakTemp - 1100.0) / 900.0, 0.0, 1.0);

        // Scorch tint: charred ablative reads as a warm sooty brown over the base black.
        var scorch = new Color(0.085f, 0.050f, 0.038f);
        Color albedo = TileBaseColor.Lerp(scorch, dmg);

        foreach (var mat in _tileMats)
        {
            mat.AlbedoColor = albedo;
            // Slightly rougher and lighter-edged as it ablates.
            mat.Roughness   = Mathf.Lerp(0.92f, 0.99f, dmg);

            bool glow = ember > 0.02f;
            mat.EmissionEnabled = glow;
            if (glow)
            {
                // Deep-red ember rising with both temperature and accumulated damage.
                float e = ember * (0.35f + 0.65f * dmg);
                mat.Emission                 = new Color(0.9f, 0.18f, 0.04f);
                mat.EmissionEnergyMultiplier = e * 1.6f;
            }
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
    // reflection with a faint specular tint. Used for small steel details that
    // don't need the procedural weld/soot banding of the main hull.
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

    // The procedural stainless-steel shader, loaded once and shared.
    private static Shader? _steelShader;
    private static Shader SteelShader =>
        _steelShader ??= GD.Load<Shader>("res://assets/shaders/steel.gdshader");

    // A continuous PBR stainless-steel material driven by steel.gdshader. Gives
    // weld-ring banding, brushed anisotropy, and an optional soot/heat gradient
    // near the engines (soot fades in below `sootTop` down to `sootBot`, in the
    // SAME local space as the mesh that uses it). Pass sootTop<=sootBot to disable.
    private ShaderMaterial SteelMat(
        Color tint, float metallic = 0.92f, float roughness = 0.20f,
        float weldSpacing = 1.6f, float sootBot = -1000f, float sootTop = -1000f)
    {
        var m = new ShaderMaterial { Shader = SteelShader };
        m.SetShaderParameter("base_tint",    tint);
        m.SetShaderParameter("metallic_val", metallic);
        m.SetShaderParameter("rough_val",    roughness);
        m.SetShaderParameter("spec_val",     0.55f);
        m.SetShaderParameter("weld_spacing", weldSpacing);
        m.SetShaderParameter("weld_depth",   0.10f);
        m.SetShaderParameter("brush_amt",    0.10f);
        m.SetShaderParameter("soot_y0",      sootTop);   // clean above
        m.SetShaderParameter("soot_y1",      sootBot);   // sooty toward engine
        m.SetShaderParameter("soot_color",   new Color(0.16f, 0.15f, 0.15f));
        m.SetShaderParameter("emit_strength", 0.0f);
        return m;
    }

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

    private MeshInstance3D AddMesh(string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    private static ArrayMesh BuildGridFinPlateMesh(float rootChord, float tipChord, float height, float thickness)
    {
        float y0 = -height * 0.5f;
        float y1 =  height * 0.5f;
        float r0 = rootChord * 0.5f;
        float r1 = tipChord * 0.5f;
        float z0 = -thickness * 0.5f;
        float z1 =  thickness * 0.5f;

        var verts = new Vector3[]
        {
            new(-r0, y0, z0), new( r0, y0, z0), new( r1, y1, z0), new(-r1, y1, z0),
            new(-r0, y0, z1), new( r0, y0, z1), new( r1, y1, z1), new(-r1, y1, z1),
        };

        int[] idx =
        {
            0, 1, 2, 0, 2, 3, // front
            5, 4, 7, 5, 7, 6, // back
            4, 0, 3, 4, 3, 7, // left edge
            1, 5, 6, 1, 6, 2, // right edge
            3, 2, 6, 3, 6, 7, // top
            4, 5, 1, 4, 1, 0, // bottom
        };

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = verts;
        arr[(int)Mesh.ArrayType.Index] = idx;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return mesh;
    }

    // Shared material for thin darker weld/panel lines so the steel reads as
    // a real, built vehicle made of stacked, welded rings.
    private StandardMaterial3D? _weldMat;
    private StandardMaterial3D WeldMat =>
        _weldMat ??= Mat(new Color(0.36f, 0.36f, 0.39f), 0.85f, 0.45f);

    private StandardMaterial3D? _frostMat;
    private StandardMaterial3D FrostMat =>
        _frostMat ??= Mat(new Color(0.70f, 0.80f, 0.86f), 0.05f, 0.86f);

    // A thin ring proud of the hull marking a weld seam between barrel sections.
    private void AddWeldRing(string name, float radius, float y)
    {
        AddHullRing(name, radius, y, 0.06f, WeldMat);
    }

    private void AddHullRing(string name, float radius, float y, float height, Material mat)
    {
        AddMesh(name, new CylinderMesh
            { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 64 },
            mat, new Vector3(0, y, 0));
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

    private void AddSurfaceBox(string name, float angle, float y, float height,
        float width, float depth, Material mat, float radius)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        var box = new MeshInstance3D
        {
            Name            = name,
            Mesh            = new BoxMesh { Size = new Vector3(width, height, depth) },
            Position        = new Vector3(radius * cos, y, radius * sin),
            RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(angle) + 90f, 0f),
        };
        box.SetSurfaceOverrideMaterial(0, mat);
        AddChild(box);
    }

    private static float ShipRelY(float o, float legacyRelFromSkirt)
    {
        const float legacyShipSpan = 21.25f;
        float span = ShipSkirtH + ShipBodyH + ShipNoseH;
        return o + ShipSkirtBase + legacyRelFromSkirt * (span / legacyShipSpan);
    }

    private void AddPayloadDoorOutline(float o, Material mat)
    {
        const float a = 0f;
        float r = BodyR + 0.052f;
        AddSurfaceBox("PayloadDoorLeft",  a - 0.17f, ShipRelY(o, 12.0f), 7.0f, 0.030f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorRight", a + 0.17f, ShipRelY(o, 12.0f), 7.0f, 0.030f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorTop",   a, ShipRelY(o, 15.5f), 0.04f, 0.56f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorBottom",a, ShipRelY(o, 8.5f), 0.04f, 0.56f, 0.12f, mat, r);
    }

    private void AddShipCloseupCues(float o)
    {
        var panelMat = Mat(new Color(0.30f, 0.30f, 0.33f), 0.86f, 0.46f);
        var darkMark = Mat(new Color(0.055f, 0.055f, 0.062f), 0.20f, 0.82f);
        var paleMark = Mat(new Color(0.72f, 0.73f, 0.75f), 0.35f, 0.66f);

        AddSurfaceBox("ShipNoseAccessPanel", 0.20f, ShipRelY(o, 17.6f), 1.15f, 0.030f, 0.11f, panelMat, BodyR + 0.050f);
        AddSurfaceBox("ShipUpperAccessPanel", -0.24f, ShipRelY(o, 13.0f), 1.55f, 0.032f, 0.11f, panelMat, BodyR + 0.050f);
        AddSurfaceBox("ShipAftAccessPanel", 0.32f, ShipRelY(o, 5.1f), 1.15f, 0.030f, 0.11f, panelMat, BodyR + 0.050f);

        foreach (float rel in new[] { 14.2f, 11.3f, 7.0f })
        {
            float y = ShipRelY(o, rel);
            AddSurfaceBox($"ShipVentPort{(int)(y * 10)}A", 0.43f, y, 0.20f, 0.055f, 0.12f, darkMark, BodyR + 0.060f);
            AddSurfaceBox($"ShipVentPort{(int)(y * 10)}B", 0.51f, y - 0.24f, 0.16f, 0.050f, 0.12f, darkMark, BodyR + 0.060f);
        }

        float a = -0.46f;
        float r = BodyR + 0.063f;
        AddSurfaceBox("ShipSerialStem", a, ShipRelY(o, 10.6f), 1.25f, 0.035f, 0.12f, darkMark, r);
        AddSurfaceBox("ShipSerialTop", a, ShipRelY(o, 11.2f), 0.035f, 0.42f, 0.12f, darkMark, r);
        AddSurfaceBox("ShipSerialMid", a, ShipRelY(o, 10.6f), 0.035f, 0.34f, 0.12f, darkMark, r);
        AddSurfaceBox("ShipSerialBot", a, ShipRelY(o, 10.0f), 0.035f, 0.42f, 0.12f, darkMark, r);
        AddSurfaceBox("ShipSerialTick0", a - 0.09f, ShipRelY(o, 11.05f), 0.42f, 0.030f, 0.12f, paleMark, r);
        AddSurfaceBox("ShipSerialTick1", a - 0.15f, ShipRelY(o, 10.18f), 0.42f, 0.030f, 0.12f, paleMark, r);
    }

    private void AddHeatShieldBorder(float yBottom, float yTop, float radius)
    {
        var border = Mat(new Color(0.018f, 0.018f, 0.022f), 0.0f, 0.96f);
        const float arc = 3.49f;
        foreach (float a in new[] { Mathf.Pi - arc * 0.5f, Mathf.Pi + arc * 0.5f })
        {
            float yMid = (yBottom + yTop) * 0.5f;
            var strip = new MeshInstance3D
            {
                Name = "HeatShieldEdge",
                Mesh = new BoxMesh { Size = new Vector3(0.055f, yTop - yBottom, 0.13f) },
                Position = new Vector3(radius * Mathf.Cos(a), yMid, radius * Mathf.Sin(a)),
                RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a) + 90f, 0f),
            };
            strip.SetSurfaceOverrideMaterial(0, border);
            AddChild(strip);
        }
    }

    private void AddAftShieldSkirt(float o, Material mat)
    {
        // Dark aft heat/soot blankets around the Ship engine bay. This makes the
        // Starship-to-engine transition read closer to the real vehicle.
        AddSurfaceBox("ShipAftBlackWrapL", Mathf.Pi - 0.72f, ShipRelY(o, 2.2f), 1.5f, 0.30f, 0.15f, mat, BodyR + 0.055f);
        AddSurfaceBox("ShipAftBlackWrapC", Mathf.Pi,         ShipRelY(o, 2.0f), 1.7f, 0.38f, 0.15f, mat, BodyR + 0.055f);
        AddSurfaceBox("ShipAftBlackWrapR", Mathf.Pi + 0.72f, ShipRelY(o, 2.2f), 1.5f, 0.30f, 0.15f, mat, BodyR + 0.055f);
    }

    // Black heat-shield tile coverage over the windward (-X) half of a body
    // section. Built from short tile staves spanning ~200° of the circumference
    // so the dark side reads clearly while the leeward side stays bare steel.
    private void AddTileBand(float yBottom, float yTop, float topRadius = BodyR + 0.015f, float botRadius = BodyR + 0.015f)
    {
        var tiles  = TileMat();
        // Register this band's tile material so _Process can char it with damage.
        _tileMats.Add(tiles);
        var seams  = Mat(new Color(0.010f, 0.010f, 0.012f), 0.0f, 0.96f);
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

            int rows = System.Math.Clamp((int)(h / 0.85f), 3, 18);
            for (int row = 1; row < rows; row++)
            {
                float y = -h * 0.5f + h * row / rows;
                var seam = new MeshInstance3D
                {
                    Name = $"TileSeamH_{(int)(yMid * 10)}_{i}_{row}",
                    Mesh = new BoxMesh { Size = new Vector3(0.50f, 0.018f, 0.108f) },
                    Position = new Vector3(0f, y, -0.006f),
                };
                seam.SetSurfaceOverrideMaterial(0, seams);
                stave.AddChild(seam);
            }

            float rowH = h / rows;
            for (int row = 0; row < rows; row++)
            {
                float y = -h * 0.5f + rowH * (row + 0.5f);
                float x = (row + i) % 2 == 0 ? -0.13f : 0.13f;
                var vSeam = new MeshInstance3D
                {
                    Name = $"TileSeamV_{(int)(yMid * 10)}_{i}_{row}",
                    Mesh = new BoxMesh { Size = new Vector3(0.018f, rowH * 0.58f, 0.110f) },
                    Position = new Vector3(x, y, -0.007f),
                };
                vSeam.SetSurfaceOverrideMaterial(0, seams);
                stave.AddChild(vSeam);
            }
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

        // Flap blade, projecting radially outward (offset rides the 9 m hull).
        var blade = new MeshInstance3D
        {
            Name            = name,
            Mesh            = new BoxMesh { Size = new Vector3(chord, length, 0.16f) },
            Position        = new Vector3((BodyR + 0.40f) * cos, y, (BodyR + 0.40f) * sin),
            RotationDegrees = new Vector3(0, deg, 0),
        };
        blade.SetSurfaceOverrideMaterial(0, mat);
        AddChild(blade);

        var edgeMat = Mat(new Color(0.020f, 0.020f, 0.024f), 0.0f, 0.94f);
        var leading = new MeshInstance3D
        {
            Name = name + "LeadingEdge",
            Mesh = new BoxMesh { Size = new Vector3(0.12f, length * 0.96f, 0.070f) },
            Position = new Vector3(chord * 0.45f, 0f, -0.10f),
        };
        leading.SetSurfaceOverrideMaterial(0, edgeMat);
        blade.AddChild(leading);

        for (int s = -2; s <= 2; s++)
        {
            var seam = new MeshInstance3D
            {
                Name = $"{name}TileSeam{s}",
                Mesh = new BoxMesh { Size = new Vector3(chord * 0.70f, 0.030f, 0.060f) },
                Position = new Vector3(0f, s * length * 0.16f, -0.105f),
            };
            seam.SetSurfaceOverrideMaterial(0, edgeMat);
            blade.AddChild(seam);
        }

        // Root fairing where the flap meets the hull.
        var root = new MeshInstance3D
        {
            Name            = name + "Root",
            Mesh            = new BoxMesh { Size = new Vector3(0.55f, length, 0.20f) },
            Position        = new Vector3((BodyR + 0.02f) * cos, y, (BodyR + 0.02f) * sin),
            RotationDegrees = new Vector3(0, deg, 0),
        };
        root.SetSurfaceOverrideMaterial(0, mat);
        AddChild(root);

        var hingeMat = Mat(new Color(0.18f, 0.18f, 0.20f), 0.80f, 0.40f);
        var hinge = new MeshInstance3D
        {
            Name = name + "Hinge",
            Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = length * 0.92f, RadialSegments = 14 },
            Position = new Vector3(-0.28f, 0f, -0.11f),
            RotationDegrees = new Vector3(0f, 0f, 0f),
        };
        hinge.SetSurfaceOverrideMaterial(0, hingeMat);
        root.AddChild(hinge);
    }

    // ── Raptor engine ─────────────────────────────────────────────────────
    // A real Raptor reads, from below, as: a flared regeneratively-cooled bell
    // (warm copper/inconel metallic), a darker recessed throat up inside it, and
    // a cluster of powerhead/turbopump plumbing on top. `pos` is the bell-exit
    // (lowest) point; the engine is built pointing down (-Y).
    private StandardMaterial3D? _bellMat, _throatMat, _powerMat;

    private void AddRaptor(string name, Vector3 pos, float exitR, float throatR, float bellLen,
        int bellRings = 4)
    {
        // Warm copper/inconel bell — metallic with a coppery albedo so it catches
        // light as real engine hardware rather than a flat dark cone.
        _bellMat   ??= Mat(new Color(0.42f, 0.27f, 0.18f), 0.95f, 0.34f);
        // Recessed throat: very dark, slightly rough (soot + shadow up the bell).
        _throatMat ??= Mat(new Color(0.06f, 0.055f, 0.05f), 0.55f, 0.70f);
        // Powerhead plumbing: greenish-grey inconel/steel.
        _powerMat  ??= Mat(new Color(0.34f, 0.35f, 0.33f), 0.88f, 0.40f);

        float topR = throatR * 1.25f;        // bell radius at its top (near throat)

        // Bell skirt — a real nozzle is a curved (bell) contour, not a straight
        // cone. Build it from a few stacked frusta whose radii follow a smooth
        // exponential flare from throat (top) to exit (bottom), with plenty of
        // radial segments so it reads as a round, machined bell.
        for (int s = 0; s < bellRings; s++)
        {
            float t0 = (float)s       / bellRings;   // 0 = top (throat), 1 = exit
            float t1 = (float)(s + 1) / bellRings;
            // ease-out flare: most of the widening happens near the exit
            float f0 = Mathf.Pow(t0, 0.62f);
            float f1 = Mathf.Pow(t1, 0.62f);
            float rTop = Mathf.Lerp(topR, exitR, f0);
            float rBot = Mathf.Lerp(topR, exitR, f1);
            float h    = bellLen / bellRings;
            // s=0 is the topmost ring; its centre sits high, exit ring at bottom.
            float yc = pos.Y + bellLen - (s + 0.5f) * h;
            AddMesh($"{name}Bell{s}",
                new CylinderMesh { TopRadius = rTop, BottomRadius = rBot, Height = h * 1.05f, RadialSegments = 36 },
                _bellMat, new Vector3(pos.X, yc, pos.Z));
        }

        // Recessed throat plug just inside the top of the bell — gives the dark
        // hollow the eye expects when looking up a nozzle from below.
        AddMesh($"{name}Throat",
            new CylinderMesh { TopRadius = throatR * 0.7f, BottomRadius = throatR, Height = bellLen * 0.45f, RadialSegments = 24 },
            _throatMat, new Vector3(pos.X, pos.Y + bellLen * 0.78f, pos.Z));

        // Powerhead / turbopump hint: a short wider drum on top of the throat.
        AddMesh($"{name}Power",
            new CylinderMesh { TopRadius = topR * 1.15f, BottomRadius = topR * 1.05f, Height = bellLen * 0.30f, RadialSegments = 24 },
            _powerMat, new Vector3(pos.X, pos.Y + bellLen + bellLen * 0.10f, pos.Z));

        // Two small plumbing nubs flanking the powerhead (turbopumps).
        float nub = topR * 0.9f;
        AddMesh($"{name}Pump0",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 14, Rings = 8 },
            _powerMat, new Vector3(pos.X + nub, pos.Y + bellLen * 1.05f, pos.Z));
        AddMesh($"{name}Pump1",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 14, Rings = 8 },
            _powerMat, new Vector3(pos.X - nub, pos.Y + bellLen * 1.05f, pos.Z));
    }

    private void ClearNodes()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _partNodes.Clear();
        _tileMats.Clear();
        _plumes   = null;
        _hullMesh = null;
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
