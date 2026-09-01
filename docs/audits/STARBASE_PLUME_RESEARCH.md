# Starbase / Super Heavy visual research

Research snapshot: 2026-08-31.

This note separates verified observations from implementation synthesis. It is
not a claim that the public material is a complete engineering drawing of the
current site or of the current Raptor variant.

## Research question and protocol

How can Exosphere make the Super Heavy plume and the Boca Chica / Starbase
context visually credible while keeping the simulation boundary intact and
avoiding restricted Google imagery?

Search themes:

1. SpaceX and NASA vehicle / mission facts.
2. NASA and FAA plume, nozzle, deluge, and pad-interaction material.
3. Open photographs and video frames for visual calibration.
4. Open geospatial sources for roads, footprints, water, wetlands, coast, and
   elevation.
5. Redistribution and attribution constraints.

Inclusion rule: a source is recorded only after opening the direct page or
document. Image-search results are leads, not evidence by themselves.

## Verified engineering and visual facts

- SpaceX describes Starship as a fully reusable system and its current mission
  pages document a Super Heavy first stage with 33 Raptor engines and a
  Starship upper stage with six engines. The flight pages also document engine
  ignition, hot-staging, and the changing vehicle configurations.
- The FAA's Raptor plume appendix and the associated Raptor environmental
  document describe a fuel-rich exhaust model and do not justify treating the
  visible cloud as soot. At low altitude the visible result is a combination of
  hot exhaust, entrained air, water-deluge vapor, pad material, and exposure.
- NASA's shock-diamond material confirms that diamonds are shock-wave features,
  not a permanent decorative stripe. Their visibility depends on pressure
  mismatch, nozzle geometry, contrast, and intervening vapor.
- NASA's launch-pad simulation work and the FAA environmental documentation
  treat the pad event as a coupled gas / water / surface interaction. The water
  deluge protects the steel plate and suppresses heat, fire, dust, and noise;
  it also changes the visible cloud and its asymmetry.
- Open IFT-5 photographs show a saturated white-to-cream root, restrained warm
  orange near the root, a large grey-white asymmetric cloud at the pad, and a
  translucent edge. In wide or orbital views the plume becomes a broken,
  diffuse white trace and clean shock diamonds are often not visible.

## Geospatial facts

- An Overpass extract for the Starbase area (bbox
  `25.988,-97.170,26.010,-97.145`) contained the following useful feature
  families: TX-4 / Boca Chica Boulevard, industrial footprints, 76 buildings,
  51 storage tanks, 27 tower/gantry elements, 23 pipeline elements, ponds,
  tidal flats, coastline, and the Pad-2 deluge runoff pond.
- The extract is stored in the project as a deliberately small selected
  derivative, not as a live network dependency. Coordinates are converted to
  local metres around the existing `starbase.json` origin and keep the project
  scale of 1 render unit = 2.8 m.
- USGS TNM reported a 1 m DEM product, `TX_LowerRioGrande_D22`, covering the
  area. The local terrain sample is used only for relative relief; it is not
  allowed to move the vehicle-interface datum or the simulation's WGS84
  placement.

## Annotated sources

### Vehicle and mission

- [SpaceX Starship](https://www.spacex.com/starship) — official vehicle and
  mission page; primary source for the 33-engine Super Heavy / six-engine
  Starship configuration shown on current flight pages.
- [SpaceX Starbase Overview](https://www.spacex.com/vehicles/starship/assets/media/Starbase%20Overview.pdf)
  — official overview PDF; useful for the launch/catch tower and the division
  between vehicle integration and ground infrastructure. SpaceX copyright; use
  as reference, not as a bundled texture.
- [NASA Artemis / Starship Flight 3](https://www.nasa.gov/directorates/esdmd/artemis-campaign-development-division/human-landing-system-program/nasa-artemis-mission-progresses-with-spacex-starship-test-flight/)
  — NASA mission summary; primary corroboration of the integrated vehicle and
  hot-staging sequence.

### Plume and launch-pad interaction

- [FAA Appendix G: Exhaust Plume Calculations](https://www.faa.gov/stakeholderengagement/spacexstarship/appendix-g-exhaust-plume-calculations)
  — official landing page and PDF link; direct project-specific plume source.
- [FAA Revised Draft Tiered Environmental Assessment, 2024](https://www.faa.gov/media/87646)
  — official PDF. It documents the current VLA layout, the change in Pad-B
  location, steel plates, deluge operation, retention ponds, and the fact that
  the exhaust / dust / water interaction is part of the environmental baseline.
- [NASA: X-2 Twin Set of Shock Diamonds](https://www.nasa.gov/image-article/x-2-twin-set-of-shock-diamonds/)
  — primary NASA visual explanation of shock diamonds.
- [NASA Ames LAVA launch-pad simulation](https://www.nasa.gov/aeronautics/artemis-sls-launch-sim/)
  — primary NASA simulation context for hot exhaust interacting with launch
  infrastructure.
- [NASA Ames methane / rocket CFD work](https://www.nas.nasa.gov/SC23/research/project10.html)
  — primary technical context for methane-oxygen combustion modelling; it
  supports using a physically controlled color / density model rather than an
  orange fire texture.

### Open visual references

- [IFT-5 ignition](https://commons.wikimedia.org/wiki/File:SpaceX_Starship_ignition_during_IFT-5.jpg)
  — CC BY 2.0 photograph by Steve Jurvetson; root saturation, warm root, and
  pad cloud.
- [IFT-5 liftoff](https://commons.wikimedia.org/wiki/File:Liftoff_of_SpaceX_IFT-5_(54064037095).jpg)
  — open Wikimedia photograph; useful for the wide asymmetric cloud and warm
  illumination on the tower.
- [IFT-5 booster plume](https://commons.wikimedia.org/wiki/File:Booster_Plume_of_SpaceX_IFT-5_(54062708072).jpg)
  — open Wikimedia photograph; useful for the white core and translucent
  grey-white edge at distance.
- [Starship 6 seen from the ISS](https://commons.wikimedia.org/wiki/File:The_launch_of_the_SpaceX_Starship_6_rocket_seen_from_the_space_station_(iss072e220043).jpg)
  — NASA / Don Pettit material mirrored on Wikimedia; the page marks the
  image as public domain. Useful for the diffuse orbital trace, not for
  near-field color calibration.
- [Starship IFT-6 from the ISS](https://commons.wikimedia.org/wiki/File:Starship_IFT-6_ISS.jpg)
  — open orbital reference; useful for atmospheric scale and the fact that the
  plume does not remain a visible bright cone at distance.

Photographs are calibration references only in this pass. They are not copied
into the game, because licenses differ and SpaceX official media does not imply
redistribution permission.

### Geospatial data

- [OpenStreetMap licence use cases](https://wiki.openstreetmap.org/wiki/License/Use_Cases)
  and [ODbL](https://wiki.openstreetmap.org/wiki/ODbL) — OSM data is reusable
  under ODbL with attribution and share-alike obligations for a derivative
  database. The game keeps the selected derivative and this provenance note
  together.
- [USGS 3DEP products and services](https://www.usgs.gov/3d-elevation-program/about-3dep-products-services)
  — primary source for free elevation products without use restrictions; the
  1 m product is lidar-derived bare-earth elevation.
- [USGS TNM Access API](https://tnmaccess.nationalmap.gov/api/v1/docs) — primary
  API documentation used to verify product availability.
- [USGS NAIP archive](https://www.usgs.gov/centers/eros/science/usgs-eros-archive-aerial-photography-national-agriculture-imagery-program-naip)
  — public-domain orthophoto source for future calibration or offline source
  material; no NAIP texture is bundled by this change.
- [NOAA Coastal Topographic Lidar](https://www.coast.noaa.gov/digitalcoast/data/coastallidar.html)
  and [NOAA Data Access Viewer](https://coast.noaa.gov/digitalcoast/tools/dav.html)
  — coastal lidar / DEM / contour source with selectable projection and datum;
  useful for a later shoreline and wetland refinement.

## Synthesis and limits

The renderer should have separate state variables for core energy, afterburn,
shock-cell visibility, steam, dust, and pad interaction. A single long opaque
cone cannot match both the close launch photographs and the diffuse orbital
references. Likewise, a rectangular green ground plane cannot communicate the
barrier-island site: the visual hierarchy must be coastline / wetland / water,
curved access road, industrial footprints, and then the launch hardware.

The public sources do not provide a complete current CAD model of every tank,
pipe, berm, or plume cell. Dimensions not explicitly present in a source remain
presentation estimates and are marked as such in code comments. The project
must not present this reconstruction as a survey or as a copy of Google Maps.
