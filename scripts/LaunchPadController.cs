namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;

/// <summary>
/// Starbase-inspired orbital launch complex (OLM + Mechazilla tower + tank
/// farm + lightning towers). This Node3D is positioned each frame by
/// SimulationBridge so it stays anchored to the Earth surface directly below
/// the vessel.
///
/// Scale: render units are ~2.8 m/unit (see VesselRenderer). The vessel sits
/// at the render origin with its base (booster engine tips) at y≈0 and a body
/// radius of ~1.607 units (~9 m diameter, matches VesselRenderer.BodyR). Everything
/// here is positioned so the booster fires down through the centre of the launch mount.
/// </summary>
public partial class LaunchPadController : Node3D
{
    public static LaunchPadController? Instance { get; private set; }

    // 1 render unit ≈ 2.8 m. Helper so the code below can read in metres.
    private const float U = 1f / 2.8f;   // metres → render units
    private static readonly LaunchComplexSpec Spec = LaunchComplexSpec.StarbasePostDeluge;
    private const float GradeY = 0f;
    private static readonly float VehicleInterfaceY = (float)Spec.VehicleInterfaceElevation * U;

    // Match VesselRenderer.BodyR — 9 m Ø hull (4.5 m radius).
    private const float VesselBodyR = 1.607f;
    private readonly List<SpotLight3D> _nightFloodlights = new();

    public override void _Ready()
    {
        Instance = this;
        BuildEnvironment();
    }

    public override void _Process(double delta)
    {
        bool night = SunController.SolarVisibility < 0.20f;
        foreach (var light in _nightFloodlights)
            light.Visible = night;
    }

    private void BuildEnvironment()
    {
        // ── Shared materials (kept few so they batch) ──────────────────────
        var concrete  = Mat(new Color(0.34f, 0.33f, 0.31f), 0.95f, 0.0f);
        var concDark  = Mat(new Color(0.24f, 0.23f, 0.21f), 0.97f, 0.0f);
        var burnt     = Mat(new Color(0.09f, 0.08f, 0.07f), 0.98f, 0.0f);
        var steel     = Mat(new Color(0.55f, 0.56f, 0.58f), 0.55f, 0.85f); // grey lattice steel
        var darkSteel = Mat(new Color(0.28f, 0.28f, 0.31f), 0.60f, 0.80f); // OLM / dark steel
        var insul     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.80f, 0.10f); // white cryo tanks
        // Weathered, slightly lighter concrete for the wide tarmac, plus a mid
        // scorch tone between clean concrete and fully-charred burnt.
        var tarmac    = Mat(new Color(0.30f, 0.30f, 0.28f), 0.96f, 0.0f); // weathered apron
        var scorch    = Mat(new Color(0.16f, 0.15f, 0.13f), 0.97f, 0.0f); // blast-zone stain
        var sandFill  = Mat(new Color(0.31f, 0.28f, 0.21f), 0.98f, 0.0f);
        var gravel    = Mat(new Color(0.20f, 0.19f, 0.17f), 0.98f, 0.0f);
        var asphalt   = Mat(new Color(0.08f, 0.085f, 0.08f), 0.96f, 0.0f);
        var paint     = Mat(new Color(0.62f, 0.52f, 0.22f), 0.88f, 0.0f);

        BuildStarbaseCivilWorks(sandFill, gravel, asphalt, tarmac, concDark, paint);
        BuildOrbitalLaunchMount(darkSteel, concDark);
        BuildMechazillaTower(steel, darkSteel);
        BuildTankFarm(insul, steel);
        BuildGroundSupport(insul, steel, darkSteel, concrete, concDark);
        BuildNightFloodlights();
    }

    /// <summary>
    /// One coherent grade-level civil assembly for Boca Chica. This replaces the former
    /// stack of overlapping 700 m ground boxes, four apron skins and duplicated roads.
    /// Dimensions are in metres and share <see cref="GradeY"/> with every structural footing.
    /// </summary>
    private void BuildStarbaseCivilWorks(StandardMaterial3D sandFill, StandardMaterial3D gravel,
        StandardMaterial3D asphalt, StandardMaterial3D concrete, StandardMaterial3D joint,
        StandardMaterial3D paint)
    {
        Spawn("StarbaseCoastalFill",
            new BoxMesh { Size = new Vector3(300f * U, 2.0f * U, 240f * U) },
            sandFill, new Vector3(-18f * U, GradeY - 1.0f * U, 0));
        Spawn("OrbitalPadApron",
            new BoxMesh { Size = new Vector3(126f * U, 0.35f * U, 112f * U) },
            concrete, new Vector3(0, GradeY + 0.175f * U, 0));
        Spawn("OlmFoundationMat",
            new CylinderMesh { TopRadius = 22f * U, BottomRadius = 24f * U,
                Height = 1.2f * U, RadialSegments = 48 },
            gravel, new Vector3(0, GradeY + 0.6f * U, 0));

        // Highway 4 / site access approaches from inland and terminates west of the pad.
        SpawnRot("StateHighway4Access",
            new BoxMesh { Size = new Vector3(14f * U, 0.18f * U, 330f * U) },
            asphalt, new Vector3(-92f * U, GradeY + 0.12f * U, 58f * U),
            new Vector3(0, -5f, 0));
        Spawn("TankFarmServiceLane",
            new BoxMesh { Size = new Vector3(115f * U, 0.16f * U, 11f * U) },
            asphalt, new Vector3(52f * U, GradeY + 0.11f * U, 38f * U));

        // Expansion joints are sparse and non-coplanar with the slab to avoid z-fighting.
        for (int i = -2; i <= 2; i++)
        {
            Spawn($"ApronJointX{i}", new BoxMesh
                { Size = new Vector3(0.18f * U, 0.04f * U, 108f * U) },
                joint, new Vector3(i * 20f * U, GradeY + 0.37f * U, 0));
            Spawn($"ApronJointZ{i}", new BoxMesh
                { Size = new Vector3(122f * U, 0.04f * U, 0.18f * U) },
                joint, new Vector3(0, GradeY + 0.37f * U, i * 18f * U));
        }

        for (int i = 0; i < 9; i++)
            Spawn($"HighwayCenterMark{i}", new BoxMesh
                { Size = new Vector3(0.35f * U, 0.04f * U, 8f * U) },
                paint, new Vector3(-92f * U, GradeY + 0.23f * U, (-78f + i * 28f) * U));
    }

    private void BuildNightFloodlights()
    {
        // High-mast industrial floodlights keep the vehicle and working deck visible during
        // a physically dark launch window. They switch off in daylight instead of adding a
        // permanent fill light to every exterior scene.
        var positions = new[]
        {
            new Vector3(-18f, 24f,  16f),
            new Vector3( 18f, 24f,  16f),
            new Vector3(-14f, 18f, -18f),
            new Vector3( 14f, 18f, -18f),
        };
        foreach (var pos in positions)
        {
            var light = new SpotLight3D
            {
                Name = "NightFloodlight",
                LightColor = new Color(1.0f, 0.86f, 0.68f),
                LightEnergy = 42f,
                SpotRange = 130f,
                SpotAngle = 42f,
                ShadowEnabled = true,
                Visible = false,
            };
            AddChild(light);
            light.LookAtFromPosition(pos, new Vector3(0f, 20f, 0f), Vector3.Up);
            _nightFloodlights.Add(light);
        }
    }

    // ── Raised concrete pad + flame deflector / trench ────────────────────
    private void BuildConcretePad(StandardMaterial3D concrete, StandardMaterial3D concDark,
                                  StandardMaterial3D burnt)
    {
        // Large ground apron (well below the mount so the pad reads as raised).
        Spawn("Ground", new BoxMesh { Size = new Vector3(700f * U, 1f, 700f * U) },
            concrete, new Vector3(0, -22f * U - 0.5f, 0));

        // Raised square concrete launch-pad base the mount stands on (~75 m wide,
        // ~6 m thick), top a couple of metres below the mount legs' footing.
        Spawn("PadBase", new BoxMesh { Size = new Vector3(75f * U, 6f * U, 75f * U) },
            concrete, new Vector3(0, -22f * U + 3f * U, 0));

        // Flame deflector / trench: a sloped concrete wedge below the OLM centre
        // that throws the exhaust out to one side (-Z direction).
        Spawn("FlameTrench", new BoxMesh { Size = new Vector3(36f * U, 14f * U, 90f * U) },
            burnt, new Vector3(0, -22f * U + 7f * U, 38f * U));

        // Sloped deflector face directly under the mount, tilted to vent exhaust.
        var deflector = new MeshInstance3D
        {
            Name            = "FlameDeflector",
            Mesh            = new BoxMesh { Size = new Vector3(30f * U, 2f * U, 40f * U) },
            Position        = new Vector3(0, -22f * U + 13f * U, 22f * U),
            RotationDegrees = new Vector3(28f, 0, 0),
        };
        deflector.SetSurfaceOverrideMaterial(0, burnt);
        AddChild(deflector);

        // Charred apron immediately around the mount footing.
        Spawn("ScorchApron", new CylinderMesh
            { TopRadius = 22f * U, BottomRadius = 22f * U, Height = 0.5f * U, RadialSegments = 32 },
            concDark, new Vector3(0, -22f * U + 6.1f * U, 0));

        // ── Open flame channel venting the exhaust out to +Z ──────────────
        // Two angled side walls forming a sloped concrete channel, and a long
        // open trough downstream of the deflector so the trench reads as a real
        // vent rather than a solid block.
        const float padY    = -22f * U + 6.5f * U;
        const float trenchZ0 = 18f * U;       // channel starts just past the mount
        const float trenchLen = 70f * U;       // runs well out to +Z
        const float wallH    = 9f * U;
        const float halfChan = 13f * U;        // half-width of the open channel

        foreach (var side in new[] { -1f, 1f })
        {
            var wall = new MeshInstance3D
            {
                Name            = side < 0 ? "TrenchWallL" : "TrenchWallR",
                Mesh            = new BoxMesh { Size = new Vector3(2.5f * U, wallH, trenchLen) },
                Position        = new Vector3(side * (halfChan + 1f * U),
                                              padY + wallH * 0.5f - 1f * U,
                                              trenchZ0 + trenchLen * 0.5f),
                RotationDegrees = new Vector3(0, 0, side * 14f),  // splay outward at top
            };
            wall.SetSurfaceOverrideMaterial(0, burnt);
            AddChild(wall);
        }

        // Charred channel floor between the walls (slightly below pad surface).
        Spawn("TrenchFloor", new BoxMesh
            { Size = new Vector3(halfChan * 2f, 1f * U, trenchLen) },
            burnt, new Vector3(0, padY - 0.5f * U, trenchZ0 + trenchLen * 0.5f));

        // Raised lip/berm at the far end where the exhaust exits the trench.
        Spawn("TrenchExitBerm", new BoxMesh
            { Size = new Vector3(halfChan * 2.4f, 5f * U, 4f * U) },
            concDark, new Vector3(0, padY + 1.5f * U, trenchZ0 + trenchLen));
    }

    // ── Coastal launch site: filled island, access roads and service marks ──
    private void BuildCoastalSite(StandardMaterial3D sandFill, StandardMaterial3D gravel,
                                  StandardMaterial3D asphalt, StandardMaterial3D tarmac,
                                  StandardMaterial3D concDark, StandardMaterial3D paint)
    {
        const float padY = GradeY;
        const float fillTop = padY - 0.08f * U;
        const float surfaceY = padY + 0.03f * U;

        // Broad compacted coastal fill under the concrete complex. This breaks
        // the "green field" read and gives the pad a Starbase-like industrial
        // island silhouette before the real Earth texture takes over in the far
        // distance.
        Spawn("CoastalFill", new BoxMesh { Size = new Vector3(360f * U, 0.65f * U, 300f * U) },
            sandFill, new Vector3(-8f * U, fillTop - 0.32f * U, 0));
        Spawn("GravelPadShoulder", new BoxMesh { Size = new Vector3(245f * U, 0.22f * U, 220f * U) },
            gravel, new Vector3(0, fillTop + 0.02f * U, 0));

        // The true-scale Earth patch sits at the surface under the vehicle and
        // can visually cover old below-grade pad slabs. These thin ground skins
        // sit just above that patch, with a centre opening so they do not cover
        // the OLM table or hold-down hardware.
        const float skinY = 0.08f * U;
        Spawn("SurfaceApronNorth", new BoxMesh { Size = new Vector3(190f * U, 0.10f * U, 62f * U) },
            tarmac, new Vector3(0, skinY, -61f * U));
        Spawn("SurfaceApronSouth", new BoxMesh { Size = new Vector3(190f * U, 0.10f * U, 62f * U) },
            tarmac, new Vector3(0, skinY, 61f * U));
        Spawn("SurfaceApronWest", new BoxMesh { Size = new Vector3(62f * U, 0.10f * U, 66f * U) },
            tarmac, new Vector3(-64f * U, skinY, 0));
        Spawn("SurfaceApronEast", new BoxMesh { Size = new Vector3(62f * U, 0.10f * U, 66f * U) },
            tarmac, new Vector3(64f * U, skinY, 0));
        Spawn("SurfaceFillWest", new BoxMesh { Size = new Vector3(70f * U, 0.10f * U, 250f * U) },
            sandFill, new Vector3(-145f * U, skinY - 0.01f * U, 0));
        Spawn("SurfaceFillEast", new BoxMesh { Size = new Vector3(70f * U, 0.10f * U, 250f * U) },
            sandFill, new Vector3(145f * U, skinY - 0.01f * U, 0));
        Spawn("SurfaceFillNorth", new BoxMesh { Size = new Vector3(250f * U, 0.10f * U, 58f * U) },
            sandFill, new Vector3(0, skinY - 0.01f * U, -128f * U));
        Spawn("SurfaceFillSouth", new BoxMesh { Size = new Vector3(250f * U, 0.10f * U, 58f * U) },
            sandFill, new Vector3(0, skinY - 0.01f * U, 128f * U));

        SpawnRot("SurfaceMainRoad", new BoxMesh { Size = new Vector3(18f * U, 0.08f * U, 330f * U) },
            asphalt, new Vector3(-84f * U, skinY + 0.04f * U, 20f * U), new Vector3(0, -7f, 0));
        SpawnRot("SurfaceTankRoad", new BoxMesh { Size = new Vector3(15f * U, 0.08f * U, 145f * U) },
            asphalt, new Vector3(54f * U, skinY + 0.045f * U, 42f * U), new Vector3(0, 88f, 0));

        for (int i = 1; i < 6; i++)
        {
            float off = (-90f + i * 30f) * U;
            Spawn($"SurfaceJointNorth{i}", new BoxMesh { Size = new Vector3(190f * U, 0.04f * U, 0.55f * U) },
                concDark, new Vector3(0, skinY + 0.07f * U, off - 61f * U));
            Spawn($"SurfaceJointSouth{i}", new BoxMesh { Size = new Vector3(190f * U, 0.04f * U, 0.55f * U) },
                concDark, new Vector3(0, skinY + 0.07f * U, off + 61f * U));
        }
        for (int i = 1; i < 5; i++)
        {
            float off = (-76f + i * 30f) * U;
            Spawn($"SurfaceJointWest{i}", new BoxMesh { Size = new Vector3(0.55f * U, 0.04f * U, 66f * U) },
                concDark, new Vector3(off - 64f * U, skinY + 0.07f * U, 0));
            Spawn($"SurfaceJointEast{i}", new BoxMesh { Size = new Vector3(0.55f * U, 0.04f * U, 66f * U) },
                concDark, new Vector3(off + 64f * U, skinY + 0.07f * U, 0));
        }

        // Low berms around the filled site, visible at pad/liftoff camera height.
        Spawn("NorthBerm", new BoxMesh { Size = new Vector3(340f * U, 2.2f * U, 5f * U) },
            sandFill, new Vector3(-8f * U, padY + 0.75f * U, -145f * U));
        Spawn("SouthBerm", new BoxMesh { Size = new Vector3(340f * U, 2.2f * U, 5f * U) },
            sandFill, new Vector3(-8f * U, padY + 0.75f * U, 145f * U));
        Spawn("WestBerm", new BoxMesh { Size = new Vector3(5f * U, 2.0f * U, 280f * U) },
            sandFill, new Vector3(-185f * U, padY + 0.65f * U, 0));

        // Service roads and paved access lanes. Slight rotations avoid a toy-like
        // grid while still reading clearly from the launch camera.
        SpawnRot("MainAccessRoad", new BoxMesh { Size = new Vector3(18f * U, 0.18f * U, 360f * U) },
            asphalt, new Vector3(-84f * U, surfaceY, 18f * U), new Vector3(0, -7f, 0));
        SpawnRot("TankFarmServiceRoad", new BoxMesh { Size = new Vector3(16f * U, 0.18f * U, 150f * U) },
            asphalt, new Vector3(52f * U, surfaceY + 0.01f * U, 38f * U), new Vector3(0, 88f, 0));
        SpawnRot("TowerServiceRoad", new BoxMesh { Size = new Vector3(13f * U, 0.18f * U, 120f * U) },
            asphalt, new Vector3(-47f * U, surfaceY + 0.01f * U, -35f * U), new Vector3(0, -32f, 0));

        // Painted centre and hold-short marks. Thin boxes keep the cost low and
        // add scale cues when the vehicle lifts off through smoke.
        for (int i = 0; i < 13; i++)
        {
            float z = (-145f + i * 24f) * U;
            SpawnRot($"AccessCenterLine{i}", new BoxMesh { Size = new Vector3(0.7f * U, 0.06f * U, 9f * U) },
                paint, new Vector3(-84f * U, surfaceY + 0.05f * U, z + 18f * U), new Vector3(0, -7f, 0));
        }
        for (int i = 0; i < 6; i++)
        {
            SpawnRot($"HoldShortStripe{i}", new BoxMesh { Size = new Vector3(2.0f * U, 0.07f * U, 18f * U) },
                paint, new Vector3((-92f + i * 3.2f) * U, surfaceY + 0.06f * U, -58f * U),
                new Vector3(0, 83f, 0));
        }

        // Subtle patched slabs around the main apron so the concrete reads as
        // poured and repaired instead of a single procedural plate.
        var patches = new (float x, float z, float sx, float sz, float yaw)[]
        {
            (-52f, -62f, 26f, 14f,  2f), (58f, -48f, 22f, 18f, -4f),
            (-64f,  40f, 18f, 22f, -3f), (42f,  68f, 28f, 12f,  5f),
            (  8f, -78f, 32f, 10f,  0f),
        };
        foreach (var (x, z, sx, sz, yaw) in patches)
        {
            SpawnRot("ConcreteRepairPatch", new BoxMesh { Size = new Vector3(sx * U, 0.08f * U, sz * U) },
                concDark, new Vector3(x * U, surfaceY + 0.02f * U, z * U), new Vector3(0, yaw, 0));
        }

        // Water-deluge outlets around the OLM deck: small dark nozzles plus a
        // feed ring on the pad surface. They show why smoke/steam blooms from
        // around the mount during ignition.
        Spawn("DelugePadRing", new TorusMesh
            { InnerRadius = 13f * U, OuterRadius = 14.2f * U, RingSegments = 32, Rings = 6 },
            concDark, new Vector3(0, surfaceY + 0.12f * U, 0));
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.Tau / 16f;
            SpawnRot($"DelugeOutlet{i}", new BoxMesh { Size = new Vector3(1.4f * U, 0.6f * U, 2.4f * U) },
                concDark,
                new Vector3(13.8f * U * Mathf.Cos(a), surfaceY + 0.35f * U, 13.8f * U * Mathf.Sin(a)),
                new Vector3(0, -Mathf.RadToDeg(a), 0));
        }
    }

    // ── Wide weathered launch apron / tarmac with panels + scorch zones ───
    private void BuildLaunchApron(StandardMaterial3D tarmac, StandardMaterial3D concDark,
                                  StandardMaterial3D scorch, StandardMaterial3D burnt)
    {
        // The apron sits flush with the pad surface so the complex reads as
        // standing on a real spaceport surface, not bare ground.
        const float padY   = -22f * U + 6.5f * U;
        const float apron  = 180f * U;        // ~180 m square tarmac
        const float topY   = padY + 0.05f * U;

        // Main weathered concrete slab.
        Spawn("ApronSlab", new BoxMesh { Size = new Vector3(apron, 1f * U, apron) },
            tarmac, new Vector3(0, topY - 0.5f * U, 0));

        // Expansion-joint / panel lines: thin darker strips on a grid so the
        // surface reads as cast concrete panels rather than one flat colour.
        const int   panels   = 9;             // grid divisions
        const float jointW   = 0.5f * U;      // thin line width
        const float surfaceY = topY + 0.02f * U;
        float step = apron / panels;
        float half = apron * 0.5f;
        for (int i = 1; i < panels; i++)
        {
            float off = -half + i * step;
            // Line running along Z (constant X).
            Spawn($"JointX{i}", new BoxMesh { Size = new Vector3(jointW, 0.1f * U, apron) },
                concDark, new Vector3(off, surfaceY, 0));
            // Line running along X (constant Z).
            Spawn($"JointZ{i}", new BoxMesh { Size = new Vector3(apron, 0.1f * U, jointW) },
                concDark, new Vector3(0, surfaceY, off));
        }

        // Radiating blast/scorch stains fanning out from the mount centre — a
        // few thin charred wedges (long boxes) at angles, darkening toward the
        // exhaust vent direction (+Z).
        var streaks = new (float ang, float len, StandardMaterial3D mat)[]
        {
            ( 90f, 78f * U, burnt),   // straight down the flame trench
            ( 60f, 52f * U, scorch),
            (120f, 52f * U, scorch),
            ( 35f, 40f * U, scorch),
            (145f, 40f * U, scorch),
            (  0f, 34f * U, scorch),
            (180f, 34f * U, scorch),
            (270f, 30f * U, scorch),  // back side, lighter reach
        };
        foreach (var (angDeg, len, mat) in streaks)
        {
            float a   = Mathf.DegToRad(angDeg);
            float mid = 18f * U + len * 0.5f;   // start outside the scorch apron
            var streak = new MeshInstance3D
            {
                Name            = $"Scorch{(int)angDeg}",
                Mesh            = new BoxMesh { Size = new Vector3(len, 0.12f * U, 9f * U) },
                Position        = new Vector3(mid * Mathf.Cos(a), surfaceY + 0.01f * U,
                                              mid * Mathf.Sin(a)),
                RotationDegrees = new Vector3(0, -angDeg, 0),
            };
            streak.SetSurfaceOverrideMaterial(0, mat);
            AddChild(streak);
        }
    }

    // ── Propellant lines, GSE blockhouse and water-deluge tower ───────────
    private void BuildGroundSupport(StandardMaterial3D insul, StandardMaterial3D steel,
                                    StandardMaterial3D darkSteel, StandardMaterial3D concrete,
                                    StandardMaterial3D concDark)
    {
        const float padY = GradeY;

        // ── Propellant pipe runs from the tank farm toward the OLM ─────────
        // The tank farm sits near (+45,+45). Run a couple of insulated lines on
        // low pipe-rack supports angling in toward the mount QD side.
        var pipeStart = new Vector3(40f * U, padY, 40f * U);
        var pipeEnd   = new Vector3(13f * U, padY, 13f * U);
        Vector3 d     = pipeEnd - pipeStart;
        float   run   = new Vector2(d.X, d.Z).Length();
        float   yaw   = Mathf.RadToDeg(Mathf.Atan2(d.Z, d.X));
        Vector3 pmid  = (pipeStart + pipeEnd) * 0.5f;

        foreach (var (lane, tag) in new[] { (-1.4f * U, "LOX"), (1.4f * U, "CH4") })
        {
            // Perpendicular offset so the two lines run parallel.
            float ox =  Mathf.Sin(Mathf.Atan2(d.Z, d.X)) * lane;
            float oz = -Mathf.Cos(Mathf.Atan2(d.Z, d.X)) * lane;
            var pipe = new MeshInstance3D
            {
                Name            = $"Pipe{tag}",
                Mesh            = new CylinderMesh
                    { TopRadius = 0.9f * U, BottomRadius = 0.9f * U, Height = run, RadialSegments = 10 },
                Position        = new Vector3(pmid.X + ox, padY + 1.6f * U, pmid.Z + oz),
                // Cylinder default axis is +Y; lay it down along the run direction.
                RotationDegrees = new Vector3(0, -yaw, 90f),
            };
            pipe.SetSurfaceOverrideMaterial(0, insul);
            AddChild(pipe);
        }

        // Low pipe-rack support posts under the lines.
        int racks = 5;
        for (int i = 1; i < racks; i++)
        {
            float t = i / (float)racks;
            Vector3 p = pipeStart.Lerp(pipeEnd, t);
            Spawn($"PipeRack{i}", new BoxMesh { Size = new Vector3(4.5f * U, 1.6f * U, 0.6f * U) },
                steel, new Vector3(p.X, padY + 0.8f * U, p.Z));
        }

        // ── Water-deluge tank: tall white cylinder on a steel stand, feeding ─
        // the pad sound-suppression system. Placed off the +X side, clear of
        // the tower and trench.
        var delugePos = new Vector3(52f * U, padY, -22f * U);
        const float delH = 30f * U, delR = 7f * U;
        // Support stand legs.
        for (int i = 0; i < 4; i++)
        {
            float a  = i * Mathf.Tau / 4f + Mathf.Pi / 4f;
            float lx = delugePos.X + Mathf.Cos(a) * delR * 0.8f;
            float lz = delugePos.Z + Mathf.Sin(a) * delR * 0.8f;
            Spawn($"DelugeLeg{i}", new BoxMesh { Size = new Vector3(0.9f * U, 14f * U, 0.9f * U) },
                steel, new Vector3(lx, padY + 7f * U, lz));
        }
        Spawn("DelugeTank", new CylinderMesh
            { TopRadius = delR, BottomRadius = delR, Height = delH, RadialSegments = 20 },
            insul, new Vector3(delugePos.X, padY + 14f * U + delH * 0.5f, delugePos.Z));
        var delDome = new MeshInstance3D
        {
            Name     = "DelugeDome",
            Mesh     = new SphereMesh
                { Radius = delR, Height = delR, IsHemisphere = true, RadialSegments = 20, Rings = 7 },
            Position = new Vector3(delugePos.X, padY + 14f * U + delH, delugePos.Z),
        };
        delDome.SetSurfaceOverrideMaterial(0, insul);
        AddChild(delDome);
        // Deluge downcomer pipe running from the tank toward the pad.
        Spawn("DelugePipe", new CylinderMesh
            { TopRadius = 0.8f * U, BottomRadius = 0.8f * U, Height = 28f * U, RadialSegments = 8 },
            steel, new Vector3(delugePos.X - 14f * U, padY + 1.2f * U, delugePos.Z));

        // ── GSE blockhouse: low reinforced control building, set well back ──
        var blockPos = new Vector3(-55f * U, padY, 50f * U);
        Spawn("Blockhouse", new BoxMesh { Size = new Vector3(20f * U, 7f * U, 14f * U) },
            concrete, new Vector3(blockPos.X, padY + 3.5f * U, blockPos.Z));
        // Bermed roof slab + lower-profile annex.
        Spawn("BlockhouseRoof", new BoxMesh { Size = new Vector3(22f * U, 1.2f * U, 16f * U) },
            concDark, new Vector3(blockPos.X, padY + 7.6f * U, blockPos.Z));
        Spawn("BlockhouseAnnex", new BoxMesh { Size = new Vector3(9f * U, 4f * U, 8f * U) },
            concrete, new Vector3(blockPos.X + 13f * U, padY + 2f * U, blockPos.Z - 3f * U));

        // ── A couple of horizontal GSE / nitrogen bullet tanks near the farm ─
        foreach (var (zoff, tag) in new[] { (0f, "A"), (8f * U, "B") })
        {
            var bullet = new MeshInstance3D
            {
                Name            = $"GseBullet{tag}",
                Mesh            = new CylinderMesh
                    { TopRadius = 2.2f * U, BottomRadius = 2.2f * U, Height = 16f * U, RadialSegments = 14 },
                Position        = new Vector3(66f * U, padY + 2.5f * U, 30f * U + zoff),
                RotationDegrees = new Vector3(0, 0, 90f),  // lay horizontal along X
            };
            bullet.SetSurfaceOverrideMaterial(0, insul);
            AddChild(bullet);
            // Saddle supports.
            Spawn($"GseSaddle{tag}1", new BoxMesh { Size = new Vector3(1f * U, 2.5f * U, 3f * U) },
                steel, new Vector3(60f * U, padY + 1.2f * U, 30f * U + zoff));
            Spawn($"GseSaddle{tag}2", new BoxMesh { Size = new Vector3(1f * U, 2.5f * U, 3f * U) },
                steel, new Vector3(72f * U, padY + 1.2f * U, 30f * U + zoff));
        }

        // ── Cable / utility tray running from the blockhouse toward the tower ──
        // Elevated tray on short posts (reads as the buried-run riser bringing
        // power and data from the control building to the OLM/OLIT).
        var trayStart = new Vector3(-45f * U, padY, 45f * U);
        var trayEnd   = new Vector3(-30f * U, padY, 6f * U);
        Vector3 td    = trayEnd - trayStart;
        float trayRun = new Vector2(td.X, td.Z).Length();
        float trayYaw = Mathf.RadToDeg(Mathf.Atan2(td.Z, td.X));
        Vector3 tmid  = (trayStart + trayEnd) * 0.5f;
        var tray = new MeshInstance3D
        {
            Name            = "CableTray",
            Mesh            = new BoxMesh { Size = new Vector3(trayRun, 0.5f * U, 1.4f * U) },
            Position        = new Vector3(tmid.X, padY + 1.4f * U, tmid.Z),
            RotationDegrees = new Vector3(0, -trayYaw, 0),
        };
        tray.SetSurfaceOverrideMaterial(0, darkSteel);
        AddChild(tray);
        int trayPosts = 5;
        for (int i = 0; i <= trayPosts; i++)
        {
            Vector3 p = trayStart.Lerp(trayEnd, i / (float)trayPosts);
            Spawn($"TrayPost{i}", new BoxMesh { Size = new Vector3(0.5f * U, 1.4f * U, 0.5f * U) },
                steel, new Vector3(p.X, padY + 0.7f * U, p.Z));
        }

        // A couple of short vertical GSE bottle tanks near the farm (N2/He).
        foreach (var (xoff, tag) in new[] { (0f, "X"), (7f * U, "Y") })
        {
            Spawn($"GseBottle{tag}", new CylinderMesh
                { TopRadius = 2.0f * U, BottomRadius = 2.0f * U, Height = 11f * U, RadialSegments = 16 },
                insul, new Vector3(58f * U + xoff, padY + 5.5f * U, 12f * U));
        }
    }

    // ── Orbital Launch Mount (OLM): ring table on splayed legs ────────────
    private void BuildOrbitalLaunchMount(StandardMaterial3D steel, StandardMaterial3D conc)
    {
        // The booster base sits at y=0; engines fire down through the centre.
        // OLM table top should land just under the booster base. Table is ~20 m
        // (≈7 units) outer diameter with a centre hole the engines clear.
        float tableTopY         = VehicleInterfaceY - 0.6f * U;
        const float tableThick  = 4f * U;                    // table deck thickness
        const float outerR      = 10.5f * U;                 // ~21 m outer diameter
        const float innerR      = VesselBodyR + 0.06f;       // ~9.2 m Ø (was legacy 7.2 m)
        float tableMidY = tableTopY - tableThick * 0.5f;

        // Steel ring deck — built as an annulus from a tube of trapezoid segments
        // so the booster engines fire through the open centre.
        const int segs = 24;
        float ringMidR = (outerR + innerR) * 0.5f;
        float ringW    = outerR - innerR;
        for (int i = 0; i < segs; i++)
        {
            float a = i * Mathf.Tau / segs;
            var seg = new MeshInstance3D
            {
                Name            = $"OLMRing{i}",
                Mesh            = new BoxMesh
                    { Size = new Vector3(ringMidR * Mathf.Tau / segs * 1.05f, tableThick, ringW) },
                Position        = new Vector3(ringMidR * Mathf.Cos(a), tableMidY, ringMidR * Mathf.Sin(a)),
                RotationDegrees = new Vector3(0, -Mathf.RadToDeg(a), 0),
            };
            seg.SetSurfaceOverrideMaterial(0, steel);
            AddChild(seg);
        }

        // Segmented outer and inner skirt panels preserve the open engine throat. Godot's
        // CylinderMesh is capped/solid and previously filled the hole it was meant to frame.
        for (int i = 0; i < segs; i++)
        {
            float a = (i + 0.5f) * Mathf.Tau / segs;
            float panelW = outerR * Mathf.Tau / segs * 1.03f;
            SpawnRot($"OLMOuterSkirt{i}", new BoxMesh
                { Size = new Vector3(panelW, 7f * U, 0.45f * U) }, steel,
                new Vector3(outerR * Mathf.Cos(a), tableTopY - tableThick - 3.5f * U,
                    outerR * Mathf.Sin(a)), new Vector3(0, -Mathf.RadToDeg(a), 0));
            SpawnRot($"OLMInnerSkirt{i}", new BoxMesh
                { Size = new Vector3(innerR * Mathf.Tau / segs * 1.03f, 6f * U, 0.35f * U) }, steel,
                new Vector3(innerR * Mathf.Cos(a), tableTopY - tableThick - 3f * U,
                    innerR * Mathf.Sin(a)), new Vector3(0, -Mathf.RadToDeg(a), 0));
        }

        // ── Hold-down clamps + QD plate hardware around the centre hole ───────
        // Twenty small clamp blocks ringing the booster interface, plus a few
        // taller stanchions reading as the booster QD / shielding pylons.
        const int clamps = 20;
        for (int i = 0; i < clamps; i++)
        {
            float a  = i * Mathf.Tau / clamps;
            float cr = innerR + 0.32f;
            Spawn($"HoldDownClamp{i}", new BoxMesh
                { Size = new Vector3(0.9f * U, 1.6f * U, 0.7f * U) },
                conc, new Vector3(cr * Mathf.Cos(a), tableTopY + 0.8f * U, cr * Mathf.Sin(a)));
        }

        // Booster Quick-Disconnect (BQD) shield mast rising from the deck on the
        // tower side — the tall housing the booster plugs into at lift-off.
        Spawn("BQDMast", new BoxMesh { Size = new Vector3(2.4f * U, 16f * U, 3.0f * U) },
            steel, new Vector3(-(innerR + 1.6f * U), tableTopY + 8f * U, 0));
        Spawn("BQDHead", new BoxMesh { Size = new Vector3(3.2f * U, 2.6f * U, 3.8f * U) },
            conc, new Vector3(-(innerR + 1.6f * U), tableTopY + 15f * U, 0));

        // ── Water-cooled steel flame deflector plate beneath the centre hole ──
        // A heavy plate slung under the table that the booster exhaust strikes,
        // sitting above the concrete trench. Reads as the steel "shower head".
        Spawn("DeflectorPlate", new CylinderMesh
            { TopRadius = innerR + 2.5f * U, BottomRadius = innerR + 2.5f * U, Height = 1.2f * U, RadialSegments = 24 },
            steel, new Vector3(0, GradeY + 0.8f * U, 0));

        // ── Splayed support legs (~6, ~20 m tall) ─────────────────────────
        // Legs run from under the table skirt down to the pad footing, splayed
        // outward at the base. Footing is at the scorch-apron level.
        const int legCount = 6;
        float legTopY = tableTopY - tableThick - 1f * U;
        const float legBotY = GradeY;                         // shared grade datum
        float legLen        = legTopY - legBotY;             // ~20 m
        const float legTopR = outerR - 1.5f * U;             // attach under skirt
        const float legBotR = outerR + 2.5f * U;             // splayed out at base

        for (int i = 0; i < legCount; i++)
        {
            float a    = i * Mathf.Tau / legCount + Mathf.Pi / legCount;
            float cos  = Mathf.Cos(a);
            float sin  = Mathf.Sin(a);
            float topX = legTopR * cos, topZ = legTopR * sin;
            float botX = legBotR * cos, botZ = legBotR * sin;
            float midX = (topX + botX) * 0.5f;
            float midZ = (topZ + botZ) * 0.5f;
            float midY = (legTopY + legBotY) * 0.5f;

            // Lean angle of the splayed leg.
            float dr   = legBotR - legTopR;
            float lean = Mathf.Atan2(dr, legLen);            // tilt outward

            var leg = new MeshInstance3D
            {
                Name            = $"OLMLeg{i}",
                Mesh            = new BoxMesh
                    { Size = new Vector3(1.8f * U, legLen + 1.5f * U, 1.8f * U) },
                Position        = new Vector3(midX, midY, midZ),
                RotationDegrees = new Vector3(
                    Mathf.RadToDeg(lean) * sin,
                    -Mathf.RadToDeg(a),
                    -Mathf.RadToDeg(lean) * cos),
            };
            leg.SetSurfaceOverrideMaterial(0, steel);
            AddChild(leg);

            // Concrete footing pad at the base of each leg.
            Spawn($"LegFoot{i}", new BoxMesh { Size = new Vector3(4f * U, 1.5f * U, 4f * U) },
                conc, new Vector3(botX, legBotY + 0.5f * U, botZ));
        }

        // Cross-bracing ring tying the legs together mid-height.
        for (int i = 0; i < legCount; i++)
        {
            float a0 = i * Mathf.Tau / legCount + Mathf.Pi / legCount;
            float a1 = (i + 1) * Mathf.Tau / legCount + Mathf.Pi / legCount;
            float braceR = (legTopR + legBotR) * 0.5f;
            float x0 = braceR * Mathf.Cos(a0), z0 = braceR * Mathf.Sin(a0);
            float x1 = braceR * Mathf.Cos(a1), z1 = braceR * Mathf.Sin(a1);
            float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f;
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            float ang = Mathf.Atan2(z1 - z0, x1 - x0);
            var brace = new MeshInstance3D
            {
                Name            = $"OLMBrace{i}",
                Mesh            = new BoxMesh { Size = new Vector3(len, 0.8f * U, 0.8f * U) },
                Position        = new Vector3(mx, (legTopY + legBotY) * 0.5f, mz),
                RotationDegrees = new Vector3(0, -Mathf.RadToDeg(ang), 0),
            };
            brace.SetSurfaceOverrideMaterial(0, steel);
            AddChild(brace);
        }

        // A second, lower bracing ring near the footing so the legs read as a
        // proper braced frame, plus diagonal kickers between the two rings.
        for (int i = 0; i < legCount; i++)
        {
            float a0 = i * Mathf.Tau / legCount + Mathf.Pi / legCount;
            float a1 = (i + 1) * Mathf.Tau / legCount + Mathf.Pi / legCount;
            float braceR = legBotR - 0.5f * U;
            float bY = legBotY + 4f * U;
            float x0 = braceR * Mathf.Cos(a0), z0 = braceR * Mathf.Sin(a0);
            float x1 = braceR * Mathf.Cos(a1), z1 = braceR * Mathf.Sin(a1);
            float mx = (x0 + x1) * 0.5f, mz = (z0 + z1) * 0.5f;
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
            float ang = Mathf.Atan2(z1 - z0, x1 - x0);
            var brace = new MeshInstance3D
            {
                Name            = $"OLMBraceLo{i}",
                Mesh            = new BoxMesh { Size = new Vector3(len, 0.7f * U, 0.7f * U) },
                Position        = new Vector3(mx, bY, mz),
                RotationDegrees = new Vector3(0, -Mathf.RadToDeg(ang), 0),
            };
            brace.SetSurfaceOverrideMaterial(0, steel);
            AddChild(brace);
        }

        // Insulated propellant feed manifold ring slung under the table deck.
        Spawn("OLMFeedRing", new TorusMesh
            { InnerRadius = innerR + 1.0f * U, OuterRadius = innerR + 2.0f * U, RingSegments = 24, Rings = 8 },
            steel, new Vector3(0, tableTopY - tableThick - 1.5f * U, 0));
    }

    // ── Mechazilla: tall square lattice integration tower + chopstick arms ─
    private void BuildMechazillaTower(StandardMaterial3D steel, StandardMaterial3D darkSteel)
    {
        // ~145 m tall (~52 units), square cross-section ~12 m, set ~35 m to one
        // side of the OLM so the booster clears it.
        float towerH          = (float)Spec.OlitHeight * U;
        const float towerW     = 12f * U;
        const float baseY      = GradeY;
        float towerX           = (float)Spec.OlitEast * U;
        const float towerZ     = 0f;
        const float halfW      = towerW * 0.5f;

        Vector3 center = new(towerX, baseY + towerH * 0.5f, towerZ);

        // Four vertical corner columns.
        float[] cx = { -halfW,  halfW,  halfW, -halfW };
        float[] cz = { -halfW, -halfW,  halfW,  halfW };
        for (int c = 0; c < 4; c++)
        {
            Spawn($"TowerCol{c}", new BoxMesh { Size = new Vector3(1.2f * U, towerH, 1.2f * U) },
                steel, new Vector3(towerX + cx[c], center.Y, towerZ + cz[c]));
        }

        // Horizontal + diagonal lattice bracing every few metres up each face.
        const int levels = 22;
        float dy = towerH / levels;
        for (int l = 0; l <= levels; l++)
        {
            float y = baseY + l * dy;
            // Horizontal ring beams across the 4 faces.
            AddBeam($"TLatXn{l}", new Vector3(towerX, y, towerZ - halfW), towerW, 0, true);
            AddBeam($"TLatXp{l}", new Vector3(towerX, y, towerZ + halfW), towerW, 0, true);
            AddBeam($"TLatZn{l}", new Vector3(towerX - halfW, y, towerZ), towerW, 0, false);
            AddBeam($"TLatZp{l}", new Vector3(towerX + halfW, y, towerZ), towerW, 0, false);

            // Diagonal cross braces on the two side faces (cheap X look).
            if (l < levels)
            {
                float diagLen = Mathf.Sqrt(towerW * towerW + dy * dy);
                float diagAng = Mathf.RadToDeg(Mathf.Atan2(dy, towerW));
                AddDiag($"TDiagN{l}", new Vector3(towerX, y + dy * 0.5f, towerZ - halfW),
                    diagLen, diagAng, true);
                AddDiag($"TDiagS{l}", new Vector3(towerX, y + dy * 0.5f, towerZ + halfW),
                    diagLen, diagAng, true);
            }
        }

        // ── Solid utility / elevator spine running up the back (-X) face ──────
        // The real OLIT has a clad service core; here a slim solid box column on
        // the back face reads as the elevator + cable spine against the lattice.
        Spawn("TowerSpine", new BoxMesh
            { Size = new Vector3(2.0f * U, towerH * 0.96f, towerW * 0.7f) },
            darkSteel, new Vector3(towerX - halfW - 0.6f * U, baseY + towerH * 0.5f, towerZ));

        // Clad service-section panels at a few levels on the front face so the
        // tower isn't pure open lattice (equipment rooms / cable trays).
        for (int p = 0; p < 4; p++)
        {
            float py = baseY + towerH * (0.18f + p * 0.22f);
            Spawn($"TowerPanel{p}", new BoxMesh
                { Size = new Vector3(0.4f * U, towerH * 0.16f, towerW * 0.8f) },
                darkSteel, new Vector3(towerX + halfW + 0.3f * U, py, towerZ));
        }

        // ── Perimeter walkways / railings at several work levels ──────────────
        for (int w = 1; w <= 5; w++)
        {
            float wy = baseY + towerH * (w / 6f);
            float ext = halfW + 1.2f * U;
            // Four railing rails ringing the tower (thin beams).
            AddBeam($"WalkXn{w}", new Vector3(towerX, wy, towerZ - ext), towerW + 2.4f * U, 0, true);
            AddBeam($"WalkXp{w}", new Vector3(towerX, wy, towerZ + ext), towerW + 2.4f * U, 0, true);
            AddBeam($"WalkZn{w}", new Vector3(towerX - ext, wy, towerZ), towerW + 2.4f * U, 0, false);
            AddBeam($"WalkZp{w}", new Vector3(towerX + ext, wy, towerZ), towerW + 2.4f * U, 0, false);
            // Thin grating deck.
            Spawn($"WalkDeck{w}", new BoxMesh
                { Size = new Vector3(towerW + 2.2f * U, 0.15f * U, towerW + 2.2f * U) },
                darkSteel, new Vector3(towerX, wy - 0.6f * U, towerZ));
        }

        // Structural cap remains inside the FAA 480 ft tower envelope.
        Spawn("TowerCap", new BoxMesh { Size = new Vector3(towerW + 1.5f * U, 5f * U, towerW + 1.5f * U) },
            darkSteel, new Vector3(towerX, baseY + towerH - 2.5f * U, towerZ));
        float rodHeight = (float)Spec.LightningRodHeight * U;
        Spawn("OlitLightningRod", new CylinderMesh
            { TopRadius = 0.08f * U, BottomRadius = 0.28f * U,
                Height = rodHeight, RadialSegments = 8 }, steel,
            new Vector3(towerX, baseY + towerH + rodHeight * 0.5f, towerZ));

        // ── Chopstick catch arms (two horizontal arms partway up) ─────────
        // Real arms sit ~40–70 m up; here ~55 m so they're above the OLM table
        // and read as the catch hardware. They point toward the OLM (+X side).
        BuildChopstickArms(towerX, towerZ, halfW, baseY, darkSteel, steel);

        BuildTowerServiceDetails(towerX, towerZ, halfW, baseY, towerH, steel, darkSteel);

    }

    private void BuildChopstickArms(float towerX, float towerZ, float halfW, float baseY,
                                    StandardMaterial3D armMat, StandardMaterial3D pivotMat)
    {
        const float armY    = 55f * U;                       // height up the tower
        const float armLen  = 30f * U;                       // 30 m reconstruction estimate
        const float armGap  = 6.25f * U;                     // centres: 10 m clear opening
        float armBaseX      = towerX + halfW;                // arms exit the +X face toward OLM
        float armMidX       = armBaseX + armLen * 0.5f;

        // Carriage that rides up/down the tower (the arms' mount).
        Spawn("ArmCarriage", new BoxMesh
            { Size = new Vector3(3f * U, 10f * U, halfW * 2f + 3f * U) },
            armMat, new Vector3(armBaseX, baseY + armY, towerZ));

        // Two parallel chopstick arms.
        foreach (var (zoff, tag) in new[] { (-armGap, "L"), (armGap, "R") })
        {
            Spawn($"Chopstick{tag}", new BoxMesh
                { Size = new Vector3(armLen, 2.5f * U, 2.5f * U) },
                armMat, new Vector3(armMidX, baseY + armY, towerZ + zoff));

            // Cradle pad on the inner top edge of each arm.
            Spawn($"ChopstickPad{tag}", new BoxMesh
                { Size = new Vector3(armLen * 0.7f, 0.6f * U, 1.2f * U) },
                pivotMat, new Vector3(armMidX, baseY + armY + 1.5f * U,
                    towerZ + zoff + (zoff < 0 ? 1.2f * U : -1.2f * U)));

            // Dark elastomer/rub rail and nose roller at the catch end. These
            // small high-contrast shapes make the arms read as catch hardware,
            // not just two rectangular beams.
            Spawn($"ChopstickRubRail{tag}", new BoxMesh
                { Size = new Vector3(armLen * 0.58f, 0.35f * U, 0.45f * U) },
                armMat, new Vector3(armMidX + armLen * 0.08f, baseY + armY + 1.75f * U,
                    towerZ + zoff + (zoff < 0 ? 1.85f * U : -1.85f * U)));
            var roller = new MeshInstance3D
            {
                Name            = $"ChopstickNoseRoller{tag}",
                Mesh            = new CylinderMesh
                    { TopRadius = 0.75f * U, BottomRadius = 0.75f * U, Height = 2.0f * U, RadialSegments = 10 },
                Position        = new Vector3(armBaseX + armLen - 1.4f * U, baseY + armY + 1.2f * U, towerZ + zoff),
                RotationDegrees = new Vector3(90f, 0, 0),
            };
            roller.SetSurfaceOverrideMaterial(0, pivotMat);
            AddChild(roller);
        }

        // Tilt linkage struts from the carriage out to the arm tips.
        foreach (var zoff in new[] { -armGap, armGap })
        {
            var strut = new MeshInstance3D
            {
                Name            = $"ArmStrut{(zoff < 0 ? "L" : "R")}",
                Mesh            = new BoxMesh { Size = new Vector3(armLen * 0.9f, 1.2f * U, 1.2f * U) },
                Position        = new Vector3(armMidX, baseY + armY + 5f * U, towerZ + zoff),
                RotationDegrees = new Vector3(0, 0, -10f),
            };
            strut.SetSurfaceOverrideMaterial(0, armMat);
            AddChild(strut);
        }

        var cableMat = Mat(new Color(0.06f, 0.06f, 0.07f), 0.9f, 0.35f);
        foreach (var zoff in new[] { -armGap, armGap })
        {
            Vector3 towerSheave = new(towerX + halfW, baseY + 94f * U, towerZ + zoff);
            Vector3 armTip      = new(armBaseX + armLen - 2f * U, baseY + armY + 4.5f * U, towerZ + zoff);
            AddCable($"CatchLiftCable{(zoff < 0 ? "L" : "R")}", towerSheave, armTip, cableMat);
        }
    }

    private void BuildTowerServiceDetails(float towerX, float towerZ, float halfW, float baseY, float towerH,
                                          StandardMaterial3D steel, StandardMaterial3D darkSteel)
    {
        var blackRubber = Mat(new Color(0.025f, 0.025f, 0.030f), 0.75f, 0.25f);
        var caution     = Mat(new Color(0.78f, 0.62f, 0.16f), 0.82f, 0.0f);
        var cableMat    = Mat(new Color(0.055f, 0.055f, 0.065f), 0.86f, 0.25f);

        // Vertical rails where the catch-arm carriage rides, plus small sheave
        // boxes near the top. These are very visible in side pad shots.
        float railY = baseY + towerH * 0.48f;
        float railH = towerH * 0.78f;
        for (int i = 0; i < 2; i++)
        {
            float z = towerZ + (i == 0 ? -4.0f * U : 4.0f * U);
            Spawn($"CarriageRail{i}", new BoxMesh { Size = new Vector3(0.55f * U, railH, 0.45f * U) },
                blackRubber, new Vector3(towerX + halfW + 0.75f * U, railY, z));
            Spawn($"CarriageSheave{i}", new BoxMesh { Size = new Vector3(1.6f * U, 1.4f * U, 1.6f * U) },
                steel, new Vector3(towerX + halfW + 0.9f * U, baseY + towerH * 0.86f, z));
        }

        // Ship quick-disconnect arm at upper-stage height. It reaches toward the
        // leeward side of the Ship but stops short of the hull so it does not
        // visually intersect the rocket.
        float sqdY = baseY + 108f * U;
        float armBaseX = towerX + halfW + 0.8f * U;
        float armLen = 28f * U;
        Spawn("ShipQDArmTruss", new BoxMesh { Size = new Vector3(armLen, 1.7f * U, 1.5f * U) },
            darkSteel, new Vector3(armBaseX + armLen * 0.5f, sqdY, towerZ + 3.4f * U));
        Spawn("ShipQDArmPipe", new CylinderMesh
            { TopRadius = 0.42f * U, BottomRadius = 0.42f * U, Height = armLen * 0.9f, RadialSegments = 8 },
            steel, new Vector3(armBaseX + armLen * 0.5f, sqdY + 1.0f * U, towerZ + 2.2f * U))
            .RotationDegrees = new Vector3(0, 90f, 90f);
        Spawn("ShipQDHead", new BoxMesh { Size = new Vector3(2.6f * U, 4.0f * U, 3.8f * U) },
            darkSteel, new Vector3(armBaseX + armLen - 1.2f * U, sqdY - 0.6f * U, towerZ + 3.4f * U));
        Spawn("ShipQDSoftSeal", new BoxMesh { Size = new Vector3(0.55f * U, 3.0f * U, 2.8f * U) },
            blackRubber, new Vector3(armBaseX + armLen + 0.3f * U, sqdY - 0.6f * U, towerZ + 3.4f * U));

        // Cable tray and caution bands around the working platforms.
        Spawn("UpperCableTray", new BoxMesh { Size = new Vector3(4.0f * U, towerH * 0.52f, 0.55f * U) },
            blackRubber, new Vector3(towerX - halfW - 1.9f * U, baseY + towerH * 0.55f, towerZ + halfW + 0.8f * U));
        for (int b = 0; b < 5; b++)
        {
            float y = baseY + towerH * (0.20f + b * 0.13f);
            Spawn($"TowerCautionBand{b}", new BoxMesh { Size = new Vector3(0.25f * U, 0.35f * U, halfW * 1.8f) },
                caution, new Vector3(towerX + halfW + 0.6f * U, y, towerZ));
        }

        Vector3 cableTop = new(towerX + halfW + 0.6f * U, baseY + towerH * 0.86f, towerZ + 3.4f * U);
        Vector3 cableEnd = new(armBaseX + armLen - 2.0f * U, sqdY + 1.6f * U, towerZ + 3.4f * U);
        AddCable("ShipQDSupportCable", cableTop, cableEnd, cableMat);
    }

    // ── Tank farm: cluster of tall white cryo storage tanks on a bund ─────
    private void BuildTankFarm(StandardMaterial3D insul, StandardMaterial3D steel)
    {
        // Off to the +X / +Z corner, away from the tower.
        Vector3 origin = new(58f * U, GradeY, 48f * U);
        const float tankR = 4.2f * U;
        float baseH = (float)Spec.CommodityTankMaxHeight * U;

        // Concrete containment bund (spill berm) the whole farm sits on.
        var bundCol = Mat(new Color(0.42f, 0.41f, 0.39f), 0.95f, 0.0f);
        Spawn("TankBund", new BoxMesh { Size = new Vector3(60f * U, 1.6f * U, 44f * U) },
            bundCol, new Vector3(origin.X + 20f * U, origin.Y + 0.8f * U, origin.Z + 10f * U));
        // Low retaining wall around the bund (four thin boxes).
        foreach (var (dx, dz, lx, lz) in new[]
        {
            (-30f * U, 0f, 1.0f * U, 44f * U), (30f * U, 0f, 1.0f * U, 44f * U),
            (0f, -22f * U, 60f * U, 1.0f * U), (0f, 22f * U, 60f * U, 1.0f * U),
        })
        {
            Spawn("BundWall", new BoxMesh { Size = new Vector3(lx, 2.4f * U, lz) },
                bundCol, new Vector3(origin.X + 20f * U + dx, origin.Y + 1.2f * U, origin.Z + 10f * U + dz));
        }

        // FAA baseline: approximately fifteen vertical commodity tanks up to 100 ft.
        float[] hMul = { 1.0f, .92f, .78f, .96f, .84f,
                         .88f, 1.0f, .72f, .94f, .80f,
                         .76f, .90f, .68f, .86f, .74f };
        int idx = 0;
        for (int gz = 0; gz < 3; gz++)
        for (int gx = 0; gx < 5; gx++)
        {
            float tankH = baseH * hMul[idx];
            float px = origin.X + gx * 10f * U;
            float pz = origin.Z + gz * 10f * U;
            string n = $"Tank{gx}_{gz}";

            // Tank body.
            Spawn($"{n}Body", new CylinderMesh
                { TopRadius = tankR, BottomRadius = tankR, Height = tankH, RadialSegments = 24 },
                insul, new Vector3(px, origin.Y + tankH * 0.5f, pz));

            // Domed top cap.
            var dome = new MeshInstance3D
            {
                Name     = $"{n}Dome",
                Mesh     = new SphereMesh
                    { Radius = tankR, Height = tankR, IsHemisphere = true, RadialSegments = 24, Rings = 8 },
                Position = new Vector3(px, origin.Y + tankH, pz),
            };
            dome.SetSurfaceOverrideMaterial(0, insul);
            AddChild(dome);

            // Short support skirt at the base.
            Spawn($"{n}Skirt", new CylinderMesh
                { TopRadius = tankR * 0.95f, BottomRadius = tankR, Height = 2f * U, RadialSegments = 20 },
                steel, new Vector3(px, origin.Y + 1f * U, pz));

            idx++;
        }

        // Top-of-tank interconnect piping header running along each row.
        foreach (int row in new[] { 0, 1, 2 })
        {
            float pz   = origin.Z + row * 10f * U;
            float topY = origin.Y + baseH + 1.5f * U;
            Spawn($"TankHeader{row}", new CylinderMesh
                { TopRadius = 0.65f * U, BottomRadius = 0.65f * U, Height = 42f * U, RadialSegments = 10 },
                steel, new Vector3(origin.X + 20f * U, topY, pz))
                .RotationDegrees = new Vector3(0, 90f, 90f);
        }
        // Cross header tying the two rows together at the tower side.
        Spawn("TankCrossHeader", new CylinderMesh
            { TopRadius = 0.65f * U, BottomRadius = 0.65f * U, Height = 22f * U, RadialSegments = 10 },
            steel, new Vector3(origin.X, origin.Y + baseH + 1.5f * U, origin.Z + 10f * U))
            .RotationDegrees = new Vector3(90f, 0, 0);
    }

    // ── Lightning-protection towers (thin tall masts) ─────────────────────
    private void BuildLightningTowers(StandardMaterial3D steel)
    {
        const float mastH = 130f * U;     // ~130 m masts
        const float baseY = -22f * U + 6.5f * U;

        // Three masts ringing the pad, clear of the stack and tower.
        var spots = new (float x, float z)[]
        {
            ( 38f * U, -38f * U),
            (-30f * U,  42f * U),
            ( 50f * U,   2f * U),
        };

        var tips = new Vector3[spots.Length];
        int idx = 0;
        foreach (var (x, z) in spots)
        {
            // Tapered mast.
            Spawn($"Mast{idx}", new CylinderMesh
                { TopRadius = 0.25f * U, BottomRadius = 1.4f * U, Height = mastH, RadialSegments = 8 },
                steel, new Vector3(x, baseY + mastH * 0.5f, z));

            // Tip spike (the air terminal).
            Spawn($"MastTip{idx}", new CylinderMesh
                { TopRadius = 0.04f * U, BottomRadius = 0.25f * U, Height = 6f * U, RadialSegments = 6 },
                steel, new Vector3(x, baseY + mastH + 3f * U, z));

            // Concrete footing block under each mast.
            Spawn($"MastFoot{idx}", new BoxMesh { Size = new Vector3(5f * U, 1.5f * U, 5f * U) },
                Mat(new Color(0.40f, 0.39f, 0.37f), 0.95f, 0.0f), new Vector3(x, baseY + 0.75f * U, z));

            tips[idx] = new Vector3(x, baseY + mastH + 6f * U, z);
            idx++;
        }

        // Implied overhead catenary cabling strung between the mast tips: a thin
        // dark wire per pair, sagging slightly (split into two angled segments).
        var cableMat = Mat(new Color(0.06f, 0.06f, 0.07f), 0.9f, 0.2f);
        for (int i = 0; i < tips.Length; i++)
        {
            Vector3 a = tips[i];
            Vector3 b = tips[(i + 1) % tips.Length];
            Vector3 mid = (a + b) * 0.5f - new Vector3(0, 8f * U, 0); // sag downward
            AddCable($"LightCableA{i}", a, mid, cableMat);
            AddCable($"LightCableB{i}", mid, b, cableMat);
        }
    }

    // Thin straight cable segment between two points (for catenary wiring).
    private void AddCable(string name, Vector3 a, Vector3 b, StandardMaterial3D mat)
    {
        Vector3 d   = b - a;
        float   len = d.Length();
        if (len < 0.001f) return;
        var node = new MeshInstance3D
        {
            Name     = name,
            Mesh     = new CylinderMesh
                { TopRadius = 0.12f * U, BottomRadius = 0.12f * U, Height = len, RadialSegments = 5 },
            Position = a + d * 0.5f,
        };
        // Orient cylinder (+Y axis) along the cable direction.
        Vector3 up  = Vector3.Up;
        Vector3 dir = d.Normalized();
        Vector3 axis = up.Cross(dir);
        if (axis.LengthSquared() > 1e-6f)
            node.Quaternion = new Quaternion(axis.Normalized(), Mathf.Acos(Mathf.Clamp(up.Dot(dir), -1f, 1f)));
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
    }

    // ── Beam helpers for the tower lattice ────────────────────────────────
    // Horizontal beam spanning `length` along X (alongX=true) or Z (false).
    private void AddBeam(string name, Vector3 pos, float length, float _, bool alongX)
    {
        var size = alongX
            ? new Vector3(length, 0.5f / 2.8f, 0.5f / 2.8f)
            : new Vector3(0.5f / 2.8f, 0.5f / 2.8f, length);
        Spawn(name, new BoxMesh { Size = size }, _latticeMat, pos);
    }

    // Diagonal brace on an X-facing face, rotated about Z.
    private void AddDiag(string name, Vector3 pos, float length, float angDeg, bool _)
    {
        var node = new MeshInstance3D
        {
            Name            = name,
            Mesh            = new BoxMesh { Size = new Vector3(length, 0.4f / 2.8f, 0.4f / 2.8f) },
            Position        = pos,
            RotationDegrees = new Vector3(0, 0, angDeg),
        };
        node.SetSurfaceOverrideMaterial(0, _latticeMat);
        AddChild(node);
    }

    // Cached lattice material so all the small beams share one material.
    private StandardMaterial3D? _latticeMatCache;
    private StandardMaterial3D _latticeMat =>
        _latticeMatCache ??= Mat(new Color(0.50f, 0.51f, 0.54f), 0.55f, 0.82f);

    private static StandardMaterial3D Mat(Color albedo, float roughness, float metallic)
    {
        return new StandardMaterial3D { AlbedoColor = albedo, Roughness = roughness, Metallic = metallic };
    }

    private MeshInstance3D Spawn(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    private MeshInstance3D SpawnRot(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos,
                                    Vector3 rotationDegrees)
    {
        var node = Spawn(name, mesh, mat, pos);
        node.RotationDegrees = rotationDegrees;
        return node;
    }
}
