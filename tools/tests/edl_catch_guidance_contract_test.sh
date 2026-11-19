#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "edl_catch_guidance_contract_test: FAIL: $*" >&2; exit 1; }

EDL="$ROOT/scripts/EDLController.cs"
[[ -f "$EDL" ]] || fail "EDLController.cs missing"

rg -q 'Edl\.Catch && pinGeometry\.BothPinsFit && pinGeometry\.LastPinGapM <= 0\.25' "$EDL" \
  || fail "catch cutoff must require both pin footprints and bounded clearance"
rg -q 'Abs\(verticalSpeed\) <= 0\.65 && lateralSpeed <= 0\.35' "$EDL" \
  || fail "catch cutoff must be bounded by cradle-relative velocity"
rg -q '_catchCutoffCommitted = true' "$EDL" \
  && rg -q 'Edl\.Catch && _catchCutoffCommitted' "$EDL" \
  || fail "catch cutoff must remain committed while pins settle"
rg -q 'shipEngines\?\.SelectEngineCount\(0\)' "$EDL" \
  || fail "final catch release does not cut the selected engines"

echo "edl_catch_guidance_contract_test: PASS (two-pin geometry, bounded speed and committed cutoff)"
