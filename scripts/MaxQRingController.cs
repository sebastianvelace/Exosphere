namespace Exosphere.Game;

using Godot;

/// <summary>
/// Renders a Prandtl-Glauert condensation ring around the rocket body
/// when dynamic pressure exceeds ~15 kPa (roughly Mach 1 in the troposphere).
/// The ring is a flat torus centred on the active vessel, fading in/out with q.
/// </summary>
[GlobalClass]
public partial class MaxQRingController : Node3D
{
    private MeshInstance3D? _ring;
    private StandardMaterial3D? _mat;

    const double Q_THRESH = 12_000.0;   // Pa: ring starts appearing
    const double Q_PEAK   = 35_000.0;   // Pa: ring at full opacity
    const double RHO0     = 1.225;      // kg/m³ sea-level air density
    const double H_SCALE  = 8500.0;     // m  atmosphere scale height

    public override void _Ready()
    {
        // Torus ring centred on vessel (render origin via FloatingOrigin)
        var torus = new TorusMesh
        {
            InnerRadius  = 1.10f,
            OuterRadius  = 1.55f,
            Rings        = 40,
            RingSegments = 10,
        };

        _mat = new StandardMaterial3D
        {
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor              = new Color(0.80f, 0.90f, 1.00f, 0f),
            EmissionEnabled          = true,
            Emission                 = new Color(0.70f, 0.85f, 1.00f),
            EmissionEnergyMultiplier = 1.8f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _ring = new MeshInstance3D
        {
            Name    = "MaxQRing",
            Mesh    = torus,
            Visible = false,
        };
        _ring.SetSurfaceOverrideMaterial(0, _mat);
        AddChild(_ring);
    }

    public override void _Process(double delta)
    {
        if (_ring == null || _mat == null) return;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var earth  = bridge?.Universe.GetBody("earth");
        if (vessel == null || earth == null) { _ring.Visible = false; return; }

        double alt      = vessel.GetAltitude(earth);
        double relSpeed = (vessel.Velocity - earth.Velocity).Magnitude;
        double rho      = RHO0 * System.Math.Exp(-alt / H_SCALE);
        double q        = 0.5 * rho * relSpeed * relSpeed;

        double intensity = System.Math.Clamp((q - Q_THRESH) / (Q_PEAK - Q_THRESH), 0.0, 1.0);

        if (intensity < 0.01)
        {
            _ring.Visible = false;
            return;
        }

        _ring.Visible = true;

        // Ring follows vessel (at render origin); position at Starship body midpoint
        // Rough heuristic: standalone Starship CoM is at y≈8; full stack is at y≈30.
        bool hasSH = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        _ring.Position = new Vector3(0, hasSH ? 30f : 8f, 0);

        // Flicker to simulate condensation turbulence
        float flicker  = 0.75f + (float)(GD.Randf() * 0.50f);
        float alpha    = (float)(intensity * 0.55f * flicker);
        _mat.AlbedoColor              = new Color(0.80f, 0.92f, 1.00f, alpha);
        _mat.EmissionEnergyMultiplier = (float)(1.0 + intensity * 2.5);

        // Ring slightly squashes in ascent to form an ellipse perpendicular to velocity
        float squat = Mathf.Lerp(1.0f, 0.25f, (float)intensity);
        _ring.Scale = new Vector3(1f, squat, 1f);
    }
}
