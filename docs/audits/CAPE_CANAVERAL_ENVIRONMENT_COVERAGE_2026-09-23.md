# Cape Canaveral / Kennedy environment coverage — 2026-09-23

## Research question and protocol

The question for this front is: which site-specific data must own the rendered
ground between the pad and the 40 km scaled-Earth handoff so that Kennedy and
Cape Canaveral do not inherit Boca Chica's geography?

The protocol was deliberately limited to primary public sources:

- NASA facilities and history pages for the physical organization of Kennedy,
  LC-39A/39B and the Cape launch complexes.
- USGS 3DEP documentation for bare-earth elevation products, resolution and
  vertical datum.
- USDA NAIP's public ImageServer for observed orthophotography.
- NOAA C-CAP and USFWS NWI documentation for coastal land-cover and wetland
  context. These are context sources, not a substitute for pad-scale imagery.

The implementation threshold is: a source-backed raster must have an explicit
WGS84 request box, dimensions, source URL, elevation encoding and preparation
date in a manifest. A procedural fallback may fill uncovered pixels, but it
must not be presented as measured site data.

## Code audit before this pass

The simulation already had correct geodetic launch-site records for Kennedy
LC-39A and Cape Canaveral LC-5, LC-14, LC-19 and SLC-36. The gap was in the
presentation layer:

1. `LaunchPadController.BuildConventionalLaunchComplex` used one generic pad
   for every non-Starbase site.
2. `EarthGroundController` loaded the Starbase NAIP/3DEP rasters
   unconditionally, so a Kennedy flight could show Boca Chica imagery.
3. There was no Florida terrain manifest or visual gate.

That meant the launch coordinates were physically correct while the visible
coastline and land cover were not.

## Implemented coverage

LC-39A now has its own measured layer:

- Regional NAIP + 3DEP: 10 km WGS84 box centered on `28.608389, -80.604333`.
- Macro NAIP + 3DEP: 50 km WGS84 box covering the Kennedy/Cape barrier-island
  complex and the Banana River / Atlantic context.
- NAIP is retained as RGB reflectance with a feathered validity mask.
- 3DEP is converted to a 16-bit relative height texture using a `-2..15 m`
  range and a `3.0 m` launch-site reference. The runtime treats it as
  bare-earth displacement, not as building height.
- The fallback profile is now site-aware: Florida uses wetter marsh/lagoon
  tones, while Starbase keeps its Boca Chica sandy/tidal-flat profile.
- The atmospheric horizon now follows the render camera altitude for Earth,
  matching the terrain/globe compositor. This prevents a chase camera below a
  seeded vehicle from inheriting the vehicle's thinner-atmosphere horizon.

The conversion is reproducible through
`tools/build_starbase_terrain_assets.py`; the historical name is retained for
backwards compatibility, but `--prefix`, `--macro-prefix`, explicit boxes and
site metadata make it a shared builder.

## Current site matrix

| Site | Local measured raster | Macro context | Status |
| --- | --- | --- | --- |
| Kennedy LC-39A | Kennedy 10 km NAIP + 3DEP | Cape 50 km NAIP + 3DEP | Implemented and gated |
| Cape LC-5 | Not yet centered on LC-5 | Not yet origin-shifted | Procedural fallback only |
| Cape LC-14 | Not yet centered on LC-14 | Not yet origin-shifted | Procedural fallback only |
| Cape LC-19 | Not yet centered on LC-19 | Not yet origin-shifted | Procedural fallback only |
| Cape SLC-36 | Not yet centered on SLC-36 | Not yet origin-shifted | Procedural fallback only |
| Starbase / Boca Chica | Existing NAIP + 3DEP stack | Existing 50 km stack | Existing validated path |

The restriction on the four historical Cape sites is intentional: reusing the
LC-39A regional raster with a different local origin would create a plausible
but geographically false pad view. They must receive independent origins or a
shared geospatial layer whose coordinate transform is explicit.

## Framebuffer evidence

The Kennedy gate completed with the compatibility renderer at 1920×1080:

- `KENNEDY_FAR_OK` with six frames: 2, 5, 8, 12, 20 and 40 km.
- Every frame reported `source=NAIP+3DEP:kennedy`, with both regional and
  macro layers ready.
- The 20 and 40 km frames reported `earthGlobeAlpha=1.000`, local ground and
  pad hidden, and no clipped surface pixels.
- The visual review showed the former dark atmospheric strip becoming a smooth
  camera-consistent limb. The remaining 20–40 km view is intentionally owned by
  the scaled Earth; pad-scale geometry is not claimed at those distances.

## Verified sources

- [NASA KSC physical assets](https://www.nasa.gov/kennedy/partnerships/kscphysicalassets/)
  identifies the VAB, launch facilities and supporting land assets.
- [NASA history of the Launch Operations Center](https://www.nasa.gov/history/creating-nasas-launch-operations-center/)
  documents the Merritt Island campus and the LC-39 support complex.
- [NASA LC-39A agreement](https://www.nasa.gov/news-release/nasa-signs-agreement-with-spacex-for-use-of-historic-launch-pad/)
  confirms LC-39A as a distinct seaside commercial launch site.
- [USGS 3DEP products and services](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services)
  distinguishes lidar/DEM products and their intended use.
- [USGS 1-meter DEM catalog record](https://data.usgs.gov/datacatalog/data/USGS%3A77ae0551-c61e-4979-aedd-d797abdcde0e)
  documents UTM/NAD83 horizontal coordinates and NAVD88 metre elevations for
  1 m bare-earth DEM products.
- [NOAA C-CAP coastal land cover](https://coast.noaa.gov/digitalcoast/data/ccapregional.html)
  provides authoritative coastal land-cover context and explicitly warns that
  30 m products are screening data for local decisions.
- [USFWS National Wetlands Inventory downloads](https://www.fws.gov/rivers/rivers/program/national-wetlands-inventory/data-download)
  provides current wetland polygons and metadata, with updates twice yearly.

## Next gate

The next environmental gate is to center independent regional layers on LC-5,
LC-14, LC-19 and SLC-36, then repeat the same 2–40 km framebuffer matrix for
each site. Their conventional launch complexes also need pad-specific geometry;
the measured terrain layer alone does not make the historical facilities
visually correct.

No simulation or flight-physics code was changed in this front.
