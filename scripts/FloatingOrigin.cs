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
    private const double MetresPerUnit   = 2.8;   // render scale (matches the vessel)
    private readonly Dictionary<string, Node3D> _planetNodes = new();
    private Camera3D? _camera;
    // Orient Earth so Cape Canaveral (28.5°N, 80.6°W) sits at the +Y launch site, i.e.
    // the rocket lifts off over Florida / the US east coast. Public so the ground patch
    // can sample the same texture region at the launch site.
    public static readonly Godot.Quaternion PlanetTilt = CapeCanaveralTilt();

    private static Godot.Quaternion CapeCanaveralTilt()
    {
        float lat = Godot.Mathf.DegToRad(28.5f);
        float lon = Godot.Mathf.DegToRad(-80.6f);
        // Equirect convention used by the Earth shader: lon = atan2(z,x), lat = asin(y).
        var pcc = new Godot.Vector3(
            Godot.Mathf.Cos(lat) * Godot.Mathf.Cos(lon),
            Godot.Mathf.Sin(lat),
            Godot.Mathf.Cos(lat) * Godot.Mathf.Sin(lon));
        var axis = pcc.Cross(Godot.Vector3.Up);
        if (axis.Length() < 1e-5f) return Godot.Quaternion.Identity;
        float angle = Godot.Mathf.Acos(Godot.Mathf.Clamp(pcc.Dot(Godot.Vector3.Up), -1f, 1f));
        return new Godot.Quaternion(axis.Normalized(), angle);
    }

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

        // Scaled-space backdrop, anchored to the CAMERA. Each planet is placed along its
        // true direction from the camera at a fixed distance, scaled to its correct angular
        // size for the camera's REAL distance to the body. Anchoring to the camera (rather
        // than the vessel) means pulling the camera far back lifts the viewpoint into space
        // so the whole planet shrinks to a disc — letting the player see the full Earth.
        if (_camera == null || !IsInstanceValid(_camera))
            _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;

        var camRender = _camera?.GlobalPosition ?? Godot.Vector3.Zero;
        // Real (sim) camera position: vessel is at the render origin; render units → metres.
        var camSim = _currentOrigin + new Vector3d(camRender.X, camRender.Y, camRender.Z) * MetresPerUnit;

        foreach (var body in bridge.Universe.Bodies)
        {
            if (_planetNodes.TryGetValue(body.Id, out var node))
            {
                var toBody = body.Position - camSim;            // from the camera (metres)
                double d   = toBody.Magnitude;
                double R   = body.Radius;
                if (d < R + 1.0) d = R + 1.0;                   // never inside the surface

                double sinA      = System.Math.Min(R / d, 0.999999);
                float  rBackdrop = BackdropDistance * (float)sinA;   // subtends asin(R/d)

                var dir = toBody.Normalized;
                node.Position = camRender + new Godot.Vector3(
                    (float)dir.X, (float)dir.Y, (float)dir.Z) * BackdropDistance;
                node.Quaternion = PlanetTilt;
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
