#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SKY="$ROOT/scripts/SkyController.cs"
ORACLE="$ROOT/ExosphereSimulation/SpectralAtmosphereOracle.cs"

fail() {
  echo "atmosphere_phase23_contract: FAIL: $1" >&2
  exit 1
}

require_fixed() {
  local needle="$1"
  local file="$2"
  rg -Fq "$needle" "$file" || fail "missing '$needle' in ${file#$ROOT/}"
}

[[ -f "$SKY" ]] || fail "SkyController.cs missing"
[[ -f "$ORACLE" ]] || fail "SpectralAtmosphereOracle.cs missing"

# The interactive renderer remains RGB and official order four.  The spectral oracle may
# provide the shared numeric constants, but it must not be built or evaluated by the frame
# loop or by the LUT worker.
require_fixed "public const int RuntimeMultipleScatteringOrder = SpectralAtmosphereOracle.OfficialRendererOrder;" "$SKY"
require_fixed "private const int MultipleScatteringMaxOrder = 4;" "$SKY"
if rg -n 'SpectralAtmosphereOracle\.Build|\.Evaluate\(' "$SKY" >/dev/null; then
  fail "runtime SkyController invokes the offline spectral oracle"
fi

# Queue/worker lifecycle must be observable and cancellation must be requested from _ExitTree
# without synchronously joining the worker on the main thread.
require_fixed "Task.Run(() => BuildAtmosphereLutsCpu(" "$SKY"
require_fixed "cancellation.Token" "$SKY"
require_fixed "CancelAtmosphereLutBuild(\"exit_tree\")" "$SKY"
require_fixed "state=cancel_requested" "$SKY"
require_fixed "state=canceled" "$SKY"
if rg -n 'task\.(Wait|Result)|\.Wait\(' "$SKY" >/dev/null; then
  fail "SkyController synchronously waits for the atmosphere worker"
fi

# Keep the telemetry units explicit: queue time, CPU worker time, current/estimated payload,
# retained CPU vectors, peak vector working set and RGBA32F upload payload are separate fields.
require_fixed "queueMs=" "$SKY"
require_fixed "estimatedBytes=" "$SKY"
require_fixed "retainedCpuBytes=" "$SKY"
require_fixed "peakBytes=" "$SKY"
require_fixed "uploadBytes=" "$SKY"

# The cache key must include profile/body, official-vs-experimental order and all resolution /
# integration controls, so changing a LUT dimension cannot reuse an incompatible texture.
require_fixed "includeExperimentalOrderFive ? \"order5\" : \"official\"" "$SKY"
for setting in \
  "transmittance=\"" \
  "global=\"" \
  "angular=\"" \
  "TransmittanceLutSamples" \
  "MultipleScatteringIntegrationSteps" \
  "MultipleScatteringSolarSamples" \
  "AngularScatteringOpticalDepthSamples"; do
  require_fixed "$setting" "$SKY"
done

# The oracle exposes the fixed 9-band/offline contract and keeps O5 diagnostic-only.
require_fixed "public const int BandCount = 9;" "$ORACLE"
require_fixed "public const int OfficialRendererOrder = 4;" "$ORACLE"
require_fixed "public const int ExperimentalOrder = 5;" "$ORACLE"
require_fixed "Order 5 is intentionally diagnostic only." "$ORACLE"

echo "atmosphere_phase23_contract: PASS"
