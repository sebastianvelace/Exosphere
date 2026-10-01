namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
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
    private ShaderMaterial? _saturnRingMaterial;
    private float _lastCameraFar = float.NaN;

    // Last scaled-space sample consumed by the visual harness/telemetry. Keeping this
    // presentation-only snapshot makes an invisible planet diagnosable without changing
    // the simulation or walking the scene tree from the capture tool.
    public string LastPresentationBodyId { get; private set; } = string.Empty;
    public bool LastPresentationVisible { get; private set; }
    public double LastPresentationCameraDistance { get; private set; }
    public float LastPresentationAngularDiameterDeg { get; private set; }
    public float LastPresentationForwardCosine { get; private set; }
    public Vector3 LastPresentationDirection { get; private set; } = Vector3.Zero;
    public Vector3 LastPresentationBackdropPosition { get; private set; } = Vector3.Zero;

    /// <summary>
    /// Legacy proper rotation for backdrop node placement. Earth texture
    /// sampling uses <see cref="EarthTextureBasis"/>: latitude/longitude axes
    /// include a reflection that cannot be encoded in this quaternion.
    /// </summary>
    public static Godot.Quaternion PlanetTilt { get; private set; } = Godot.Quaternion.Identity;
    /// <summary>Legacy backdrop rotation including the live sidereal spin phase.</summary>
    public static Godot.Quaternion PlanetOrientation { get; private set; } = Godot.Quaternion.Identity;
    // Texture coordinates (east, north, ninety-east) form a reflected basis.
    // A quaternion cannot represent it: Godot flips its north axis when
    // extracting a proper rotation. Retain the full basis for Earth sampling.
    public static Basis EarthTextureBasis { get; private set; } = Basis.Identity;
    private static Basis _earthTextureBaseBasis = Basis.Identity;

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

        _earthTextureBaseBasis = basis;
        EarthTextureBasis = basis;
        PlanetTilt = basis.GetRotationQuaternion();
        PlanetOrientation = PlanetTilt;
    }

    // Último origen usado (en coordenadas de simulación)
    private Vector3d _currentOrigin = Vector3d.Zero;

    // Camera altitude over Earth's surface (metres), updated each frame. Both the distant-Earth
    // detail overlay fades on this axis; the global Earth stays opaque.
    public static double CameraAltOverEarth { get; private set; } = 0.0;
    public static Vector3 CameraEarthRadialUp { get; private set; } = Vector3.Up;
    public static double CameraEarthRadiusM { get; private set; } = 6371008.8;
    public static double CameraEarthRadialAltitudeM { get; private set; }


    /// <summary>
    /// Shared pad→globe handoff. The local patch owns the horizon through the whole
    /// ascent camera regime; the scaled-space Earth arrives before the tangent
    /// approximation becomes a coloured cookie in a pulled-back camera.
    /// </summary>
    // The tangent patch carries measured Starbase detail through the low ascent;
    // complete the globe handoff before the pulled-back 20 km view. Both render
    // representations share optical transport and geographic depth.
    public const double EarthVisualHandoffLowM = 12_000.0;
    public const double EarthVisualHandoffHighM = 18_000.0;

    /// <summary>
    /// Legacy name for the global-detail handoff weight, not planet opacity.
    /// The opaque Earth remains visible while local terrain coverage fades.
    /// </summary>
    public static float EarthGlobeAlpha(double cameraAltitudeM) =>
        (float)Smoothstep01(EarthVisualHandoffLowM, EarthVisualHandoffHighM, cameraAltitudeM);

    /// <summary>
    /// Geocentric radius of the reference surface under <paramref name="worldPos"/>.
    /// Reads the live sim body (WGS ellipsoid when oblate) so render code does not
    /// fork a second Earth model.
    /// </summary>
    public static double VisualSurfaceRadiusMetres(CelestialBody body, Vector3d worldPos)
    {
        var surface = body.GetSurfacePoint(worldPos, 0.0);
        double radius = (surface - body.Position).Magnitude;
        if (!double.IsFinite(radius) || radius < 1.0)
            return body.MaximumRadius > 1.0 ? body.MaximumRadius : body.Radius;
        return radius;
    }

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

        var liveEarth = bridge.Universe.GetBody("earth");
        if (liveEarth != null)
        {
            var axis = ToGodotV3(liveEarth.RotationAxis).Normalized();
            var spin = new Godot.Quaternion(axis,
                (float)(liveEarth.AngularSpeed * bridge.Universe.CurrentTime));
            PlanetOrientation = spin * PlanetTilt;
            EarthTextureBasis = new Basis(spin) * _earthTextureBaseBasis;
        }

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

        var earthReference = bridge.Universe.GetBody("earth");
        if (earthReference != null)
        {
            var cameraFromEarth = camSim - earthReference.Position;
            CameraEarthRadialUp = ToGodotV3(cameraFromEarth.Normalized);
            CameraEarthRadiusM = VisualSurfaceRadiusMetres(earthReference, camSim);
            CameraEarthRadialAltitudeM = System.Math.Max(cameraFromEarth.Magnitude - CameraEarthRadiusM, 0.0);
            CameraAltOverEarth = System.Math.Max(earthReference.GetAltitude(camSim), 0.0);
        }

        foreach (var body in bridge.Universe.Bodies)
        {
            if (_planetNodes.TryGetValue(body.Id, out var node))
            {
                var toBody = body.Position - camSim;            // from the camera (metres)
                double d   = toBody.Magnitude;
                // Use the live ellipsoid (or sphere) under the camera so angular size and
                // altitude match pad geodesy instead of a mean-radius sphere that buries
                // Kennedy/Starbase by kilometres.
                double R = body.Id == "earth"
                    ? VisualSurfaceRadiusMetres(body, camSim)
                    : body.Radius;
                if (d < R + 1.0) d = R + 1.0;                   // never inside the surface

                // Earth stays opaque at every height. Only measured local detail fades.
                if (body.Id == "earth")
                {
                    // The backdrop is a continuous surface. Only local detail fades;
                    // fading Earth itself exposes the sky instead of the terrain below.
                    node.Visible = true;
                    ExpandCameraFarForHorizon(R, CameraAltOverEarth);
                }

                double sinA      = System.Math.Min(R / d, 0.999999);
                float  rBackdrop = BackdropDistance * (float)sinA;   // subtends asin(R/d)

                var dir = toBody.Normalized;
                if (_camera != null && body.Id ==
                    (bridge.Universe.GetDominantBody(activeVessel.Position)?.Id ?? string.Empty))
                {
                    var dirRender = new Godot.Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
                    var forward = -_camera.GlobalTransform.Basis.Z.Normalized();
                    LastPresentationBodyId = body.Id;
                    LastPresentationVisible = node.Visible;
                    LastPresentationCameraDistance = d;
                    LastPresentationAngularDiameterDeg =
                        Mathf.RadToDeg(2.0f * Mathf.Asin((float)sinA));
                    LastPresentationForwardCosine = forward.Dot(dirRender);
                    LastPresentationDirection = dirRender;
                    LastPresentationBackdropPosition = camRender + dirRender * BackdropDistance;
                }
                node.Position = camRender + new Godot.Vector3(
                    (float)dir.X, (float)dir.Y, (float)dir.Z) * BackdropDistance;
                node.Quaternion = body.Id == "earth" ? PlanetOrientation : PlanetTilt;
                node.Scale = Godot.Vector3.One * System.Math.Max(rBackdrop, 0.001f);
                if (node is MeshInstance3D planet
                    && planet.GetSurfaceOverrideMaterial(0) is ShaderMaterial material)
                {
                    material.SetShaderParameter("physical_depth_scale", (float)(d / (BackdropDistance * MetresPerUnit)));
                    material.SetShaderParameter("backdrop_far_units", _camera?.Far ?? 420_000f);
                    BindEarthGeometry(material);

                    if (body.Id == "saturn")
                    {
                        // Preserve the ring/body proxy depth, sharing Earth occlusion only.
                        if (_saturnRingMaterial == null || !IsInstanceValid(_saturnRingMaterial))
                            _saturnRingMaterial = node.GetNodeOrNull<MeshInstance3D>("SaturnRing")?
                                .GetSurfaceOverrideMaterial(0) as ShaderMaterial;
                        _saturnRingMaterial?.SetShaderParameter("physical_depth_scale", (float)(d / (BackdropDistance * MetresPerUnit)));
                        if (_saturnRingMaterial != null) BindEarthGeometry(_saturnRingMaterial);
                    }
                    if (body.Id == "earth")
                    {
                        PlanetMaterials.BindSurfaceLuts(material);
                        PlanetMaterials.BindEarthClouds(material, body, bridge.Universe.CurrentTime);
                        material.SetShaderParameter("world_to_earth_texture", EarthTextureBasis.Inverse());
                        if (bridge.LaunchSiteOrNull is { } site)
                        {
                            var sitePosition = site.GetPosition(body, bridge.Universe.CurrentTime);
                            var frame = site.GetLocalFrame(body, bridge.Universe.CurrentTime);
                            material.SetShaderParameter("site_radial_up", ToGodotV3((sitePosition - body.Position).Normalized));
                            material.SetShaderParameter("site_east", ToGodotV3(frame.East));
                            material.SetShaderParameter("site_north", ToGodotV3(frame.North));
                        }
                    }
                }
            }
        }
    }

    // Registrar un nodo Godot para que sea posicionado por el FloatingOrigin
    public void RegisterBodyNode(string bodyId, Node3D node)     => _bodyNodes[bodyId]     = node;
    public void RegisterVesselNode(string vesselId, Node3D node) => _vesselNodes[vesselId] = node;
    public void UnregisterVesselNode(string vesselId)            => _vesselNodes.Remove(vesselId);

    // Registrar un nodo de planeta que se posiciona con PlanetRenderScale
    public void RegisterPlanetNode(string bodyId, Node3D node) => _planetNodes[bodyId] = node;

    /// <summary>
    /// SimulationBridge caps Far at 120 km of render range. Geometric horizon
    /// at 20 km is already ~490 km, so the far plane cut the ground disc into
    /// a cookie. Expand Far with the horizon while the local Earth patch is up.
    /// </summary>
    private static void BindEarthGeometry(ShaderMaterial material)
    {
        material.SetShaderParameter("physical_camera_up", CameraEarthRadialUp);
        material.SetShaderParameter("physical_camera_altitude_m", (float)CameraEarthRadialAltitudeM);
        material.SetShaderParameter("physical_planet_radius_m", (float)CameraEarthRadiusM);
    }

    private void ExpandCameraFarForHorizon(double surfaceRadiusM, double cameraAltM)
    {
        if (_camera == null || !IsInstanceValid(_camera)) return;
        double alt = System.Math.Max(cameraAltM, 50.0);
        double horizonM = System.Math.Sqrt(System.Math.Max(0.0,
            2.0 * surfaceRadiusM * alt + alt * alt));
        float far = Mathf.Clamp(
            (float)(horizonM / MetresPerUnit) * 1.30f + 8000f,
            120_000f,
            420_000f);
        if (float.IsNaN(_lastCameraFar) || Mathf.Abs(_lastCameraFar - far) > 400f)
        {
            _camera.Far = far;
            _lastCameraFar = far;
        }
    }

    // Helpers de conversión double → float
    private static Godot.Vector3 ToGodotV3(Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    private static Godot.Quaternion ToGodotQ(Exosphere.Simulation.Math.Quaterniond q) =>
        new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
}
