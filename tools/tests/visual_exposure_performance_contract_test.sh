#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EXPOSURE="$ROOT_DIR/scripts/VisualExposureController.cs"

fail() {
  echo "visual_exposure_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$EXPOSURE" ]] || fail "missing VisualExposureController.cs"

# Eye adaptation remains per frame, but its physical luminance inputs are a bounded
# presentation sample. Direct transmittance keeps its own validated 10 Hz cadence.
rg -q --fixed-strings 'PresentationSamplePeriodSeconds = 1.0 / 20.0' "$EXPOSURE" \
  || fail "visual-exposure presentation cadence missing"
rg -q --fixed-strings '_presentationSampleTimer' "$EXPOSURE" \
  || fail "visual-exposure sample timer missing"
rg -q --fixed-strings 'SampleExposureState(vessel, universe);' "$EXPOSURE" \
  || fail "visual-exposure sampled-state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$EXPOSURE" \
  || fail "visual-exposure vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$EXPOSURE" \
  || fail "visual-exposure universe transition refresh missing"

# Preserve adaptation, heat-driven luminance and the existing optical cadence.
rg -q --fixed-strings 'float exposure = (float)_adaptation.Update(target, delta);' "$EXPOSURE" \
  || fail "per-frame exposure adaptation was removed"
rg -q --fixed-strings 'vessel.ComputeStagnationHeatFlux(density, surfVel)' "$EXPOSURE" \
  || fail "exposure heat-flux source was removed"
rg -q --fixed-strings 'DirectTransmittanceCadenceSeconds = 0.10' "$EXPOSURE" \
  || fail "direct-transmittance cadence changed"
rg -q --fixed-strings 'optics.DirectSolarTransmittance(' "$EXPOSURE" \
  || fail "direct-transmittance integration was removed"
rg -q --fixed-strings '_environment.TonemapExposure - exposure' "$EXPOSURE" \
  || fail "exposure dirty gate was removed"

echo "visual_exposure_performance_contract_test: PASS (inputs=20Hz, adaptation per-frame, direct optics=10Hz)"
