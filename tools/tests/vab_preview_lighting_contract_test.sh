#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "vab_preview_lighting_contract_test: FAIL: $*" >&2; exit 1; }

controller="$ROOT/scripts/ConstructionController.cs"

rg -q 'Name = "PreviewLight"' "$controller" \
  || fail "VAB preview has no key light"
rg -q 'Name = "PreviewFill"' "$controller" \
  || fail "VAB preview has no dedicated fill light"
rg -q 'LightEnergy = 0\.62f' "$controller" \
  || fail "VAB fill energy changed without an explicit bounded contract"
rg -q 'ShadowEnabled = false' "$controller" \
  || fail "VAB fill light should not add a second shadow map"

echo "vab_preview_lighting_contract_test: PASS (studio key/fill lighting is VAB-local and bounded)"
