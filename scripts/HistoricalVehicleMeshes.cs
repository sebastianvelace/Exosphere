namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation.Parts;

// Procedural exterior detail for the historical launchers and spacecraft (Mercury-Redstone,
// Mercury-Atlas, Titan II GLV + Gemini, Agena, Saturn V + Apollo CSM/LM). Presentation only:
// every builder reads the part's JSON length/diameter and never touches physics data.
//
// Geometry is authored in METRES in the part frame (origin at the part centre, y up) and
// merged into one multi-surface mesh per part (one draw call per material), scaled to
// render units by a single MeshInstance3D. Meshes are cached per part definition and
// context flags, so staging rebuilds and repeated launches reuse them.
//
// Seam rule (see the rocket-visual-design skill): stacked barrels are open lathes; a flat
// closure is only added on a genuinely exposed end, inset from the joint plane, so two
// coplanar caps never z-fight.
public partial class VesselRenderer
{
    private static readonly Dictionary<string, ArrayMesh?> HistMeshCache = new();

    private static readonly Color HWhite = new(0.86f, 0.86f, 0.83f);
    private static readonly Color HBlack = new(0.035f, 0.035f, 0.04f);

    private static Vector2[] Prof(params float[] radiusHeightPairs)
    {
        var points = new Vector2[radiusHeightPairs.Length / 2];
        for (int i = 0; i < points.Length; i++)
            points[i] = new Vector2(radiusHeightPairs[i * 2], radiusHeightPairs[i * 2 + 1]);
        return points;
    }

    private static Vector3 Radial(float theta) => new(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
    private static Vector3 Tangent(float theta) => new(-Mathf.Sin(theta), 0f, Mathf.Cos(theta));

    // ── Materials ─────────────────────────────────────────────────────────

    private static StandardMaterial3D HPaint(string key, Color color, float roughness = 0.55f,
        string? normal = null, Vector2? uv = null, float normalScale = 0.6f,
        string? albedo = null, bool doubleSided = false)
        => HistMaterials.Get("paint_" + key, color, 0f, roughness, normal, normalScale, uv,
            albedo, doubleSided);

    private static StandardMaterial3D HMetal(string key, Color color, float metallic,
        float roughness, string? normal = null, Vector2? uv = null, float normalScale = 0.6f,
        string? albedo = null, float emission = 0.10f, bool doubleSided = false)
        => HistMaterials.Get("metal_" + key, color, metallic, roughness, normal, normalScale,
            uv, albedo, doubleSided, emission);

    private static StandardMaterial3D HWhitePaint => HPaint("white", HWhite);
    private static StandardMaterial3D HBlackPaint => HPaint("black", HBlack, 0.62f);
    private static StandardMaterial3D HDarkInterior =>
        HPaint("interior", new Color(0.05f, 0.05f, 0.055f), 0.85f, doubleSided: true);
    private static StandardMaterial3D HDarkMetal =>
        HMetal("dark", new Color(0.20f, 0.20f, 0.21f), 0.55f, 0.42f, emission: 0.05f);
    private static StandardMaterial3D HGlass =>
        HPaint("glass", new Color(0.01f, 0.012f, 0.016f), 0.6f);

    // ── Shared primitives ─────────────────────────────────────────────────

    private static void HCylinder(HistGeo geo, float radius, float yBottom, float yTop,
        int segments = 48)
        => geo.Lathe(Prof(radius, yBottom, radius, yTop), segments);

    /// <summary>Flat closure facing up or down (double-sided dark materials preferred).</summary>
    private static void HDisk(HistGeo geo, float radius, float y, bool facingUp,
        int segments = 48)
        => geo.Lathe(facingUp ? Prof(radius, y, 0f, y) : Prof(0f, y, radius, y), segments);

    /// <summary>Slightly proud circumferential band (weld land, frame, separation joint).</summary>
    private static void HBand(HistGeo geo, float radius, float y, float height, float proud,
        int segments = 48)
    {
        float h = height * 0.5f;
        geo.Lathe(Prof(radius, y - h, radius + proud, y - h, radius + proud, y + h,
            radius, y + h), segments, smoothDeg: 10f);
    }

    /// <summary>
    /// Cylinder split into equal azimuth sectors alternating two paints: the black/white
    /// roll-reference patterns of Redstone, Titan and Saturn stages.
    /// </summary>
    private static void HRollPattern(HistMeshKit kit, float radius, float yBottom, float yTop,
        int sectors, int phase, Material light, Material dark, float startAngle = 0f,
        float slopeTopRadius = -1f)
    {
        float topRadius = slopeTopRadius > 0f ? slopeTopRadius : radius;
        int perSector = Mathf.Max(2, 64 / sectors);
        for (int k = 0; k < sectors; k++)
        {
            float a0 = startAngle + Mathf.Tau * k / sectors;
            float a1 = startAngle + Mathf.Tau * (k + 1) / sectors;
            var material = (k + phase) % 2 == 0 ? light : dark;
            kit[material].Lathe(Prof(radius, yBottom, topRadius, yTop), perSector, a0, a1);
        }
    }

    /// <summary>Closures for genuinely exposed ends, inset so they never share a joint plane.</summary>
    private static void HExposedCaps(HistGeo geo, float radius, float yBottom, float yTop,
        bool hasAbove, bool hasBelow)
    {
        if (!hasAbove) HDisk(geo, radius, yTop - 0.02f, facingUp: true);
        if (!hasBelow) HDisk(geo, radius, yBottom + 0.02f, facingUp: false);
    }

    private static Node3D HAttach(Node3D root, string cacheKey, Action<HistMeshKit> build,
        bool flip = false)
    {
        if (!HistMeshCache.TryGetValue(cacheKey, out var mesh))
        {
            var kit = new HistMeshKit();
            build(kit);
            mesh = kit.Build();
            HistMeshCache[cacheKey] = mesh;
        }
        if (mesh != null)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "Detail",
                Mesh = mesh,
                Scale = Vector3.One / MetresPerUnit,
                // Parts whose forward face is local -Y (Agena) are authored nose-up.
                Rotation = flip ? new Vector3(Mathf.Pi, 0f, 0f) : Vector3.Zero,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
        }
        return root;
    }

    private bool VesselHasRole(string role)
        => TargetVessel?.Parts.Parts.Any(p => p.Definition.HasVehicleRole(role)) ?? false;

    private static string CapKey(bool above, bool below) => (above ? "A" : "a") + (below ? "B" : "b");

    private static void AddVerticalMarking(Node3D root, string text, float theta,
        float radiusM, float yM, float letterHeightM, Color color)
    {
        const int fontSize = 96;
        var label = new Label3D
        {
            Name = "Marking_" + text.Replace(" ", ""),
            Text = text,
            FontSize = fontSize,
            PixelSize = letterHeightM / MetresPerUnit / fontSize,
            Modulate = color,
            OutlineSize = 0,
            DoubleSided = false,
            Shaded = true,
            Position = Radial(theta) * (radiusM / MetresPerUnit) + Vector3.Up * (yM / MetresPerUnit),
        };
        // Face outward along the radial direction, text running top-to-bottom.
        Vector3 normal = Radial(theta);
        label.Basis = new Basis(Vector3.Down, normal.Cross(Vector3.Down), normal);
        root.AddChild(label);
    }

    // ── Dispatch ──────────────────────────────────────────────────────────

    private bool TryCreateHistoricalPartNode(Part part, bool hasAbove, bool hasBelow,
        out Node3D node)
    {
        var d = part.Definition;
        string family = d.VehicleFamily.ToLowerInvariant();
        Node3D? built = family switch
        {
            "mercury" => CreateMercuryFamilyNode(part, hasAbove, hasBelow),
            "gemini" => CreateGeminiFamilyNode(part, hasAbove, hasBelow),
            "agena" => d.HasVehicleRole("agena_target_docking_port")
                ? BuildAgenaDockingAdapter(d)
                : BuildAgenaBus(d, hasBelow),
            "apollo" => CreateApolloFamilyNode(part, hasAbove, hasBelow),
            _ => null,
        };
        node = built!;
        return built != null;
    }

    private Node3D? CreateMercuryFamilyNode(Part part, bool hasAbove, bool hasBelow)
    {
        var d = part.Definition;
        if (d.HasVehicleRole("capsule")) return BuildMercuryCapsule(part);
        if (d.HasVehicleRole("escape_tower")) return BuildMercuryEscapeTower(d);
        if (d.HasVehicleRole("retro_pack")) return BuildMercuryRetroPack(d);
        if (d.HasVehicleRole("spacecraft_separation")) return BuildSeparationRing(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("adapter"))
            return d.Id.Contains("redstone", StringComparison.Ordinal)
                ? BuildRedstoneAdapter(d)
                : BuildAtlasAdapter(d);
        if (d.HasVehicleRole("booster_tank")) return BuildRedstoneTank(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("booster_engine")) return BuildRedstoneTail(d);
        if (d.HasVehicleRole("atlas_tank")) return BuildAtlasTank(d, hasAbove);
        if (d.HasVehicleRole("sustainer_engine")) return BuildAtlasThrustSection(d);
        if (d.HasVehicleRole("booster_engine_package")) return BuildAtlasBoosterSection(d);
        return null;
    }

    private Node3D? CreateGeminiFamilyNode(Part part, bool hasAbove, bool hasBelow)
    {
        var d = part.Definition;
        if (d.HasVehicleRole("gemini_reentry_capsule")) return BuildGeminiReentryModule(part);
        if (d.HasVehicleRole("gemini_docking_port")) return BuildGeminiNose(d);
        if (d.HasVehicleRole("gemini_retro_module")) return BuildGeminiAdapterCone(d, 0.87f, 0.76f, true, hasAbove, hasBelow);
        if (d.HasVehicleRole("gemini_equipment_adapter")) return BuildGeminiAdapterCone(d, 1.0f, 0.87f, false, hasAbove, hasBelow);
        if (d.HasVehicleRole("spacecraft_separation")) return BuildSeparationRing(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("titan_stage1_tank") || d.HasVehicleRole("titan_stage2_tank"))
            return BuildTitanTank(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("titan_stage2_engine")) return BuildTitanStage2Engine(d);
        if (d.HasVehicleRole("titan_stage1_engine")) return BuildTitanStage1Aft(d);
        if (d.HasVehicleRole("titan_interstage")) return BuildTitanInterstage(d, hasAbove, hasBelow);
        return null;
    }

    private Node3D? CreateApolloFamilyNode(Part part, bool hasAbove, bool hasBelow)
    {
        var d = part.Definition;
        if (d.HasVehicleRole("launch_escape_system")) return BuildApolloLaunchEscapeSystem(d);
        if (d.HasVehicleRole("command_module")) return BuildApolloCommandModule(d);
        if (d.HasVehicleRole("service_module")) return BuildApolloServiceModule(d);
        if (d.HasVehicleRole("service_propulsion_engine")) return BuildApolloSpsMount(d);
        if (d.HasVehicleRole("csm_separation")) return BuildApolloCsmSeparationRing(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("sla_lunar_test_article") || d.HasVehicleRole("sla_lunar_module"))
            return BuildApolloSla(d);
        if (d.HasVehicleRole("instrument_unit")) return BuildSaturnInstrumentUnit(d, hasAbove);
        if (d.HasVehicleRole("sivb_tank")) return BuildSaturnSivbTank(d, hasAbove, hasBelow);
        if (d.HasVehicleRole("sivb_engine")) return BuildSaturnSivbAft(d);
        if (d.HasVehicleRole("sii_sivb_interstage")) return BuildSaturnSiiSivbInterstage(d);
        if (d.HasVehicleRole("sii_tank")) return BuildSaturnSiiTank(d, hasAbove);
        if (d.HasVehicleRole("sii_engine_cluster")) return BuildSaturnSiiAft(d);
        if (d.HasVehicleRole("sic_sii_interstage")) return BuildSaturnSicSiiInterstage(d, hasAbove);
        if (d.HasVehicleRole("sic_tank")) return BuildSaturnSicTank(d, hasAbove);
        if (d.HasVehicleRole("sic_engine_cluster")) return BuildSaturnSicThrustSection(d);
        if (d.HasVehicleRole("lunar_module_ascent_stage")) return BuildLmAscentStage(d);
        if (d.HasVehicleRole("lunar_module_ascent_engine")) return BuildLmAscentMidsection(d);
        if (d.HasVehicleRole("lunar_module_stage_separation")) return BuildLmStagePlate(d);
        if (d.HasVehicleRole("lunar_module_descent_stage")) return BuildLmDescentStage(d);
        if (d.HasVehicleRole("lunar_module_descent_engine")) return BuildLmDescentEngineMount(d);
        return null;
    }

    // ══ Mercury ═══════════════════════════════════════════════════════════

    private static StandardMaterial3D MercuryShingles => HMetal("mercury_shingle",
        new Color(0.11f, 0.11f, 0.115f), 0.45f, 0.40f, "shingles", new Vector2(24f, 3.4f),
        normalScale: 0.9f, emission: 0.04f);
    private static StandardMaterial3D MercuryBeryllium => HMetal("mercury_beryllium",
        new Color(0.17f, 0.165f, 0.16f), 0.40f, 0.46f, "ribs", new Vector2(36f, 1f),
        normalScale: 0.5f, emission: 0.04f);
    private static StandardMaterial3D AblativeShield => HPaint("ablative",
        new Color(0.30f, 0.21f, 0.13f), 0.88f);
    private static StandardMaterial3D MercuryRed => HPaint("mercury_red",
        new Color(0.70f, 0.11f, 0.07f), 0.48f);

    private Node3D BuildMercuryCapsule(Part part)
    {
        var d = part.Definition;
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        bool friendship = d.Id.Contains("friendship", StringComparison.Ordinal);
        var root = new Node3D { Name = "MercuryCapsule" };
        HAttach(root, "mercury_capsule_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f;
            float hs = 0.05f * L;
            float coneBase = y0 + 0.03f, coneTop = y0 + 0.53f * L;
            float rTop = 0.43f * R, rRecovery = 0.40f * R;
            kit[AblativeShield].Lathe(Prof(0f, y0 - hs, 0.55f * R, y0 - 0.7f * hs,
                0.92f * R, y0 - 0.25f * hs, R, y0, R, coneBase), 56);
            kit[MercuryShingles].Lathe(Prof(R, coneBase, rTop, coneTop), 56);
            var metal = HDarkMetal;
            kit[metal].Lathe(Prof(rTop, coneTop, rTop * 1.03f, coneTop,
                rTop * 1.03f, coneTop + 0.02f * L, rRecovery, coneTop + 0.02f * L), 48, smoothDeg: 10f);
            kit[MercuryBeryllium].Lathe(Prof(rRecovery, coneTop + 0.02f * L,
                rRecovery, y0 + 0.74f * L), 40);
            var antenna = HMetal("mercury_antenna", new Color(0.42f, 0.42f, 0.41f), 0.5f, 0.38f,
                "ribs", new Vector2(16f, 1f), normalScale: 0.4f);
            kit[antenna].Lathe(Prof(rRecovery, y0 + 0.74f * L, 0.31f * R, y0 + 0.765f * L,
                0.29f * R, y0 + 0.965f * L, 0.20f * R, y0 + 0.99f * L, 0f, y0 + L), 36,
                smoothDeg: 25f);
            // Recovery-compartment frames and the horizon-scanner ring.
            HBand(kit[metal], rRecovery, y0 + 0.66f * L, 0.03f, 0.012f, 40);
            HBand(kit[metal], 0.29f * R, y0 + 0.90f * L, 0.025f, 0.01f, 32);

            float slope = (rTop - R) / (coneTop - coneBase);
            float RadiusAt(float y) => R + slope * (y - coneBase);
            float front = Mathf.Pi * 0.5f;
            if (friendship)
            {
                float yw = y0 + 0.40f * L;
                kit[HGlass].SurfaceBox(front, RadiusAt(yw), yw, 0.30f, 0.42f, 0.012f, slope);
                kit[metal].SurfaceBox(front, RadiusAt(yw), yw, 0.38f, 0.50f, 0.006f, slope);
            }
            else
            {
                foreach (float side in new[] { -0.28f, 0.28f })
                {
                    float yw = y0 + 0.38f * L;
                    kit[HGlass].SurfaceBox(front + side, RadiusAt(yw), yw, 0.15f, 0.15f, 0.012f, slope);
                }
            }
            // Entry hatch outline and the retro-pack strap anchors.
            float yh = y0 + 0.22f * L;
            kit[metal].SurfaceBox(front + 0.85f, RadiusAt(yh), yh, 0.56f, 0.03f, 0.014f, slope);
            kit[metal].SurfaceBox(front + 0.85f, RadiusAt(yh + 0.55f), yh + 0.55f, 0.46f, 0.03f, 0.014f, slope);
        });

        // Main parachute visual, toggled by part deployment (render units).
        float height = L / MetresPerUnit;
        var canopy = new Node3D
        {
            Name = "MainParachute",
            Position = Vector3.Up * (height * 0.5f + 3.0f),
            Visible = false,
        };
        var canopyMesh = new MeshInstance3D
        {
            Name = "Canopy",
            Mesh = new SphereMesh { Radius = 3.4f, Height = 2.1f, RadialSegments = 48, Rings = 16 },
        };
        canopyMesh.SetSurfaceOverrideMaterial(0, Mat(new Color(0.88f, 0.34f, 0.25f), 0.0f, 0.76f));
        canopy.AddChild(canopyMesh);
        var lineMat = Mat(new Color(0.78f, 0.76f, 0.68f), 0.0f, 0.8f);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Tau / 8f;
            var line = new MeshInstance3D
            {
                Name = $"SuspensionLine{i}",
                Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = 3.0f, RadialSegments = 8 },
                Position = new Vector3(1.8f * Mathf.Cos(angle), -1.8f, 1.8f * Mathf.Sin(angle)),
                RotationDegrees = new Vector3(20f * Mathf.Sin(angle), 0f, -20f * Mathf.Cos(angle)),
            };
            line.SetSurfaceOverrideMaterial(0, lineMat);
            canopy.AddChild(line);
        }
        root.AddChild(canopy);
        _parachuteVisuals[part.InstanceId] = canopy;
        return root;
    }

    private static Node3D BuildMercuryEscapeTower(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "MercuryEscapeTower" };
        return HAttach(root, "mercury_tower_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f;
            var red = MercuryRed;
            var geo = kit[red];
            float trussTop = y0 + 0.50f * L;
            float rBase = R * 0.95f, rTopTruss = 0.15f;
            const int bays = 5;
            const float strut = 0.022f;
            Vector3 LegPoint(int leg, int level)
            {
                float t = (float)level / bays;
                float r = Mathf.Lerp(rBase, rTopTruss, t);
                float theta = Mathf.Pi * 0.5f + leg * Mathf.Tau / 3f;
                return Radial(theta) * r + Vector3.Up * Mathf.Lerp(y0 + 0.03f, trussTop, t);
            }
            for (int leg = 0; leg < 3; leg++)
            {
                geo.Tube(LegPoint(leg, 0), LegPoint(leg, bays), strut * 1.4f, 6);
                for (int level = 0; level <= bays; level++)
                    geo.Tube(LegPoint(leg, level), LegPoint((leg + 1) % 3, level), strut, 5);
                for (int level = 0; level < bays; level++)
                    geo.Tube(LegPoint(leg, level), LegPoint((leg + 1) % 3, level + 1), strut * 0.85f, 5);
            }
            geo.Torus(new Vector3(0f, y0 + 0.03f, 0f), rBase, 0.03f, 32, 6);

            // Escape motor: nozzle block with three canted nozzles, case, jettison motor, nose.
            float caseBottom = trussTop + 0.05f * L, caseTop = y0 + 0.85f * L;
            geo.Lathe(Prof(0.10f, trussTop - 0.02f, 0.16f, trussTop, 0.14f, caseBottom), 32);
            geo.Lathe(Prof(0.14f, caseBottom, 0.14f, caseTop, 0.095f, caseTop + 0.03f * L,
                0.095f, y0 + 0.92f * L), 32, smoothDeg: 20f);
            var nose = new List<Vector2>();
            for (int i = 0; i <= 6; i++)
            {
                float t = i / 6f;
                nose.Add(new Vector2(0.095f * Mathf.Sqrt(1f - t * t * 0.98f) * (1f - t * 0.15f),
                    y0 + 0.92f * L + t * 0.08f * L));
            }
            nose.Add(new Vector2(0f, y0 + L));
            geo.Lathe(nose, 32);
            HBand(geo, 0.14f, caseBottom + 0.25f * (caseTop - caseBottom), 0.03f, 0.008f, 32);
            HBand(geo, 0.14f, caseBottom + 0.70f * (caseTop - caseBottom), 0.03f, 0.008f, 32);
            var nozzle = HDarkMetal;
            for (int k = 0; k < 3; k++)
            {
                Vector3 dir = Radial(Mathf.Pi / 6f + k * Mathf.Tau / 3f);
                Vector3 a = dir * 0.07f + Vector3.Up * (trussTop + 0.01f);
                Vector3 b = a + (dir * Mathf.Sin(0.33f) - Vector3.Up * Mathf.Cos(0.33f)) * 0.22f;
                kit[nozzle].Frustum(a, b, 0.035f, 0.075f, 12);
                kit[HDarkInterior].Frustum(b + (a - b) * 0.02f, a, 0.07f, 0.03f, 12);
            }
        });
    }

    private static Node3D BuildMercuryRetroPack(PartDefinition d)
    {
        float D = (float)d.DiameterM;
        var root = new Node3D { Name = "MercuryRetroPack" };
        return HAttach(root, "mercury_retro_" + d.Id, kit =>
        {
            var casing = HMetal("retro_casing", new Color(0.30f, 0.28f, 0.25f), 0.45f, 0.5f);
            var metal = HDarkMetal;
            kit[metal].Lathe(Prof(0.44f, -0.23f, 0.44f, -0.19f, 0f, -0.17f), 32);
            kit[metal].Lathe(Prof(0f, -0.23f, 0.44f, -0.23f), 32);
            for (int k = 0; k < 3; k++)
            {
                Vector3 c = Radial(Mathf.Pi * 0.5f + k * Mathf.Tau / 3f) * 0.19f + Vector3.Down * 0.39f;
                var sphere = new List<Vector2>();
                for (int i = 0; i <= 10; i++)
                {
                    float a = -Mathf.Pi * 0.5f + Mathf.Pi * i / 10f;
                    sphere.Add(new Vector2(0.155f * Mathf.Cos(a), 0.155f * Mathf.Sin(a)));
                }
                kit[casing].Lathe(sphere, 20, center: c, smoothDeg: 80f);
                kit[metal].Frustum(c + Vector3.Down * 0.14f, c + Vector3.Down * 0.22f, 0.04f, 0.06f, 10);
            }
            for (int k = 0; k < 3; k++)
            {
                Vector3 dir = Radial(Mathf.Pi * 0.5f + Mathf.Pi / 3f + k * Mathf.Tau / 3f);
                kit[metal].Tube(dir * 0.42f + Vector3.Down * 0.21f,
                    dir * (0.47f * D) + Vector3.Down * 0.01f, 0.018f, 4);
            }
        });
    }

    private static Node3D BuildSeparationRing(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = Mathf.Max(0.08f, (float)d.LengthM), R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "SeparationRing" };
        return HAttach(root, "sep_ring_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            var metal = HMetal("sep_ring", new Color(0.42f, 0.42f, 0.40f), 0.55f, 0.36f,
                "ribs", new Vector2(60f, 1f), normalScale: 0.3f);
            HCylinder(kit[metal], R, -L * 0.5f, L * 0.5f, 56);
            kit[HDarkMetal].Torus(Vector3.Zero, R, Mathf.Min(0.035f, L * 0.25f), 56, 6);
            HExposedCaps(kit[HDarkInterior], R, -L * 0.5f, L * 0.5f, hasAbove, hasBelow);
        });
    }

    // ── Redstone ──────────────────────────────────────────────────────────

    private static Node3D BuildRedstoneAdapter(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "MercuryRedstoneAdapter" };
        return HAttach(root, "redstone_adapter_" + d.Id, kit =>
        {
            var white = HPaint("redstone_adapter", HWhite, 0.5f, "ribs", new Vector2(40f, 1f), 0.45f);
            HCylinder(kit[white], R, -L * 0.5f, L * 0.5f, 56);
            HBand(kit[HDarkMetal], R, L * 0.5f - 0.03f, 0.05f, 0.012f, 56);
            HBand(kit[HDarkMetal], R, -L * 0.5f + 0.02f, 0.03f, 0.008f, 56);
        });
    }

    private static Node3D BuildRedstoneTank(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "RedstonePropellantStage" };
        HAttach(root, "redstone_tank_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var white = HPaint("redstone_white", HWhite, 0.5f, albedo: "panels_fine",
                uv: new Vector2(4f, 1f / 2.4f));
            float patternBottom = y1 - 1.45f;
            HCylinder(kit[white], R, y0, patternBottom, 64);
            // Instrument compartment roll pattern below the spacecraft adapter.
            HRollPattern(kit, R, patternBottom, y1, 4, 0, white, HBlackPaint, Mathf.Pi * 0.25f);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            foreach (float y in new[] { patternBottom, y0 + 0.42f * L, y0 + 0.08f * L })
                HBand(kit[seam], R, y, 0.04f, 0.006f, 64);
            kit[white].SurfaceBox(0f, R, (y0 + patternBottom) * 0.5f, 0.12f,
                patternBottom - y0 - 0.6f, 0.06f);
            kit[white].SurfaceBox(Mathf.Pi, R, y0 + 0.30f * L, 0.10f, 0.45f * L, 0.05f);
            HExposedCaps(kit[HDarkInterior], R, y0, y1, hasAbove, hasBelow);
        });
        AddVerticalMarking(root, "UNITED STATES", Mathf.Pi * 0.5f, R + 0.004f,
            0.05f * L, 0.30f, new Color(0.05f, 0.05f, 0.05f));
        return root;
    }

    private static Node3D BuildRedstoneTail(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "RedstoneTailUnit" };
        return HAttach(root, "redstone_tail_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var white = HPaint("redstone_white", HWhite, 0.5f, albedo: "panels_fine",
                uv: new Vector2(4f, 1f / 2.4f));
            HRollPattern(kit, R, y0, y1, 4, 1, white, HBlackPaint, Mathf.Pi * 0.25f);
            kit[HDarkInterior].Lathe(Prof(R, y0, R * 0.99f, y0 + 0.02f), 56);
            HDisk(kit[HDarkInterior], R, y0 + 1.30f, facingUp: false);
            HBand(kit[HDarkMetal], R, y0 + 0.02f, 0.04f, 0.012f, 56);

            var rudder = HPaint("redstone_rudder", new Color(0.10f, 0.10f, 0.11f), 0.55f);
            for (int k = 0; k < 4; k++)
            {
                float theta = k * Mathf.Pi * 0.5f;
                Vector3 radial = Radial(theta), tangent = Tangent(theta);
                Vector3 P(float s, float y) => radial * s + Vector3.Up * y;
                kit[white].Slab(new[]
                {
                    P(R - 0.02f, y0 + 0.30f), P(R - 0.02f, y1 - 0.06f),
                    P(R + 0.70f, y0 + 0.62f), P(R + 0.70f, y0 + 0.30f),
                }, tangent, 0.07f);
                // Air rudder hinged on the fin tip's trailing edge.
                kit[rudder].Slab(new[]
                {
                    P(R + 0.40f, y0 + 0.02f), P(R + 0.40f, y0 + 0.30f),
                    P(R + 0.78f, y0 + 0.30f), P(R + 0.78f, y0 + 0.02f),
                }, tangent, 0.05f);
                // Graphite jet vane in the exhaust plane.
                Vector3 vaneRadial = Radial(theta + Mathf.Pi * 0.25f);
                Vector3 V(float s, float y) => vaneRadial * s + Vector3.Up * y;
                kit[rudder].Slab(new[]
                {
                    V(0.18f, y0 - 0.02f), V(0.18f, y0 + 0.26f), V(0.40f, y0 + 0.26f), V(0.40f, y0 + 0.02f),
                }, Tangent(theta + Mathf.Pi * 0.25f), 0.03f);
            }
        });
    }

    // ── Atlas ─────────────────────────────────────────────────────────────

    private static StandardMaterial3D AtlasSteel => HMetal("atlas_steel",
        new Color(0.60f, 0.605f, 0.60f), 0.48f, 0.33f, albedo: "panels_fine",
        uv: new Vector2(6f, 1f / 1.95f), emission: 0.07f);

    private static Node3D BuildAtlasAdapter(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "MercuryAtlasAdapter" };
        return HAttach(root, "atlas_adapter_" + d.Id, kit =>
        {
            var skin = HMetal("atlas_adapter", new Color(0.58f, 0.585f, 0.58f), 0.45f, 0.36f,
                "ribs_sharp", new Vector2(48f, 1f), 0.5f, emission: 0.07f);
            float rTop = Mathf.Min(R, 0.945f);
            kit[skin].Lathe(Prof(R, -L * 0.5f, R, -L * 0.5f + 0.06f, rTop, L * 0.5f), 64);
            HBand(kit[HDarkMetal], rTop, L * 0.5f - 0.02f, 0.04f, 0.01f, 56);
        });
    }

    private static Node3D BuildAtlasTank(PartDefinition d, bool hasAbove)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "AtlasBalloonTank" };
        HAttach(root, "atlas_tank_" + d.Id + (hasAbove ? "A" : "a"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var steel = AtlasSteel;
            HCylinder(kit[steel], R, y0, y1, 72);
            var weld = HMetal("atlas_weld", new Color(0.50f, 0.50f, 0.49f), 0.55f, 0.36f, emission: 0.10f);
            for (int i = 1; i < 9; i++)
                HBand(kit[weld], R, y0 + L * i / 9f, 0.03f, 0.004f, 72);
            // Cable raceway and the LOX feed fairing down the sides.
            kit[weld].SurfaceBox(0f, R, 0f, 0.16f, L - 1.2f, 0.08f);
            kit[steel].SurfaceBox(Mathf.Pi * 0.85f, R, y0 + 0.30f * L, 0.30f, 0.55f * L, 0.10f);
            // Vernier engines with their housings, low on the tank sides.
            foreach (float theta in new[] { Mathf.Pi * 0.5f, Mathf.Pi * 1.5f })
            {
                kit[steel].SurfaceBox(theta, R, y0 + 0.45f, 0.38f, 0.55f, 0.26f);
                Vector3 radial = Radial(theta);
                Vector3 a = radial * (R + 0.22f) + Vector3.Up * (y0 + 0.20f);
                Vector3 b = a + (radial * 0.25f - Vector3.Up * 0.95f).Normalized() * 0.36f;
                kit[HDarkMetal].Frustum(a, b, 0.05f, 0.11f, 14);
            }
            if (!hasAbove)
                kit[steel].Lathe(Prof(R, y1, R * 0.6f, y1 + 0.18f, 0f, y1 + 0.24f), 48);
        });
        // Historical national marking on the pad-camera face.
        var marking = new Label3D
        {
            Name = "UnitedStatesMarkingAft",
            Text = "UNITED STATES",
            FontSize = 42,
            PixelSize = 0.0065f,
            Modulate = new Color(0.045f, 0.045f, 0.04f),
            OutlineSize = 0,
            Position = new Vector3(0f, L * 0.12f / MetresPerUnit, -R * 1.012f / MetresPerUnit),
            RotationDegrees = new Vector3(0f, 180f, -90f),
        };
        root.AddChild(marking);
        return root;
    }

    private Node3D BuildAtlasThrustSection(PartDefinition d)
    {
        float L = (float)d.LengthM;
        const float atlasRadius = 1.5f;
        bool boosterAttached = VesselHasRole("booster_engine_package");
        var root = new Node3D { Name = "AtlasSustainerSection" };
        return HAttach(root, "atlas_thrust_" + d.Id + (boosterAttached ? "B" : "b"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            if (boosterAttached)
            {
                // Upper booster-section skirt enclosing the sustainer until staging.
                var skirt = HMetal("atlas_skirt", new Color(0.56f, 0.565f, 0.56f), 0.48f, 0.34f,
                    "ribs_sharp", new Vector2(72f, 1f), 0.35f, emission: 0.07f);
                HCylinder(kit[skirt], atlasRadius, y0, y1, 72);
                HBand(kit[HDarkMetal], atlasRadius, y1 - 0.03f, 0.05f, 0.01f, 72);
                return;
            }
            // Exposed after booster staging: aft bulkhead, thrust cone and turbopumps.
            kit[HDarkMetal].Lathe(Prof(0.30f, y0 + 0.75f, 0.62f, y1 - 0.45f,
                atlasRadius * 0.98f, y1 - 0.05f), 48);
            HDisk(kit[HDarkInterior], atlasRadius, y1 - 0.04f, facingUp: false);
            kit[HDarkMetal].Box(new Vector3(0.48f, y0 + 0.95f, 0.18f), new Vector3(0.30f, 0.42f, 0.30f));
            kit[HDarkMetal].Box(new Vector3(-0.46f, y0 + 1.0f, -0.12f), new Vector3(0.26f, 0.36f, 0.26f));
        });
    }

    private static Node3D BuildAtlasBoosterSection(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "AtlasBoosterPackage" };
        return HAttach(root, "atlas_booster_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var skirt = HMetal("atlas_skirt", new Color(0.56f, 0.565f, 0.56f), 0.48f, 0.34f,
                "ribs_sharp", new Vector2(72f, 1f), 0.35f, emission: 0.07f);
            kit[skirt].Lathe(Prof(R * 1.07f, y0, R * 1.04f, y0 + 0.45f, R, y1), 72);
            foreach (float x in new[] { -1.15f, 1.15f })
            {
                var c = new Vector3(x, 0f, 0f);
                kit[skirt].Lathe(Prof(0.64f, y0, 0.64f, y0 + 0.75f, 0.50f, y0 + 1.20f,
                    0.18f, y1 - 0.02f), 32, center: c);
                kit[HDarkInterior].Lathe(Prof(0.63f, y0 + 0.75f, 0.63f, y0 + 0.01f), 32, center: c);
            }
            HDisk(kit[HDarkInterior], R * 1.04f, y0 + 0.30f, facingUp: false, 64);
            HBand(kit[HDarkMetal], R * 1.07f, y0 + 0.02f, 0.04f, 0.01f, 72);
        });
    }

    // ══ Titan II GLV ══════════════════════════════════════════════════════

    private static StandardMaterial3D TitanWhite => HPaint("titan_white",
        new Color(0.84f, 0.85f, 0.83f), 0.5f, albedo: "panels_fine", uv: new Vector2(8f, 1f / 2.2f));

    private static Node3D BuildTitanTank(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        bool stage1 = d.HasVehicleRole("titan_stage1_tank");
        var root = new Node3D { Name = stage1 ? "TitanStage1" : "TitanStage2" };
        return HAttach(root, "titan_tank_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var white = TitanWhite;
            var bands = stage1
                ? new[] { (y1 - 1.65f, y1 - 0.20f), (y0 + 0.56f * L, y0 + 0.56f * L + 1.35f) }
                : new[] { (y1 - 1.30f, y1 - 0.15f) };
            float cursor = y0;
            foreach (var (b0, b1) in bands.OrderBy(b => b.Item1))
            {
                HCylinder(kit[white], R, cursor, b0, 64);
                HRollPattern(kit, R, b0, b1, 8, 0, white, HBlackPaint);
                cursor = b1;
            }
            HCylinder(kit[white], R, cursor, y1, 64);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], R, y1 - 0.08f, 0.04f, 0.006f, 64);
            HBand(kit[seam], R, y0 + (stage1 ? 0.30f : 0.45f) * L, 0.04f, 0.006f, 64);
            // External propellant/cable conduits, the Titan's signature tubes.
            kit[white].SurfaceBox(0f, R, 0f, 0.17f, L - 0.5f, 0.11f);
            if (stage1)
                kit[white].SurfaceBox(Mathf.Pi, R, y0 + 0.30f * L, 0.14f, 0.56f * L, 0.09f);
            HExposedCaps(kit[HDarkInterior], R, y0, y1, hasAbove, hasBelow);
        });
    }

    private Node3D BuildTitanStage2Engine(PartDefinition d)
    {
        float L = (float)d.LengthM;
        const float titanRadius = 1.524f;
        bool interstage = VesselHasRole("titan_stage1_tank");
        var root = new Node3D { Name = "TitanStage2EngineBay" };
        return HAttach(root, "titan_lr91_bay_" + d.Id + (interstage ? "I" : "i"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            if (interstage)
            {
                // Interstage skirt with the "fire-in-the-hole" vents for stage-2 ignition.
                HCylinder(kit[TitanWhite], titanRadius, y0, y1, 64);
                for (int k = 0; k < 12; k++)
                    kit[HDarkInterior].SurfaceBox(Mathf.Tau * (k + 0.5f) / 12f, titanRadius,
                        0f, 0.34f, L * 0.55f, 0.012f);
                HBand(kit[HDarkMetal], titanRadius, y1 - 0.03f, 0.04f, 0.008f, 64);
                return;
            }
            var geo = kit[HDarkMetal];
            for (int k = 0; k < 8; k++)
            {
                float theta = Mathf.Tau * k / 8f;
                geo.Tube(Radial(theta) * 1.15f + Vector3.Up * (y1 - 0.02f),
                    Radial(theta + 0.4f) * 0.42f + Vector3.Up * (y1 - 0.75f), 0.035f, 5);
            }
            geo.Box(new Vector3(0.45f, y1 - 0.55f, 0f), new Vector3(0.32f, 0.40f, 0.30f));
            geo.Tube(new Vector3(0.5f, y1 - 0.75f, 0f), new Vector3(0.95f, y0 + 0.10f, 0f), 0.06f, 8);
            HDisk(kit[HDarkInterior], titanRadius * 0.98f, y1 - 0.01f, facingUp: false);
        });
    }

    private static Node3D BuildTitanInterstage(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = Mathf.Max(0.08f, (float)d.LengthM), R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "TitanInterstage" };
        return HAttach(root, "titan_interstage_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            HCylinder(kit[TitanWhite], R, -L * 0.5f, L * 0.5f, 64);
            HBand(kit[HDarkMetal], R, 0f, 0.03f, 0.008f, 64);
            HExposedCaps(kit[HDarkInterior], R, -L * 0.5f, L * 0.5f, hasAbove, hasBelow);
        });
    }

    private static Node3D BuildTitanStage1Aft(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "TitanStage1AftSkirt" };
        return HAttach(root, "titan_lr87_aft_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float skirtBottom = y0 + 0.95f;
            HRollPattern(kit, R, skirtBottom, skirtBottom + 0.95f, 8, 1, TitanWhite, HBlackPaint);
            HCylinder(kit[TitanWhite], R, skirtBottom + 0.95f, y1, 64);
            kit[HDarkInterior].Lathe(Prof(R, skirtBottom, R * 0.99f, skirtBottom + 0.02f), 64);
            HDisk(kit[HDarkInterior], R * 0.99f, skirtBottom + 0.05f, facingUp: false);
            HBand(kit[HDarkMetal], R, skirtBottom + 0.02f, 0.04f, 0.01f, 64);
            foreach (float x in new[] { -0.7f, 0.7f })
            {
                // Turbine exhaust duct alongside each LR-87 thrust chamber.
                var top = new Vector3(x * 1.05f, skirtBottom + 0.05f, 0.64f);
                var bottom = new Vector3(x * 1.12f, y0 + 0.30f, 0.70f);
                kit[HDarkMetal].Tube(top, bottom, 0.075f, 10);
                kit[HDarkInterior].Frustum(bottom, bottom + Vector3.Down * 0.10f, 0.075f, 0.095f, 10);
            }
        });
    }

    // ══ Gemini & Agena ════════════════════════════════════════════════════

    private static StandardMaterial3D GeminiShingles => HMetal("gemini_shingle",
        new Color(0.075f, 0.075f, 0.08f), 0.45f, 0.42f, "shingles", new Vector2(36f, 3.6f),
        normalScale: 0.9f, emission: 0.03f);
    private static StandardMaterial3D GeminiAdapterWhite => HPaint("gemini_adapter",
        new Color(0.88f, 0.88f, 0.85f), 0.5f, "ribs_sharp", new Vector2(110f, 1f), 0.7f);

    private static Node3D BuildGeminiNose(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "GeminiRendezvousNose" };
        return HAttach(root, "gemini_nose_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            var section = HMetal("gemini_rr", new Color(0.34f, 0.34f, 0.35f), 0.5f, 0.38f,
                "ribs", new Vector2(28f, 1f), 0.4f);
            float yDome = y0 + 0.62f * L;
            kit[section].Lathe(Prof(R, y0, 0.34f, yDome), 40);
            var radome = HPaint("radome", new Color(0.90f, 0.89f, 0.84f), 0.45f);
            var dome = new List<Vector2>();
            for (int i = 0; i <= 7; i++)
            {
                float a = Mathf.Pi * 0.5f * i / 7f;
                dome.Add(new Vector2(0.32f * Mathf.Cos(a), yDome + (y1 - yDome) * Mathf.Sin(a)));
            }
            kit[radome].Lathe(dome, 32);
            kit[HDarkMetal].Box(new Vector3(0f, y1 - 0.04f, 0.36f), new Vector3(0.05f, 0.26f, 0.04f));
        });
    }

    private Node3D BuildGeminiReentryModule(Part part)
    {
        var d = part.Definition;
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "GeminiReentryModule" };
        HAttach(root, "gemini_rm_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float rb = 0.76f * R;
            float coneBase = y0 + 0.02f, coneTop = y0 + 0.72f * L, rTop = 0.50f;
            kit[AblativeShield].Lathe(Prof(0f, y0 - 0.09f, 0.6f * rb, y0 - 0.06f, 0.95f * rb,
                y0 - 0.015f, rb, y0, rb, coneBase), 56);
            kit[GeminiShingles].Lathe(Prof(rb, coneBase, rTop, coneTop), 56);
            var metal = HDarkMetal;
            kit[metal].Lathe(Prof(rTop, coneTop, rTop + 0.02f, coneTop, rTop + 0.02f,
                coneTop + 0.03f, 0.49f, coneTop + 0.03f), 40, smoothDeg: 10f);
            kit[GeminiShingles].Lathe(Prof(0.49f, coneTop + 0.03f, 0.475f, y1), 40);
            float slope = (rTop - rb) / (coneTop - coneBase);
            float RadiusAt(float y) => rb + slope * (y - coneBase);
            float front = Mathf.Pi * 0.5f;
            float yw = y0 + 0.52f * L;
            foreach (float side in new[] { -0.36f, 0.36f })
            {
                kit[HGlass].SurfaceBox(front + side, RadiusAt(yw), yw, 0.30f, 0.34f, 0.012f, slope);
                kit[metal].SurfaceBox(front + side, RadiusAt(yw), yw, 0.36f, 0.40f, 0.006f, slope);
                // Hatch outlines framing each crew station.
                float yh = y0 + 0.28f * L;
                kit[metal].SurfaceBox(front + side, RadiusAt(yh), yh, 0.62f, 0.025f, 0.012f, slope);
            }
            // Re-entry control system thruster ports.
            for (int k = 0; k < 8; k++)
            {
                Vector3 dir = Radial(k * Mathf.Tau / 8f + Mathf.Pi / 8f);
                float y = y0 + 0.88f * L;
                kit[metal].Frustum(dir * 0.48f + Vector3.Up * y, dir * 0.55f + Vector3.Up * y, 0.025f, 0.04f, 8);
            }
        });

        float height = L / MetresPerUnit;
        var canopy = new Node3D
        {
            Name = "GeminiMainParachute",
            Position = Vector3.Up * (height * 0.5f + 3.0f),
            Visible = false,
        };
        var canopyMesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 3.8f, Height = 2.2f, RadialSegments = 48, Rings = 16 },
        };
        canopyMesh.SetSurfaceOverrideMaterial(0, Mat(new Color(0.90f, 0.42f, 0.20f), 0.0f, 0.78f));
        canopy.AddChild(canopyMesh);
        root.AddChild(canopy);
        _parachuteVisuals[part.InstanceId] = canopy;
        return root;
    }

    private static Node3D BuildGeminiAdapterCone(PartDefinition d, float bottomFraction,
        float topFraction, bool retro, bool hasAbove, bool hasBelow)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = retro ? "GeminiRetroSection" : "GeminiEquipmentSection" };
        return HAttach(root, "gemini_adapter_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float rb = R * bottomFraction, rt = R * topFraction;
            kit[GeminiAdapterWhite].Lathe(Prof(rb, y0, rt, y1), 72);
            var frame = HPaint("gemini_frame", new Color(0.78f, 0.78f, 0.75f), 0.5f);
            float slope = (rt - rb) / L;
            HBand(kit[frame], rt, y1 - 0.025f, 0.05f, 0.012f, 64);
            if (!retro)
            {
                HBand(kit[frame], Mathf.Lerp(rb, rt, 0.5f), 0f, 0.05f, 0.012f, 64);
                // Orbit attitude and manoeuvre thrusters around the equipment section.
                for (int k = 0; k < 8; k++)
                {
                    float theta = Mathf.Tau * k / 8f + Mathf.Pi / 8f;
                    float y = y0 + 0.30f * L;
                    float r = rb + slope * (y - y0);
                    kit[HDarkMetal].SurfaceBox(theta, r, y, 0.16f, 0.16f, 0.05f, slope);
                    kit[HDarkInterior].Frustum(Radial(theta) * (r + 0.04f) + Vector3.Up * y,
                        Radial(theta) * (r + 0.10f) + Vector3.Up * y, 0.03f, 0.05f, 8);
                }
            }
            HExposedCaps(kit[HDarkInterior], Mathf.Min(rb, rt), y0, y1, hasAbove, hasBelow);
        });
    }

    private static Node3D BuildAgenaDockingAdapter(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "AgenaTargetDockingAdapter" };
        return HAttach(root, "agena_tda_" + d.Id, flip: true, build: kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float bodyTop = y0 + 0.58f * L;
            var body = HMetal("agena_tda_body", new Color(0.70f, 0.70f, 0.68f), 0.5f, 0.36f,
                "ribs", new Vector2(32f, 1f), 0.4f, emission: 0.12f);
            var cone = HPaint("agena_cone", new Color(0.88f, 0.86f, 0.78f), 0.5f);
            HCylinder(kit[body], R * 0.92f, y0, bodyTop, 48);
            kit[cone].Lathe(Prof(R * 0.92f, bodyTop, R, y1), 48);
            var inner = HPaint("agena_cone_inner", new Color(0.55f, 0.53f, 0.48f), 0.6f, doubleSided: true);
            kit[inner].Lathe(Prof(R * 0.97f, y1, 0.22f, y0 + 0.30f * L), 48);
            kit[HDarkMetal].Torus(new Vector3(0f, y1, 0f), R * 0.985f, 0.025f, 48, 6);
            kit[HBlackPaint].SurfaceBox(Mathf.Pi * 0.5f, R * 0.98f, y1 - 0.12f, 0.12f, 0.26f, 0.03f, 0.3f);
            // Status display panel the Gemini crew read during approach.
            kit[HDarkMetal].SurfaceBox(Mathf.Pi * 0.5f, R * 0.92f, y0 + 0.32f * L, 0.34f, 0.24f, 0.06f);
            var lamp = HistMaterials.Get("agena_lamp", new Color(0.25f, 0.9f, 0.35f), 0f, 0.4f, emission: 1.2f);
            kit[lamp].SurfaceBox(Mathf.Pi * 0.5f, R * 0.92f + 0.06f, y0 + 0.32f * L, 0.08f, 0.08f, 0.01f);
        });
    }

    private static Node3D BuildAgenaBus(PartDefinition d, bool forwardCovered)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "AgenaTargetVehicle" };
        return HAttach(root, "agena_bus_" + d.Id + (forwardCovered ? "F" : "f"), flip: true, build: kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float forward = y1 - 1.4f, aft = y0 + 1.3f;
            var white = HPaint("agena_white", new Color(0.86f, 0.86f, 0.82f), 0.5f);
            var tank = HMetal("agena_tank", new Color(0.30f, 0.31f, 0.32f), 0.5f, 0.42f,
                albedo: "panels_fine", uv: new Vector2(8f, 1f / 1.2f), emission: 0.08f);
            HRollPattern(kit, R, forward, y1, 8, 0, white, HBlackPaint);
            HCylinder(kit[tank], R, aft, forward, 48);
            var seam = HDarkMetal;
            HBand(kit[seam], R, forward, 0.04f, 0.008f, 48);
            HBand(kit[seam], R, aft, 0.04f, 0.008f, 48);
            // Aft rack, main engine and the two secondary propulsion modules.
            kit[tank].Lathe(Prof(0.50f, y0 + 0.75f, R, aft), 48);
            HDisk(kit[HDarkInterior], 0.50f, y0 + 0.76f, facingUp: false, 32);
            var bell = HMetal("agena_bell", new Color(0.24f, 0.23f, 0.22f), 0.55f, 0.42f,
                "ribs", new Vector2(90f, 1f), 0.5f);
            kit[bell].Lathe(Prof(0.46f, y0, 0.30f, y0 + 0.45f, 0.14f, y0 + 0.80f), 32);
            kit[HDarkInterior].Lathe(Prof(0.135f, y0 + 0.78f, 0.29f, y0 + 0.45f, 0.45f, y0 + 0.01f), 32);
            foreach (float theta in new[] { 0f, Mathf.Pi })
            {
                // Secondary propulsion modules flush on the aft body, nozzles aft.
                kit[seam].SurfaceBox(theta, R, aft + 0.40f, 0.34f, 0.70f, 0.22f);
                Vector3 n = Radial(theta) * (R + 0.11f) + Vector3.Up * (aft + 0.05f);
                kit[HDarkInterior].Frustum(n, n + Vector3.Down * 0.12f, 0.03f, 0.05f, 8);
            }
            for (int k = 0; k < 2; k++)
            {
                Vector3 b = Radial(Mathf.Pi * 0.5f + k * Mathf.Pi) * (R + 0.02f) + Vector3.Up * (forward + 0.3f);
                kit[seam].Tube(b, b + Radial(Mathf.Pi * 0.5f + k * Mathf.Pi) * 0.35f + Vector3.Up * 0.25f, 0.012f, 4);
            }
            if (!forwardCovered) HDisk(kit[HDarkInterior], R, y1 - 0.02f, facingUp: true);
        });
    }

    // ══ Apollo / Saturn V ═════════════════════════════════════════════════

    private const float SaturnRadius = 5.03f;       // S-IC / S-II, 10.06 m
    private const float SivbRadius = 3.30f;         // S-IVB / IU / SLA base, 6.6 m
    private const float CsmRadius = 1.955f;         // CM / SM, 3.91 m

    private static StandardMaterial3D SaturnWhite => HPaint("saturn_white",
        new Color(0.87f, 0.87f, 0.84f), 0.55f, albedo: "panels_fine", uv: new Vector2(16f, 1f / 3.0f));
    private static StandardMaterial3D SaturnStringers => HPaint("saturn_stringers",
        new Color(0.86f, 0.86f, 0.83f), 0.55f, "ribs_sharp", new Vector2(160f, 1f), 0.55f);
    private static StandardMaterial3D ApolloAluminium => HMetal("apollo_alu",
        new Color(0.74f, 0.75f, 0.76f), 0.62f, 0.30f, albedo: "panels", uv: new Vector2(6f, 1f / 3.9f),
        emission: 0.16f);
    private static StandardMaterial3D ApolloMylar => HMetal("apollo_mylar",
        new Color(0.80f, 0.80f, 0.80f), 0.70f, 0.14f, albedo: "panels_fine",
        uv: new Vector2(1f, 1f / 0.32f), emission: 0.18f);
    private static StandardMaterial3D LesOrange => HPaint("les_orange",
        new Color(0.80f, 0.24f, 0.08f), 0.5f);

    private Node3D BuildApolloLaunchEscapeSystem(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "LaunchEscapeSystem" };
        return HAttach(root, "apollo_les_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f;
            var tower = kit[LesOrange];
            // Four-leg tower truss, legs anchored on the boost protective cover below.
            float trussTop = y0 + 0.30f * L;
            const int bays = 3;
            Vector3 Corner(int leg, float t)
            {
                float theta = Mathf.Pi * 0.25f + leg * Mathf.Pi * 0.5f;
                float r = Mathf.Lerp(1.05f, 0.44f, t);
                return Radial(theta) * r + Vector3.Up * Mathf.Lerp(y0 - 0.90f, trussTop, t);
            }
            for (int leg = 0; leg < 4; leg++)
            {
                tower.Tube(Corner(leg, 0f), Corner(leg, 1f), 0.055f, 8);
                for (int bay = 0; bay <= bays; bay++)
                {
                    float t0 = (float)bay / bays, t1 = (float)(bay + 1) / bays;
                    tower.Tube(Corner(leg, t0), Corner((leg + 1) % 4, t0), 0.035f, 6);
                    if (bay < bays)
                    {
                        tower.Tube(Corner(leg, t0), Corner((leg + 1) % 4, t1), 0.03f, 6);
                        tower.Tube(Corner((leg + 1) % 4, t0), Corner(leg, t1), 0.03f, 6);
                    }
                }
            }
            // Escape motor with four canted nozzles, tower-jettison motor, pitch motor, canards, Q-ball.
            var motor = HPaint("les_motor", new Color(0.82f, 0.82f, 0.79f), 0.5f);
            float caseBottom = trussTop + 0.04f * L, caseTop = y0 + 0.78f * L;
            kit[motor].Lathe(Prof(0.30f, trussTop - 0.02f, 0.46f, trussTop, 0.33f, caseBottom), 40);
            kit[motor].Lathe(Prof(0.33f, caseBottom, 0.33f, caseTop), 40);
            HBand(kit[HDarkMetal], 0.33f, caseBottom + 0.3f, 0.05f, 0.008f, 40);
            HBand(kit[HDarkMetal], 0.33f, caseTop - 0.05f, 0.05f, 0.008f, 40);
            for (int k = 0; k < 4; k++)
            {
                Vector3 dir = Radial(k * Mathf.Pi * 0.5f);
                Vector3 a = dir * 0.22f + Vector3.Up * (trussTop + 0.02f);
                Vector3 b = a + (dir * Mathf.Sin(0.61f) - Vector3.Up * Mathf.Cos(0.61f)) * 0.45f;
                kit[HDarkMetal].Frustum(a, b, 0.07f, 0.15f, 14);
                kit[HDarkInterior].Frustum(b + (a - b) * 0.02f, a, 0.145f, 0.06f, 14);
            }
            float jettisonTop = y0 + 0.86f * L;
            kit[motor].Lathe(Prof(0.33f, caseTop, 0.26f, caseTop + 0.10f, 0.26f, jettisonTop), 40, smoothDeg: 20f);
            for (int k = 0; k < 2; k++)
            {
                Vector3 dir = Radial(Mathf.Pi * 0.5f + k * Mathf.Pi);
                Vector3 a = dir * 0.24f + Vector3.Up * (caseTop + 0.10f);
                kit[HDarkMetal].Frustum(a, a + (dir * 0.8f - Vector3.Up * 0.6f).Normalized() * 0.22f, 0.05f, 0.09f, 10);
            }
            float canardBase = y0 + 0.90f * L;
            kit[LesOrange].Lathe(Prof(0.26f, jettisonTop, 0.22f, canardBase), 40);
            kit[motor].Lathe(Prof(0.22f, canardBase, 0.13f, y0 + 0.965f * L), 40);
            kit[HBlackPaint].Lathe(Prof(0.13f, y0 + 0.965f * L, 0.08f, y0 + 0.99f * L, 0f, y0 + L), 32);
            for (int k = 0; k < 2; k++)
            {
                float theta = k * Mathf.Pi;
                Vector3 radial = Radial(theta), tangent = Tangent(theta);
                Vector3 P(float s, float y) => radial * s + Vector3.Up * y;
                kit[motor].Slab(new[]
                {
                    P(0.20f, canardBase + 0.04f), P(0.16f, canardBase + 0.40f),
                    P(0.24f, canardBase + 0.40f), P(0.27f, canardBase + 0.04f),
                }, tangent, 0.02f);
            }
        });
    }

    private Node3D BuildApolloCommandModule(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        bool cover = VesselHasRole("launch_escape_system");
        bool probe = d.Id.Contains("csm107", StringComparison.Ordinal);
        var root = new Node3D { Name = cover ? "CommandModuleWithBoostCover" : "CommandModule" };
        return HAttach(root, "apollo_cm_" + d.Id + (cover ? "C" : "c"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            // Aft heat shield: shallow spherical segment with the toroidal shoulder.
            var shield = HPaint("cm_shield", new Color(0.36f, 0.30f, 0.22f), 0.8f);
            kit[shield].Lathe(Prof(0f, y0 - 0.24f, 0.9f * R, y0 - 0.14f, 0.97f * R, y0 - 0.08f,
                R, y0, R * 0.995f, y0 + 0.06f), 64, smoothDeg: 50f);
            float coneBase = y0 + 0.06f;
            float apexRadius = 0.62f;
            float coneTop = coneBase + (R - apexRadius) / Mathf.Tan(Mathf.DegToRad(33f));
            float slope = (apexRadius - R) / (coneTop - coneBase);
            float RadiusAt(float y) => R + slope * (y - coneBase);
            float front = Mathf.Pi * 0.5f;
            if (cover)
            {
                // Boost protective cover: white shell over the whole conical section.
                var bpc = HPaint("bpc_white", new Color(0.90f, 0.90f, 0.87f), 0.6f,
                    albedo: "panels_fine", uv: new Vector2(8f, 1f / 0.9f));
                kit[bpc].Lathe(Prof(R * 1.01f, coneBase, apexRadius + 0.05f, coneTop,
                    0.46f, coneTop + 0.30f, 0.44f, y1), 64, smoothDeg: 30f);
                float yw = coneBase + 0.95f;
                kit[HGlass].SurfaceBox(front - 0.5f, RadiusAt(yw) + 0.02f, yw, 0.22f, 0.22f, 0.02f, slope);
                return;
            }
            kit[ApolloMylar].Lathe(Prof(R * 0.995f, coneBase, apexRadius, coneTop), 72);
            var fwd = HMetal("cm_forward", new Color(0.70f, 0.70f, 0.70f), 0.6f, 0.22f, emission: 0.14f);
            kit[fwd].Lathe(Prof(apexRadius, coneTop, 0.52f, coneTop + 0.22f, 0.43f, coneTop + 0.38f), 56, smoothDeg: 50f);
            float tunnelTop = Mathf.Min(y1 - 0.05f, coneTop + 0.72f);
            kit[HDarkMetal].Lathe(Prof(0.43f, coneTop + 0.38f, 0.42f, coneTop + 0.40f,
                0.42f, tunnelTop, 0.30f, tunnelTop), 40, smoothDeg: 10f);
            if (probe)
            {
                kit[HDarkMetal].Frustum(Vector3.Up * tunnelTop, Vector3.Up * (y1 - 0.06f), 0.16f, 0.06f, 12);
                kit[HDarkMetal].Tube(Vector3.Up * (y1 - 0.10f), Vector3.Up * y1, 0.03f, 8);
            }
            // Rendezvous, side and hatch windows; RCS ports.
            foreach (float side in new[] { -0.28f, 0.28f })
            {
                float yr = coneTop - 0.55f;
                kit[HGlass].SurfaceBox(front + side, RadiusAt(yr), yr, 0.22f, 0.26f, 0.012f, slope);
            }
            foreach (float side in new[] { -1.15f, 1.15f })
            {
                float ys = coneBase + 0.80f;
                kit[HGlass].SurfaceBox(front + side, RadiusAt(ys), ys, 0.30f, 0.30f, 0.012f, slope);
            }
            float yh = coneBase + 0.95f;
            kit[HGlass].SurfaceBox(front, RadiusAt(yh), yh, 0.22f, 0.22f, 0.012f, slope);
            kit[HDarkMetal].SurfaceBox(front, RadiusAt(yh), yh, 0.80f, 0.85f, 0.008f, slope);
            for (int k = 0; k < 6; k++)
            {
                float y = coneBase + 0.35f + (k % 2) * 0.8f;
                kit[HBlackPaint].SurfaceBox(Mathf.Pi * (0.1f + k / 3f), RadiusAt(y), y, 0.10f, 0.10f, 0.02f, slope);
            }
        });
    }

    private Node3D BuildApolloServiceModule(PartDefinition d)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        bool stacked = VesselHasRole("sla_lunar_test_article") || VesselHasRole("sla_lunar_module");
        var root = new Node3D { Name = "ServiceModule" };
        return HAttach(root, "apollo_sm_" + d.Id + (stacked ? "S" : "s"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float fairingBottom = y1 - 0.45f, bodyBottom = y1 - 4.40f;
            var radiator = HPaint("sm_radiator", new Color(0.92f, 0.92f, 0.90f), 0.5f);
            HRollPattern(kit, R, fairingBottom, y1, 48, 0, radiator, ApolloAluminium);
            HCylinder(kit[ApolloAluminium], R, bodyBottom, fairingBottom, 72);
            // ECS radiator panels.
            foreach (float theta in new[] { 0.1f, Mathf.Pi + 0.1f })
                kit[radiator].Lathe(Prof(R + 0.008f, bodyBottom + 0.7f, R + 0.008f, bodyBottom + 2.6f),
                    10, theta - 0.38f, theta + 0.38f);
            for (int k = 0; k < 6; k++)
                kit[HDarkMetal].SurfaceBox(Mathf.Tau * k / 6f + 0.52f, R, (bodyBottom + fairingBottom) * 0.5f,
                    0.03f, fairingBottom - bodyBottom, 0.006f);
            // Four RCS quads.
            var quadMat = HMetal("rcs_quad", new Color(0.82f, 0.82f, 0.80f), 0.45f, 0.36f, emission: 0.12f);
            float yq = fairingBottom - 1.35f;
            for (int k = 0; k < 4; k++)
            {
                float theta = Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f;
                kit[quadMat].SurfaceBox(theta, R, yq, 0.62f, 1.05f, 0.10f);
                Vector3 radial = Radial(theta), tangent = Tangent(theta);
                Vector3 hub = radial * (R + 0.20f) + Vector3.Up * yq;
                kit[quadMat].Box(hub, new Basis(tangent, Vector3.Up, radial), new Vector3(0.22f, 0.22f, 0.20f));
                foreach (Vector3 dir in new[] { Vector3.Up, Vector3.Down, tangent, -tangent })
                    kit[HDarkMetal].Frustum(hub + dir * 0.10f, hub + dir * 0.30f, 0.035f, 0.07f, 10);
            }
            if (stacked)
            {
                // The SLA's forward fixed ring wraps the SPS nozzle on the pad.
                HCylinder(kit[SlaWhite], R, y0, bodyBottom, 64);
                HBand(kit[HDarkMetal], R, bodyBottom, 0.05f, 0.01f, 64);
                return;
            }
            var bulkhead = HPaint("sm_aft", new Color(0.12f, 0.12f, 0.13f), 0.7f, doubleSided: true);
            kit[bulkhead].Lathe(Prof(R, bodyBottom, R * 0.82f, bodyBottom - 0.16f,
                0.70f, bodyBottom - 0.24f, 0f, bodyBottom - 0.24f), 64);
            // Deployed S-band high-gain antenna on its boom.
            var antenna = HPaint("hga", new Color(0.88f, 0.88f, 0.86f), 0.45f, doubleSided: true);
            Vector3 boomBase = Radial(Mathf.Pi) * (R - 0.05f) + Vector3.Up * (bodyBottom + 0.10f);
            Vector3 boomTip = boomBase + new Vector3(-1.05f, -1.45f, 0f);
            kit[HDarkMetal].Tube(boomBase, boomTip, 0.05f, 6);
            Vector3 axis = new Vector3(-0.6f, -0.35f, 0.2f).Normalized();
            foreach (var offset in new[] { new Vector3(0, 0.42f, 0.42f), new Vector3(0, 0.42f, -0.42f),
                         new Vector3(0, -0.42f, 0.42f), new Vector3(0, -0.42f, -0.42f) })
                kit[antenna].Dish(boomTip + offset * 0.9f, axis, 0.40f, 0.13f, 14);
            kit[HDarkMetal].Box(boomTip, new Vector3(0.22f, 0.22f, 0.22f));
        });
    }

    private Node3D BuildApolloSpsMount(PartDefinition d)
    {
        float L = (float)d.LengthM;
        bool stacked = VesselHasRole("sla_lunar_test_article") || VesselHasRole("sla_lunar_module");
        var root = new Node3D { Name = "ServicePropulsionMount" };
        return HAttach(root, "apollo_sps_mount_" + d.Id + (stacked ? "S" : "s"), kit =>
        {
            if (stacked)
            {
                HCylinder(kit[SlaWhite], CsmRadius, -L * 0.5f, L * 0.5f, 64);
                return;
            }
            kit[HDarkMetal].Lathe(Prof(0.62f, L * 0.5f, 0.55f, L * 0.5f - 0.18f, 0.45f, L * 0.5f - 0.24f), 32);
        });
    }

    private Node3D BuildApolloCsmSeparationRing(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = Mathf.Max(0.06f, (float)d.LengthM), R = (float)d.DiameterM * 0.5f;
        bool stacked = VesselHasRole("sla_lunar_test_article") || VesselHasRole("sla_lunar_module");
        var root = new Node3D { Name = "CsmSeparationRing" };
        return HAttach(root, "apollo_csm_ring_" + d.Id + CapKey(hasAbove, hasBelow) + (stacked ? "S" : "s"), kit =>
        {
            HCylinder(kit[stacked ? SlaWhite : HDarkMetal], R, -L * 0.5f, L * 0.5f, 64);
            HBand(kit[HDarkMetal], R, 0f, Mathf.Min(0.03f, L * 0.5f), 0.008f, 64);
            HExposedCaps(kit[HDarkInterior], R, -L * 0.5f, L * 0.5f, hasAbove, hasBelow);
        });
    }

    private static StandardMaterial3D SlaWhite => HPaint("sla_white",
        new Color(0.86f, 0.86f, 0.82f), 0.62f, albedo: "panels_fine", uv: new Vector2(8f, 1f / 2.1f));

    private static Node3D BuildApolloSla(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SpacecraftLmAdapter" };
        return HAttach(root, "apollo_sla_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float split = y0 + 2.1f;
            kit[SlaWhite].Lathe(Prof(SivbRadius, y0, CsmRadius, y1), 72);
            kit[HDarkInterior].Lathe(Prof(CsmRadius - 0.03f, y1, SivbRadius - 0.03f, y0), 48);
            float slope = (CsmRadius - SivbRadius) / L;
            float RadiusAt(float y) => SivbRadius + slope * (y - y0);
            var seam = HPaint("sla_seam", new Color(0.50f, 0.50f, 0.48f), 0.6f);
            HBand(kit[seam], RadiusAt(split), split, 0.05f, 0.012f, 72);
            HBand(kit[seam], CsmRadius, y1 - 0.04f, 0.06f, 0.012f, 64);
            HBand(kit[seam], SivbRadius, y0 + 0.04f, 0.06f, 0.012f, 72);
            for (int k = 0; k < 4; k++)
            {
                float theta = Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f;
                float yMid = (split + y1) * 0.5f;
                kit[seam].SurfaceBox(theta, RadiusAt(yMid), yMid, 0.045f,
                    (y1 - split) * Mathf.Sqrt(1f + slope * slope), 0.012f, slope);
            }
        });
    }

    private static Node3D BuildSaturnInstrumentUnit(PartDefinition d, bool hasAbove)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "InstrumentUnit" };
        return HAttach(root, "saturn_iu_" + d.Id + (hasAbove ? "A" : "a"), kit =>
        {
            var skin = HPaint("iu_skin", new Color(0.80f, 0.80f, 0.77f), 0.5f, "ribs", new Vector2(220f, 1f), 0.45f);
            HCylinder(kit[skin], R, -L * 0.5f, L * 0.5f, 72);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], R, L * 0.5f - 0.03f, 0.05f, 0.01f, 72);
            HBand(kit[seam], R, -L * 0.5f + 0.03f, 0.05f, 0.01f, 72);
            for (int k = 0; k < 6; k++)
                kit[seam].SurfaceBox(Mathf.Tau * k / 6f + 0.3f, R, 0f, 0.55f, L * 0.45f, 0.01f);
            if (!hasAbove) HDisk(kit[HDarkInterior], R, L * 0.5f - 0.02f, facingUp: true, 64);
        });
    }

    private static Node3D BuildSaturnSivbTank(PartDefinition d, bool hasAbove, bool hasBelow)
    {
        float L = (float)d.LengthM, R = (float)d.DiameterM * 0.5f;
        var root = new Node3D { Name = "SivbStage" };
        return HAttach(root, "saturn_sivb_" + d.Id + CapKey(hasAbove, hasBelow), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            HCylinder(kit[SaturnWhite], R, y0, y1, 72);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], R, y1 - 1.0f, 0.05f, 0.008f, 72);
            HBand(kit[seam], R, y0 + 0.6f, 0.05f, 0.008f, 72);
            foreach (float theta in new[] { 0f, Mathf.Pi })
                kit[SaturnWhite].SurfaceBox(theta, R, 0f, 0.36f, L - 0.8f, 0.16f);
            HExposedCaps(kit[HDarkInterior], R, y0, y1, hasAbove, hasBelow);
        });
    }

    private static Node3D BuildSaturnSivbAft(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SivbAftSkirt" };
        return HAttach(root, "saturn_sivb_aft_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float skirtBottom = y0 + 0.9f;
            HRollPattern(kit, SivbRadius, skirtBottom, y1, 8, 0, SaturnWhite, HBlackPaint, Mathf.Pi / 8f);
            kit[HDarkInterior].Lathe(Prof(0.75f, skirtBottom + 0.65f, 1.6f, skirtBottom + 0.30f,
                SivbRadius * 0.99f, skirtBottom + 0.02f), 56);
            HBand(kit[HDarkMetal], SivbRadius, skirtBottom + 0.02f, 0.05f, 0.012f, 72);
            // Auxiliary propulsion system modules.
            var aps = HPaint("aps", new Color(0.80f, 0.80f, 0.78f), 0.5f);
            foreach (float theta in new[] { Mathf.Pi * 0.5f, Mathf.Pi * 1.5f })
            {
                kit[aps].SurfaceBox(theta, SivbRadius, skirtBottom + 0.55f, 1.0f, 1.65f, 0.55f);
                kit[HBlackPaint].SurfaceBox(theta, SivbRadius + 0.55f, skirtBottom - 0.18f, 1.0f, 0.18f, 0.01f);
            }
        });
    }

    private static Node3D BuildSaturnSiiSivbInterstage(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SiiSivbInterstage" };
        return HAttach(root, "saturn_sii_sivb_is_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            kit[SaturnStringers].Lathe(Prof(SaturnRadius, y0, SivbRadius, y1), 96);
            float slope = (SivbRadius - SaturnRadius) / L;
            float RadiusAt(float y) => SaturnRadius + slope * (y - y0);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], SivbRadius, y1 - 0.04f, 0.06f, 0.012f, 72);
            HBand(kit[seam], SaturnRadius, y0 + 0.04f, 0.06f, 0.012f, 96);
            // S-II retrorocket fairings.
            for (int k = 0; k < 4; k++)
            {
                float theta = Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f;
                float y = y0 + 1.25f;
                kit[SaturnWhite].SurfaceBox(theta, RadiusAt(y), y, 0.42f, 1.8f, 0.32f, slope);
            }
        });
    }

    private static Node3D BuildSaturnSiiTank(PartDefinition d, bool hasAbove)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SiiStage" };
        return HAttach(root, "saturn_sii_" + d.Id + (hasAbove ? "A" : "a"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            HCylinder(kit[SaturnWhite], SaturnRadius, y0, y1, 96);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            foreach (float y in new[] { y1 - 3.4f, y0 + 4.2f, 0.5f })
                HBand(kit[seam], SaturnRadius, y, 0.06f, 0.01f, 96);
            kit[SaturnWhite].SurfaceBox(0f, SaturnRadius, 0f, 0.55f, L - 1.2f, 0.26f);
            if (!hasAbove) HDisk(kit[HDarkInterior], SaturnRadius, y1 - 0.02f, facingUp: true, 96);
        });
    }

    private Node3D BuildSaturnSiiAft(PartDefinition d)
    {
        float L = (float)d.LengthM;
        bool interstage = VesselHasRole("sic_sii_interstage");
        var root = new Node3D { Name = "SiiAftSkirt" };
        return HAttach(root, "saturn_sii_aft_" + d.Id + (interstage ? "I" : "i"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float skirtBottom = interstage ? y0 : y0 + 1.9f;
            HCylinder(kit[SaturnStringers], SaturnRadius, skirtBottom, y1, 96);
            if (interstage) return;
            HBand(kit[HDarkMetal], SaturnRadius, skirtBottom + 0.03f, 0.06f, 0.012f, 96);
            HDisk(kit[HDarkInterior], SaturnRadius * 0.99f, skirtBottom + 0.05f, facingUp: false, 96);
        });
    }

    private static Node3D BuildSaturnSicSiiInterstage(PartDefinition d, bool hasAbove)
    {
        float L = (float)d.LengthM;
        int ullage = d.Id.Contains("as503", StringComparison.Ordinal) ? 8 : 4;
        var root = new Node3D { Name = "SicSiiInterstage" };
        return HAttach(root, "saturn_sic_sii_is_" + d.Id + (hasAbove ? "A" : "a"), kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            HCylinder(kit[SaturnStringers], SaturnRadius, y0, y1, 96);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], SaturnRadius, y1 - 0.05f, 0.08f, 0.012f, 96);
            HBand(kit[seam], SaturnRadius, y0 + 0.05f, 0.08f, 0.012f, 96);
            // S-II ullage motors in their fairings (eight on AS-503, four from AS-504 on).
            var pod = HPaint("ullage_pod", new Color(0.80f, 0.80f, 0.77f), 0.5f);
            for (int k = 0; k < ullage; k++)
            {
                float theta = Mathf.Tau * (k + 0.5f) / ullage;
                Vector3 c = Radial(theta) * (SaturnRadius + 0.20f);
                kit[pod].Lathe(Prof(0.12f, y0 + 0.35f, 0.24f, y0 + 0.55f, 0.24f, y0 + 2.2f,
                    0.12f, y0 + 2.7f, 0f, y0 + 2.85f), 16, center: c, smoothDeg: 30f);
                kit[HDarkMetal].Frustum(c + Vector3.Up * (y0 + 0.40f), c + Vector3.Up * (y0 + 0.20f), 0.08f, 0.12f, 10);
            }
            if (!hasAbove) HDisk(kit[HDarkInterior], SaturnRadius, y1 - 0.02f, facingUp: true, 96);
        });
    }

    private static Node3D BuildSaturnSicTank(PartDefinition d, bool hasAbove)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SicStage" };
        float y0 = -L * 0.5f, y1 = L * 0.5f;
        float skirtBottom = y1 - 3.0f, loxBottom = y1 - 16.5f, intertankBottom = y1 - 23.2f;
        HAttach(root, "saturn_sic_" + d.Id + (hasAbove ? "A" : "a"), kit =>
        {
            var white = SaturnWhite;
            HCylinder(kit[SaturnStringers], SaturnRadius, skirtBottom, y1, 96);
            HCylinder(kit[white], SaturnRadius, loxBottom, skirtBottom, 96);
            HRollPattern(kit, SaturnRadius, intertankBottom, loxBottom, 8, 1, SaturnStringers, HBlackPaint);
            HRollPattern(kit, SaturnRadius, y0, intertankBottom, 8, 0, white, HBlackPaint);
            var seam = HPaint("seam_grey", new Color(0.58f, 0.58f, 0.56f), 0.5f);
            HBand(kit[seam], SaturnRadius, skirtBottom, 0.06f, 0.01f, 96);
            HBand(kit[HBlackPaint], SaturnRadius, y1 - 0.20f, 0.25f, 0.004f, 96);
            HBand(kit[seam], SaturnRadius, loxBottom, 0.06f, 0.01f, 96);
            HBand(kit[seam], SaturnRadius, intertankBottom, 0.06f, 0.01f, 96);
            if (!hasAbove) HDisk(kit[HDarkInterior], SaturnRadius, y1 - 0.02f, facingUp: true, 96);
        });
        AddVerticalMarking(root, "UNITED STATES", Mathf.Pi * 0.5f, SaturnRadius + 0.01f,
            (skirtBottom + loxBottom) * 0.5f, 1.55f, new Color(0.04f, 0.04f, 0.045f));
        return root;
    }

    private static Node3D BuildSaturnSicThrustSection(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "SicThrustSection" };
        return HAttach(root, "saturn_sic_aft_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            float heatShield = y0 + 3.3f;
            HCylinder(kit[SaturnStringers], SaturnRadius, heatShield, y1, 96);
            HBand(kit[HDarkMetal], SaturnRadius, heatShield + 0.03f, 0.06f, 0.012f, 96);
            HDisk(kit[HDarkInterior], SaturnRadius * 0.99f, heatShield + 0.05f, facingUp: false, 96);
            var white = SaturnWhite;
            for (int k = 0; k < 4; k++)
            {
                float theta = Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f;
                Vector3 c = Radial(theta) * 4.62f;
                // Conical engine fairing around each outboard F-1.
                kit[white].Lathe(Prof(1.95f, y0 + 1.6f, 1.80f, y0 + 2.5f, 1.35f, y0 + 3.7f,
                    0.80f, y0 + 4.9f, 0.20f, y1 - 0.15f), 32, center: c, smoothDeg: 30f);
                kit[HDarkInterior].Lathe(Prof(1.92f, y0 + 2.6f, 1.92f, y0 + 1.61f), 32, center: c);
                // Stabilising fin on the outboard face of the fairing.
                Vector3 radial = Radial(theta);
                Vector3 P(float s, float y) => radial * s + Vector3.Up * y;
                kit[white].Slab(new[]
                {
                    P(6.50f, y0 + 1.6f), P(5.40f, y0 + 4.9f), P(8.25f, y0 + 2.45f), P(8.25f, y0 + 1.45f),
                }, Tangent(theta), 0.30f);
                // Retrorocket pair on the fairing nose.
                foreach (float side in new[] { -0.45f, 0.45f })
                {
                    Vector3 rc = c + radial * 0.55f + Tangent(theta) * side;
                    kit[white].Lathe(Prof(0.16f, y0 + 4.4f, 0.16f, y0 + 5.2f, 0f, y0 + 5.55f), 12,
                        center: new Vector3(rc.X, 0f, rc.Z), smoothDeg: 30f);
                }
            }
        });
    }

    // ── Lunar Module ──────────────────────────────────────────────────────

    private static StandardMaterial3D LmGold => HMetal("lm_gold", new Color(0.82f, 0.60f, 0.20f),
        0.62f, 0.30f, "crinkle", new Vector2(3f, 1f / 1.6f), 0.9f, "foil_albedo", emission: 0.18f,
        doubleSided: true);
    private static StandardMaterial3D LmAmber => HMetal("lm_amber", new Color(0.36f, 0.25f, 0.08f),
        0.55f, 0.35f, "crinkle", new Vector2(2f, 1f / 1.2f), 0.8f, "foil_albedo", emission: 0.10f);
    private static StandardMaterial3D LmGrey => HMetal("lm_grey", new Color(0.46f, 0.45f, 0.41f),
        0.30f, 0.55f, emission: 0.03f);
    private static StandardMaterial3D LmBlack => HPaint("lm_black", new Color(0.05f, 0.05f, 0.055f),
        0.5f, "crinkle", new Vector2(2f, 1f), 0.22f);

    private static Node3D BuildLmAscentStage(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "LunarModuleAscentStage" };
        return HAttach(root, "lm_ascent_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f;
            var grey = LmGrey;
            var black = LmBlack;
            // Midsection and the side propellant-tank bulges.
            kit[grey].Box(new Vector3(0f, y0 + 0.60f, -0.15f), new Vector3(2.5f, 1.2f, 1.9f));
            foreach (float x in new[] { -1.42f, 1.42f })
                kit[black].Box(new Vector3(x, y0 + 0.85f, -0.30f), new Vector3(0.55f, 1.05f, 1.25f));
            // Crew compartment with the faceted, back-raked front face.
            kit[grey].Box(new Vector3(0f, y0 + 1.95f, 0.15f), new Vector3(2.30f, 1.55f, 1.75f));
            var face = new Basis(Vector3.Right, -0.35f);
            kit[black].Box(new Vector3(0f, y0 + 2.20f, 1.03f), face, new Vector3(2.05f, 1.05f, 0.10f));
            Vector3 n = face.Column2;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 c = new Vector3(side * 0.45f, y0 + 2.28f, 1.10f) + n * 0.03f;
                kit[HGlass].Slab(new[]
                {
                    c + face.Column0 * (-0.32f * side) + face.Column1 * 0.22f,
                    c + face.Column0 * (0.30f * side) + face.Column1 * 0.22f,
                    c + face.Column0 * (0.30f * side) + face.Column1 * -0.26f,
                }, n, 0.02f);
            }
            kit[grey].Box(new Vector3(0f, y0 + 1.30f, 1.05f), new Vector3(0.85f, 0.85f, 0.06f));
            // Aft equipment bay and upper deck.
            kit[black].Box(new Vector3(0f, y0 + 2.05f, -1.30f), new Vector3(2.0f, 1.45f, 0.85f));
            kit[grey].Box(new Vector3(0f, y0 + 2.85f, -0.05f), new Vector3(1.75f, 0.40f, 1.65f));
            // Docking tunnel and drogue ring.
            kit[grey].Lathe(Prof(0.45f, y0 + 3.0f, 0.42f, y0 + 3.08f, 0.42f, y0 + 3.55f,
                0.36f, y0 + 3.62f), 32, smoothDeg: 15f);
            kit[HDarkInterior].Lathe(Prof(0.35f, y0 + 3.62f, 0.10f, y0 + 3.40f), 24);
            kit[black].Box(new Vector3(0f, y0 + 3.64f, 0f), new Vector3(0.40f, 0.04f, 0.04f));
            // RCS quads on outriggers at the four corners.
            for (int k = 0; k < 4; k++)
            {
                float theta = Mathf.Pi * 0.25f + k * Mathf.Pi * 0.5f;
                Vector3 radial = Radial(theta), tangent = Tangent(theta);
                Vector3 hub = radial * 1.85f + Vector3.Up * (y0 + 2.45f);
                kit[grey].Tube(radial * 1.05f + Vector3.Up * (y0 + 2.2f), hub, 0.05f, 6);
                kit[grey].Box(hub, new Basis(tangent, Vector3.Up, radial), new Vector3(0.26f, 0.26f, 0.22f));
                foreach (Vector3 dir in new[] { Vector3.Up, Vector3.Down, tangent, -tangent })
                    kit[HDarkMetal].Frustum(hub + dir * 0.12f, hub + dir * 0.34f, 0.04f, 0.08f, 10);
                kit[HBlackPaint].Box(hub - Vector3.Up * 0.55f + radial * 0.05f,
                    new Basis(tangent, Vector3.Up, radial), new Vector3(0.36f, 0.04f, 0.30f));
            }
            // Rendezvous radar, steerable S-band and VHF antennas.
            var dish = HPaint("lm_dish", new Color(0.86f, 0.86f, 0.84f), 0.45f, doubleSided: true);
            kit[grey].Tube(new Vector3(0f, y0 + 3.05f, 0.55f), new Vector3(0f, y0 + 3.35f, 0.75f), 0.04f, 6);
            kit[dish].Dish(new Vector3(0f, y0 + 3.35f, 0.75f), new Vector3(0f, 0.5f, 1f), 0.30f, 0.10f, 14);
            Vector3 sbandBase = new(0.85f, y0 + 3.05f, -0.75f);
            Vector3 sbandTip = sbandBase + new Vector3(0.35f, 0.55f, 0f);
            kit[grey].Tube(sbandBase, sbandTip, 0.035f, 6);
            kit[dish].Dish(sbandTip, new Vector3(0.4f, 1f, 0.3f), 0.33f, 0.10f, 14);
            foreach (float x in new[] { -0.75f, 0.75f })
                kit[HDarkMetal].Tube(new Vector3(x, y0 + 2.8f, -1.25f), new Vector3(x * 1.3f, y0 + 3.6f, -1.45f), 0.015f, 4);
        });
    }

    private static Node3D BuildLmAscentMidsection(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "LunarModuleAscentEngineBay" };
        return HAttach(root, "lm_aps_bay_" + d.Id, kit =>
            kit[LmBlack].Box(Vector3.Zero, new Vector3(2.4f, L, 1.8f)));
    }

    private static Node3D BuildLmStagePlate(PartDefinition d)
    {
        float L = Mathf.Max(0.05f, (float)d.LengthM);
        var root = new Node3D { Name = "LunarModuleInterstagePlate" };
        return HAttach(root, "lm_plate_" + d.Id, kit =>
            kit[HDarkMetal].Box(Vector3.Zero, new Vector3(2.2f, L, 2.2f)));
    }

    private static Node3D BuildLmDescentStage(PartDefinition d)
    {
        float L = (float)d.LengthM;
        var root = new Node3D { Name = "LunarModuleDescentStage" };
        return HAttach(root, "lm_descent_" + d.Id, kit =>
        {
            float y0 = -L * 0.5f, y1 = L * 0.5f;
            const float body = 2.05f;
            float a0 = Mathf.Pi / 8f;
            var gold = LmGold;
            kit[gold].Lathe(Prof(body, y0 + 0.45f, body, y1 - 0.02f), 8, a0, a0 + Mathf.Tau, flatAround: true);
            kit[LmBlack].Lathe(Prof(body, y1 - 0.02f, body * 0.5f, y1, 0f, y1), 8, a0, a0 + Mathf.Tau, flatAround: true);
            kit[gold].Lathe(Prof(0.95f, y0, 1.35f, y0 + 0.08f, body, y0 + 0.45f), 8, a0, a0 + Mathf.Tau, flatAround: true);
            HDisk(kit[HDarkInterior], 0.95f, y0 + 0.01f, facingUp: false, 16);
            // Black thermal panels on the four leg-mount faces.
            for (int k = 0; k < 4; k++)
                kit[LmAmber].SurfaceBox(k * Mathf.Pi * 0.5f, body * Mathf.Cos(Mathf.Pi / 8f), 0.05f,
                    1.2f, L - 0.75f, 0.01f);

            // Cantilever landing gear: primary strut, two secondaries, footpad; ladder on +Z.
            float footY = y0 - 0.25f + 0.12f;
            var strut = HMetal("lm_strut", new Color(0.74f, 0.70f, 0.58f), 0.5f, 0.35f, emission: 0.10f);
            for (int k = 0; k < 4; k++)
            {
                float theta = k * Mathf.Pi * 0.5f;
                Vector3 radial = Radial(theta), tangent = Tangent(theta);
                Vector3 top = radial * (body * 0.95f) + Vector3.Up * (y1 - 0.25f);
                Vector3 foot = radial * 4.25f + Vector3.Up * (footY + 0.10f);
                kit[gold].Tube(top, foot, 0.08f, 8);
                Vector3 knee = top.Lerp(foot, 0.55f);
                foreach (float side in new[] { -0.75f, 0.75f })
                    kit[strut].Tube(radial * (body * 0.92f) + tangent * side + Vector3.Up * (y0 + 0.55f), knee, 0.045f, 6);
                kit[strut].Tube(radial * (body * 0.92f) + Vector3.Up * (y0 + 0.50f), knee, 0.04f, 6);
                var pad = new Vector3(foot.X, 0f, foot.Z);
                kit[strut].Lathe(Prof(0f, footY - 0.12f, 0.40f, footY - 0.09f, 0.47f, footY + 0.02f,
                    0.44f, footY + 0.03f), 20, center: pad, smoothDeg: 50f);
                kit[HDarkInterior].Lathe(Prof(0.44f, footY + 0.03f, 0f, footY - 0.06f), 20, center: pad);
                if (k == 1)
                {
                    foreach (float side in new[] { -0.26f, 0.26f })
                        kit[strut].Tube(top + tangent * side + radial * 0.10f, foot + tangent * side + Vector3.Up * 0.35f, 0.022f, 4);
                    for (int rung = 1; rung < 9; rung++)
                    {
                        Vector3 p = (top + radial * 0.10f).Lerp(foot + Vector3.Up * 0.35f, rung / 9f);
                        kit[strut].Tube(p - tangent * 0.26f, p + tangent * 0.26f, 0.015f, 4);
                    }
                    kit[strut].Box(top + radial * 0.35f + Vector3.Up * 0.02f,
                        new Basis(tangent, Vector3.Up, radial), new Vector3(0.95f, 0.05f, 0.55f));
                }
            }
        });
    }

    private static Node3D BuildLmDescentEngineMount(PartDefinition d)
    {
        var root = new Node3D { Name = "LunarModuleDescentEngineMount" };
        return HAttach(root, "lm_dps_mount_" + d.Id, kit =>
            HBand(kit[HDarkMetal], 0.80f, (float)d.LengthM * 0.5f - 0.03f, 0.05f, 0.03f, 24));
    }

    // ── Engine bells ──────────────────────────────────────────────────────

    private sealed record HistBellSpec(float ExitDiameter, float Length, float ThroatRadius,
        float ExtensionFraction, Color Upper, Color Lower, int Tubes, bool Manifold, int DarkBands);

    private static HistBellSpec? GetHistoricalBellSpec(string engineModelId)
    {
        string id = engineModelId.ToLowerInvariant();
        var dark = new Color(0.17f, 0.17f, 0.18f);
        if (id.StartsWith("f1-")) return new(3.76f, 3.9f, 0.48f, 0.58f, new Color(0.36f, 0.35f, 0.34f), new Color(0.07f, 0.07f, 0.075f), 178, true, 0);
        if (id.StartsWith("j2-")) return new(1.96f, 3.0f, 0.24f, 1.0f, new Color(0.42f, 0.42f, 0.42f), new Color(0.42f, 0.42f, 0.42f), 180, false, 2);
        if (id.StartsWith("titan2-lr87")) return new(1.14f, 2.0f, 0.17f, 1.0f, dark, dark, 120, false, 1);
        if (id.StartsWith("titan2-lr91")) return new(1.60f, 2.4f, 0.16f, 0.35f, dark, new Color(0.52f, 0.54f, 0.58f), 110, false, 0);
        if (id.StartsWith("atlas-lr89")) return new(1.12f, 1.75f, 0.16f, 1.0f, dark, dark, 110, false, 1);
        if (id.StartsWith("atlas-lr105")) return new(1.24f, 2.0f, 0.14f, 1.0f, dark, dark, 110, false, 1);
        if (id.StartsWith("redstone-a7")) return new(0.86f, 1.30f, 0.16f, 1.0f, dark, dark, 80, false, 0);
        if (id.StartsWith("apollo-sps")) return new(2.40f, 3.0f, 0.30f, 0.25f, new Color(0.22f, 0.22f, 0.23f), new Color(0.55f, 0.55f, 0.56f), 0, false, 3);
        if (id.StartsWith("lm-dps")) return new(1.50f, 1.55f, 0.22f, 0.40f, dark, new Color(0.38f, 0.35f, 0.30f), 0, false, 0);
        if (id.StartsWith("lm-aps")) return new(0.86f, 0.90f, 0.15f, 1.0f, dark, dark, 0, false, 0);
        return null;
    }

    private static ArrayMesh? BuildHistoricalBellMesh(string key, HistBellSpec spec)
    {
        if (HistMeshCache.TryGetValue("bell_" + key, out var cached)) return cached;
        var kit = new HistMeshKit();
        float exitR = spec.ExitDiameter * 0.5f;
        const int samples = 14;
        float RadiusAt(float t) => spec.ThroatRadius + (exitR - spec.ThroatRadius) * Mathf.Pow(t, 0.62f);
        float YAt(float t) => spec.Length * (1f - t);
        var upperPts = new List<Vector2>();
        var lowerPts = new List<Vector2>();
        float split = 1f - spec.ExtensionFraction;
        for (int i = samples; i >= 0; i--)
        {
            float t = (float)i / samples;
            var p = new Vector2(RadiusAt(t), YAt(t));
            if (t >= split) lowerPts.Add(p);
            if (t <= split) upperPts.Add(p);
        }
        if (split < 1f && lowerPts.Count > 0 && (upperPts.Count == 0 || upperPts[0] != new Vector2(RadiusAt(split), YAt(split))))
            upperPts.Insert(0, new Vector2(RadiusAt(split), YAt(split)));
        if (lowerPts.Count > 0 && lowerPts[^1].Y < YAt(split))
            lowerPts.Add(new Vector2(RadiusAt(split), YAt(split)));

        Material upper = spec.Tubes > 0
            ? HMetal($"bell_up_{key}", spec.Upper, 0.55f, 0.40f, "ribs", new Vector2(spec.Tubes, 1f), 0.8f, emission: 0.05f)
            : HMetal($"bell_up_{key}", spec.Upper, 0.55f, 0.40f, emission: 0.05f);
        Material lower = HMetal($"bell_low_{key}", spec.Lower, 0.55f, 0.38f, emission: 0.08f);
        // Outer contour: exit lip upward to the throat.
        if (lowerPts.Count > 1) kit[spec.ExtensionFraction >= 0.999f ? upper : lower].Lathe(lowerPts, 40, smoothDeg: 60f);
        if (upperPts.Count > 1) kit[upper].Lathe(upperPts, 40, smoothDeg: 60f);
        // Sooty interior (reversed profile faces inward) and the exit lip.
        var inner = new List<Vector2>();
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            inner.Add(new Vector2(RadiusAt(t) * 0.965f, YAt(t) + 0.01f));
        }
        kit[HPaint("bell_soot", new Color(0.03f, 0.028f, 0.026f), 0.9f)].Lathe(inner, 40, smoothDeg: 60f);
        kit[lower].Lathe(Prof(exitR * 0.965f, 0.01f, exitR * 0.965f, 0f, exitR * 1.01f, 0f,
            exitR * 1.01f, 0.04f), 40, smoothDeg: 10f);
        var band = HPaint("bell_band", new Color(0.05f, 0.05f, 0.05f), 0.6f);
        for (int b = 0; b < spec.DarkBands; b++)
        {
            float t = 0.35f + 0.55f * (b + 1f) / (spec.DarkBands + 1f);
            HBand(kit[band], RadiusAt(t), YAt(t), spec.Length * 0.025f, 0.012f, 40);
        }
        if (spec.Manifold)
        {
            // F-1 turbine exhaust manifold torus where the nozzle extension begins.
            float t = split;
            kit[HMetal("f1_manifold", new Color(0.55f, 0.55f, 0.53f), 0.5f, 0.4f, emission: 0.08f)]
                .Torus(new Vector3(0f, YAt(t), 0f), RadiusAt(t) + 0.10f, 0.13f, 40, 8);
        }
        // Thrust chamber / powerhead above the throat.
        kit[HDarkMetal].Lathe(Prof(spec.ThroatRadius, spec.Length, spec.ThroatRadius * 1.6f,
            spec.Length + spec.ThroatRadius * 1.2f, spec.ThroatRadius * 1.4f,
            spec.Length + spec.ThroatRadius * 2.4f, 0f, spec.Length + spec.ThroatRadius * 2.6f), 20);
        var mesh = kit.Build();
        HistMeshCache["bell_" + key] = mesh;
        return mesh;
    }

    /// <summary>Contoured, reference-sized nozzle for a known historical engine model.</summary>
    private bool TryAddHistoricalEngineBell(string instanceId, Vector3 exitPosition,
        string engineModelId)
    {
        var spec = GetHistoricalBellSpec(engineModelId);
        if (spec == null) return false;
        string key = engineModelId.Split('-')[0] + "_" + engineModelId.Split('-').ElementAtOrDefault(1);
        var mesh = BuildHistoricalBellMesh(key, spec);
        if (mesh == null) return false;
        AddChild(new MeshInstance3D
        {
            Name = $"Bell_{instanceId.Replace(':', '_')}",
            Mesh = mesh,
            Position = exitPosition,
            Scale = Vector3.One / MetresPerUnit,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        });
        return true;
    }
}
