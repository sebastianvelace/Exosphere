namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Presentation;
using Exosphere.Simulation.Propulsion;

public partial class VesselRenderer : Node3D
{
    public Vessel? TargetVessel { get; set; }

    private readonly Dictionary<string, Node3D> _partNodes = new();
    private readonly Dictionary<string, Node3D> _parachuteVisuals = new();
    private MeshInstance3D? _hullMesh;
    private PlumeSystem?    _plumes;
    private bool _usesGenericPlumes;
    private bool _hasSuperHeavy;
    private int _selectedShipEngines = 6;
    private readonly HashSet<string> _superHeavyEngineIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shipEngineIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _perEngineThrottle = new(StringComparer.Ordinal);
    private readonly List<EngineReadout> _engineReadoutScratch = new();
    private double _engineVisualTimer;
    private double _thermalVisualTimer;
    private double _landingGearStateTimer;
    private double _parachuteStateTimer;
    private bool _landingGearDeployed;
    private float _cachedFlapDeployment;
    private float _cachedFlapPitch;
    private float _cachedFlapRoll;
    private double _flapInputTimer;
    private double _landingGearMotionDelta;
    private float _lastGlow = float.NaN;
    private bool? _lastVisualHotStage;
    private float _lastVisualSuperHeavyThrottle = float.NaN;
    private float _lastVisualShipThrottle = float.NaN;
    private CelestialBody? _cachedPresentationBody;
    private double _cachedPresentationAltitude;
    private double _cachedPresentationPressureRatio;
    private double _presentationSampleTimer;
    private static EngineDefinitionCatalog? _engineCatalog;
    private static bool _engineCatalogLoadAttempted;

    private const double EngineVisualPeriodSeconds = 1.0 / 30.0;
    private const double ThermalVisualPeriodSeconds = 1.0 / 15.0;
    private const double SecondaryVisualPeriodSeconds = 1.0 / 20.0;
    private const double PresentationSamplePeriodSeconds = 1.0 / 20.0;

    private sealed class FlapRig
    {
        public MeshInstance3D Blade = null!;
        public Quaternion Neutral;
        public float Side;
        public bool Forward;
    }

    // The four aerodynamic surfaces are live visual instruments: their deployment follows
    // dynamic pressure, heat-shield attitude and control mixing instead of remaining frozen.
    private readonly List<FlapRig> _flapRigs = new();

    private sealed class LandingLegRig
    {
        public Node3D Root = null!;
        public MeshInstance3D Strut = null!;
        public MeshInstance3D Foot = null!;
        public int Index;
        public float FullLength;
        public float LastVisibleLength = -1f;
        public double CompressionM;
    }

    private readonly List<LandingLegRig> _landingLegRigs = new();
    private float _landingGearDeployment;

    private enum TileCharZone { Belly, Nose, FwdFlap, AftFlap }

    // Windward heat-shield tile materials grouped by zone so belly, nose and flaps
    // char at different rates during re-entry.
    private readonly Dictionary<TileCharZone, List<ShaderMaterial>> _tileZoneMats = new();
    private readonly List<ShaderMaterial> _shipSteelMats = new();
    private readonly List<ShaderMaterial> _steelMats = new();
    private static readonly Color TileBaseColor = new(0.070f, 0.068f, 0.072f);
    private OmniLight3D? _engineBayLight;
    private OmniLight3D? _engineRimLight;
    private OmniLight3D? _skyFillLight;

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

        bool hasSH = vessel.Parts.Parts.Any(
            p => p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster"));
        bool hasStarship = vessel.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily
            && (p.Definition.HasVehicleRole("ship_engines")
                || p.Definition.HasVehicleRole("command")));
        bool hasFalcon9 = vessel.Parts.Parts.Any(p => string.Equals(
            p.Definition.VehicleFamily, "falcon9", StringComparison.OrdinalIgnoreCase));
        bool hasNewGlenn = vessel.Parts.Parts.Any(p => string.Equals(
            p.Definition.VehicleFamily, "newglenn", StringComparison.OrdinalIgnoreCase));
        _hasSuperHeavy = hasSH;
        BuildEngineVisualGroups(vessel);
        _selectedShipEngines = vessel.Parts.Parts
            .FirstOrDefault(p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"))?.SelectedEngineCount ?? 6;
        if      (hasSH && hasStarship) BuildFullStack(vessel);
        else if (hasSH)                BuildSuperHeavyOnly(vessel);
        else if (hasStarship)          BuildStarshipSection(vessel, yOffset: -22f);
        else if (hasFalcon9)           BuildFalcon9Section(vessel);
        else if (hasNewGlenn)          BuildNewGlennSection(vessel);
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
        var darkSteel = SteelMat(new Color(0.46f, 0.46f, 0.49f), 0.58f, 0.36f,
            weldSpacing: 2.4f);
        // Soot-darkened steel for the engine skirt: heat/exhaust staining.
        var sootSteel = SteelMat(new Color(0.22f, 0.21f, 0.20f), 0.48f, 0.55f,
            weldSpacing: 1.6f, sootBot: -1.2f, sootTop: 1.1f);

        // Main body (y=2 → y=20), a single tall barrel. Body-local y runs
        // [-9, +9]; soot fades in over the bottom ~3 units (toward the engines).
        var shSteel = SteelMat(new Color(0.68f, 0.67f, 0.65f), 0.22f, 0.32f,
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
        AddSHChines(darkSteel);

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
        AttachVehicleLights(shBellY, superHeavy: true);
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
        var finMat   = SteelMat(new Color(0.48f, 0.47f, 0.46f), 0.58f, 0.34f, weldSpacing: 0.55f);
        var mountMat = SteelMat(new Color(0.30f, 0.30f, 0.32f), 0.55f, 0.40f, weldSpacing: 0.8f);
        var gridMat  = SteelMat(new Color(0.12f, 0.12f, 0.13f), 0.42f, 0.55f, weldSpacing: 0.4f);

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
        var fwdFlapTiles = TileMat();
        RegisterTileMat(TileCharZone.FwdFlap, fwdFlapTiles);
        var aftFlapTiles = TileMat();
        RegisterTileMat(TileCharZone.AftFlap, aftFlapTiles);
        var darkSteel = Mat(new Color(0.50f, 0.50f, 0.53f), 0.88f, 0.32f);

        float o = yOffset;
        float bodyMid = o + ShipBodyBase + ShipBodyH * 0.5f;
        float bodyTop = o + ShipNoseBase;
        float bodyBot = o + ShipBodyBase;
        float fwdFlapY = o + ShipNoseBase - 1.5f;
        float aftFlapY = o + ShipBodyBase + 2.4f;
        float skirtMid = o + ShipSkirtBase + ShipSkirtH * 0.5f;
        float skirtTop = o + ShipBodyBase;

        var shipSteel = SteelMat(new Color(0.74f, 0.74f, 0.76f), 0.24f, 0.26f, weldSpacing: 1.55f);
        _shipSteelMats.Add(shipSteel);
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

        AddTileBand(bodyBot, bodyTop, TileCharZone.Belly);
        AddHeatShieldBorder(bodyBot, bodyTop, BodyR + 0.035f);

        const int   noseSeg  = 22;
        const float noseBase = ShipNoseBase;
        const float noseLen  = ShipNoseH;
        const float noseR    = BodyR;
        var noseSteel = SteelMat(new Color(0.74f, 0.74f, 0.76f), 0.24f, 0.26f,
            weldSpacing: 1.3f);
        _shipSteelMats.Add(noseSteel);
        // Ogive profile: a circular-arc shape. Using a near-tangent-ogive gives
        // a fuller, more realistic Starship nose than a simple sqrt curve.
        float OgiveR(float u)                // u in [0,1], 0=base 1=tip
            => (float)VehicleVisualPhysics.TangentOgiveRadius(u, noseR, noseLen);
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

        // Unlike a cylindrical tile band, these short rows follow the local
        // ogive radius all the way to the tip and preserve the tapered silhouette.
        AddNoseTileShell(o + ShipNoseBase, ShipNoseH, OgiveR);

        AddMesh("NoseTip",
            new SphereMesh { Radius = 0.085f, Height = 0.17f,
                RadialSegments = 24, Rings = 8 }, noseSteel,
            new Vector3(0, o + noseBase + noseLen - 0.055f, 0));

        AddMesh("Skirt",
            new CylinderMesh { TopRadius = BodyR, BottomRadius = 1.08f * RScale, Height = ShipSkirtH, RadialSegments = 48 },
            darkSteel, new Vector3(0, skirtMid, 0));
        AddWeldRing("SkirtLip", 1.155f * RScale, skirtTop);

        // Flight 7-ish proportions: forward canards shorter/narrower; aft elevons longer
        // with deeper chord, seated closer to the belly so the silhouette reads Starship.
        AddFlap("FwdFlapL", fwdFlapY + 0.15f, 2.55f, 1.72f, -0.58f, fwdFlapTiles);
        AddFlap("FwdFlapR", fwdFlapY + 0.15f, 2.55f, 1.72f,  0.58f, fwdFlapTiles);
        AddFlap("AftFlapL", aftFlapY - 0.20f, 6.35f, 3.95f, -0.48f, aftFlapTiles);
        AddFlap("AftFlapR", aftFlapY - 0.20f, 6.35f, 3.95f,  0.48f, aftFlapTiles);
        var sootSteel = Mat(new Color(0.20f, 0.19f, 0.19f), 0.70f, 0.62f);
        AddMesh("ShipBaySoot", new CylinderMesh
            { TopRadius = 1.08f * RScale, BottomRadius = 1.10f * RScale, Height = 0.9f, RadialSegments = 48 },
            sootSteel, new Vector3(0, o + ShipSkirtBase + 0.4f, 0));
        AddAftShieldSkirt(o, sootSteel);
        if (vessel.Parts.Parts.Any(p => p.Definition.Id == "starship_landing_gear"))
            AddStarshipLandingGear(o + ShipSkirtBase);

        const float slR  = 0.38f * RScale; // three compact central gimballing engines
        const float vacR = 0.72f * RScale; // three large outer vacuum engines
        const float vacExitR = 0.43f;
        const float slExitR = 0.25f;
        const float vacBellLen = 1.15f;
        const float slBellLen = 0.72f;
        float bellY = o + ShipSkirtBase - 1.05f;
        float vacExitY = bellY - 0.30f;
        float slExitY = bellY + 0.35f;

        AddMesh("ShipThrustPuck", new CylinderMesh
            { TopRadius = 1.18f, BottomRadius = 1.28f, Height = 0.24f,
                RadialSegments = 48 }, sootSteel,
            new Vector3(0, o + ShipSkirtBase - 0.02f, 0));

        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            AddRaptor($"RapVac{i}",
                new Vector3(vacR * Mathf.Cos(a), vacExitY, vacR * Mathf.Sin(a)),
                exitR: vacExitR, throatR: 0.13f, bellLen: vacBellLen, bellRings: 8);
        }

        // 3 sea-level Raptors (central, shorter gimballing bells)
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddRaptor($"RapSL{i}",
                new Vector3(slR * Mathf.Cos(a), slExitY, slR * Mathf.Sin(a)),
                exitR: slExitR, throatR: 0.095f, bellLen: slBellLen, bellRings: 6);
        }

        // GPU plumes for Starship engines
        if (_plumes == null) { _plumes = new PlumeSystem { Name = "Plumes" }; AddChild(_plumes); }
        _plumes.SetupStarship(vacR, slR, vacExitY, slExitY, vacExitR, slExitR);
        if (_engineBayLight == null)
            AttachVehicleLights(slExitY, superHeavy: false);

        if (yOffset == -22f)
        {
            foreach (var part in vessel.Parts.Parts)
                _partNodes[part.InstanceId] = _hullMesh!;
        }
    }

    // ── Falcon 9 Block 5 dedicated visual path ────────────────────────────
    //
    // Reuses the same helper toolkit as Starship/Super Heavy — SteelMat/Mat,
    // AddWeldRing(s), AddHullRing, AddSurfaceBox, and the AddRaptor
    // stacked-frusta bell technique (adapted below into AddSimpleEngineBell) — instead
    // of falling through the flat-cylinder generic path. Falcon 9's first
    // stage is painted white/off-white bare aluminum with visible welds and
    // stringer rings, not Starship's raw brushed steel, so the hull uses a
    // plain Mat() white material with weld-ring/raceway accents rather than
    // SteelMat's brushed-anisotropy shader (that shader's look is tuned for
    // exposed stainless, which would read wrong for a painted booster).
    //
    // Reutiliza el mismo set de helpers de Starship/Super Heavy en vez de
    // caer al camino genérico de cilindros planos. La primera etapa de
    // Falcon 9 es aluminio pintado blanco, no acero crudo como Starship, así
    // que usamos Mat() blanco liso en vez del shader de acero.
    private void BuildFalcon9Section(Vessel vessel)
    {
        _usesGenericPlumes = true;
        var positions = vessel.Parts.ComputePartLocalPositions();
        double minimumY = positions.Count == 0
            ? 0.0
            : positions.Min(pair => pair.Value.Y - pair.Key.Definition.LengthM * 0.5);
        double datumShiftY = -minimumY;
        float ToU(double m) => (float)(m / MetresPerUnit);
        float YOf(Vector3d local) => ToU(local.Y + datumShiftY);

        var hullWhite       = Mat(new Color(0.90f, 0.90f, 0.87f), 0.20f, 0.42f);
        var darkTrim        = Mat(new Color(0.40f, 0.40f, 0.43f), 0.85f, 0.40f);
        var interstageBlack = Mat(new Color(0.035f, 0.035f, 0.04f), 0.22f, 0.55f);
        var fairingWhite    = Mat(new Color(0.94f, 0.94f, 0.93f), 0.05f, 0.28f);
        var sootMat         = Mat(new Color(0.11f, 0.10f, 0.095f), 0.32f, 0.70f);
        var octawebMat      = Mat(new Color(0.24f, 0.24f, 0.26f), 0.80f, 0.46f);

        MeshInstance3D? firstStageMesh = null;

        foreach (var (part, localPos) in positions)
        {
            var definition = part.Definition;
            float y = YOf(localPos);
            float halfH = ToU(definition.LengthM * 0.5);
            float radius = ToU(definition.DiameterM * 0.5);

            switch (definition.Id)
            {
                case "falcon9_first_stage_tank":
                {
                    firstStageMesh = AddMesh("F9FirstStage",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 56,
                            // Top butts against the interstage below the same
                            // radius — suppress this cap so the two coincident
                            // discs don't z-fight into a bright seam line.
                            CapTop = false,
                        },
                        hullWhite, new Vector3(0, y, 0));
                    // Visible stringer/ring frames along the barrel.
                    AddWeldRings("F9Stringer", radius + 0.012f,
                        y - halfH + 1.2f, y + halfH - 1.4f, 9);
                    // The single iconic vertical raceway duct.
                    AddSurfaceBox("F9Raceway", angle: 0f, y: y,
                        height: halfH * 1.86f, width: 0.085f, depth: 0.15f,
                        mat: darkTrim, radius: radius + 0.012f);
                    // Static scorch/soot gradient near the octaweb base — a
                    // short darker band blended with the white paint rather
                    // than Starship's live thermal charring.
                    AddHullRing("F9SootBand", radius + 0.006f,
                        y - halfH + 0.55f, 1.05f, sootMat);
                    break;
                }
                case "falcon9_interstage":
                {
                    // Real interstage is a black composite shroud, subtly
                    // tapered rather than a flat cylinder.
                    AddMesh("F9Interstage",
                        new CylinderMesh
                        {
                            TopRadius = radius * 0.86f, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 48,
                            // Same seam fix: top meets the second stage's
                            // bottom cap, bottom meets the first stage's
                            // (already-suppressed) top — only one side of
                            // each junction may keep a cap.
                            CapTop = false,
                        },
                        interstageBlack, new Vector3(0, y, 0));
                    AddF9GridFins(y - halfH * 0.35f, radius);
                    break;
                }
                case "falcon9_second_stage_tank":
                {
                    AddMesh("F9SecondStage",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 48,
                        },
                        hullWhite, new Vector3(0, y, 0));
                    AddWeldRings("F9UpperStringer", radius + 0.012f,
                        y - halfH + 0.6f, y + halfH - 0.6f, 3);
                    break;
                }
                case "merlin1d_cluster9_block5":
                {
                    // Octaweb thrust structure: a dark, wide disc the nine
                    // Merlins mount to, scorched at its rim.
                    AddMesh("F9Octaweb",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius * 1.03f,
                            Height = halfH * 2f, RadialSegments = 48,
                            CapTop = false,
                        },
                        octawebMat, new Vector3(0, y, 0));
                    AddHullRing("F9OctawebSoot", radius * 1.035f,
                        y - halfH * 0.55f, halfH * 0.9f, sootMat);
                    break;
                }
                case "falcon9_fairing_standard":
                case "falcon9_fairing_extended":
                {
                    BuildFalcon9Fairing(y, halfH * 2f, radius, fairingWhite, darkTrim);
                    break;
                }
                case "merlin1d_vac_block5":
                {
                    // Simple mount plate; the vacuum bell itself is added
                    // below alongside the octaweb engines, from cluster data.
                    AddMesh("F9SecondStageMount",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius * 0.9f,
                            Height = halfH * 2f, RadialSegments = 40,
                            CapTop = false,
                        },
                        octawebMat, new Vector3(0, y, 0));
                    break;
                }
                default:
                {
                    // Payload simulator and anything unanticipated: keep a
                    // plain neutral cylinder rather than inventing new detail.
                    AddMesh($"F9_{definition.Id}",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 32,
                        },
                        darkTrim, new Vector3(0, y, 0));
                    break;
                }
            }
        }

        _hullMesh = firstStageMesh;
        Node3D fallbackNode = (Node3D?)_hullMesh ?? this;
        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = fallbackNode;

        // Merlin engine bells, positioned from the same engine-cluster JSON
        // data the physics/torque model already uses (real octaweb layout —
        // 1 centre + 8 outer — and the single second-stage MVac), not an
        // invented layout.
        var catalog = GetEngineCatalog();
        if (catalog == null) return;
        // Sea-level Merlin bells read as brushed steel/silver; the vacuum
        // Merlin's larger niobium-alloy extension oxidizes to a duller,
        // warmer bronze-brown.
        var merlinSlMat  = Mat(new Color(0.55f, 0.55f, 0.57f), 0.85f, 0.32f);
        var merlinVacMat = Mat(new Color(0.44f, 0.34f, 0.24f), 0.55f, 0.55f);
        var merlinNubMat = Mat(new Color(0.30f, 0.31f, 0.29f), 0.82f, 0.42f);
        var plumeMounts = new List<PlumeSystem.EnginePlumeMount>();
        foreach (var enginePart in vessel.Parts.Parts.Where(p =>
                     p.HasEngineRuntime
                     && !string.IsNullOrWhiteSpace(p.Definition.EngineClusterId)))
        {
            if (!positions.TryGetValue(enginePart, out var partPosition)
                || !catalog.Clusters.TryGetValue(
                    enginePart.Definition.EngineClusterId, out var cluster))
                continue;

            bool vacuum = enginePart.Definition.Id == "merlin1d_vac_block5";
            float exitRadius = enginePart.Definition.EngineCount > 1
                ? (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM
                    / (2.0 * System.Math.Sqrt(enginePart.Definition.EngineCount)),
                    0.22, 0.62)
                : (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM * 0.32, 0.35, 0.95);
            float throatRadius = vacuum ? exitRadius * 0.12f : exitRadius * 0.30f;
            float bellLen = vacuum ? exitRadius * 2.7f : exitRadius * 1.35f;
            float exitY = YOf(partPosition) - ToU(enginePart.Definition.LengthM * 0.5);

            int count = System.Math.Min(
                enginePart.EngineStates.Count, cluster.Engines.Count);
            for (int i = 0; i < count; i++)
            {
                var local = cluster.Engines[i].Position;
                var position = new Vector3(
                    ToU(partPosition.X + local.X),
                    exitY + ToU(local.Y),
                    ToU(partPosition.Z + local.Z));
                string id = enginePart.EngineStates[i].InstanceId;
                plumeMounts.Add(new PlumeSystem.EnginePlumeMount(
                    id, position, exitRadius / MetresPerUnit, vacuum));
                AddSimpleEngineBell(
                    $"Merlin_{id.Replace(':', '_')}", position,
                    exitRadius, throatRadius, bellLen,
                    vacuum ? merlinVacMat : merlinSlMat, merlinNubMat);
            }
        }

        if (plumeMounts.Count > 0)
        {
            _plumes = new PlumeSystem { Name = "EnginePlumes" };
            AddChild(_plumes);
            _plumes.SetupGenericCluster(plumeMounts);
        }
    }

    // A proper tangent-ogive nose over a cylindrical barrel (not a rounded
    // CapsuleMesh) with a visible longitudinal separation line for the
    // two-piece clamshell fairing.
    private void BuildFalcon9Fairing(
        float centerY, float totalHeight, float radius,
        Material hullMat, Material seamMat)
    {
        float bottom = centerY - totalHeight * 0.5f;
        float barrelH = totalHeight * 0.42f;
        float noseH   = totalHeight - barrelH;
        float barrelTop = bottom + barrelH;

        AddMesh("F9FairingBarrel",
            new CylinderMesh
            {
                TopRadius = radius, BottomRadius = radius,
                Height = barrelH, RadialSegments = 48,
                CapBottom = false,
            },
            hullMat, new Vector3(0, bottom + barrelH * 0.5f, 0));

        const int noseSeg = 14;
        float NoseR(float u) =>
            (float)VehicleVisualPhysics.TangentOgiveRadius(u, radius, noseH);
        for (int i = 0; i < noseSeg; i++)
        {
            float u0 = (float)i / noseSeg;
            float u1 = (float)(i + 1) / noseSeg;
            float rBot = NoseR(u0);
            float rTop = NoseR(u1);
            float segH = noseH / noseSeg;
            float yMid = barrelTop + (u0 + u1) * 0.5f * noseH;
            AddMesh($"F9FairingNose{i}",
                new CylinderMesh
                {
                    TopRadius = rTop, BottomRadius = rBot,
                    Height = segH * 1.05f, RadialSegments = 48,
                    CapTop = i == noseSeg - 1, CapBottom = false,
                },
                hullMat, new Vector3(0, yMid, 0));
        }

        // Circumferential seam at the barrel/ogive transition.
        AddHullRing("F9FairingRing", radius + 0.01f, barrelTop, 0.05f, seamMat);
        // Two longitudinal separation lines (the real clamshell split plane).
        foreach (float angle in new[] { 0f, Mathf.Pi })
            AddSurfaceBox($"F9FairingSeam{(int)Mathf.RadToDeg(angle)}", angle, centerY,
                totalHeight * 0.98f, 0.02f, 0.08f, seamMat, radius + 0.012f);
    }

    // Four fixed grid fins near the top of the first stage — smaller and a
    // simpler solid/perforated titanium grid than Starship's larger hinged
    // fins, adapted from the same BuildGridFinPlateMesh geometry.
    private void AddF9GridFins(float y, float bodyRadius)
    {
        var finMat = Mat(new Color(0.52f, 0.52f, 0.54f), 0.55f, 0.55f);
        var mountMat = Mat(new Color(0.30f, 0.30f, 0.32f), 0.80f, 0.45f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);
            float deg = -Mathf.RadToDeg(a);

            AddMesh($"F9GridMount{i}",
                new BoxMesh { Size = new Vector3(0.22f, 0.45f, 0.30f) },
                mountMat, new Vector3((bodyRadius + 0.01f) * cos, y, (bodyRadius + 0.01f) * sin));

            var fin = new MeshInstance3D
            {
                Name = $"F9GridFin{i}",
                Mesh = BuildGridFinPlateMesh(
                    rootChord: 0.62f, tipChord: 0.48f, height: 0.68f, thickness: 0.06f),
                Position = new Vector3(
                    (bodyRadius + 0.30f) * cos, y + 0.05f, (bodyRadius + 0.30f) * sin),
                RotationDegrees = new Vector3(0f, deg, 0f),
            };
            fin.SetSurfaceOverrideMaterial(0, finMat);
            AddChild(fin);
        }
    }

    // Single-frustum engine bell: a clean bell-to-throat taper (no separate
    // throat plug/lattice like Raptor) plus a small gimbal-actuator/turbopump
    // nub. Modeled on AddRaptor's structure but simplified to one frustum
    // instead of stacked rings, since neither a Merlin nor a BE-4/BE-3U bell
    // has Raptor's pronounced contoured shape. Takes its bell/nub materials as
    // parameters rather than hardcoding a Merlin-specific palette, so Falcon 9
    // and New Glenn (different engines, different colors) can share the same
    // geometry without one family's engines rendering in the other's finish.
    private void AddSimpleEngineBell(
        string name, Vector3 pos, float exitR, float throatR, float bellLen,
        Material bellMat, Material nubMat)
    {
        float topR = throatR * 1.15f;
        AddMesh($"{name}Bell",
            new CylinderMesh
            {
                TopRadius = topR, BottomRadius = exitR, Height = bellLen,
                RadialSegments = 32, CapTop = false, CapBottom = false,
            },
            bellMat, pos + Vector3.Up * (bellLen * 0.5f));

        AddMesh($"{name}ExitLip",
            new CylinderMesh
            {
                TopRadius = exitR * 1.02f, BottomRadius = exitR * 1.02f,
                Height = 0.04f, RadialSegments = 32, CapTop = false, CapBottom = false,
            },
            bellMat, pos + Vector3.Up * 0.02f);

        AddMesh($"{name}Throat",
            new CylinderMesh
            {
                TopRadius = throatR * 0.8f, BottomRadius = throatR,
                Height = bellLen * 0.35f, RadialSegments = 20,
            },
            nubMat, pos + Vector3.Up * (bellLen + bellLen * 0.16f));

        // Compact gimbal actuator / turbopump nub above the throat.
        AddMesh($"{name}Actuator",
            new SphereMesh { Radius = topR * 0.65f, Height = topR * 1.3f, RadialSegments = 12, Rings = 8 },
            nubMat, pos + Vector3.Up * (bellLen + bellLen * 0.42f));
    }

    // ── New Glenn dedicated visual path ────────────────────────────────────
    //
    // Same toolkit and same seam-avoidance discipline as BuildFalcon9Section —
    // continuous per-part barrels, panel-line rings instead of Starship's
    // brushed-steel weld bands (New Glenn's airframe is composite, not exposed
    // metal), a flared dark boattail around the 7-engine BE-4 cluster, and
    // fixed reentry strakes instead of Falcon 9's grid fins (New Glenn does
    // not use a grid-fin descent system). 7 m diameter throughout — unlike
    // Falcon 9, the fairing does not step out from the booster diameter, so
    // no diameter transition geometry is needed at that joint.
    //
    // Mismo set de herramientas y misma disciplina anti-costura que
    // BuildFalcon9Section. El fuselaje de New Glenn es compuesto (paneles,
    // no soldaduras de metal expuesto) y usa strakes fijos en vez de rejillas.
    private void BuildNewGlennSection(Vessel vessel)
    {
        _usesGenericPlumes = true;
        var positions = vessel.Parts.ComputePartLocalPositions();
        double minimumY = positions.Count == 0
            ? 0.0
            : positions.Min(pair => pair.Value.Y - pair.Key.Definition.LengthM * 0.5);
        double datumShiftY = -minimumY;
        float ToU(double m) => (float)(m / MetresPerUnit);
        float YOf(Vector3d local) => ToU(local.Y + datumShiftY);

        var hullWhite       = Mat(new Color(0.87f, 0.89f, 0.92f), 0.22f, 0.40f);
        var darkTrim        = Mat(new Color(0.38f, 0.39f, 0.42f), 0.80f, 0.42f);
        var interstageBlack = Mat(new Color(0.04f, 0.04f, 0.045f), 0.24f, 0.52f);
        var fairingWhite    = Mat(new Color(0.95f, 0.95f, 0.95f), 0.05f, 0.26f);
        var sootMat         = Mat(new Color(0.10f, 0.095f, 0.09f), 0.30f, 0.68f);
        var boattailMat     = Mat(new Color(0.14f, 0.14f, 0.16f), 0.55f, 0.55f);

        MeshInstance3D? firstStageMesh = null;

        foreach (var (part, localPos) in positions)
        {
            var definition = part.Definition;
            float y = YOf(localPos);
            float halfH = ToU(definition.LengthM * 0.5);
            float radius = ToU(definition.DiameterM * 0.5);

            switch (definition.Id)
            {
                case "newglenn_first_stage_tank":
                {
                    firstStageMesh = AddMesh("NGFirstStage",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 56,
                            // Top butts the interstage at the same radius —
                            // suppress this cap (same coincident-cap fix as
                            // Falcon 9's first stage).
                            CapTop = false,
                        },
                        hullWhite, new Vector3(0, y, 0));
                    // Composite panel-line rings (not weld seams — New Glenn's
                    // airframe is carbon composite, not machined metal).
                    AddWeldRings("NGPanelLine", radius + 0.012f,
                        y - halfH + 1.6f, y + halfH - 1.8f, 7);
                    AddSurfaceBox("NGRaceway", angle: 0f, y: y,
                        height: halfH * 1.82f, width: 0.10f, depth: 0.16f,
                        mat: darkTrim, radius: radius + 0.012f);
                    AddNGStrakes(y + halfH * 0.30f, radius);
                    break;
                }
                case "newglenn_interstage":
                {
                    AddMesh("NGInterstage",
                        new CylinderMesh
                        {
                            TopRadius = radius * 0.90f, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 48,
                            CapTop = false,
                        },
                        interstageBlack, new Vector3(0, y, 0));
                    break;
                }
                case "newglenn_second_stage_tank":
                {
                    AddMesh("NGSecondStage",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 48,
                        },
                        hullWhite, new Vector3(0, y, 0));
                    AddWeldRings("NGUpperPanelLine", radius + 0.012f,
                        y - halfH + 0.8f, y + halfH - 0.8f, 3);
                    break;
                }
                case "be4_cluster7_newglenn_2026":
                {
                    // Flared boattail around the 7-engine BE-4 cluster — New
                    // Glenn's aft section widens slightly toward the engines,
                    // unlike Falcon 9's flat octaweb disc.
                    AddMesh("NGBoattail",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius * 1.08f,
                            Height = halfH * 2f, RadialSegments = 48,
                            CapTop = false,
                        },
                        boattailMat, new Vector3(0, y, 0));
                    AddHullRing("NGBoattailSoot", radius * 1.085f,
                        y - halfH * 0.5f, halfH * 0.85f, sootMat);
                    break;
                }
                case "newglenn_fairing_7m":
                {
                    BuildNewGlennFairing(y, halfH * 2f, radius, fairingWhite, darkTrim);
                    break;
                }
                case "be3u_cluster2_newglenn_2026":
                {
                    AddMesh("NGSecondStageMount",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius * 0.92f,
                            Height = halfH * 2f, RadialSegments = 40,
                            CapTop = false,
                        },
                        boattailMat, new Vector3(0, y, 0));
                    break;
                }
                default:
                {
                    // Payload simulator and anything unanticipated.
                    AddMesh($"NG_{definition.Id}",
                        new CylinderMesh
                        {
                            TopRadius = radius, BottomRadius = radius,
                            Height = halfH * 2f, RadialSegments = 32,
                        },
                        darkTrim, new Vector3(0, y, 0));
                    break;
                }
            }
        }

        _hullMesh = firstStageMesh;
        Node3D fallbackNode = (Node3D?)_hullMesh ?? this;
        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = fallbackNode;

        // BE-4/BE-3U bells, positioned from the real engine-cluster JSON
        // mount data (1 centre + 6 ring BE-4s, 2 BE-3Us) — not an invented
        // layout.
        var catalog = GetEngineCatalog();
        if (catalog == null) return;
        // BE-4 (methalox, sea level) regen-cooled bells read as coppery
        // bronze in flight footage; BE-3U's much larger hydrolox vacuum
        // extension is a dull, unpolished silver-grey.
        var be4Mat  = Mat(new Color(0.52f, 0.34f, 0.22f), 0.60f, 0.42f);
        var be3uMat = Mat(new Color(0.72f, 0.73f, 0.75f), 0.50f, 0.55f);
        var beNubMat = Mat(new Color(0.28f, 0.29f, 0.30f), 0.80f, 0.44f);
        var plumeMounts = new List<PlumeSystem.EnginePlumeMount>();
        foreach (var enginePart in vessel.Parts.Parts.Where(p =>
                     p.HasEngineRuntime
                     && !string.IsNullOrWhiteSpace(p.Definition.EngineClusterId)))
        {
            if (!positions.TryGetValue(enginePart, out var partPosition)
                || !catalog.Clusters.TryGetValue(
                    enginePart.Definition.EngineClusterId, out var cluster))
                continue;

            bool vacuum = enginePart.Definition.Id == "be3u_cluster2_newglenn_2026";
            float exitRadius = enginePart.Definition.EngineCount > 1
                ? (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM
                    / (2.0 * System.Math.Sqrt(enginePart.Definition.EngineCount)),
                    0.28, 0.85)
                : (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM * 0.32, 0.35, 0.95);
            // BE-3U is hydrolox with a large expansion-ratio vacuum bell —
            // proportionally longer/wider than the BE-4's methalox sea-level bell.
            float throatRadius = vacuum ? exitRadius * 0.14f : exitRadius * 0.32f;
            float bellLen = vacuum ? exitRadius * 3.1f : exitRadius * 1.30f;
            float exitY = YOf(partPosition) - ToU(enginePart.Definition.LengthM * 0.5);

            int count = System.Math.Min(
                enginePart.EngineStates.Count, cluster.Engines.Count);
            for (int i = 0; i < count; i++)
            {
                var local = cluster.Engines[i].Position;
                var position = new Vector3(
                    ToU(partPosition.X + local.X),
                    exitY + ToU(local.Y),
                    ToU(partPosition.Z + local.Z));
                string id = enginePart.EngineStates[i].InstanceId;
                plumeMounts.Add(new PlumeSystem.EnginePlumeMount(
                    id, position, exitRadius / MetresPerUnit, vacuum));
                AddSimpleEngineBell(
                    $"BE_{id.Replace(':', '_')}", position,
                    exitRadius, throatRadius, bellLen,
                    vacuum ? be3uMat : be4Mat, beNubMat);
            }
        }

        if (plumeMounts.Count > 0)
        {
            _plumes = new PlumeSystem { Name = "EnginePlumes" };
            AddChild(_plumes);
            _plumes.SetupGenericCluster(plumeMounts);
        }
    }

    // Same tangent-ogive nose construction as BuildFalcon9Fairing, but no
    // barrel-diameter step from the booster (New Glenn is 7 m throughout),
    // so the ogive rises directly off the same radius as the second stage.
    private void BuildNewGlennFairing(
        float centerY, float totalHeight, float radius,
        Material hullMat, Material seamMat)
    {
        float bottom = centerY - totalHeight * 0.5f;
        float barrelH = totalHeight * 0.38f;
        float noseH   = totalHeight - barrelH;
        float barrelTop = bottom + barrelH;

        AddMesh("NGFairingBarrel",
            new CylinderMesh
            {
                TopRadius = radius, BottomRadius = radius,
                Height = barrelH, RadialSegments = 48,
                CapBottom = false,
            },
            hullMat, new Vector3(0, bottom + barrelH * 0.5f, 0));

        const int noseSeg = 14;
        float NoseR(float u) =>
            (float)VehicleVisualPhysics.TangentOgiveRadius(u, radius, noseH);
        for (int i = 0; i < noseSeg; i++)
        {
            float u0 = (float)i / noseSeg;
            float u1 = (float)(i + 1) / noseSeg;
            float rBot = NoseR(u0);
            float rTop = NoseR(u1);
            float segH = noseH / noseSeg;
            float yMid = barrelTop + (u0 + u1) * 0.5f * noseH;
            AddMesh($"NGFairingNose{i}",
                new CylinderMesh
                {
                    TopRadius = rTop, BottomRadius = rBot,
                    Height = segH * 1.05f, RadialSegments = 48,
                    CapTop = i == noseSeg - 1, CapBottom = false,
                },
                hullMat, new Vector3(0, yMid, 0));
        }

        AddHullRing("NGFairingRing", radius + 0.01f, barrelTop, 0.05f, seamMat);
        foreach (float angle in new[] { 0f, Mathf.Pi })
            AddSurfaceBox($"NGFairingSeam{(int)Mathf.RadToDeg(angle)}", angle, centerY,
                totalHeight * 0.98f, 0.02f, 0.08f, seamMat, radius + 0.012f);
    }

    // Fixed reentry strakes near the top of the first stage — flat vertical
    // blades, NOT a grid-fin lattice like Falcon 9/Starship (New Glenn's
    // published descent-control system is strake-based, not grid fins).
    private void AddNGStrakes(float y, float bodyRadius)
    {
        var strakeMat = Mat(new Color(0.30f, 0.30f, 0.33f), 0.60f, 0.50f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi * 0.5f;
            float cos = Mathf.Cos(a);
            float sin = Mathf.Sin(a);
            var strake = new MeshInstance3D
            {
                Name = $"NGStrake{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.05f, 1.6f, 0.55f) },
                Position = new Vector3((bodyRadius + 0.03f) * cos, y, (bodyRadius + 0.03f) * sin),
                RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a), 0f),
            };
            strake.SetSurfaceOverrideMaterial(0, strakeMat);
            AddChild(strake);
        }
    }

    // ── Per-frame visual updates ──────────────────────────────────────────

    public override void _Process(double delta)
    {
        // CameraController hides the exterior renderer during IVA. Visibility does
        // not pause Node3D processing, so avoid physics queries and material/particle
        // setters while the entire exterior is outside the active camera. The next
        // visible frame runs the normal cadence immediately (timers are not advanced
        // while hidden), keeping re-entry and cockpit exit visually synchronized.
        if (!Visible || TargetVessel == null) return;

        _presentationSampleTimer -= System.Math.Max(0.0, delta);
        if (_presentationSampleTimer <= 0.0)
        {
            _presentationSampleTimer = PresentationSamplePeriodSeconds;
            RefreshPresentationSample();
        }

        var body = _cachedPresentationBody;
        double alt = _cachedPresentationAltitude;

        _engineVisualTimer -= System.Math.Max(0.0, delta);
        if (_engineVisualTimer <= 0.0)
        {
            _engineVisualTimer = EngineVisualPeriodSeconds;
            // Commanded throttle is not the same as delivered thrust during chill/spin/ignition
            // or after an engine-out. Keep the plume consistent with the physical engine
            // telemetry; the pad startup controller supplies the separate pre-chamber glow.
            TargetVessel.FillEngineReadoutsAtPressure(
                _engineReadoutScratch,
                _cachedPresentationPressureRatio * 101_325.0);
            ComputeDeliveredPlumeThrottles(
                _engineReadoutScratch,
                out float superHeavyThrottle,
                out float shipThrottle);
            float throttle = Mathf.Max(superHeavyThrottle, shipThrottle);
            _plumes?.Update(superHeavyThrottle, shipThrottle, alt,
                _cachedPresentationPressureRatio, _selectedShipEngines);
            ReportVisualPlumeTelemetry(superHeavyThrottle, shipThrottle);
            if (_usesGenericPlumes && _plumes != null)
            {
                _perEngineThrottle.Clear();
                foreach (var row in _engineReadoutScratch)
                    _perEngineThrottle[row.InstanceId] = row.Throttle;
                _plumes.UpdateGeneric(_perEngineThrottle, alt,
                    _cachedPresentationPressureRatio);
            }
            UpdateVehicleLighting(throttle, alt);
        }

        _flapInputTimer -= System.Math.Max(0.0, delta);
        if (_flapInputTimer <= 0.0)
        {
            _flapInputTimer = SecondaryVisualPeriodSeconds;
            _cachedFlapDeployment = ComputeFlapDeployment(body);
            _cachedFlapPitch = (float)TargetVessel.PitchYawRoll.X;
            _cachedFlapRoll = (float)TargetVessel.PitchYawRoll.Z;
        }
        UpdateFlaps(delta, _cachedFlapDeployment, _cachedFlapPitch, _cachedFlapRoll);

        _landingGearMotionDelta += System.Math.Max(0.0, delta);
        _landingGearStateTimer -= System.Math.Max(0.0, delta);
        if (_landingGearStateTimer <= 0.0)
        {
            _landingGearStateTimer = SecondaryVisualPeriodSeconds;
            _landingGearDeployed = TargetVessel.Parts.Parts.Any(p =>
                p.Definition.Category == PartCategory.Landing && p.IsDeployed);
            foreach (var rig in _landingLegRigs)
            {
                rig.CompressionM = 0.0;
                var points = TargetVessel.LastSurfaceContact?.Points;
                if (points == null) continue;
                foreach (var point in points)
                {
                    if (point.Name.EndsWith($"-{rig.Index}", StringComparison.Ordinal))
                    {
                        rig.CompressionM = point.PenetrationM;
                        break;
                    }
                }
            }
        }
        AdvanceLandingGear(_landingGearMotionDelta);
        _landingGearMotionDelta = 0.0;
        _parachuteStateTimer -= System.Math.Max(0.0, delta);
        if (_parachuteStateTimer <= 0.0)
        {
            _parachuteStateTimer = SecondaryVisualPeriodSeconds;
            UpdateParachutes();
        }

        // Thermal presentation exists only on Starship heat-shield/steel materials.
        // Falcon 9, New Glenn and generic renderers have no thermal material targets;
        // avoid querying atmosphere, surface velocity and every part for a result that
        // cannot affect their visuals. This is presentation-only and leaves Vessel
        // thermal simulation untouched.
        if (_shipSteelMats.Count == 0 && _tileZoneMats.Count == 0) return;

        _thermalVisualTimer -= System.Math.Max(0.0, delta);
        if (_thermalVisualTimer > 0.0) return;
        _thermalVisualTimer = ThermalVisualPeriodSeconds;

        // Visible incandescence is a reentry phenomenon, not a generic "warm
        // skin" indicator. Gate it with the same physical observables as the
        // plasma system: descending radial velocity and meaningful convective flux.
        double heatFlux = 0.0;
        double radialSpeed = 0.0;
        if (body != null)
        {
            Vector3d up = (TargetVessel.Position - body.Position).Normalized;
            Vector3d surfaceVelocity = TargetVessel.GetSurfaceVelocity(body);
            radialSpeed = surfaceVelocity.Dot(up);
            heatFlux = TargetVessel.ComputeStagnationHeatFlux(
                body.GetAtmosphericDensity(TargetVessel.Position), surfaceVelocity);
        }
        double hottestSkin = 290.0;
        double hottestThermalRatio = 0.0;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (part.SkinTemperature > hottestSkin) hottestSkin = part.SkinTemperature;
            if (double.IsFinite(part.ThermalRatio))
                hottestThermalRatio = System.Math.Max(hottestThermalRatio, part.ThermalRatio);
        }
        float glow = (float)VehicleVisualPhysics.ReentryGlow(radialSpeed, heatFlux, hottestSkin);

        // Some deterministic EDL profiles expose a high structural heat ratio before the
        // simplified skin-temperature integrator has risen to its photographic value. Keep
        // the physically-gated reentry read visible in that interval: this is an emissive
        // presentation floor, not a change to thermal damage or survival physics.
        if (VehicleVisualPhysics.IsVisibleReentryHeating(radialSpeed, heatFlux))
        {
            float ratioGlow = Mathf.Clamp(
                (float)((hottestThermalRatio - 0.15) / 0.85), 0f, 1f);
            glow = Mathf.Max(glow, ratioGlow * 0.72f);
        }

        if (!float.IsNaN(_lastGlow) && Mathf.Abs(glow - _lastGlow) < 0.001f)
        {
            UpdateTileCharring(body);
            return;
        }
        _lastGlow = glow;

        // Bare leeward steel only catches a faint reflected plasma glow. Keep it
        // subordinate to the TPS/plasma effects, but make the flux-driven cue
        // readable at peak heating when the hull is silhouetted against space.
        // The scale is intentionally bounded: it is a presentation cue, not a
        // second thermal solver or a substitute for the plasma controller.
        foreach (var steelMat in _shipSteelMats)
            steelMat.SetShaderParameter("emit_strength", glow * glow * 0.28f);
        foreach (var mats in _tileZoneMats.Values)
        foreach (var tileMat in mats)
        {
            tileMat.SetShaderParameter("emit_strength", glow * 1.6f);
            tileMat.SetShaderParameter("rim_strength", 0.035f + glow * 0.04f);
        }

        UpdateTileCharring(body);
    }

    // VesselRenderer is presentation-only. Body selection, altitude and ambient pressure
    // are sampled once at the secondary-visual cadence and reused by plumes, flaps and
    // thermal materials. Universe.Tick and all gameplay consumers retain their own live
    // physics queries; this cache only bounds redundant renderer-side reads.
    private void BuildEngineVisualGroups(Vessel vessel)
    {
        _superHeavyEngineIds.Clear();
        _shipEngineIds.Clear();
        foreach (var part in vessel.Parts.Parts)
        {
            if (part.Definition.Category != PartCategory.Engine)
                continue;

            bool isSuperHeavy = part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("booster");
            bool isShip = part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("ship_engines");
            if (!isSuperHeavy && !isShip)
                continue;

            var destination = isSuperHeavy ? _superHeavyEngineIds : _shipEngineIds;
            if (!part.HasEngineRuntime)
            {
                destination.Add(part.InstanceId);
                continue;
            }

            foreach (var state in part.EngineStates)
                destination.Add(state.InstanceId);
        }
    }

    private void ComputeDeliveredPlumeThrottles(
        IReadOnlyList<EngineReadout> readouts,
        out float superHeavyThrottle,
        out float shipThrottle)
    {
        double superHeavySum = 0.0;
        double shipSum = 0.0;
        double allSum = 0.0;
        int superHeavyCount = 0;
        int shipCount = 0;
        int knownCount = 0;
        for (int i = 0; i < readouts.Count; i++)
        {
            var readout = readouts[i];
            bool delivered = EngineHudPresentation.IsDelivered(readout);
            double value = delivered
                ? System.Math.Clamp(readout.Throttle, 0.0, 1.0)
                : 0.0;
            allSum += value;

            if (_superHeavyEngineIds.Contains(readout.InstanceId))
            {
                superHeavyCount++;
                superHeavySum += value;
                knownCount++;
            }
            else if (_shipEngineIds.Contains(readout.InstanceId))
            {
                shipCount++;
                shipSum += value;
                knownCount++;
            }
        }

        // A custom/static engine without a runtime instance can still arrive through the
        // compatibility readout API. Preserve the old single-cluster behavior as a safe
        // fallback, but never use it when a mixed Starship stack has role-tagged rows.
        if (knownCount == 0)
        {
            float aggregate = readouts.Count > 0
                ? (float)System.Math.Clamp(allSum / readouts.Count, 0.0, 1.0)
                : 0f;
            superHeavyThrottle = _hasSuperHeavy ? aggregate : 0f;
            shipThrottle = _hasSuperHeavy ? 0f : aggregate;
            return;
        }

        superHeavyThrottle = superHeavyCount > 0
            ? (float)System.Math.Clamp(superHeavySum / superHeavyCount, 0.0, 1.0)
            : 0f;
        shipThrottle = shipCount > 0
            ? (float)System.Math.Clamp(shipSum / shipCount, 0.0, 1.0)
            : 0f;
    }

    private void ReportVisualPlumeTelemetry(float superHeavyThrottle, float shipThrottle)
    {
        if (TargetVessel == null)
            return;

        bool overlap = TargetVessel.IsHotStageOverlapping;
        if (_lastVisualHotStage == overlap
            && !float.IsNaN(_lastVisualSuperHeavyThrottle)
            && Mathf.Abs(superHeavyThrottle - _lastVisualSuperHeavyThrottle) < 0.03f
            && Mathf.Abs(shipThrottle - _lastVisualShipThrottle) < 0.03f)
            return;

        _lastVisualHotStage = overlap;
        _lastVisualSuperHeavyThrottle = superHeavyThrottle;
        _lastVisualShipThrottle = shipThrottle;
        GD.Print($"VISUAL_PLUME overlap={overlap} shDelivered={superHeavyThrottle:F3} "
            + $"shipDelivered={shipThrottle:F3} shUnits={_hasSuperHeavy}");
    }

    private void RefreshPresentationSample()
    {
        if (TargetVessel == null)
        {
            _cachedPresentationBody = null;
            _cachedPresentationAltitude = 0.0;
            _cachedPresentationPressureRatio = 0.0;
            return;
        }

        var universe = SimulationBridge.Instance?.Universe;
        var body = universe?.GetDominantBody(TargetVessel.Position);
        _cachedPresentationBody = body;
        _cachedPresentationAltitude = body != null
            ? TargetVessel.GetAltitude(body)
            : 0.0;
        if (body?.Atmosphere == null)
        {
            _cachedPresentationPressureRatio = 0.0;
            return;
        }

        // Plume expansion is dimensionless p/p0. Earth happened to work with the old
        // 101325 Pa constant, but it made Mars look vacuum-like and Venus look like a
        // lightly pressurized Earth. Resolve p0 from the same atmosphere model that owns
        // the physical pressure query; this is presentation-only and does not alter thrust.
        double seaLevelPressure = body.Atmosphere.GetPressure(0.0);
        double ambientPressure = TargetVessel.GetAmbientPressure(body);
        _cachedPresentationPressureRatio = seaLevelPressure > 1e-9
            ? System.Math.Clamp(ambientPressure / seaLevelPressure, 0.0, 1.0)
            : 0.0;
    }

    private float ComputeFlapDeployment(CelestialBody? body)
    {
        if (_flapRigs.Count == 0 || TargetVessel == null) return 0f;

        float aeroDeployment = 0f;
        if (body?.Atmosphere != null)
        {
            double q = TargetVessel.GetDynamicPressure(body);
            var surfVel = TargetVessel.GetSurfaceVelocity(body);
            if (surfVel.Magnitude > 1.0)
            {
                var localFlow = TargetVessel.Orientation.Inverse().Rotate(surfVel.Normalized);
                float belly = (float)System.Math.Clamp(localFlow.Dot(-Vector3d.Right), 0.0, 1.0);
                float qAuthority = Mathf.SmoothStep(0f, 1f,
                    (float)System.Math.Clamp((q - 250.0) / 4_000.0, 0.0, 1.0));
                aeroDeployment = belly * qAuthority;
            }
        }

        return Mathf.Max(aeroDeployment, 0.28f);
    }

    private void UpdateFlaps(double delta, float aeroDeployment, float pitch, float roll)
    {
        if (_flapRigs.Count == 0 || TargetVessel == null) return;

        float response = 1f - Mathf.Exp(-(float)delta * 3.8f);

        foreach (var flap in _flapRigs)
        {
            float baseDeg = (flap.Forward ? 28f : 42f) * aeroDeployment;
            float pitchMix = pitch * (flap.Forward ? 8f : -10f) * aeroDeployment;
            float rollMix  = roll * flap.Side * 11f * aeroDeployment;
            float signedDeg = flap.Side * Mathf.Clamp(baseDeg + pitchMix + rollMix, 0f, 52f);
            var target = flap.Neutral * new Quaternion(Vector3.Up, Mathf.DegToRad(signedDeg));
            flap.Blade.Quaternion = flap.Blade.Quaternion.Slerp(target, response);
        }
    }

    // ── Heat-shield tile charring ─────────────────────────────────────────
    // Each tile zone chars from its owning part (command → nose/fwd flaps, tank →
    // belly/aft flaps) and scales with attitude misalignment via WindwardFactor.
    private void UpdateTileCharring(CelestialBody? body)
    {
        if (_tileZoneMats.Count == 0 || TargetVessel == null) return;

        double cmdDamage = 0.0, cmdTemp = 0.0;
        double tankDamage = 0.0, tankTemp = 0.0;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            switch (part.Definition.VehicleRole)
            {
                case "command" when part.Definition.IsStarshipFamily:
                    if (part.ThermalDamage   > cmdDamage) cmdDamage = part.ThermalDamage;
                    if (part.SkinTemperature > cmdTemp)   cmdTemp   = part.SkinTemperature;
                    break;
                case "tank" when part.Definition.IsStarshipFamily:
                    if (part.ThermalDamage   > tankDamage) tankDamage = part.ThermalDamage;
                    if (part.SkinTemperature > tankTemp)   tankTemp   = part.SkinTemperature;
                    break;
            }
        }

        float align = 1f;
        if (body != null)
        {
            var surfVel = TargetVessel.GetSurfaceVelocity(body);
            if (surfVel.Magnitude > 1e-6)
            {
                Vector3d flowLocal = TargetVessel.Orientation.Inverse().Rotate(surfVel);
                align = (float)ThermalModel.WindwardFactor(flowLocal);
            }
        }
        float misalign = 1f - align;

        CharZone(TileCharZone.Belly,   tankDamage, tankTemp, Mathf.Lerp(1.35f, 0.80f, align));
        CharZone(TileCharZone.Nose,    cmdDamage,  cmdTemp,  0.30f + 0.85f * misalign);
        CharZone(TileCharZone.FwdFlap, cmdDamage,  cmdTemp,  0.40f + 0.90f * misalign);
        CharZone(TileCharZone.AftFlap, tankDamage, tankTemp, 0.35f + 0.80f * misalign);
    }

    private void CharZone(TileCharZone zone, double rawDamage, double peakTemp, float dmgScale)
    {
        if (!_tileZoneMats.TryGetValue(zone, out var mats) || mats.Count == 0) return;

        float dmg = (float)System.Math.Clamp(rawDamage * dmgScale, 0.0, 1.0);
        float ember = (float)System.Math.Clamp((peakTemp - 1100.0) / 900.0, 0.0, 1.0);
        bool glow = ember > 0.02f;

        var scorch = new Color(0.085f, 0.050f, 0.038f);
        Color albedo = TileBaseColor.Lerp(scorch, dmg);

        foreach (var mat in mats)
        {
            mat.SetShaderParameter("albedo_color", albedo);
            mat.SetShaderParameter("roughness_val", Mathf.Lerp(0.92f, 0.99f, dmg));
            mat.SetShaderParameter("char_amt", dmg);
            if (glow)
            {
                float e = ember * (0.35f + 0.65f * dmg);
                mat.SetShaderParameter("emit_strength", e * 1.6f);
            }
            else
            {
                mat.SetShaderParameter("emit_strength", 0.0f);
            }
        }
    }

    private void AddStarshipLandingGear(float skirtDatumY)
    {
        var strutMat = Mat(new Color(0.32f, 0.34f, 0.36f), 0.90f, 0.30f);
        var footMat = Mat(new Color(0.18f, 0.19f, 0.20f), 0.82f, 0.48f);
        const int count = 6;
        const float physicalRingM = 4.20f;
        const float physicalLengthM = 7.50f;
        float ring = physicalRingM / MetresPerUnit;
        float length = physicalLengthM / MetresPerUnit;

        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.Tau / count;
            var root = new Node3D
            {
                Name = $"LandingLeg{i}",
                Position = new Vector3(ring * Mathf.Cos(angle), skirtDatumY, ring * Mathf.Sin(angle)),
            };
            AddChild(root);

            var strut = new MeshInstance3D
            {
                Name = "Strut",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.075f,
                    BottomRadius = 0.105f,
                    Height = length,
                    RadialSegments = 18,
                },
                Position = new Vector3(0f, -length * 0.5f, 0f),
            };
            strut.SetSurfaceOverrideMaterial(0, strutMat);
            root.AddChild(strut);

            var foot = new MeshInstance3D
            {
                Name = "Footpad",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.35f / MetresPerUnit,
                    BottomRadius = 0.42f / MetresPerUnit,
                    Height = 0.10f,
                    RadialSegments = 24,
                },
                Position = new Vector3(0f, -length, 0f),
            };
            foot.SetSurfaceOverrideMaterial(0, footMat);
            root.AddChild(foot);
            _landingLegRigs.Add(new LandingLegRig
            {
                Root = root,
                Strut = strut,
                Foot = foot,
                Index = i,
                FullLength = length,
            });
        }
    }

    private void AdvanceLandingGear(double delta)
    {
        if (TargetVessel == null || _landingLegRigs.Count == 0) return;
        _landingGearDeployment = Mathf.MoveToward(
            _landingGearDeployment, _landingGearDeployed ? 1f : 0f, (float)delta * 1.5f);

        foreach (var rig in _landingLegRigs)
        {
            float compression = (float)(rig.CompressionM / MetresPerUnit);
            float visibleLength = Mathf.Max(0.08f,
                rig.FullLength * _landingGearDeployment - compression);
            bool visible = _landingGearDeployment > 0.01f;
            if (Mathf.Abs(visibleLength - rig.LastVisibleLength) < 0.0005f
                && rig.Root.Visible == visible)
                continue;
            rig.LastVisibleLength = visibleLength;
            rig.Root.Visible = visible;
            rig.Strut.Scale = new Vector3(1f, visibleLength / rig.FullLength, 1f);
            rig.Strut.Position = new Vector3(0f, -visibleLength * 0.5f, 0f);
            rig.Foot.Position = new Vector3(0f, -visibleLength, 0f);
        }
    }

    // Everyday Astronaut IFT-3: four elongated triangular chines near the aft
    // hold COPVs. Visual fairings only — not a mass/aero model.
    private void AddSHChines(Material mat)
    {
        float y = ShBodyBot + 3.4f;
        float h = 6.2f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f;
            AddSurfaceBox($"SHChine{i}", a, y, h, 0.55f, 0.22f, mat, BodyR + 0.09f);
        }
    }

    private void AttachVehicleLights(float engineY, bool superHeavy)
    {
        _engineBayLight?.QueueFree();
        _engineRimLight?.QueueFree();
        _skyFillLight?.QueueFree();

        float skirtY = engineY + (superHeavy ? 1.8f : 1.15f);
        float midY = superHeavy ? ShBodyBot + ShBodyH * 0.45f : engineY + 8.0f;

        _engineBayLight = new OmniLight3D
        {
            Name = "EngineBayBounce",
            Position = new Vector3(0f, skirtY, 0f),
            LightColor = new Color(1.0f, 0.62f, 0.32f),
            LightEnergy = 0f,
            OmniRange = superHeavy ? 24f : 12f,
            OmniAttenuation = 0.85f,
            LightSpecular = 0.55f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(_engineBayLight);

        _engineRimLight = new OmniLight3D
        {
            Name = "EngineClusterRim",
            Position = new Vector3(0f, engineY - 0.85f, 0f),
            LightColor = new Color(0.72f, 0.88f, 1.0f),
            LightEnergy = 0f,
            OmniRange = superHeavy ? 16f : 8f,
            OmniAttenuation = 1.05f,
            LightSpecular = 0.70f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(_engineRimLight);

        _skyFillLight = new OmniLight3D
        {
            Name = "VehicleSkyFill",
            Position = new Vector3(0f, midY, 0f),
            LightColor = new Color(0.55f, 0.72f, 1.0f),
            LightEnergy = 4.8f,
            OmniRange = superHeavy ? 56f : 32f,
            OmniAttenuation = 1.15f,
            LightSpecular = 0.15f,
            ShadowEnabled = false,
        };
        AddChild(_skyFillLight);
    }

    private void UpdateVehicleLighting(float throttle, double altitudeM)
    {
        float t = Mathf.Clamp(throttle, 0f, 1f);
        float space = Mathf.Clamp(((float)altitudeM - 70_000f) / 50_000f, 0f, 1f);
        float fill = Mathf.Lerp(0.028f, 0.042f, space);
        float rim = Mathf.Lerp(0.28f, 0.10f, space);
        float bounce = Mathf.Lerp(0.92f, 0.55f, space);
        Vector3 sunDir = new Vector3(0.35f, 0.78f, 0.25f);
        var key = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (key != null)
            sunDir = -key.GlobalTransform.Basis.Z.Normalized();
        foreach (var mat in _steelMats)
        {
            mat.SetShaderParameter("fill_strength", fill);
            mat.SetShaderParameter("rim_strength", rim);
            mat.SetShaderParameter("sky_bounce", bounce);
            mat.SetShaderParameter("sun_dir", sunDir);
            mat.SetShaderParameter("engine_light", t * (1.4f - space * 0.5f));
        }
        foreach (var list in _tileZoneMats.Values)
        {
            foreach (var tile in list)
            {
                tile.SetShaderParameter("sky_bounce", Mathf.Lerp(1.15f, 0.45f, space));
                tile.SetShaderParameter("sun_dir", sunDir);
            }
        }

        bool firing = t > 0.02f;
        if (_engineBayLight != null)
        {
            _engineBayLight.Visible = firing;
            _engineBayLight.LightEnergy = firing
                ? (18f + 42f * t) * (1f - space * 0.35f)
                : 0f;
        }
        if (_engineRimLight != null)
        {
            _engineRimLight.Visible = firing;
            _engineRimLight.LightEnergy = firing
                ? (10f + 22f * t) * (1f - space * 0.25f)
                : 0f;
        }
        if (_skyFillLight != null)
            _skyFillLight.LightEnergy = Mathf.Lerp(4.8f, 1.1f, space);

        if (_bellMat != null)
        {
            _bellMat.EmissionEnabled = firing;
            _bellMat.Emission = new Color(1.0f, 0.42f, 0.12f);
            _bellMat.EmissionEnergyMultiplier = t * 3.8f;
        }
        if (_throatMat != null)
        {
            _throatMat.EmissionEnabled = firing;
            _throatMat.Emission = new Color(1.0f, 0.72f, 0.35f);
            _throatMat.EmissionEnergyMultiplier = t * 6.5f;
        }
        if (_bellLipMat != null)
        {
            _bellLipMat.EmissionEnabled = firing;
            _bellLipMat.Emission = new Color(1.0f, 0.85f, 0.55f);
            _bellLipMat.EmissionEnergyMultiplier = t * 2.2f;
        }
    }

    private void RegisterTileMat(TileCharZone zone, ShaderMaterial mat)
    {
        if (!_tileZoneMats.TryGetValue(zone, out var list))
        {
            list = new List<ShaderMaterial>();
            _tileZoneMats[zone] = list;
        }
        list.Add(mat);
    }

    // ── Generic vessel fallback ───────────────────────────────────────────

    private void BuildGenericVessel(Vessel vessel)
    {
        _usesGenericPlumes = true;
        var positions = vessel.Parts.ComputePartLocalPositions();
        double minimumY = positions.Count == 0
            ? 0.0
            : positions.Min(pair =>
                pair.Value.Y - pair.Key.Definition.LengthM * 0.5);
        double datumShiftY = -minimumY;
        foreach (var (part, localPos) in positions)
        {
            var (hasAbove, hasBelow) = GetStackNeighborCaps(vessel, part, positions);
            var node = CreateGenericPartNode(part, hasAbove, hasBelow);
            node.Position = new Vector3(
                (float)(localPos.X / MetresPerUnit),
                (float)((localPos.Y + datumShiftY) / MetresPerUnit),
                (float)(localPos.Z / MetresPerUnit));
            AddChild(node);
            _partNodes[part.InstanceId] = node;
        }

        var catalog = GetEngineCatalog();
        if (catalog == null) return;
        var plumeMounts = new List<PlumeSystem.EnginePlumeMount>();
        foreach (var enginePart in vessel.Parts.Parts.Where(p =>
                     p.HasEngineRuntime
                     && !string.IsNullOrWhiteSpace(p.Definition.EngineClusterId)))
        {
            if (!positions.TryGetValue(enginePart, out var partPosition)
                || !catalog.Clusters.TryGetValue(
                    enginePart.Definition.EngineClusterId, out var cluster))
                continue;

            int count = System.Math.Min(
                enginePart.EngineStates.Count, cluster.Engines.Count);
            float exitRadius = enginePart.Definition.EngineCount > 1
                ? (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM
                    / (2.0 * System.Math.Sqrt(enginePart.Definition.EngineCount)),
                    0.22,
                    0.62)
                : (float)System.Math.Clamp(
                    enginePart.Definition.DiameterM * 0.32, 0.35, 0.95);
            bool vacuum = enginePart.Definition.IspVac
                - enginePart.Definition.IspSL > 60.0;
            double exitY = partPosition.Y - enginePart.Definition.LengthM * 0.5;
            for (int i = 0; i < count; i++)
            {
                var local = cluster.Engines[i].Position;
                var position = new Vector3(
                    (float)((partPosition.X + local.X) / MetresPerUnit),
                    (float)((exitY + local.Y + datumShiftY) / MetresPerUnit),
                    (float)((partPosition.Z + local.Z) / MetresPerUnit));
                string id = enginePart.EngineStates[i].InstanceId;
                plumeMounts.Add(new PlumeSystem.EnginePlumeMount(
                    id, position, exitRadius / MetresPerUnit, vacuum));
                AddGenericEngineBell(
                    id, position, exitRadius / MetresPerUnit, vacuum);
            }
        }

        if (plumeMounts.Count > 0)
        {
            _plumes = new PlumeSystem { Name = "EnginePlumes" };
            AddChild(_plumes);
            _plumes.SetupGenericCluster(plumeMounts);
        }
    }

    // Coincident-cap seam fix: a part butting against a same-radius neighbour
    // (parent or child, connected through the same stack-node joint system used
    // to position every node) must not draw a flat end-cap disc on that face —
    // two independent opposite-facing caps sharing the exact same world-space
    // plane z-fight and read as a bright angle-dependent seam line. Only a face
    // with no neighbour (a genuinely exposed end: the stack's very top or the
    // engine end at the very bottom) keeps its cap.
    // Fix del seam de tapas coincidentes: si una pieza empalma con un vecino del
    // mismo radio no dibujamos la tapa de esa cara (si no, dos discos opuestos
    // en el mismo plano generan z-fighting = línea blanca según el ángulo).
    private static (bool hasAbove, bool hasBelow) GetStackNeighborCaps(
        Vessel vessel, Part part, Dictionary<Part, Vector3d> positions)
    {
        bool hasAbove = false, hasBelow = false;
        if (!positions.TryGetValue(part, out var partPos)) return (false, false);

        var parentJoint = vessel.Parts.Joints.FirstOrDefault(j => j.Child == part);
        if (parentJoint != null && positions.TryGetValue(parentJoint.Parent, out var parentPos))
        {
            if (parentPos.Y > partPos.Y) hasAbove = true;
            else if (parentPos.Y < partPos.Y) hasBelow = true;
        }
        foreach (var child in vessel.Parts.GetChildren(part))
        {
            if (!positions.TryGetValue(child, out var childPos)) continue;
            if (childPos.Y > partPos.Y) hasAbove = true;
            else if (childPos.Y < partPos.Y) hasBelow = true;
        }
        return (hasAbove, hasBelow);
    }

    private void AddGenericEngineBell(
        string instanceId,
        Vector3 exitPosition,
        float exitRadius,
        bool vacuum)
    {
        var material = Mat(new Color(0.20f, 0.19f, 0.18f), 0.91f, 0.32f);
        float length = vacuum ? exitRadius * 2.4f : exitRadius * 1.65f;
        AddMesh(
            $"Bell_{instanceId.Replace(':', '_')}",
            new CylinderMesh
            {
                TopRadius = exitRadius * 0.42f,
                BottomRadius = exitRadius,
                Height = length,
                RadialSegments = 28,
                CapTop = false,
                CapBottom = false,
            },
            material,
            exitPosition + Vector3.Up * (length * 0.5f));
    }

    private static EngineDefinitionCatalog? GetEngineCatalog()
    {
        if (_engineCatalogLoadAttempted) return _engineCatalog;
        _engineCatalogLoadAttempted = true;
        try
        {
            string data = ProjectSettings.GlobalizePath("res://data");
            var provenance = DataProvenanceRegistry.LoadFromDirectory(
                System.IO.Path.Combine(data, "provenance"));
            _engineCatalog = EngineDefinitionCatalog.Load(
                System.IO.Path.Combine(data, "engines"),
                System.IO.Path.Combine(data, "engine_clusters"),
                provenance);
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Engine visual catalog unavailable: {error.Message}");
        }
        return _engineCatalog;
    }

    private Node3D CreateGenericPartNode(
        Part part, bool hasNeighborAbove = false, bool hasNeighborBelow = false)
    {
        var definition = part.Definition;
        if (definition.VehicleFamily.Equals(
                "mercury", StringComparison.OrdinalIgnoreCase))
        {
            if (definition.HasVehicleRole("capsule"))
                return CreateMercuryCapsuleNode(part);
            if (definition.HasVehicleRole("escape_tower"))
                return CreateMercuryEscapeTowerNode(definition);
            if (definition.HasVehicleRole("adapter"))
                return CreateMercuryAdapterNode(definition);
            if (definition.HasVehicleRole("atlas_tank"))
                return CreateAtlasTankNode(definition);
            if (definition.HasVehicleRole("sustainer_engine")
                || definition.HasVehicleRole("booster_engine_package"))
                return CreateAtlasEngineSectionNode(definition);
            if (definition.HasVehicleRole("retro_pack"))
                return CreateMercuryRetroPackNode(definition);
            if (definition.HasVehicleRole("spacecraft_separation"))
                return CreateMercurySeparationRingNode(definition);
        }
        if (definition.VehicleFamily.Equals(
                "gemini", StringComparison.OrdinalIgnoreCase))
        {
            if (definition.HasVehicleRole("gemini_reentry_capsule"))
                return CreateGeminiCapsuleNode(part);
            if (definition.HasVehicleRole("titan_stage1_tank")
                || definition.HasVehicleRole("titan_stage2_tank"))
                return CreateTitanTankNode(definition);
            if (definition.HasVehicleRole("titan_stage1_engine")
                || definition.HasVehicleRole("titan_stage2_engine"))
                return CreateTitanEngineNode(definition);
            return CreateGeminiAdapterNode(definition);
        }
        if (definition.VehicleFamily.Equals(
                "agena", StringComparison.OrdinalIgnoreCase))
            return CreateAgenaNode(definition);
        if (definition.VehicleFamily.Equals(
                "apollo", StringComparison.OrdinalIgnoreCase))
            return CreateApolloPartNode(part, hasNeighborAbove, hasNeighborBelow);
        var node = new MeshInstance3D { Name = definition.Name.Replace(" ", "_") };
        float diameter = (float)System.Math.Max(
            0.2 / MetresPerUnit, definition.DiameterM / MetresPerUnit);
        float radius = diameter * 0.5f;
        float length = (float)System.Math.Max(
            0.2 / MetresPerUnit, definition.LengthM / MetresPerUnit);
        Mesh mesh = definition.Category switch
        {
            PartCategory.Engine => new CylinderMesh
            {
                TopRadius = radius * 0.55f,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 32,
                CapTop = !hasNeighborAbove,
                CapBottom = !hasNeighborBelow,
            },
            PartCategory.Fairing => new CapsuleMesh
            {
                Radius = radius,
                Height = System.Math.Max(length, diameter),
                RadialSegments = 32,
                Rings = 12,
            },
            PartCategory.Command => new CapsuleMesh
            {
                Radius = System.Math.Min(radius, length * 0.5f),
                Height = System.Math.Max(length, diameter),
                RadialSegments = 32,
                Rings = 10,
            },
            _ => new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = 32,
                CapTop = !hasNeighborAbove,
                CapBottom = !hasNeighborBelow,
            },
        };
        node.Mesh = mesh;
        node.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            { AlbedoColor = GetCategoryColor(definition.Category), Roughness = 0.48f });
        return node;
    }

    private Node3D CreateApolloPartNode(
        Part part, bool hasNeighborAbove = false, bool hasNeighborBelow = false)
    {
        var definition = part.Definition;
        float height = (float)System.Math.Max(
            0.08, definition.LengthM / MetresPerUnit);
        float radius = (float)System.Math.Max(
            0.08, definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D
        {
            Name = definition.Name.Replace(" ", "_"),
        };
        var white = Mat(new Color(0.88f, 0.88f, 0.84f), 0.10f, 0.62f);
        var black = Mat(new Color(0.025f, 0.028f, 0.03f), 0.06f, 0.80f);
        var metal = Mat(new Color(0.58f, 0.59f, 0.57f), 0.84f, 0.34f);
        var shield = Mat(new Color(0.13f, 0.085f, 0.045f), 0.02f, 0.93f);

        if (definition.HasVehicleRole("launch_escape_system"))
        {
            var cover = new MeshInstance3D
            {
                Name = "BoostProtectiveCover",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius * 0.22f,
                    BottomRadius = radius,
                    Height = height * 0.43f,
                    RadialSegments = 48,
                },
                Position = Vector3.Down * (height * 0.285f),
            };
            cover.SetSurfaceOverrideMaterial(0, white);
            root.AddChild(cover);

            var tower = new MeshInstance3D
            {
                Name = "EscapeTower",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius * 0.075f,
                    BottomRadius = radius * 0.12f,
                    Height = height * 0.57f,
                    RadialSegments = 20,
                },
                Position = Vector3.Up * (height * 0.215f),
            };
            tower.SetSurfaceOverrideMaterial(0, black);
            root.AddChild(tower);
            return root;
        }

        if (definition.HasVehicleRole("command_module"))
        {
            var cabin = new MeshInstance3D
            {
                Name = "CommandModuleCabin",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius * 0.12f,
                    BottomRadius = radius,
                    Height = height * 0.94f,
                    RadialSegments = 56,
                },
            };
            cabin.SetSurfaceOverrideMaterial(0, metal);
            root.AddChild(cabin);
            var heatShield = new MeshInstance3D
            {
                Name = "CommandModuleHeatShield",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius,
                    BottomRadius = radius * 1.015f,
                    Height = Mathf.Max(0.04f, height * 0.06f),
                    RadialSegments = 56,
                },
                Position = Vector3.Down * (height * 0.49f),
            };
            heatShield.SetSurfaceOverrideMaterial(0, shield);
            root.AddChild(heatShield);
            return root;
        }

        if (definition.HasVehicleRole("service_module"))
        {
            var module = new MeshInstance3D
            {
                Name = "ServiceModule",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius,
                    BottomRadius = radius,
                    Height = height,
                    RadialSegments = 48,
                },
            };
            module.SetSurfaceOverrideMaterial(0, metal);
            root.AddChild(module);
            for (int panel = 0; panel < 4; panel++)
            {
                float angle = panel * Mathf.Pi * 0.5f;
                var seam = new MeshInstance3D
                {
                    Name = $"ServiceModulePanelSeam{panel}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(0.018f, height * 0.94f, 0.018f),
                    },
                    Position = new Vector3(
                        radius * Mathf.Cos(angle), 0f,
                        radius * Mathf.Sin(angle)),
                };
                seam.SetSurfaceOverrideMaterial(0, black);
                root.AddChild(seam);
            }
            return root;
        }

        bool adapter = definition.HasVehicleRole("sla_lunar_test_article")
            || definition.HasVehicleRole("sla_lunar_module");
        bool engineSection = definition.HasVehicleRole("sic_engine_cluster")
            || definition.HasVehicleRole("sii_engine_cluster")
            || definition.HasVehicleRole("sivb_engine");
        var barrel = new MeshInstance3D
        {
            Name = adapter ? "SpacecraftLunarModuleAdapter"
                : engineSection ? "EngineThrustStructure" : "SaturnAirframe",
            Mesh = new CylinderMesh
            {
                TopRadius = adapter
                    ? (float)(3.91 * 0.5 / MetresPerUnit)
                    : engineSection ? radius * 0.72f : radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 56,
                CapTop = !hasNeighborAbove,
                CapBottom = !hasNeighborBelow,
            },
        };
        barrel.SetSurfaceOverrideMaterial(
            0,
            definition.HasVehicleRole("instrument_unit")
                || engineSection ? black : white);
        root.AddChild(barrel);

        bool stageTank = definition.HasVehicleRole("sic_tank")
            || definition.HasVehicleRole("sii_tank")
            || definition.HasVehicleRole("sivb_tank");
        if (stageTank)
        {
            int bands = definition.HasVehicleRole("sic_tank") ? 2 : 1;
            for (int bandIndex = 0; bandIndex < bands; bandIndex++)
            {
                float y = bands == 1
                    ? -height * 0.32f
                    : Mathf.Lerp(-height * 0.38f, height * 0.30f, bandIndex);
                var band = new MeshInstance3D
                {
                    Name = $"SaturnIdentificationBand{bandIndex}",
                    Mesh = new CylinderMesh
                    {
                        TopRadius = radius * 1.006f,
                        BottomRadius = radius * 1.006f,
                        Height = Mathf.Max(0.045f, height * 0.045f),
                        RadialSegments = 56,
                    },
                    Position = Vector3.Up * y,
                };
                band.SetSurfaceOverrideMaterial(0, black);
                root.AddChild(band);
            }
        }
        return root;
    }

    private Node3D CreateTitanTankNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D { Name = definition.Name.Replace(" ", "_") };
        var white = Mat(new Color(0.82f, 0.84f, 0.82f), 0.14f, 0.54f);
        var black = Mat(new Color(0.035f, 0.04f, 0.045f), 0.05f, 0.75f);
        var barrel = new MeshInstance3D
        {
            Name = "TitanAirframe",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 48,
            },
        };
        barrel.SetSurfaceOverrideMaterial(0, white);
        root.AddChild(barrel);
        int bands = definition.HasVehicleRole("titan_stage1_tank") ? 3 : 2;
        for (int i = 0; i < bands; i++)
        {
            float fraction = (i + 1f) / (bands + 1f);
            var band = new MeshInstance3D
            {
                Name = $"IdentificationBand{i}",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius * 1.008f,
                    BottomRadius = radius * 1.008f,
                    Height = Mathf.Max(0.035f, height * 0.035f),
                    RadialSegments = 48,
                },
                Position = Vector3.Up * (-height * 0.5f + height * fraction),
            };
            band.SetSurfaceOverrideMaterial(0, black);
            root.AddChild(band);
        }
        return root;
    }

    private Node3D CreateTitanEngineNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var node = new MeshInstance3D
        {
            Name = definition.Name.Replace(" ", "_"),
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.72f,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 40,
            },
        };
        node.SetSurfaceOverrideMaterial(
            0, Mat(new Color(0.16f, 0.17f, 0.18f), 0.78f, 0.46f));
        return node;
    }

    private Node3D CreateGeminiCapsuleNode(Part part)
    {
        var definition = part.Definition;
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D { Name = "GeminiReentryModule" };
        var black = Mat(new Color(0.055f, 0.06f, 0.065f), 0.22f, 0.72f);
        var shield = Mat(new Color(0.18f, 0.13f, 0.08f), 0.02f, 0.92f);
        var glass = Mat(new Color(0.03f, 0.10f, 0.14f), 0.32f, 0.16f);
        var cabin = new MeshInstance3D
        {
            Name = "BlackPressureCabin",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.38f,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 48,
            },
        };
        cabin.SetSurfaceOverrideMaterial(0, black);
        root.AddChild(cabin);
        var heatShield = new MeshInstance3D
        {
            Name = "AblativeHeatShield",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius * 1.015f,
                Height = 0.10f,
                RadialSegments = 48,
            },
            Position = Vector3.Down * (height * 0.5f),
        };
        heatShield.SetSurfaceOverrideMaterial(0, shield);
        root.AddChild(heatShield);
        for (int side = -1; side <= 1; side += 2)
        {
            var window = new MeshInstance3D
            {
                Name = side < 0 ? "LeftWindow" : "RightWindow",
                Mesh = new BoxMesh
                {
                    Size = new Vector3(
                        radius * 0.28f, height * 0.20f, 0.025f),
                },
                Position = new Vector3(
                    side * radius * 0.34f,
                    height * 0.12f,
                    radius * 0.73f),
            };
            window.SetSurfaceOverrideMaterial(0, glass);
            root.AddChild(window);
        }
        var canopy = new Node3D
        {
            Name = "GeminiMainParachute",
            Position = Vector3.Up * (height * 0.5f + 3.0f),
            Visible = false,
        };
        var canopyMesh = new MeshInstance3D
        {
            Mesh = new SphereMesh
            {
                Radius = 3.8f,
                Height = 2.2f,
                RadialSegments = 48,
                Rings = 16,
            },
        };
        canopyMesh.SetSurfaceOverrideMaterial(
            0, Mat(new Color(0.90f, 0.42f, 0.20f), 0.0f, 0.78f));
        canopy.AddChild(canopyMesh);
        root.AddChild(canopy);
        _parachuteVisuals[part.InstanceId] = canopy;
        return root;
    }

    private Node3D CreateGeminiAdapterNode(PartDefinition definition)
    {
        float height = (float)System.Math.Max(
            0.08, definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        bool nose = definition.HasVehicleRole("gemini_docking_port");
        var node = new MeshInstance3D
        {
            Name = definition.Name.Replace(" ", "_"),
            Mesh = new CylinderMesh
            {
                TopRadius = nose ? radius * 0.48f : radius * 0.88f,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 40,
            },
        };
        node.SetSurfaceOverrideMaterial(
            0,
            Mat(nose
                ? new Color(0.76f, 0.77f, 0.75f)
                : new Color(0.88f, 0.88f, 0.84f),
                nose ? 0.62f : 0.18f,
                0.48f));
        return node;
    }

    private Node3D CreateAgenaNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        bool port = definition.HasVehicleRole("agena_target_docking_port");
        var node = new MeshInstance3D
        {
            Name = definition.Name.Replace(" ", "_"),
            Mesh = new CylinderMesh
            {
                TopRadius = port ? radius : radius * 0.88f,
                BottomRadius = port ? radius * 0.34f : radius,
                Height = height,
                RadialSegments = 40,
            },
        };
        node.SetSurfaceOverrideMaterial(
            0,
            Mat(port
                ? new Color(0.74f, 0.72f, 0.67f)
                : new Color(0.20f, 0.23f, 0.25f),
                port ? 0.72f : 0.42f,
                port ? 0.35f : 0.62f));
        return node;
    }

    private Node3D CreateMercuryCapsuleNode(Part part)
    {
        var definition = part.Definition;
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D { Name = "MercuryCapsule" };
        var silver = Mat(new Color(0.72f, 0.74f, 0.75f), 0.78f, 0.34f);
        var shield = Mat(new Color(0.10f, 0.075f, 0.055f), 0.05f, 0.92f);
        var window = Mat(new Color(0.035f, 0.09f, 0.13f), 0.25f, 0.18f);

        var cabin = new MeshInstance3D
        {
            Name = "PressureCabin",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.16f,
                BottomRadius = radius,
                Height = height * 0.92f,
                RadialSegments = 48,
            },
            Position = Vector3.Up * (height * 0.04f),
        };
        cabin.SetSurfaceOverrideMaterial(0, silver);
        root.AddChild(cabin);

        var heatShield = new MeshInstance3D
        {
            Name = "AblativeHeatShield",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius * 1.03f,
                Height = 0.10f,
                RadialSegments = 48,
            },
            Position = Vector3.Down * (height * 0.46f),
        };
        heatShield.SetSurfaceOverrideMaterial(0, shield);
        root.AddChild(heatShield);

        var porthole = new MeshInstance3D
        {
            Name = "Porthole",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.12f,
                BottomRadius = radius * 0.12f,
                Height = 0.035f,
                RadialSegments = 24,
            },
            Position = new Vector3(0f, height * 0.10f, radius * 0.82f),
            RotationDegrees = new Vector3(90f, 0f, 0f),
        };
        porthole.SetSurfaceOverrideMaterial(0, window);
        root.AddChild(porthole);

        var canopy = new Node3D
        {
            Name = "MainParachute",
            Position = Vector3.Up * (height * 0.5f + 3.0f),
            Visible = false,
        };
        var canopyMesh = new MeshInstance3D
        {
            Name = "Canopy",
            Mesh = new SphereMesh
            {
                Radius = 3.4f,
                Height = 2.1f,
                RadialSegments = 48,
                Rings = 16,
            },
        };
        canopyMesh.SetSurfaceOverrideMaterial(
            0,
            Mat(new Color(0.88f, 0.34f, 0.25f), 0.0f, 0.76f));
        canopy.AddChild(canopyMesh);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Tau / 8f;
            var line = new MeshInstance3D
            {
                Name = $"SuspensionLine{i}",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.008f,
                    BottomRadius = 0.008f,
                    Height = 3.0f,
                    RadialSegments = 8,
                },
                Position = new Vector3(
                    1.8f * Mathf.Cos(angle),
                    -1.8f,
                    1.8f * Mathf.Sin(angle)),
                RotationDegrees = new Vector3(
                    20f * Mathf.Sin(angle),
                    0f,
                    -20f * Mathf.Cos(angle)),
            };
            line.SetSurfaceOverrideMaterial(
                0,
                Mat(new Color(0.78f, 0.76f, 0.68f), 0.0f, 0.8f));
            canopy.AddChild(line);
        }
        root.AddChild(canopy);
        _parachuteVisuals[part.InstanceId] = canopy;
        return root;
    }

    private Node3D CreateMercuryEscapeTowerNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        var root = new Node3D { Name = "MercuryEscapeTower" };
        var red = Mat(new Color(0.66f, 0.16f, 0.10f), 0.35f, 0.55f);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Tau / 4f;
            var strut = new MeshInstance3D
            {
                Name = $"TowerStrut{i}",
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.025f,
                    BottomRadius = 0.025f,
                    Height = height * 0.78f,
                    RadialSegments = 10,
                },
                Position = new Vector3(
                    0.10f * Mathf.Cos(angle),
                    -height * 0.06f,
                    0.10f * Mathf.Sin(angle)),
            };
            strut.SetSurfaceOverrideMaterial(0, red);
            root.AddChild(strut);
        }
        var motor = new MeshInstance3D
        {
            Name = "EscapeMotor",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.05f,
                BottomRadius = 0.16f,
                Height = height * 0.22f,
                RadialSegments = 24,
            },
            Position = Vector3.Up * (height * 0.42f),
        };
        motor.SetSurfaceOverrideMaterial(0, red);
        root.AddChild(motor);
        return root;
    }

    private Node3D CreateMercuryAdapterNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var node = new MeshInstance3D
        {
            Name = "MercuryAdapter",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.72f,
                BottomRadius = radius,
                Height = Mathf.Max(0.05f, height),
                RadialSegments = 32,
            },
        };
        node.SetSurfaceOverrideMaterial(
            0,
            Mat(new Color(0.76f, 0.77f, 0.74f), 0.45f, 0.48f));
        return node;
    }

    private Node3D CreateAtlasTankNode(PartDefinition definition)
    {
        float height = (float)(definition.LengthM / MetresPerUnit);
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D { Name = "AtlasBalloonTank" };
        var steel = Mat(new Color(0.70f, 0.72f, 0.70f), 0.94f, 0.20f);
        var seam = Mat(new Color(0.43f, 0.44f, 0.42f), 0.88f, 0.30f);

        var shell = new MeshInstance3D
        {
            Name = "PressureStabilizedShell",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 64,
            },
        };
        shell.SetSurfaceOverrideMaterial(0, steel);
        root.AddChild(shell);

        for (int i = 1; i < 9; i++)
        {
            float y = -height * 0.5f + height * i / 9f;
            var weld = new MeshInstance3D
            {
                Name = $"BalloonTankWeld{i}",
                Mesh = new CylinderMesh
                {
                    TopRadius = radius * 1.004f,
                    BottomRadius = radius * 1.004f,
                    Height = 0.018f,
                    RadialSegments = 64,
                },
                Position = Vector3.Up * y,
            };
            weld.SetSurfaceOverrideMaterial(0, seam);
            root.AddChild(weld);
        }

        void AddNationalMarking(
            string suffix,
            Vector3 position,
            Vector3 rotationDegrees)
        {
            var marking = new Label3D
            {
                Name = $"UnitedStatesMarking{suffix}",
                Text = "UNITED STATES",
                FontSize = 42,
                PixelSize = 0.0065f,
                Modulate = new Color(0.045f, 0.045f, 0.04f),
                OutlineSize = 0,
                Position = position,
                RotationDegrees = rotationDegrees,
            };
            root.AddChild(marking);
        }

        // Place the historical marking on the pad-camera face. Keeping a single
        // inscription avoids doubled text where adjacent cylinder faces converge.
        float markingY = height * 0.12f;
        float markingR = radius * 1.012f;
        AddNationalMarking(
            "Aft",
            new Vector3(0.0f, markingY, -markingR),
            new Vector3(0.0f, 180.0f, -90.0f));
        return root;
    }

    private Node3D CreateAtlasEngineSectionNode(
        PartDefinition definition)
    {
        float height = (float)System.Math.Max(
            0.25, definition.LengthM / MetresPerUnit);
        float radius = (float)System.Math.Max(
            0.30, definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D
        {
            Name = definition.HasVehicleRole("booster_engine_package")
                ? "AtlasBoosterPackage"
                : "AtlasSustainerSection",
        };
        var skirt = new MeshInstance3D
        {
            Name = "EngineSkirt",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.84f,
                BottomRadius = radius,
                Height = height,
                RadialSegments = 48,
            },
        };
        skirt.SetSurfaceOverrideMaterial(
            0,
            Mat(new Color(0.54f, 0.55f, 0.53f), 0.88f, 0.28f));
        root.AddChild(skirt);

        if (definition.HasVehicleRole("booster_engine_package"))
        {
            var dark = Mat(
                new Color(0.16f, 0.16f, 0.15f), 0.72f, 0.42f);
            foreach (float x in new[] { -radius * 0.62f, radius * 0.62f })
            {
                var pod = new MeshInstance3D
                {
                    Name = x < 0 ? "YLR89PodLeft" : "YLR89PodRight",
                    Mesh = new CylinderMesh
                    {
                        TopRadius = radius * 0.22f,
                        BottomRadius = radius * 0.28f,
                        Height = height * 0.86f,
                        RadialSegments = 32,
                    },
                    Position = new Vector3(x, -height * 0.08f, 0.0f),
                };
                pod.SetSurfaceOverrideMaterial(0, dark);
                root.AddChild(pod);
            }
        }
        return root;
    }

    private Node3D CreateMercuryRetroPackNode(
        PartDefinition definition)
    {
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        var root = new Node3D { Name = "MercuryRetroPack" };
        var pack = new MeshInstance3D
        {
            Name = "RetroPackage",
            Mesh = new CylinderMesh
            {
                TopRadius = radius * 0.96f,
                BottomRadius = radius,
                Height = 0.12f,
                RadialSegments = 48,
            },
        };
        pack.SetSurfaceOverrideMaterial(
            0,
            Mat(new Color(0.16f, 0.12f, 0.085f), 0.10f, 0.86f));
        root.AddChild(pack);
        return root;
    }

    private Node3D CreateMercurySeparationRingNode(
        PartDefinition definition)
    {
        float radius = (float)(definition.DiameterM * 0.5 / MetresPerUnit);
        float height = (float)System.Math.Max(
            0.08, definition.LengthM / MetresPerUnit);
        var ring = new MeshInstance3D
        {
            Name = "MercuryAtlasSeparationRing",
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius * 1.02f,
                Height = height,
                RadialSegments = 48,
            },
        };
        ring.SetSurfaceOverrideMaterial(
            0,
            Mat(new Color(0.22f, 0.22f, 0.20f), 0.72f, 0.42f));
        return ring;
    }

    private void UpdateParachutes()
    {
        if (TargetVessel == null || _parachuteVisuals.Count == 0) return;
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!_parachuteVisuals.TryGetValue(part.InstanceId, out var canopy))
                continue;
            canopy.Visible = part.IsDeployed;
        }
    }

    private static Color GetCategoryColor(PartCategory cat) => cat switch
    {
        PartCategory.Command   => new Color(0.9f, 0.9f, 0.9f),
        PartCategory.Engine    => new Color(0.6f, 0.6f, 0.65f),
        PartCategory.FuelTank  => new Color(0.8f, 0.85f, 0.9f),
        PartCategory.Decoupler => new Color(1f, 0.8f, 0.2f),
        PartCategory.Fairing   => new Color(0.94f, 0.94f, 0.92f),
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
            Metallic         = metallic > 0.45f ? Mathf.Min(metallic, 0.62f) : metallic,
            MetallicSpecular = 0.55f,
            Roughness        = roughness,
            RimEnabled       = false,
            EmissionEnabled  = metallic > 0.45f,
            Emission         = albedo * new Color(0.55f, 0.72f, 1.0f),
            EmissionEnergyMultiplier = metallic > 0.45f ? 2.2f : 0f,
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
        Color tint, float metallic = 0.58f, float roughness = 0.28f,
        float weldSpacing = 1.6f, float sootBot = -1000f, float sootTop = -1000f)
    {
        var m = new ShaderMaterial { Shader = SteelShader };
        m.SetShaderParameter("base_tint",    tint);
        m.SetShaderParameter("metallic_val", metallic);
        m.SetShaderParameter("rough_val",    roughness);
        m.SetShaderParameter("spec_val",     0.62f);
        m.SetShaderParameter("weld_spacing", weldSpacing);
        m.SetShaderParameter("weld_depth",   0.08f);
        m.SetShaderParameter("brush_amt",    0.06f);
        m.SetShaderParameter("soot_y0",      sootTop);   // clean above
        m.SetShaderParameter("soot_y1",      sootBot);   // sooty toward engine
        m.SetShaderParameter("soot_color",   new Color(0.16f, 0.15f, 0.15f));
        m.SetShaderParameter("fill_strength", 0.038f);
        m.SetShaderParameter("rim_color",    new Color(0.55f, 0.72f, 1.0f));
        m.SetShaderParameter("rim_strength", 0.18f);
        m.SetShaderParameter("sky_bounce",   0.92f);
        m.SetShaderParameter("sun_dir",      new Vector3(0.35f, 0.78f, 0.25f));
        m.SetShaderParameter("engine_light", 0.0f);
        m.SetShaderParameter("emit_strength", 0.0f);
        _steelMats.Add(m);
        return m;
    }

    // Black hexagonal heat-shield tiles: dark, matte, dielectric (non-metal),
    // with a touch of micro-specular so panel edges still catch light.
    private static Shader? _tileShader;
    private static Shader TileShader =>
        _tileShader ??= GD.Load<Shader>("res://assets/shaders/heat_tile.gdshader");

    private static ShaderMaterial TileMat()
    {
        var m = new ShaderMaterial { Shader = TileShader };
        m.SetShaderParameter("albedo_color", TileBaseColor);
        m.SetShaderParameter("roughness_val", 0.93f);
        // Kept for material compatibility; panel gaps are geometry, not a
        // fragment-level procedural pattern.
        m.SetShaderParameter("hex_scale", 1.0f);
        m.SetShaderParameter("gap_width", 0.0f);
        m.SetShaderParameter("char_amt", 0.0f);
        m.SetShaderParameter("rim_strength", 0.06f);
        m.SetShaderParameter("sky_bounce", 1.0f);
        m.SetShaderParameter("emit_strength", 0.0f);
        return m;
    }

    private MeshInstance3D AddMesh(string name, Mesh mesh, Material mat, Vector3 pos)
    {
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
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
    private void AddTileBand(float yBottom, float yTop, TileCharZone zone,
        float topRadius = BodyR + 0.015f, float botRadius = BodyR + 0.015f)
    {
        var tiles = TileMat();
        RegisterTileMat(zone, tiles);
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

    // Segmented windward TPS that conforms to the narrowing nose ogive.
    private void AddNoseTileShell(float yBase, float noseLength, Func<float, float> radiusAt)
    {
        var tiles = TileMat();
        RegisterTileMat(TileCharZone.Nose, tiles);
        const int rows = 14;
        const int staves = 12;
        const float arc = 3.49f;
        float rowH = noseLength / rows;

        for (int row = 0; row < rows; row++)
        {
            float u0 = row / (float)rows;
            float u1 = (row + 1f) / rows;
            float um = (u0 + u1) * 0.5f;
            float r = radiusAt(um) + 0.035f;
            if (r < 0.08f) continue;
            float width = Mathf.Max(0.045f, r * arc / staves * 0.985f);
            float y = yBase + um * noseLength;

            for (int i = 0; i < staves; i++)
            {
                float a = Mathf.Pi - arc * 0.5f + arc * (i + 0.5f) / staves;
                var tile = new MeshInstance3D
                {
                    Name = $"NoseTile_{row}_{i}",
                    Mesh = new BoxMesh { Size = new Vector3(width, rowH * 0.985f, 0.070f) },
                    Position = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a)),
                    RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a) + 90f, 0f),
                };
                tile.SetSurfaceOverrideMaterial(0, tiles);
                AddChild(tile);
            }
        }
    }

    // A Starship aerodynamic flap: a tile-covered slab plus a steel root, both
    // mounted on the windward (-X) side and offset around the body by `angOff`
    // radians from the -X axis.
    private void AddFlap(string name, float y, float length, float chord, float angOff, Material mat)
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
        _flapRigs.Add(new FlapRig
        {
            Blade = blade,
            Neutral = blade.Quaternion,
            Side = name.EndsWith("L", StringComparison.Ordinal) ? -1f : 1f,
            Forward = name.StartsWith("Fwd", StringComparison.Ordinal),
        });

        var edgeMat = Mat(new Color(0.020f, 0.020f, 0.024f), 0.0f, 0.94f);
        var leading = new MeshInstance3D
        {
            Name = name + "LeadingEdge",
            Mesh = new BoxMesh { Size = new Vector3(0.12f, length * 0.96f, 0.070f) },
            Position = new Vector3(chord * 0.45f, 0f, -0.10f),
        };
        leading.SetSurfaceOverrideMaterial(0, edgeMat);
        blade.AddChild(leading);

        // Denser tile seams (∼6 rows) so flaps read as tiled TPS at mid distance.
        for (int s = -3; s <= 3; s++)
        {
            var seam = new MeshInstance3D
            {
                Name = $"{name}TileSeam{s}",
                Mesh = new BoxMesh { Size = new Vector3(chord * 0.72f, 0.028f, 0.055f) },
                Position = new Vector3(0f, s * length * 0.125f, -0.105f),
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
    private StandardMaterial3D? _bellMat, _throatMat, _powerMat, _bellLipMat;

    private void AddRaptor(string name, Vector3 pos, float exitR, float throatR, float bellLen,
        int bellRings = 4)
    {
        // Raptor's external nozzle hardware reads as dark Inconel/steel, not as
        // a uniform copper flowerpot. A brighter lip catches the exit-plane light.
        _bellMat   ??= Mat(new Color(0.17f, 0.16f, 0.15f), 0.94f, 0.30f);
        _bellLipMat ??= Mat(new Color(0.38f, 0.35f, 0.32f), 0.96f, 0.24f);
        // Recessed throat: very dark, slightly rough (soot + shadow up the bell).
        _throatMat ??= Mat(new Color(0.06f, 0.055f, 0.05f), 0.55f, 0.70f);
        _bellMat.EmissionEnabled = true;
        _bellLipMat.EmissionEnabled = true;
        _throatMat.EmissionEnabled = true;
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
                new CylinderMesh { TopRadius = rTop, BottomRadius = rBot,
                    Height = h * 1.05f, RadialSegments = 36,
                    CapTop = false, CapBottom = false },
                _bellMat, new Vector3(pos.X, yc, pos.Z));
        }

        AddMesh($"{name}ExitLip",
            new CylinderMesh { TopRadius = exitR * 1.015f, BottomRadius = exitR * 1.015f,
                Height = 0.055f, RadialSegments = 48, CapTop = false, CapBottom = false },
            _bellLipMat, new Vector3(pos.X, pos.Y + 0.028f, pos.Z));

        // Thin-wall RVac extensions have conspicuous longitudinal stiffeners.
        if (bellRings >= 8)
        {
            const int stiffeners = 12;
            for (int i = 0; i < stiffeners; i++)
            {
                float a = i * Mathf.Tau / stiffeners;
                float rr = exitR * 0.89f;
                var rib = new MeshInstance3D
                {
                    Name = $"{name}Stiffener{i}",
                    Mesh = new BoxMesh
                        { Size = new Vector3(0.028f, bellLen * 0.66f, 0.045f) },
                    Position = new Vector3(pos.X + rr * Mathf.Cos(a),
                        pos.Y + bellLen * 0.34f, pos.Z + rr * Mathf.Sin(a)),
                    RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a), 0f),
                };
                rib.SetSurfaceOverrideMaterial(0, _bellLipMat);
                AddChild(rib);
            }
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

        // Two compact turbopump housings and feed-line risers flank the powerhead.
        float nub = topR * 0.9f;
        AddMesh($"{name}Pump0",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 14, Rings = 8 },
            _powerMat, new Vector3(pos.X + nub, pos.Y + bellLen * 1.05f, pos.Z));
        AddMesh($"{name}Pump1",
            new SphereMesh { Radius = topR * 0.5f, Height = topR, RadialSegments = 14, Rings = 8 },
            _powerMat, new Vector3(pos.X - nub, pos.Y + bellLen * 1.05f, pos.Z));
        foreach (float side in new[] { -1f, 1f })
            AddMesh($"{name}Feed{(side < 0 ? "L" : "R")}",
                new CylinderMesh { TopRadius = topR * 0.10f, BottomRadius = topR * 0.10f,
                    Height = bellLen * 0.55f, RadialSegments = 10 },
                _bellLipMat, new Vector3(pos.X + side * topR * 0.72f,
                    pos.Y + bellLen * 0.90f, pos.Z + topR * 0.55f));
    }

    private void ClearNodes()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _partNodes.Clear();
        _parachuteVisuals.Clear();
        _tileZoneMats.Clear();
        _shipSteelMats.Clear();
        _steelMats.Clear();
        _engineBayLight = null;
        _engineRimLight = null;
        _skyFillLight = null;
        _flapRigs.Clear();
        _landingLegRigs.Clear();
        _landingGearDeployment = 0f;
        _plumes   = null;
        _usesGenericPlumes = false;
        _hasSuperHeavy = false;
        _selectedShipEngines = 6;
        _superHeavyEngineIds.Clear();
        _shipEngineIds.Clear();
        _perEngineThrottle.Clear();
        _engineReadoutScratch.Clear();
        _engineVisualTimer = 0.0;
        _thermalVisualTimer = 0.0;
        _landingGearStateTimer = 0.0;
        _parachuteStateTimer = 0.0;
        _flapInputTimer = 0.0;
        _lastGlow = float.NaN;
        _lastVisualHotStage = null;
        _lastVisualSuperHeavyThrottle = float.NaN;
        _lastVisualShipThrottle = float.NaN;
        _cachedPresentationBody = null;
        _cachedPresentationAltitude = 0.0;
        _cachedPresentationPressureRatio = 0.0;
        _presentationSampleTimer = 0.0;
        _hullMesh = null;
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
