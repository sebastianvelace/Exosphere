#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "earth_ground_lighting_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/earth_ground.gdshader"
controller="$ROOT/scripts/EarthGroundController.cs"
launch_shader="$ROOT/assets/shaders/launch_surface.gdshader"
launch_pad="$ROOT/scripts/LaunchPadController.cs"

[[ -f "$launch_shader" ]] || fail "launch surface shader is missing"
rg -q 'v_world_position = \(MODEL_MATRIX \* vec4\(VERTEX, 1\.0\)\)\.xyz' "$launch_shader" \
  || fail "launch surface detail is not world-space stable"
rg -q 'float aggregate = noise01' "$launch_shader" \
  || fail "launch surface has no aggregate breakup"
rg -q 'float grain = noise01' "$launch_shader" \
  || fail "launch surface has no fine grain"
rg -q 'CreateLaunchSurfaceMaterial' "$launch_pad" \
  || fail "launch pad does not bind procedural surface materials"
rg -q 'ApplySurface\("OrbitalPadApron"' "$launch_pad" \
  || fail "starbase apron is not textured"

rg -q 'uniform float night_floor' "$shader" \
  || fail "Earth ground has no bounded indirect night floor"
rg -q 'uniform sampler2D night_tex' "$shader" \
  || fail "Earth ground has no real night-side texture input"
rg -q 'uniform float terrain_relief_strength' "$shader" \
  || fail "Earth ground has no bounded local relief control"
rg -q 'v_patch[[:space:]]*=[[:space:]]*VERTEX\.xz \* metres_per_unit' "$shader" \
  || fail "Earth ground detail coordinate depends on unreliable UV2 interpolation"
rg -q 'float macro = noise01' "$shader" \
  || fail "Earth ground has no multi-scale macro terrain breakup"
rg -q 'float regional = noise01' "$shader" \
  || fail "Earth ground has no regional terrain breakup"
rg -q 'float relief_scale = terrain_relief_strength \* land_mask' "$shader" \
  || fail "Earth ground relief is not restricted to land"
rg -q 'vec3 city_lights = night_map \* city_mask \* night_side' "$shader" \
  || fail "Earth ground city lights do not follow the night side"
rg -q 'uniform float terminator_width' "$shader" \
  || fail "Earth ground has no bounded terminator transition"
rg -q 'float terminator = smoothstep' "$shader" \
  || fail "Earth ground terminator transition is not explicit"
rg -q 'vec3 direct = vec3\(1\.05 \* ndl \* solar_visibility\)' "$shader" \
  || fail "Earth ground direct solar term is not visibility-gated"
rg -q 'render_mode cull_back, unshaded, depth_draw_opaque' "$shader" \
  || fail "Earth ground must own its manual radiance"
rg -q 'earthshine_gain' "$shader" \
  || fail "Earth ground earthshine gain is missing"
rg -q 'earthshine_min_reflectance' "$shader" \
  || fail "Earth ground minimum earthshine reflectance is missing"
rg -q 'ground_radiance = lit \+ indirect_emission' "$shader" \
  || fail "Earth ground earthshine is not composed into radiance"
rg -q 'night_floor", NightFloor' "$controller" \
  || fail "Earth ground calibration is not configured from C#"
rg -q 'private const float NightFloor = 0\.12f;' "$controller" \
  || fail "Earth ground floor is not at the validated bounded ceiling"
rg -q 'private const float EarthshineGain = 2\.80f;' "$controller" \
  || fail "Earth ground earthshine gain is not configured"
rg -q 'private const float EarthshineMinReflectance = 0\.055f;' "$controller" \
  || fail "Earth ground minimum reflectance is not configured"
rg -q 'private const float DetailStrength = 0\.18f;' "$controller" \
  || fail "Earth ground detail strength is not configured"
rg -q 'private const float TerrainReliefStrength = 0\.18f;' "$controller" \
  || fail "Earth ground relief strength is not configured"
rg -q 'private const float NightCityGain = 0\.34f;' "$controller" \
  || fail "Earth ground city-light gain is not configured"
rg -q 'earth_night\.jpg' "$controller" \
  || fail "Earth ground does not bind the night texture"
rg -q 'private const float CoastalGrade = 0\.28f;' "$controller" \
  || fail "Earth ground coastal grade is not configured"
rg -q 'private const float HorizonHazeStrength = 0\.92f;' "$controller" \
  || fail "Earth ground horizon haze is too broad or unconfigured"
rg -q 'HorizonHazeStrength' "$controller" \
  || fail "Earth ground horizon seam mitigation is not bounded/configured"

rg -q 'GetSurfacePoint\(vessel.Position, 0.0\)' "$controller" \
  || fail "Earth ground is not anchored to the live ellipsoid surface"
rg -q 'GetGeodeticUp\(vessel.Position\)' "$controller" \
  || fail "Earth ground does not use geodetic up"
rg -q 'EarthGlobeAlpha' "$controller" \
  || fail "Earth ground fade is not complementary with the scaled-space globe"
rg -q 'EarthVisualHandoffLowM = 18_000.0' "$ROOT/scripts/FloatingOrigin.cs" \
  || fail "Earth visual handoff low altitude changed"
rg -q 'EarthVisualHandoffHighM = 42_000.0' "$ROOT/scripts/FloatingOrigin.cs" \
  || fail "Earth visual handoff high altitude changed"
rg -q 'VisualSurfaceRadiusMetres' "$ROOT/scripts/FloatingOrigin.cs" \
  || fail "scaled-space Earth does not read live surface radius"
rg -q 'ApplySurface\("StarbaseWetlandSkirt"' "$launch_pad" \
  || fail "starbase wetland skirt is not textured"
rg -q 'float site_core = 1.0 - smoothstep' "$shader" \
  || fail "Earth ground does not reconstruct land out to the play-camera horizon"
rg -q 'float coastal_belt = 1.0 - smoothstep' "$shader" \
  || fail "Earth ground has no coastal belt beyond the pad island"
rg -q '22000f \* U' "$launch_pad" \
  || fail "starbase wetland skirt is still the old 1.6 km island"
rg -q 'uniform float edge_fade' "$launch_shader" \
  || fail "launch surface has no skirt edge fade"
rg -q 'edgeFade: 1f' "$launch_pad" \
  || fail "starbase wetland skirt does not fade into the planetary patch"

echo "earth_ground_lighting_contract_test: PASS (bounded night floor, ellipsoid anchor, complementary globe handoff)"
