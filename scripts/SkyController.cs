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

    // ── Mars ground-level sky (thin dusty CO₂ atmosphere) ─────────────────
    static readonly Color M_Top     = new(0.45f, 0.28f, 0.18f);   // butterscotch zenith
    static readonly Color M_Horizon = new(0.82f, 0.46f, 0.24f);   // dusty orange horizon
    static readonly Color M_GndH    = new(0.55f, 0.30f, 0.16f);   // rust ground horizon
    static readonly Color M_GndB    = new(0.30f, 0.16f, 0.10f);   // dark rust base
    static readonly Color M_Ambient = new(0.95f, 0.70f, 0.50f);   // warm dusty light

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
        UpdateSky(0.0f, "earth");   // start at ground level
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        // Sky profile follows whichever body currently dominates the vessel.
        var body = universe.GetDominantBody(vessel.Position);
        double alt = vessel.GetAltitude(body);
        double raw = System.Math.Clamp((alt - TRANS_LOW) / (TRANS_HIGH - TRANS_LOW), 0.0, 1.0);
        UpdateSky((float)raw, body.Id);
    }

    private void UpdateSky(float t, string bodyId)
    {
        float f = Smooth(t);

        // Pick the ground-level palette for the current body (default: Earth blue).
        bool isMars = bodyId == "mars";
        Color gTop = isMars ? M_Top : G_Top;
        Color gHor = isMars ? M_Horizon : G_Horizon;
        Color gGH  = isMars ? M_GndH : G_GndH;
        Color gGB  = isMars ? M_GndB : G_GndB;
        Color aGnd = isMars ? M_Ambient : A_Ground;

        if (_skyMat != null)
        {
            _skyMat.SkyTopColor      = gTop.Lerp(S_Top, f);
            _skyMat.SkyHorizonColor  = gHor.Lerp(S_Horizon, f);
            _skyMat.GroundHorizonColor = gGH.Lerp(S_GndH, f);
            _skyMat.GroundBottomColor  = gGB.Lerp(S_GndB, f);
            // Sun glow fades as atmosphere thins (Mars sun is smaller/dimmer).
            _skyMat.SunAngleMax = Mathf.Lerp(isMars ? 3.5f : 6.0f, 1.0f, f);
        }

        if (_env != null)
        {
            _env.AmbientLightColor  = aGnd.Lerp(A_Space, f);
            _env.AmbientLightEnergy = Mathf.Lerp(isMars ? 0.35f : 0.45f, 0.08f, f);
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
