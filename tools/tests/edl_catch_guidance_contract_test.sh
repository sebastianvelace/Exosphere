#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "edl_catch_guidance_contract_test: FAIL: $*" >&2; exit 1; }

EDL="$ROOT/scripts/EDLController.cs"
[[ -f "$EDL" ]] || fail "EDLController.cs missing"

rg -q 'bool finalCatchRelease = _phase == Edl\.Catch' "$EDL" \
  || fail "catch approach has no bounded final release gate"
rg -q 'heightToContact <= 1\.5' "$EDL" \
  || fail "final release is not limited to the last contact metre"
rg -q 'vDown < 0\.65' "$EDL" \
  || fail "final release is not speed-gated"
rg -q 'horizontalError <= 2\.0' "$EDL" \
  || fail "final release can occur while the vessel is laterally misaligned"
rg -q 'shipEngines\?\.SelectEngineCount\(0\)' "$EDL" \
  || fail "final catch release does not cut the selected engines"

echo "edl_catch_guidance_contract_test: PASS (bounded final catch release prevents hover plateau)"
