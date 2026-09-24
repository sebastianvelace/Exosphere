#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "visual_material_fill_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/steel.gdshader"
tile_shader="$ROOT/assets/shaders/heat_tile.gdshader"
renderer="$ROOT/scripts/VesselRenderer.cs"

rg -q 'var noseBlack = TileMat\(rimStrength: 0\.035f, tileScale: 13\.0f\)' "$renderer" \
  || fail "Starship nose is not assigned the dedicated black TPS material"
rg -q 'RegisterTileMat\(TileCharZone\.Nose, noseBlack\)' "$renderer" \
  || fail "Starship nose TPS does not participate in thermal presentation"
rg -q 'BuildOgiveSectorMesh\(noseLen, OgiveR,' "$renderer" \
  || fail "Starship ogive is not split into bounded material sectors"
rg -q 'AddMesh\("NoseTPS",' "$renderer" \
  || fail "Starship nose has no dedicated windward TPS sector"
rg -q 'AddMesh\("NoseSteel",' "$renderer" \
  || fail "Starship nose has no leeward stainless-steel sector"
rg -q 'Mathf\.Tau - noseTpsArc' "$renderer" \
  || fail "Starship nose steel sector does not complement the TPS coverage"

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
rg -q 'render_mode cull_back, diffuse_burley, specular_schlick_ggx;' "$tile_shader" \
  || fail "TPS shader is not using the opaque PBR lighting path"
rg -q 'float filter_width = max\(fwidth\(edge_distance\)' "$tile_shader" \
  || fail "TPS tile joints are not derivative-filtered"
rg -q 'ALBEDO = tile;' "$tile_shader" \
  || fail "TPS shader does not preserve tile color as PBR albedo"
rg -q 'ROUGHNESS = roughness_val;' "$tile_shader" \
  || fail "TPS shader does not expose its matte roughness"
rg -q 'EMISSION = emission;' "$tile_shader" \
  || fail "TPS shader does not layer bounded thermal emission"
if rg -q 'render_mode.*unshaded|^\s*ALPHA\s*=' "$tile_shader"; then
  fail "TPS shader bypasses lighting or enters the transparent pipeline"
fi
rg -q 'm\.SetShaderParameter\("albedo_color", TileBaseColor\)' "$renderer" \
  || fail "renderer does not configure the TPS baseline color"
rg -q 'm\.SetShaderParameter\("emit_strength", 0\.0f\)' "$renderer" \
  || fail "TPS material does not initialize thermal emission"

rg -q 'BuildCylindricalSectorMesh' "$renderer" \
  || fail "Starship TPS still lacks a continuous curved body surface"
rg -Fq 'surface.SetNormal(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)))' "$renderer" \
  || fail "Starship TPS sector does not carry explicit outward normals"
rg -Fq 'surface.AddIndex(i11);' "$renderer" \
  || fail "Starship TPS sector lacks exterior-facing triangle winding"
rg -q 'BuildStarshipFlapMesh' "$renderer" \
  || fail "Starship flaps still lack a tapered aerodynamic mesh"
rg -q 'AddFlap\("FwdFlapL".*2\.05f, 0\.91f' "$renderer" \
  && rg -q 'fwdFlapTiles, shipSteel, tipSpanFraction: 0\.56f' "$renderer" \
  || fail "Starship forward flaps are not using bounded image-derived proportions"
rg -q 'AddFlap\("AftFlapL".*3\.95f, 1\.32f' "$renderer" \
  && rg -q 'aftFlapTiles, shipSteel, tipSpanFraction: 0\.60f' "$renderer" \
  || fail "Starship aft flaps are not using bounded image-derived proportions"
rg -q 'BuildStarshipFlapRootFairingMesh' "$renderer" \
  && rg -q 'BuildExtrudedPlanformMesh' "$renderer" \
  || fail "Starship flap roots and blades are not using bounded extruded planforms"
rg -q 'splitTopSurface: true' "$renderer" \
  && rg -q 'blade\.SetSurfaceOverrideMaterial\(0, rootMat\)' "$renderer" \
  && rg -q 'blade\.SetSurfaceOverrideMaterial\(1, bladeMat\)' "$renderer" \
  || fail "Starship flaps do not separate leeward steel from windward TPS"
rg -q 'StarshipEngineExitGroup' "$renderer" \
  && rg -q 'CapBottom = false' "$renderer" \
  || fail "Starship aft engine bay is not open and instrumentable"
if rg -q 'name \+ "Root".*BoxMesh' "$renderer"; then
  fail "Starship flap root regressed to a rectangular box"
fi
if rg -q 'TileSeam' "$renderer"; then
  fail "Starship flap still carries box seam overlays over the filtered TPS pattern"
fi
if rg -q 'ShipBarrelWeld' "$renderer"; then
  fail "Starship still draws metal weld rings over the windward TPS surface"
fi

echo "visual_material_fill_contract_test: PASS (opaque PBR steel/TPS, filtered joints, curved shield and tapered flaps)"
