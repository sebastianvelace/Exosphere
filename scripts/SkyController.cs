namespace Exosphere.Game;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;
using Godot;

/// <summary>
/// Binds the dominant body's physical optical atmosphere to the spherical single-scattering
/// sky shader. The shader integrates Rayleigh/Mie/ozone extinction from the vessel altitude,
/// so ground, twilight, limb and orbit use one continuous model instead of altitude palettes.
/// </summary>
[GlobalClass]
public partial class SkyController : Node
{
    public static Color CurrentHorizonColor { get; private set; } = new(0.40f, 0.65f, 1.0f);

    private const string SkyShaderPath = "res://assets/shaders/space_sky.gdshader";
    private const string StarTexPath = "res://assets/textures/starmap_milkyway_8k.jpg";
    private const string EarthCloudTexPath = "res://assets/textures/earth_clouds.jpg";
    private const string VenusCloudTexPath = "res://assets/textures/venus.jpg";
    // The 8K map contains photographic Milky-Way exposure.  Naked-eye stars are
    // substantially dimmer whenever Earth, atmosphere or a sunlit vehicle is in view.
    private const float StarEnergy = 0.24f;

    private ShaderMaterial? _skyMat;
    private Godot.Environment? _env;
    private string? _boundCloudBodyId;
    private double _updateAccumulator = 1.0;

    public override void _Ready()
    {
        ProcessPriority = -10;
        var worldEnvironment = GetTree().Root.FindChild(
            "WorldEnvironment", true, false) as WorldEnvironment;
        _env = worldEnvironment?.Environment;

        if (_env?.Sky == null) return;
        _skyMat = new ShaderMaterial { Shader = GD.Load<Shader>(SkyShaderPath) };
        _skyMat.SetShaderParameter("star_tex", LoadStarTexture());
        _skyMat.SetShaderParameter("cloud_coverage_tex", LoadTexture(EarthCloudTexPath, Colors.Black));
        _skyMat.SetShaderParameter("star_energy", StarEnergy);
        _env.Sky.SkyMaterial = _skyMat;
        // The cloud field evolves slowly. Incremental refresh updates one cubemap face per
        // frame and avoids paying the complete gas+cloud integration six times every frame.
        _env.Sky.ProcessMode = Sky.ProcessModeEnum.Incremental;
    }

    public override void _Process(double delta)
    {
        // Rebuilding a procedural sky cubemap for every simulation frame was one
        // of the largest launch-time stalls.  Atmospheric geometry changes slowly;
        // 12 Hz remains visually continuous and lets incremental cubemap work settle.
        _updateAccumulator += delta;
        if (_updateAccumulator < 1.0 / 12.0) return;
        _updateAccumulator = 0.0;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null || _skyMat == null) return;

        var body = universe.GetDominantBody(vessel.Position);
        var sun = universe.GetBody("sun");
        Vector3d upD = (vessel.Position - body.Position).Normalized;
        Vector3d sunD = sun != null
            ? (sun.Position - vessel.Position).Normalized
            : new Vector3d(0.4, 0.5, 0.8).Normalized;
        double altitude = vessel.GetAltitude(body);

        BindAtmosphere(body, altitude, upD, sunD);
        BindSolarGeometry(vessel.Position, sun, body.Id);
        UpdateEnvironment(body, altitude, upD.Dot(sunD));
    }

    private void BindSolarGeometry(
        Vector3d observer,
        CelestialBody? sun,
        string atmosphereBodyId)
    {
        if (_skyMat == null || sun == null || SimulationBridge.Instance?.Universe is not { } universe)
            return;

        double sunDistance = (sun.Position - observer).Magnitude;
        _skyMat.SetShaderParameter("sun_angular_radius", (float)
            MissionGeometry.ApparentAngularRadius(sun.Radius, sunDistance));

        CelestialBody? bestOccluder = null;
        double lowestVisibility = 1.0;
        double atmosphericVisibility = 1.0;
        foreach (var candidate in universe.Bodies)
        {
            if (candidate.Id == "sun") continue;
            double visibility = MissionGeometry.SolarDiscVisibility(
                observer, candidate.Position, candidate.Radius, sun.Position, sun.Radius);
            if (candidate.Id != atmosphereBodyId)
                atmosphericVisibility = System.Math.Min(atmosphericVisibility, visibility);
            if (visibility < lowestVisibility)
            {
                lowestVisibility = visibility;
                bestOccluder = candidate;
            }
        }

        bool enabled = bestOccluder != null && lowestVisibility < 0.999999;
        _skyMat.SetShaderParameter("solar_visibility", (float)lowestVisibility);
        _skyMat.SetShaderParameter("atmospheric_solar_visibility", (float)atmosphericVisibility);
        _skyMat.SetShaderParameter("solar_occluder_enabled", enabled);
        if (!enabled) return;

        Vector3d direction = (bestOccluder!.Position - observer).Normalized;
        double distance = (bestOccluder.Position - observer).Magnitude;
        _skyMat.SetShaderParameter("solar_occluder_dir", ToGodot(direction));
        _skyMat.SetShaderParameter("solar_occluder_angular_radius", (float)
            MissionGeometry.ApparentAngularRadius(bestOccluder.Radius, distance));
    }

    private void BindAtmosphere(
        CelestialBody body,
        double altitude,
        Vector3d up,
        Vector3d toSun)
    {
        var atmosphere = body.Atmosphere;
        var optics = atmosphere?.Optics;
        bool enabled = atmosphere != null && optics?.IsEnabled == true;

        _skyMat!.SetShaderParameter("local_up", ToGodot(up));
        _skyMat.SetShaderParameter("sun_dir", ToGodot(toSun));
        _skyMat.SetShaderParameter("planet_radius", (float)body.Radius);
        _skyMat.SetShaderParameter("observer_altitude", (float)System.Math.Max(1.0, altitude));
        _skyMat.SetShaderParameter("atmosphere_height",
            enabled ? (float)atmosphere!.MaxAltitude : 1.0f);
        _skyMat.SetShaderParameter("star_energy", StarEnergy);

        if (!enabled)
        {
            _skyMat.SetShaderParameter("rayleigh_scattering", Vector3.Zero);
            _skyMat.SetShaderParameter("mie_scattering", Vector3.Zero);
            _skyMat.SetShaderParameter("mie_absorption", Vector3.Zero);
            _skyMat.SetShaderParameter("ozone_absorption", Vector3.Zero);
            _skyMat.SetShaderParameter("airglow_emission", Vector3.Zero);
            _skyMat.SetShaderParameter("low_order_diffuse_strength", 0.0f);
            _skyMat.SetShaderParameter("cloud_enabled", false);
            return;
        }

        _skyMat.SetShaderParameter("rayleigh_scattering", ToGodot(optics!.RayleighScattering));
        _skyMat.SetShaderParameter("mie_scattering", ToGodot(optics.MieScattering));
        _skyMat.SetShaderParameter("mie_absorption", ToGodot(optics.MieAbsorption));
        _skyMat.SetShaderParameter("ozone_absorption", ToGodot(optics.OzoneAbsorption));
        _skyMat.SetShaderParameter("airglow_emission", ToGodot(optics.AirglowEmission));
        _skyMat.SetShaderParameter("airglow_center_altitude", (float)optics.AirglowCenterAltitude);
        _skyMat.SetShaderParameter("airglow_scale_height", (float)optics.AirglowScaleHeight);
        _skyMat.SetShaderParameter("rayleigh_scale_height", (float)optics.RayleighScaleHeight);
        _skyMat.SetShaderParameter("mie_scale_height", (float)optics.MieScaleHeight);
        _skyMat.SetShaderParameter("ozone_center_altitude", (float)optics.OzoneCenterAltitude);
        _skyMat.SetShaderParameter("ozone_half_width", (float)optics.OzoneHalfWidth);
        _skyMat.SetShaderParameter("mie_g", (float)optics.MieAnisotropy);
        _skyMat.SetShaderParameter("sun_illuminance", (float)optics.SunIlluminanceScale);
        _skyMat.SetShaderParameter("low_order_diffuse_strength",
            (float)optics.LowOrderDiffuseStrength);
        _skyMat.SetShaderParameter("cloud_enabled", optics.HasCloudLayer);
        _skyMat.SetShaderParameter("cloud_base_altitude", (float)optics.CloudBaseAltitude);
        _skyMat.SetShaderParameter("cloud_top_altitude", (float)optics.CloudTopAltitude);
        _skyMat.SetShaderParameter("cloud_extinction", (float)optics.CloudExtinction);
        _skyMat.SetShaderParameter("cloud_coverage", (float)optics.CloudCoverage);
        _skyMat.SetShaderParameter("cloud_longitude_offset",
            (float)(SimulationBridge.Instance!.Universe.CurrentTime
                * optics.CloudWindRadiansPerSecond / Mathf.Tau));
        _skyMat.SetShaderParameter("cloud_world_to_texture",
            new Basis(FloatingOrigin.PlanetOrientation.Inverse()));
        if (_boundCloudBodyId != body.Id)
        {
            _skyMat.SetShaderParameter("cloud_coverage_tex", LoadCloudTexture(body.Id));
            _boundCloudBodyId = body.Id;
        }

        Color groundHorizon = body.Id switch
        {
            "mars" => new Color(0.72f, 0.38f, 0.20f),
            "venus" => new Color(0.92f, 0.72f, 0.38f),
            _ => new Color(0.30f, 0.55f, 0.90f),
        };
        _skyMat.SetShaderParameter("ground_horizon", groundHorizon);
        _skyMat.SetShaderParameter("ground_bottom", groundHorizon.Darkened(0.45f));
    }

    private void UpdateEnvironment(CelestialBody body, double altitude, double sunElevationSin)
    {
        var optics = body.Atmosphere?.Optics;
        double column = optics?.RayleighDensity(altitude) ?? 0.0;
        float daylight = Smoothstep(-0.12f, 0.03f, (float)sunElevationSin);
        float air = (float)System.Math.Clamp(column, 0.0, 1.0);

        Color horizon = body.Id switch
        {
            "mars" => new Color(0.82f, 0.46f, 0.24f),
            "venus" => new Color(0.95f, 0.78f, 0.45f),
            _ => new Color(0.40f, 0.65f, 1.00f),
        };
        CurrentHorizonColor = horizon.Lerp(Colors.Black, 1.0f - air * daylight);

        if (_env == null) return;
        Color ambient = body.Id switch
        {
            "mars" => new Color(0.95f, 0.70f, 0.50f),
            "venus" => new Color(1.0f, 0.80f, 0.52f),
            _ => new Color(0.55f, 0.70f, 1.00f),
        };
        float atmosphericFill = air * Mathf.Lerp(0.025f, 1.0f, daylight);
        // Approximate visible Earth-reflected fill. The former 0.28 floor was comparable
        // to direct daylight and kept eclipse scenes implausibly bright; this restrained
        // term now behaves as secondary bounce rather than another light source.
        float earthshine = body.Id == "earth" && altitude < 1_000_000.0
            ? 0.035f * Mathf.Lerp(0.10f, 1.0f, daylight)
            : 0.0f;
        _env.AmbientLightColor = ambient * Mathf.Max(atmosphericFill, earthshine);
        _env.BackgroundEnergyMultiplier = 1.0f;
    }

    private static Vector3 ToGodot(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    private static Texture2D LoadStarTexture()
        => LoadTexture(StarTexPath, Colors.Black);

    private static Texture2D LoadCloudTexture(string bodyId) => bodyId == "venus"
        ? LoadTexture(VenusCloudTexPath, Colors.Black)
        : LoadTexture(EarthCloudTexPath, Colors.Black);

    private static Texture2D LoadTexture(string resourcePath, Color fallback)
    {
        // Use Godot's imported, cached texture instead of decoding and mipmapping
        // the same 8K JPEG synchronously in several controllers.
        var imported = GD.Load<Texture2D>(resourcePath);
        if (imported != null) return imported;
        var dark = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
        dark.Fill(fallback);
        return ImageTexture.CreateFromImage(dark);
    }

    private static float Smoothstep(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
