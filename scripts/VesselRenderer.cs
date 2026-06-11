namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public partial class VesselRenderer : Node3D
{
    // El vessel que este renderer visualiza
    public Vessel? TargetVessel { get; set; }

    // Prefab path para un mesh genérico de cápsula, tanque y motor
    [Export] public string DefaultPartMeshPath { get; set; } = "res://assets/models/default_part.glb";

    // Diccionario instanciaId → Node3D en escena
    private readonly Dictionary<string, Node3D> _partNodes = new();

    private static StandardMaterial3D? _fallbackMaterial;

    private static StandardMaterial3D GetFallback()
    {
        if (_fallbackMaterial != null) return _fallbackMaterial;
        _fallbackMaterial = new StandardMaterial3D();
        _fallbackMaterial.AlbedoColor = new Color(0.8f, 0.2f, 0.2f);
        return _fallbackMaterial;
    }

    // Construir el árbol de nodos según el PartGraph actual
    public void BuildFromVessel(Vessel vessel)
    {
        TargetVessel = vessel;
        ClearNodes();

        var positions = vessel.Parts.ComputePartLocalPositions();
        foreach (var (part, localPos) in positions)
        {
            var node = CreatePartNode(part);
            node.Position = ToV3(localPos);
            AddChild(node);
            _partNodes[part.InstanceId] = node;
        }
    }

    public override void _Process(double delta)
    {
        if (TargetVessel == null) return;

        // Actualizar temperatura visual (color de calor)
        foreach (var part in TargetVessel.Parts.Parts)
        {
            if (!_partNodes.TryGetValue(part.InstanceId, out var node)) continue;
            if (node is MeshInstance3D mesh && mesh.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
            {
                // Temperatura → color: azul (fría) → naranja → blanco (caliente)
                float t = (float)System.Math.Clamp((part.Temperature - 290.0) / 2000.0, 0.0, 1.0);
                mat.EmissionEnabled = t > 0.05f;
                if (t > 0.05f)
                    mat.Emission = new Color(t, t * 0.4f, 0f) * t;
            }
        }
    }

    private Node3D CreatePartNode(Part part)
    {
        var node = new MeshInstance3D();
        node.Name = part.Definition.Name.Replace(" ", "_");

        // Mesh por defecto basado en categoría
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

    private static CylinderMesh CreateCylinderMesh(float radius, float height)
        => new() { TopRadius = radius, BottomRadius = radius, Height = height };

    private static SphereMesh CreateSphereMesh(float radius)
        => new() { Radius = radius, Height = radius * 2 };

    private static BoxMesh CreateBoxMesh(float w, float h, float d)
        => new() { Size = new Godot.Vector3(w, h, d) };

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
    }

    private static Godot.Vector3 ToV3(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
