# Global Earth relief pipeline

Status: source validated, derived texture not yet runtime-bound (2026-09-24).

## Why this source

The scaled Earth needs a real global elevation signal once the camera leaves the
Starbase or Kennedy regional rasters. The project therefore selects NOAA's ETOPO
2022 bedrock relief as the source for the backdrop layer. It combines land
topography and ocean bathymetry in a global geographic grid. The repository keeps
the provenance and conversion procedure, not the multi-hundred-megabyte raw
dataset.

This is a different job from the local terrain stack:

- NAIP + 3DEP remains authoritative near a launch site, where roads, pads,
  marshes and site-scale relief must read correctly.
- ETOPO supplies broad planetary curvature and relief in the scaled-space band.
- The derived asset is visual-only. It must never become a collision mesh,
  gravity source, launch-site elevation source or flight-dynamics input.

## Reproducible conversion

1. Download the 60 arc-second ETOPO 2022 bedrock GeoTIFF from the URL recorded in
   `data/provenance/earth_etopo2022_relief.json`.
2. Inspect the file and record its checksum without writing project assets:

   ```text
   bash tools/prepare_earth_relief.sh /path/to/ETOPO_2022_v1_60s_N90W180_bed.tif --check-only
   ```

3. Generate the bounded runtime texture only after the metadata check passes:

   ```text
   bash tools/prepare_earth_relief.sh /path/to/ETOPO_2022_v1_60s_N90W180_bed.tif
   ```

The conversion produces a 4096×2048 equirectangular, 16-bit grayscale PNG. The
height range is explicitly mapped from -11,000 m to 9,000 m. This covers Earth's
deep ocean basins and highest terrain while avoiding an implicit, source-dependent
normalization that would make later builds visually incomparable.

## Coordinate and handoff rules

The texture follows the same convention as the scaled Earth shader: longitude is
`atan2(z, x)` and latitude is `asin(y)`. The longitude seam must be tested at both
edges of the image; the poles must not introduce a visible pinching discontinuity.

The first runtime gate is 20, 50, 100 and 500 km camera altitude. The expected
result is continuity, not close-up geological detail. Local NAIP/3DEP terrain
must fade out before this backdrop becomes visible, and the handoff must not
change physics telemetry.

## Known limits

ETOPO is unsuitable for navigation and does not represent launch-site objects,
vegetation, buildings or fine coastal infrastructure. The source is public domain
under NOAA's CC0-1.0 statement, but the project still records the DOI and citation
for reproducibility. Any future switch to GEBCO, a higher-resolution extract or an
ice-surface variant must update the manifest and rerun the visual gate.
