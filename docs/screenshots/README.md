# Screenshot provenance

Gallery refreshed on 2026-10-03 against simulator source revision
`0b6aab5065fc86891abc6aae1e3847a2f50936d6`. These are unedited Godot framebuffer
captures. Each prepared scene is described below; they are not a continuous
reconstruction of the actual Flight 14 mission. Research figures and source video
frames elsewhere in `docs/` retain their historical meaning.

| File | Scene and source | Resolution |
| --- | --- | --- |
| `menu.png` | Current operations menu; `menu_quick_check.py --case home-1280` | 1280 × 720 |
| `pad.png` | Flight 12 V3 at Starbase; `visual_playtest.sh --smoke --flight12 --camera-preset pad_side` | 1280 × 720 |
| `orbit.png` | Flight 12 V3 prepared orbital-plume scene, using the production staging/jump and rendering paths | 1920 × 1080 |
| `cockpit.png` | Simulator cockpit; prepared 118 km geocentric orbit (HUD around 114.9 km geodetic altitude) | 1280 × 720 |
| `entry.png` | Default Starship IFT-7 prepared 70 km entry, captured during descent around 65.3 km | 1280 × 720 |
| `water-motion.png` | Seeded Flight 14 terminal bench at contact +10.02 s; original working-tree implementation committed as the revision above | 1280 × 720 |

## Rendering and checks

Fresh menu, pad, orbital, cockpit and entry captures use the Quality profile with
OpenGL3/Compatibility under Xvfb. The water bench uses the same resolved Quality
profile and renderer. The renderer here is software; these screenshots do not
measure performance on the target integrated GPU.

Pad, orbital-plume and cockpit harnesses passed their existing gates, and the
menu check passed layout/focus verification. The orbital-plume gate requires
1920 × 1080; an initial 1280 × 720 capture was rejected for its dimensions and
was replaced by the accepted native-resolution run. No gate was weakened.
The entry capture used the public `BeginReentryDemonstration(true)` scenario,
advanced sixty rendered frames, paused with `SetTimeScale(0)`, and settled for
eighteen frames before saving. Its completed log contains no script/shader errors.
All six published images were decoded and inspected visually.

The water bench was inspected and physically checked separately in
[the water-motion audit](../audits/flight14_water_motion_2026-10-03.md).
The hull is sealed and physical waves/flooding remain unmodelled. Its daylight,
sea state and heeling timing are engineering presentation/model results, not
source-backed Flight 14 weather or capsize timing.

## Reproduce the standard captures

Run each harness sequentially because its temporary autoload owns `project.godot`.
Each invocation removes the helper and restores the project when it exits.

```bash
EXOSPHERE_GRAPHICS_PRESET=quality bash tools/visual_playtest.sh \
  --smoke --flight12 --camera-preset pad_side --resolution 1280x720 \
  --run-id gallery-pad-20261003
EXOSPHERE_GRAPHICS_PRESET=quality bash tools/visual_playtest.sh \
  --orbital-plume --flight12 --run-id gallery-orbit-native-20261003
EXOSPHERE_GRAPHICS_PRESET=quality bash tools/visual_playtest.sh \
  --cockpit --flight12 --resolution 1280x720 --run-id gallery-cockpit-20261003
EXOSPHERE_GRAPHICS_PRESET=quality python3 tools/menu_quick_check.py \
  --skip-build --case home-1280 --output exports/repository-gallery-20261003
```

Capture logs, image copies and the temporary entry script are preserved locally in
`exports/repository-gallery-20261003/`. The seeded terminal script and its logs are
in `exports/flight14-water-motion-review/`. These ignored directories are review
artifacts; temporary scripts/autoloads are not part of the shipped game.
The published PNG checksums are in [manifest.json](manifest.json).

## Independent Super Heavy return refresh

`booster-return.png` is an unedited 1280 × 800 production framebuffer captured on
2026-10-03 from the working tree based on `a2d6ce3`, including this increment's
boostback/landing calibration and HUD correction. The original six images above
retain their source revision and provenance. This frame replaces only the earlier
booster addition; its prior sequence remains local evidence in
`exports/flight14-booster-return-review/`.

The IntegratedGpu preset uses software OpenGL Compatibility under Xvfb. The
frame is at T+418.84 s, shortly after continuously simulated water contact;
it is not a seeded terminal scene or the exact transition witness. Engine
shutdown is commanded and the sealed-hull water response has already begun.

The seven-frame sequence captures boostback (THR 60%), coast with the stale
impact alert cleared, eleven-engine ignition, three delivered landing engines,
water observation, abstracted FTS retirement and camera/HUD return to the
continuing Starship. Landing uses requested 3x warp and water observation 1x;
whole 20 ms physics steps remain authoritative.

Local console log, source-file hashes, screenshot hashes and temporary helper
backups are in `exports/flight14-booster-calibration/`. The helper scripts were
removed after capture; `project.godot` was never modified. Gallery SHA-256:
`db90f31538bdb5ab9b4f6a96b01adc95297c4c6a05bbd67f94751f680d24c7d3`.

The timing and height improvement does not establish exact flown targeting,
header inventory, engine-transition timing or ocean appearance. Distant surface
silhouette, cloud spacing and water spray remain visual limitations. See
[the calibration audit](../audits/flight14_booster_calibration_2026-10-03.md).
