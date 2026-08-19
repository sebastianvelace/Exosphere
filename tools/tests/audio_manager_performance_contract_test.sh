#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
AUDIO="$ROOT_DIR/scripts/AudioManager.cs"

fail() {
  echo "audio_manager_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$AUDIO" ]] || fail "missing AudioManager.cs"

# Audio generation remains continuous, but physics-facing level inputs are a presentation
# sample and must not be recomputed once per rendered frame.
rg -q --fixed-strings 'PhysicsSamplePeriodSeconds = 1.0 / 20.0' "$AUDIO" \
  || fail "audio physics sample cadence missing"
rg -q --fixed-strings '_physicsSampleTimer' "$AUDIO" \
  || fail "audio sample timer missing"
rg -q --fixed-strings 'SampleAudioLevels(vessel, universe);' "$AUDIO" \
  || fail "audio sampled-state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$AUDIO" \
  || fail "audio vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$AUDIO" \
  || fail "audio universe transition refresh missing"

# Keep synthesis on the audio-buffer path and preserve the physical mappings that align
# sound with visuals and the simulation.
rg -q --fixed-strings 'FillEngineSl();' "$AUDIO" \
  || fail "sea-level engine generator was removed"
rg -q --fixed-strings 'FillEngineVac();' "$AUDIO" \
  || fail "vacuum engine generator was removed"
rg -q --fixed-strings 'FillAero();' "$AUDIO" \
  || fail "aero generator was removed"
rg -q --fixed-strings 'double flux = vessel.ComputeStagnationHeatFlux(rho, surfVel);' "$AUDIO" \
  || fail "audio heat-flux source was removed"
rg -q --fixed-strings 'VehicleVisualPhysics.IsVisibleReentryHeating(radialSpeed, flux)' "$AUDIO" \
  || fail "audio re-entry gate was removed"
rg -q --fixed-strings 'double q        = vessel.GetDynamicPressure(body);' "$AUDIO" \
  || fail "audio dynamic-pressure source was removed"
rg -q --fixed-strings 'double radialSpeed = surfVel.Dot(' "$AUDIO" \
  || fail "audio surface-velocity sample is not reused"
rg -q --fixed-strings 'Mathf.Lerp(_slLevel,     _sampledSlTarget' "$AUDIO" \
  || fail "audio per-frame level smoothing missing"

echo "audio_manager_performance_contract_test: PASS (physics sample=20Hz, generators continuous, mappings preserved)"
