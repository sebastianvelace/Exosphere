namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Physics;

/// <summary>
/// Re-entry plasma glow around the active vessel. Driven by the SAME convective heat
/// flux the simulation uses for thermal damage (<see cref="ThermalModel.ComputeHeatFlux"/>),
/// so the visible fireball tracks the real physics: it ignites only when ρ·v³ heating is
/// significant and brightens from deep orange to white-hot as the flux climbs.
///
/// Resplandor de plasma de reentrada: usa el mismo flujo de calor que el daño térmico
/// del sim, así que la bola de fuego aparece cuando el calentamiento real es alto.
/// </summary>
[GlobalClass]
public partial class ReentryPlasmaController : Node3D
{
    private MeshInstance3D?     _shock;   // bright shock cap on the windward side
    private MeshInstance3D?     _wake;    // trailing ionised wake
    private StandardMaterial3D? _shockMat;
    private StandardMaterial3D? _wakeMat;

    // Heat-flux thresholds (W/m²). Below FLUX_THRESH there is no visible plasma;
    // at/above FLUX_PEAK the glow is saturated white-hot.
    const double FLUX_THRESH = 5.0e4;
    const double FLUX_PEAK   = 6.0e5;

    public override void _Ready()
    {
        // Windward shock — a sphere we squash into a glowing cap facing the airflow.
        _shockMat = new StandardMaterial3D
        {
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor              = new Color(1.0f, 0.45f, 0.10f, 0f),
            EmissionEnabled          = true,
            Emission                 = new Color(1.0f, 0.45f, 0.12f),
            EmissionEnergyMultiplier = 2.5f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _shock = new MeshInstance3D
        {
            Name    = "ReentryShock",
            Mesh    = new SphereMesh { Radius = 2.4f, Height = 4.8f, RadialSegments = 24, Rings = 12 },
            Visible = false,
        };
        _shock.SetSurfaceOverrideMaterial(0, _shockMat);
        AddChild(_shock);

        // Trailing wake — a long faint cone of ionised gas behind the vessel.
        _wakeMat = new StandardMaterial3D
        {
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor              = new Color(0.6f, 0.30f, 1.0f, 0f),
            EmissionEnabled          = true,
            Emission                 = new Color(0.55f, 0.30f, 1.0f),
            EmissionEnergyMultiplier = 1.4f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _wake = new MeshInstance3D
        {
            Name    = "ReentryWake",
            Mesh    = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 2.0f, Height = 18f, RadialSegments = 20 },
            Visible = false,
        };
        _wake.SetSurfaceOverrideMaterial(0, _wakeMat);
        AddChild(_wake);
    }

    public override void _Process(double delta)
    {
        if (_shock == null || _wake == null || _shockMat == null || _wakeMat == null) return;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || vessel.IsDestroyed)
        {
            _shock.Visible = false;
            _wake.Visible  = false;
            return;
        }

        var body = bridge.Universe.GetDominantBody(vessel.Position);

        double density  = body.GetAtmosphericDensity(vessel.Position);
        var    surfVel  = vessel.GetSurfaceVelocity(body);
        double airspeed = surfVel.Magnitude;
        double flux     = ThermalModel.ComputeHeatFlux(density, airspeed);

        double intensity = System.Math.Clamp(
            (flux - FLUX_THRESH) / (FLUX_PEAK - FLUX_THRESH), 0.0, 1.0);

        if (intensity < 0.01)
        {
            _shock.Visible = false;
            _wake.Visible  = false;
            return;
        }

        _shock.Visible = true;
        _wake.Visible  = true;

        // Airflow direction in render space (sim and render share axis convention).
        Vector3 flowDir = new Vector3((float)surfVel.X, (float)surfVel.Y, (float)surfVel.Z);
        flowDir = flowDir.LengthSquared() > 1e-6f ? flowDir.Normalized() : Vector3.Up;

        // Vessel body centre at the render origin; full stack sits higher than a lone Starship.
        bool hasSH = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        Vector3 bodyCentre = new Vector3(0f, hasSH ? 30f : 8f, 0f);

        // Shock sits on the windward (leading) face; wake streams out behind.
        _shock.Position = bodyCentre + flowDir * (hasSH ? 14f : 6f);
        _wake.Position  = bodyCentre - flowDir * 9f;

        // Orient the wake cylinder (+Y axis) to point downstream (away from the flow).
        OrientYAxis(_wake, -flowDir);

        // Flicker so the plasma boils rather than glowing flat.
        float flicker = 0.8f + (float)(GD.Randf() * 0.4f);

        // Colour shifts orange → white-hot as intensity rises.
        float white = (float)intensity;
        var shockCol = new Color(
            1.0f,
            0.45f + 0.45f * white,
            0.10f + 0.70f * white,
            (float)(intensity * 0.85f * flicker));
        _shockMat.AlbedoColor              = shockCol;
        _shockMat.Emission                 = new Color(shockCol.R, shockCol.G, shockCol.B);
        _shockMat.EmissionEnergyMultiplier = (float)(2.0 + intensity * 4.0) * flicker;

        _wakeMat.AlbedoColor              = new Color(0.6f, 0.30f, 1.0f, (float)(intensity * 0.35f));
        _wakeMat.EmissionEnergyMultiplier = (float)(1.0 + intensity * 2.0);

        float sizeScale = Mathf.Lerp(0.7f, 1.4f, (float)intensity);
        _shock.Scale = new Vector3(sizeScale, sizeScale, sizeScale);
    }

    // Rotate a mesh whose local +Y should point along <paramref name="dir"/>.
    private static void OrientYAxis(Node3D node, Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        Vector3 up   = dir.Normalized();
        Vector3 axis = Vector3.Up.Cross(up);
        if (axis.LengthSquared() < 1e-6f) { node.Basis = Basis.Identity; return; }
        float angle = Vector3.Up.AngleTo(up);
        node.Basis = new Basis(axis.Normalized(), angle);
    }
}
