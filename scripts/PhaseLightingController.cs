namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;

/// <summary>
/// Drives the scene <see cref="WorldEnvironment"/> and the sun
/// <see cref="DirectionalLight3D"/> by flight phase so lighting reads correctly in
/// both regimes instead of using one global look.
///
/// The core issue this fixes: a single global environment cannot be right for both
/// the daylit pad (needs bright sky-blue ambient fill) and space (needs almost no
/// fill, high contrast, and HDR bloom so the sun and steel specular pop). A global
/// ACES/glow pass either washed the pad or left the ship subexposed in orbit.
///
/// Rather than switch discretely on the mission FSM (which snaps), this blends
/// smoothly on ALTITUDE — a robust proxy for "how much atmosphere/sky is around
/// you". Below <see cref="AtmoBlendLow"/> we keep the validated pad/ascent daylight
/// look; above <see cref="AtmoBlendHigh"/> we reach the full space look; in between
/// it interpolates.
///
/// Tonemapping stays Filmic (verified to expose the steel well). We only move the
/// ambient fill down, ramp HDR glow up, and lift the sun energy for stronger
/// specular. <see cref="SunController"/> owns the light's ORIENTATION and never
/// touches energy, so there is no conflict.
/// </summary>
[GlobalClass]
public partial class PhaseLightingController : Node
{
    /// <summary>Altitude (m) at/below which the full atmospheric/pad look applies.</summary>
    private const float AtmoBlendLow  = 70_000f;

    /// <summary>Altitude (m) at/above which the full space look applies.</summary>
    private const float AtmoBlendHigh = 130_000f;

    // Ambient fill energy: bright bluish sky fill on the pad → near-dark in space.
    private const float AmbientEnergyPad   = 0.45f;
    private const float AmbientEnergySpace = 0.12f;

    // Sun directional energy: baseline on the pad → stronger in vacuum so steel
    // specular reads against the black background.
    private const float SunEnergyPad   = 1.5f;
    private const float SunEnergySpace = 1.95f;

    // HDR glow (bloom): off on the diffuse pad → on in space, where the sun disc,
    // bright Earth limb and steel specular are genuine HDR hotspots.
    private const float GlowIntensitySpace = 0.6f;

    private Godot.Environment? _env;
    private DirectionalLight3D? _light;

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var av = bridge?.ActiveVessel;
        if (av == null) return;

        EnsureRefs();
        if (_env == null) return;

        var body = bridge!.Universe.GetDominantBody(av.Position);
        double alt = av.GetAltitude(body);
        float s = Smoothstep(AtmoBlendLow, AtmoBlendHigh, (float)alt);

        // Ambient fill drops toward space → higher contrast, more metallic ship.
        _env.AmbientLightEnergy = Mathf.Lerp(AmbientEnergyPad, AmbientEnergySpace, s);

        // Glow stays enabled but its intensity ramps from 0, so the pad (s = 0) gets
        // no bloom and space gets the full HDR pop. Only pixels above the HDR
        // threshold bloom, so the UI (separate CanvasLayer) and diffuse steel stay clean.
        _env.GlowEnabled       = true;
        _env.GlowIntensity     = Mathf.Lerp(0.0f, GlowIntensitySpace, s);
        _env.GlowStrength      = 0.9f;
        _env.GlowBloom         = 0.05f;
        _env.GlowBlendMode     = Godot.Environment.GlowBlendModeEnum.Additive;
        _env.GlowHdrThreshold  = 1.0f;

        if (_light != null)
            _light.LightEnergy = Mathf.Lerp(SunEnergyPad, SunEnergySpace, s);
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
