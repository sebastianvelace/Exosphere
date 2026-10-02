#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHADER="$ROOT/assets/shaders/space_sky.gdshader"
SKY="$ROOT/scripts/SkyController.cs"
SUN="$ROOT/scripts/SunController.cs"
EXPOSURE="$ROOT/scripts/VisualExposureController.cs"
PHASE_LIGHTING="$ROOT/scripts/PhaseLightingController.cs"
GROUND="$ROOT/scripts/EarthGroundController.cs"

fail() {
  echo "sky_runtime_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$SHADER" ]] || fail "missing sky shader"
[[ -f "$SKY" ]] || fail "missing SkyController"
[[ -f "$SUN" ]] || fail "missing SunController"
[[ -f "$EXPOSURE" ]] || fail "missing VisualExposureController"
[[ -f "$PHASE_LIGHTING" ]] || fail "missing PhaseLightingController"
[[ -f "$GROUND" ]] || fail "missing EarthGroundController"

# Realtime sampling must remain bounded without changing the CPU/LUT oracle.
rg -q --fixed-strings 'uniform float atmosphere_quality' "$SHADER" \
  || fail "shader quality uniform missing"
rg -q --fixed-strings 'float effective_step_count(float requested_steps)' "$SHADER" \
  || fail "fractional quality step normalization missing"
rg -q --fixed-strings 'return max(ceil(requested_steps), 1.0);' "$SHADER" \
  || fail "fractional quality step normalization is not clamped"
rg -q --fixed-strings 'float view_steps = effective_step_count(' "$SHADER" \
  || fail "view integration minimum bound missing"
rg -q --fixed-strings 'if (float(i) >= view_steps) break;' "$SHADER" \
  || fail "view loop does not honor quality bound"
rg -q --fixed-strings 'float cloud_view_steps = effective_step_count(' "$SHADER" \
  || fail "cloud integration minimum bound missing"
rg -q --fixed-strings 'if (float(i) >= cloud_view_steps || cloud_transmittance < 0.005) break;' "$SHADER" \
  || fail "cloud loop does not honor quality bound"
rg -q --fixed-strings 'float light_steps = effective_step_count(' "$SHADER" \
  || fail "solar integration step normalization missing"
rg -q --fixed-strings 'float cloud_light_steps = effective_step_count(' "$SHADER" \
  || fail "cloud-shadow step normalization missing"
rg -q '^const int CLOUD_VIEW_STEPS = 24;$' "$SHADER" \
  || fail "cloud view ceiling is not the validated 24-sample path"
# The coastal core covers the entire ray with a 32 x 4 budget. A 96 x 10
# adaptive march reproduced GPU watchdog resets during dense-cloud traversal.
rg -q '^const int CLOUD_LOCAL_VIEW_STEPS = 32;$' "$SHADER" \
  || fail "local cloud view ceiling is not explicitly bounded"
rg -q --fixed-strings 'for (int i = 0; i < CLOUD_LOCAL_VIEW_STEPS; i++)' "$SHADER" \
  || fail "local cloud loop bypasses its declared ceiling"
rg -q --fixed-strings 'local_interval ? float(CLOUD_LOCAL_VIEW_STEPS)' "$SHADER" \
  || fail "local quadrature does not cover the complete interval"
rg -q --fixed-strings 'cloud_local_sun_transmission_samples(position, to_sun, distance_to_edge,' "$SHADER" \
  || fail "local sky shadow quadrature bypasses its four-sample budget"
rg -q --fixed-strings 'for (int i = 0; i < 10; i++)' "$ROOT/assets/shaders/cloud_field.gdshaderinc" \
  || fail "local solar quadrature ceiling missing"
rg -q '^const int CLOUD_LIGHT_STEPS = 5;$' "$SHADER" \
  || fail "cloud shadow ceiling is not the bounded five-sample path"
rg -q --fixed-strings 'private const float LowAltitudeAtmosphereQuality = 0.48f;' "$SKY" \
  || fail "low-altitude visual quality is not bounded explicitly"

# The expensive coastal march must stay out of the full-resolution atmosphere
# and lighting cubemap. These routing guards supplement real GPU/image evidence;
# they are not a timing benchmark or a guarantee against driver resets.
rg -q '^render_mode use_quarter_res_pass;' "$SHADER" \
  || fail "bounded cloud subpass missing"
rg -q --fixed-strings 'vec4 integrate_clouds(vec3 view_dir)' "$SHADER" \
  || fail "cloud integral is not separated from atmosphere lighting"
rg -q --fixed-strings 'if (AT_QUARTER_RES_PASS)' "$SHADER" \
  || fail "distant cloud march is not routed to the reduced-resolution pass"
rg -q --fixed-strings 'lighting_pass ? 6.0 : render_step_count(12.0, float(VIEW_STEPS))' "$SHADER" \
  || fail "lighting view quadrature bypasses its startup bound"
rg -q --fixed-strings 'AT_CUBEMAP_PASS ? vec4(0.0, 0.0, 0.0, 1.0) : QUARTER_RES_COLOR' "$SHADER" \
  || fail "cubemap repeats cloud transport or clear-air transmission is incorrect"
# GLES in Godot 4.6.3 generates invalid sampler constructors for sky subpass
# reads. Its camera path must retain clouds without requesting that subpass.
rg -q '^#if CURRENT_RENDERER != RENDERER_COMPATIBILITY$' "$SHADER" \
  || fail "GLES sky subpass exclusion missing"
rg -q --fixed-strings 'if (!AT_CUBEMAP_PASS && cloud_enabled) clouds = integrate_clouds(normalize(EYEDIR));' "$SHADER" \
  || fail "bounded direct GLES clouds or cloud-free lighting guard missing"

# Removing the sky did not prevent the reproduced AMD reset. Terrain-side
# cloud transport needs its own opacity termination and bounded solar budget.
rg -q --fixed-strings 'if (transmission < 0.005) break;' "$ROOT/assets/shaders/cloud_surface.gdshaderinc" \
  || fail "terrain cloud transport continues through an opaque interior"
rg -q --fixed-strings 'cloud_local_sun_transmission_samples(point, sun, distance_m,' "$ROOT/assets/shaders/cloud_surface.gdshaderinc" \
  || fail "terrain local cloud lighting budget missing"
for shader in "$SHADER" "$ROOT/assets/shaders/cloud_surface.gdshaderinc"; do
  rg -q --fixed-strings 'clamp(cloud_light_sample_budget, 2, 4)' "$shader" \
    || fail "local solar quadrature bypasses its two-to-four-sample bound"
done
# Lookup tables have no mipmaps. Implicit derivatives inside divergent marches
# are undefined; explicit level zero preserves LUT filtering without derivatives.
if rg -q 'texture\((density_lut|transmittance_lut|multiple_scattering_lut|surface_density_lut|surface_solar_lut),' \
  "$SHADER" "$ROOT/assets/shaders/cloud_surface.gdshaderinc" "$ROOT/assets/shaders/surface_atmosphere.gdshaderinc"; then
  fail "implicit-derivative lookup inside atmospheric/cloud transport"
fi
rg -q --fixed-strings 'float atmosphereQuality = altitude < 45_000.0' "$SKY" \
  || fail "low-altitude quality is not altitude-gated"
rg -q --fixed-strings '_lastAtmosphereQuality' "$SKY" \
  || fail "atmosphere quality updates are not dirty-gated"

# Pad uses Realtime so the play camera is not stuck on a black Incremental
# cubemap at T=0. Incremental remains the high-altitude path.
rg -q --fixed-strings '? Sky.RadianceSizeEnum.Size128 : Sky.RadianceSizeEnum.Size256;' "$SKY" \
  || fail "radiance map does not respect Compatibility and RenderingDevice realtime sizes"
rg -q --fixed-strings '_env.Sky.ProcessMode = Sky.ProcessModeEnum.Realtime;' "$SKY" \
  || fail "pad sky process mode is not realtime"
rg -q --fixed-strings 'bool realtime = altitude < 45_000.0;' "$SKY" \
  || fail "sky process mode is not altitude-gated back to incremental"
rg -q --fixed-strings 'using var workerPriority = new WorkerThreadPriorityScope();' "$SKY" \
  || fail "atmosphere worker priority scope missing"

# Solar disc geometry is a presentation sample. SunController owns the bounded 20 Hz
# calculation; SkyController consumes the snapshot at its 12 Hz cadence instead of
# running a second limb-darkened body loop.
rg -q --fixed-strings 'VisualUpdatePeriodSeconds = 1.0 / 20.0' "$SUN" \
  || fail "solar geometry cadence missing"
rg -q --fixed-strings 'TryGetCachedSolarGeometry(' "$SUN" \
  || fail "solar geometry snapshot API missing"
rg -q --fixed-strings 'PERF_SOLAR_GEOMETRY mode=shared cadenceHz=20 skyConsumerHz=12' "$SUN" \
  || fail "solar geometry sharing telemetry missing"
rg -q --fixed-strings 'TryGetCachedSolarGeometry(atmosphereBodyId' "$SKY" \
  || fail "SkyController does not consume shared solar geometry"

# Custom shader uniforms must not be rewritten at render cadence when stable.
rg -q --fixed-strings '_lastCloudWeatherPrefilter' "$SKY" \
  || fail "cloud prefilter dirty cache missing"
rg -q --fixed-strings '_lastEyeStarGain' "$EXPOSURE" \
  || fail "eye-star dirty cache missing"
rg -q --fixed-strings 'System.Math.Abs(eyeStarGain - _lastEyeStarGain) > 0.005f' "$EXPOSURE" \
  || fail "eye-star update threshold missing"
rg -q --fixed-strings 'ColorDiffers(_env.AmbientLightColor, targetAmbient)' "$SKY" \
  || fail "sky ambient color dirty check missing"
rg -q --fixed-strings 'if (FloatDiffers(_env.AmbientLightEnergy, ambient))' "$PHASE_LIGHTING" \
  || fail "phase ambient energy dirty check missing"
rg -q --fixed-strings 'if (ColorDiffers(_light.LightColor, lightColor))' "$PHASE_LIGHTING" \
  || fail "phase light color dirty check missing"
rg -q --fixed-strings 'if (FloatDiffers(_light.LightEnergy, lightEnergy))' "$PHASE_LIGHTING" \
  || fail "phase light energy dirty check missing"
rg -q --fixed-strings 'Mathf.Abs(_environment.TonemapExposure - exposure) > 1e-4f' "$EXPOSURE" \
  || fail "tonemap exposure dirty check missing"
rg -q --fixed-strings '_groundShaderStateInitialized' "$GROUND" \
  || fail "earth-ground shader state cache missing"
rg -q --fixed-strings 'FloatDiffers(_lastFade, fade)' "$GROUND" \
  || fail "earth-ground fade dirty check missing"
rg -q --fixed-strings '_lastSunDirection.DistanceSquaredTo(sunDirection)' "$GROUND" \
  || fail "earth-ground sun direction dirty check missing"

echo "sky_runtime_performance_contract_test: PASS (bounded global 24x5 and local 32x4 cloud paths, cached uniforms, low-priority LUT worker)"
