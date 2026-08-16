#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHADER="$ROOT/assets/shaders/space_sky.gdshader"
SKY="$ROOT/scripts/SkyController.cs"
EXPOSURE="$ROOT/scripts/VisualExposureController.cs"

fail() {
  echo "sky_runtime_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$SHADER" ]] || fail "missing sky shader"
[[ -f "$SKY" ]] || fail "missing SkyController"
[[ -f "$EXPOSURE" ]] || fail "missing VisualExposureController"

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

# The selected Godot path must be the low-frequency incremental map, not the slower
# importance-sampling path repeatedly invalidated by dynamic uniforms.
rg -q --fixed-strings '_env.Sky.RadianceSize = Sky.RadianceSizeEnum.Size128;' "$SKY" \
  || fail "radiance map is not bounded to 128"
rg -q --fixed-strings '_env.Sky.ProcessMode = Sky.ProcessModeEnum.Incremental;' "$SKY" \
  || fail "sky process mode is not incremental"
rg -q --fixed-strings 'using var workerPriority = new WorkerThreadPriorityScope();' "$SKY" \
  || fail "atmosphere worker priority scope missing"

# Custom shader uniforms must not be rewritten at render cadence when stable.
rg -q --fixed-strings '_lastCloudWeatherPrefilter' "$SKY" \
  || fail "cloud prefilter dirty cache missing"
rg -q --fixed-strings '_lastEyeStarGain' "$EXPOSURE" \
  || fail "eye-star dirty cache missing"
rg -q --fixed-strings 'System.Math.Abs(eyeStarGain - _lastEyeStarGain) > 0.005f' "$EXPOSURE" \
  || fail "eye-star update threshold missing"

echo "sky_runtime_performance_contract_test: PASS (bounded quadrature, cached uniforms, low-priority LUT worker)"
