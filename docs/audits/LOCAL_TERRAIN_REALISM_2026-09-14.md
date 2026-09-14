# Starbase regional terrain realism audit — 2026-09-14

## Scope

This pass addressed the first-flight visual defect where the pad looked like a detailed
industrial diorama over a generic procedural coastal patch. The goal was to make the
ground read as Boca Chica during the first kilometres without changing flight physics,
the launch-site datum, civil-works ownership or the larger planetary handoff.

## Root cause

`EarthGroundController` renders a large tangent-plane mesh with a procedural shader. Its
Blue Marble input supplies useful planetary colour at orbital scale, but its source texels
are kilometres wide at launch range. The shader's coast and marsh reconstruction can
therefore suggest a coastline without supplying observed roads, tidal flats, beach
geometry or the characteristic Gulf/land relationship. Existing OSM footprints and the
3DEP hero relief are separate meshes, so they cannot provide continuous ground material
between the pad structures.

## Implemented solution

- Added `assets/textures/starbase_naip_10km.jpg`: a 4096×4096 USDA NAIP RGB raster for a
  0.1° WGS84 box centered on the Starbase launch-site profile.
- Added `assets/textures/starbase_naip_10km_mask.png`: an edge-feather mask. The provider's
  open-Gulf no-data strip is filled by a row-wise interpolation from observed water, so
  the runtime cannot expose a black rectangle or a hard ocean seam.
- Added `assets/textures/starbase_3dep_10km_height.png`: a 2048×2048 16-bit normalized
  broad-relief raster. Invalid float-service values are rejected, filled at the launch
  reference height, clamped to -2..12 m, and median-filtered before export.
- Added `tools/build_starbase_terrain_assets.py` and
  `data/launch_sites/starbase_terrain.json` so the conversion, dimensions, bounds,
  datum and source provenance are reproducible and reviewable.
- Updated `assets/shaders/earth_ground.gdshader` to map mesh +X to geodetic east and +Z
  to geodetic north, sample relief in the vertex path, blend observed colour in the
  fragment path, and feather the 10 km region into the existing fallback.
- Updated `scripts/EarthGroundController.cs` to bind all three assets and emit a runtime
  readiness diagnostic. The existing local pad geometry and larger-scale ground remain
  in their current ownership paths.

## Verification

Source and runtime checks:

- `bash tools/tests/earth_ground_lighting_contract_test.sh` — PASS.
- `dotnet build Exosphere.csproj --no-restore` — PASS, 0 warnings and 0 errors.
- Godot 4.6.3 `--headless --path . --import --quit` — PASS; all three regional assets
  imported successfully.
- Asset inspection — 4096² RGB ortho, 4096² grayscale mask, 2048² height raster; the
  final NAIP image contains zero fully-black pixels.

Real framebuffer evidence used the production camera with the Compatibility renderer at
1280×720 under Xvfb:

| Run | Evidence | Result |
| --- | --- | --- |
| `launch-track` | pad, startup ramp, liftoff, tower clear, early ascent | `LAUNCH_TRACK_OK`; runtime logged `source=NAIP+3DEP ready=True` |
| `starbase-far` | 2, 5, 8, 12, 20 and 40 km | `STARBASE_FAR_OK`; no internal diagonal ocean seam in the regional handoff |

The 2–8 km frames retain visible roads, marsh, beach and Gulf water while the launch
complex remains anchored in the scene. At 12–40 km the regional material progressively
gives way to the existing far-field presentation. A dark horizontal horizon band remains
in those far frames; it is part of the pre-existing large-scale atmosphere/ground fade,
not the removed NAIP provider seam.

## Data provenance and limits

The orthophoto source is the [USDA NAIP CONUS ImageServer](https://apps.geo.fpac.usda.gov/geo-imagery/rest/services/naip/conus_naip/ImageServer).
The elevation source is the [USGS 3DEP Elevation ImageServer](https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer),
with USGS product context documented in the [3DEP products and services overview](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services).
The manifest records the exact bbox, dimensions, encoding and preparation date.

The assets are presentation rasters for a 10 km regional window. They do not replace
collision geometry, high-frequency terrain normals or a full statewide terrain system.
NAIP acquisition timing is provider-dependent and is intentionally not asserted as a
single capture year. The next visual work should compare against dated launch references
and measure performance on the target GPU before increasing raster coverage or detail.
