# Atmosphere and reentry: visual changes and physics audit

Date: 2026-09-30. Scope: shared Starship presentation, entry guidance and automated
production gameplay. Existing launch improvements are preserved. This is an engineering
simulator audit, not a reconstruction of measured Flight 14 entry telemetry.

## Visible changes

- **Cloud continuity:** replace diagonal, two-corner value-noise interpolation with
  continuous eight-corner trilinear interpolation. The weather map, cloud shell and
  view/sun step budgets remain unchanged.
- **Cloud distance:** extinguish cloud radiance through the molecular/aerosol column
  between observer and cloud samples. Four bounded foreground samples supplement the
  existing cloud integration. This removes a missing foreground transmission factor;
  it does not change the simulated air density.
- **Horizon:** measure the existing daylight dome from the spherical ground tangent,
  rather than clamping every downward sky ray to local elevation zero. The former
  constant-colour interval was a visible stripe at 10 km. The surface limit and the
  atmospheric scattering integral are preserved. This dome is still an artistic floor.
- **Plasma:** stretch the windward cap and downstream wake to the renderer's actual
  Starship hull span. Advected noise, feathered masks and separate wake/edge gains
  replace the small central glow and opaque material cues. Flap glows follow the
  live flap transforms. Animation uses simulation time and deterministic flicker.
- **Visibility:** scale and place the complete attitude instrument group to leave the
  ship visible in small external-entry viewports. Keep the thermal and descent readouts.

Plasma reads the vessel's attitude-dependent stagnation flux and atmosphere-relative
velocity. It samples thermal presentation at 20 Hz; poses still follow each rendered
frame. Its colour, optical gain and thickness are display proxies, not measured plasma
temperature, shock standoff distance or CFD. Additive unshaded radiance uses `ALBEDO`:
Godot's Compatibility unshaded path ignores `EMISSION`. Fog is disabled for this source
to avoid treating luminous plasma as reflected fog-coloured material.

## Findings corrected during gameplay

1. A valid deorbit from the observed ~150 km parking orbit needs **28.3 m/s**. Four
   autopilot decisions incorrectly required a retrograde component below -50 m/s.
   The planner now exposes explicit deorbit intent; a fresh manual node clears that
   intent. The harness verifies the computed post-burn ellipse enters the atmosphere
   while its periapsis stays above the surface. No artificial extra burn is added.
2. The full harness used 720 seconds of wall time for ascent and entry despite its
   longer declared wall budget. On software rendering it stopped a physically
   progressing ascent around 136 simulation seconds. Mission deadlines now use
   elapsed simulation time relative to ascent/deorbit arming; the wall watchdog remains.
3. Entry guidance recomputed a complete long-horizon footprint at every 20 ms control
   epoch. Above 25 km, the forecast is held for at most **0.2 simulation seconds**;
   context changes, time rewind and a >2 km body-relative displacement invalidate it.
   Below 25 km every control epoch refreshes it. Force integration, flap servos and
   attitude control keep their original cadence. E2E evidence records 46,776 control
   updates versus 4,678 forecasts at high altitude, with `guidanceDt=0.020000`.
4. Capture heat telemetry used the thermal helper's default 1 m radius, disagreeing
   with the production attitude-dependent radius. Capture telemetry and the comms
   blackout input now read `Vessel.ComputeStagnationHeatFlux`, like thermal damage,
   plasma presentation and entry diagnostics. The interface fixture also labels
   7,600 m/s correctly as **body-centred inertial speed**, separately from airspeed.

## End-to-end evidence

The `--flight12` V3 preset was flown continuously through pad, powered ascent,
hot-staging, stable orbit, map-planned deorbit, atmospheric entry, aero descent,
physical flip, powered descent and the `LANDED` mission event. This is the existing
V3 engineering preset, not an independently calibrated Flight 14 vehicle.

The complete dynamics run used the headless renderer with a fixed 30 Hz render clock
to make software rendering practical. It produced telemetry, **not acceptance PNGs**.
Its final visual gate correctly failed for missing real PNGs; `SUMMARY reason=LANDED`
must not be reported as a passing full visual test. A separate real-framebuffer baseline
captured ascent until the former wall-clock deadline. Entry and atmosphere images were
then captured independently under Xvfb using the production flight scene/controllers.
This was automated gameplay through production APIs, not a manual keyboard/UI playthrough.

| Continuous dynamics measurement | Result |
| --- | ---: |
| Parking-orbit periapsis | 149,214 m |
| Planned deorbit | 28.3 m/s |
| Propellant at deorbit arming | 209,154 kg |
| Peak entry dynamic pressure | 33,319 Pa |
| Peak production stagnation heat flux | 596,807 W/m² |
| Peak aerodynamic load | 6.633 g |
| Minimum sampled entry windward factor | 0.8895 |
| Post-setup direct attitude assignments | 0 |
| Orbit-to-entry teleport / fuel reseed | false / false |

**Landing limitation:** the late touchdown capture records `landingParts=0`,
`contacts=0`, `settled=False`, altitude 0.9 m and downward speed 0.9 m/s. The phase
transition used the existing low-speed aggregate surface-support boundary; the capture
arrived after its initial settled event. It proves arrival at the mission event, not
stable multi-point support or a successful tower catch. The rendered catch corridor,
persistent final settling and an appropriate no-legs landing policy remain open.

## Physics boundaries and remaining improvements

- Density comes from the existing standard-atmosphere/thermosphere model; drag and lift
  use rotating-atmosphere relative velocity. Inertial speed remains the curvature frame.
- Heating is an engineering stagnation correlation with effective orientation-dependent
  radius; the TPS has two thermal nodes. These are useful causal approximations, not
  a resolved spatial heat-load distribution, shock chemistry or ablation simulation.
- Existing aggregate flap aerodynamics and the default legacy integrator remain active.
  The experimental coupled 6-DoF switch remains off. No mass, Cd/Cl, force or thermal
  coefficient was retuned to manufacture the visual result.
- The high-altitude footprint now has bounded forecast latency; its optimality against
  a continuously refreshed closed-loop solution is not certified by this audit.
- The horizon stripe and cloud discontinuities improve, but cloud silhouettes, distant
  terrain/haze transitions, large solar glare and spectral limb colour still need work.
  Plasma is more coherent with the hull but remains a mesh-based optical proxy.
- Initial entry-control traces show repeated flap saturation. This deserves a separate
  authority/gain audit against the physical entry envelope before tuning the controller.

## Validation and artifacts

- Simulation suite: **902 passed, zero failed**; atmosphere quick check: **82 passed**.
- Simulation/game builds: zero warnings and errors.
- Atmosphere low-altitude/prefilter and phase-2/3 contracts passed.
- Plasma performance and managed orientation/sample-cadence regressions passed.
- Playtest contract fixtures and actual planner/physics deadline regression passed.
- Real 1280×720 Compatibility atmosphere case: `ATMOSPHERE_LOW_OK`, correct 10 km
  geometry, paused clock and settled exposure; inspected before/after images.
- Independent production-scene entry captures inspect the plasma in Compatibility
  and Forward Plus. These start at an explicit physical interface and are separate
  from the continuous ascent-to-entry evidence.

Local evidence: `exports/atmosphere-entry-review/` (ignored captures, logs, comparison
page and reproduction notes). Tests and the audit are tracked source; temporary capture
helpers/autoloads are removed from the production checkout after capture.

Reproduce the standard atmosphere case:

```bash
bash tools/visual_playtest.sh --atmosphere-low --resolution 1280x720 \
  --run-id atmosphere-entry-review
bash tools/atmosphere_quick_check.sh
python3 tools/tests/playtest_physics_gate_regression.py
```

For a full rendered mission, use `bash tools/visual_playtest.sh --flight12 --run-id
entry-full-rendered --resolution 1280x720`. This was not completed on the software
renderer in this audit; inspect its telemetry and PNGs separately before accepting it.

Primary references: [physics model](../physics/PHYSICS_MODEL.md),
[entry source register](../research/REENTRY_PHYSICS_SOURCES_2026-09-25.md),
[NASA Sutton–Graves engineering stagnation correlation](https://ntrs.nasa.gov/citations/19720003329),
[Godot spatial shader contract](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html),
[Godot 4.6 Compatibility unshaded source](https://github.com/godotengine/godot/blob/4.6-stable/drivers/gles3/shaders/scene.glsl).
