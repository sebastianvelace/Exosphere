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
    private MeshInstance3D? _hullMesh; // for reentry heat glow
    private readonly List<MeshInstance3D> _plumes = new(); // engine exhaust cones

    private static StandardMaterial3D? _fallbackMaterial;
    private static StandardMaterial3D GetFallback()
    {
        if (_fallbackMaterial != null) return _fallbackMaterial;
        _fallbackMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.2f, 0.2f) };
        return _fallbackMaterial;
    }

    public void BuildFromVessel(Vessel vessel)
    {
        TargetVessel = vessel;
        ClearNodes();

        bool hasEngine = vessel.Parts.Parts.Any(p => p.Definition.Category == PartCategory.Engine);
        if (hasEngine)
            BuildStarship(vessel);
        else
            BuildGenericVessel(vessel);
    }

    // ── SpaceX Starship visual ────────────────────────────────────────────
    // Y-up, nose at top:
    //   y=+12  nose tip
    //   y=+7   nose base / body top
    //   y= 0   body centre (CoM)
    //   y=-7   body bottom / skirt top
    //   y=-9   skirt bottom / engine bay
    //   y=-11  vacuum Raptor nozzle exits

    private void BuildStarship(Vessel vessel)
    {
        var steelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.86f, 0.88f),
            Metallic    = 0.92f,
            Roughness   = 0.18f,
        };
        var tileMat = new StandardMaterial3D   // heat-shield belly tiles
        {
            AlbedoColor = new Color(0.09f, 0.09f, 0.11f),
            Metallic    = 0.04f,
            Roughness   = 0.94f,
        };
        var darkSteelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.50f, 0.50f, 0.53f),
            Metallic    = 0.88f,
            Roughness   = 0.32f,
        };
        var engineMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.18f, 0.20f),
            Metallic    = 0.82f,
            Roughness   = 0.38f,
        };

        // Body — two halves with different materials
        _hullMesh = AddMesh("BodyUpper",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            steelMat, new Vector3(0, 3.5f, 0));

        AddMesh("BodyLower",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = 7f, RadialSegments = 48 },
            tileMat, new Vector3(0, -3.5f, 0));

        // Nose cone
        AddMesh("Nose",
            new CylinderMesh { TopRadius = 0.04f, BottomRadius = 1.15f, Height = 5f, RadialSegments = 48 },
            steelMat, new Vector3(0, 9.5f, 0));

        // Forward canards (two small delta wings near the top)
        var canardMesh = new BoxMesh { Size = new Vector3(0.12f, 1.6f, 2.6f) };
        AddMesh("CanardL", canardMesh, darkSteelMat, new Vector3(-1.23f, 5.5f, 0));
        AddMesh("CanardR", canardMesh, darkSteelMat, new Vector3( 1.23f, 5.5f, 0));
        var canardRoot = new BoxMesh { Size = new Vector3(0.18f, 2.0f, 1.0f) };
        AddMesh("CanardRootL", canardRoot, steelMat, new Vector3(-1.16f, 5.5f, 0));
        AddMesh("CanardRootR", canardRoot, steelMat, new Vector3( 1.16f, 5.5f, 0));

        // Aft body flaps (two large control surfaces at the base)
        var flapMesh = new BoxMesh { Size = new Vector3(0.14f, 5.5f, 4.6f) };
        AddMesh("FlapL", flapMesh, tileMat, new Vector3(-1.23f, -4.5f, 0));
        AddMesh("FlapR", flapMesh, tileMat, new Vector3( 1.23f, -4.5f, 0));
        var flapRoot = new BoxMesh { Size = new Vector3(0.20f, 5.5f, 1.2f) };
        AddMesh("FlapRootL", flapRoot, tileMat, new Vector3(-1.16f, -4.5f, 0));
        AddMesh("FlapRootR", flapRoot, tileMat, new Vector3( 1.16f, -4.5f, 0));

        // Engine skirt
        AddMesh("Skirt",
            new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.08f, Height = 2f, RadialSegments = 48 },
            darkSteelMat, new Vector3(0, -8f, 0));

        // 3 center vacuum Raptors — longer bell, inner ring
        const float vacRingR = 0.38f;
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            AddMesh($"RapVac{i}",
                new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.44f, Height = 2.1f, RadialSegments = 20 },
                engineMat, new Vector3(vacRingR * Mathf.Cos(a), -10.05f, vacRingR * Mathf.Sin(a)));
        }

        // 3 outer sea-level Raptors — shorter, wider bell, 60° offset from vacuum
        const float slRingR = 0.72f;
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            AddMesh($"RapSL{i}",
                new CylinderMesh { TopRadius = 0.21f, BottomRadius = 0.33f, Height = 1.4f, RadialSegments = 20 },
                engineMat, new Vector3(slRingR * Mathf.Cos(a), -9.7f, slRingR * Mathf.Sin(a)));
        }

        foreach (var part in vessel.Parts.Parts)
            _partNodes[part.InstanceId] = _hullMesh;

        // Engine exhaust plumes — one per Raptor (6 total)
        SpawnPlumes(vacRingR, slRingR, engineMat);
    }

    private void SpawnPlumes(float vacRingR, float slRingR, StandardMaterial3D engineMat)
    {
        var plumeMat = new StandardMaterial3D
        {
            AlbedoColor              = new Color(1.0f, 0.85f, 0.5f, 0.0f),
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled          = true,
            Emission                 = new Color(1.0f, 0.6f, 0.1f),
            EmissionEnergyMultiplier = 3.0f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
        };

        // 3 vacuum Raptor plumes
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f;
            var cone = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.55f, Height = 5.0f, RadialSegments = 12 };
            var node = new MeshInstance3D { Name = $"PlumeVac{i}", Mesh = cone, Visible = false };
            node.SetSurfaceOverrideMaterial(0, (StandardMaterial3D)plumeMat.Duplicate());
            // Base of plume at nozzle exit (y=-11.05), points downward (-Y)
            node.Position = new Vector3(vacRingR * Mathf.Cos(a), -12.55f, vacRingR * Mathf.Sin(a));
            AddChild(node);
            _plumes.Add(node);
        }

        // 3 sea-level Raptor plumes (slightly shorter)
        var plumeSLMat = (StandardMaterial3D)plumeMat.Duplicate();
        plumeSLMat.Emission = new Color(1.0f, 0.7f, 0.2f);
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.094395f + 1.047198f;
            var cone = new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.42f, Height = 3.5f, RadialSegments = 12 };
            var node = new MeshInstance3D { Name = $"PlumeSL{i}", Mesh = cone, Visible = false };
            node.SetSurfaceOverrideMaterial(0, (StandardMaterial3D)plumeSLMat.Duplicate());
            node.Position = new Vector3(slRingR * Mathf.Cos(a), -11.45f, slRingR * Mathf.Sin(a));
            AddChild(node);
            _plumes.Add(node);
        }
    }

    private MeshInstance3D AddMesh(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }

    // ── Visual updates (plumes + heat glow) ──────────────────────────────

    public override void _Process(double delta)
    {
        if (TargetVessel == null) return;

        // Engine plumes — scale and brighten with throttle
        float throttle = (float)TargetVessel.Throttle;
        bool  firing   = throttle > 0.01f;
        foreach (var plume in _plumes)
        {
            plume.Visible = firing;
            if (firing)
            {
                // Flicker: ±10% random scale variation for flame effect
                float flicker = 0.9f + (float)(GD.Randf() * 0.2f);
                plume.Scale   = new Vector3(throttle * flicker, throttle * flicker, throttle * flicker);

                if (plume.GetSurfaceOverrideMaterial(0) is StandardMaterial3D pm)
                    pm.EmissionEnergyMultiplier = 2.5f + throttle * 2.0f;
            }
        }

        // Reentry heat glow
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!_partNodes.TryGetValue(part.InstanceId, out var node)) continue;
            if (node is MeshInstance3D mesh && mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
            {
                float t = (float)System.Math.Clamp((part.Temperature - 290.0) / 2000.0, 0.0, 1.0);
                mat.EmissionEnabled = t > 0.05f;
                if (t > 0.05f)
                    mat.Emission = new Color(t, t * 0.35f, 0f) * t;
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
        var node = new MeshInstance3D();
        node.Name = part.Definition.Name.Replace(" ", "_");

        Mesh mesh = part.Definition.Category switch
        {
            PartCategory.Engine   => CreateCylinderMesh(0.4f, 0.8f),
            PartCategory.FuelTank => CreateCylinderMesh(0.625f, 1.875f),
            PartCategory.Command  => CreateSphereMesh(0.625f),
            _                     => (Mesh)CreateBoxMesh(0.5f, 0.5f, 0.5f)
        };
        node.Mesh = mesh;

        var mat = (StandardMaterial3D)GetFallback().Duplicate();
        mat.AlbedoColor = GetCategoryColor(part.Definition.Category);
        node.SetSurfaceOverrideMaterial(0, mat);
        return node;
    }

    private static CylinderMesh CreateCylinderMesh(float r, float h) =>
        new() { TopRadius = r, BottomRadius = r, Height = h };
    private static SphereMesh   CreateSphereMesh(float r) =>
        new() { Radius = r, Height = r * 2 };
    private static BoxMesh      CreateBoxMesh(float w, float h, float d) =>
        new() { Size = new Godot.Vector3(w, h, d) };

    private static Color GetCategoryColor(PartCategory cat) => cat switch
    {
        PartCategory.Command   => new Color(0.9f, 0.9f, 0.9f),
        PartCategory.Engine    => new Color(0.6f, 0.6f, 0.65f),
        PartCategory.FuelTank  => new Color(0.8f, 0.85f, 0.9f),
        PartCategory.Decoupler => new Color(1f, 0.8f, 0.2f),
        _                      => new Color(0.7f, 0.7f, 0.7f)
    };

    private void ClearNodes()
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _partNodes.Clear();
        _plumes.Clear();
        _hullMesh = null;
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
