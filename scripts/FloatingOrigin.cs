namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

public partial class FloatingOrigin : Node
{
    // El nodo raíz de la escena 3D de vuelo que contiene todos los objetos renderizados
    [Export] public NodePath SceneRootPath { get; set; } = "";

    // Mapeo de IDs de cuerpos/vessels a sus nodos Node3D en escena
    private readonly Dictionary<string, Node3D> _bodyNodes   = new();
    private readonly Dictionary<string, Node3D> _vesselNodes = new();

    // Planetas renderizados a escala reducida para evitar problemas de precisión float
    private const float PlanetRenderScale = 1.0f / 10000.0f;
    private readonly Dictionary<string, Node3D> _planetNodes = new();

    // Último origen usado (en coordenadas de simulación)
    private Vector3d _currentOrigin = Vector3d.Zero;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        var activeVessel = bridge.ActiveVessel;
        if (activeVessel == null) return;

        // El nuevo origen es la posición del vessel activo
        _currentOrigin = activeVessel.Position;

        // Actualizar posición de todos los cuerpos celestes (escala real, sin usar)
        foreach (var body in bridge.Universe.Bodies)
        {
            if (_bodyNodes.TryGetValue(body.Id, out var node))
            {
                var relPos = body.Position - _currentOrigin;
                node.Position = ToGodotV3(relPos);
            }
        }

        // Actualizar posición de todos los vessels
        foreach (var vessel in bridge.Universe.Vessels)
        {
            if (_vesselNodes.TryGetValue(vessel.Id, out var node))
            {
                var relPos = vessel.Position - _currentOrigin;
                node.Position = ToGodotV3(relPos);

                // Aplicar orientación
                node.Quaternion = ToGodotQ(vessel.Orientation);
            }
        }

        // Actualizar posición de planetas con escala de render reducida
        foreach (var body in bridge.Universe.Bodies)
        {
            if (_planetNodes.TryGetValue(body.Id, out var node))
            {
                var relPos = body.Position - _currentOrigin;
                node.Position = new Godot.Vector3(
                    (float)(relPos.X * PlanetRenderScale),
                    (float)(relPos.Y * PlanetRenderScale),
                    (float)(relPos.Z * PlanetRenderScale));
            }
        }
    }

    // Registrar un nodo Godot para que sea posicionado por el FloatingOrigin
    public void RegisterBodyNode(string bodyId, Node3D node)     => _bodyNodes[bodyId]     = node;
    public void RegisterVesselNode(string vesselId, Node3D node) => _vesselNodes[vesselId] = node;
    public void UnregisterVesselNode(string vesselId)            => _vesselNodes.Remove(vesselId);

    // Registrar un nodo de planeta que se posiciona con PlanetRenderScale
    public void RegisterPlanetNode(string bodyId, Node3D node) => _planetNodes[bodyId] = node;

    // Helpers de conversión double → float
    private static Godot.Vector3 ToGodotV3(Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    private static Godot.Quaternion ToGodotQ(Exosphere.Simulation.Math.Quaterniond q) =>
        new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
}
