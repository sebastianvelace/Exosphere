namespace Exosphere.Game;

using Godot;

/// <summary>
/// Drives the ProceduralSkyMaterial + WorldEnvironment based on vessel altitude.
/// Ground (0–10 km): bright blue troposphere sky (Rayleigh look).
/// Transition (10–70 km): smoothly darkens through stratosphere/mesosphere.
/// Space (80+ km): pure-black deep space — sky energy and ambient energy ~0,
/// so the starfield + planet read against true black regardless of zoom.
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

    // ── Space sky — PURE BLACK (no residual colour cast at orbit) ─────────
    static readonly Color S_Black = new(0.0f, 0.0f, 0.0f);

    // ── Ambient light ─────────────────────────────────────────────────────
    static readonly Color A_Ground = new(0.55f, 0.70f, 1.00f);   // bluish sky light
    static readonly Color A_Space  = new(0.0f,  0.0f,  0.0f);    // no fake sky light

    // Altitude bands (metres of altitude over the dominant body):
    //   < TRANS_LOW           → full ground-level atmosphere sky.
    //   TRANS_LOW → TRANS_HIGH → blend ground → pure black.
    //   > TRANS_HIGH          → fully space (pure black, ~0 ambient).
    const double TRANS_LOW  = 10_000.0;   // m: sky starts darkening
    const double TRANS_HIGH = 80_000.0;   // m: fully black space

    // Ground-level sky/ambient energies (driven to ~0 in space).
    const float SKY_ENERGY_GROUND = 1.0f;
    const float AMB_ENERGY_EARTH  = 0.45f;
    const float AMB_ENERGY_MARS   = 0.35f;

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
        // f: 0 at ground, 1 in space. Smoothstep, then bias toward black so the
        // upper atmosphere is essentially black well before the hard ceiling.
        float f = Smooth(t);
        float fBlack = Smooth(f);   // double-smoothed → blue collapses early, black holds

        // Pick the ground-level palette for the current body (default: Earth blue).
        bool isMars = bodyId == "mars";
        Color gTop = isMars ? M_Top : G_Top;
        Color gHor = isMars ? M_Horizon : G_Horizon;
        Color gGH  = isMars ? M_GndH : G_GndH;
        Color gGB  = isMars ? M_GndB : G_GndB;
        Color aGnd = isMars ? M_Ambient : A_Ground;
        float aGndE = isMars ? AMB_ENERGY_MARS : AMB_ENERGY_EARTH;

        if (_skyMat != null)
        {
            // Every band of the sky collapses to pure black in space — no blue cast.
            _skyMat.SkyTopColor        = gTop.Lerp(S_Black, fBlack);
            _skyMat.SkyHorizonColor    = gHor.Lerp(S_Black, fBlack);
            _skyMat.GroundHorizonColor = gGH.Lerp(S_Black, fBlack);
            _skyMat.GroundBottomColor  = gGB.Lerp(S_Black, fBlack);
            // Sun glow tightens as atmosphere thins (Mars sun is smaller/dimmer).
            _skyMat.SunAngleMax = Mathf.Lerp(isMars ? 3.5f : 6.0f, 1.0f, f);
            // Energy multiplier scales the whole sky; → ~0 so even the sun-disk
            // scatter can't wash the background. Sun disk itself still draws.
            _skyMat.EnergyMultiplier = Mathf.Lerp(SKY_ENERGY_GROUND, 0.0f, fBlack);
        }

        if (_env != null)
        {
            // Ambient → black with ~0 energy in space: nothing lit by a fake sky.
            _env.AmbientLightColor  = aGnd.Lerp(A_Space, fBlack);
            _env.AmbientLightEnergy = Mathf.Lerp(aGndE, 0.0f, fBlack);

            // Background (sky) energy also collapses, so an environment whose
            // background is the Sky reads as true black in orbit at any zoom.
            _env.BackgroundEnergyMultiplier = Mathf.Lerp(1.0f, 0.0f, fBlack);

            // Kill any atmospheric fog wash at altitude (depth/height fog → off).
            _env.FogEnabled = f < 0.5f && _env.FogEnabled;
        }
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
}
