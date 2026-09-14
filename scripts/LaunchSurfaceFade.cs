namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Material-level opacity for the single-surface launch-site meshes. Instance
/// Transparency is ignored by Compatibility; owning a material copy also keeps
/// hero, regional and civil fades from modifying each other's shared resources.
/// </summary>
internal sealed class LaunchSurfaceFade
{
    private readonly Dictionary<MeshInstance3D, (Material Material, float Alpha,
        BaseMaterial3D.TransparencyEnum Mode)> _materials = new();
    private readonly Dictionary<MeshInstance3D, float> _lastOpacity = new();

    public void Apply(MeshInstance3D mesh, float opacity)
    {
        opacity = Mathf.Clamp(opacity, 0f, 1f);
        mesh.Visible = opacity > 0.001f;
        if (_lastOpacity.TryGetValue(mesh, out float previous) && previous == opacity)
            return;
        if (!_materials.TryGetValue(mesh, out var binding))
        {
            var source = mesh.GetActiveMaterial(0);
            if (source == null) return;
            var owned = (Material)source.Duplicate();
            binding = owned is StandardMaterial3D standard
                ? (owned, standard.AlbedoColor.A, standard.Transparency)
                : (owned, 1f, BaseMaterial3D.TransparencyEnum.Disabled);
            _materials.Add(mesh, binding);
            mesh.SetSurfaceOverrideMaterial(0, owned);
        }
        _lastOpacity[mesh] = opacity;

        if (binding.Material is StandardMaterial3D material)
        {
            var color = material.AlbedoColor;
            color.A = binding.Alpha * opacity;
            material.AlbedoColor = color;
            material.Transparency = opacity >= 1f
                ? binding.Mode : BaseMaterial3D.TransparencyEnum.Alpha;
        }
        else if (binding.Material is ShaderMaterial shader)
        {
            // Civil shader materials are authored by CreateLaunchSurfaceMaterial.
            shader.SetShaderParameter("surface_opacity", opacity);
        }
    }
}
