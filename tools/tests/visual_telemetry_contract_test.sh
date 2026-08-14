#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PART_GRAPH="$ROOT_DIR/ExosphereSimulation/Parts/PartGraph.cs"
VESSEL="$ROOT_DIR/ExosphereSimulation/Vessel.cs"
HUD="$ROOT_DIR/scripts/EngineGridHUD.cs"
RENDERER="$ROOT_DIR/scripts/VesselRenderer.cs"

fail() {
  echo "visual_telemetry_contract_test: FAIL: $*" >&2
  exit 1
}

for file in "$PART_GRAPH" "$VESSEL" "$HUD" "$RENDERER"; do
  [[ -f "$file" ]] || fail "missing $file"
done

rg -q --fixed-strings 'public void FillEngineReadouts(double ambientPressure, List<EngineReadout> destination)' "$PART_GRAPH" \
  || fail "PartGraph buffer fill API missing"
rg -q --fixed-strings 'Parts.FillEngineReadouts(GetAmbientPressure(body), destination)' "$VESSEL" \
  || fail "Vessel buffer fill wrapper missing"
rg -q --fixed-strings 'vessel.FillEngineReadouts(body, _readoutScratch);' "$HUD" \
  || fail "EngineGridHUD does not use the reusable telemetry buffer"
rg -q --fixed-strings 'TargetVessel.FillEngineReadouts(body, _engineReadoutScratch);' "$RENDERER" \
  || fail "VesselRenderer does not use the reusable telemetry buffer"
if rg -q --fixed-strings 'vessel.GetEngineReadouts(body)' "$HUD"; then
  fail "EngineGridHUD still enumerates compatibility telemetry"
fi
if rg -q --fixed-strings 'TargetVessel.GetEngineReadouts(body)' "$RENDERER"; then
  fail "VesselRenderer still enumerates compatibility telemetry"
fi
rg -q --fixed-strings 'TelemetryUpdatePeriodSeconds = 0.10' "$HUD" \
  || fail "HUD telemetry cadence guard missing"
rg -q --fixed-strings 'EngineVisualPeriodSeconds = 1.0 / 30.0' "$RENDERER" \
  || fail "renderer telemetry cadence guard missing"
rg -q --fixed-strings 'if (!Visible || TargetVessel == null) return;' "$RENDERER" \
  || fail "renderer must pause visual work when the exterior is hidden"

echo "visual_telemetry_contract_test: PASS (reused buffers, bounded cadences, hidden-renderer gate)"
