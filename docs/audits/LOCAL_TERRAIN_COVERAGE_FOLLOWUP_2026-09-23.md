# Starbase terrain coverage follow-up — 2026-09-23

## Scope

This follow-up addresses the rectangular grey terrain cut visible during the
Starbase/Boca Chica ascent views. It is a presentation-data defect; no flight
physics, launch-site datum, or Earth handoff thresholds were changed.

The active default site is `starbase` at 25.9972 N, -97.1566 W. Cape Canaveral
and Kennedy are separate Florida launch complexes and must be treated as a
separate environment-data pass, not as alternate names for this scene.

## Root cause confirmed by framebuffer evidence

The checked-in 10 km and 50 km NAIP JPEGs contain opaque provider no-data areas
filled with a neutral olive colour. Their masks are intentionally feathered,
but the previous shader threshold was low enough to blend part of that fill as
measured terrain. The result was a rectangular or diagonal grey/olive region
when the camera looked across the coastal transition.

Runtime diagnostics confirmed that all terrain assets loaded:

```text
[EARTH_GROUND_TERRAIN] source=NAIP+3DEP+macro ready=True regional=10000m macro=50000m
```

The real 1920×1080 `starbase-far` captures in
`/tmp/exo_env_far_final/` reproduced the defect before the fix and then passed
the production gate after the fix. The deterministic matrix covers 2, 5, 8,
12, 20 and 40 km and ended with `STARBASE_FAR_OK`.

## Implemented correction

`assets/shaders/earth_ground.gdshader` now:

- rejects locally flat, neutral JPEG fill in both regional and macro rasters;
- raises the mask acceptance threshold from the provider feather tail into the
  measured core of each footprint;
- returns those pixels to the existing Blue Marble/procedural fallback instead
  of inventing measured geography;
- preserves the existing circular horizon rim and 12–18 km Earth-globe handoff.

`tools/tests/earth_ground_lighting_contract_test.sh` now guards both no-data
rejection functions and the stricter macro mask threshold.

## Scaled-Earth radiance follow-up

The grey tile-shaped cut is closed. The remaining far-field defect was a dark
ocean and a dark interpolation band at the 20–40 km scaled-Earth handoff. The
solar material binding and the world-space normal frame were both correct:

```text
PERF_SOLAR_BIND earthMaterial=bound sunDir=0.3744,-0.6431,-0.6680
PERF_SOLAR_CYCLE ... elevationDeg=28.123 ... solarVisibility=1.000
```

The cause was presentation radiance: the linear Blue Marble ocean had direct
Lambertian response but no bounded atmospheric sky fill, while the sky's
daylight floor faded too early for the configured 140 km shell. This was not a
flight-physics or solar-geometry defect.

The correction is split across the two render layers:

- `earth_surface.gdshader` adds a view-independent ocean term derived from the
  same Rayleigh optical-depth state, gated by water, daylight, and solar
  visibility. It cannot create a camera-centred contour.
- `space_sky.gdshader` fades its bounded daylight floor against the active
  atmosphere height, keeping the 40 km handoff continuous and forcing the floor
  to zero before vacuum.

The final real 1920×1080 `starbase-far` run in `/tmp/exo_env_ocean_final3/`
finished with `STARBASE_FAR_OK`. The scaled-Earth contract passed for both
handoff frames; `lowerMean` improved from the previous baseline of roughly
`0.069` to `0.19773` at 20 km and `0.20213` at 40 km, with
`surfaceClippedFrac=0.00000` in both frames. The images still show a broad dark
gradient immediately above the bright blue limb at 40 km. Terrain/globe
ownership is continuous, but the atmospheric composition itself is not closed:
the residual is in grazing-ray sky transport or sky/limb overlap, not ocean
albedo. Do not hide it with another global brightness multiplier; the next
visual front should isolate `hits_ground`, tangent sky, and opaque globe
coverage against a calibrated camera/exposure reference.

## Remaining environment work

The Starbase/Boca Chica transition is now continuous in the validated matrix.
Cape Canaveral/Kennedy remains a separate environment-data pass: its launch
complex geometry, coastal rasters, datum, and site-specific horizon need their
own source inventory and visual gate rather than reusing the Starbase assets.

## Data references

- [USGS 3DEP products and services](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services)
- [NASA Kennedy Space Center physical assets](https://www.nasa.gov/kennedy/partnerships/kscphysicalassets/)
- [NASA launch sites: Kennedy and Cape Canaveral](https://www.nasa.gov/kennedy/launch-services-program/launch-services-program-launch-sites/)
