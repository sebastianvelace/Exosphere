#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EFFECTS="$ROOT_DIR/scripts/LaunchEffectsController.cs"

fail() {
  echo "launch_effects_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$EFFECTS" ]] || fail "missing LaunchEffectsController.cs"

# The deluge condition is presentation-only. Sample body/altitude/engine state at 20 Hz while
# preserving the per-frame particle/MultiMesh animation.
rg -q --fixed-strings 'PhysicsSamplePeriodSeconds = 1.0 / 20.0' "$EFFECTS" \
  || fail "launch-effects physics sample cadence missing"
rg -q --fixed-strings '_physicsSampleTimer' "$EFFECTS" \
  || fail "launch-effects sample timer missing"
rg -q --fixed-strings 'SampleLaunchState(vessel, universe);' "$EFFECTS" \
  || fail "launch-effects sampled-state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$EFFECTS" \
  || fail "launch-effects vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$EFFECTS" \
  || fail "launch-effects universe transition refresh missing"
rg -q --fixed-strings 'DriveAmounts(_intensity);' "$EFFECTS" \
  || fail "per-frame particle drive missing"
rg -q --fixed-strings 'DriveImmediateSteam(_intensity, _ignitionAge);' "$EFFECTS" \
  || fail "per-frame MultiMesh animation missing"

# Preserve launch gates and the existing emission dirty gate.
rg -q --fixed-strings 'vessel.HasActiveEngineParts' "$EFFECTS" \
  || fail "engine gate was removed"
rg -q --fixed-strings 'altitude < TriggerCeilingM' "$EFFECTS" \
  || fail "altitude gate was removed"
rg -q --fixed-strings 'if (_emitting == on) return;' "$EFFECTS" \
  || fail "particle emission setter is not dirty-gated"

echo "launch_effects_performance_contract_test: PASS (physics sample=20Hz, particle animation per-frame, launch gates preserved)"
