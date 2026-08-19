#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STARTUP="$ROOT_DIR/scripts/EngineStartupController.cs"

fail() {
  echo "engine_startup_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$STARTUP" ]] || fail "missing EngineStartupController.cs"

# The pre-liftoff VFX is presentation-only. Keep Drive/smoothing per frame but sample body,
# altitude, engine presence and vehicle shape at a bounded cadence.
rg -q --fixed-strings 'PhysicsSamplePeriodSeconds = 1.0 / 20.0' "$STARTUP" \
  || fail "engine-startup physics sample cadence missing"
rg -q --fixed-strings '_physicsSampleTimer' "$STARTUP" \
  || fail "engine-startup sample timer missing"
rg -q --fixed-strings 'SampleStartupState(vessel, universe);' "$STARTUP" \
  || fail "engine-startup sampled-state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$STARTUP" \
  || fail "engine-startup vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$STARTUP" \
  || fail "engine-startup universe transition refresh missing"

# Preserve the actual startup gate and per-frame visual smoothing.
rg -q --fixed-strings 'vessel.IsGroundHeld' "$STARTUP" \
  || fail "ground-hold startup gate was removed"
rg -q --fixed-strings 'vessel.HasActiveEngineParts' "$STARTUP" \
  || fail "engine-presence startup gate was removed"
rg -q --fixed-strings 'altitude < MaxStartupAltitudeM' "$STARTUP" \
  || fail "startup altitude gate was removed"
rg -q --fixed-strings 'Drive(_sampledThrottle, delta);' "$STARTUP" \
  || fail "per-frame startup visual drive missing"
rg -q --fixed-strings 'private static bool HasSuperHeavy(Vessel vessel)' "$STARTUP" \
  || fail "engine-startup vehicle-shape helper missing"
rg -q --fixed-strings 'for (int partIndex = 0; partIndex < parts.Count; partIndex++)' "$STARTUP" \
  || fail "engine-startup vehicle-shape scan is not indexed"
if rg -q --fixed-strings 'Parts.Parts.Any(' "$STARTUP"; then
  fail "engine-startup still enumerates vehicle shape through LINQ"
fi

echo "engine_startup_performance_contract_test: PASS (physics sample=20Hz, Drive remains per-frame, startup gate preserved)"
