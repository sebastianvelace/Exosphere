# Flight 14 plasma and x200 exploration

## Scope

The user requested x200 acceleration and a plasma presentation closer to the
already supplied broadcast references. The integrated graphics preset remains
supported; force, atmosphere, thermal damage, TPS integrity and guidance are unchanged.

## Reference comparison

The supplied gameplay image at T+03:00:08 shows a thin orange plate detached from
the ship, separate rods near flaps, and a largely flat red shield. The inspected
Flight 14 frames at T+02:44:44 (`exports/flight14-cloud-light-review/reference/video_09900.jpg`)
and T+02:52:49 (`docs/research/flight14_descent_browser_captures_2026-10-02/video_10384.jpg`)
show a darker vehicle silhouetted against a bright white/violet/rose surrounding
sheath. These are onboard views, not matched external camera geometry. Colour is
an appearance calibration; exposure, spectrum and temperature cannot be recovered
from these pixels. The late terminal-burn frame is not used as a plasma reference.

## Changes

- The Starship bow sheath reuses the rendered barrel and tangent-ogive geometry,
  with a small normal offset and airflow-facing mask. The old displaced disc remains
  only as a fallback for other geometry. Hull synchronization still occurs each
  render frame; material/thermal queries retain the existing 20 Hz presentation cadence.
- A faint outer sheath adds depth with one shared mesh/draw, without volumetric
  raymarching. Four flap glow proxies share the actual articulated blade meshes and
  their live transforms; the separate nose/barrel rods are hidden for this hull.
  Ordinary opaque depth continues to occlude the optical effects.
- The calibrated line-emission palette is violet/rose with a brighter pale core.
  Shield incandescence stays a separate warm thermal cue with a reduced presentation
  floor/gain so the entire shield does not become an orange plate. None of these
  gains change simulated heat flux, temperature or survival.
- x200 is inserted between x100 and x1000. Existing historical autopilot indices
  are shifted to preserve their previous rates; saves restore by stored rate.
  Flight 14 still advances exclusively in whole 20 ms physical/control steps.
  The bounded per-frame budget increases from 50 to 100 steps (2 simulated seconds);
  overloaded hardware runs slower than the requested multiplier, without a backlog,
  enlarged integration step or rail promotion. The two-second frame budget also
  limits achieved acceleration below 100 render frames/s; x200 is a requested
  rate, not a guarantee of wall-clock throughput.

## Verification

Controlled real-framebuffer OpenGL captures in the Integrated preset were inspected
at three camera angles, before and after, at 60 km and 4.8 km/s atmosphere-relative
speed. Both samples report 171376 W/m2 physical heat flux and 0.663727 visual
intensity. The separate orange disc/rods are absent; the shield remains readable
through the violet/rose sheath, and leeward geometry occludes the windward effect.
These seeded, paused presentation fixtures are not continuous mission acceptance
or matched onboard optics. Artifacts and comparison: `exports/flight14-plasma-warp-review/`.

The managed plasma orientation/cadence regression and the performance/physical-input
contract passed. A rate audit verified that all 50 existing historical warp requests
retain their previous multipliers and save restoration remains rate-based.

The continuous clicked-menu gameplay run at requested x200 passed to
T+10808.84 s, altitude 59440.259 m and mass 191120.028 kg: all 26 payloads,
28 physical vessels/renderers, original ship identity and no control block. Pause
froze the committed state. Intermediate rendering was disabled on this long
software-renderer run, then enabled for the final entry capture; this is physical
continuity evidence, not continuously rendered mission/reference acceptance.
Artifacts: `/tmp/flight14-plasma-live-entry/` and the review folder above.

`tools/ci_check.sh` passed with 995/995 xUnit tests (including x200 whole-step
parity at 120/240 render Hz, pause/CPU bounds and the complete loaded moving-Sun/Earth
mission to the diagnostic boundary), all three builds with zero warnings/errors,
flight startup and graphics-menu checks. The simulation library is the tested revision.
A subsequent game-only fallback guard restores the sphere proxy when switching away
from a Starship renderer. It was compiled with zero warnings/errors. The final
shader-only halo feather/core-white refinement was separately reviewed at all three
paused-fixture angles after the continuous entry capture; that earlier capture must
not be presented as proof of the final colour calibration. Temporary helper source/UID files were removed, `project.godot` is unchanged,
and the cleaned final game build passed with zero warnings/errors. Its 60-frame
headless Flight scene smoke exited successfully with no script/runtime errors.

No target GPU performance or proprietary plasma/CFD validation is claimed. The
external camera differs from onboard broadcast exposure and framing.
