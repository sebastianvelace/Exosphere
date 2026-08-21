namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Drives the scene <see cref="WorldEnvironment"/> and the sun
/// <see cref="DirectionalLight3D"/> by flight phase so lighting reads correctly in
/// both regimes instead of using one global look.
///
/// Rather than switch discretely on the mission FSM (which snaps), this blends
/// smoothly on ALTITUDE — a robust proxy for "how much atmosphere/sky is around
/// you". Below <see cref="AtmoBlendLow"/> we keep the validated pad/ascent daylight
/// look; above <see cref="AtmoBlendHigh"/> we reach the full space look; in between
/// it interpolates.
///
/// Re-entry adds a second overlay driven by the same convective heat flux the plasma
/// VFX uses (<see cref="ThermalModel.ComputeHeatFlux"/>), optionally primed by
/// mission descent phases. When plasma is hot, ambient and sun dim so the emissive
/// fireball dominates; glow ramps so the shock reads without washing HUD/cockpit.
///
/// Tonemapping stays Filmic. <see cref="SunController"/> owns the light's
/// ORIENTATION; <see cref="SkyController"/> owns ambient COLOUR; this controller
/// is the sole writer of ambient ENERGY (V-039). Re-entry may overlay warm ambient
/// colour on top of the sky palette when plasma is active.
/// </summary>
[GlobalClass]
public partial class PhaseLightingController : Node
{
    private const float AmbientEnergyPad   = 0.45f;
    private const float AmbientEnergySpace = 0.18f;
    private const float SunEnergyPad   = 1.5f;
    private const float SunEnergySpace = 1.95f;
    private const float GlowIntensitySpace = 0.6f;

    private const double FluxThresh = VehicleVisualPhysics.VisibleReentryFluxWm2;
    private const double FluxPeak   = VehicleVisualPhysics.SaturatedReentryFluxWm2;
    private const float AmbientEnergyReentry = 0.08f;
    private const float SunEnergyReentry     = 0.72f;
    private const float GlowIntensityReentry = 0.95f;
    private static readonly Color AmbientColorReentry = new(0.88f, 0.38f, 0.16f);

    private const float CockpitAmbientBoost  = 0.08f;
    private const float CockpitGlowReduction = 0.18f;
    private const double DirectTransmittanceCadenceSeconds = 0.10;
    private const double DirectAltitudeRefreshMeters = 2_000.0;
    private const double DirectSunDirectionDotThreshold = 0.9995;

    private Godot.Environment? _env;
    private DirectionalLight3D? _light;
    private double _directTransmittanceAccumulator = double.MaxValue;
    private Vector3d _cachedDirectTransmittance = new(1.0, 1.0, 1.0);
    private string? _cachedDirectBodyId;
    private double _cachedDirectAltitude = double.NaN;
    private double _cachedDirectSunElevation = double.NaN;
    private Vector3d _cachedDirectSunDirection = Vector3d.Zero;
    private const double PresentationSamplePeriodSeconds = 1.0 / 20.0;
    private double _presentationSampleTimer;
    private Vessel? _sampledVessel;
    private Universe? _sampledUniverse;
    private CelestialBody? _sampledBody;
    private AtmosphereOptics? _sampledOptics;
    private double _sampledAltitude;
    private double _sampledOpticalAir;
    private float _sampledReentry;
    private double _sampledSunElevation;
    private Vector3d _sampledSunDirection = Vector3d.Up;

    public override void _Ready()
    {
        ProcessPriority = 10; // after SkyController so ambient energy is last-writer
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var av = bridge?.ActiveVessel;
        if (av == null) return;
        var universe = bridge?.Universe;
        if (universe == null) return;

        EnsureRefs();
        if (_env == null) return;
        _directTransmittanceAccumulator += double.IsFinite(delta) && delta > 0.0 ? delta : 0.0;

        _presentationSampleTimer -= System.Math.Max(0.0, delta);
        if (_presentationSampleTimer <= 0.0
            || !ReferenceEquals(av, _sampledVessel)
            || !ReferenceEquals(universe, _sampledUniverse))
        {
            _presentationSampleTimer = PresentationSamplePeriodSeconds;
            _sampledVessel = av;
            _sampledUniverse = universe;
            SampleLightingState(av, universe);
        }

        var body = _sampledBody;
        if (body == null) return;
        double alt = _sampledAltitude;
        var optics = _sampledOptics;
        double opticalAir = _sampledOpticalAir;
        float s = 1.0f - Smoothstep(0.0002f, 0.02f, (float)opticalAir);

        float ambient = Mathf.Lerp(AmbientEnergyPad, AmbientEnergySpace, s);
        float sun     = Mathf.Lerp(SunEnergyPad, SunEnergySpace, s);
        float glow    = Mathf.Lerp(0.0f, GlowIntensitySpace, s);

        float reentry = _sampledReentry;

        if (reentry > 0.001f)
        {
            ambient = Mathf.Lerp(ambient, AmbientEnergyReentry, reentry);
            sun     = Mathf.Lerp(sun, SunEnergyReentry, reentry);
            glow    = Mathf.Lerp(glow, GlowIntensityReentry, reentry);
            var reentryColor = _env.AmbientLightColor.Lerp(AmbientColorReentry, reentry);
            if (ColorDiffers(_env.AmbientLightColor, reentryColor))
                _env.AmbientLightColor = reentryColor;
        }

        if (CameraController.Instance?.IsCockpitView == true)
        {
            ambient = Mathf.Min(AmbientEnergyPad, ambient + CockpitAmbientBoost * Mathf.Max(0.25f, reentry));
            glow = Mathf.Min(glow, 0.12f);
        }

        if (FloatDiffers(_env.AmbientLightEnergy, ambient))
            _env.AmbientLightEnergy = ambient;

        // These are presentation properties, not per-frame state. Avoid invalidating
        // the environment resource when the phase has converged to the same values.
        if (!_env.GlowEnabled) _env.GlowEnabled = true;
        if (FloatDiffers(_env.GlowIntensity, glow)) _env.GlowIntensity = glow;
        if (FloatDiffers(_env.GlowStrength, 0.9f)) _env.GlowStrength = 0.9f;
        if (FloatDiffers(_env.GlowBloom, 0.05f)) _env.GlowBloom = 0.05f;
        if (_env.GlowBlendMode != Godot.Environment.GlowBlendModeEnum.Additive)
            _env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
        if (FloatDiffers(_env.GlowHdrThreshold, 1.0f)) _env.GlowHdrThreshold = 1.0f;

        if (_light != null)
        {
            var sunDirection = _sampledSunDirection;
            double sunElevation = _sampledSunElevation;
            var direct = GetCachedDirectTransmittance(
                body, optics, alt, sunElevation, sunDirection);
            double peak = System.Math.Max(1e-6,
                System.Math.Max(direct.X, System.Math.Max(direct.Y, direct.Z)));
            var lightColor = new Color(
                (float)(direct.X / peak),
                (float)(direct.Y / peak),
                (float)(direct.Z / peak));
            double luminance = 0.2126 * direct.X + 0.7152 * direct.Y + 0.0722 * direct.Z;
            float lightEnergy = sun * (float)luminance * SunController.SolarVisibility;
            float elevationGate = Smoothstep(-0.04f, 0.08f, (float)_sampledSunElevation);
            lightEnergy *= elevationGate;
            if (ColorDiffers(_light.LightColor, lightColor))
                _light.LightColor = lightColor;
            if (FloatDiffers(_light.LightEnergy, lightEnergy))
                _light.LightEnergy = lightEnergy;
        }
    }

    private void SampleLightingState(Vessel av, Universe universe)
    {
        _sampledBody = universe.GetDominantBody(av.Position);
        _sampledOptics = _sampledBody?.Atmosphere?.Optics;
        _sampledAltitude = 0.0;
        _sampledOpticalAir = 0.0;
        _sampledReentry = 0.0f;
        _sampledSunElevation = 1.0;
        _sampledSunDirection = Vector3d.Up;

        if (_sampledBody == null) return;

        _sampledAltitude = av.GetAltitude(_sampledBody);
        if (_sampledOptics != null)
            _sampledOpticalAir = System.Math.Max(
                _sampledOptics.RayleighDensity(_sampledAltitude),
                _sampledOptics.MieDensity(_sampledAltitude));

        double density = _sampledBody.GetAtmosphericDensity(av.Position);
        Vector3d surfVel = av.GetSurfaceVelocity(_sampledBody);
        _sampledReentry = ComputeReentryFactor(
            av, _sampledBody, _sampledAltitude, density, surfVel);

        Vector3d up = _sampledBody.GetGeodeticUp(av.Position);
        var sun = universe.GetBody("sun");
        if (sun != null)
        {
            var physicalDirection = (sun.Position - av.Position).Normalized;
            _sampledSunDirection = SunController.Instance != null
                ? SunController.Instance.GetVisualSunDirection(
                    _sampledBody, av.Position, physicalDirection)
                : physicalDirection;
            _sampledSunElevation = up.Dot(_sampledSunDirection);
        }
    }

    private Vector3d GetCachedDirectTransmittance(
        CelestialBody body, AtmosphereOptics? optics, double altitude,
        double sunElevation, Vector3d sunDirection)
    {
        if (optics == null)
        {
            _cachedDirectTransmittance = new Vector3d(1.0, 1.0, 1.0);
            _cachedDirectBodyId = body.Id;
            _cachedDirectAltitude = altitude;
            _cachedDirectSunElevation = sunElevation;
            _cachedDirectSunDirection = sunDirection;
            _directTransmittanceAccumulator = 0.0;
            return _cachedDirectTransmittance;
        }

        bool bodyChanged = _cachedDirectBodyId != body.Id;
        bool horizonChanged = double.IsFinite(_cachedDirectSunElevation)
            && (_cachedDirectSunElevation >= 0.0) != (sunElevation >= 0.0);
        bool altitudeChanged = !double.IsFinite(_cachedDirectAltitude)
            || System.Math.Abs(altitude - _cachedDirectAltitude) >= DirectAltitudeRefreshMeters;
        bool directionChanged = _cachedDirectSunDirection == Vector3d.Zero
            || _cachedDirectSunDirection.Dot(sunDirection) < DirectSunDirectionDotThreshold;
        bool refresh = bodyChanged || horizonChanged || altitudeChanged || directionChanged
            || _directTransmittanceAccumulator >= DirectTransmittanceCadenceSeconds;
        if (!refresh) return _cachedDirectTransmittance;

        _cachedDirectTransmittance = optics.DirectSolarTransmittance(
            altitude,
            sunElevation,
            body.Radius,
            body.Atmosphere?.MaxAltitude ?? 0.0,
            sampleCount: 32);
        _cachedDirectBodyId = body.Id;
        _cachedDirectAltitude = altitude;
        _cachedDirectSunElevation = sunElevation;
        _cachedDirectSunDirection = sunDirection;
        _directTransmittanceAccumulator = 0.0;
        return _cachedDirectTransmittance;
    }

    private static float ComputeReentryFactor(Vessel av, CelestialBody body,
        double alt, double density, Vector3d surfVel)
    {
        double flux     = av.ComputeStagnationHeatFlux(density, surfVel);
        float fluxFactor  = (float)System.Math.Clamp(
            (flux - FluxThresh) / (FluxPeak - FluxThresh), 0.0, 1.0);

        float phaseFactor = 0f;
        var mission = MissionManager.Instance;
        if (mission?.InDescent == true && alt < 120_000.0)
        {
            Vector3d up = (av.Position - body.Position).Normalized;
            double vUp = surfVel.Dot(up);
            if (vUp < -20.0)
            {
                phaseFactor = mission.Phase switch
                {
                    MissionPhase.ENTRY         => 0.35f,
                    MissionPhase.PEAK_HEATING    => 0.70f,
                    MissionPhase.AERO_DESCENT    => 0.22f,
                    MissionPhase.FINAL_DESCENT   => 0.12f,
                    _                            => 0f,
                };
            }
        }

        return Mathf.Max(fluxFactor, phaseFactor);
    }

    private void EnsureRefs()
    {
        if (_env == null || !IsInstanceValid(_env))
        {
            var wenv = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
            _env = wenv?.Environment;
        }
        if (_light == null || !IsInstanceValid(_light))
            _light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static bool FloatDiffers(float a, float b) =>
        float.IsNaN(a) || float.IsNaN(b) || Mathf.Abs(a - b) > 1e-4f;

    private static bool ColorDiffers(Color a, Color b) =>
        FloatDiffers(a.R, b.R)
        || FloatDiffers(a.G, b.G)
        || FloatDiffers(a.B, b.B)
        || FloatDiffers(a.A, b.A);
}
