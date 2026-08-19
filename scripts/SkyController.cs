namespace Exosphere.Game;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
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
    public const string MultipleScatteringLutVersion = "rgb-ms-order4-interactive-v21";
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
    // Interactive runtime profile: preserve the same physical model and official order 4,
    // but bound CPU work tightly enough that llvmpipe/Godot remains responsive while the
    // worker builds. The offline spectral/reference tools keep their independent high-
    // resolution settings; these dimensions are renderer-quality settings, not physics data.
    private const int TransmittanceLutWidth = 64;
    private const int TransmittanceLutHeight = 96;
    private const int TransmittanceLutSamples = 16;
    private const int MultipleScatteringLutWidth = 32;
    private const int MultipleScatteringLutHeight = 24;
    private const int MultipleScatteringIntegrationSteps = 16;
    private const int MultipleScatteringSolarSamples = 12;
    // Order four is the first higher-order pass beyond the validated S2/S3 fallback.
    // The CPU builder keeps the legacy order selectable for diagnostics; the realtime sky
    // opts into the finite order-four accumulation once per body/profile.
    private const int MultipleScatteringMaxOrder = 4;
    private const int AngularScatteringLutWidth = 16;
    private const int AngularScatteringSolarLayers = 8;
    private const int AngularScatteringViewLayers = 8;
    private const int AngularScatteringMuLayers = 8;
    private const int AngularScatteringOpticalDepthSamples = 12;
    private const int MaxCpuLutCacheEntries = 3;
    // Runtime-only shader quadrature quality. Offline LUT/reference sampling is
    // intentionally independent and remains at its validated resolution.
    private const float InteractiveAtmosphereQuality = 0.60f;

    private enum AtmosphereLutWorkerState
    {
        Idle,
        Queued,
        Running,
        CancelRequested,
        Completed,
        Canceled,
        Faulted,
    }

    private enum AtmosphereLutWorkerPhase
    {
        None,
        Transmittance,
        GlobalMultipleScattering,
        ExperimentalOrderFive,
        AngularAtlas,
        Completed,
    }

    private ShaderMaterial? _skyMat;
    private Godot.Environment? _env;
    private string? _boundCloudBodyId;
    private readonly Dictionary<string, Texture2D> _transmittanceLuts = new();
    private readonly Dictionary<string, Texture2D> _multipleScatteringLuts = new();
    private readonly Dictionary<string, AtmosphereLutCpuResult> _cpuLutCache = new();
    private readonly Dictionary<string, (Texture2D Texture, float TopAltitude)> _densityLuts = new();
    private readonly Dictionary<string, AtmosphereDensityProfile> _densityProfiles = new();
    private Task<AtmosphereLutCpuResult>? _atmosphereLutTask;
    private CancellationTokenSource? _atmosphereLutCancellation;
    private string? _atmosphereLutTaskBodyId;
    private string? _atmosphereLutTaskCacheKey;
    private int _atmosphereLutGeneration;
    private int _workerState;
    private int _workerPhase;
    private long _workerQueuedTimestamp;
    private long _workerStartedTimestamp;
    private long _workerFinishedTimestamp;
    private long _workerProducedBytes;
    private long _workerEstimatedBytes;
    private int _lastTelemetryGeneration = -1;
    private int _lastTelemetryState = -1;
    private int _lastTelemetryPhase = -1;
    private long _lastTelemetryTimestamp;
    private bool _isExiting;
    public bool IsAtmosphereLutBuildPending => _atmosphereLutTask is { IsCompleted: false };
    public double LastAtmosphereLutBuildMilliseconds { get; private set; }
    public long LastAtmosphereLutEstimatedBytes { get; private set; }
    public long LastAtmosphereLutPeakBytes { get; private set; }
    public long LastAtmosphereLutUploadBytes { get; private set; }
    public string AtmosphereLutWorkerStatus => WorkerStateName(Volatile.Read(ref _workerState));
    public double AtmosphereLutWorkerElapsedMilliseconds => WorkerElapsedMilliseconds();
    public long AtmosphereLutWorkerEstimatedBytes => Interlocked.Read(ref _workerEstimatedBytes);
    public long AtmosphereLutWorkerProducedBytes => Interlocked.Read(ref _workerProducedBytes);
    private double _updateAccumulator = 1.0;
    private bool _hasAtmosphereState;
    private string? _lastAtmosphereBodyId;
    private double _lastAtmosphereAltitude;
    private Vector3d _lastAtmosphereUp;
    private Vector3d _lastAtmosphereSun;
    // Sky.ProcessMode.Incremental invalidates the radiance cubemap when a custom
    // uniform is written. Keep presentation parameters sticky: writing the same
    // value every frame defeats the incremental renderer and turns one cubemap
    // face into a full atmosphere integration on every render tick.
    private bool _solarGeometryInitialized;
    private float _lastSolarAngularRadius = float.NaN;
    private float _lastSolarVisibility = float.NaN;
    private float _lastAtmosphericSolarVisibility = float.NaN;
    private bool _lastSolarOccluderEnabled;
    private Vector3 _lastSolarOccluderDirection;
    private float _lastSolarOccluderAngularRadius = float.NaN;
    private float _lastCloudWeatherPrefilter = float.NaN;
    private bool _sharedSolarGeometryTelemetryPublished;

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
        _skyMat.SetShaderParameter("atmosphere_quality", InteractiveAtmosphereQuality);
        _skyMat.SetShaderParameter("density_lut_enabled", false);
        _skyMat.SetShaderParameter("transmittance_lut_enabled", false);
        _skyMat.SetShaderParameter("multiple_scattering_lut_enabled", false);
        _env.Sky.SkyMaterial = _skyMat;
        // Atmospheric radiance is low frequency and is refreshed incrementally.
        // 128² keeps the six-face cubemap cheap on integrated/llvmpipe renderers;
        // the full-screen background still uses the filtered cubemap.
        _env.Sky.RadianceSize = Sky.RadianceSizeEnum.Size128;
        _env.Sky.ProcessMode = Sky.ProcessModeEnum.Incremental;
        GD.Print($"PERF_RENDER stage=sky_config radiance=128 process=incremental "
            + $"atmosphereQuality={InteractiveAtmosphereQuality:F2}");
    }

    public override void _ExitTree()
    {
        _isExiting = true;
        CancelAtmosphereLutBuild("exit_tree");
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
        float sunAngularRadius = (float)MissionGeometry.ApparentAngularRadius(
            sun.Radius, sunDistance);
        if (!_solarGeometryInitialized
            || System.Math.Abs(sunAngularRadius - _lastSolarAngularRadius) > 1e-7f)
        {
            _skyMat.SetShaderParameter("sun_angular_radius", sunAngularRadius);
            _lastSolarAngularRadius = sunAngularRadius;
        }

        var sunController = SunController.Instance;
        SunController.SolarGeometrySnapshot cachedSolarGeometry = default;
        bool hasCachedSolarGeometry = sunController != null
            && sunController.TryGetCachedSolarGeometry(atmosphereBodyId, out cachedSolarGeometry);
        if (hasCachedSolarGeometry && !_sharedSolarGeometryTelemetryPublished)
        {
            GD.Print("PERF_SOLAR_GEOMETRY consumer=sky cache_hit=True");
            _sharedSolarGeometryTelemetryPublished = true;
        }
        bool enabled;
        float solarVisibility;
        float atmosphericSolarVisibility;
        Vector3 occluderDirection;
        float occluderAngularRadius;
        if (hasCachedSolarGeometry)
        {
            // Reuse SunController's exact 20 Hz limb-darkened sample; this path removes
            // a second body loop from the 12 Hz sky update without changing process order.
            enabled = cachedSolarGeometry.OccluderEnabled;
            solarVisibility = cachedSolarGeometry.Visibility;
            atmosphericSolarVisibility = cachedSolarGeometry.AtmosphericVisibility;
            occluderDirection = cachedSolarGeometry.OccluderDirection;
            occluderAngularRadius = cachedSolarGeometry.OccluderAngularRadius;
        }
        else
        {
            // Keep a first-frame fallback while SunController publishes its first sample,
            // so scene startup cannot expose uninitialized eclipse data.
            CelestialBody? bestOccluder = null;
            double lowestVisibility = 1.0;
            double atmosphericVisibility = 1.0;
            foreach (var candidate in universe.Bodies)
            {
                if (candidate.Id == "sun") continue;
                // The atmosphere receives irradiance from the limb-darkened photosphere,
                // not from a uniform geometric disc. Central occultations therefore remove
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

            enabled = bestOccluder != null && lowestVisibility < 0.999999;
            solarVisibility = (float)lowestVisibility;
            atmosphericSolarVisibility = (float)atmosphericVisibility;
            occluderDirection = Vector3.Zero;
            occluderAngularRadius = 0.0f;
            if (enabled)
            {
                Vector3d direction = (bestOccluder!.Position - observer).Normalized;
                double distance = (bestOccluder.Position - observer).Magnitude;
                occluderDirection = ToGodot(direction);
                occluderAngularRadius = (float)MissionGeometry.ApparentAngularRadius(
                    bestOccluder.Radius, distance);
            }
        }

        if (!_solarGeometryInitialized
            || System.Math.Abs(solarVisibility - _lastSolarVisibility) > 1e-5f)
        {
            _skyMat.SetShaderParameter("solar_visibility", solarVisibility);
            _lastSolarVisibility = solarVisibility;
        }
        if (!_solarGeometryInitialized
            || System.Math.Abs(atmosphericSolarVisibility - _lastAtmosphericSolarVisibility) > 1e-5f)
        {
            _skyMat.SetShaderParameter("atmospheric_solar_visibility", atmosphericSolarVisibility);
            _lastAtmosphericSolarVisibility = atmosphericSolarVisibility;
        }
        if (!_solarGeometryInitialized || enabled != _lastSolarOccluderEnabled)
        {
            _skyMat.SetShaderParameter("solar_occluder_enabled", enabled);
            _lastSolarOccluderEnabled = enabled;
        }
        if (!enabled)
        {
            _solarGeometryInitialized = true;
            return;
        }

        if (!_solarGeometryInitialized
            || _lastSolarOccluderDirection.DistanceSquaredTo(occluderDirection) > 1e-10f)
        {
            _skyMat.SetShaderParameter("solar_occluder_dir", occluderDirection);
            _lastSolarOccluderDirection = occluderDirection;
        }
        if (!_solarGeometryInitialized
            || System.Math.Abs(occluderAngularRadius - _lastSolarOccluderAngularRadius) > 1e-7f)
        {
            _skyMat.SetShaderParameter("solar_occluder_angular_radius", occluderAngularRadius);
            _lastSolarOccluderAngularRadius = occluderAngularRadius;
        }
        _solarGeometryInitialized = true;
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
        float cloudWeatherPrefilter =
            1.0f - Smoothstep(0.02f, 0.18f, (float)sunElevationSin);
        if (_skyMat != null
            && (float.IsNaN(_lastCloudWeatherPrefilter)
                || System.Math.Abs(cloudWeatherPrefilter - _lastCloudWeatherPrefilter) > 1e-3f))
        {
            _skyMat.SetShaderParameter("cloud_weather_prefilter", cloudWeatherPrefilter);
            _lastCloudWeatherPrefilter = cloudWeatherPrefilter;
        }

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
        Color targetAmbient = ambient * Mathf.Max(atmosphericFill, earthshine);
        if (ColorDiffers(_env.AmbientLightColor, targetAmbient))
            _env.AmbientLightColor = targetAmbient;
        if (System.Math.Abs(_env.BackgroundEnergyMultiplier - 1.0f) > 1e-4f)
            _env.BackgroundEnergyMultiplier = 1.0f;
    }

    private static Vector3 ToGodot(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    private static bool ColorDiffers(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) > 1e-4f
        || Mathf.Abs(a.G - b.G) > 1e-4f
        || Mathf.Abs(a.B - b.B) > 1e-4f
        || Mathf.Abs(a.A - b.A) > 1e-4f;

    private bool TryGetAtmosphereLuts(
        string bodyId,
        AtmosphereDensityProfile profile,
        double planetRadius,
        double atmosphereTopAltitude,
        out Texture2D? transmittance,
        out Texture2D? multipleScattering)
    {
        bool includeExperimentalOrderFive = GenerateExperimentalOrderFive;
        string cacheKey = CreateAtmosphereLutCacheKey(
            bodyId, profile, planetRadius, atmosphereTopAltitude,
            includeExperimentalOrderFive);
        if (_transmittanceLuts.TryGetValue(cacheKey, out transmittance)
            && _multipleScatteringLuts.TryGetValue(cacheKey, out multipleScattering))
            return true;

        transmittance = null;
        multipleScattering = null;

        if (_cpuLutCache.TryGetValue(cacheKey, out var cached))
        {
            try
            {
                ActivateAtmosphereLuts(cacheKey, cached, cacheHit: true);
                transmittance = _transmittanceLuts[cacheKey];
                multipleScattering = _multipleScatteringLuts[cacheKey];
                return true;
            }
            catch (Exception exception)
            {
                _cpuLutCache.Remove(cacheKey);
                GD.PrintErr($"PERF_ATMOS body={bodyId} stage=cache_failed "
                    + $"state=faulted error={exception.GetType().Name}");
            }
        }

        // PollAtmosphereLutBuild is the sole consumer of completed tasks.  The task may
        // complete between that poll and this bind, so do not clear a completed task here:
        // doing so would lose the result and queue a duplicate CPU build.
        if (_atmosphereLutTask != null)
        {
            if (!string.Equals(_atmosphereLutTaskCacheKey, cacheKey, StringComparison.Ordinal))
                CancelAtmosphereLutBuild("profile_changed");
            return false;
        }

        var cancellation = new CancellationTokenSource();
        int generation = ++_atmosphereLutGeneration;
        _atmosphereLutTaskBodyId = bodyId;
        _atmosphereLutTaskCacheKey = cacheKey;
        _atmosphereLutCancellation = cancellation;
        _workerQueuedTimestamp = Stopwatch.GetTimestamp();
        _workerStartedTimestamp = 0;
        _workerFinishedTimestamp = 0;
        Interlocked.Exchange(ref _workerEstimatedBytes, EstimateAtmosphereLutPeakBytes(
            includeExperimentalOrderFive));
        Interlocked.Exchange(ref _workerProducedBytes, 0);
        Interlocked.Exchange(ref _workerPhase, (int)AtmosphereLutWorkerPhase.None);
        Interlocked.Exchange(ref _workerState, (int)AtmosphereLutWorkerState.Queued);
        _lastTelemetryGeneration = -1;
        _lastTelemetryState = -1;
        _lastTelemetryPhase = -1;
        _lastTelemetryTimestamp = 0;
        GD.Print($"PERF_ATMOS body={bodyId} stage=queued worker=true generation={generation} "
            + $"state=queued estimatedBytes={AtmosphereLutWorkerEstimatedBytes} cache=miss");
        _atmosphereLutTask = Task.Run(() => BuildAtmosphereLutsCpu(
            bodyId, cacheKey, profile, planetRadius, atmosphereTopAltitude,
            includeExperimentalOrderFive, generation, cancellation.Token, this), cancellation.Token);
        return false;
    }

    private void PollAtmosphereLutBuild()
    {
        EmitWorkerTelemetryIfChanged();
        var task = _atmosphereLutTask;
        if (task == null || !task.IsCompleted) return;

        string bodyId = _atmosphereLutTaskBodyId ?? string.Empty;
        string cacheKey = _atmosphereLutTaskCacheKey ?? string.Empty;
        var cancellation = _atmosphereLutCancellation;
        _atmosphereLutTask = null;
        _atmosphereLutCancellation = null;
        _atmosphereLutTaskBodyId = null;
        _atmosphereLutTaskCacheKey = null;
        _workerFinishedTimestamp = Stopwatch.GetTimestamp();
        bool cancellationRequested = cancellation?.IsCancellationRequested == true;
        if (task.IsCanceled || task.IsFaulted || cancellationRequested)
        {
            bool canceled = task.IsCanceled || cancellationRequested;
            Interlocked.Exchange(ref _workerState, (int)(canceled
                ? AtmosphereLutWorkerState.Canceled
                : AtmosphereLutWorkerState.Faulted));
            string error = task.Exception?.GetBaseException().GetType().Name ?? "canceled";
            GD.Print(canceled
                ? $"PERF_ATMOS body={bodyId} stage=worker_canceled state=canceled "
                    + $"elapsedMs={AtmosphereLutWorkerElapsedMilliseconds:F1} "
                    + $"queueMs={QueueMilliseconds():F1} "
                    + $"bytes={AtmosphereLutWorkerProducedBytes}/"
                    + $"{AtmosphereLutWorkerEstimatedBytes} "
                    + $"estimatedBytes={AtmosphereLutWorkerEstimatedBytes}"
                : $"PERF_ATMOS body={bodyId} stage=worker_failed state=faulted "
                    + $"elapsedMs={AtmosphereLutWorkerElapsedMilliseconds:F1} error={error}");
            cancellation?.Dispose();
            if (!_isExiting) _hasAtmosphereState = false;
            return;
        }

        try
        {
            var result = task.GetAwaiter().GetResult();
            ActivateAtmosphereLuts(cacheKey, result, cacheHit: false);
            GD.Print($"PERF_ATMOS body={bodyId} stage=worker_complete state=completed "
                + $"generation={result.Generation} cpuMs={result.BuildMilliseconds:F1} "
                + $"queueMs={QueueMilliseconds():F1} uploadBytes={result.UploadBytes} "
                + $"retainedCpuBytes={result.RetainedCpuBytes} "
                + $"peakBytes={result.PeakWorkingBytes}");
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _workerState, (int)AtmosphereLutWorkerState.Faulted);
            GD.PrintErr($"PERF_ATMOS body={bodyId} stage=upload_failed state=faulted "
                + $"error={exception.GetType().Name}");
        }
        finally
        {
            cancellation?.Dispose();
        }

        // Re-enter BindAtmosphere on the next cadence so the newly uploaded textures become
        // active without rebuilding the CPU tables. This also handles an SOI crossing while
        // the worker was running.
        if (!_isExiting) _hasAtmosphereState = false;
    }

    private void ActivateAtmosphereLuts(
        string cacheKey,
        AtmosphereLutCpuResult result,
        bool cacheHit)
    {
        var uploadTimer = Stopwatch.StartNew();
        if (!_transmittanceLuts.TryGetValue(cacheKey, out var transmittance))
        {
            transmittance = CreateTransmittanceTexture(result.Transmittance);
            _transmittanceLuts[cacheKey] = transmittance;
        }

        if (!_multipleScatteringLuts.TryGetValue(cacheKey, out var multipleScattering))
        {
            multipleScattering = CreateMultipleScatteringTexture(result.Angular);
            _multipleScatteringLuts[cacheKey] = multipleScattering;
        }

        _cpuLutCache[cacheKey] = result;
        TrimCpuLutCache(cacheKey);
        LastAtmosphereLutBuildMilliseconds = result.BuildMilliseconds;
        LastAtmosphereLutEstimatedBytes = result.RetainedCpuBytes;
        LastAtmosphereLutPeakBytes = result.PeakWorkingBytes;
        LastAtmosphereLutUploadBytes = result.UploadBytes;
        LastExperimentalOrderFiveBuildMilliseconds = result.ExperimentalOrderFiveMilliseconds;
        LastExperimentalOrderFiveEstimatedBytes = result.ExperimentalOrderFiveEstimatedBytes;
        Interlocked.Exchange(ref _workerProducedBytes, result.RetainedCpuBytes);
        Interlocked.Exchange(ref _workerEstimatedBytes, result.PeakWorkingBytes);
        Interlocked.Exchange(ref _workerState, (int)AtmosphereLutWorkerState.Completed);
        _workerFinishedTimestamp = Stopwatch.GetTimestamp();

        if (cacheHit)
        {
            GD.Print($"PERF_ATMOS body={result.BodyId} stage=cache_hit state=completed "
                + $"cpuMs={result.BuildMilliseconds:F1} uploadMs={uploadTimer.Elapsed.TotalMilliseconds:F1} "
                + $"retainedCpuBytes={result.RetainedCpuBytes} uploadBytes={result.UploadBytes}");
        }
    }

    private void TrimCpuLutCache(string activeKey)
    {
        while (_cpuLutCache.Count > MaxCpuLutCacheEntries)
        {
            string? evictionKey = null;
            foreach (string key in _cpuLutCache.Keys)
            {
                if (!string.Equals(key, activeKey, StringComparison.Ordinal))
                {
                    evictionKey = key;
                    break;
                }
            }

            if (evictionKey == null) return;
            _cpuLutCache.Remove(evictionKey);
            // The renderer texture cache is keyed by the same immutable profile key. Drop
            // both entries together so profile churn cannot retain an unbounded pair of
            // CPU/GPU LUTs. Godot releases the resources when these references disappear.
            _transmittanceLuts.Remove(evictionKey);
            _multipleScatteringLuts.Remove(evictionKey);
        }
    }

    private void CancelAtmosphereLutBuild(string reason)
    {
        var task = _atmosphereLutTask;
        var cancellation = _atmosphereLutCancellation;
        if (task == null || cancellation == null || task.IsCompleted
            || cancellation.IsCancellationRequested)
            return;

        Interlocked.Exchange(ref _workerState, (int)AtmosphereLutWorkerState.CancelRequested);
        cancellation.Cancel();
        string bodyId = _atmosphereLutTaskBodyId ?? string.Empty;
        GD.Print($"PERF_ATMOS body={bodyId} stage=cancel_requested "
            + $"state=cancel_requested reason={reason} "
            + $"elapsedMs={AtmosphereLutWorkerElapsedMilliseconds:F1} "
            + $"queueMs={QueueMilliseconds():F1} "
            + $"bytes={AtmosphereLutWorkerProducedBytes}/"
            + $"{AtmosphereLutWorkerEstimatedBytes} "
            + $"estimatedBytes={AtmosphereLutWorkerEstimatedBytes}");

        if (_isExiting)
        {
            // _ExitTree will not receive another _Process callback. Dispose the CTS after
            // the detached worker observes cancellation; never wait on the main thread.
            _ = task.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _atmosphereLutCancellation = null;
        }
    }

    private void SetWorkerRunning()
    {
        _workerStartedTimestamp = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref _workerState, (int)AtmosphereLutWorkerState.Running);
    }

    private void SetWorkerPhase(AtmosphereLutWorkerPhase phase, long producedBytes)
    {
        Interlocked.Exchange(ref _workerPhase, (int)phase);
        Interlocked.Exchange(ref _workerProducedBytes, producedBytes);
    }

    private void SetWorkerProducedBytes(long producedBytes) =>
        Interlocked.Exchange(ref _workerProducedBytes, producedBytes);

    private void EmitWorkerTelemetryIfChanged()
    {
        int state = Volatile.Read(ref _workerState);
        int phase = Volatile.Read(ref _workerPhase);
        int generation = _atmosphereLutGeneration;
        long now = Stopwatch.GetTimestamp();
        bool changed = generation != _lastTelemetryGeneration
            || state != _lastTelemetryState
            || phase != _lastTelemetryPhase;
        bool periodic = _lastTelemetryTimestamp == 0
            || now - _lastTelemetryTimestamp >= Stopwatch.Frequency * 5;
        if (!changed && !periodic) return;

        _lastTelemetryGeneration = generation;
        _lastTelemetryState = state;
        _lastTelemetryPhase = phase;
        _lastTelemetryTimestamp = now;
        string bodyId = _atmosphereLutTaskBodyId ?? _lastAtmosphereBodyId ?? string.Empty;
        string stage = state == (int)AtmosphereLutWorkerState.Queued
            ? "worker_queued"
            : state == (int)AtmosphereLutWorkerState.Running
                ? "worker_running"
                : "worker_progress";
        GD.Print($"PERF_ATMOS body={bodyId} stage={stage} "
            + $"state={WorkerStateName(state)} phase={WorkerPhaseName(phase)} "
            + $"generation={generation} elapsedMs={AtmosphereLutWorkerElapsedMilliseconds:F1} "
            + $"bytes={AtmosphereLutWorkerProducedBytes}/"
            + $"{AtmosphereLutWorkerEstimatedBytes}");
    }

    private double WorkerElapsedMilliseconds()
    {
        long queued = Interlocked.Read(ref _workerQueuedTimestamp);
        if (queued == 0) return 0.0;
        long started = Interlocked.Read(ref _workerStartedTimestamp);
        long begin = started > 0 ? started : queued;
        long finished = Interlocked.Read(ref _workerFinishedTimestamp);
        long end = finished > 0 ? finished : Stopwatch.GetTimestamp();
        return (end - begin) * 1000.0 / Stopwatch.Frequency;
    }

    private double QueueMilliseconds()
    {
        long queued = Interlocked.Read(ref _workerQueuedTimestamp);
        long started = Interlocked.Read(ref _workerStartedTimestamp);
        if (queued == 0 || started == 0) return 0.0;
        return (started - queued) * 1000.0 / Stopwatch.Frequency;
    }

    private static string WorkerStateName(int state) => state switch
    {
        (int)AtmosphereLutWorkerState.Queued => "queued",
        (int)AtmosphereLutWorkerState.Running => "running",
        (int)AtmosphereLutWorkerState.CancelRequested => "cancel_requested",
        (int)AtmosphereLutWorkerState.Completed => "completed",
        (int)AtmosphereLutWorkerState.Canceled => "canceled",
        (int)AtmosphereLutWorkerState.Faulted => "faulted",
        _ => "idle",
    };

    private static string WorkerPhaseName(int phase) => phase switch
    {
        (int)AtmosphereLutWorkerPhase.Transmittance => "transmittance",
        (int)AtmosphereLutWorkerPhase.GlobalMultipleScattering => "global_ms_order4",
        (int)AtmosphereLutWorkerPhase.ExperimentalOrderFive => "experimental_order5",
        (int)AtmosphereLutWorkerPhase.AngularAtlas => "angular_atlas",
        (int)AtmosphereLutWorkerPhase.Completed => "completed",
        _ => "none",
    };

    private static long EstimateAtmosphereLutPeakBytes(bool includeExperimentalOrderFive)
    {
        long transmittance = EstimateVectorBytes(TransmittanceLutWidth * TransmittanceLutHeight);
        long global = EstimateVectorBytes(MultipleScatteringLutWidth * MultipleScatteringLutHeight);
        long angular = EstimateVectorBytes(
            AngularScatteringLutWidth * AngularScatteringSolarLayers
            * AngularScatteringViewLayers * AngularScatteringMuLayers);
        long experimental = includeExperimentalOrderFive ? global : 0;
        return transmittance + global + angular + experimental;
    }

    private static long EstimateVectorBytes(long vectorCount) => checked(vectorCount * 3 * sizeof(double));

    private static long EstimateTextureBytes(int width, int height) =>
        checked((long)width * height * 4 * sizeof(float));

    private static string CreateAtmosphereLutCacheKey(
        string bodyId,
        AtmosphereDensityProfile profile,
        double planetRadius,
        double atmosphereTopAltitude,
        bool includeExperimentalOrderFive)
    {
        var builder = new StringBuilder(1_024);
        builder.Append(bodyId).Append('|')
            .Append(MultipleScatteringLutVersion).Append('|')
            .Append(RuntimeMultipleScatteringOrder).Append('|')
            .Append(includeExperimentalOrderFive ? "order5" : "official");
        // Keep the in-process cache safe when an interactive resolution or integration
        // setting changes without a version-string edit.  These are renderer controls;
        // the spectral oracle remains offline and never participates in this key.
        builder.Append("|transmittance=").Append(TransmittanceLutWidth).Append('x')
            .Append(TransmittanceLutHeight).Append('x').Append(TransmittanceLutSamples)
            .Append("|global=").Append(MultipleScatteringLutWidth).Append('x')
            .Append(MultipleScatteringLutHeight).Append('x')
            .Append(MultipleScatteringIntegrationSteps).Append('x')
            .Append(MultipleScatteringSolarSamples)
            .Append("|angular=").Append(AngularScatteringLutWidth).Append('x')
            .Append(AngularScatteringSolarLayers).Append('x')
            .Append(AngularScatteringViewLayers).Append('x')
            .Append(AngularScatteringMuLayers).Append('x')
            .Append(AngularScatteringOpticalDepthSamples);
        AppendDouble(builder, planetRadius);
        AppendDouble(builder, atmosphereTopAltitude);
        AppendDouble(builder, profile.TopAltitude);

        var atmosphere = profile.Atmosphere;
        AppendDouble(builder, atmosphere.SurfaceGravity);
        AppendDouble(builder, atmosphere.GeopotentialRadius);
        AppendDouble(builder, atmosphere.MaxAltitude);
        AppendDouble(builder, atmosphere.SeaLevelDensity);
        AppendDouble(builder, atmosphere.ScaleHeight);
        AppendDouble(builder, atmosphere.SeaLevelPressure);
        AppendDouble(builder, atmosphere.SeaLevelTemperature);
        AppendDouble(builder, atmosphere.MolarMass);
        AppendDouble(builder, atmosphere.ThermosphereScaleHeight);
        AppendDouble(builder, atmosphere.ThermosphereScaleHeightGrowth);
        AppendDouble(builder, atmosphere.ThermosphereTopAltitude);
        builder.Append('|').Append(atmosphere.Layers.Count);
        foreach (var layer in atmosphere.Layers)
        {
            AppendDouble(builder, layer.AltMin);
            AppendDouble(builder, layer.AltMax);
            AppendDouble(builder, layer.TempBase);
            AppendDouble(builder, layer.LapseRate);
        }

        var optics = profile.Optics;
        AppendVector(builder, optics.RayleighScattering);
        AppendVector(builder, optics.MieScattering);
        AppendVector(builder, optics.MieAbsorption);
        AppendVector(builder, optics.OzoneAbsorption);
        AppendDouble(builder, optics.RayleighScaleHeight);
        AppendDouble(builder, optics.MieScaleHeight);
        AppendDouble(builder, optics.OzoneCenterAltitude);
        AppendDouble(builder, optics.OzoneHalfWidth);
        AppendDouble(builder, optics.MieAnisotropy);
        return builder.ToString();
    }

    private static void AppendVector(StringBuilder builder, Vector3d value)
    {
        AppendDouble(builder, value.X);
        AppendDouble(builder, value.Y);
        AppendDouble(builder, value.Z);
    }

    private static void AppendDouble(StringBuilder builder, double value) =>
        builder.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));

    private static AtmosphereLutCpuResult BuildAtmosphereLutsCpu(
        string bodyId,
        string cacheKey,
        AtmosphereDensityProfile profile,
        double planetRadius,
        double atmosphereTopAltitude,
        bool includeExperimentalOrderFive,
        int generation,
        CancellationToken cancellationToken,
        SkyController telemetry)
    {
        // The LUT is deliberately asynchronous, but a normal-priority thread can
        // still starve the renderer on machines with few cores (and on llvmpipe).
        // Lower only this worker for its lifetime and restore the ThreadPool thread
        // before it returns to the pool; simulation/physics threads are untouched.
        using var workerPriority = new WorkerThreadPriorityScope();
        var stopwatch = Stopwatch.StartNew();
        telemetry?.SetWorkerRunning();
        telemetry?.SetWorkerPhase(AtmosphereLutWorkerPhase.Transmittance, 0);
        var transmittance = AtmosphereTransmittanceLut.Build(
            profile,
            planetRadius,
            atmosphereTopAltitude,
            TransmittanceLutWidth,
            TransmittanceLutHeight,
            TransmittanceLutSamples);
        cancellationToken.ThrowIfCancellationRequested();
        long transmittanceBytes = EstimateVectorBytes(transmittance.Width * transmittance.Height);
        telemetry?.SetWorkerProducedBytes(transmittanceBytes);

        telemetry?.SetWorkerPhase(AtmosphereLutWorkerPhase.GlobalMultipleScattering,
            transmittanceBytes);
        var global = AtmosphereMultipleScatteringLut.Build(
            profile,
            planetRadius,
            atmosphereTopAltitude,
            MultipleScatteringLutWidth,
            MultipleScatteringLutHeight,
            MultipleScatteringIntegrationSteps,
            MultipleScatteringSolarSamples,
            MultipleScatteringMaxOrder);
        cancellationToken.ThrowIfCancellationRequested();
        long globalBytes = EstimateVectorBytes(global.Width * global.Height);
        telemetry?.SetWorkerProducedBytes(transmittanceBytes + globalBytes);

        double experimentalMilliseconds = 0.0;
        long experimentalBytes = 0;
        if (includeExperimentalOrderFive)
        {
            telemetry?.SetWorkerPhase(AtmosphereLutWorkerPhase.ExperimentalOrderFive,
                transmittanceBytes + globalBytes);
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
            cancellationToken.ThrowIfCancellationRequested();
            telemetry?.SetWorkerProducedBytes(transmittanceBytes + globalBytes + experimentalBytes);
        }

        telemetry?.SetWorkerPhase(AtmosphereLutWorkerPhase.AngularAtlas,
            transmittanceBytes + globalBytes + experimentalBytes);
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
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();
        long angularBytes = EstimateVectorBytes(angular.Width * angular.PackedHeight);
        long peakBytes = transmittanceBytes + globalBytes + angularBytes + experimentalBytes;
        long uploadBytes = EstimateTextureBytes(transmittance.Width, transmittance.Height)
            + EstimateTextureBytes(angular.Width, angular.PackedHeight);
        telemetry?.SetWorkerProducedBytes(transmittanceBytes + angularBytes);
        telemetry?.SetWorkerPhase(AtmosphereLutWorkerPhase.Completed,
            transmittanceBytes + angularBytes);
        return new AtmosphereLutCpuResult(
            bodyId, cacheKey, generation, transmittance, angular,
            stopwatch.Elapsed.TotalMilliseconds, experimentalMilliseconds, experimentalBytes,
            transmittanceBytes + angularBytes, peakBytes, uploadBytes);
    }

    private sealed class WorkerThreadPriorityScope : IDisposable
    {
        private readonly Thread? _thread;
        private readonly ThreadPriority _previous;

        public WorkerThreadPriorityScope()
        {
            try
            {
                _thread = Thread.CurrentThread;
                _previous = _thread.Priority;
                _thread.Priority = ThreadPriority.BelowNormal;
            }
            catch (Exception exception) when (
                exception is PlatformNotSupportedException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                _thread = null;
                _previous = ThreadPriority.Normal;
            }
        }

        public void Dispose()
        {
            if (_thread == null) return;
            try { _thread.Priority = _previous; }
            catch (Exception exception) when (
                exception is PlatformNotSupportedException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                // Priority is an optional scheduling hint; failure to restore it
                // must never turn a valid LUT into a simulation fault.
            }
        }
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
        string CacheKey,
        int Generation,
        AtmosphereTransmittanceLut Transmittance,
        AtmosphereAngularMultipleScatteringLut Angular,
        double BuildMilliseconds,
        double ExperimentalOrderFiveMilliseconds,
        long ExperimentalOrderFiveEstimatedBytes,
        long RetainedCpuBytes,
        long PeakWorkingBytes,
        long UploadBytes);

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
