#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "starbase_3dep_relief_contract_test: FAIL: $*" >&2; exit 1; }
has() { rg -qF "$1" "$2" || fail "$3"; }

geo="$ROOT/scripts/StarbaseGeospatialContext.cs"
far="$ROOT/scripts/StarbaseFarField.cs"
earth="$ROOT/scripts/EarthGroundController.cs"
origin="$ROOT/scripts/FloatingOrigin.cs"

has 'BuildFaded3DepReliefMesh' "$geo" "hero and far-field 3DEP no longer share one faded mesh"
has 'BuildFaded3DepReliefMesh(HeroDepReliefPeakAlpha)' "$geo" "hero 3DEP does not use the hero peak-alpha path"
has 'BuildFaded3DepReliefMesh(DepReliefPeakAlpha)' "$far" "far-field 3DEP no longer uses the shared faded mesh"
has 'DepReliefRimStart = 0.62f' "$geo" "3DEP radial rim start drifted from far-field 0.62"
has 'DepReliefPeakAlpha = 0.08f' "$geo" "3DEP far-field peak alpha drifted from 0.08"
has 'CreateDepReliefMaterial' "$far" "3DEP material helper is missing"
has 'VertexColorUseAsAlbedo = true' "$far" "3DEP material does not use vertex colors as albedo"
has 'Transparency = BaseMaterial3D.TransparencyEnum.Alpha' "$far" "3DEP material is not alpha-blended"
has 'AddFadedReliefTriangle' "$geo" "hero 3DEP does not write vertex-color rim triangles"
has 'Mathf.Lerp(0.48f, 0.66f, tone)' "$geo" "3DEP sand/olive vertex tone drifted"
has 'HeroGeospatialFadeLowM = 1_000f' "$geo" "hero 3DEP/OSM campus does not start fading at 1 km"
has 'HeroGeospatialFadeHighM = 8_000f' "$geo" "hero 3DEP/OSM campus is not gone by 8 km"
has 'UpdateHeroGeospatialFade' "$geo" "hero 3DEP/OSM overlays are not faded on ascent"
has 'GeoRoad_' "$geo" "hero OSM roads are not in the geospatial fade set"
has 'FarContextMat(new Color(0.075f, 0.080f, 0.075f)' "$geo" "hero OSM roads are still opaque stickers"
has '_starbaseFarFieldContextMeshes' "$far" "regional context is not separated from civil silhouettes"
has 'AddFarContextMesh("Mapped3DepRelief"' "$far" "far-field 3DEP is not in the regional context fade"
has 'AddFarContextRotated($"MappedRoad_' "$far" "far-field roads are not in the regional context fade"
has 'FarSmoothstep(1_000f, 8_000f' "$far" "regional context does not replace the hero 1-8 km"
has 'FarSmoothstep(10_000f, 14_000f' "$far" "civil silhouettes do not fade after hero retirement"
has '_starbaseFarFieldContextMeshes.Contains(mesh)' "$far" "regional and civil far-field opacity are not applied independently"
has '_farSurfaceFade.Apply(mesh, meshOpacity)' "$far" "far-field fade does not reach materials on Compatibility"
has '_heroSurfaceFade.Apply(mesh, 1f - hide)' "$geo" "hero fade still relies on renderer-specific instance transparency"

if rg -qF 'BuildStarbase3DepRelief(wetland)' "$geo"; then
  fail "hero 3DEP still uses the opaque wetland albedo"
fi

if rg -A 20 'private void CollectCivilGroundMeshes' "$earth" | rg -q 'Starbase3DepRelief'; then
  fail "hero 3DEP was added to FadeCivilGroundBox; that fade is apron/OLM only"
fi

has 'EarthVisualHandoffLowM = 12_000.0' "$origin" "Earth visual handoff low altitude changed"
has 'EarthVisualHandoffHighM = 18_000.0' "$origin" "Earth visual handoff high altitude changed"

echo "starbase_3dep_relief_contract_test: PASS (hero 3DEP shares far-field vertex-color rim, not wetland Mat)"
