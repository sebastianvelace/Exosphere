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

The final real 1920×1080 `starbase-far` run in
`/tmp/exo_env_tangent_shell_final/` finished with `STARBASE_FAR_OK`. The
scaled-Earth contract passed for both handoff frames; `lowerMean` is `0.19773`
at 20 km and `0.20213` at 40 km, with `surfaceClippedFrac=0.00000` and
`darkFrac=0.00000` in both frames.

The remaining dark band was isolated with a diagnostic framebuffer as a
grazing-ray sky-classification error. At 20–40 km, a ray can point below local
horizontal, miss the opaque globe, and still intersect the visible atmospheric
shell. The previous `above-horizon` floor classified that ray as neither ground
nor sky, so the interpolation fell through to a dark planet fill. The fix in
`space_sky.gdshader` computes the ray's closest-approach altitude, gates it with
`hits_ground`, and applies the bounded daylight floor across the full active
atmosphere shell. It does not change terrain ownership, solar geometry, or
flight physics. The new real captures show a continuous pale-blue transition;
the residual soft gradient is atmospheric composition, not an opaque black
artifact.

## Remaining environment work

The Starbase/Boca Chica transition is now continuous in the validated matrix,
including the tangent atmosphere limb above the scaled Earth. The next
environment front is still Cape Canaveral/Kennedy: its launch-complex geometry,
coastal rasters, datum, and site-specific horizon need their own source
inventory and visual gate rather than reusing the Starbase assets.

## Data references

- [USGS 3DEP products and services](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services)
- [NASA Kennedy Space Center physical assets](https://www.nasa.gov/kennedy/partnerships/kscphysicalassets/)
- [NASA launch sites: Kennedy and Cape Canaveral](https://www.nasa.gov/kennedy/launch-services-program/launch-services-program-launch-sites/)
