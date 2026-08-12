namespace Exosphere.Game;

using System.Diagnostics;
using System.Threading.Tasks;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;
using Godot;

/// <summary>
/// Binds the dominant body's physical optical atmosphere to the spherical sky shader. The
/// view ray remains integrated in the shader, while direct solar throughput and the global
/// multiple-scattering field come from CPU-built spherical LUTs shared with the simulation
/// oracle. This removes noisy per-pixel solar quadrature and keeps ground, twilight, limb and
/// orbit on one continuous model.
/// </summary>
[GlobalClass]
public partial class SkyController : Node
{
    public static Color CurrentHorizonColor { get; private set; } = new(0.40f, 0.65f, 1.0f);

    /// <summary>Stable telemetry contract for the RGB runtime atmosphere LUT.</summary>
    public const string MultipleScatteringLutVersion = "rgb-ms-order4-v20";
    public const int RuntimeMultipleScatteringOrder = SpectralAtmosphereOracle.OfficialRendererOrder;
    public const int ExperimentalMultipleScatteringOrder = SpectralAtmosphereOracle.ExperimentalOrder;

    /// <summary>
    /// Development-only switch. When enabled, the CPU builds an order-five diagnostic LUT and
    /// records its cost, but the shader continues sampling the official order-four texture.
    /// </summary>
    [Export] public bool GenerateExperimentalOrderFive { get; set; }
    public double LastExperimentalOrderFiveBuildMilliseconds { get; private set; }
    public long LastExperimentalOrderFiveEstimatedBytes { get; private set; }

    private const string SkyShaderPath = "res://assets/shaders/space_sky.gdshader";
    private const string StarTexPath = "res://assets/textures/starmap_milkyway_8k.jpg";
    private const string EarthCloudTexPath = "res://assets/textures/earth_clouds.jpg";
    private const string VenusCloudTexPath = "res://assets/textures/venus.jpg";
    // The 8K map contains photographic Milky-Way exposure.  Naked-eye stars are
    // substantially dimmer whenever Earth, atmosphere or a sunlit vehicle is in view.
    // The photographic map is authored for a camera exposure, not naked-eye
    // linear radiance.  Calibrate it upward here; the shader still applies
    // spherical extinction and the eye adaptation gate before display.
    private const float StarEnergy = 0.55f;
    // The optical coefficients are physical cross-sections; the realtime sky
    // integrates a visible-band solar irradiance proxy.  Calibrate that proxy
    // once here so the accumulated HDR sky does not white-clip the lower limb.
    private const float VisibleSolarRadianceScale = 0.35f;
    private const int TransmittanceLutWidth = 128;
    // The solar coordinate is square-root warped toward the horizon.  Doubling the
    // vertical resolution keeps the refracted twilight limb continuous when the LUT is
    // bilinearly sampled by the sky shader; the horizontal resolution remains unchanged.
    private const int TransmittanceLutHeight = 192;
    private const int TransmittanceLutSamples = 48;
    private const int MultipleScatteringLutWidth = 64;
    private const int MultipleScatteringLutHeight = 48;
    private const int MultipleScatteringIntegrationSteps = 48;
    private const int MultipleScatteringSolarSamples = 32;
    // Order four is the first higher-order pass beyond the validated S2/S3 fallback.
    // The CPU builder keeps the legacy order selectable for diagnostics; the realtime sky
    // opts into the finite order-four accumulation once per body/profile.
    private const int MultipleScatteringMaxOrder = 4;
    private const int AngularScatteringLutWidth = 32;
    private const int AngularScatteringSolarLayers = 20;
    private const int AngularScatteringViewLayers = 12;
    private const int AngularScatteringMuLayers = 12;
    private const int AngularScatteringOpticalDepthSamples = 32;

    private ShaderMaterial? _skyMat;
    private Godot.Environment? _env;
    private string? _boundCloudBodyId;
    private readonly Dictionary<string, Texture2D> _transmittanceLuts = new();
    private readonly Dictionary<string, Texture2D> _multipleScatteringLuts = new();
    private readonly Dictionary<string, (Texture2D Texture, float TopAltitude)> _densityLuts = new();
    private readonly Dictionary<string, AtmosphereDensityProfile> _densityProfiles = new();
    private Task<AtmosphereLutCpuResult>? _atmosphereLutTask;
    private string? _atmosphereLutTaskBodyId;
    public bool IsAtmosphereLutBuildPending => _atmosphereLutTask is { IsCompleted: false };
    public double LastAtmosphereLutBuildMilliseconds { get; private set; }
    private double _updateAccumulator = 1.0;
    private bool _hasAtmosphereState;
    private string? _lastAtmosphereBodyId;
    private double _lastAtmosphereAltitude;
    private Vector3d _lastAtmosphereUp;
    private Vector3d _lastAtmosphereSun;

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
        _skyMat.SetShaderParameter("transmittance_lut_min_solar_sin",
            (float)AtmosphereTransmittanceLut.MinimumSolarElevationSin);
        _skyMat.SetShaderParameter("transmittance_lut_height",
            (float)TransmittanceLutHeight);
        _skyMat.SetShaderParameter("multiple_scattering_solar_layers",
            (float)AngularScatteringSolarLayers);
        _skyMat.SetShaderParameter("multiple_scattering_view_layers",
            (float)AngularScatteringViewLayers);
        _skyMat.SetShaderParameter("multiple_scattering_mu_layers",
            (float)AngularScatteringMuLayers);
        _skyMat.SetShaderParameter("density_lut_enabled", false);
        _skyMat.SetShaderParameter("transmittance_lut_enabled", false);
        _skyMat.SetShaderParameter("multiple_scattering_lut_enabled", false);
        _env.Sky.SkyMaterial = _skyMat;
        // The cloud field evolves slowly. Incremental refresh updates one cubemap face per
        // frame and avoids paying the complete gas+cloud integration six times every frame.
        _env.Sky.ProcessMode = Sky.ProcessModeEnum.Incremental;
    }

    public override void _Process(double delta)
    {
        PollAtmosphereLutBuild();

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

        // Incremental skies rebuild one cubemap face at a time. Reassigning every
        // uniform at 12 Hz invalidates that work before the six faces can complete;
        // the result is a black/half-updated cubemap exactly at the terminator. Keep
        // the physical bindings until the state has moved enough to be visible, then
        // invalidate once and let the incremental renderer converge.
        bool atmosphereChanged = !_hasAtmosphereState
            || _lastAtmosphereBodyId != body.Id
            || System.Math.Abs(altitude - _lastAtmosphereAltitude) > 100.0
            || _lastAtmosphereUp.Dot(upD) < 0.99999
            || _lastAtmosphereSun.Dot(sunD) < 0.999999;
        if (atmosphereChanged)
        {
            BindAtmosphere(body, altitude, upD, sunD);
            _hasAtmosphereState = true;
            _lastAtmosphereBodyId = body.Id;
            _lastAtmosphereAltitude = altitude;
            _lastAtmosphereUp = upD;
            _lastAtmosphereSun = sunD;
        }
        // Occluders can move while the local atmosphere and Sun direction remain within
        // the cache tolerances (the Moon is the common case).  Solar geometry therefore
        // needs its own update path; otherwise an eclipse would remain visually stuck until
        // the vessel moved enough to rebuild the atmospheric bindings.
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
            // The atmosphere receives irradiance from the limb-darkened photosphere,
            // not from a uniform geometric disc.  Central occultations therefore remove
            // slightly more radiance than equal-area limb occultations.
            double visibility = MissionGeometry.LimbDarkenedSolarDiscVisibility(
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
        // Disable before resolving a new body/profile so a missing texture can never
        // replace the established exponential fallback with the black default sampler.
        _skyMat.SetShaderParameter("density_lut_enabled", false);
        _skyMat.SetShaderParameter("atmosphere_height",
            enabled ? (float)atmosphere!.MaxAltitude : 1.0f);
        _skyMat.SetShaderParameter("star_energy", StarEnergy);
        _skyMat.SetShaderParameter("transmittance_lut_enabled", false);
        _skyMat.SetShaderParameter("multiple_scattering_lut_enabled", false);

        if (!enabled)
        {
            _skyMat.SetShaderParameter("rayleigh_scattering", Vector3.Zero);
            _skyMat.SetShaderParameter("mie_scattering", Vector3.Zero);
            _skyMat.SetShaderParameter("mie_absorption", Vector3.Zero);
            _skyMat.SetShaderParameter("ozone_absorption", Vector3.Zero);
            _skyMat.SetShaderParameter("airglow_emission", Vector3.Zero);
            _skyMat.SetShaderParameter("surface_refractivity", 0.0f);
            _skyMat.SetShaderParameter("low_order_diffuse_strength", 0.0f);
            _skyMat.SetShaderParameter("cloud_enabled", false);
            return;
        }

        var densityProfile = GetDensityProfile(body.Id, atmosphere!);
        var buildTimer = Stopwatch.StartNew();
        var densityLut = GetDensityLut(body.Id, densityProfile);
        GD.Print($"PERF_ATMOS body={body.Id} stage=density_lut ms={buildTimer.Elapsed.TotalMilliseconds:F1}");
        _skyMat.SetShaderParameter("density_lut", densityLut.Texture);
        _skyMat.SetShaderParameter("density_lut_top_altitude", densityLut.TopAltitude);
        _skyMat.SetShaderParameter("density_lut_enabled", true);

        // These tables are pure CPU work but can take seconds for Earth.  Queue them away
        // from the main thread and keep the shader's analytical fallback active until the
        // immutable result is ready.  Texture/Image creation stays on this thread.
        if (TryGetAtmosphereLuts(body.Id, densityProfile, body.Radius, atmosphere!.MaxAltitude,
            out var transmittance, out var multipleScattering)
            && transmittance != null && multipleScattering != null)
        {
            _skyMat.SetShaderParameter("transmittance_lut", transmittance);
            _skyMat.SetShaderParameter("transmittance_lut_enabled", true);
            _skyMat.SetShaderParameter("multiple_scattering_lut", multipleScattering);
            _skyMat.SetShaderParameter("multiple_scattering_lut_enabled", true);
        }

        _skyMat.SetShaderParameter("rayleigh_scattering", ToGodot(optics!.RayleighScattering));
        _skyMat.SetShaderParameter("mie_scattering", ToGodot(optics.MieScattering));
        _skyMat.SetShaderParameter("mie_absorption", ToGodot(optics.MieAbsorption));
        _skyMat.SetShaderParameter("ozone_absorption", ToGodot(optics.OzoneAbsorption));
        _skyMat.SetShaderParameter("airglow_emission", ToGodot(optics.AirglowEmission));
        _skyMat.SetShaderParameter("airglow_center_altitude", (float)optics.AirglowCenterAltitude);
        _skyMat.SetShaderParameter("airglow_scale_height", (float)optics.AirglowScaleHeight);
        _skyMat.SetShaderParameter("airglow_secondary_emission",
            ToGodot(optics.AirglowSecondaryEmission));
        _skyMat.SetShaderParameter("airglow_secondary_center_altitude",
            (float)optics.AirglowSecondaryCenterAltitude);
        _skyMat.SetShaderParameter("airglow_secondary_scale_height",
            (float)optics.AirglowSecondaryScaleHeight);
        _skyMat.SetShaderParameter("airglow_daylight_fraction",
            (float)System.Math.Clamp(optics.AirglowDaylightFraction, 0.0, 1.0));
        _skyMat.SetShaderParameter("rayleigh_scale_height", (float)optics.RayleighScaleHeight);
        _skyMat.SetShaderParameter("mie_scale_height", (float)optics.MieScaleHeight);
        _skyMat.SetShaderParameter("ozone_center_altitude", (float)optics.OzoneCenterAltitude);
        _skyMat.SetShaderParameter("ozone_half_width", (float)optics.OzoneHalfWidth);
        _skyMat.SetShaderParameter("mie_g", (float)optics.MieAnisotropy);
        _skyMat.SetShaderParameter("sun_illuminance",
            (float)(optics.SunIlluminanceScale * VisibleSolarRadianceScale));
        _skyMat.SetShaderParameter("surface_refractivity", (float)optics.SurfaceRefractivity);
        _skyMat.SetShaderParameter("refractive_scale_height", (float)optics.RefractiveScaleHeight);
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
        // At low solar elevations, the long tangent path magnifies individual
        // latitude rows in the equirectangular cloud map.  Fade in the shader's
        // narrow latitude prefilter only there; full daylight keeps the original
        // high-frequency weather detail intact.
        _skyMat?.SetShaderParameter("cloud_weather_prefilter",
            1.0f - Smoothstep(0.02f, 0.18f, (float)sunElevationSin));

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

    private bool TryGetAtmosphereLuts(
        string bodyId,
        AtmosphereDensityProfile profile,
        double planetRadius,
        double atmosphereTopAltitude,
        out Texture2D? transmittance,
        out Texture2D? multipleScattering)
    {
        if (_transmittanceLuts.TryGetValue(bodyId, out transmittance)
            && _multipleScatteringLuts.TryGetValue(bodyId, out multipleScattering))
            return true;

        transmittance = null;
        multipleScattering = null;
        // PollAtmosphereLutBuild is the sole consumer of completed tasks.  The task may
        // complete between that poll and this bind, so do not clear a completed task here:
        // doing so would lose the result and queue a duplicate CPU build.
        if (_atmosphereLutTask != null) return false;

        bool includeExperimentalOrderFive = GenerateExperimentalOrderFive;
        _atmosphereLutTaskBodyId = bodyId;
        _atmosphereLutTask = Task.Run(() => BuildAtmosphereLutsCpu(
            bodyId, profile, planetRadius, atmosphereTopAltitude,
            includeExperimentalOrderFive));
        GD.Print($"PERF_ATMOS body={bodyId} stage=queued worker=true");
        return false;
    }

    private void PollAtmosphereLutBuild()
    {
        var task = _atmosphereLutTask;
        if (task == null || !task.IsCompleted) return;

        string bodyId = _atmosphereLutTaskBodyId ?? string.Empty;
        _atmosphereLutTask = null;
        _atmosphereLutTaskBodyId = null;
        if (task.IsCanceled || task.IsFaulted)
        {
            GD.PrintErr($"PERF_ATMOS body={bodyId} stage=worker_failed error={task.Exception}");
            _hasAtmosphereState = false;
            return;
        }

        var result = task.GetAwaiter().GetResult();
        var timer = Stopwatch.StartNew();
        _transmittanceLuts[bodyId] = CreateTransmittanceTexture(result.Transmittance);
        _multipleScatteringLuts[bodyId] = CreateMultipleScatteringTexture(result.Angular);
        LastAtmosphereLutBuildMilliseconds = result.BuildMilliseconds;
        LastExperimentalOrderFiveBuildMilliseconds = result.ExperimentalOrderFiveMilliseconds;
        LastExperimentalOrderFiveEstimatedBytes = result.ExperimentalOrderFiveEstimatedBytes;
        GD.Print($"PERF_ATMOS body={bodyId} stage=worker_complete "
            + $"cpuMs={result.BuildMilliseconds:F1} uploadMs={timer.Elapsed.TotalMilliseconds:F1}");

        // Re-enter BindAtmosphere on the next cadence so the newly uploaded textures become
        // active without rebuilding the CPU tables. This also handles an SOI crossing while
        // the worker was running.
        _hasAtmosphereState = false;
    }

    private static AtmosphereLutCpuResult BuildAtmosphereLutsCpu(
        string bodyId,
        AtmosphereDensityProfile profile,
        double planetRadius,
        double atmosphereTopAltitude,
        bool includeExperimentalOrderFive)
    {
        var stopwatch = Stopwatch.StartNew();
        var transmittance = AtmosphereTransmittanceLut.Build(
            profile,
            planetRadius,
            atmosphereTopAltitude,
            TransmittanceLutWidth,
            TransmittanceLutHeight,
            TransmittanceLutSamples);

        var global = AtmosphereMultipleScatteringLut.Build(
            profile,
            planetRadius,
            atmosphereTopAltitude,
            MultipleScatteringLutWidth,
            MultipleScatteringLutHeight,
            MultipleScatteringIntegrationSteps,
            MultipleScatteringSolarSamples,
            MultipleScatteringMaxOrder);

        double experimentalMilliseconds = 0.0;
        long experimentalBytes = 0;
        if (includeExperimentalOrderFive)
        {
            var experimentalTimer = Stopwatch.StartNew();
            var experimental = AtmosphereMultipleScatteringLut.Build(
                profile,
                planetRadius,
                atmosphereTopAltitude,
                MultipleScatteringLutWidth,
                MultipleScatteringLutHeight,
                MultipleScatteringIntegrationSteps,
                MultipleScatteringSolarSamples,
                ExperimentalMultipleScatteringOrder);
            experimentalTimer.Stop();
            experimentalMilliseconds = experimentalTimer.Elapsed.TotalMilliseconds;
            experimentalBytes = (long)experimental.Width * experimental.Height
                * 3 * sizeof(double);
        }

        var angular = AtmosphereAngularMultipleScatteringLut.Build(
            profile,
            global,
            planetRadius,
            atmosphereTopAltitude,
            AngularScatteringLutWidth,
            AngularScatteringSolarLayers,
            AngularScatteringViewLayers,
            AngularScatteringMuLayers,
            AngularScatteringOpticalDepthSamples);
        stopwatch.Stop();
        return new AtmosphereLutCpuResult(
            bodyId, transmittance, angular, stopwatch.Elapsed.TotalMilliseconds,
            experimentalMilliseconds, experimentalBytes);
    }

    private static Texture2D CreateTransmittanceTexture(AtmosphereTransmittanceLut lut)
    {
        var image = Image.CreateEmpty(
            lut.Width, lut.Height, false, Image.Format.Rgbaf);
        for (int y = 0; y < lut.Height; y++)
        {
            for (int x = 0; x < lut.Width; x++)
            {
                var value = lut.GetTexel(x, y);
                image.SetPixel(x, y, new Color(
                    (float)value.X, (float)value.Y, (float)value.Z, 1.0f));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private AtmosphereDensityProfile GetDensityProfile(
        string bodyId,
        AtmosphereModel atmosphere)
    {
        if (_densityProfiles.TryGetValue(bodyId, out var cached)) return cached;
        var profile = AtmosphereDensityProfile.Create(atmosphere);
        _densityProfiles[bodyId] = profile;
        return profile;
    }

    private (Texture2D Texture, float TopAltitude) GetDensityLut(
        string bodyId,
        AtmosphereDensityProfile profile)
    {
        if (_densityLuts.TryGetValue(bodyId, out var cached)) return cached;

        var lut = AtmosphereDensityLut.Build(profile.Atmosphere);
        var image = Image.CreateEmpty(lut.Width, lut.Height, false, Image.Format.Rgbaf);
        for (int x = 0; x < lut.Width; x++)
        {
            var value = lut.GetTexel(x);
            image.SetPixel(x, 0, new Color(
                (float)value.X, (float)value.Y, (float)value.Z, 1.0f));
        }

        var texture = ImageTexture.CreateFromImage(image);
        var result = (texture, (float)lut.AtmosphereTopAltitude);
        _densityLuts[bodyId] = result;
        return result;
    }

    private static Texture2D CreateMultipleScatteringTexture(
        AtmosphereAngularMultipleScatteringLut lut)
    {
        var image = Image.CreateEmpty(
            lut.Width, lut.PackedHeight, false, Image.Format.Rgbaf);
        for (int mu = 0; mu < lut.MuWidth; mu++)
        {
            for (int view = 0; view < lut.ViewHeight; view++)
            {
                int rowBase = (mu * lut.ViewHeight + view) * lut.SolarHeight;
                for (int solar = 0; solar < lut.SolarHeight; solar++)
                {
                    int row = rowBase + solar;
                    for (int x = 0; x < lut.Width; x++)
                    {
                        var value = lut.GetTexel(x, solar, view, mu);
                        image.SetPixel(x, row, new Color(
                            (float)value.X, (float)value.Y, (float)value.Z, 1.0f));
                    }
                }
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    private sealed record AtmosphereLutCpuResult(
        string BodyId,
        AtmosphereTransmittanceLut Transmittance,
        AtmosphereAngularMultipleScatteringLut Angular,
        double BuildMilliseconds,
        double ExperimentalOrderFiveMilliseconds,
        long ExperimentalOrderFiveEstimatedBytes);

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
