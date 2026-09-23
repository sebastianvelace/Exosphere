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

## Remaining visual gap

The grey tile-shaped cut is closed, but the low-camera 2–12 km fixtures still
show a broad dark atmospheric limb. That is a separate horizon-compositing
issue, not provider coverage: the next pass should calibrate the atmosphere/
ground radiance and camera altitude relationship using the same measured sky
state. It should not be hidden with an arbitrary brightness multiplier.

## Data references

- [USGS 3DEP products and services](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services)
- [NASA Kennedy Space Center physical assets](https://www.nasa.gov/kennedy/partnerships/kscphysicalassets/)
- [NASA launch sites: Kennedy and Cape Canaveral](https://www.nasa.gov/kennedy/launch-services-program/launch-services-program-launch-sites/)
