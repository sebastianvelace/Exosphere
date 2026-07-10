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

    // Cached node lookups — re-found lazily if they go null (e.g. scene rebuild).
    private DirectionalLight3D? _light;
    private ShaderMaterial?     _earthMat;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var sun = universe?.GetBody("sun");
        if (vessel == null || universe == null || sun == null) return;

        Vector3d simDir = (sun.Position - vessel.Position).Normalized;
        var renderDir = new Vector3((float)simDir.X, (float)simDir.Y, (float)simDir.Z);
        OrientLight(renderDir);
        FeedSunDir(renderDir);

        double visibility = 1.0;
        foreach (var body in universe.Bodies)
        {
            if (body.Id == "sun") continue;
            visibility = System.Math.Min(visibility, MissionGeometry.SolarDiscVisibility(
                vessel.Position, body.Position, body.Radius, sun.Position, sun.Radius));
        }
        SolarVisibility = (float)visibility;
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

        // Cheap pass over the remaining planet meshes (Mars, etc.). These materials
        // are created once and persist, so this is just a handful of cheap calls/frame.
        var planets = GetTree().Root.FindChild("Planets", true, false) as Node3D;
        if (planets == null) return;

        foreach (var child in planets.GetChildren())
        {
            if (child is not MeshInstance3D mesh) continue;
            if (mesh.Name == "Earth_mesh") continue;   // already handled above
            // Body shaders (planet_body / earth_surface) all declare `sun_dir`. The Sun
            // itself uses a StandardMaterial3D, so the ShaderMaterial test skips it; any
            // other ShaderMaterial here carries the uniform, so the set is always valid.
            if ((mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0)) is ShaderMaterial sm)
                sm.SetShaderParameter("sun_dir", sunDir);
        }
    }

    private ShaderMaterial? FindBodyMaterial(string meshName)
    {
        var mesh = GetTree().Root.FindChild(meshName, true, false) as MeshInstance3D;
        if (mesh == null) return null;
        return (mesh.GetSurfaceOverrideMaterial(0) ?? mesh.GetActiveMaterial(0)) as ShaderMaterial;
    }
}
