#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "starbase_local_terrain_visual_contract_test: FAIL: $*" >&2; exit 1; }
has() { rg -qF -- "$1" "$2" || fail "$3"; }

shader="$ROOT/assets/shaders/earth_ground.gdshader"
controller="$ROOT/scripts/EarthGroundController.cs"
fixture="$ROOT/tools/visual_playtest.sh"

[[ -f "$shader" ]] || fail "Earth ground shader is missing"
[[ -f "$controller" ]] || fail "Earth ground controller is missing"
[[ -f "$fixture" ]] || fail "visual fixture is missing"

# The measured DEM must affect both displacement and the manually-computed
# radiance normal. A height texture that only moves vertices is a flat decal.
has 'float sample_height_m(sampler2D height_tex' "$shader" \
  "DEM decode helper is missing"
has 'vec2 sample_height_slope(sampler2D height_tex' "$shader" \
  "DEM finite-difference slope is missing"
has 'textureLod(height_tex' "$shader" \
  "DEM slope does not sample the source raster"
has 'v_terrain_slope = clamp(terrain_slope' "$shader" \
  "DEM slope is not bounded at the provider edge"
has 'N = normalize(N' "$shader" \
  "terrain normal composition is missing"
has '- v_world_tangent_e * v_terrain_slope.x' "$shader" \
  "east DEM grade is not applied to the ground normal"
has '- v_world_tangent_n * v_terrain_slope.y' "$shader" \
  "north DEM grade is not applied to the ground normal"
has 'starbase_far_2km' "$fixture" \
  "2 km Starbase fixture is missing"
has 'starbase_far_40km' "$fixture" \
  "40 km Starbase fixture is missing"

# Optional artifact gate. This is intentionally separate from
# visual_playtest.sh so terrain work can be checked without owning or editing
# its temporary harness. Usage:
#   bash tools/tests/starbase_local_terrain_visual_contract_test.sh OUT LOG
if [[ $# -eq 0 ]]; then
  echo "starbase_local_terrain_visual_contract_test: PASS (DEM displacement and normals wired)"
  exit 0
fi
[[ $# -eq 2 ]] || fail "usage is [OUT_DIR LOG] or no arguments"
out_dir="$1"
log="$2"
[[ -d "$out_dir" ]] || fail "capture directory is missing: $out_dir"
[[ -f "$log" ]] || fail "telemetry log is missing: $log"

rg -q '^SUMMARY reason=STARBASE_FAR_OK ' "$log" \
  || fail "Starbase far-field run did not finish with its success summary"
rg -q '^VISUAL_STARBASE_FAR slug=starbase_far_2km source=OSM\+EarthGround ' "$log" \
  || fail "mapped EarthGround far-field source was not proven"
console="${log}.console"
if [[ -f "$console" ]]; then
  rg -q '^\[EARTH_GROUND_TERRAIN\] source=NAIP\+3DEP\+macro ready=True ' "$console" \
    || fail "runtime did not bind the NAIP/3DEP terrain stack"
fi

slugs=(2km 5km 8km 12km 20km 40km)
for km in "${slugs[@]}"; do
  slug="starbase_far_${km}"
  image="$out_dir/exo_play_${slug}.png"
  [[ -s "$image" ]] || fail "missing or empty capture: $image"
  rg -q "^CAPTURE ${slug} " "$log" \
    || fail "capture telemetry is missing: ${slug}"
  rg -q "^VISUAL_COMPOSITOR slug=${slug} " "$log" \
    || fail "compositor telemetry is missing: ${slug}"
  rg -q "^VISUAL_STARBASE_TERRAIN_TILE slug=${slug} source=USGS_3DEP .*edgeFade=radial .*built=True$" "$log" \
    || fail "source-derived 3DEP tile was not proven for ${slug}"
done

# The 2–12 km fixture is deliberately a site-oriented pullback: its camera is
# kept near the local ground so the pad/coast composition is legible. The
# 20–40 km cases are the actual camera-altitude globe handoff. Keep this
# distinction explicit so a passing run cannot claim that all six frames
# exercised the same altitude blend.
for km in 2 5 8 12; do
  rg -q "^VISUAL_COMPOSITOR slug=starbase_far_${km}km .*vesselAlt=${km}000[.]0 cameraAlt=([5-9][0-9][0-9]|1[0-4][0-9][0-9])[.]" "$log" \
    || fail "${km} km site-oriented fixture has no bounded local camera altitude"
done
for km in 20 40; do
  rg -q "^VISUAL_COMPOSITOR slug=starbase_far_${km}km .*vesselAlt=${km}000[.]0 cameraAlt=${km}[0-9]{3}[.].*earthGlobeAlpha=1[.]000 groundVisible=False" "$log" \
    || fail "${km} km fixture did not prove the scaled-space handoff at camera altitude"
done

for km in "${slugs[@]}"; do
  slug="starbase_far_${km}"
  size="$(stat -c '%s' "$out_dir/exo_play_${slug}.png")"
  (( size > 8000 )) || fail "capture is too small: ${slug}"
  awk -v slug="$slug" '
    $1 == "IMAGE" && $2 == ("slug=" slug) {
      for (i = 1; i <= NF; i++) {
        if ($i ~ /^width=/) { split($i, p, "="); width = p[2] + 0 }
        if ($i ~ /^height=/) { split($i, p, "="); height = p[2] + 0 }
        if ($i ~ /^mean=/) { split($i, p, "="); mean = p[2] + 0 }
      }
      found = 1
    }
    END { exit !(found && width >= 640 && height >= 360 && mean > 0.01) }
  ' "$log" || fail "image telemetry is empty, undersized, or black: ${slug}"
done
echo "starbase_local_terrain_visual_contract_test: image evidence is non-empty"

echo "starbase_local_terrain_visual_contract_test: PASS (DEM normals and 2–40 km evidence gate)"
