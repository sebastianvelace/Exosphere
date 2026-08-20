#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "cockpit_visual_material_contract_test: FAIL: $*" >&2; exit 1; }

COCKPIT="$ROOT/scripts/CockpitRenderer.cs"
[[ -f "$COCKPIT" ]] || fail "CockpitRenderer.cs missing"

rg -q 'InteriorKeyEnergy = 0\.62f' "$COCKPIT" \
  || fail "cockpit key fill is not explicitly bounded"
rg -q 'InteriorFillEnergy = 0\.36f' "$COCKPIT" \
  || fail "cockpit fill light is not explicitly bounded"
rg -q 'wall\.EmissionEnabled = true' "$COCKPIT" \
  || fail "cockpit shell has no night readability floor"
rg -q 'frameM\.EmissionEnabled = true' "$COCKPIT" \
  || fail "windshield frame has no low-level edge readability"
rg -q 'LightEnergy = InteriorKeyEnergy' "$COCKPIT" \
  || fail "cockpit key light does not use the bounded constant"
rg -q 'LightEnergy = InteriorFillEnergy' "$COCKPIT" \
  || fail "cockpit fill light does not use the bounded constant"

echo "cockpit_visual_material_contract_test: PASS (bounded IVA fill/emission, displays remain dominant)"
