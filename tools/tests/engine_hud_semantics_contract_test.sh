#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HUD="$ROOT_DIR/scripts/EngineGridHUD.cs"
RENDERER="$ROOT_DIR/scripts/VesselRenderer.cs"
PRESENTER="$ROOT_DIR/ExosphereSimulation/Presentation/EngineHudPresentation.cs"

fail() {
  echo "engine_hud_semantics_contract_test: FAIL: $*" >&2
  exit 1
}

for file in "$HUD" "$RENDERER" "$PRESENTER"; do
  [[ -f "$file" ]] || fail "missing $file"
done

rg -q --fixed-strings 'EngineHudPresentation.CountDelivered(_readoutScratch)' "$HUD" \
  || fail "HUD lit count is not derived from engine telemetry rows"
rg -q --fixed-strings 'EngineHudPresentation.CountFailures(_readoutScratch)' "$HUD" \
  || fail "HUD failure count is not derived from engine telemetry rows"
rg -q --fixed-strings 'EngineHudPresentation.Classify(readout)' "$HUD" \
  || fail "HUD dot colors do not use the shared presentation classifier"
rg -q --fixed-strings 'EngineHudPresentation.DeliveredThrottle(' "$RENDERER" \
  || fail "renderer plume intensity still uses commanded vessel throttle"
rg -q --fixed-strings 'readout.FailureCode != null' "$PRESENTER" \
  || fail "failure semantics are not explicit"
rg -q --fixed-strings 'EngineLifecycleState.Ramp' "$PRESENTER" \
  || fail "startup lifecycle is not represented"

echo "engine_hud_semantics_contract_test: PASS"
