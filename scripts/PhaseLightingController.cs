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
    private const float AmbientEnergySpace = 0.12f;
    private const float SunEnergyPad   = 1.5f;
    private const float SunEnergySpace = 1.95f;
    private const float GlowIntensitySpace = 0.6f;

    private const double FluxThresh = 5.0e4;
    private const double FluxPeak   = 6.0e5;
    private const float AmbientEnergyReentry = 0.10f;
    private const float SunEnergyReentry     = 0.90f;
    private const float GlowIntensityReentry = 0.80f;
    private static readonly Color AmbientColorReentry = new(0.82f, 0.42f, 0.20f);

    private const float CockpitAmbientBoost  = 0.08f;
    private const float CockpitGlowReduction = 0.18f;

    private Godot.Environment? _env;
    private DirectionalLight3D? _light;

    public override void _Ready()
    {
        ProcessPriority = 10; // after SkyController so ambient energy is last-writer
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var av = bridge?.ActiveVessel;
        if (av == null) return;

        EnsureRefs();
        if (_env == null) return;

        var body = bridge!.Universe.GetDominantBody(av.Position);
        double alt = av.GetAltitude(body);
        var optics = body.Atmosphere?.Optics;
        double opticalAir = optics == null ? 0.0 : System.Math.Max(
            optics.RayleighDensity(alt), optics.MieDensity(alt));
        float s = 1.0f - Smoothstep(0.0002f, 0.02f, (float)opticalAir);

        float ambient = Mathf.Lerp(AmbientEnergyPad, AmbientEnergySpace, s);
        float sun     = Mathf.Lerp(SunEnergyPad, SunEnergySpace, s);
        float glow    = Mathf.Lerp(0.0f, GlowIntensitySpace, s);

        float reentry = ComputeReentryFactor(bridge, av, body, alt);

        if (reentry > 0.001f)
        {
            ambient = Mathf.Lerp(ambient, AmbientEnergyReentry, reentry);
            sun     = Mathf.Lerp(sun, SunEnergyReentry, reentry);
            glow    = Mathf.Lerp(glow, GlowIntensityReentry, reentry);
            _env.AmbientLightColor = _env.AmbientLightColor.Lerp(AmbientColorReentry, reentry);
        }

        if (CameraController.Instance?.IsCockpitView == true && reentry > 0.01f)
        {
            ambient = Mathf.Min(AmbientEnergyPad, ambient + CockpitAmbientBoost * reentry);
            glow    = Mathf.Max(0.0f, glow - CockpitGlowReduction * reentry);
        }

        _env.AmbientLightEnergy = ambient;

        _env.GlowEnabled      = true;
        _env.GlowIntensity    = glow;
        _env.GlowStrength     = 0.9f;
        _env.GlowBloom        = 0.05f;
        _env.GlowBlendMode    = Godot.Environment.GlowBlendModeEnum.Additive;
        _env.GlowHdrThreshold = 1.0f;

        if (_light != null)
        {
            var sunBody = bridge.Universe.GetBody("sun");
            var up = (av.Position - body.Position).Normalized;
            double sunElevation = sunBody != null
                ? up.Dot((sunBody.Position - av.Position).Normalized)
                : 1.0;
            var direct = optics?.DirectSolarTransmittance(alt, sunElevation)
                ?? new Vector3d(1.0, 1.0, 1.0);
            double peak = System.Math.Max(1e-6,
                System.Math.Max(direct.X, System.Math.Max(direct.Y, direct.Z)));
            _light.LightColor = new Color(
                (float)(direct.X / peak),
                (float)(direct.Y / peak),
                (float)(direct.Z / peak));
            double luminance = 0.2126 * direct.X + 0.7152 * direct.Y + 0.0722 * direct.Z;
            _light.LightEnergy = sun * (float)luminance * SunController.SolarVisibility;
        }
    }

    private static float ComputeReentryFactor(SimulationBridge bridge, Vessel av,
        CelestialBody body, double alt)
    {
        double density  = body.GetAtmosphericDensity(av.Position);
        double airspeed = av.GetSurfaceVelocity(body).Magnitude;
        double flux     = ThermalModel.ComputeHeatFlux(
            density, airspeed, System.Math.Max(0.1, av.MaximumDiameter * 0.5));
        float fluxFactor  = (float)System.Math.Clamp(
            (flux - FluxThresh) / (FluxPeak - FluxThresh), 0.0, 1.0);

        float phaseFactor = 0f;
        var mission = MissionManager.Instance;
        if (mission?.InDescent == true && alt < 120_000.0)
        {
            Vector3d up = (av.Position - body.Position).Normalized;
            double vUp = av.GetSurfaceVelocity(body).Dot(up);
            if (vUp < -20.0)
            {
                phaseFactor = mission.Phase switch
                {
                    MissionPhase.ENTRY         => 0.30f,
                    MissionPhase.PEAK_HEATING    => 0.50f,
                    MissionPhase.AERO_DESCENT    => 0.20f,
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
}
