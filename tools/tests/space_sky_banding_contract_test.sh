#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SHADER="$ROOT/assets/shaders/space_sky.gdshader"
fail() { echo "space_sky_banding_contract_test: FAIL: $*" >&2; exit 1; }

[[ -f "$SHADER" ]] || fail "space sky shader missing"

# The visible artifact is produced by the cloud-shell view quadrature, not by the
# atmospheric colour gradient. Keep enough stratified samples to resolve tangent
# horizon crossings and retain stable (non-TIME) in-segment jitter.
rg -q '^const int CLOUD_VIEW_STEPS = 24;$' "$SHADER" \
  || fail "cloud view quadrature regressed below the validated 24-sample path"
rg -q '^const float CLOUD_VIEW_JITTER = 0\.40;$' "$SHADER" \
  || fail "stable cloud-segment jitter changed without revalidation"
rg -q 'float jitter = stable_jitter\(' "$SHADER" \
  || fail "cloud view integration lost deterministic sample jitter"
rg -q 'clamp\(0\.5 \+ jitter \* CLOUD_VIEW_JITTER, 0\.14, 0\.86\)' "$SHADER" \
  || fail "cloud jitter is not bounded inside each ray segment"
if rg -q 'CLOUD_VIEW_STEPS = (1[0-9]|2[0-3]);' "$SHADER"; then
  fail "cloud view quadrature is in the known banding-prone range"
fi

echo "space_sky_banding_contract_test: PASS (24-sample stable cloud-shell quadrature)"
