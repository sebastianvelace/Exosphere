namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;

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
    private double _visualSampleTimer;
    private Vessel? _lastSampledVessel;
    private bool _lastHasSuperHeavy;
    private Node3D? _vesselFrame;

    private const double VisualSamplePeriodSeconds = 1.0 / 20.0;
    private const double Q_THRESH = 12_000.0;   // Pa: ring starts appearing
    private const double Q_PEAK   = 35_000.0;   // Pa: ring at full opacity
    private const double RHO0     = 1.225;      // kg/m³ sea-level air density
    private const double H_SCALE  = 8500.0;     // m  atmosphere scale height

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

        _visualSampleTimer -= System.Math.Max(0.0, delta);
        if (_visualSampleTimer > 0.0) return;
        _visualSampleTimer = VisualSamplePeriodSeconds;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var earth  = bridge?.Universe.GetBody("earth");
        if (vessel == null || earth == null)
        {
            SetRingVisible(false);
            _lastSampledVessel = null;
            return;
        }

        // The ring is a child of Vessels, not of the active renderer. Mirror the renderer's
        // floating-origin transform so the condensation cue follows attitude changes instead
        // of remaining in the old inertial frame (which looked like a detached white halo in
        // EDL captures).
        SyncToVesselFrame();

        double alt      = vessel.GetAltitude(earth);
        double relSpeed = (vessel.Velocity - earth.Velocity).Magnitude;
        double rho      = RHO0 * System.Math.Exp(-alt / H_SCALE);
        double q        = 0.5 * rho * relSpeed * relSpeed;

        double intensity = System.Math.Clamp((q - Q_THRESH) / (Q_PEAK - Q_THRESH), 0.0, 1.0);

        if (intensity < 0.01)
        {
            SetRingVisible(false);
            return;
        }

        SetRingVisible(true);

        // Ring follows vessel (at render origin); position at Starship body midpoint
        // Rough heuristic: standalone Starship CoM is at y≈8; full stack is at y≈30.
        bool hasSH = HasSuperHeavy(vessel);
        if (!ReferenceEquals(vessel, _lastSampledVessel) || hasSH != _lastHasSuperHeavy)
        {
            _lastSampledVessel = vessel;
            _lastHasSuperHeavy = hasSH;
            _ring.Position = new Vector3(0, hasSH ? 30f : 8f, 0);
        }

        // Flicker to simulate condensation turbulence
        float flicker  = 0.75f + (float)(GD.Randf() * 0.50f);
        float alpha    = (float)(intensity * 0.55f * flicker);
        _mat.AlbedoColor              = new Color(0.80f, 0.92f, 1.00f, alpha);
        _mat.EmissionEnergyMultiplier = (float)(1.0 + intensity * 2.5);

        // Ring slightly squashes in ascent to form an ellipse perpendicular to velocity
        float squat = Mathf.Lerp(1.0f, 0.25f, (float)intensity);
        _ring.Scale = new Vector3(1f, squat, 1f);
    }

    private void SetRingVisible(bool visible)
    {
        if (_ring != null && _ring.Visible != visible)
            _ring.Visible = visible;
    }

    private static bool HasSuperHeavy(Vessel vessel)
    {
        var parts = vessel.Parts.Parts;
        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            if (part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("booster"))
                return true;
        }
        return false;
    }

    private void SyncToVesselFrame()
    {
        if (_vesselFrame == null || !GodotObject.IsInstanceValid(_vesselFrame))
        {
            _vesselFrame = GetTree().Root.FindChild(
                "ActiveVesselRenderer", true, false) as Node3D;
        }

        if (_vesselFrame == null) return;
        Position = _vesselFrame.Position;
        Quaternion = _vesselFrame.Quaternion;
    }
}
