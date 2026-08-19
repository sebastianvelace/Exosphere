#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STARFIELD="$ROOT_DIR/scripts/StarfieldController.cs"

fail() {
  echo "starfield_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$STARFIELD" ]] || fail "missing StarfieldController.cs"

# Camera recentering remains per frame, but atmosphere-dependent alpha/streak inputs are a
# presentation sample and must not be recalculated at render frequency.
rg -q --fixed-strings 'SimulationSamplePeriodSeconds = 1.0 / 20.0' "$STARFIELD" \
  || fail "starfield simulation sample cadence missing"
rg -q --fixed-strings '_simulationSampleTimer' "$STARFIELD" \
  || fail "starfield sample timer missing"
rg -q --fixed-strings 'SampleSimulationState(vessel, universe);' "$STARFIELD" \
  || fail "starfield sampled-state path missing"
rg -q --fixed-strings 'ReferenceEquals(vessel, _sampledVessel)' "$STARFIELD" \
  || fail "starfield vessel transition refresh missing"
rg -q --fixed-strings 'ReferenceEquals(universe, _sampledUniverse)' "$STARFIELD" \
  || fail "starfield universe transition refresh missing"

# Preserve per-frame camera tracking and the atmosphere/velocity cues.
rg -q --fixed-strings 'GlobalPosition = _camera.GlobalPosition;' "$STARFIELD" \
  || fail "starfield camera recentering was removed"
rg -q --fixed-strings '_streaks.GlobalTransform = _camera.GlobalTransform;' "$STARFIELD" \
  || fail "air-streak camera transform was removed"
rg -q --fixed-strings 'body.GetAtmosphericDensity(vessel.Position)' "$STARFIELD" \
  || fail "starfield density cue was removed"
rg -q --fixed-strings 'vessel.GetSurfaceVelocity(body).Magnitude' "$STARFIELD" \
  || fail "starfield speed cue was removed"
rg -q --fixed-strings 'if (_streaks != null && _streaks.Emitting != streaksOn)' "$STARFIELD" \
  || fail "starfield streak visibility dirty gate missing"

echo "starfield_performance_contract_test: PASS (camera per-frame, simulation sample=20Hz, cue setters dirty-gated)"
