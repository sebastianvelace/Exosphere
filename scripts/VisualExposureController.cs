namespace Exosphere.Game;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Visual;
using Godot;

/// <summary>
/// Drives pre-tonemap exposure with an asymmetric human-eye adaptation model.
/// The luminance proxy combines atmospheric daylight, illuminated vehicle surfaces,
/// eclipse visibility and re-entry plasma; it deliberately changes continuously rather
/// than switching between hand-authored flight phases.
/// </summary>
[GlobalClass]
public partial class VisualExposureController : Node
{
    private readonly ExposureAdaptation _adaptation = new();
    private Godot.Environment? _environment;
    private ShaderMaterial? _skyMaterial;

    public override void _Ready() => ProcessPriority = 20;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;

        EnsureReferences();
        if (_environment == null) return;

        var body = bridge.Universe.GetDominantBody(vessel.Position);
        var optics = body.Atmosphere?.Optics;
        double altitude = vessel.GetAltitude(body);
        Vector3d up = (vessel.Position - body.Position).Normalized;
        var sun = bridge.Universe.GetBody("sun");
        double sunElevation = sun == null
            ? 1.0
            : up.Dot((sun.Position - vessel.Position).Normalized);

        double air = optics == null ? 0.0 : System.Math.Max(
            optics.RayleighDensity(altitude), optics.MieDensity(altitude));
        double daylight = Smoothstep(-0.12, 0.03, sunElevation);
        Vector3d direct = optics?.DirectSolarTransmittance(altitude, sunElevation)
            ?? new Vector3d(1.0, 1.0, 1.0);
        double directLuminance = 0.2126 * direct.X + 0.7152 * direct.Y + 0.0722 * direct.Z;

        double density = body.GetAtmosphericDensity(vessel.Position);
        double speed = vessel.GetSurfaceVelocity(body).Magnitude;
        double heatFlux = ThermalModel.ComputeHeatFlux(
            density, speed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
        double plasma = Smoothstep(5.0e4, 6.0e5, heatFlux);

        // Relative field luminance: diffuse sky dominates in atmosphere; direct light
        // represents sunlit cabin/vehicle surfaces, with plasma acting as a bright source.
        double skyLuminance = 0.22 * System.Math.Clamp(air, 0.0, 1.0) * daylight;
        double surfaceLuminance = 0.16 * directLuminance * SunController.SolarVisibility;
        double sceneLuminance = 0.0004 + skyLuminance + surfaceLuminance + 0.55 * plasma;
        if (CameraController.Instance?.IsCockpitView == true)
            sceneLuminance *= 0.72;

        double target = ExposureAdaptation.TargetForLuminance(sceneLuminance);
        float exposure = (float)_adaptation.Update(target, delta);
        _environment.TonemapExposure = exposure;

        // Star visibility remains governed by local sky luminance in the shader; this gain
        // adds the slower retinal response, preventing instant stars after entering shadow.
        _skyMaterial?.SetShaderParameter("eye_star_gain",
            Mathf.Clamp((exposure - 0.65f) / 3.35f, 0.0f, 1.0f));
    }

    private void EnsureReferences()
    {
        if (_environment == null || !IsInstanceValid(_environment))
        {
            var world = GetTree().Root.FindChild("WorldEnvironment", true, false)
                as WorldEnvironment;
            _environment = world?.Environment;
        }

        if (_skyMaterial == null || !IsInstanceValid(_skyMaterial))
        {
            var world = GetTree().Root.FindChild("WorldEnvironment", true, false)
                as WorldEnvironment;
            _skyMaterial = world?.Environment?.Sky?.SkyMaterial as ShaderMaterial;
        }
    }

    private static double Smoothstep(double low, double high, double value)
    {
        double t = System.Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
