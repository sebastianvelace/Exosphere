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

    // Scaled-space backdrop: planets are unit spheres rendered at a FIXED distance and
    // scaled to subtend their correct angular size for the vessel's real altitude. This
    // keeps coordinates small (float-precise, no z-fighting) while the rocket-to-planet
    // proportion stays physically correct — a 121 m rocket really is ~1/52,000 of Earth.
    private const float BackdropDistance = 50_000.0f;
    private readonly Dictionary<string, Node3D> _planetNodes = new();
    // 90° about X: brings a body's equator (not its pole) to face the +Y launch site.
    private static readonly Godot.Quaternion _planetTilt =
        new(Godot.Vector3.Right, Godot.Mathf.Pi * 0.5f);

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

        // Scaled-space backdrop: place each planet along its TRUE direction from the
        // vessel, at a fixed distance, scaled so it subtends the correct angular size.
        foreach (var body in bridge.Universe.Bodies)
        {
            if (_planetNodes.TryGetValue(body.Id, out var node))
            {
                var toBody = body.Position - _currentOrigin;   // true offset (metres)
                double d   = toBody.Magnitude;
                double R   = body.Radius;
                if (d < R + 1.0) d = R + 1.0;                   // never inside the surface

                // Angular radius α of the body; backdrop radius so it subtends 2α at the
                // fixed backdrop distance. Surface vessel → α≈90° (fills the sky).
                double sinA = System.Math.Min(R / d, 0.999999);
                double alpha = System.Math.Asin(sinA);
                float  rBackdrop = BackdropDistance * (float)System.Math.Sin(alpha);

                var dir = toBody.Normalized;
                node.Position = new Godot.Vector3(
                    (float)dir.X, (float)dir.Y, (float)dir.Z) * BackdropDistance;
                // Tilt the body 90° so its equator (blue/green) — not the polar ice cap —
                // faces the +Y launch site; scale composes with this rotation.
                node.Quaternion = _planetTilt;
                node.Scale = Godot.Vector3.One * System.Math.Max(rBackdrop, 0.001f);
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
