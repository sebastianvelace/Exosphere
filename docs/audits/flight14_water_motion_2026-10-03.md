# Flight 14 water-only mass-centre motion — 2026-10-03

## Delivered scope

The exploration now observes ten seconds after its accepted physical water entry.
The ship can heel under calculated loads rather than rotate around an independently
translated skirt datum. This remains a bounded engineering estimate of a sealed
cylindrical hull. It does not reproduce flooding, flight termination, measured
capsize timing, recovery, or a source-backed splashdown location.

The earlier water-entry and inclined-water audits are historical evidence for the
one-second legacy preview. This increment supersedes that integration limitation,
not their stated geometry and geographic uncertainties.

## Physical integration

`Flight14LandingBurn` enables `WaterMotionEnabled` only after its existing contact
gate accepts the original ship: descending exposed nozzle, speed <= 5 m/s, upright
within 10 degrees, and inside the declared northern Pacific region. Shutdown still
uses delivered engine spool-down. There is no position, velocity, attitude, fuel or
clock snap at the handoff. Leaving the region blocks the observation.

The global coupled flag remains disabled. Only post-contact off-rails motion uses
`RigidBody6DofIntegrator`: position/velocity at the mass centre, quaternion attitude,
and body angular velocity advance together with RK4 and the inertia tensor's
Euler term. Engine/gimbal/resource/flap preparation runs once per physical step.
Gravity, prepared thrust, aerodynamic loads and water forces are evaluated from
the candidate state, not an attitude frozen at the start of the step.

`WaterMotionFrame` resolves actual part-graph mass and inertia, shifts the root
centre to the visible skirt datum, and includes `omega cross offset` in conversion
of point velocity. The public vessel datum remains the skirt so rendering/contact
witnesses remain compatible. Post-step proper acceleration and thermal/stress
inputs use mass-centre kinematics. Water velocity subtracts body translation and
rotation once and includes angular transport at each wet section.

The old submerged-area weighting understated the projected drag silhouette of a
shallow, nearly horizontal hull. A ten-second fixture then rebounded clear of the
water. Cross-flow drag now uses the clipped circular section's actual projected
width perpendicular to local flow. The pressure coefficient is still an estimated
constant; this is not a calibrated slamming/added-mass model. Axial nozzle drag and
clipped-section buoyancy retain their prior definitions.

## Presentation

Foam follows the projected wet hull segment and declared hull radius, including
side contact when the nozzle is dry. Spray uses the displaced centre and wet-section
relative motion. The sea retains the Compatibility depth correction and renders
before translucent exhaust/spray. Waves remain optical; physical contact uses the
calm geodetic mean plane.

## Evidence

- Free-motion conversion and integration tests conserve mass-centre velocity,
  world angular momentum and rotational energy over twenty seconds.
- Actual loaded V3 part properties update the mass centre when propellant changes.
- A production `Universe.Tick` sealed-hull fixture remains wet over ten seconds.
  Final COM position differences against 5 ms are 0.01173 m at 20 ms and 0.001921 m
  at 10 ms; the 20 ms angular-rate difference is 0.00001921 rad/s. All three end at
  approximately 89.77 degrees heel. These are seeded-fixture results, not observed
  Flight 14 timing or a full-mission endpoint.
- Independent horizontal clipped-section tests check projected widths for vertical
  and transverse flow at half and shallow immersion.
- The terminal controller fixture retains the original <= 5 m/s / <= 10 degree
  entry gate, then permits calculated side contact during the ten-second observer.
- The moving-solar-system mission regression retains the same loaded ship, 26
  payload deployments, physical contact and frozen terminal state. Its final gate
  checks wet hull/nozzle contact, finite water-motion telemetry and the entry
  witness; upright attitude is required at entry, not after ten seconds of heeling.

Focused evidence: `exports/flight14-water-motion-review/focused-tests.log` (36 cases).
A real Xvfb/OpenGL3 seeded terminal bench captured braking, pre-contact, and
water motion at approximately 1.04, 5.04 and 10.02 seconds. The final hull remains
wet (232.79 m3 displaced, lowest hull point -1.093 m) with a dry axial nozzle;
there is no rigid-floor hold. Captures were inspected for submergence, foam,
spray placement and calculated heeling. No shader/runtime errors occurred in the
completed capture. Files: `landing-0.png` through `landing-4.png`,
`terminal-render.console.log` and preserved `terminal-bench.cs.txt/.gd.txt` under
`exports/flight14-water-motion-review/`. The temporary helper and UID files were
removed from the source tree. No capture autoload was installed.

This bench is explicitly labelled **SEEDED TERMINAL BENCH / NOT FULL MISSION**.
It does not validate the full trajectory or the video's sea state/lighting. The
supplied terminal frames (`video_11326.jpg`, `video_11339.jpg`, where filename is
video playhead rather than mission elapsed time) show more pronounced irregular
surface waves and different illumination. Those visual differences remain open.

The first visual run was interrupted by Xvfb closure; its log is retained. The
first full CI was interrupted with exit 143 before reporting an xUnit result;
`interrupted-full-ci.log` is retained and is not counted as a pass. Complete CI
was repeated with periodic progress output; its final result is recorded below.

The repeated `bash tools/ci_check.sh` completed with exit 0 in 1346 seconds.
All 1031 xUnit cases passed (0 failed, 0 skipped; test duration 21 m 58 s),
including the loaded moving-solar-system exploration regression. The simulation,
game and trajectory-probe builds reported 0 warnings and 0 errors. Contract
checks, the asynchronous flight-startup gate, Godot menu/construction smoke and
the graphics-1280 menu check also passed. Evidence: `full-ci.log` in the same
review directory. This is test and software-renderer evidence, not physical-GPU
performance certification or acceptance of the video's sea-state fidelity.

## Remaining limits

The 25..35 N / 165..140 W box is an engineering open-water context, not a measured
SpaceX target. The cylindrical sealed volume approximates nose, skirt and flaps.
Physical waves, flooding, venting, flight termination, slamming/added mass, measured
post-contact trajectory and independent booster recovery remain pending. Current
terminal flip altitude and mission clock are estimates; no timing or lighting snap
is introduced to imitate the supplied video. The observer ends after ten seconds
without asserting that the floating hull has reached equilibrium.
