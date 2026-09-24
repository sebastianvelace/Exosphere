#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "cape_canaveral_terrain_contract_test: FAIL: $*" >&2; exit 1; }

controller="$ROOT/scripts/EarthGroundController.cs"
shader="$ROOT/assets/shaders/earth_ground.gdshader"
manifest="$ROOT/data/launch_sites/kennedy_terrain.json"

[[ -f "$manifest" ]] || fail "Kennedy terrain manifest is missing"
[[ -f "$ROOT/assets/textures/kennedy_naip_10km.jpg" ]] || fail "Kennedy regional NAIP asset is missing"
[[ -f "$ROOT/assets/textures/kennedy_naip_10km_mask.png" ]] || fail "Kennedy regional mask is missing"
[[ -f "$ROOT/assets/textures/kennedy_3dep_10km_height.png" ]] || fail "Kennedy regional 3DEP asset is missing"
[[ -f "$ROOT/assets/textures/cape_canaveral_naip_50km.jpg" ]] || fail "Cape macro NAIP asset is missing"
[[ -f "$ROOT/assets/textures/cape_canaveral_naip_50km_mask.png" ]] || fail "Cape macro mask is missing"
[[ -f "$ROOT/assets/textures/cape_canaveral_3dep_50km_height.png" ]] || fail "Cape macro 3DEP asset is missing"

rg -q 'bool isKennedySite = string\.Equals\(launchSiteId, "kennedy"' "$controller" \
  || fail "EarthGround does not select Kennedy by launch-site id"
rg -q 'string macroPrefix = isKennedySite \? "cape_canaveral"' "$controller" \
  || fail "Kennedy does not use the Cape macro terrain prefix"
rg -q 'if \(!string\.IsNullOrEmpty\(terrainPrefix\)\)' "$controller" \
  || fail "unmeasured sites can still accidentally load Starbase rasters"
rg -q 'uniform float site_profile' "$shader" \
  || fail "Earth-ground shader has no site profile"
rg -q 'florida_marsh' "$shader" \
  || fail "Florida fallback palette is missing"
rg -q '"site": "Kennedy Space Center / LC-39A"' "$manifest" \
  || fail "Kennedy manifest site identity is missing"
rg -q '"source": "USDA NAIP CONUS ImageServer"' "$manifest" \
  || fail "Kennedy NAIP provenance is missing"
rg -q '"source": "USGS 3DEP Elevation ImageServer"' "$manifest" \
  || fail "Kennedy 3DEP provenance is missing"
rg -q '"height_max_m": 15\.0' "$manifest" \
  || fail "Kennedy elevation range is not recorded"
rg -q 'KennedyHeightMaxM = 15\.0f' "$controller" \
  || fail "Kennedy runtime elevation maximum is missing"
rg -q 'KennedyHeightReferenceM = 3\.0f' "$controller" \
  || fail "Kennedy runtime elevation reference is missing"
rg -q 'float heightMaxM = isKennedySite \? KennedyHeightMaxM' "$controller" \
  || fail "Kennedy elevation encoding is not selected at runtime"

echo "cape_canaveral_terrain_contract_test: PASS (LC-39A NAIP+3DEP plus Florida fallback profile)"
