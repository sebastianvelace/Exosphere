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

    /// <summary>
    /// Rotation carrying the Earth texture's own lat/lon frame onto the simulation's
    /// body-fixed frame, so a point at (lat, lon) in the texture is drawn exactly where
    /// the simulation puts that latitude and longitude.
    ///
    /// This used to be a compensating tilt: the rocket was spawned on the inertial +Y axis
    /// and the planet was spun until Florida happened to sit under it. Now the pad is
    /// placed at its real geodetic coordinates, so the texture only has to agree with the
    /// spin axis and the launch site lands on Florida on its own.
    ///
    /// Public so the ground patch can undo it and sample the same texture region.
    /// </summary>
    public static Godot.Quaternion PlanetTilt { get; private set; } = Godot.Quaternion.Identity;

    /// <summary>
    /// Builds <see cref="PlanetTilt"/> from the body's spin axis, using the SAME body-fixed
    /// basis as <c>CelestialBody.GetSurfacePosition</c>. Texture convention (equirect, as
    /// the Earth shader reads it): +Y is the north pole, lon = atan2(z, x).
    /// </summary>
    private static void BuildPlanetTilt(Exosphere.Simulation.CelestialBody body)
    {
        var north = body.RotationAxis;
        var seed  = System.Math.Abs(north.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        var primeMeridian = seed.Cross(north).Normalized;        // texture (lat 0, lon 0)
        var ninetyEast    = north.Cross(primeMeridian).Normalized; // texture (lat 0, lon 90°E)

        // Columns map texture axes → simulation axes: x → prime meridian, y → north, z → 90°E.
        var basis = new Godot.Basis(
            new Godot.Vector3((float)primeMeridian.X, (float)primeMeridian.Y, (float)primeMeridian.Z),
            new Godot.Vector3((float)north.X,         (float)north.Y,         (float)north.Z),
            new Godot.Vector3((float)ninetyEast.X,    (float)ninetyEast.Y,    (float)ninetyEast.Z));

        PlanetTilt = basis.GetRotationQuaternion();
    }

    // Último origen usado (en coordenadas de simulación)
    private Vector3d _currentOrigin = Vector3d.Zero;

    // Camera altitude over Earth's surface (metres), updated each frame. Both the distant-Earth
    // backdrop and the local ground patch fade on this axis so they never overlap into a seam.
    public static double CameraAltOverEarth { get; private set; } = 0.0;

    private static double Smoothstep01(double a, double b, double x)
    {
        if (b <= a) return x >= b ? 1.0 : 0.0;
        double t = System.Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private bool _planetTiltBuilt;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge?.Universe == null) return;

        // The spin axis comes from body JSON, so the texture frame can only be built once
        // the universe is loaded.
        if (!_planetTiltBuilt)
        {
            var earthBody = bridge.Universe.GetBody("earth");
            if (earthBody != null)
            {
                BuildPlanetTilt(earthBody);
                _planetTiltBuilt = true;
            }
        }

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

                // Earth: fade the distant backdrop in only as the CAMERA climbs (real ascent
                // or zooming out). In the low launch view it stays hidden so the local ground
                // patch + procedural sky own the horizon — no grey seam where the two meet.
                if (body.Id == "earth")
                {
                    CameraAltOverEarth = d - R;
                    float a = (float)Smoothstep01(15_000.0, 32_000.0, CameraAltOverEarth);
                    if (node is MeshInstance3D mi &&
                        mi.GetSurfaceOverrideMaterial(0) is ShaderMaterial sm)
                        sm.SetShaderParameter("planet_alpha", a);
                    node.Visible = a > 0.002f;
                }

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
