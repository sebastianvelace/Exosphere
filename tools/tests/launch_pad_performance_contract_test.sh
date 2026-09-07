#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PAD="$ROOT/scripts/LaunchPadController.cs"

fail() {
  echo "launch_pad_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$PAD" ]] || fail "missing LaunchPadController.cs"

# The launch complex is presentation-only. Catch physics remains in Universe; the pad
# samples fleet state at a bounded cadence and keeps the arm interpolation smooth.
rg -q --fixed-strings 'CatchPresentationPeriodSeconds = 1.0 / 20.0' "$PAD" \
  || fail "catch presentation cadence missing"
rg -q --fixed-strings 'RefreshCatchState();' "$PAD" \
  || fail "catch state refresh missing"
rg -q --fixed-strings 'for (int vesselIndex = 0; vesselIndex < vessels.Count; vesselIndex++)' "$PAD" \
  || fail "catch state scan is not index-based"
if rg -q --fixed-strings 'vessels?.Any(' "$PAD"; then
  fail "launch pad still enumerates catch state through LINQ"
fi

# Lighting and arm transforms must be dirty-gated while the pad remains visible for the
# complete reentry/catch track.
rg -q --fixed-strings '_lastNightFloodlightsState' "$PAD" \
  || fail "night-light dirty cache missing"
rg -q --fixed-strings '_lastChopstickScale' "$PAD" \
  || fail "chopstick pose dirty cache missing"
rg -q --fixed-strings 'Mathf.Abs(scale - _lastChopstickScale) > 0.0001f' "$PAD" \
  || fail "chopstick pose threshold missing"
rg -q --fixed-strings 'float target = CatchCaptured ? 1f : 0f;' "$PAD" \
  || fail "chopstick close pose is no longer catch-authoritative"
# The detailed pad must retire before the 12 km far-field acceptance frame.
# Allow tuning inside the regional handoff instead of pinning an obsolete literal.
awk '/public const double PadVisibilityCeilingM =/ {
  value = $6; gsub(/[_;]/, "", value);
  if (value + 0 >= 3500 && value + 0 < 12000) valid = 1;
} END { exit !valid }' "$PAD" \
  || fail "public pad visibility ceiling must retire hero geometry before 12 km"
rg -q --fixed-strings 'PadVisibilityCeilingM' "$ROOT/scripts/SimulationBridge.cs" \
  || fail "pad visibility is not updated from bridge altitude/catch state"

echo "launch_pad_performance_contract_test: PASS (catch scan=20Hz, lights/arms dirty-gated, physics authoritative)"
