#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PLASMA="$ROOT_DIR/scripts/ReentryPlasmaController.cs"

fail() {
  echo "reentry_plasma_performance_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$PLASMA" ]] || fail "missing ReentryPlasmaController.cs"

# Plasma is a presentation consumer of the physical heat flux. Keep the solver-facing
# equations intact while bounding the render-side sampling rate.
rg -q --fixed-strings 'VisualSamplePeriodSeconds = 1.0 / 20.0' "$PLASMA" \
  || fail "re-entry plasma visual cadence missing"
rg -q --fixed-strings '_visualSampleTimer' "$PLASMA" \
  || fail "re-entry plasma sample timer missing"
rg -q --fixed-strings 'SetCoreEffectsVisible(true);' "$PLASMA" \
  || fail "active plasma visibility path missing"
rg -q --fixed-strings 'if (_shock != null && _shock.Visible != visible)' "$PLASMA" \
  || fail "shock visibility setter is not dirty-gated"
rg -q --fixed-strings 'if (_wake != null && _wake.Visible != visible)' "$PLASMA" \
  || fail "wake visibility setter is not dirty-gated"
rg -q --fixed-strings 'if (edge.Mesh.Visible != visible)' "$PLASMA" \
  || fail "localized edge visibility setter is not dirty-gated"
rg -q --fixed-strings 'SyncToVesselFrame();' "$PLASMA" \
  || fail "re-entry plasma is not synchronized to the active vessel frame"

# Vehicle-shape detection must remain bounded and allocation-free in the visual sample.
rg -q --fixed-strings 'private static bool HasSuperHeavy(Vessel vessel)' "$PLASMA" \
  || fail "re-entry vehicle-shape helper missing"
rg -q --fixed-strings 'for (int partIndex = 0; partIndex < parts.Count; partIndex++)' "$PLASMA" \
  || fail "re-entry vehicle-shape scan is not indexed"
if rg -q --fixed-strings 'Parts.Parts.Any(' "$PLASMA"; then
  fail "re-entry plasma still enumerates vehicle shape through LINQ"
fi

# Preserve the physics-driven source of truth and both thermal visual gates.
rg -q --fixed-strings 'vessel.ComputeStagnationHeatFlux(density, surfVel)' "$PLASMA" \
  || fail "stagnation heat-flux source was removed"
rg -q --fixed-strings 'VehicleVisualPhysics.ReentryPlasmaVisualIntensity' "$PLASMA" \
  || fail "re-entry visual intensity gate was removed"
rg -q --fixed-strings 'UpdateLocalizedEdgeGlows((float)intensity' "$PLASMA" \
  || fail "localized re-entry heat cues were removed"

echo "reentry_plasma_performance_contract_test: PASS (sample=20Hz, visibility dirty-gated, heat physics preserved)"
