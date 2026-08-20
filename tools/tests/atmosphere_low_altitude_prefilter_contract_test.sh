#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "atmosphere_low_altitude_prefilter_contract_test: FAIL: $*" >&2; exit 1; }

SKY="$ROOT/scripts/SkyController.cs"
[[ -f "$SKY" ]] || fail "SkyController.cs missing"

rg -q 'float altitudePrefilter' "$SKY" \
  || fail "low-altitude cloud prefilter is not declared"
rg -q 'Smoothstep\(6_000\.0f, 45_000\.0f' "$SKY" \
  || fail "low-altitude cloud prefilter bounds changed unexpectedly"
rg -q 'altitudePrefilter\);' "$SKY" \
  || fail "low-altitude cloud prefilter is not fully applied"
rg -q 'Mathf\.Max\(' "$SKY" \
  || fail "solar prefilter is not combined conservatively"
SHADER="$ROOT/assets/shaders/space_sky.gdshader"
rg -q 'float cloud_detail_sample' "$SHADER" \
  || fail "high-frequency cloud detail has no shared prefilter path"
rg -q 'clamp\(cloud_weather_prefilter, 0\.0, 1\.0\)' "$SHADER" \
  || fail "cloud detail prefilter is not fully applied"
rg -q 'float cloud_weather_spherical_sample' "$SHADER" \
  || fail "low-altitude weather path is not using a spherical footprint"
rg -q 'normalize\(direction \+ tangent \* footprint\)' "$SHADER" \
  || fail "spherical weather footprint has no tangent sampling"
rg -q 'textureLod\(cloud_coverage_tex, detail_uv, 6\.0\)' "$SHADER" \
  || fail "low-altitude detail path is not using the coarser mip level"
rg -q 'textureLod\(cloud_coverage_tex,' "$SHADER" \
  || fail "low-altitude weather path has no filtered texture reads"
rg -q 'prefiltered = center \* 0\.08' "$SHADER" \
  || fail "low-altitude weather path retains too much aliased centre sampling"
rg -q 'float threshold_softness = mix\(0\.055, 0\.175' "$SHADER" \
  || fail "cloud coverage threshold is not softened at low altitude"
rg -q 'float dither_strength = mix\(0\.004, 0\.025' "$SHADER" \
  || fail "low-altitude cloud dither is not bounded"
IMPORT="$ROOT/assets/textures/earth_clouds.jpg.import"
[[ -f "$IMPORT" ]] || fail "Earth cloud import metadata missing"
rg -q '^mipmaps/generate=true$' "$IMPORT" \
  || fail "Earth cloud texture has no generated mipmaps for textureLod"

echo "atmosphere_low_altitude_prefilter_contract_test: PASS (bounded 10km horizon anti-alias path)"
