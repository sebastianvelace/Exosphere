namespace Exosphere.Game;

using Godot;

/// <summary>
/// Starbase-inspired orbital launch complex (OLM + Mechazilla tower + tank
/// farm + lightning towers). This Node3D is positioned each frame by
/// SimulationBridge so it stays anchored to the Earth surface directly below
/// the vessel.
///
/// Scale: render units are ~2.8 m/unit (see VesselRenderer). The vessel sits
/// at the render origin with its base (booster engine tips) at y≈0 and a body
/// radius of ~1.15 units (~9 m diameter). Everything here is positioned so the
/// booster fires down through the centre of the launch mount.
/// </summary>
public partial class LaunchPadController : Node3D
{
    public static LaunchPadController? Instance { get; private set; }

    // 1 render unit ≈ 2.8 m. Helper so the code below can read in metres.
    private const float U = 1f / 2.8f;   // metres → render units

    public override void _Ready()
    {
        Instance = this;
        BuildEnvironment();
    }

    private void BuildEnvironment()
    {
        // ── Shared materials (kept few so they batch) ──────────────────────
        var concrete  = Mat(new Color(0.40f, 0.39f, 0.37f), 0.95f, 0.0f);
        var concDark  = Mat(new Color(0.24f, 0.23f, 0.21f), 0.97f, 0.0f);
        var burnt     = Mat(new Color(0.09f, 0.08f, 0.07f), 0.98f, 0.0f);
        var steel     = Mat(new Color(0.55f, 0.56f, 0.58f), 0.55f, 0.85f); // grey lattice steel
        var darkSteel = Mat(new Color(0.28f, 0.28f, 0.31f), 0.60f, 0.80f); // OLM / dark steel
        var insul     = Mat(new Color(0.86f, 0.86f, 0.88f), 0.80f, 0.10f); // white cryo tanks
        // Weathered, slightly lighter concrete for the wide tarmac, plus a mid
        // scorch tone between clean concrete and fully-charred burnt.
        var tarmac    = Mat(new Color(0.46f, 0.45f, 0.42f), 0.96f, 0.0f); // weathered apron
        var scorch    = Mat(new Color(0.16f, 0.15f, 0.13f), 0.97f, 0.0f); // blast-zone stain

        BuildConcretePad(concrete, concDark, burnt);
        BuildLaunchApron(tarmac, concDark, scorch, burnt);
        BuildOrbitalLaunchMount(darkSteel, concDark);
        BuildMechazillaTower(steel, darkSteel);
        BuildTankFarm(insul, steel);
        BuildLightningTowers(steel);
        BuildGroundSupport(insul, steel, darkSteel, concrete, concDark);
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
        const float padY = -22f * U + 6.5f * U;

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
    }

    // ── Orbital Launch Mount (OLM): ring table on splayed legs ────────────
    private void BuildOrbitalLaunchMount(StandardMaterial3D steel, StandardMaterial3D conc)
    {
        // The booster base sits at y=0; engines fire down through the centre.
        // OLM table top should land just under the booster base. Table is ~20 m
        // (≈7 units) outer diameter with a centre hole the engines clear.
        const float tableTopY   = -0.6f * U;                 // just below booster base
        const float tableThick  = 4f * U;                    // table deck thickness
        const float outerR      = 10.5f * U;                 // ~21 m outer diameter
        const float innerR      = 3.6f * U;                  // centre hole (booster R≈1.15u)
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

        // Outer skirt wall of the table (the tall cylindrical band of the OLM).
        Spawn("OLMSkirt", new CylinderMesh
            { TopRadius = outerR, BottomRadius = outerR, Height = 7f * U, RadialSegments = 28 },
            steel, new Vector3(0, tableTopY - tableThick - 3.5f * U, 0));

        // Inner ring wall around the centre hole.
        Spawn("OLMHole", new CylinderMesh
            { TopRadius = innerR, BottomRadius = innerR, Height = 7f * U, RadialSegments = 24 },
            steel, new Vector3(0, tableTopY - tableThick - 3.0f * U, 0));

        // Booster hold-down ring on the deck (where the booster clamps sit).
        Spawn("HoldDownRing", new CylinderMesh
            { TopRadius = innerR + 0.6f * U, BottomRadius = innerR + 0.6f * U, Height = 0.5f * U, RadialSegments = 24 },
            steel, new Vector3(0, tableTopY + 0.2f * U, 0));

        // ── Splayed support legs (~6, ~20 m tall) ─────────────────────────
        // Legs run from under the table skirt down to the pad footing, splayed
        // outward at the base. Footing is at the scorch-apron level.
        const int legCount = 6;
        const float legTopY = tableTopY - tableThick - 1f * U;
        const float legBotY = -22f * U + 6.5f * U;           // pad surface
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
    }

    // ── Mechazilla: tall square lattice integration tower + chopstick arms ─
    private void BuildMechazillaTower(StandardMaterial3D steel, StandardMaterial3D darkSteel)
    {
        // ~145 m tall (~52 units), square cross-section ~12 m, set ~35 m to one
        // side of the OLM so the booster clears it.
        const float towerH    = 145f * U;
        const float towerW     = 12f * U;
        const float baseY      = -22f * U + 6.5f * U;        // foots on the pad
        const float towerX     = -35f * U;                   // off to the -X side
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

        // Tower cap / crane head.
        Spawn("TowerCap", new BoxMesh { Size = new Vector3(towerW + 1.5f * U, 5f * U, towerW + 1.5f * U) },
            darkSteel, new Vector3(towerX, baseY + towerH + 2.5f * U, towerZ));

        // ── Chopstick catch arms (two horizontal arms partway up) ─────────
        // Real arms sit ~40–70 m up; here ~55 m so they're above the OLM table
        // and read as the catch hardware. They point toward the OLM (+X side).
        BuildChopstickArms(towerX, towerZ, halfW, baseY, darkSteel, steel);

        // Quick-disconnect / carrier arm lower down, swinging toward the stack.
        Spawn("QDArm", new BoxMesh { Size = new Vector3(26f * U, 3f * U, 4f * U) },
            darkSteel, new Vector3(towerX + 14f * U, baseY + 30f * U, towerZ - 6f * U));
    }

    private void BuildChopstickArms(float towerX, float towerZ, float halfW, float baseY,
                                    StandardMaterial3D armMat, StandardMaterial3D pivotMat)
    {
        const float armY    = 55f * U;                       // height up the tower
        const float armLen  = 30f * U;                       // ~84 m reach
        const float armGap  = 5f * U;                         // gap between the two sticks
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
                { Size = new Vector3(armLen, 2.5f * U, 3f * U) },
                armMat, new Vector3(armMidX, baseY + armY, towerZ + zoff));

            // Cradle pad on the inner top edge of each arm.
            Spawn($"ChopstickPad{tag}", new BoxMesh
                { Size = new Vector3(armLen * 0.7f, 0.6f * U, 1.2f * U) },
                pivotMat, new Vector3(armMidX, baseY + armY + 1.5f * U,
                    towerZ + zoff + (zoff < 0 ? 1.2f * U : -1.2f * U)));
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
    }

    // ── Tank farm: cluster of tall white cryo storage tanks ───────────────
    private void BuildTankFarm(StandardMaterial3D insul, StandardMaterial3D steel)
    {
        // Off to the +X / +Z corner, away from the tower.
        Vector3 origin = new(45f * U, -22f * U + 6.5f * U, 45f * U);
        const float tankR = 5.5f * U;     // ~11 m diameter
        const float tankH = 26f * U;      // ~26 m tall

        // 2×3 grid of cylindrical tanks (6 total).
        for (int gx = 0; gx < 3; gx++)
        for (int gz = 0; gz < 2; gz++)
        {
            float px = origin.X + gx * 13f * U;
            float pz = origin.Z + gz * 13f * U;
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
        }
    }

    // ── Lightning-protection towers (thin tall masts) ─────────────────────
    private void BuildLightningTowers(StandardMaterial3D steel)
    {
        const float mastH = 130f * U;     // ~130 m masts
        const float baseY = -22f * U + 6.5f * U;

        // A few masts ringing the pad, clear of the stack and tower.
        var spots = new (float x, float z)[]
        {
            ( 38f * U, -38f * U),
            (-30f * U,  42f * U),
            ( 50f * U,   2f * U),
        };

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
            idx++;
        }
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
}
