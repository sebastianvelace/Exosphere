# Screenshot provenance

Gallery refreshed on 2026-11-10 against simulator source revision
`bd864f39b2ede58635aaafd22fac9d195f18c868` (`main`). These are unedited Godot framebuffer
captures at 1920 × 1080 with the Quality profile, OpenGL3/Compatibility under Xvfb. The
renderer is software; the images do not measure performance on a target GPU. Prepared scenes
are labelled; none is a continuous reconstruction of a real flight. Research figures and
source video frames elsewhere in `docs/` retain their historical meaning.

| File | Scene | Harness |
| --- | --- | --- |
| `liftoff.png` | Starship Flight 12 V3 lifting off Starbase, T+4 s | `--launch --flight12 --camera-preset liftoff_wide` |
| `apollo11-liftoff.png` | Saturn V AS-506 lifting off LC-39A on the dated Apollo 11 environment, T+9 s | `--apollo11` |
| `orbit.png` | Prepared Flight 12 V3 orbital vacuum-plume scene, 197 km | `--orbital-plume --flight12` |
| `entry.png` | Prepared IFT-7 70 km entry, aerodynamic descent at 32.5 km | `--edl --camera-preset edl_side` |
| `landing-burn.png` | Same entry, landing retro burn at 4.3 km | `--edl --camera-preset edl_side` |
| `cockpit.png` | Simulator cockpit in a prepared 115 km orbit | `--cockpit --flight12` |
| `pad.png` | Starship Flight 12 V3 on the Starbase pad before launch | `--smoke --flight12 --camera-preset pad_side` |
| `water-motion.png` | Seeded Flight 14 terminal water bench (2026-10-03, revision `0b6aab50`) | see below |
| `booster-return.png` | Super Heavy after Gulf water contact (2026-10-04) | see below |

## Checks

All images were decoded and inspected visually. The pad, launch and cockpit harnesses passed
their gates. Three runs ended non-zero for reasons that do not affect the published frame:

- `orbit.png`: the harness logged `ORBITAL_PLUME_OK`, but its framebuffer gate requires more
  tail-to-backdrop contrast than this frame's measured `plumeTailContrast=0.0988` against a
  bright Earth. The plume, anchoring, HUD and silhouette checks passed.
- `entry.png`, `landing-burn.png`: the EDL run captured entry, peak heating, retro burn and flip,
  then hit the 1500 s wall-clock budget set for this refresh before touchdown.
- `apollo11-liftoff.png`: the harness logged `LAUNCH_OK`; Godot then reported
  `RenderingServer` null-singleton errors while shutting down, after the capture.

No gate was weakened. A night pad capture (`--sun-elevation -8`) was taken and not published:
the sky renders flat black at that elevation.

## Reproduce

Run each harness sequentially because its temporary autoload owns `project.godot`; each
invocation removes the helper and restores the project when it exits. Set `GODOT_BIN` to the
Godot 4.6.3 .NET binary.

```bash
export EXOSPHERE_GRAPHICS_PRESET=quality
bash tools/visual_playtest.sh --launch --flight12 --camera-preset liftoff_wide --resolution 1920x1080 --run-id g-launch
bash tools/visual_playtest.sh --apollo11 --resolution 1920x1080 --run-id g-apollo11
bash tools/visual_playtest.sh --orbital-plume --flight12 --run-id g-orbit
bash tools/visual_playtest.sh --edl --camera-preset edl_side --resolution 1920x1080 --run-id g-edl
bash tools/visual_playtest.sh --cockpit --flight12 --resolution 1920x1080 --run-id g-cockpit
bash tools/visual_playtest.sh --smoke --flight12 --camera-preset pad_side --resolution 1920x1080 --run-id g-pad
```

Godot does not import this folder (`.gdignore`): the gallery is documentation, not a game
resource. The published PNG checksums are in [manifest.json](manifest.json).

## Flight 14 water-motion bench

`water-motion.png` is unchanged from the 2026-10-03 gallery: the seeded Flight 14 terminal bench
at contact +10.02 s, revision `0b6aab5065fc86891abc6aae1e3847a2f50936d6`. The hull is sealed and
physical waves/flooding remain unmodelled; see
[the water-motion audit](../audits/flight14_water_motion_2026-10-03.md).

## Independent Super Heavy return refresh

`booster-return.png` is an unedited 1280 × 800 production framebuffer captured on
2026-10-04 from the working tree based on `d5cc7bd5`, including this increment's
resource-driven boostback refinement and retired-booster HUD correction. The
original six images above retain their source revision and provenance. This frame
replaces only the booster addition; the prior sequences remain local evidence in
`exports/flight14-booster-return-review/` and `exports/flight14-booster-calibration/`.

The IntegratedGpu preset uses software OpenGL Compatibility under Xvfb. The frame
is at T+414.78 s, shortly after continuously simulated water contact at T+414.36;
it is not a seeded terminal scene or the exact transition witness. Shutdown has
completed in the frame and the bounded sealed-hull water response has begun.

The seven-frame sequence captures delivered boostback (THR 40%), coast,
eleven-engine ignition request (startup not completed), the three-engine selection
request (five still delivered while spooling down), water observation, abstracted
FTS retirement and camera/HUD return to the continuing Starship. Actual delivery
of 11/5/3 is separately checked by the continuous integration test; screenshot
filenames describe the selected cluster, not instantaneous running engine count.
Landing requests 3x warp and water observation 1x; whole 20 ms physics steps remain
authoritative. Paused frame scheduling can show the previous time-scale display.

The retirement capture preserves the final geodetic altitude −7.119 m and surface
speed 5.302 m/s instead of recomputing them against a later moving Earth. Its
navball is unavailable. Returning observation restores the ship's live HUD; the
left fleet engine board is currently empty after that observer switch.

Local console log, source/image hashes and temporary helper backups are in
`exports/flight14-booster-coast-refinement/`. Helpers were removed after capture;
`project.godot` was never modified. Gallery SHA-256: `79196025210ea7a36cbaf72a3f10e54fec98d960c7151173586fa1c606d38d54`.

The closer aggregate coast comparison does not establish exact flown attitude,
targeting, header inventory, engine-transition timing or ocean appearance. Distant
surface silhouette, cloud spacing and water spray remain visual limitations. See
[the coast calibration audit](../audits/flight14_booster_coast_calibration_2026-10-04.md).
