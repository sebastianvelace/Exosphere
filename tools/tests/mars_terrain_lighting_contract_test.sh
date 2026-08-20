#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "mars_terrain_lighting_contract_test: FAIL: $*" >&2; exit 1; }

shader="$ROOT/assets/shaders/mars_terrain.gdshader"
[[ -f "$shader" ]] || fail "Mars terrain shader is missing"

rg -q 'uniform float solar_visibility' "$shader" \
  || fail "terrain shader has no solar visibility input"
rg -q 'uniform float night_floor' "$shader" \
  || fail "terrain shader has no configurable night floor"
rg -q 'uniform vec3 night_tint' "$shader" \
  || fail "terrain shader has no nightside tint"
rg -q 'uniform float rim_strength' "$shader" \
  || fail "terrain shader has no bounded rim strength"
rg -q 'uniform float rim_power' "$shader" \
  || fail "terrain shader has no bounded rim falloff"
rg -q 'const float MIN_READABLE_FLOOR = 0\.012' "$shader" \
  || fail "terrain shader lacks the minimum readable floor"
rg -q 'float floor_term = max\(night_floor, MIN_READABLE_FLOOR\)' "$shader" \
  || fail "night floor is not clamped to a finite lower bound"
rg -q 'float solar_term = day \* day_gain \* solar_visibility' "$shader" \
  || fail "direct terrain response is not gated by solar visibility"
rg -q 'float grazing = pow\(1\.0 - abs\(dot\(N, V\)\), rim_power\)' "$shader" \
  || fail "terrain rim is not view-angle based"
rg -q 'vec3 rim = night_tint \* grazing \* rim_strength' "$shader" \
  || fail "terrain rim is not bounded by rim_strength"
rg -q 'PatchSize = 120_000f' "$ROOT/scripts/MarsTerrainController.cs" \
  || fail "Mars low-altitude patch is too small to reach the geometric horizon"
rg -q 'Frequency = 0\.00008f' "$ROOT/scripts/MarsTerrainController.cs" \
  || fail "Mars terrain lacks broad-scale relief"
rg -q 'noise\.GetNoise2D\(x, z\) \* 520f' "$ROOT/scripts/MarsTerrainController.cs" \
  || fail "Mars low-altitude relief amplitude was not raised for the verified horizon"
rg -q 'terrain_fbm\(v_local_position\.xz \* 0\.00018\)' "$shader" \
  || fail "Mars terrain lacks world-locked macro albedo breakup"

echo "mars_terrain_lighting_contract_test: PASS (bounded night floor, solar gate, and terrain rim)"
