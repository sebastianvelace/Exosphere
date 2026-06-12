namespace Exosphere.Game;

using Godot;

/// <summary>
/// Drives the ProceduralSkyMaterial based on vessel altitude.
/// Ground (0 km): bright blue troposphere sky.
/// Transition (10–80 km): smoothly darkens through stratosphere/mesosphere.
/// Space (80+ km): black starfield sky.
/// </summary>
[GlobalClass]
public partial class SkyController : Node
{
    private ProceduralSkyMaterial? _skyMat;
    private Environment?           _env;

    // ── Ground-level sky ──────────────────────────────────────────────────
    static readonly Color G_Top     = new(0.06f, 0.22f, 0.72f);   // deep blue zenith
    static readonly Color G_Horizon = new(0.40f, 0.65f, 1.00f);   // light blue horizon
    static readonly Color G_GndH    = new(0.30f, 0.55f, 0.90f);   // ground horizon tint
    static readonly Color G_GndB    = new(0.18f, 0.30f, 0.50f);   // ground base

    // ── Space sky ─────────────────────────────────────────────────────────
    static readonly Color S_Top     = new(0.004f, 0.004f, 0.012f);
    static readonly Color S_Horizon = new(0.012f, 0.016f, 0.035f);
    static readonly Color S_GndH    = new(0.008f, 0.010f, 0.020f);
    static readonly Color S_GndB    = new(0.0f,   0.0f,   0.0f);

    // ── Ambient light ─────────────────────────────────────────────────────
    static readonly Color A_Ground = new(0.55f, 0.70f, 1.00f);   // bluish sky light
    static readonly Color A_Space  = new(0.06f, 0.07f, 0.12f);   // faint starlight

    const double TRANS_LOW  =  8_000.0;   // m: sky starts darkening
    const double TRANS_HIGH = 80_000.0;   // m: fully space

    public override void _Ready()
    {
        var wenv = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        _env    = wenv?.Environment;
        _skyMat = _env?.Sky?.SkyMaterial as ProceduralSkyMaterial;
        UpdateSky(0.0f);   // start at ground level
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var earth  = bridge?.Universe.GetBody("earth");
        if (vessel == null || earth == null) return;

        double alt = vessel.GetAltitude(earth);
        double raw = System.Math.Clamp((alt - TRANS_LOW) / (TRANS_HIGH - TRANS_LOW), 0.0, 1.0);
        UpdateSky((float)raw);
    }

    private void UpdateSky(float t)
    {
        float f = Smooth(t);

        if (_skyMat != null)
        {
            _skyMat.SkyTopColor      = G_Top.Lerp(S_Top, f);
            _skyMat.SkyHorizonColor  = G_Horizon.Lerp(S_Horizon, f);
            _skyMat.GroundHorizonColor = G_GndH.Lerp(S_GndH, f);
            _skyMat.GroundBottomColor  = G_GndB.Lerp(S_GndB, f);
            // Sun glow fades as atmosphere thins
            _skyMat.SunAngleMax = Mathf.Lerp(6.0f, 1.0f, f);
        }

        if (_env != null)
        {
            _env.AmbientLightColor  = A_Ground.Lerp(A_Space, f);
            _env.AmbientLightEnergy = Mathf.Lerp(0.45f, 0.08f, f);
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
