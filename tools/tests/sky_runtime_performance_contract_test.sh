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
rg -q --fixed-strings 'if (float(i) >= cloud_view_steps) break;' "$SHADER" \
  || fail "cloud loop does not honor quality bound"
rg -q --fixed-strings 'float light_steps = effective_step_count(' "$SHADER" \
  || fail "solar integration step normalization missing"
rg -q --fixed-strings 'float cloud_light_steps = effective_step_count(' "$SHADER" \
  || fail "cloud-shadow step normalization missing"
rg -q '^const int CLOUD_VIEW_STEPS = 24;$' "$SHADER" \
  || fail "cloud view ceiling is not the validated 24-sample path"
rg -q '^const int CLOUD_LIGHT_STEPS = 5;$' "$SHADER" \
  || fail "cloud shadow ceiling is not the bounded five-sample path"
rg -q --fixed-strings 'private const float LowAltitudeAtmosphereQuality = 0.48f;' "$SKY" \
  || fail "low-altitude visual quality is not bounded explicitly"
rg -q --fixed-strings 'float atmosphereQuality = altitude < 45_000.0' "$SKY" \
  || fail "low-altitude quality is not altitude-gated"
rg -q --fixed-strings '_lastAtmosphereQuality' "$SKY" \
  || fail "atmosphere quality updates are not dirty-gated"

# Pad uses Realtime so the play camera is not stuck on a black Incremental
# cubemap at T=0. Incremental remains the high-altitude path.
rg -q --fixed-strings '_env.Sky.RadianceSize = Sky.RadianceSizeEnum.Size128;' "$SKY" \
  || fail "radiance map is not bounded to 128"
rg -q --fixed-strings '_env.Sky.ProcessMode = Sky.ProcessModeEnum.Realtime;' "$SKY" \
  || fail "pad sky process mode is not realtime"
rg -q --fixed-strings 'bool realtime = altitude < 28_000.0;' "$SKY" \
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

echo "sky_runtime_performance_contract_test: PASS (bounded 24x5 cloud quadrature, cached uniforms, low-priority LUT worker)"
