namespace Exosphere.Game;

using Godot;

/// <summary>
/// Drives the sky + WorldEnvironment based on vessel altitude.
/// Ground (0–10 km): bright blue troposphere sky (Rayleigh look).
/// Transition (10–70 km): smoothly darkens through stratosphere/mesosphere.
/// Space (80+ km): no longer pure black — a real equirectangular Milky-Way /
/// star panorama owns the dome, with a glowing Sun disc + halo. The same altitude
/// bands cross-fade the blue atmosphere → star sky.
///
/// The Sky's material is swapped at runtime from the scene's ProceduralSkyMaterial
/// to a custom <c>space_sky.gdshader</c> (shader_type sky) so a single dome can carry
/// both the atmosphere gradient and the star backdrop. The physical Sun is rendered
/// separately at its real angular size.
/// </summary>
[GlobalClass]
public partial class SkyController : Node
{
    /// Live ground-horizon sky colour (so the local ground patch can haze to a matching tint).
    public static Color CurrentHorizonColor { get; private set; } = new(0.40f, 0.65f, 1.0f);

    private ShaderMaterial? _skyMat;
    private Environment?    _env;

    private const string SkyShaderPath = "res://assets/shaders/space_sky.gdshader";
    private const string StarTexPath   = "res://assets/textures/starmap_milkyway_8k.jpg";

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

    // ── Ambient light ─────────────────────────────────────────────────────
    static readonly Color A_Ground = new(0.55f, 0.70f, 1.00f);   // bluish sky light
    static readonly Color A_Space  = new(0.0f,  0.0f,  0.0f);    // no fake sky light

    // Altitude bands (metres of altitude over the dominant body):
    //   < TRANS_LOW           → full ground-level atmosphere sky.
    //   TRANS_LOW → TRANS_HIGH → blend ground → star sky.
    //   > TRANS_HIGH          → fully space (stars own the dome, ~0 atmosphere).
    const double TRANS_LOW  = 10_000.0;   // m: sky starts darkening
    const double TRANS_HIGH = 80_000.0;   // m: fully space / stars

    // Ground-level sky/ambient energies (driven to ~0 in space).
    const float SKY_ENERGY_GROUND = 1.0f;
    const float AMB_ENERGY_EARTH  = 0.45f;
    const float AMB_ENERGY_MARS   = 0.35f;

    // Star panorama brightness: dim so daytime ground sky isn't speckled, full in space.
    const float STAR_ENERGY = 0.9f;

    public override void _Ready()
    {
        ProcessPriority = -10; // before PhaseLightingController (ambient colour only)
        var wenv = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        _env = wenv?.Environment;

        // Swap the scene's ProceduralSkyMaterial for our combined atmosphere+stars shader.
        if (_env?.Sky != null)
        {
            var shader = GD.Load<Shader>(SkyShaderPath);
            _skyMat = new ShaderMaterial { Shader = shader };
            _skyMat.SetShaderParameter("star_tex", LoadStarTexture());
            _skyMat.SetShaderParameter("star_energy", STAR_ENERGY);
            _env.Sky.SkyMaterial = _skyMat;
            // Process the sky every frame even when nothing moves (sun/blend can change).
            _env.Sky.ProcessMode = Sky.ProcessModeEnum.Realtime;
        }

        UpdateSky(0.0f, "earth", 1.0f);   // start at ground level
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
        double raw = body.Atmosphere == null
            ? 1.0
            : System.Math.Clamp((alt - TRANS_LOW) / (TRANS_HIGH - TRANS_LOW), 0.0, 1.0);
        float daylight = 1f;
        var sun = universe.GetBody("sun");
        if (sun != null)
        {
            var up = (vessel.Position - body.Position).Normalized;
            var toSun = (sun.Position - vessel.Position).Normalized;
            // Atmospheric twilight begins while the Sun is still below the geometric
            // horizon. Airless bodies ignore this palette because raw=1 above.
            daylight = Smoothstep(-0.12f, 0.03f, (float)up.Dot(toSun));
        }
        UpdateSky((float)raw, body.Id, daylight);
    }

    private void UpdateSky(float t, string bodyId, float daylight)
    {
        // f: 0 at ground, 1 in space. Smoothstep, then bias toward space so the
        // upper atmosphere is essentially gone (stars showing) well before the ceiling.
        float f = Smooth(t);
        float fSpace = Smooth(f);   // double-smoothed → blue collapses early, stars hold

        // Pick the ground-level palette for the current body (default: Earth blue).
        bool isMars = bodyId == "mars";
        Color gTop = isMars ? M_Top : G_Top;
        Color gHor = isMars ? M_Horizon : G_Horizon;
        Color gGH  = isMars ? M_GndH : G_GndH;
        Color gGB  = isMars ? M_GndB : G_GndB;
        Color aGnd = isMars ? M_Ambient : A_Ground;

        if (_skyMat != null)
        {
            // Feed the atmosphere gradient (full-strength colours; energy fades the band).
            _skyMat.SetShaderParameter("sky_top_color", gTop);
            _skyMat.SetShaderParameter("sky_horizon_color", gHor);
            _skyMat.SetShaderParameter("ground_horizon", gGH);
            _skyMat.SetShaderParameter("ground_bottom", gGB);

            // Atmosphere energy → ~0 in space so the blue dome can't wash the stars.
            float atmoDayEnergy = Mathf.Lerp(0.015f, SKY_ENERGY_GROUND, daylight);
            _skyMat.SetShaderParameter("atmo_energy", Mathf.Lerp(atmoDayEnergy, 0.0f, fSpace));
            // Cross-fade blue atmosphere → star panorama.
            // Ground facilities/planet foreground keep the eye adapted; only a restrained
            // star field survives at surface exposure. Deep space still reaches full strength.
            float nightStars = (1f - daylight) * (1f - fSpace) * 0.38f;
            float starBlend = Mathf.Max(fSpace, nightStars);
            _skyMat.SetShaderParameter("space_blend", starBlend);
            // Brighten stars only as the sky darkens (kept faint near the ground).
            _skyMat.SetShaderParameter("star_energy", STAR_ENERGY * starBlend);

            CurrentHorizonColor = gGH.Lerp(new Color(0, 0, 0),
                Mathf.Max(fSpace, (1f - daylight) * 0.96f));
        }

        if (_env != null)
        {
            // Ambient colour only — energy is owned by PhaseLightingController (V-039).
            Color litAmbient = aGnd * Mathf.Lerp(0.035f, 1f, daylight);
            _env.AmbientLightColor = litAmbient.Lerp(A_Space, fSpace);

            // Keep the sky background visible at full strength so the stars (and the
            // Sun disc/halo) read in orbit at any zoom. The shader itself fades the
            // blue atmosphere via atmo_energy, so this no longer needs to crush to 0.
            _env.BackgroundEnergyMultiplier = 1.0f;

            // Kill any atmospheric fog wash at altitude (depth/height fog → off).
            _env.FogEnabled = f < 0.5f && _env.FogEnabled;
        }
    }

    /// <summary>
    /// Loads the 8K star panorama from disk at runtime (no .import dependency).
    /// Returns a tiny dark fallback if the file is missing so the dome stays black
    /// rather than erroring.
    /// </summary>
    private static Texture2D LoadStarTexture()
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(StarTexPath));
        if (img != null)
        {
            img.GenerateMipmaps();
            return ImageTexture.CreateFromImage(img);
        }
        var dark = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
        dark.Fill(new Color(0, 0, 0));
        return ImageTexture.CreateFromImage(dark);
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return Smooth(t);
    }
}
