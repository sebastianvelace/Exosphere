#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "vab_preview_lighting_contract_test: FAIL: $*" >&2; exit 1; }

controller="$ROOT/scripts/ConstructionController.cs"
floor_shader="$ROOT/assets/shaders/vab_floor.gdshader"

rg -q 'Name = "PreviewLight"' "$controller" \
  || fail "VAB preview has no key light"
rg -q 'Name = "PreviewFill"' "$controller" \
  || fail "VAB preview has no dedicated fill light"
rg -q 'LightEnergy = 0\.62f' "$controller" \
  || fail "VAB fill energy changed without an explicit bounded contract"
rg -q 'ShadowEnabled = false' "$controller" \
  || fail "VAB fill light should not add a second shadow map"
[[ -f "$floor_shader" ]] \
  || fail "VAB preview floor shader is missing"
rg -q 'Name = "PreviewFloor"' "$controller" \
  || fail "VAB preview has no dedicated floor node"
rg -q 'new PlaneMesh' "$controller" \
  || fail "VAB preview floor is not mesh-backed"
rg -q 'vab_floor\.gdshader' "$controller" \
  || fail "VAB preview floor does not bind its isolated material"
rg -q 'float seam = 1\.0 - smoothstep' "$floor_shader" \
  || fail "VAB floor has no panel seam breakup"
rg -q 'float grain = noise01' "$floor_shader" \
  || fail "VAB floor has no fine aggregate detail"
rg -q 'Position = new Vector3\(0f, box\.Position\.Y - 0\.08f, 0f\)' "$controller" \
  || fail "VAB floor is not anchored to rendered bounds"

echo "vab_preview_lighting_contract_test: PASS (bounded studio lights and anchored procedural floor)"
