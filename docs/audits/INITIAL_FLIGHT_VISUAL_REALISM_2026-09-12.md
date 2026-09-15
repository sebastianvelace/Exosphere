# Initial-flight visual realism — September 12, 2026

## Scope and completion criteria

Complete the interrupted Starbase-to-40 km presentation pass without changing flight
physics or the existing 40–75 km local-ground/globe handoff. Close this pass only with
clean CI, reviewed framebuffer PNGs, renderer identity, material fade evidence and
logical commits. This is a bounded rendering correction, not a photorealism claim.

## Verified causes and changes

- The plume used black source color with mix blending. Non-black source radiance,
  separate bounded sheath/core opacity and a narrower core remove the dark wedge;
  vacuum opacity still responds to throttle and expansion.
- The opening camera stayed anchored to the ground until 1.1 km, making the vehicle
  progressively smaller. A height-relative smooth curve now moves the target and
  distance onto the vehicle, reaching the Chase frame at 1.5 vehicle heights.
- Steel was unshaded. It now supplies albedo, metallic and roughness to scene lighting
  with a bounded emission floor. It does not write `ALPHA`, preserving the opaque
  depth path needed by screen-space effects.
- Launch illumination uses sky ambient/reflections, tighter four-split shadows and
  depth-based aerial perspective that follows atmospheric presence and horizon color.
  SSAO is enabled in Compatibility and Forward+; SSIL/SSR are enabled only in Forward+.
  Realtime sky radiance explicitly requests 128 in Compatibility and the required 256
  in RenderingDevice backends, matching the actual allocation.
- Godot Compatibility ignores `GeometryInstance3D.Transparency`. Earlier 1–8 km
  telemetry reported the intended fade while the material could remain unchanged.
  `LaunchSurfaceFade` now owns a material copy per mesh, scales original material alpha
  (or civil shader opacity), and hides geometry at zero opacity. Shared materials cannot
  couple the hero and regional layers. Full-opacity materials retain their original mode.
- Regional land cover fades in during the same 1–8 km interval that hero land cover
  fades out. Regional structures remain hidden while the hero complex is visible,
  then fade in over 10–14 km. The existing globe handoff still removes local context.

## Validation

Baseline before the final integration: `tools/ci_check.sh` passed all 774 xUnit tests,
both C# builds without warnings/errors, static contracts and scene startup checks.
The final regional run passed in Compatibility with actual method `gl_compatibility`:
`/tmp/exo_play-resume-transition-final3/` contains all six PNGs and
`STARBASE_FAR_OK`; material alpha measured `0.055, 0.606, 1.000, 1.000, 1.000, 1.000`
at 2/5/8/12/20/40 km, and visible structures were `0, 0, 0, 37, 37, 37`.
The final launch-track run passed with `LAUNCH_TRACK_OK` at
`/tmp/exo_play-resume-track-final/`; the 300 m and 1 km views retained both vehicle
endpoints and projected heights `0.386` and `0.383`. The final Compatibility orbital
plume run passed with full/half/off frames in `/tmp/exo_play-resume-ship-final/`.
The earlier Forward+ launch comparison recorded actual method `forward_plus` in
`/tmp/exo_play-resume-launch-forward/`; its software Vulkan image was paler than
Compatibility, so it is comparison evidence rather than a quality claim.

The new `--launch-track` mode flies through 250 m and 1 km, requiring Chase framing,
both vehicle endpoints in the frustum and projected height between 18% and 85% of the
viewport. `--starbase-far` seeds six fixed altitude fixtures with a ground-facing camera;
it is a LOD/material test, not proof of a naturally flown view at those heights. Its gate
checks material alpha, exclusive visible structures, source geometry and PNG integrity.
Every capture logs the actual rendering method; requested/actual mismatch is an error.

## Remaining fidelity limits and next work

Status update 2026-09-14: the first 10 km now uses a geographically aligned USDA NAIP
orthophoto plus a normalized USGS 3DEP broad-relief raster, and a filtered 50 km NAIP /
3DEP macro layer now bridges the mid-altitude pullback. OSM civil geometry remains
anchored to the same Starbase datum, and the two observed terrain layers feather into the
existing planetary ground before the large-scale Starbase handoff. The 2–40 km runtime
matrix no longer shows the isolated realistic square; see
`docs/audits/LOCAL_TERRAIN_REALISM_2026-09-14.md` for the implementation and evidence.

The regional ground is a baked presentation asset, not a collision mesh or a complete
high-resolution reconstruction. The pad still retains visibly procedural tanks/buildings
and discrete deluge billboards. Forward+ produced a paler image in the software-rendered
comparison; additional rendering features alone do not establish improved realism.

The 2026-09-14 follow-up closes the previously documented tangent-patch handoff issue:
the globe handoff now completes at 18 km, before the pulled-back 20 km view, and the
corrected camera fixture proves a single planetary surface at 20 and 40 km. Remaining
priority is fine reference matching and target-GPU frame-time/memory measurement. Keep
camera and physical flight identical between comparisons; avoid hiding remaining detail
limits with stronger haze.

Offscreen scene reflections are not supplied by SSR, and transparent ground overlays
do not share all opaque screen-space effects. Software Vulkan/OpenGL captures establish
correctness and appearance in this environment, not a target-GPU performance budget.
