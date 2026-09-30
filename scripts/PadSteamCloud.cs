namespace Exosphere.Game;

using Godot;

/// <summary>
/// Ground-anchored deluge optical volumes. Lobe motion is a bounded visual proxy;
/// extinction and source lighting follow delivered power, not commanded throttle.
/// No simulation state is modified here.
/// </summary>
public partial class PadSteamCloud : Node3D
{
    private readonly List<(MeshInstance3D mesh, ShaderMaterial material,
        Vector3 size, Vector3 center)> _lobes = new();
    public float OpticalWeight { get; private set; }
    public int LobeCount => _lobes.Count;

    public override void _Ready()
    {
        var noise = new NoiseTexture3D
        {
            Width = 64, Height = 64, Depth = 64, Seamless = true,
            Noise = new FastNoiseLite
            {
                Seed = 140, Frequency = 0.04f,
                NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
                FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3,
            },
        };
        var shader = GD.Load<Shader>("res://assets/shaders/pad_steam.gdshader");
        var box = new BoxMesh { Size = Vector3.One };
        // Two exhaust outflows with overlapping rising lobes, not camera-authored cards.
        // Units below are render units (2.8 m). Max height is about one stack height.
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 6; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            var center = new Vector3(side * (14f + i * 9f),
                0f, (phase - 0.5f) * 24f);
            var size = new Vector3(34f + phase * 12f, 16f + i * 6f, 30f + phase * 14f);
            // Composite local condensed water after planetary transparency and
            // the exhaust. Opaque terrain/hull still clip the ray integration.
            var mat = new ShaderMaterial { Shader = shader, RenderPriority = 4 };
            mat.SetShaderParameter("density_noise", noise);
            mat.SetShaderParameter("seed", phase + (side + 1) * 0.21f);
            var mesh = new MeshInstance3D
            {
                Name = $"SteamLobe{side}_{i}", Mesh = box,
                MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(mesh);
            _lobes.Add((mesh, mat, size, center));
        }
    }

    public void UpdateCloud(float weight, float sourcePower, float age, Vector3 sunDirection,
        float solarVisibility)
    {
        OpticalWeight = weight;
        Visible = weight > 0.01f;
        if (!Visible) return;
        float spread = 1f - Mathf.Exp(-Mathf.Max(0f, age) / 5f);
        Vector3 sunLocal = GlobalBasis.Orthonormalized().Inverse() * sunDirection;
        foreach (var (mesh, material, size, center) in _lobes)
        {
            // The outgoing flow reaches the outer lobes over time; it never appears
            // hundreds of metres away on the first combustion frame.
            float delay = Mathf.Abs(center.X) / 12f;
            float fill = Mathf.SmoothStep(delay * 0.35f, delay * 0.35f + 2f, age);
            mesh.Visible = fill > 0.01f;
            var scale = size * Mathf.Lerp(0.32f, 1f, spread);
            Vector3 origin = new(center.X * Mathf.Lerp(0.20f, 1f, spread),
                scale.Y * 0.50f, center.Z * spread);
            mesh.Position = origin;
            mesh.Scale = scale;
            material.SetShaderParameter("size_m", scale * 2.8f);
            material.SetShaderParameter("sun_local", sunLocal);
            material.SetShaderParameter("solar_visibility", solarVisibility);
            material.SetShaderParameter("fire_local", (new Vector3(0f, 4f, 0f) - origin) / scale);
            material.SetShaderParameter("source_power", sourcePower);
            material.SetShaderParameter("opacity", weight * fill);
            material.SetShaderParameter("age", age);
        }
    }
}
