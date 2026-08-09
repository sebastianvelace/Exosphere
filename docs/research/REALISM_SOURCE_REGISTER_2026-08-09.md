# Realism source register — 2026-08-09

This register separates published data from game approximations. Values that are not
published by the vehicle operator are intentionally left as configurable estimates rather
than presented as “Raptor 3 facts”.

## Atmosphere and aerothermodynamics

- [NASA CCMC NRLMSISE-00](https://ccmc.gsfc.nasa.gov/models/NRLMSIS~00/) — empirical,
  global temperature and density model from the ground through the exosphere, used for
  satellite-drag prediction. Future orbital atmosphere work should accept date, latitude,
  longitude, solar flux and geomagnetic inputs rather than relying on a single exponential.
- [NASA Glenn rocket thrust equation](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/rocket-thrust-equation/)
  — thrust is `F = mdot Ve + Ae (pe - p0)`. The simulation already models ambient-pressure
  correction; the next audit should verify nozzle area and chamber-pressure telemetry stay
  consistent with this equation.
- [NASA Glenn specific impulse](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/specific-impulse/)
  — `Isp = F / (mdot g0)` for the equivalent exhaust velocity. This is the invariant used
  by the propulsion tests and should remain the source of truth for mass flow.
- [NASA thermal protection systems](https://www.nasa.gov/reference/jsc-thermal-protection-systems/)
  — entry heating is coupled to speed, pressure and heat-shield surface temperature. Visual
  red/orange emissive effects must remain gated by computed heat flux and material state, not
  by altitude alone.
- [NASA Glenn drag equation](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/falling-object-with-air-resistance/)
  — drag scales with `Cd · 1/2 ρ V² · A`. Entry tests should report dynamic pressure and
  drag separately from the visual plume/heat effect.

## Starship and Raptor evidence

- [SpaceX Starship User’s Guide, revision 1.0](https://www.spacex.com/media/starship_users_guide_v1.pdf)
  — public system-level architecture: two stages, sub-cooled methane/oxygen, payload
  accommodation and Earth/Moon/Mars mission intent. It is not a current Raptor 3 data sheet.
- [SpaceX Flight 7 report](https://www.spacex.com/launches/starship-flight-7) — public
  flight evidence includes all 33 engines at launch, 12/13 planned booster relights during
  boostback and 13/13 at landing burn. Use this to validate stage-aware engine telemetry,
  not to hard-code every future mission.
- [SpaceX Flight 8 report](https://www.spacex.com/launches/starship-flight-8) — public
  evidence for three-engine hot-stage shutdown, six Ship engines and partial booster relight.
- [SpaceX Flight 12 report](https://www.spacex.com/launches/starship-flight-12) — identifies
  the first V3/Raptor 3 flight and reports 33 Raptor 3 engines on Super Heavy, six on Ship,
  and an ascent engine-out. Detailed thrust, chamber pressure, mixture ratio and gimbal maps
  are not published there; those values must remain explicit game assumptions until a primary
  source exists.

## Implementation policy

1. Tag every physics constant as `published`, `measured-from-run`, `calibrated-approximation`
   or `hypothesis` in code/docs.
2. Preserve source URLs and retrieval date in research commits.
3. Never infer an exact Raptor 3 nozzle, thrust or throttle-minimum specification from a
   rendered screenshot or a press article. Expose those as scenario configuration and test
   the invariants (thrust/flow/Isp, stage counts, engine-out handling) instead.
4. A visual effect passes review only when its physical trigger is visible in telemetry and
   the same trigger is exercised by a deterministic test.
