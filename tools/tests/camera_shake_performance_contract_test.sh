#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CAMERA="$ROOT_DIR/scripts/CameraShake.cs"

fail() {
  echo "camera_shake_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$CAMERA" ]] || fail "missing CameraShake.cs"

# Keep the high-rate envelope/noise integration while bounding physics-facing reads to a
# presentation sample. This must not change the deterministic simulation tick.
rg -q --fixed-strings 'PhysicsSamplePeriodSeconds = 1.0 / 20.0' "$CAMERA" \
  || fail "camera-shake physics sample cadence missing"
rg -q --fixed-strings '_physicsSampleTimer' "$CAMERA" \
  || fail "camera-shake sample timer missing"
rg -q --fixed-strings 'SampleFlightState(vessel, universe);' "$CAMERA" \
  || fail "camera-shake sampled state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$CAMERA" \
  || fail "camera-shake vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$CAMERA" \
  || fail "camera-shake universe transition refresh missing"

# The smooth response remains per frame; sampling must not remove damping or the existing
# physical sources of q, thrust and aerodynamic entry load.
rg -q --fixed-strings '_thrustEnv = Damp(_thrustEnv' "$CAMERA" \
  || fail "engine envelope smoothing missing"
rg -q --fixed-strings '_buffetEnv = Damp(_buffetEnv' "$CAMERA" \
  || fail "Max-Q envelope smoothing missing"
rg -q --fixed-strings 'double q = 0.5 * density * v * v;' "$CAMERA" \
  || fail "dynamic-pressure equation changed"
rg -q --fixed-strings 'var thrust = vessel.ComputeThrust(body);' "$CAMERA" \
  || fail "thrust source was removed"
rg -q --fixed-strings 'var drag   = vessel.ComputeDrag(body);' "$CAMERA" \
  || fail "drag source was removed"
rg -q --fixed-strings 'double aeroG = drag.Magnitude / mass / 9.80665;' "$CAMERA" \
  || fail "entry-load equation changed"

echo "camera_shake_performance_contract_test: PASS (physics sample=20Hz, envelopes remain per-frame)"
