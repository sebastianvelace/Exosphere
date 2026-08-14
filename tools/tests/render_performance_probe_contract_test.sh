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
