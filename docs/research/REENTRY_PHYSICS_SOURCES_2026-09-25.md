# Reentry physics source register — 2026-09-25

## Research question

Which parts of Exosphere's orbital return and Starship entry behavior are physically
supported, which are engineering approximations, and which can create visibly artificial
motion?

## Method

- Prefer primary sources from NASA, FAA and SpaceX.
- Use public sources only for observable mission behavior and general entry physics; SpaceX
  does not publish a complete Starship aerodynamic database or flight-control law.
- Keep source-backed facts separate from code-derived findings and implementation choices.
- Treat every Starship-specific coefficient without a public primary source as an estimate.

## Annotated sources

### U.S. Standard Atmosphere, 1976

- **Publisher:** NOAA, NASA and U.S. Air Force
- **Primary source:** [NASA NTRS 19770009539](https://ntrs.nasa.gov/citations/19770009539)
- **Verified:** 2026-09-25
- **Supports:** pressure, temperature and density reference profiles through 1000 km, with
  the principal standard-atmosphere tables through 85 km.
- **Does not support:** day-specific winds, turbulence, latitude/season variability or
  solar-activity-driven thermosphere density.
- **Use in Exosphere:** atmosphere-model validation and explicit labeling of the current
  atmosphere as a standard mean profile, not weather.

### Sutton–Graves stagnation-point heating correlation

- **Authors:** Kenneth Sutton and Randolph Graves Jr.
- **Primary source:** [NASA TR R-376](https://ntrs.nasa.gov/citations/19720003329)
- **Verified:** 2026-09-25
- **Supports:** an engineering correlation for convective stagnation-point heat transfer to
  blunt bodies over broad gas-mixture and entry-condition ranges.
- **Does not support:** full surface heat distribution, tile conduction geometry, catalytic
  wall chemistry or Starship-specific plasma radiation.
- **Use in Exosphere:** stagnation heat-flux telemetry and thermal-order-of-magnitude gates.

### Entry flight-control analysis for a reusable launch vehicle

- **Author:** Philip C. Calhoun, NASA Langley
- **Primary source:** [NASA/AIAA 2000-1046](https://ntrs.nasa.gov/archive/nasa/casi.ntrs.nasa.gov/20000032921.pdf)
- **Verified:** 2026-09-25
- **Supports:** angle of attack as an energy/heating control variable; bank angle as the main
  means of rotating the lift vector for downrange and crossrange control; control-authority
  variation through the Mach regime; sensitivity to centre of gravity, control mixing and
  atmospheric disturbance.
- **Does not support:** direct reuse of X-vehicle gains or coefficients for Starship.
- **Use in Exosphere:** architecture of energy-aware bank guidance and the required
  Mach/AoA/control-surface dimensions of an aerodynamic database.

### NASA atmospheric-flight 6-DoF verification cases

- **Publisher:** NASA Engineering and Safety Center
- **Primary source:** [Six-degree-of-freedom check-case overview](https://nescacademy.nasa.gov/flightsim/)
- **Verified:** 2026-09-25
- **Supports:** independent check cases for equations of motion, atmosphere, rotating-Earth
  frames and atmospheric/orbital transitions.
- **Does not support:** Starship geometry, flap maps or a mission trajectory.
- **Use in Exosphere:** verification structure: canonical states, frame-explicit telemetry,
  independent invariants and cross-rate repeatability.

### SpaceX Starship Flight 12 mission profile

- **Publisher:** SpaceX
- **Primary source:** [Starship Flight 12](https://www.spacex.com/launches/starship-flight-12)
- **Verified:** 2026-09-25
- **Supports:** publicly described dynamic banking, four-flap guidance, landing flip and
  landing burn; the published timeline separates entry, transonic flight, subsonic flight,
  landing burn and flip by many minutes.
- **Does not support:** unpublished state vectors, aerodynamic coefficients, controller gains
  or a Flight 14 performance reconstruction.
- **Use in Exosphere:** qualitative phase ordering and timing plausibility only.

### FAA Starship return-to-launch-site environmental assessment

- **Publisher:** Federal Aviation Administration
- **Primary source:** [Final Tiered Environmental Assessment, additional trajectories and
  Starship RTLS](https://www.faa.gov/space/stakeholder_engagement/spacex_starship/Final_Tiered_EA_Additional_Launch_Trajectories_Starship_RTLS_mission_profiles_SpaceX_Starship-Super_Heavy_Boca_Chica.pdf)
- **Verified:** 2026-09-25
- **Supports:** a long return ground track from the Pacific across the southwestern United
  States/Mexico toward South Texas and the Gulf, rather than a local vertical homing maneuver.
- **Does not support:** a public high-rate trajectory or control law.
- **Use in Exosphere:** corridor-scale requirements and rejection of short-range point-homing
  as a complete orbital-entry solution.

## Synthesis boundaries

The sources support the required state variables and verification method. They do **not**
provide enough public data to claim a flight-certified Starship model. Exosphere should
therefore target physically causal, frame-correct and sensitivity-tested behavior, while
publishing uncertainty ranges for the estimated aerodynamic and control parameters.
