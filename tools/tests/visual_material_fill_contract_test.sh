#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "visual_material_fill_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/steel.gdshader"
renderer="$ROOT/scripts/VesselRenderer.cs"

rg -q 'uniform float fill_strength' "$shader" \
  || fail "steel shader has no bounded presentation fill uniform"
rg -q 'uniform float fill_strength : hint_range\(0\.0, 0\.12\)' "$shader" \
  || fail "steel presentation fill upper bound changed without revalidation"
rg -q 'EMISSION = base_tint \* fill_strength;' "$shader" \
  || fail "steel shader does not seed the baseline fill"
rg -q 'EMISSION \+= emit_color \* emit_strength;' "$shader" \
  || fail "thermal emission no longer layers on top of the baseline fill"
rg -q 'emit_strength", glow \* glow \* 0\.28f' "$renderer" \
  || fail "peak-heating steel cue is not using the bounded thermal contrast scale"
rg -q 'SetShaderParameter\("fill_strength", 0\.12f\)' "$renderer" \
  || fail "renderer does not configure the steel fill strength"
rg -q 'EmissionEnabled  = true' "$renderer" \
  || fail "TPS material does not enable its baseline fill"
rg -q 'tileMat\.EmissionEnabled = true;' "$renderer" \
  || fail "thermal update can still disable the TPS baseline fill"
if rg -q 'mat\.EmissionEnabled = glow;' "$renderer"; then
  fail "cold TPS path still disables its baseline readability fill"
fi
rg -q 'mat\.EmissionEnergyMultiplier = 1\.0f;' "$renderer" \
  || fail "cold TPS path has no explicit neutral emission multiplier"
rg -q 'new Color\(0\.050f, 0\.050f, 0\.060f\)' "$renderer" \
  || fail "TPS baseline fill color is not explicit"

echo "visual_material_fill_contract_test: PASS (bounded steel/TPS shadow fill, thermal emission additive)"
