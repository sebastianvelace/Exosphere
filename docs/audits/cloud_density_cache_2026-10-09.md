# Near-pad cloud density cache and Flight 14 follow-up

## Problem and scope

The initial Flight 14 cloud crossing slowed down even on a recipient's dedicated
GPU. The recipient's adapter was not profiled, so the measurements here establish
local behavior only. Native X11 rendering on AMD Radeon Graphics (RADV RENOIR)
identified expensive procedural cloud density/shadow evaluation in the opaque
terrain transport. Paused hull-occlusion A/B did not explain the dominant cost.

This change reuses density samples across sky, terrain and hull transport. It does
not modify the physical atmosphere, aerodynamic forces, heating, guidance, clock,
integration steps or vessel resources.

## Implementation and bounds

`CloudDensityCache` incrementally renders a double-buffered 32-layer atlas of the
existing shared procedural density. One slice is submitted per rendered frame;
`FramePostDraw` confirms submission before a completed volume becomes visible.
There is no GPU readback or per-frame CPU density construction. A 32 km square
around Starbase uses 256 samples per horizontal axis and 32 altitude samples.
The two 4096 x 512 RGBA8 pixel stores nominally require 16 MiB; renderer
intermediates require additional memory. A larger 512-side experiment exceeded
this integrated GPU's allocation budget and was discarded.

Alpha stores linear density, avoiding RGB color-space conversion. Trilinear
sampling introduces a presentation approximation: approximately 125 m horizontal
spacing and 45 m vertical spacing for the current weather profile. Rays outside
the volume use the original procedural field. Refreshes begin after four metres
of estimated horizontal advection; stale volumes exceeding half a grid cell of
advection fall back to the procedural field. Above 6 km, new local bakes are
suspended. Front-buffer metadata is retained independently of a pending bake.
Cloud shape, extinction, lighting quadrature and weather parameters are unchanged.

## Reproducible evidence

Use `tools/perf/cloud_traversal_probe.gd` through the real Flight 14 menu route:

```sh
EXOSPHERE_GRAPHICS_PRESET=quality CAPTURE_MENU_MODAL=flight14launch \
CAPTURE_FLIGHT_HUD=1 CLOUD_PROFILE_OUT=/tmp/cloud-profile \
"$GODOT_BIN" --path . --display-driver x11 --resolution 1280x720 \
  --rendering-method forward_plus --rendering-driver vulkan \
  --script res://tools/perf/cloud_traversal_probe.gd
python3 tools/perf/check_cloud_profile.py /tmp/cloud-profile
```

Use the actual target GPU. Xvfb on this machine selects llvmpipe for Vulkan and
is not suitable for hardware performance claims. The probe flies naturally to
1.75 km, pauses physics, warms each mode for 40 frames and collects 40 frames
without PNG readback in the timed loop. It verifies the actual material transport
switch and identical MET/mass. The validator requires four identical physical
snapshots, valid GPU timing samples and bounded differences in the cloud region.

At the same paused epoch (MET 28.96 s, altitude 1780.79 m), 1280 x 720 Quality:

| Transport | Median GPU ms | Median frame ms | Frame p95 ms |
|---|---:|---:|---:|
| Procedural | 594.21 | 607.71 | 1206.96 |
| Cached | 134.37 | 142.03 | 155.23 |
| Cached repeat | 135.94 | 140.68 | 150.88 |
| Cached, hull overlay off | 132.82 | 139.19 | 158.51 |

The median GPU improvement is 4.42x for this fixture. Full-frame mean RGB byte
differences are 0.66/0.58/0.53; the central cloud-region validator also passes.
Reviewed captures retain coherent foreground hiding of the lower stack. The cloud
occlusion fixture now waits for atmospheric LUT upload and 64 rendered warm-up
frames, preventing a lighting-path change or incomplete volume between its A/B
captures. The first fixture failed its unchanged-background guard when the LUT
completed between captures; the tolerance was not relaxed. The settled six-view Forward+/Integrated run passes
with a 69.1% reduction of visible hull-feature contrast inside the cloud, while
retaining the unchanged-background guard. These
image bounds test a regression against the existing appearance, not atmospheric
or reference-video truth. OpenGL/Integrated also passes the same paused-state and
image checks, with no shader errors. Its timings overlap the CPU test suite and
are not used as a controlled performance comparison.

Remaining costs include procedural distant clouds, light transport and the
underlying atmosphere/terrain passes. This is not a 30/60 FPS guarantee, and
there is no measurement yet on the recipient's dedicated GPU.

## End-to-end game validation

A temporary SceneTree harness launched **Explore Flight 14** from the real menu
on the native Vulkan framebuffer, flew naturally through the cloud layer,
observed Super Heavy after separation, and continued the same Ship identity.
After all 26 releases, it exercised **SKIP TO ENTRY**, **CANCEL SKIP** (MET stayed
fixed while paused), then restarted the shortcut. It paused at 119997.11 m and
resumed through atmospheric entry, hypersonic descent, powered flip and water
response. Super Heavy reached `ContactObserved` with no blocker. All 28 vessels
retained their renderers at the ship terminal boundary.

The Ship reached `SplashdownReached` at MET 11422.72 s with a water-entry witness
of 2.31092 m/s. The ten-second water response completed and terminal MET remained
fixed. **RESTART** created a new Ship with zero payloads and **MENU** returned to
the main menu. The run emitted `E2E_OK` and exited 0 without engine errors.
Final `tools/ci_check.sh` passes: 1059 simulation tests, zero failures/skips,
clean builds, asynchronous flight startup, Godot menu/VAB smoke and graphics-menu
validation. The amended visual harness contract also passes. Temporary capture
code and autoloads were removed, followed by a clean production game rebuild.

This validates playable engineering behavior, not exact measured Flight 14
trajectory or the missing water physics described below.

## Physics priorities after the playthrough

1. **Controlled whole-mission 6-DoF verification.** The global coupled integrator
   is still disabled. Existing coast/short powered parity is insufficient for a
   long controlled ascent and entry. Compare the same mission at 20/10/5 ms,
   with phase transitions, resources, actuator commands, attitude and angular
   momentum recorded. Hold the guidance/actuator sampling contract constant while
   changing the force-integration step; also compare different render/warp schedules
   at identical physical epochs. Define tolerances before considering activation.
2. **Aerodynamic calibration with uncertainty.** The live model has generic
   geometry-dependent drag and `CL = 0.7 sin(2 alpha)` with aggregate flap response.
   Replace assumptions only when supported by Mach/AoA/control-deflection data;
   first quantify trajectory sensitivity to coefficient uncertainty. A plausible
   silhouette or successful landing is not independent force-model validation.
3. **Landing target and thermal validation.** The declared northern-Pacific water
   envelope is an engineering estimate, not measured mission coordinates.
   Thermal heating uses a stagnation-point correlation and a two-node TPS model.
   Validate frame conventions, energy budgets and reference-supported targeting
   before adding flooding, slamming, added mass or sea-state forces.

Authoritative local contracts: `docs/physics/PHYSICS_MODEL.md`,
`docs/physics/coupled_6dof_migration.md`,
`ExosphereSimulation/Physics/AerodynamicsModel.cs`, and
`ExosphereSimulation/Physics/ThermalModel.cs`. No physical model was changed by
this optimization; future physics work needs separate acceptance evidence.
