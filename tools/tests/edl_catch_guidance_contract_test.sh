#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "edl_catch_guidance_contract_test: FAIL: $*" >&2; exit 1; }

EDL="$ROOT/scripts/EDLController.cs"
[[ -f "$EDL" ]] || fail "EDLController.cs missing"

rg -q 'TowerCatchCutoff\.Decide\(pinGeometry' "$EDL" \
  || fail "catch cutoff must go through the predicted-impact gate"
rg -q 'if \(cutoff\.Commit\)' "$EDL" \
  || fail "catch cutoff must honour the gate decision"
rg -q '_catchCutoffCommitted = true' "$EDL" \
  && rg -q 'Edl\.Catch && _catchCutoffCommitted' "$EDL" \
  || fail "catch cutoff must remain committed while pins settle"
rg -q 'shipEngines\?\.SelectEngineCount\(0\)' "$EDL" \
  || fail "final catch release does not cut the selected engines"

echo "edl_catch_guidance_contract_test: PASS (predicted-impact gate and committed cutoff)"
