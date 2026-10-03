# Flight 14 inclined-water load audit — 2026-10-03

## Scope

Replace the upright-only displacement approximation before attempting a longer
post-contact phase. The playable exploration still freezes after one second of
water response. This increment does not assert capsize, flooding, exact ocean
coordinates or full mission recovery.

## Physical changes

`WaterContactSolver` clips each circular cross-section of a sealed cylinder against
the local geodetic sea plane. The hull uses the existing skirt datum, radius, length
and mass-centre lever; the exposed nozzle remains a separate contact witness.
For circle radius R and clipping coordinate h (water occupies z <= h), partial
section area and vertical first moment are:

- A = R²(π/2 + asin(h/R)) + h sqrt(R² − h²).
- Mz = −(2/3)(R² − h²)^(3/2).

Dry and fully submerged sections use exact zero/full area. Axial quadrature is
split at their transition boundaries; sixteen-point Gauss-Legendre integration
then yields displaced volume and its centroid without dividing by upright alignment.
This removes the previous artificial minimum alignment of 0.1, which made
horizontal/inverted support incorrect.

Buoyancy acts upward at the displaced centroid. Sectional cross-flow drag uses a
projected side strip weighted by the wet section fraction and the local relative
velocity, including angular transport. Axial nozzle drag retains the existing
wetting-depth ramp. Every drag element opposes its own velocity. The resulting
force and torque about the mass centre therefore satisfy nonpositive drag power
F·v_COM + τ_COM·ω for still water. This is a strip estimate, not CFD or measured
Starship drag calibration; axial spin skin friction is not modeled.

The wet-contact rigid-floor bypass now also recognizes the hull's lowest support.
A horizontal wet cylinder can therefore receive water loads while its axial nozzle
is above the sea. The mean plane is a local tangent approximation across the hull;
optical waves do not move the physical waterline.

## Visual changes

The local ocean frame now uses a right-handed east/up/south basis. Contact foam and
spray follow the displaced centroid projected onto the sea; before hull displacement,
the fan follows the nozzle projection. The ocean remains driven by delivered thrust
and physical contact, without a timed splash animation or attitude snap.

Reference reviewed: `exports/flight14-cloud-light-review/reference/video_11326.jpg`,
showing broadcast T+03:08:30, 0.1 km, 13 km/h, dark water and a pale fan around the
terminal flame. The current seeded bench checks water/foam placement; its weather,
lighting and camera do not reproduce that reference exactly.

## Acceptance and limits

Focused analytic tests cover dry/upright entry, half-submerged horizontal volume
and centroid, full volume at six inclinations, complementary cuts, dissipative
translation/pitch loads, analytic oblique first moments, invariance under a translating
body frame and zero drag for a hull corotating with prograde/retrograde water. The existing
20 ms terminal-burn/contact bench remains a regression gate.

The force geometry can now be evaluated at arbitrary inclination. This does not
make the production legacy trajectory a validated capsize simulation. Its angular
step and skirt-datum translation are separate, rather than a jointly propagated
mass-centre rigid body. The next physical increment must conserve mass-centre motion
when orientation changes and verify time-step convergence before extending the
observation window. There is also a datum conversion to audit before reusing the coupled path:
`PartGraph.ComputePartLocalPositions()` places the root part centre at zero, while
`VesselRenderer` shifts visible parts by minus their minimum local Y. Water's
mass-centre lever already subtracts that bottom coordinate. The coupled integrator
currently takes `Parts.CenterOfMass` directly. A water integration adapter must
explicitly map the visible skirt datum to the part-graph root and mass centre rather
than mixing these offsets or simply enabling the global coupled flag.

Required next gates are: one shared datum conversion, conserved free rotation
about the mass centre, water drag power in a rotating frame, timestep convergence
at 20/10/5 ms, and continuity across the wet-contact handoff with finite engine
spool-down. Only then should the production observation window grow. Flooding
would additionally need open-volume geometry, water ingress and evolving
mass/inertia data; none are invented here.

## Render evidence

The local ocean generated a black stippled row at the horizon. A frozen-state A/B
capture removes that row when the local ocean mesh is hidden, isolating the local
water/underlying geographic depth competition. The original 2.8 m depth decal
bias became insufficient at grazing range. Compatibility rendering now additionally
uses a two-step 24-bit depth offset toward the camera, fading in from 1 to 5 km.
The contact plane and physical geometry are unchanged. Forward+/Mobile retain the
existing physical depth bias. Godot's [reverse-Z depth convention](https://docs.godotengine.org/en/4.6/tutorials/shaders/advanced_postprocessing.html)
places the near plane at 1, so the correction adds depth rather than subtracting it.

Real-framebuffer OpenGL3 captures in
`exports/flight14-inclined-water-review/landing-2.png` and `landing-3.png` remove the
stippled horizon during terminal burn and wet contact. `before-depth-landing-2.png`
and `before-depth-landing-3.png` retain the previous artifact;
`before-depth-landing-4.png` is the frozen-state ocean-hidden control. No shader or
renderer error occurred in the captured runs. These are labeled seeded benches,
not loaded-menu mission or physical GPU performance acceptance.

The final depth review uses x5 below 100 m to avoid long software-renderer waits.
Its fixture has a standalone landing controller, so display batches can overshoot
the one-second terminal gate. The final contact screenshot is presentation evidence,
not an exact one-second production witness. The pure 20 ms regression and loaded
exploration test establish the production gate separately. This lighting/weather
still differs from the reference; optical wave silhouette/sea-state forces remain
pending.

## Completed validation

- `bash tools/ci_check.sh`: exit 0; 1,018 xUnit tests passed in 23 min 42 s,
  simulation/game/probe builds had zero warnings/errors. Flight startup reached
  60 frames; main menu, construction smoke and graphics menu checks passed.
  This suite includes the loaded moving-Solar-System exploration through all
  payload releases to bounded water entry, plus the original 100 m diagnostic.
- Five additional analytic/frame cases were added while the long suite was running.
  The final focused water/terminal regression ran 27 cases, all passing. Physics
  implementation did not change between the full and supplementary runs.
- Clean game build after removal of temporary capture helpers: zero warnings/errors.
- Real OpenGL3/Xvfb terminal benchmarks reached `SplashdownReached`; final water shader
  compiled without shader/renderer errors, and inspected burn/contact images confirm
  the corrected horizon and localized spray. These seeded visual runs do not assert
  the exact loaded-flight contact state or reference weather match.

Logs, before/after PNGs and reproducible `.cs.txt`/`.gd.txt` fixtures are retained in
ignored `exports/flight14-inclined-water-review/`. No capture helper, autoload,
generated UID or export artifact is committed.
