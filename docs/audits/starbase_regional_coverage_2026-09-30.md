# Starbase regional coverage — 2026-09-30

The user's ascent screenshots (HUD AP values, not measured camera altitudes)
still showed a circular photographic
island disappearing into gray terrain. The earlier geographic-depth fix preserved
Earth coverage, but did not fix the imagery footprint or the missing regional data.

## Root causes and resulting behavior

- NAIP's 10 km and macro mosaics contain irregular provider voids. Their old
  Gaussian masks used 192/768 pixel feathers and an additional circular shader
  cutoff, hiding substantial valid geography.
- The macro request covers one degree of longitude and latitude. Its old 50 km
  runtime extent compressed roughly 100.12 x 110.79 km of imagery into a square,
  breaking alignment with the local mosaic.
- Offshore NAIP acquisitions have conspicuous strip colour/seam differences.
  Removing the circle alone exposed those rectangular ocean edges.
- The global texture basis had determinant -1: its columns swap the usual
  north/east order. Converting it to a quaternion discarded the reflection.
  A Godot diagnostic mapped Starbase's 25.9972°N / 97.1566°W to
  25.9972°S / 82.8434°E, placing the regional image over Indian Ocean imagery.
  Earth shaders now receive the full reflected world-to-texture matrix, including
  live sidereal spin. Global UVs convert geocentric direction to geodetic latitude
  using the live body's squared polar/equatorial radius ratio. Local ground uses
  the same complete basis; the simulation's frame is unchanged.
- The original visual gate accepted frames rendered with missing texture assets.
  Shader compilation checks were necessary but insufficient.

The local ground and opaque globe now share manifest-derived east/north raster
sizes, observed footprints and approximately 333 km of Landsat/BMNG context.
Valid Starbase imagery is no longer cropped to a circle. Missing coverage reveals
geography, not neutral fill or a procedural coast. Pad 2 uses the same raster
centre with its coordinate offset; moving the pad does not move the shoreline.
Other sites retain their legacy masks and calibration.

Legacy JPEG voids are recovered offline by border-connected neutral fill, not
by colour alone. An isolated agricultural field of the same colour stays valid.
The source boundary feathers inward by 12/24 pixels, rather than blurring the
mask across voids. Geographic shore distance blends offshore NAIP into continuous
NASA ocean context over 250–1250 m. This is a presentation blend, not measured
bathymetry. NASA's coarser ocean/land separation limits shoreline accuracy there;
measured coastal imagery and inlets remain the near-field detail.

The 330 km raster also has a broad border blend into the correctly oriented
global map. This retires resolution/detail rather than replacing land with ocean.
Optical fixtures measure the *bound shader matrix* at the live launch-site epoch:
latitude/longitude errors must stay below 0.001°, with determinant -1 retained.

## Sources and physical authority

[NASA GIBS access](https://nasa-gibs.github.io/gibs-api-docs/access-advanced-topics/)
provides the `Landsat_WELD_CorrectedReflectance_TrueColor_Global_Annual` layer
at `2000-12-01`. The 4096² regional export samples approximately 81 m per pixel;
the native GWELD product is described in the
[USGS user guide](https://lpdaac.usgs.gov/documents/2086/GWELD_User_Guide_V32.pdf).
The continuous ocean uses NASA's
[Blue Marble Next Generation](https://science.nasa.gov/earth/earth-observatory/history-of-the-blue-marble/),
a historical 500 m product, exported at 1024² for this region. These are geographic
context mosaics, not photographs or weather of Flight 14. New buildings outside
the existing NAIP footprint are not reconstructed by historical Landsat.

Bounds, layer identifiers, export dimensions, input SHA-256 hashes, attribution,
mask parameters and limitations are stored in
`data/launch_sites/starbase_terrain.json`. The old scalar runtime extents remain
for compatibility; `runtime_size_m` is authoritative for Starbase rendering.
WGS84 degree lengths at the site provide a local tangent approximation, not a
full regional reprojection. Error increases toward the 330 km raster perimeter.

No flight dynamics, atmosphere density, heating, gravitational model, collisions,
integrator, guidance or time-warp settings changed. The existing finite RGB
camera-to-ground optical transport remains intact. Coverage and alpha express
imagery availability; they do not control atmospheric extinction.

## Reproduce

Download two PNG GetMap responses from the GIBS endpoint with WMS 1.3.0,
`CRS=EPSG:4326`, latitude-first bbox
`24.4972,-98.822,27.4972,-95.4912`, `FORMAT=image/png`,
`STYLES=`, `TRANSPARENT=TRUE`, `TIME=2000-12-01`:

- Landsat layer above: `WIDTH=4096`, `HEIGHT=4096`.
- `BlueMarble_NextGeneration`: `WIDTH=1024`, `HEIGHT=1024` (static August 2004 layer).

```bash
python3 tools/build_starbase_context_assets.py \
  --landsat /tmp/starbase-landsat-context.png \
  --blue-marble /tmp/starbase-bmng-context.png
python3 tools/tests/starbase_coverage_assets_test.py
"$GODOT_BIN" --headless --path . --import
bash tools/visual_playtest.sh --starbase-coverage \
  --run-id starbase-coverage-final --renderer compatibility \
  --resolution 960x540 --skip-build
```

The offline builder requires Python, NumPy, Pillow and SciPy. The shipped game
uses checked-in assets and does not download imagery at runtime. Texture import
policies enable mipmaps so acquisition detail does not turn into aliasing during
ascent. The coverage fixture pauses physics at 7/16/26 km, with 28° oblique and
80° near-nadir views. It proves optical coverage; its ORBIT label and zero airspeed
do not certify a physical orbit or an ascent trajectory.

## Validation

- Final full CI: 902/902 xUnit tests, both builds with zero warnings/errors,
  all shell contracts and Godot startup/scene smoke passed
  (`/tmp/starbase-geographic-final-ci.log`).
- Four offline behavior checks passed: neutral fields are retained, connected
  voids are excluded, offshore handoff follows shore distance, macro scales are
  not compressed and shipped context contains geographic variation in Mexico.
- Final Compatibility: `starbase-geographic-coverage`, six 960x540 views at
  7/16/26 km vessel altitudes, oblique/near-nadir; 345 frames, PASS. Manual review
  confirms continuous land and Gulf coverage without the circular gray void.
- Final Forward+: `starbase-geographic-forward`, six 640x360 views at
  0/12/15/18/100/250 km from Pad 2; 345 frames, PASS. Manual review confirms that
  the regional coast joins the global geography at 100/250 km.
- Actual bound shader matrix: determinant -1, latitude/longitude error at the
  launch site 0–0.000001° in both capture runs, below the 0.001° gate.
- Four negative diagnostic fixtures reject shader/script failures and missing
  resources; benign UID/audio warnings remain accepted. Rechecking an older run
  also rejects its missing geographic telemetry (not a resource-guard-only proof).
- Local review: `exports/starbase-regional-coverage/comparison.html`. The user
  screenshot has AP telemetry rather than actual altitude; camera/lighting differ
  from the new fixtures. This is qualitative comparison, not a matched-camera A/B.

Intermediate captures `starbase-coverage-v1` were invalid: the newly created
texture had not been imported and the old gate accepted fallback frames. The
updated guard rejects missing-resource errors as well as shader/script failures.
`v2`, `v3`, `starbase-coverage-final` and `starbase-coverage-forward` are
intermediate imagery/mask checks, not final acceptance images. Orbital review of
the latter exposed the global basis error; accepting only close-up views would
have missed the regional rectangle sitting over the wrong global geography.

Remaining limits include historical source dates, unequal NAIP/Landsat colour,
coarser geography beyond the detailed footprint, approximate clouds, the existing
orbital spectral/exposure calibration and unmeasured physical-GPU frame rate.
