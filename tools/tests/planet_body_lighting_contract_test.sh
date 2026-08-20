#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "planet_body_lighting_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/planet_body.gdshader"
materials="$ROOT/scripts/PlanetMaterials.cs"

rg -q 'uniform float solar_visibility' "$shader" \
  || fail "generic planet shader has no solar visibility input"
rg -q 'uniform float day_gain' "$shader" \
  || fail "generic planet shader has no calibrated direct-light gain"
rg -q 'uniform float night_floor' "$shader" \
  || fail "generic planet shader has no bounded night floor"
rg -q 'uniform float night_floor[[:space:]]+: hint_range\(0\.0, 0\.05\)' "$shader" \
  || fail "night floor is not explicitly bounded"
rg -q 'const float ROCKY_ORBITAL_NIGHT_FLOOR = 0\.024;' "$shader" \
  || fail "rocky orbital night calibration is missing or changed"
rg -q 'float mode_night_floor = mode == 0 \? max\(night_floor, ROCKY_ORBITAL_NIGHT_FLOOR\) : night_floor;' "$shader" \
  || fail "night-floor calibration is not isolated to rocky bodies"
rg -q 'float direct = day \* day_gain \* solar_visibility;' "$shader" \
  || fail "direct body lighting is not gated by solar visibility"
rg -q 'col \*= mode_night_floor \+ direct;' "$shader" \
  || fail "body shader does not use the bounded night/direct split"
rg -q 'float rim_day = smoothstep\(-0\.3, 0\.4, ndl\) \* solar_visibility;' "$shader" \
  || fail "daylight rim is not gated by solar visibility"
rg -q 'dayGain: 0\.22f, nightFloor: 0\.004f' "$materials" \
  || fail "Venus day/night calibration is not explicit"
rg -q 'rim_day_response = mode == 2 \? 0\.55 : 0\.9;' "$shader" \
  || fail "Venus cloud-deck rim response is not bounded"
rg -q 'uniform float cloud_band_strength' "$shader" \
  || fail "Venus cloud-band strength is not explicit"
rg -q 'float band = 0\.5 \+ 0\.5 \* sin' "$shader" \
  || fail "Venus cloud deck has no latitude-streak response"
rg -q 'cloud_band_strength", 0\.55f' "$materials" \
  || fail "Venus cloud-band calibration is not wired"
rg -q 'dayGain: 0\.92f, nightFloor: 0\.012f' "$materials" \
  || fail "Mars day/night calibration is not explicit"

echo "planet_body_lighting_contract_test: PASS (planet shader solar split and Mars/Venus calibrations)"
