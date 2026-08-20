#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROBE="$ROOT/scripts/RenderPerformanceProbe.cs"
BRIDGE="$ROOT/scripts/SimulationBridge.cs"

fail() {
  echo "render_performance_probe_contract_test: FAIL: $1" >&2
  exit 1
}

require_text() {
  local file="$1" pattern="$2" description="$3"
  rg -q --fixed-strings "$pattern" "$file" || fail "$description"
}

[[ -f "$PROBE" ]] || fail "probe source is missing"
require_text "$PROBE" 'EXOSPHERE_RENDER_PROBE' \
  "probe must be opt-in through an environment variable"
require_text "$PROBE" 'EXOSPHERE_RENDER_AB' \
  "probe must support explicit render A/B overrides"
require_text "$PROBE" 'no_directional_shadows' \
  "probe must expose a directional-shadow A/B variant"
require_text "$PROBE" 'hide_pad' \
  "probe must expose a launch-pad A/B variant"
require_text "$PROBE" 'hide_sky' \
  "probe must expose an atmosphere-sky A/B variant"
require_text "$PROBE" 'sky_quality_low' \
  "probe must expose a low-atmosphere-quality A/B variant"
require_text "$PROBE" 'earth_day_gain_090' \
  "probe must expose an isolated scaled-space Earth gain A/B variant"
require_text "$PROBE" 'earth_day_gain_075' \
  "probe must expose a stronger scaled-space Earth gain A/B variant"
require_text "$PROBE" 'earth_cloud_amount_065' \
  "probe must expose an isolated scaled-space Earth cloud A/B variant"
require_text "$PROBE" 'earth_cloud_amount_040' \
  "probe must expose a stronger scaled-space Earth cloud A/B variant"
require_text "$PROBE" 'Earth_mesh' \
  "probe must target the scaled-space Earth material explicitly"
require_text "$PROBE" 'ViewportSetMeasureRenderTime' \
  "probe must enable viewport render measurements"
require_text "$PROBE" 'ViewportGetMeasuredRenderTimeCpu' \
  "probe must expose CPU render timing"
require_text "$PROBE" 'ViewportGetMeasuredRenderTimeGpu' \
  "probe must expose GPU render timing"
require_text "$PROBE" 'GetRenderingInfo' \
  "probe must expose renderer counters"
require_text "$PROBE" 'TotalDrawCallsInFrame' \
  "probe must expose draw calls"
require_text "$PROBE" 'TotalPrimitivesInFrame' \
  "probe must expose primitives"
require_text "$PROBE" 'VideoMemUsed' \
  "probe must expose renderer video memory"
require_text "$PROBE" 'gpu_ms={Metric(gpuMs)}' \
  "probe must preserve an explicit GPU sample field"
require_text "$PROBE" ': "NOT_MEASURED";' \
  "probe must fail closed when GPU timing is unavailable"
require_text "$PROBE" 'ViewportSetMeasureRenderTime(_viewportRid, false)' \
  "probe must disable timing on exit"
require_text "$BRIDGE" 'RenderPerformanceProbe.IsRequested()' \
  "Flight must create the probe only when explicitly requested"

if rg -q 'OS\.GetVideoAdapterDriverInfo' "$PROBE"; then
  fail "probe must not call the documented blocking driver-info API"
fi

echo "render_performance_probe_contract_test: PASS (opt-in in-process CPU/GPU/render counters)"
