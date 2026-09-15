#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "visual_material_fill_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/steel.gdshader"
tile_shader="$ROOT/assets/shaders/heat_tile.gdshader"
renderer="$ROOT/scripts/VesselRenderer.cs"

rg -q 'var noseBlack = Mat\(new Color\(0\.001f, 0\.001f, 0\.001f\), 0\.0f, 0\.96f\)' "$renderer" \
  || fail "Starship nose is not assigned the dedicated opaque black material"
rg -q 'noseBlack\.Transparency = BaseMaterial3D\.TransparencyEnum\.Disabled' "$renderer" \
  || fail "Starship nose black material is not explicitly opaque"
rg -q 'BuildOgiveMesh\(noseLen, OgiveR\), noseBlack' "$renderer" \
  || fail "Starship ogive does not use the black nose material"

rg -q 'uniform float fill_strength' "$shader" \
  || fail "steel shader has no bounded presentation fill uniform"
rg -q 'uniform float fill_strength : hint_range\(0\.0, 0\.12\)' "$shader" \
  || fail "steel presentation fill upper bound changed without revalidation"
rg -q 'render_mode cull_back, diffuse_burley, specular_schlick_ggx;' "$shader" \
  || fail "steel shader is not using the PBR lighting path"
rg -q 'ALBEDO = col;' "$shader" \
  || fail "steel shader does not preserve the procedural surface as PBR albedo"
rg -q 'METALLIC = metallic_val;' "$shader" \
  || fail "steel shader does not expose metallic response to sky reflections"
rg -q 'ROUGHNESS = clamp' "$shader" \
  || fail "steel shader does not drive brushed roughness"
rg -q 'EMISSION = emission;' "$shader" \
  || fail "steel shader does not keep the bounded presentation fill"
rg -qF 'emission += emit_color * emit_strength;' "$shader" \
  || fail "thermal emission no longer layers on top of the baseline fill"
if rg -q 'render_mode.*unshaded' "$shader"; then
  fail "steel shader bypasses scene light, shadows, and reflections"
fi
if rg -q '^\s*ALPHA\s*=' "$shader"; then
  fail "opaque steel is routed through the transparent pipeline"
fi
rg -q 'emit_strength", glow \* glow \* 0\.28f' "$renderer" \
  || fail "peak-heating steel cue is not using the bounded thermal contrast scale"
rg -q 'SetShaderParameter\("fill_strength", 0\.038f\)' "$renderer" \
  || fail "renderer does not configure the steel fill strength"
[[ -f "$tile_shader" ]] || fail "heat-tile shader is missing"
rg -q 'uniform vec3  albedo_color' "$tile_shader" \
  || fail "TPS shader has no explicit baseline albedo"
rg -q 'vec3 lit = tile \*' "$tile_shader" \
  || fail "TPS shader does not seed the baseline fill"
rg -q 'ALBEDO = lit;' "$tile_shader" \
  || fail "TPS shader does not write its display-referred fill"
rg -q 'm\.SetShaderParameter\("albedo_color", TileBaseColor\)' "$renderer" \
  || fail "renderer does not configure the TPS baseline color"
rg -q 'm\.SetShaderParameter\("emit_strength", 0\.0f\)' "$renderer" \
  || fail "TPS material does not initialize thermal emission"

echo "visual_material_fill_contract_test: PASS (PBR steel reflections, bounded fill, thermal emission additive)"
