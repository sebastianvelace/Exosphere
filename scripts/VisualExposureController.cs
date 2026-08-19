namespace Exosphere.Game;

using Exosphere.Simulation;
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
    private const double DirectTransmittanceCadenceSeconds = 0.10;
    private readonly ExposureAdaptation _adaptation = new();
    private Godot.Environment? _environment;
    private ShaderMaterial? _skyMaterial;
    private double _directTransmittanceAccumulator = double.MaxValue;
    private Vector3d _cachedDirectTransmittance = new(1.0, 1.0, 1.0);
    private string? _cachedBodyId;
    private double _cachedAltitude;
    private double _cachedSunElevation;
    private float _lastEyeStarGain = float.NaN;
    private const double PresentationSamplePeriodSeconds = 1.0 / 20.0;
    private double _presentationSampleTimer;
    private Vessel? _sampledVessel;
    private Universe? _sampledUniverse;
    private CelestialBody? _sampledBody;
    private AtmosphereOptics? _sampledOptics;
    private double _sampledAltitude;
    private double _sampledSunElevation;
    private double _sampledAir;
    private double _sampledDensity;
    private Vector3d _sampledSurfaceVelocity;

    public override void _Ready() => ProcessPriority = 20;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null) return;

        EnsureReferences();
        if (_environment == null) return;

        var universe = bridge.Universe;
        _presentationSampleTimer -= System.Math.Max(0.0, delta);
        if (_presentationSampleTimer <= 0.0
            || !ReferenceEquals(vessel, _sampledVessel)
            || !ReferenceEquals(universe, _sampledUniverse))
        {
            _presentationSampleTimer = PresentationSamplePeriodSeconds;
            _sampledVessel = vessel;
            _sampledUniverse = universe;
            SampleExposureState(vessel, universe);
        }

        var body = _sampledBody;
        if (body == null) return;
        var optics = _sampledOptics;
        double altitude = _sampledAltitude;
        double sunElevation = _sampledSunElevation;
        double air = _sampledAir;
        double daylight = Smoothstep(-0.12, 0.03, sunElevation);
        _directTransmittanceAccumulator += delta;
        // The exposure integrator is intentionally a lower-rate consumer of flight state.
        // Altitude and solar elevation can change every physics tick during ascent, but a
        // 10 Hz optical sample is sufficient for the eye-adaptation time scale.  Only an
        // SOI/body transition bypasses the cadence so a new atmosphere is never displayed
        // with the previous body's direct beam.
        bool directStateChanged = _cachedBodyId != body.Id;
        if (optics != null && (_directTransmittanceAccumulator >= DirectTransmittanceCadenceSeconds
            || directStateChanged))
        {
            _cachedDirectTransmittance = optics.DirectSolarTransmittance(
                altitude,
                sunElevation,
                body.Radius,
                body.Atmosphere!.MaxAltitude,
                sampleCount: 32);
            _cachedBodyId = body.Id;
            _cachedAltitude = altitude;
            _cachedSunElevation = sunElevation;
            _directTransmittanceAccumulator = 0.0;
        }
        else if (optics == null)
        {
            _cachedDirectTransmittance = new Vector3d(1.0, 1.0, 1.0);
            _cachedBodyId = body.Id;
            _directTransmittanceAccumulator = 0.0;
        }
        Vector3d direct = _cachedDirectTransmittance;
        double directLuminance = 0.2126 * direct.X + 0.7152 * direct.Y + 0.0722 * direct.Z;

        double density = _sampledDensity;
        var    surfVel = _sampledSurfaceVelocity;
        double heatFlux = vessel.ComputeStagnationHeatFlux(density, surfVel);
        double plasma = Smoothstep(VehicleVisualPhysics.VisibleReentryFluxWm2,
            VehicleVisualPhysics.SaturatedReentryFluxWm2, heatFlux);

        // Relative field luminance: diffuse sky dominates in atmosphere; direct light
        // represents sunlit cabin/vehicle surfaces, with plasma acting as a bright source.
        double skyLuminance = 0.22 * System.Math.Clamp(air, 0.0, 1.0) * daylight;
        double surfaceLuminance = 0.16 * directLuminance * SunController.SolarVisibility;
        double sceneLuminance = 0.0004 + skyLuminance + surfaceLuminance + 0.55 * plasma;
        bool cockpit = CameraController.Instance?.IsCockpitView == true;
        if (cockpit)
            sceneLuminance = System.Math.Max(sceneLuminance, 0.056); // interior practical-light floor

        double target = ExposureAdaptation.TargetForLuminance(sceneLuminance);
        if (cockpit) target = System.Math.Min(target, 1.8);
        float exposure = (float)_adaptation.Update(target, delta);
        if (cockpit) exposure = Mathf.Min(exposure, 1.8f);
        // TonemapExposure is an Environment property. Avoid invalidating the
        // post-process resource when the adaptation has already converged to the
        // same value; the adaptation state itself still advances every frame.
        if (float.IsNaN(_environment.TonemapExposure)
            || Mathf.Abs(_environment.TonemapExposure - exposure) > 1e-4f)
            _environment.TonemapExposure = exposure;

        // Star visibility remains governed by local sky luminance in the shader; this gain
        // adds the slower retinal response, preventing instant stars after entering shadow.
        float darkAdaptation = Mathf.Clamp((exposure - 1.45f) / 3.55f, 0.0f, 1.0f);
        float photopicSuppression = (float)System.Math.Clamp(
            skyLuminance * 4.0 + surfaceLuminance * 6.0 + plasma, 0.0, 1.0);
        float eyeStarGain = darkAdaptation * (1.0f - photopicSuppression);
        // This is a custom sky uniform. Rewriting it every frame invalidates the
        // incremental sky cubemap even after exposure has settled. A 0.005 step is
        // below a visible star-luminance change but prevents a permanent rebuild loop.
        if (_skyMaterial != null
            && (float.IsNaN(_lastEyeStarGain)
                || System.Math.Abs(eyeStarGain - _lastEyeStarGain) > 0.005f))
        {
            _skyMaterial.SetShaderParameter("eye_star_gain", eyeStarGain);
            _lastEyeStarGain = eyeStarGain;
        }
    }

    private void SampleExposureState(Vessel vessel, Universe universe)
    {
        _sampledBody = universe.GetDominantBody(vessel.Position);
        _sampledOptics = _sampledBody?.Atmosphere?.Optics;
        _sampledAltitude = 0.0;
        _sampledSunElevation = 1.0;
        _sampledAir = 0.0;
        _sampledDensity = 0.0;
        _sampledSurfaceVelocity = Vector3d.Zero;

        if (_sampledBody == null) return;

        _sampledAltitude = vessel.GetAltitude(_sampledBody);
        Vector3d up = (vessel.Position - _sampledBody.Position).Normalized;
        var sun = universe.GetBody("sun");
        _sampledSunElevation = sun == null
            ? 1.0
            : up.Dot((sun.Position - vessel.Position).Normalized);

        if (_sampledOptics != null)
            _sampledAir = System.Math.Max(
                _sampledOptics.RayleighDensity(_sampledAltitude),
                _sampledOptics.MieDensity(_sampledAltitude));

        _sampledDensity = _sampledBody.GetAtmosphericDensity(vessel.Position);
        _sampledSurfaceVelocity = vessel.GetSurfaceVelocity(_sampledBody);
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
