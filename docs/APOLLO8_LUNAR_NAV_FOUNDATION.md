# Apollo 8 — lunar navigation foundation

## Scope

This increment closes the simulation-layer prerequisite for Apollo 8. It does
not yet add Saturn V, CSM-103, crew, mission objectives or the map interaction.
It provides one authoritative, Godot-free route from an Earth parking orbit to
a physically continuous lunar SOI encounter:

1. sample the moving Moon ephemeris at arrival;
2. search one parking-orbit burn window;
3. solve the Earth-centred, zero-revolution Lambert boundary problem;
4. aim off Moon centre in the approach B-plane;
5. propagate to lunar SOI entry;
6. reframe the entry state as a Moon-centred hyperbola;
7. reject surface impacts and estimate the lunar insertion burn.

No state is teleported. The acceptance test installs the selected post-TLI state
in `Universe`, advances at maximum rails warp and observes the ordinary
Earth→Moon SOI transition.

## Historical calibration

The timing band is anchored to the Apollo 8 Mission Report:

- TLI ignition: mission elapsed time `02:50:37.1`;
- TLI cutoff: `02:55:55.5`;
- LOI ignition: `69:08:20.4`;
- Earth parking insertion altitude: `103.3 nmi`;
- lunar approach/perilunium class: approximately `60–70 nmi` altitude.

The regression therefore uses a `66 h 17 min 43.3 s` TLI-to-LOI coast and
accepts:

- TLI magnitude: `2.8–3.5 km/s`;
- SOI entry: `2.0–3.5 days` after TLI;
- predicted lunar perilunium: `40–300 km` altitude;
- one-burn circular lunar insertion estimate: `0.7–1.5 km/s`.

The insertion value is an engineering acceptance envelope, not a claim that
Apollo 8 performed one circularizing burn. Apollo 8 used LOI-1 followed by a
small circularization maneuver. Those burns will be modeled explicitly in the
mission increment.

Primary source:

- NASA, *Apollo 8 Mission Report*, MSC-PA-R-69-1 (February 1969):
  <https://ntrs.nasa.gov/citations/19700033031>

## Model boundary and provenance

`LambertSolver` is a universal-variable two-body solver using Stumpff functions.
`LunarTransferPlanner` is an ephemeris-targeted patched-conic planner. Lunar
gravity is introduced at the SOI boundary; its focusing is included when
computing perilunium and LOI energy.

The current `data/bodies/moon.json` is an offline mean-element ephemeris whose
epoch is still generic (`epoch = 0`). Consequently this is an
**engineering/derived Apollo-class calibration**, not a reconstruction of the
Moon's inertial state on 21 December 1968. A dated Horizons/SPICE state with an
explicit frame and timescale remains required before claiming historical
trajectory reconstruction.

The old audit target of `5.8–6.2 km/s total` mixed unspecified transfer phases
and was not traceable to the Apollo 8 report. Acceptance now keeps TLI and lunar
insertion separate so every comparison has a defined physical boundary.

## Regression coverage

`LunarTransferPlannerTests` verifies:

- Lambert propagation closes at the requested terminal position and velocity;
- inputs are finite and physically valid;
- the Apollo-class plan falls inside the timing and Δv envelopes;
- the B-plane aim radius accounts for lunar gravitational focusing;
- the predicted Moon-centred conic does not intersect the surface;
- maximum-warp propagation reaches the lunar SOI through `Universe` and
  re-references the vessel to `moon` without destruction.

## Product integration

The map now routes `moon` to `LunarTransferPlanner`, labels TLI and LOI
separately, centres the estimated finite TLI burn on its absolute impulsive
epoch and draws the Earth–Moon conic, lunar SOI and focused perilunium. Manual
Δv adjustment calls the same pure
`AnalyzeEncounter` contract used by tests, including miss and impact states.

Framebuffer acceptance:

```bash
bash tools/visual_playtest.sh --lunar-map
```

The accepted 200 km, ephemeris-plane case produced TLI `3.180 km/s`, LOI
estimate `0.959 km/s`, coast `2.76 d` and perilunium `115 km`. The map rejects
one-orbit solutions above `4.5 km/s` rather than presenting a datum/plane
mismatch as a useful transfer.

Next: create an executable LOI node, expand the search beyond one parking orbit,
replace the generic epoch with dated Horizons/SPICE state, then let Apollo 8
hardware and mission data consume the same contracts.
