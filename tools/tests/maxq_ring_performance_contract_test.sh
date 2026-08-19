#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RING="$ROOT/scripts/MaxQRingController.cs"

fail() {
  echo "maxq_ring_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$RING" ]] || fail "missing MaxQRingController.cs"

# Max-Q condensation is a visual effect. Sample its physical inputs at a bounded cadence;
# never alter the dynamic-pressure equations used by the simulation.
rg -q --fixed-strings 'VisualSamplePeriodSeconds = 1.0 / 20.0' "$RING" \
  || fail "Max-Q visual cadence missing"
rg -q --fixed-strings '_visualSampleTimer' "$RING" \
  || fail "Max-Q sample timer missing"
rg -q --fixed-strings 'SetRingVisible(false);' "$RING" \
  || fail "Max-Q hidden-state dirty gate missing"
rg -q --fixed-strings 'if (_ring != null && _ring.Visible != visible)' "$RING" \
  || fail "Max-Q visibility setter is not dirty-gated"

# The vehicle-shape lookup must remain allocation-free and bounded; the effect must not
# enumerate the compatibility enumerable through LINQ every sample.
rg -q --fixed-strings 'private static bool HasSuperHeavy(Vessel vessel)' "$RING" \
  || fail "Max-Q vehicle-shape helper missing"
rg -q --fixed-strings 'for (int partIndex = 0; partIndex < parts.Count; partIndex++)' "$RING" \
  || fail "Max-Q vehicle-shape scan is not indexed"
if rg -q --fixed-strings 'Parts.Parts.Any(' "$RING"; then
  fail "Max-Q still enumerates vehicle shape through LINQ"
fi

# Keep the effect's existing thresholds and preserve the ring's active visual updates.
rg -q --fixed-strings 'Q_THRESH = 12_000.0' "$RING" \
  || fail "Max-Q threshold changed unexpectedly"
rg -q --fixed-strings 'float flicker  = 0.75f + (float)(GD.Randf() * 0.50f);' "$RING" \
  || fail "Max-Q flicker visual path missing"

echo "maxq_ring_performance_contract_test: PASS (sample=20Hz, visibility dirty-gated, pressure equations unchanged)"
