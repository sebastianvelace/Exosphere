namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

/// <summary>Camera-to-hull cloud transport; leaves the hull in the opaque depth pass.</summary>
public partial class VesselCloudOcclusion : Node
{
    private readonly List<MeshInstance3D> _meshes = new();
    private ShaderMaterial _material = null!;
    private bool _attached;
    public ShaderMaterial CloudMaterial => _material;
    public bool IsAttached => _attached;
    public bool PresentationEnabled { get; set; } = true;

    public override void _Ready()
    {
        ProcessPriority = 20; // After floating origin, sky and camera updates.
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/vessel_cloud_occlusion.gdshader"),
            RenderPriority = 120,
        };
        Collect(GetParent());
    }

    private void Collect(Node node)
    {
        // Effects already use transparency. Their backgrounds must not receive
        // another cloud source term through a rectangular particle/cone mesh.
        if (node is PlumeSystem || node.IsQueuedForDeletion()) return;
        if (node is MeshInstance3D mesh && mesh.MaterialOverlay == null)
            _meshes.Add(mesh);
        foreach (var child in node.GetChildren())
            if (child != this) Collect(child);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var renderer = GetParent() as VesselRenderer;
        var vessel = renderer?.TargetVessel;
        var camera = GetViewport().GetCamera3D();
        if (bridge == null || vessel == null || camera == null) return;
        var body = bridge.Universe.GetDominantBody(vessel.Position);
        var optics = body.Atmosphere?.Optics;
        bool enabled = body.Id == "earth" && optics?.HasCloudLayer == true
            && PresentationEnabled && !SkyController.CloudsDisabled && renderer!.IsVisibleInTree();
        if (enabled)
        {
            // Conservative bounds for nearby vehicle geometry. Do not submit
            // hundreds of overlay draws in clear pad or orbital views.
            var eye = FloatingOrigin.CameraEarthRadialUp
                * (float)(FloatingOrigin.CameraEarthRadiusM + FloatingOrigin.CameraEarthRadialAltitudeM);
            var centre = eye + (renderer!.GlobalPosition - camera.GlobalPosition) * 2.8f;
            var segment = centre - eye;
            float closest = segment.LengthSquared() > 0
                ? Mathf.Clamp(-eye.Dot(segment) / segment.LengthSquared(), 0, 1) : 0;
            double low = (eye + segment * closest).Length() - FloatingOrigin.CameraEarthRadiusM - 160;
            double high = System.Math.Max(eye.Length(), centre.Length()) - FloatingOrigin.CameraEarthRadiusM + 160;
            enabled = low <= optics!.CloudTopAltitude && high >= optics.CloudBaseAltitude;
        }
        if (enabled != _attached)
        {
            foreach (var mesh in _meshes)
                if (IsInstanceValid(mesh)) mesh.MaterialOverlay = enabled ? _material : null;
            _attached = enabled;
        }
        if (!enabled) return;
        PlanetMaterials.BindEarthClouds(_material, body, bridge.Universe.CurrentTime);
        PlanetMaterials.BindSurfaceOptics(_material, optics!, body.Atmosphere!.MaxAltitude);
        PlanetMaterials.BindSurfaceLuts(_material);
        _material.SetShaderParameter("physical_camera_up", FloatingOrigin.CameraEarthRadialUp);
        _material.SetShaderParameter("physical_camera_altitude_m", (float)FloatingOrigin.CameraEarthRadialAltitudeM);
        _material.SetShaderParameter("physical_planet_radius_m", (float)FloatingOrigin.CameraEarthRadiusM);
        var sun = bridge.Universe.GetBody("sun");
        Vector3d physical = sun == null ? Vector3d.Up : (sun.Position - vessel.Position).Normalized;
        var direction = SunController.Instance?.GetVisualSunDirection(body, vessel.Position, physical) ?? physical;
        _material.SetShaderParameter("cloud_foreground_sun", new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z));
    }
}
