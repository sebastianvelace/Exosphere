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

## Independent Super Heavy return addition

`booster-return.png` is an unedited 1280 × 800 production framebuffer captured on
2026-10-03 from the working tree based on `91dd7b7`, including the independent
booster implementation committed with this gallery addition. The original six
images above keep their source revision and provenance. This capture uses the
IntegratedGpu profile with software OpenGL Compatibility under Xvfb. It shows
water entry after continuous simulation from the original loaded launch stack;
it is not a seeded terminal scene or a reconstruction of the flown trajectory.

The completed seven-frame sequence witnesses boostback, coast, ignition,
three-engine braking, contact, abstracted FTS retirement, and return of camera/HUD
to the continuing Starship. Landing uses requested 3x warp and water observation
1x; production physics retains whole 20 ms steps. Local capture scripts, console
log and hashes are in `exports/flight14-booster-return-review/`. The timing,
protected fuel budget, distant surface silhouette, cloud spacing and near-water
visual fidelity remain bounded as described in [the booster audit](../audits/flight14_booster_return_2026-10-03.md).
