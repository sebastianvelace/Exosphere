# Starbase distance audit — in progress

## Current evidence

- Baseline: `/tmp/exo_play-audit-baseline-0905/exo_play_starbase_far.png`.
  The 12 km paused fixture completed at frame 95, but the site was not
  recognizable. Its old automated success marker was not visual acceptance.
- Corrected terrain winding:
  `/tmp/exo_play-audit-winding-0905b/exo_play_starbase_far.png`.
  Runtime mesh inspection reports 200 top triangles and zero back-facing tops.
  The image still fails the recognizable-site criterion.
- Projection diagnostic: at 1280x720 the site center projects to (640, 488),
  inside the frustum but behind the central T+ HUD counter. Camera altitude is
  22,233 m; vessel altitude is 12,000 m. These are not interchangeable.
- Clean HUD plus adaptive external near plane:
  `/tmp/exo_play-audit-depth-0906/exo_play_starbase_far.png`.
  The site is visible, but has an artificial polygonal footprint and thin
  straight water strip over a blurred regional coastline. This is NOT approved
  visual realism. HUD removal and near-plane adjustment changed together;
  this comparison does not isolate their individual contribution.

## Changes under verification

Godot uses clockwise front faces; shared relief/polygon tops previously forced
the opposite winding. The far-field footprint now uses the same corrected
triangle emitter. Reference:
https://docs.godotengine.org/en/4.6/classes/class_arraymesh.html

External camera near clipping now follows one percent of the actual smoothed
target distance, with the configured minimum and a 1000-render-unit ceiling.
Cockpit projection is unchanged. Eight camera-framing tests pass, including
four new near-plane cases. This has not yet been validated across the full
pad/chase/cockpit/orbital camera matrix. Precision rationale:
https://docs.godotengine.org/en/4.6/classes/class_camera3d.html

## Required next work

1. Replace the always-present synthetic far-field footprint/coastal ribbon when
   mapped context exists; reconcile mapped geometry with regional terrain.
2. Verify source-to-active-pad transforms and terrain elevation/occlusion.
3. Test the actual 12–40 km interval, not only a single paused 12 km seed.
4. Add projected-size and unobstructed-subject checks; mesh presence, winding,
   whole-frame brightness and a valid PNG still do not prove visual quality.
5. Capture pad/chase/cockpit/orbital regressions before committing camera work.

Temporary capture helpers were cleaned and `project.godot` restored after the
completed runs. Do not edit the shell harness while a run is executing: the
shell can resume reading changed byte offsets after its child process exits.
