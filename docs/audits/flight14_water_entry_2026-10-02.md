# Flight 14: bounded terminal burn and water entry — 2026-10-02

## Result and acceptance boundary

The playable exploration continues the original launch, 26 payload releases and return
through the retained 100 m diagnostic to actual exposed-nozzle water contact. It commands
shutdown at contact and integrates one second of water response before freezing. This
is estimated initial water entry, not stable flotation, recovered hardware or full mission
acceptance. Exact targeting, flooding, capsize and independent booster recovery remain open.
The standalone powered-return diagnostic still defaults to the older 100 m boundary.

## Reference comparison

Sources: [SpaceX Flight 14](https://www.spacex.com/launches/starship-flight-14) and the
[user-supplied mission video](https://youtu.be/lw0nj_XiUoU). Existing extracted frames:

- `exports/flight14-cloud-light-review/reference/video_11313.jpg`: visible T+03:08:17,
  approximately 0.6 km and 327 km/h; a short, intense terminal burn while upright.
- `video_11326.jpg`: T+03:08:30, approximately 0.1 km and 13 km/h; dark water,
  an orange exhaust footprint and a broad pale spray/steam fan.
- `video_11336.jpg`: T+03:08:40; bright post-contact effects and water reflections.
  These are not automatically reentry plasma; no post-splash FTS event is implemented.

Frame filenames are video playhead seconds, not mission elapsed time. Current flip
initiation remains an estimated 1.2 km, not the approximately 0.6 km reference gate.
Remaining tank resources, engine starts, throttle floors and delivered thrust constrain
the trajectory; reference clocks do not move the ship or refill tanks. The declared
25..35 N, 165..140 W ocean envelope is an engineering constraint, not measured SpaceX
coordinates. Simulated lighting follows the existing solar epoch, not a forced dusk filter.
Weather, target location and exact burn timing still need reference calibration.

## Physics

`WaterContactSolver` is explicitly opt-in and Earth-region-gated. It uses the renderer's
skirt datum and -3.78 m exposed nozzle, an upright sealed cylinder, seawater density
1025 kg/m³ and estimated Cd 0.8. Archimedes support acts at the submerged-volume centre;
quadratic drag opposes point velocity relative to translating, rotating water. Geometry
and the centre-of-mass lever are derived at landing handoff. This is not distributed
hydrodynamics or slamming CFD. The legacy translating-body RK4 evaluates water force at
translation stages; the existing angular tick integrates torque. Coupled 6-DoF water
continuation is rejected until parity is demonstrated.

Contact requires descent, point speed <=5 m/s and attitude within 10 degrees upright.
The 100 m witness remains recorded. Below it lateral correction tapers toward upright;
engine selection falls monotonically from three sea-level Raptors to one, with actual
spool-down after shutdown. Wet contact bypasses only the generic rigid-floor clamp:
no position/attitude snap, grounding or replacement vessel is introduced.

A longer three-second observation produced attitude outside the upright model's useful
domain. The supported observation is therefore one second. Post-cutoff vertical speed
can increase while the exposed nozzle enters before the sealed skirt displaces water;
a successful contact witness does not assert stationary support or recovery.

## Rendering

The scene adds optical water, bounded CPU spray, foam and delivered-thrust-driven
exhaust reflection. The ocean follows mean planetary curvature; ripple normals do not
move the physical contact plane. Per-instance engine telemetry now drives each Starship
plume, avoiding sixfold dimming of a one-engine burn by the installed-engine average.
The chase camera remains above mean water. Launch-site procedural coast detail is
anchored to its actual site rather than reappearing under a ship across the Pacific.

## Validation

- Full `tools/ci_check.sh`: **1000 passed**, zero failed/skipped; builds zero warnings/errors;
  startup, graphics settings and scene smoke checks passed. This ran before the final
  one-second observation and ocean presentation refinements.
- Latest focused water/landing tests: **5 passed** after the observation change.
- Latest `MovingSolarSystemPreviewPropagatesLoadedShipToWaterEntryAndFreezes`: **passed**
  in 14 m 45 s. Loads the moving solar system and real ship, retains its identity,
  releases 26 payloads, reaches wet contact without grounding, retains <15-degree
  upright attitude, and verifies the terminal state freezes.
- Real menu-to-water integration before final presentation refinements: **passed**;
  contact T+11412.82 s, datum altitude 3.48 m, same 159073 kg ship, 26 releases.
  Its final image had depth artifacts and is not visual acceptance evidence.
- Seeded terminal captures under Xvfb/OpenGL3 exercise the same landing burn and water
  integration through the real bridge. They seed a 1200 m handoff and tank resources;
  they do **not** prove full mission progress. The original mission HUD in those captures
  still shows its independent ascent state. Numerical telemetry is in capture logs.

## Ocean depth finding

A small plane initially showed wave-trough holes and a hard boundary. A curved radial
mesh covers up to 25 km with concentrated inner triangles, shared analytic geographic
depth and a metre-scale decal bias. Depth writing must apply in the transparent pass:
writing ALPHA selects that pass, so depth_draw_opaque does not preserve the ocean in
front of the regional surface overlay. This follows the [Godot 4.6 spatial shader
contract](https://docs.godotengine.org/en/4.6/tutorials/shaders/shader_reference/spatial_shader.html).
The final material uses depth_draw_always and discards nearly invisible horizon-edge
fragments. It draws before translucent spray/plumes so the large transparent sea does
not overpaint those effects merely because their object origins sort closer. This keeps vessel depth testing active; it does not paint water over the ship.
CPU spray uses the local radial frame, so cosmetic gravity follows the sea normal.
Particle-aware, two-sided billboards preserve particle scale and avoid losing soft
sprites through face culling in the east/up/north surface frame.

## Final continuous game route

The second menu-to-terminal run passed with the latest one-second physical boundary:
`/tmp/flight14-water-final-live/flight14-launch-1280.log`. It clicked the real exploration
menu, requested x200, preserved the original ship, released all 26 payloads and reached
`SplashdownReached` at T+11413.72 s with no block and no grounding. The recorded entry
witness has 2.31 m/s atmosphere-relative datum speed. The terminal state retains the
same 159073.01 kg ship, wet-nozzle clearance -5.13 m and approximately 1.98 MN water load.
Sparse rendering accelerates intermediate captures, not the physical propagation.
This process started before the last transparent-water depth/spray presentation fixes;
its screenshot proves the real water-entry UI route, not the final ocean appearance.

Final ocean/plume/spray presentation is reviewed separately in labelled seeded bench
captures, with continuous rendering below 100 m so optical particles develop before
pausing. No weather/lighting match or complete post-splash recovery is claimed.


Reviewed final presentation: `exports/flight14-landing-review/terminal-burn-bench.png`
and `initial-water-entry-bench.png`, labelled as seeded terminal tests. Burn spray is
a low translucent fan; contact removes the flame footprint while retaining fading
water spray. The retained logs and `.txt` fixture sources are in that ignored export
folder. Bench display frames may advance slightly past the one-second gate; the real
exploration stops its physical batch immediately on the terminal phase.

Remaining visual gaps include fine horizon aliasing, stylized spray billboards and
lighting/weather differences from the overcast reference. These captures confirm the
new water/effect mechanisms, not exact Flight 14 visual reconstruction or GPU
performance certification. Temporary fixture sources were removed from game sources.
