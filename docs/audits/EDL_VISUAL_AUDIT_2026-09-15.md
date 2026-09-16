# EDL visual audit — 2026-09-15

## Scope

This audit checks the real Godot framebuffer for the Starship entry, powered descent,
pad and cockpit paths after the reentry attitude work. It does not treat source-only
checks or interrupted runs as visual acceptance.

## Verified evidence

| Run | Result | Evidence |
|---|---|---|
| `--smoke --resolution 1280x720` | `SMOKE_OK` | `/tmp/exo_audit_smoke/exo_play_pad.png` |
| `--cockpit --resolution 1280x720` | `COCKPIT_OK` | `/tmp/exo_audit_cockpit/exo_play_cockpit.png` |
| `--edl --resolution 1280x720` | Partial visual milestones | `entry`, `peak_heating`, `retro_burn`, `flip_complete` PNGs in `/tmp/exo_audit_edl_v3/` |

The EDL captures show a stable belly-flop attitude, a controlled low-altitude flip and
an opaque black nose cone. The final catch contact was not accepted from the interrupted
llvmpipe runs: the earlier harness warp produced a repeatable hover near 0.8 km, while
the x1 rerun reached the final approach too slowly for this audit window to provide a
completed catch gate.

## Implementation delivered

- `PoweredLandingGuidance` now allows a physical coast while the vehicle is below its
  descent-rate profile, then requests thrust only when braking or meaningful lateral
  correction is required.
- A 0.50 m/s lateral deadband prevents minimum-throttle Raptors from turning sub-m/s
  cradle noise into a hover.
- The EDL harness remains at x1 after flip, matching the production EDL cadence.
- The full .NET suite passes 782/782 tests and the Godot build passes with zero warnings.

## Remaining visual work

The remaining visual track is reference matching: richer entry shock/plasma timing,
liftoff and Max-Q capture coverage, orbital cloud/terrain comparison at higher altitude,
and a completed x1 catch/touchdown framebuffer gate on a sustained run. These remain
separate from the completed menu/HUD redesign and the current pad/cockpit gates.
