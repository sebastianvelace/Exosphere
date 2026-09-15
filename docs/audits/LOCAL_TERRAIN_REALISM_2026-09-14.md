# Starbase terrain transition realism audit — 2026-09-14

## Scope

This pass addressed the first-flight visual defect where the pad looked like a detailed
industrial diorama over a generic procedural coastal patch and then became an isolated
realistic square during the mid-altitude pullback. The goal was to make the ground read
as Boca Chica from the launch range through the 2–40 km transition without changing
flight physics, the launch-site datum, civil-works ownership or the larger planetary
handoff.

## Root cause

`EarthGroundController` renders a large tangent-plane mesh with a procedural shader. Its
Blue Marble input supplies useful planetary colour at orbital scale, but its source texels
are kilometres wide at launch range. The shader's coast and marsh reconstruction can
therefore suggest a coastline without supplying observed roads, tidal flats, beach
geometry or the characteristic Gulf/land relationship. Existing OSM footprints and the
3DEP hero relief are separate meshes, so they cannot provide continuous ground material
between the pad structures. Extending the detailed raster directly would have introduced
provider no-data blocks and visible scanlines, so the transition needs a separately
filtered macro layer with an explicit validity mask.

## Implemented solution

- Added `assets/textures/starbase_naip_10km.jpg`: a 4096×4096 USDA NAIP RGB raster for a
  0.1° WGS84 box centered on the Starbase launch-site profile.
- Added `assets/textures/starbase_naip_10km_mask.png`: an edge-feather mask. The provider's
  open-Gulf no-data strip is filled by a row-wise interpolation from observed water, so
  the runtime cannot expose a black rectangle or a hard ocean seam.
- Added `assets/textures/starbase_3dep_10km_height.png`: a 2048×2048 16-bit normalized
  broad-relief raster. Invalid float-service values are rejected, filled at the launch
  reference height, clamped to -2..12 m, and median-filtered before export.
- Added 4096² NAIP and 2048² 3DEP macro rasters covering a 50 km WGS84 box. Their
  provider footprint is retained as a feathered validity mask; no-data pixels are filled
  with a filtered-safe neutral colour so bilinear sampling cannot pull black scanlines
  into the observed terrain.
- Added `tools/build_starbase_terrain_assets.py` and
  `data/launch_sites/starbase_terrain.json` so the conversion, dimensions, bounds,
  datum and source provenance are reproducible and reviewable.
- Updated `assets/shaders/earth_ground.gdshader` to map mesh +X to geodetic east and +Z
  to geodetic north, sample absolute relief in the vertex path, blend macro then regional
  observed colour in the fragment path, and feather the macro and 10 km layers into the
  existing fallback.
- Updated `scripts/EarthGroundController.cs` to bind all six terrain textures and emit a
  runtime readiness diagnostic. The existing local pad geometry and larger-scale ground
  remain in their current ownership paths.

## Verification

Source and runtime checks:

- `bash tools/tests/earth_ground_lighting_contract_test.sh` — PASS.
- `dotnet build Exosphere.csproj --no-restore` — PASS, 0 warnings and 0 errors.
- Godot 4.6.3 `--headless --path . --import --quit` — PASS; regional and macro assets
  imported successfully.
- Asset inspection — regional 4096² RGB ortho, macro 4096² RGB ortho, 4096² masks and
  2048² height rasters; processed NAIP imagery contains no fully-black provider void.

Real framebuffer evidence used the production camera with the Compatibility renderer at
1280×720 under Xvfb:

| Run | Evidence | Result |
| --- | --- | --- |
| `launch-track` | pad, startup ramp, liftoff, tower clear, early ascent | `LAUNCH_TRACK_OK`; runtime logged `source=NAIP+3DEP+macro ready=True` |
| `starbase-far` | 2, 5, 8, 12, 20 and 40 km | `STARBASE_FAR_OK`; no isolated regional square or internal diagonal ocean seam |

The 2–8 km frames retain visible roads, marsh, beach and Gulf water while the launch
complex remains anchored in the scene. The 12 km frame is the last local Starbase context
view. The scaled Earth handoff then completes at 18 km, before the 20 km pulled-back
camera can expose the tangent patch as a foreground band; both 20 and 40 km use one
continuous planetary surface. The isolated realistic square and the 20–40 km horizon
band are therefore absent from the final handoff frames. A bounded deep-blue limb remains
in the low local 2–12 km horizon, where it belongs to the atmosphere/ground transition,
not to a provider mask or raster boundary.

The follow-up handoff run used the corrected high-altitude camera fixture so the camera
altitude matched each 20/40 km target instead of remaining near 716 m. Its compositor
telemetry recorded `earthGlobeAlpha=1.000`, `groundVisible=False`, `padVisible=False`
and `farFieldVisible=False` at both scaled-Earth cases. The six PNGs are retained in
`/tmp/exo_handoff_narrow_valid/`; the 1280×720 `--verify-only` gate passed.

## Data provenance and limits

The orthophoto source is the [USDA NAIP CONUS ImageServer](https://apps.geo.fpac.usda.gov/geo-imagery/rest/services/naip/conus_naip/ImageServer).
The elevation source is the [USGS 3DEP Elevation ImageServer](https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer),
with USGS product context documented in the [3DEP products and services overview](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services).
The manifest records the exact regional and macro bboxes, dimensions, encoding and
preparation date.

The assets are presentation rasters for detailed 10 km and macro 50 km windows. They do
not replace collision geometry, high-frequency terrain normals or a full statewide terrain
system.
NAIP acquisition timing is provider-dependent and is intentionally not asserted as a
single capture year. The next visual work should compare against dated launch references
and measure performance on the target GPU before increasing raster coverage or detail.
