# Flight 14 Visual Baseline — 2026-10-14

## Scope

This audit establishes reproducible framebuffer evidence for launch and orbital
presentation before further Flight 14 visual changes. The captures are diagnostic
fixtures, not an exact reconstruction of Flight 14 hardware, broadcast cameras, or
environmental conditions.

The launch sequence uses the Starship Flight 12 V3 profile as the closest available
launch vehicle fixture. The standalone orbital-plume capture is explicitly an IFT-7
fixture. Both were rendered at 1280×720 with Godot Forward+ under Xvfb using Mesa
llvmpipe; these images can be used for composition and material comparisons, but they
do not certify performance on the user's Radeon or another physical GPU.

## Captures

Saved in `exports/flight14-visual-baseline-2026-10-14/`:

| Capture | Gate | What it establishes |
| --- | --- | --- |
| `exo_play_pad.png` | Pass | Pre-ignition launch complex composition |
| `exo_play_startup_ramp.png` | Pass | Engine startup and initial deluge onset |
| `exo_play_liftoff.png` | Pass | Liftoff plume, vehicle, pad and HUD |
| `exo_play_tower_clear.png` | Pass | Vehicle framing after clearing the tower |
| `exo_play_early_ascent.png` | Pass | Vehicle framing around 1.1 km altitude |
| `orbital_plume_ift7_fixture_not_f14.png` | Pass | Vacuum plume, Earth limb, vehicle and HUD in the IFT-7 fixture |

`launch-track-run-summary.txt` and `orbital-plume-run-summary.txt` preserve the
playtest summaries. The corresponding telemetry logs and full-size capture output
remain under `/tmp/exo_play-priority1-*` for this session.

Reproduce the launch sequence with:

```sh
bash tools/visual_playtest.sh --launch-track --renderer forward_plus \
  --resolution 1280x720 --sun-elevation 12 --run-id flight14-baseline-launch
```

Reproduce and verify the orbital fixture with:

```sh
bash tools/visual_playtest.sh --orbital-plume --renderer forward_plus \
  --resolution 1280x720 --run-id flight14-baseline-orbit
bash tools/visual_playtest.sh --orbital-plume --renderer forward_plus \
  --resolution 1280x720 --run-id flight14-baseline-orbit --verify-only
```

For `--verify-only`, pass the same renderer and resolution used to create the
artifacts. The orbital-plume acceptance check now compares against the requested
resolution instead of assuming 1920×1080.

## Comparison with Flight 14 reference

`exports/flight14-cloud-light-review/reference/video_00025.jpg` is the relevant
public-video frame at approximately T+9 s. It is an elevated oblique shot: the
vehicle is large in frame, the pad and surrounding wetlands remain visible, and a
voluminous asymmetric vapor cloud extends to one side. The cloud has warm light near
the exhaust and retains texture through its bright regions.

The current production launch capture is a lower, more distant view. The stack is
roughly one quarter of the scene height; the plume resolves into two similarly sized
white lobes, and the brightest region loses detail. That makes the current mismatch
primarily a combination of camera composition, steam-cloud morphology, and highlight
range—not evidence of a flight-dynamics error.

An experimental close aerial camera composition was tested, then removed because it
made the stack readable but failed the existing image-quality gate: the liftoff frame
reported `surfaceClippedFrac=0.15162` and `neonGreenFrac=0.007812` at 6° presentation
sun elevation. The gate was not weakened to accept it. This indicates that a camera
change alone is insufficient; the plume/ground image metrics and highlights need a
more representative treatment before keeping a closer aerial preset.

## Recommended order

1. Improve the deluge cloud's silhouette and lighting while retaining the current
   physics boundary. `scripts/PadSteamCloud.cs` creates mirrored lobes with matched
   dimensions and longitudinal positions, which explains why the current cloud tends
   to read as paired puffs. There is no explicit local atmospheric wind vector in the
   current launch-effects path, so an asymmetric drift should use a measured source or
   remain a clearly presentation-only seeded variation; it should not pretend to be
   a simulated wind field.
2. Revisit the aerial camera only after the cloud has texture in its bright regions.
   Tune framing against the T+9 s reference while retaining the current whole-vehicle
   and image-quality gates.
3. Repeat the same launch capture on a physical GPU to check cloud-crossing frame
   pacing. The current llvmpipe run is not valid GPU performance evidence.
4. After the launch baseline is accepted, extend the same reference-matched method to
   hot-stage, payload deployment, Flight 14's early deorbit, and entry/plasma.

No trajectory, atmosphere, propulsion, or thermal physics were changed for this audit.
