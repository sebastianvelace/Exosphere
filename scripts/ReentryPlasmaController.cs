namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;
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
            Mesh    = new SphereMesh { Radius = 0.95f, Height = 1.9f, RadialSegments = 24, Rings = 12 },
            Visible = false,
        };
        _shock.SetSurfaceOverrideMaterial(0, _shockMat);
        AddChild(_shock);

        // Trailing wake — a long faint cone of ionised gas behind the vessel.
        _wakeMat = new StandardMaterial3D
        {
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor              = new Color(1.0f, 0.30f, 0.08f, 0f),
            EmissionEnabled          = true,
            Emission                 = new Color(1.0f, 0.28f, 0.08f),
            EmissionEnergyMultiplier = 1.4f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _wake = new MeshInstance3D
        {
            Name    = "ReentryWake",
            Mesh    = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.75f, Height = 10f, RadialSegments = 20 },
            Visible = false,
        };
        _wake.SetSurfaceOverrideMaterial(0, _wakeMat);
        AddChild(_wake);

        // Break-up VFX lives as a sibling effect at the same render origin. We host it
        // here (rather than in SimulationBridge) so the plasma + break-up re-entry
        // effects are created and torn down together. It watches the active vessel's
        // thermal-destruction state on its own.
        // El breakup cuelga del mismo origen de render; observa la destrucción térmica solo.
        AddChild(new ReentryBreakupController { Name = "ReentryBreakup" });
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

        // ── Windward concentration ─────────────────────────────────────────
        // The visible bow shock should sit on, and brighten with, the face that
        // actually MEETS the flow — exactly the windward face the heat model uses.
        // We express the airflow in the vessel's local frame and read how squarely
        // the ventral heat shield faces it: belly-first (good attitude) lights the
        // ventral cap hard; a bad attitude spreads a hotter, more chaotic glow.
        //
        // Concentración windward: el shock se ata a la cara que encara el flujo
        // (la misma que usa el modelo térmico), brillando con su alineación real.
        Vector3d flowLocal = vessel.Orientation.Inverse().Rotate(
            new Vector3d(surfVel.X, surfVel.Y, surfVel.Z));
        double windward = ThermalModel.WindwardFactor(flowLocal);   // 1 = belly squarely into flow

        // Vessel body centre at the render origin; full stack sits higher than a lone Starship.
        bool hasSH = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        Vector3 bodyCentre = new Vector3(0f, hasSH ? 30f : 8f, 0f);

        // Shock sits on the windward (leading) face; wake streams out behind.
        _shock.Position = bodyCentre + flowDir * (hasSH ? 5f : 1.35f);
        _wake.Position  = bodyCentre - flowDir * (hasSH ? 7f : 4.0f);

        // Flatten the shock into the flow plane so it reads as a bow cap hugging the
        // windward face rather than a round ball. Squash along the flow axis.
        OrientYAxis(_shock, flowDir);

        // Orient the wake cylinder (+Y axis) to point downstream (away from the flow).
        OrientYAxis(_wake, -flowDir);

        // Flicker so the plasma boils rather than glowing flat.
        float flicker = 0.8f + (float)(GD.Randf() * 0.4f);

        // A well-shielded belly-first re-entry burns brightest and tightest on the
        // ventral cap; a turned-away (bare-side) attitude is hotter overall but the
        // glow smears and reddens (no shield deflecting the flux).
        float align     = (float)windward;                       // 0..1
        float concentr  = Mathf.Lerp(0.55f, 1.0f, align);        // ventral focus
        float exposure  = Mathf.Lerp(1.15f, 1.0f, align);        // bare side runs hotter

        // Colour shifts orange → white-hot as intensity rises; bare-side bias reddens it.
        float white = (float)intensity * concentr;
        var shockCol = new Color(
            1.0f,
            0.40f + 0.50f * white,
            0.08f + 0.72f * white,
            (float)(intensity * 0.28f * flicker) * (0.6f + 0.4f * concentr));
        _shockMat.AlbedoColor              = shockCol;
        _shockMat.Emission                 = new Color(shockCol.R, shockCol.G, shockCol.B);
        _shockMat.EmissionEnergyMultiplier = (float)(0.8 + intensity * 2.0) * flicker * exposure;

        _wakeMat.AlbedoColor              = new Color(1.0f, 0.28f, 0.08f, (float)(intensity * 0.16f));
        _wakeMat.EmissionEnergyMultiplier = (float)(0.7 + intensity * 1.4);

        // Windward cap: flatten along the flow (thin, wide bow shock) and grow with flux.
        float sizeScale = Mathf.Lerp(0.45f, 1.0f, (float)intensity);
        float flatten   = Mathf.Lerp(0.85f, 0.45f, align);       // belly-first = thinner cap
        // Mesh local +Y now points along the flow (set by OrientYAxis above), so
        // squash Y to press the cap onto the windward face.
        _shock.Scale = new Vector3(sizeScale * (1f + 0.4f * align), sizeScale * flatten, sizeScale * (1f + 0.4f * align));
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
