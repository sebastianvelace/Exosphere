#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HUD="$ROOT_DIR/scripts/EngineGridHUD.cs"

fail() {
  echo "engine_hud_visual_semantics_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$HUD" ]] || fail "missing EngineGridHUD.cs"

# The centre ratio is delivered chamber pressure, not the vessel command.  A
# command can be 100% while engines are still in Chill/SpinPrime/Ignition.
rg -q --fixed-strings 'EngineHudPresentation.CountDelivered(_readoutScratch)' "$HUD" \
  || fail "centre count is not based on delivered engine telemetry"
rg -q --fixed-strings 'string centre = $"{_litEngines}/{_nominalEngines}";' "$HUD" \
  || fail "centre ratio presentation is missing"

# Keep the visual vocabulary unambiguous: off=track, startup=warning,
# failure=alert.  Red must never be the default/off state.
rg -q --fixed-strings 'private static readonly Color DotOff      = InterfaceTheme.Track;' "$HUD" \
  || fail "off engines are not mapped to the neutral track color"
rg -q --fixed-strings 'indicator == EngineHudIndicatorState.Failed' "$HUD" \
  || fail "failed engine state is not classified before color selection"
rg -q --fixed-strings '? InterfaceTheme.Alert' "$HUD" \
  || fail "failure state is not mapped to alert red"
rg -q --fixed-strings '? InterfaceTheme.Warning' "$HUD" \
  || fail "startup state is not mapped to warning yellow"
rg -q --fixed-strings 'EngineHudPresentation.CountDelivered(_engineReadoutScratch)' \
  "$ROOT_DIR/ExosphereSimulation/Presentation/FlightHudPresenter.cs" \
  || fail "attitude strip snapshot is not based on delivered engine telemetry"
rg -q --fixed-strings 'engineTelemetry.NominalEngineCount' \
  "$ROOT_DIR/ExosphereSimulation/Presentation/FlightHudPresenter.cs" \
  || fail "attitude strip snapshot loses declared aggregate engine count"
rg -q --fixed-strings 'LogEngineVisualTelemetry(slug)' \
  "$ROOT_DIR/tools/visual_playtest.sh" \
  || fail "visual harness does not record engine-state evidence"
rg -q --fixed-strings 'VISUAL_ENGINES slug={slug} commandThrottle=' \
  "$ROOT_DIR/tools/visual_playtest.sh" \
  || fail "engine-state telemetry omits command/delivered distinction"

echo "engine_hud_visual_semantics_contract_test: PASS"
