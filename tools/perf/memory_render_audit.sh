#!/usr/bin/env bash
set -euo pipefail

# Read-only static memory/render audit for Exosphere.
#
# This script never imports, launches, edits, or deletes project files. It reports:
#   * source image dimensions and conservative RGBA8 decoded/mipmap estimates;
#   * sizes of the existing Godot import cache when present;
#   * scene/resource/shader/subviewport census;
#   * deterministic planet mesh and particle-budget arithmetic from source;
#   * static allocation and render-update signals for follow-up profiling.
#
# It deliberately does not present decoded estimates as measured GPU allocation. A real
# renderer capture is required to account for driver compression, staging copies, depth,
# MSAA, transient render targets, and duplicated resources.

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if ! command -v file >/dev/null 2>&1; then
  echo "memory_render_audit: FAIL (required command 'file' is unavailable)" >&2
  exit 1
fi

human_bytes() {
  awk -v bytes="$1" 'BEGIN {
    if (bytes >= 1073741824) printf "%.2f GiB", bytes / 1073741824;
    else if (bytes >= 1048576) printf "%.2f MiB", bytes / 1048576;
    else if (bytes >= 1024) printf "%.2f KiB", bytes / 1024;
    else printf "%d B", bytes;
  }'
}

sum_files() {
  find "$@" -type f -printf '%s\n' 2>/dev/null | awk '{sum += $1} END {print sum + 0}'
}

count_rg() {
  local pattern="$1"
  shift
  { rg -n "$pattern" "$@" || true; } | wc -l | tr -d ' '
}

echo "memory_render_audit version=1"
echo "root=$ROOT"
echo "timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo "mode=read-only-static"
echo

echo "== source images: conservative decoded estimates =="
total_source=0
total_decoded=0
total_mipped=0
total_imported=0
image_count=0

printf '%-38s %10s %10s %12s %12s %12s\n' \
  "asset" "dimensions" "source" "RGBA8" "RGBA8+mip" "imported"

while IFS= read -r asset; do
  meta="$(file "$asset")"
  dimensions="$(printf '%s\n' "$meta" | sed -nE 's/.* ([0-9]+)x([0-9]+).*/\1x\2/p')"
  if [[ -z "$dimensions" ]]; then
    dimensions="$(printf '%s\n' "$meta" | sed -nE 's/.* ([0-9]+) x ([0-9]+).*/\1x\2/p')"
  fi
  if [[ -z "$dimensions" ]]; then
    echo "SKIP asset=$asset reason=dimensions-not-detected"
    continue
  fi

  width="${dimensions%x*}"
  height="${dimensions#*x}"
  source_bytes="$(stat -c '%s' "$asset")"
  decoded_bytes=$((width * height * 4))
  mipped_bytes=$(((decoded_bytes * 4 + 2) / 3))
  base="$(basename "$asset")"
  imported_bytes="$(find .godot/imported -maxdepth 1 -type f -name "$base-*" -name '*.ctex' -printf '%s\n' 2>/dev/null | awk '{sum += $1} END {print sum + 0}')"

  printf '%-38s %10s %10s %12s %12s %12s\n' \
    "$asset" "$dimensions" "$(human_bytes "$source_bytes")" \
    "$(human_bytes "$decoded_bytes")" "$(human_bytes "$mipped_bytes")" \
    "$(human_bytes "$imported_bytes")"

  total_source=$((total_source + source_bytes))
  total_decoded=$((total_decoded + decoded_bytes))
  total_mipped=$((total_mipped + mipped_bytes))
  total_imported=$((total_imported + imported_bytes))
  image_count=$((image_count + 1))
done < <(find assets/textures -maxdepth 1 -type f \( \
  -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.png' -o -iname '*.webp' -o \
  -iname '*.exr' -o -iname '*.hdr' \) -print | sort)

echo "image_count=$image_count"
echo "source_bytes=$total_source ($(human_bytes "$total_source"))"
echo "decoded_rgba8_bytes=$total_decoded ($(human_bytes "$total_decoded"))"
echo "decoded_rgba8_with_mip_upper_bound_bytes=$total_mipped ($(human_bytes "$total_mipped"))"
echo "imported_ctex_bytes=$total_imported ($(human_bytes "$total_imported"))"
echo "decoded_estimate_scope=RGBA8 upper bound; RGB source may be stored in a compressed GPU format"
echo

echo "== import settings and cache =="
echo "import_files=$(find assets -type f -name '*.import' | wc -l | tr -d ' ')"
echo "imported_cache_files=$(find .godot/imported -maxdepth 1 -type f 2>/dev/null | wc -l | tr -d ' ')"
echo "imported_cache_bytes=$(sum_files .godot/imported) ($(human_bytes "$(sum_files .godot/imported)"))"
echo "texture_import_vram_false=$(rg -l '"vram_texture": false' assets/textures --glob '*.import' | wc -l | tr -d ' ')"
echo "texture_import_mipmaps_disabled=$(rg -l 'mipmaps/generate=false' assets/textures --glob '*.import' | wc -l | tr -d ' ')"
echo "texture_import_size_limit_unbounded=$(rg -l 'process/size_limit=0' assets/textures --glob '*.import' | wc -l | tr -d ' ')"
echo

echo "== scene/resource census =="
echo "scene_files=$(find scenes -type f \( -name '*.tscn' -o -name '*.scn' \) | wc -l | tr -d ' ')"
echo "scene_nodes=$(count_rg '^\[node ' scenes --glob '*.tscn' --glob '*.scn')"
echo "scene_subresources=$(count_rg '^\[sub_resource ' scenes --glob '*.tscn' --glob '*.scn')"
echo "scene_ext_resources=$(count_rg '^\[ext_resource ' scenes --glob '*.tscn' --glob '*.scn')"
echo "flight_scene_nodes=$(count_rg '^\[node ' scenes/flight/Flight.tscn)"
echo "flight_scene_subresources=$(count_rg '^\[sub_resource ' scenes/flight/Flight.tscn)"
echo

echo "== render targets and camera/shadow settings =="
echo "subviewport_new_expressions=$(count_rg 'new SubViewport' scripts --glob '*.cs')"
echo "subviewport_array_slots=$(sed -nE 's/.*SubViewport\[\] _vp.*new SubViewport\[([0-9]+)\].*/\1/p' scripts/CockpitInstruments.cs | head -1)"
echo "subviewport_runtime_instances_estimate=4 (3 cockpit + 1 construction)"
echo "subviewport_always_update_assignment_sites=$(count_rg 'RenderTargetUpdateMode = SubViewport.UpdateMode.Always' scripts --glob '*.cs')"
echo "subviewport_always_update_runtime_instances_estimate=4"
echo "subviewport_512x512_definition_sites=$(count_rg 'Size = new Vector2I\(512, 512\)' scripts --glob '*.cs')"
echo "subviewport_512x512_runtime_instances_estimate=3"
echo "subviewport_1024x1024_definition_sites=$(count_rg 'Size = new Vector2I\(1024, 1024\)' scripts --glob '*.cs')"
echo "subviewport_1024x1024_runtime_instances_estimate=1"
echo "flight_directional_shadow_mode=$(sed -nE 's/^directional_shadow_mode = (.*)$/\1/p' scenes/flight/Flight.tscn | head -1)"
echo "flight_scene_camera_far=$(sed -nE 's/^far = (.*)$/\1/p' scenes/flight/Flight.tscn | head -1)"
echo "flight_runtime_camera_far=$(sed -nE 's/.*Far[[:space:]]*= *([0-9_]+).*/\1/p' scripts/SimulationBridge.cs | head -1)"
echo

echo "== deterministic mesh/particle arithmetic =="
planet_bodies="$(find data/bodies -maxdepth 1 -type f -name '*.json' ! -name 'sun.json' | wc -l | tr -d ' ')"
radial_segments="$(sed -nE 's/.*RadialSegments = ([0-9]+),/\1/p' scripts/SimulationBridge.cs | head -1)"
rings="$(sed -nE 's/.*Rings = ([0-9]+),/\1/p' scripts/SimulationBridge.cs | head -1)"
if [[ -n "$radial_segments" && -n "$rings" ]]; then
  sphere_triangles=$((radial_segments * (rings - 1) * 2))
  all_planet_triangles=$((sphere_triangles * planet_bodies))
  echo "non_sun_body_json_count=$planet_bodies"
  echo "planet_sphere_radial_segments=$radial_segments"
  echo "planet_sphere_rings=$rings"
  echo "planet_triangles_per_sphere=$sphere_triangles"
  echo "planet_triangles_all_non_sun=$all_planet_triangles"
fi
ring_segments="$(sed -nE 's/.*BuildRingMesh\(1\.20f, 2\.30f, ([0-9]+)\).*/\1/p' scripts/SimulationBridge.cs | head -1)"
if [[ -n "$ring_segments" ]]; then
  echo "saturn_ring_segments=$ring_segments"
  echo "saturn_ring_triangles=$((ring_segments * 2))"
fi
echo "starfield_points=$(sed -nE 's/.*StarCount[[:space:]]*= *([0-9]+).*/\1/p' scripts/StarfieldController.cs | head -1)"
echo "gpu_particle_amount_lines=$(count_rg 'Amount[[:space:]]*=' scripts --glob '*.cs')"
echo "gpu_particle_declared_amount_sum=$( { rg -o 'Amount[[:space:]]*=[[:space:]]*[0-9]+' scripts --glob '*.cs' || true; } | sed -E 's/.*= *//' | awk '{sum += $1} END {print sum + 0}')"
echo

echo "== shader/static-cost signals =="
echo "shader_files=$(find assets/shaders -maxdepth 1 -type f -name '*.gdshader' | wc -l | tr -d ' ')"
echo "shader_bytes=$(find assets/shaders -maxdepth 1 -type f -name '*.gdshader' -printf '%s\n' | awk '{sum += $1} END {print sum + 0}')"
echo "shader_texture_uniforms=$(count_rg 'uniform sampler' assets/shaders --glob '*.gdshader')"
echo "shader_for_loops=$(count_rg 'for[[:space:]]*\(' assets/shaders --glob '*.gdshader')"
echo "space_sky_lines=$(wc -l < assets/shaders/space_sky.gdshader | tr -d ' ')"
echo "space_sky_loop_lines=$(count_rg 'for[[:space:]]*\(' assets/shaders/space_sky.gdshader)"
echo "earth_surface_texture_samples=$(count_rg 'texture\(' assets/shaders/earth_surface.gdshader)"
echo "planet_body_texture_samples=$(count_rg 'texture\(' assets/shaders/planet_body.gdshader)"
echo

echo "== static managed-allocation signals in visible paths =="
for path in scripts/VesselRenderer.cs scripts/SimulationBridge.cs scripts/SunController.cs \
  scripts/SystemsController.cs scripts/CockpitInstruments.cs scripts/StarfieldController.cs; do
  echo "file=$path"
  printf '  process_methods='; count_rg 'override void _Process|override void _PhysicsProcess' "$path"; echo
  printf '  to_array='; count_rg 'ToArray\(' "$path"; echo
  printf '  to_list='; count_rg 'ToList\(' "$path"; echo
  printf '  order_by='; count_rg 'OrderBy\(' "$path"; echo
  printf '  select_many='; count_rg 'SelectMany\(' "$path"; echo
  printf '  new_dictionary='; count_rg 'new Dictionary' "$path"; echo
  printf '  new_list='; count_rg 'new List' "$path"; echo
done
echo

echo "== interpretation guardrails =="
echo "measured=source sizes, import-cache sizes, static counts, and arithmetic derived from checked-in files"
echo "estimated=decoded RGBA8/mipmap bytes, VRAM residency, render-pass cost, and per-frame allocation bytes"
echo "not_measured=GPU counters, driver residency, draw calls, overdraw, frame p95/p99, and managed allocation rate"
echo "next_gate=run renderer-backed Xvfb capture with Godot profiler plus process RSS/VRAM evidence before runtime changes"
