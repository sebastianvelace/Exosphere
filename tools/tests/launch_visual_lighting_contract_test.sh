#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "launch_visual_lighting_contract_test: FAIL: $*" >&2; exit 1; }

controller="$ROOT/scripts/LaunchPadController.cs"
harness="$ROOT/tools/visual_playtest.sh"

rg -q 'public int NightFloodlightCount => _nightFloodlights\.Count;' "$controller" \
  || fail "launch pad does not expose floodlight count telemetry"
rg -q 'public bool NightFloodlightsActive' "$controller" \
  || fail "launch pad does not expose floodlight state telemetry"
rg -q 'var fixtures = new \(Vector3 position, Vector3 target\)\[\]' "$controller" \
  || fail "night lights are not sector-targeted"
rg -q 'LightEnergy = 30f' "$controller" \
  || fail "night floodlight energy changed without a bounded contract"
rg -q 'SpotRange = 170f' "$controller" \
  || fail "night floodlight range changed without a bounded contract"
rg -q 'SpotAngle = 50f' "$controller" \
  || fail "night floodlight cone changed without a bounded contract"

fixture_count="$(grep -Ec '^[[:space:]]+\(new Vector3' "$controller")"
[[ "$fixture_count" == "4" ]] \
  || fail "expected exactly four shadow-casting fixtures, found $fixture_count"

rg -q 'LogLaunchComplexVisualTelemetry\(slug\)' "$harness" \
  || fail "visual harness does not record launch-complex evidence"
rg -q 'VISUAL_LAUNCH slug=\{slug\} present=True' "$harness" \
  || fail "launch telemetry does not prove complex presence"
rg -q 'delugeOutlets=\{delugeOutlets\} tankBodies=\{tankBodies\} ' "$harness" \
  || fail "launch telemetry omits structural readability counts"

echo "launch_visual_lighting_contract_test: PASS (four sector-targeted shadow pools and structural telemetry)"
