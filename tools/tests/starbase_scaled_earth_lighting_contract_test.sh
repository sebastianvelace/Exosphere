#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "starbase_scaled_earth_lighting_contract_test: FAIL: $*" >&2; exit 1; }
has() { rg -qF -- "$1" "$2" || fail "$3"; }
has_not() { rg -qF -- "$1" "$2" && fail "$3" || true; }

shader="$ROOT/assets/shaders/earth_surface.gdshader"
materials="$ROOT/scripts/PlanetMaterials.cs"
sun="$ROOT/scripts/SunController.cs"
[[ -f "$shader" ]] || fail "scaled Earth shader is missing"
[[ -f "$materials" ]] || fail "planet material factory is missing"
[[ -f "$sun" ]] || fail "sun controller is missing"

# World-space lighting: N is the rotated sphere normal, L is the visual sun
# (including the 28° pad override). Remapping L through PlanetOrientation⁻¹
# while N stayed local, or crossing local N with world V, produced either
# city-light night or a black ALPHA disc. Keep the frames matched.
has 'vec3 N = normalize(v_world_normal);' "$shader" \
  "scaled Earth lighting normal is not world-space"
has 'vec3 L = normalize(sun_dir);' "$shader" \
  "scaled Earth sun is not used in world space"
has 'vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);' "$shader" \
  "scaled Earth coverage is not the geometric camera silhouette"
has_not 'ToEarthSurfaceSunDirection' "$sun" \
  "Earth sun_dir is still remapped into a second texture-local frame"
has_not 'view_dir_local' "$shader" \
  "scaled Earth still has a constant coverage axis"
has '!IsInstanceValid(_earthMat)' "$sun" \
  "Earth material is not re-found after the first visual tick"
has '_earthMat?.SetShaderParameter("sun_dir", sunDir);' "$sun" \
  "Earth sun_dir is not the visual world-space sun"
has 'solar_visibility' "$shader" \
  "scaled Earth solar visibility gate is missing"
has 'night_floor : hint_range(0.0, 0.16)' "$shader" \
  "scaled Earth night floor is not bounded"
has 'night_floor", 0.12f' "$materials" \
  "scaled Earth night floor calibration is not explicit"

if [[ $# -eq 0 ]]; then
  echo "starbase_scaled_earth_lighting_contract_test: PASS (world-space solar lighting is bounded)"
  exit 0
fi
[[ $# -eq 3 ]] || fail "usage is [OUT_DIR LOG BASELINE_LOG] or no arguments"
out_dir="$1"
log="$2"
baseline="$3"
[[ -d "$out_dir" ]] || fail "capture directory is missing: $out_dir"
[[ -f "$log" ]] || fail "post-change telemetry log is missing: $log"
[[ -f "$baseline" ]] || fail "baseline telemetry log is missing: $baseline"

for km in 20 40; do
  slug="starbase_far_${km}km"
  image="$out_dir/exo_play_${slug}.png"
  [[ -s "$image" ]] || fail "missing scaled-Earth capture: $image"
  rg -q "^CAPTURE ${slug} " "$log" \
    || fail "post-change capture telemetry is missing: ${slug}"
  rg -q "^VISUAL_COMPOSITOR slug=${slug} .*earthGlobeAlpha=1[.]000 groundVisible=False" "$log" \
    || fail "${slug} does not prove scaled-Earth ownership"
done

read_metric() {
  local file="$1" slug="$2" key="$3"
  awk -v slug="$slug" -v key="$key" '
    $1 == "IMAGE" && $2 == ("slug=" slug) {
      for (i = 1; i <= NF; i++) {
        if ($i ~ ("^" key "=")) {
          split($i, p, "="); value = p[2] + 0
        }
      }
      found = 1
    }
    END { if (!found) exit 2; print value }
  ' "$file"
}

for km in 20 40; do
  slug="starbase_far_${km}km"
  after="$(read_metric "$log" "$slug" lowerMean)" \
    || fail "post-change lowerMean is missing: ${slug}"
  clipped="$(read_metric "$log" "$slug" surfaceWhiteClipFrac)" \
    || fail "post-change clipping metric is missing: ${slug}"
  awk -v after="$after" -v clipped="$clipped" '
    BEGIN {
      # Opaque dayside ocean/land must be brighter than city-light night (~0.07)
      # without blooming the surface.
      exit !(after >= 0.09 && clipped <= 0.02)
    }
  ' || fail "${slug} did not prove opaque dayside radiance without clipping"
done

echo "starbase_scaled_earth_lighting_contract_test: PASS (20–40 km opaque dayside)"
