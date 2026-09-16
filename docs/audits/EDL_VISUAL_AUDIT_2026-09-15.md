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
| `--edl --resolution 1280x720` | Visual milestones through flip | `entry`, `peak_heating`, `retro_burn`, `flip_complete` PNGs in `/tmp/exo_audit_edl_v9/` |
| `--edl --resolution 640x360` | `CAUGHT` | `/tmp/exo_audit_edl_v10/exo_play_caught.png` and `CHECK tower_catch caught=True pins=2` |

The EDL captures show a stable belly-flop attitude, a controlled low-altitude flip, an
opaque black nose cone and a completed two-pin tower catch. The final run keeps the
production x1 cadence after the flip; the reduced 640x360 framebuffer is a test-runtime
constraint for llvmpipe, while the physics and gate are unchanged.

## Implementation delivered

- `PoweredLandingGuidance` now allows a physical coast while the vehicle is below its
  descent-rate profile, then requests thrust only when braking or meaningful lateral
  correction is required.
- A 0.50 m/s lateral deadband prevents minimum-throttle Raptors from turning sub-m/s
  cradle noise into a hover.
- The EDL harness remains at x1 after flip, matching the production EDL cadence.
- The full .NET suite passes 782/782 tests and the Godot build passes with zero warnings.
- The completed run reports `CAUGHT`, two pin contacts, zero relative speed and a settled
  angular rate, so the contact gate is now accepted.

## Remaining visual work

The remaining visual track is reference matching: richer entry shock/plasma timing,
liftoff and Max-Q capture coverage, orbital cloud/terrain comparison at higher altitude,
and crash/abort capture coverage. These remain separate from the completed menu/HUD
redesign, pad/cockpit gates and physical EDL catch gate.
