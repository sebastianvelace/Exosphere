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
        var ringSteel = SteelMat(new Color(0.42f, 0.41f, 0.42f), 0.86f, 0.45f,
            weldSpacing: 0.9f);
        var ventMat   = Mat(new Color(0.10f, 0.10f, 0.11f), 0.70f, 0.55f);
        AddMesh("Interstage", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = BodyR, Height = 2f, RadialSegments = 64 },
            ringSteel, new Vector3(0, 21f, 0));

        // Vertical vent slots around the hot-stage ring (dark recesses).
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Pi / 12f;
            AddMesh($"Vent{i}", new BoxMesh { Size = new Vector3(0.10f, 1.4f, 0.16f) },
                ventMat, new Vector3(1.14f * RScale * Mathf.Cos(a), 21f, 1.14f * RScale * Mathf.Sin(a)));
        }
        // Lip rings top and bottom of the interstage.
        AddWeldRing("InterLipB", 1.155f * RScale, 20.1f);
        AddWeldRing("InterLipT", 1.155f * RScale, 21.9f);

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
            weldSpacing: 1.6f, sootBot: -9f, sootTop: -5.5f);
        _hullMesh = AddMesh("SHBody", new CylinderMesh
            { TopRadius = BodyR, BottomRadius = BodyR, Height = 18f, RadialSegments = 64 },
            shSteel, new Vector3(0, 11f, 0));
        AddWeldRings("SHBarrelWeld", BodyR + 0.018f, 3.1f, 19.0f, 9);
        AddHullRing("SHFrostLOX", BodyR + 0.026f, 15.8f, 0.10f, FrostMat);

        // Raceway / conduit running up one side of the booster (real SH detail).
        AddMesh("SHRaceway", new BoxMesh { Size = new Vector3(0.20f, 16.5f, 0.34f) },
            darkSteel, new Vector3(BodyR + 0.01f, 11f, 0f));
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
            scorched, new Vector3(0, 21f, 0));

        // Vertical vent slots around the ring — these are the open passages the
        // Ship's exhaust blew through during hot-staging.
        for (int i = 0; i < 24; i++)
        {
            float a = i * Mathf.Pi / 12f;
            AddMesh($"HotVent{i}", new BoxMesh { Size = new Vector3(0.11f, 1.5f, 0.18f) },
                ventMat, new Vector3((BodyR - 0.02f) * Mathf.Cos(a), 21f, (BodyR - 0.02f) * Mathf.Sin(a)));
        }

        // Burnt separation lip capping the exposed ring (the torn separation plane).
        AddMesh("SepLip", new CylinderMesh
            { TopRadius = BodyR + 0.02f, BottomRadius = BodyR + 0.02f, Height = 0.22f, RadialSegments = 48 },
            lipMat, new Vector3(0, 22.05f, 0));
    }

    private void AddBoosterLongitudinalSeams(Material mat)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f + Mathf.Pi / 8f;
            AddSurfaceBox($"SHLongSeam{i}", a, 11.3f, 15.2f, 0.030f, 0.10f, mat, BodyR + 0.030f);
        }
    }

    // ── 4 grid fins near top of Super Heavy ──────────────────────────────

    private void AddSHGridFins()
    {
        // Real SH grid fins: 4 near the top, offset ~90° apart, slightly
        // forward of the body. Built from a mount arm + a thin lattice slab.
        var finMat   = Mat(new Color(0.40f, 0.40f, 0.43f), 0.90f, 0.34f);
        var mountMat = Mat(new Color(0.34f, 0.34f, 0.37f), 0.86f, 0.42f);
        var gridMat  = Mat(new Color(0.18f, 0.18f, 0.20f), 0.86f, 0.45f);

        for (int i = 0; i < 4; i++)
        {
            float a   = i * Mathf.Pi * 0.5f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);

            // Mount hinge/arm against the hull.
            AddMesh($"GridFinMount{i}", new BoxMesh { Size = new Vector3(0.55f, 1.3f, 0.70f) },
                mountMat, new Vector3((BodyR + 0.03f) * cos, 18.6f, (BodyR + 0.03f) * sin));

            // The lattice slab itself, projecting outward (the recognisable fin).
            var fin = new MeshInstance3D
            {
                Name            = $"GridFin{i}",
                Mesh            = new BoxMesh { Size = new Vector3(1.45f, 1.65f, 0.16f) },
                Position        = new Vector3((BodyR + 0.63f) * cos, 18.8f, (BodyR + 0.63f) * sin),
                RotationDegrees = new Vector3(0, -i * 90f, 0),
            };
            fin.SetSurfaceOverrideMaterial(0, finMat);
            AddChild(fin);

            // Grid lattice ribs on the fin face. These small raised bars make the
            // fins read as real open grid fins instead of flat paddles.
            for (int r = -2; r <= 2; r++)
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_RibH{r}",
                    Mesh = new BoxMesh { Size = new Vector3(1.30f, 0.045f, 0.19f) },
                    Position = new Vector3(0f, r * 0.26f, -0.01f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
            for (int c = -2; c <= 2; c++)
            {
                var rib = new MeshInstance3D
                {
                    Name = $"GridFin{i}_RibV{c}",
                    Mesh = new BoxMesh { Size = new Vector3(0.045f, 1.42f, 0.20f) },
                    Position = new Vector3(c * 0.25f, 0f, -0.02f),
                };
                rib.SetSurfaceOverrideMaterial(0, gridMat);
                fin.AddChild(rib);
            }
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
        var tiles     = TileMat();
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);

        float o = yOffset;

        // ── Body barrel (steel) y=o+24 → o+38 ─────────────────────────────
        // The windward (one) side is black heat-shield tiles; the leeward side
        // stays bare steel. We model this as ONE continuous full-height steel
        // barrel (no upper/lower seam) plus a tile "shell" half wrapping the
        // windward (-X / forward) side. Body-local y runs [-7, +7]; the shader
        // adds weld banding so the long tube doesn't read flat.
        var shipSteel = SteelMat(new Color(0.88f, 0.88f, 0.90f), 0.93f, 0.16f,
            weldSpacing: 1.55f);
        _hullMesh = AddMesh("Body",
            new CylinderMesh { TopRadius = BodyR, BottomRadius = BodyR, Height = 14f, RadialSegments = 64 },
            shipSteel, new Vector3(0, o + 31f, 0));
        AddWeldRings("ShipBarrelWeld", BodyR + 0.018f, o + 24.8f, o + 37.3f, 7);
        AddHullRing("ShipFrostLOX", BodyR + 0.026f, o + 34.2f, 0.08f, FrostMat);
        AddHullRing("ShipFrostCH4", BodyR + 0.026f, o + 28.0f, 0.07f, FrostMat);

        // Leeward external raceway/cable cover. It gives the upper stage an
        // asymmetric real-vehicle cue without changing the windward tile side.
        AddSurfaceBox("ShipRaceway", angle: 0f, y: o + 31.0f, height: 11.4f,
            width: 0.18f, depth: 0.18f, mat: darkSteel, radius: BodyR + 0.045f);
        AddPayloadDoorOutline(o, darkSteel);

        // Windward black-tile band: a slightly larger half-cylinder shell on the
        // -X side, running the full body height. Built from short tile staves so
        // the dark heat-shield reads clearly on one side only.
        AddTileBand(o + 24f, o + 38f);
        AddHeatShieldBorder(o + 24f, o + 38f, BodyR + 0.035f);

        // ── Ogive nosecone (smooth multi-segment taper) ───────────────────
        // Real Starship nose is a smooth tangent ogive. Build it from many short
        // frusta whose radii follow the ogive curve; with enough segments and a
        // slight vertical overlap the profile reads round, not faceted. The
        // shared-vertex radii match exactly across joints so there are no steps.
        // Base y=o+38, tip y≈o+43.2 (5.2 units ≈ 14.6 m).
        const int   noseSeg  = 22;
        const float noseBase = 38f;          // body-relative base
        const float noseLen  = 5.2f;
        const float noseR    = BodyR;        // base radius matches the 9 m body
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
        AddTileBand(o + 38f, o + 41.5f, topRadius: 0.83f * RScale, botRadius: BodyR + 0.01f);
        AddHeatShieldBorder(o + 38f, o + 41.5f, BodyR + 0.030f);

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

        // Engine skirt (y=o+22 → o+24)
        AddMesh("Skirt",
            new CylinderMesh { TopRadius = BodyR, BottomRadius = 1.08f * RScale, Height = 2f, RadialSegments = 48 },
            darkSteel, new Vector3(0, o + 23f, 0));
        AddWeldRing("SkirtLip", 1.155f * RScale, o + 24f);

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
            { TopRadius = 1.08f * RScale, BottomRadius = 1.10f * RScale, Height = 0.9f, RadialSegments = 48 },
            sootSteel, new Vector3(0, o + 22.4f, 0));
        AddAftShieldSkirt(o, sootSteel);

        // 3 vacuum Raptors (inner) + 3 sea-level (outer). Ring radii scale with
        // the wider 9 m hull so the six bells stay spread under the skirt.
        const float vacR = 0.38f * RScale;
        const float slR  = 0.72f * RScale;
        float bellY = o + 22f - 1.05f;

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

    private void AddPayloadDoorOutline(float o, Material mat)
    {
        // Subtle leeward payload-bay/maintenance panel outline. It gives the
        // stainless side real scale cues without turning into visible text.
        const float a = 0f; // leeward side opposite the windward heat shield
        float r = BodyR + 0.052f;
        AddSurfaceBox("PayloadDoorLeft",  a - 0.17f, o + 34.0f, 7.0f, 0.030f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorRight", a + 0.17f, o + 34.0f, 7.0f, 0.030f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorTop",   a, o + 37.5f, 0.04f, 0.56f, 0.12f, mat, r);
        AddSurfaceBox("PayloadDoorBottom",a, o + 30.5f, 0.04f, 0.56f, 0.12f, mat, r);
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
        AddSurfaceBox("ShipAftBlackWrapL", Mathf.Pi - 0.72f, o + 24.2f, 1.5f, 0.30f, 0.15f, mat, BodyR + 0.055f);
        AddSurfaceBox("ShipAftBlackWrapC", Mathf.Pi,         o + 24.0f, 1.7f, 0.38f, 0.15f, mat, BodyR + 0.055f);
        AddSurfaceBox("ShipAftBlackWrapR", Mathf.Pi + 0.72f, o + 24.2f, 1.5f, 0.30f, 0.15f, mat, BodyR + 0.055f);
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
