namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;

/// <summary>
/// Keeps scene lighting consistent with the Sun's true position each frame.
///
/// The simulation tracks the Sun as a real body, but the scene
/// <see cref="DirectionalLight3D"/> and the planet shaders' <c>sun_dir</c> uniforms
/// were authored at fixed values, so the Earth's daylit hemisphere, terminator and
/// night-side city lights never tracked the actual Sun.
///
/// This node computes the unit direction TOWARD the Sun in render/world space and:
///   • orients the scene directional light so its forward (−Z) points away from the
///     Sun (i.e. light travels FROM the Sun toward the scene), and
///   • pushes <c>sun_dir</c> into every planet material that exposes it (Earth first).
///
/// Render space is only translated relative to sim space (floating origin), never
/// rotated, so a sim-space direction equals a render-space direction. The Sun is so
/// distant that (sun − vessel) is essentially the Earth→Sun direction.
/// </summary>
[GlobalClass]
public partial class SunController : Node
{
    public static float SolarVisibility { get; private set; } = 1f;

    // Lighting is presentation-only.  Twenty updates per second are enough to keep a
    // moving terminator/eclipsing body visually locked while avoiding a tree walk and
    // several material writes on every render frame.
    private const double VisualUpdatePeriodSeconds = 1.0 / 20.0;
    private double _visualUpdateTimer;
    private Vector3 _lastSunDir;
    private float _lastFedVisibility = -1f;
    private Universe? _cachedUniverse;
    private CelestialBody? _cachedSun;
    private readonly List<ShaderMaterial> _planetMaterials = new();
    private Node3D? _planetsNode;
    private MeshInstance3D? _groundMesh;
    private ShaderMaterial? _groundMaterial;

    // Cached node lookups — re-found lazily if they go null (e.g. scene rebuild).
    private DirectionalLight3D? _light;
    private ShaderMaterial?     _earthMat;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (!ReferenceEquals(_cachedUniverse, universe))
        {
            _cachedUniverse = universe;
            _cachedSun = universe?.GetBody("sun");
            _planetMaterials.Clear();
            _planetsNode = null;
            _groundMesh = null;
            _groundMaterial = null;
            _earthMat = null;
            _lastSunDir = Vector3.Zero;
            _lastFedVisibility = -1f;
            _visualUpdateTimer = 0.0;
        }

        var sun = _cachedSun;
        if (vessel == null || universe == null || sun == null) return;

        Vector3d simDir = (sun.Position - vessel.Position).Normalized;
        var renderDir = new Vector3((float)simDir.X, (float)simDir.Y, (float)simDir.Z);
        _visualUpdateTimer -= System.Math.Max(0.0, delta);
        if (_visualUpdateTimer > 0.0) return;
        _visualUpdateTimer = VisualUpdatePeriodSeconds;

        bool sunDirectionChanged = _lastSunDir == Vector3.Zero
            || _lastSunDir.DistanceSquaredTo(renderDir) > 1e-10f;
        bool materialsNeedRefresh = _earthMat == null
            || _planetMaterials.Count == 0
            || _planetsNode == null
            || !IsInstanceValid(_planetsNode);
        if (sunDirectionChanged)
        {
            OrientLight(renderDir);
            _lastSunDir = renderDir;
        }
        if (sunDirectionChanged || materialsNeedRefresh)
            FeedSunDir(renderDir);

        double visibility = 1.0;
        foreach (var body in universe.Bodies)
        {
            if (body.Id == "sun") continue;
            visibility = System.Math.Min(visibility, MissionGeometry.LimbDarkenedSolarDiscVisibility(
                vessel.Position, body.Position, body.Radius, sun.Position, sun.Radius));
        }
        float visibilityValue = (float)visibility;
        if (System.Math.Abs(visibilityValue - _lastFedVisibility) > 1e-4f)
        {
            SolarVisibility = visibilityValue;
            FeedSolarVisibility(visibilityValue);
            _lastFedVisibility = visibilityValue;
        }
    }

    /// <summary>
    /// Aims the directional light so it emits FROM the Sun toward the scene: a
    /// DirectionalLight3D shines along its forward/−Z axis, so to light the
    /// sun-facing side we need forward == −sunDir, i.e. −basis.z == sunDir.
    /// Energy and other light settings are left untouched.
    /// </summary>
    private void OrientLight(Vector3 sunDir)
    {
        if (_light == null || !IsInstanceValid(_light))
            _light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (_light == null) return;

        // Look toward −sunDir (the travel direction of the light). Pick an "up" that
        // isn't parallel to the look direction to keep the basis well-conditioned.
        Vector3 up = Mathf.Abs(sunDir.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        var origin = _light.GlobalPosition;
        _light.LookAtFromPosition(origin, origin - sunDir, up);
    }

    /// <summary>
    /// Pushes <c>sun_dir</c> into the Earth material (priority) and any other body
    /// material that exposes the same uniform, so every shader-driven terminator and
    /// night-side city-light field lines up with the real Sun.
    /// </summary>
    private void FeedSunDir(Vector3 sunDir)
    {
        if (_earthMat == null)
            _earthMat = FindBodyMaterial("Earth_mesh");
        _earthMat?.SetShaderParameter("sun_dir", sunDir);

        RefreshPlanetMaterials();
        foreach (var material in _planetMaterials)
            material.SetShaderParameter("sun_dir", sunDir);
    }

    private void FeedSolarVisibility(float visibility)
    {
        if (_earthMat == null)
            _earthMat = FindBodyMaterial("Earth_mesh");
        _earthMat?.SetShaderParameter("solar_visibility", visibility);

        // The low-altitude tangent patch is a separate unshaded material.  It uses the
        // same parameter so a synthetic or real eclipse cannot leave the local terrain
        // at full direct-light strength while the atmosphere is in shadow.
        if (_groundMesh == null || !IsInstanceValid(_groundMesh))
        {
            _groundMesh = GetTree().Root.FindChild("EarthGround", true, false) as MeshInstance3D;
            _groundMaterial = _groundMesh == null
                ? null
                : (_groundMesh.GetSurfaceOverrideMaterial(0) ?? _groundMesh.GetActiveMaterial(0))
                    as ShaderMaterial;
        }
        _groundMaterial?.SetShaderParameter("solar_visibility", visibility);
    }

    private void RefreshPlanetMaterials()
    {
        if (_planetsNode == null || !IsInstanceValid(_planetsNode))
        {
            _planetsNode = GetTree().Root.FindChild("Planets", true, false) as Node3D;
            _planetMaterials.Clear();
            if (_planetsNode == null) return;

            foreach (var child in _planetsNode.GetChildren())
            {
                if (child is not MeshInstance3D mesh || mesh.Name == "Earth_mesh") continue;
                // Body shaders (planet_body / earth_surface) all declare `sun_dir`.
                // Keep only valid ShaderMaterial references so this traversal happens
                // once per scene/universe instead of once per render frame.
                if ((mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0))
                    is ShaderMaterial material)
                    _planetMaterials.Add(material);
            }
        }
    }

    private ShaderMaterial? FindBodyMaterial(string meshName)
    {
        var mesh = GetTree().Root.FindChild(meshName, true, false) as MeshInstance3D;
        if (mesh == null) return null;
        return (mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0)) as ShaderMaterial;
    }
}
