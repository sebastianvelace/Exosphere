# Hot-Staging Vent Exhaust — 2026-10-31

## Scope

First real-framebuffer pixel check of the hot-staging overlap. The 2026-08-20 pass in
`NEXT_VISUAL_REALISM_SPEC.md` proved anchoring and timing headless, but left "PNG daylight
overlap/separation" and "both vehicles readable" pending because that VM had no X11.
Presentation only; staging physics, thrust overlap and separation are unchanged.

## Reference

No IFT video frame could be acquired here (the pinned YouTube source requires sign-in). The
acceptance target is the documented one: a static overlap frame must clearly show Ship thrust
before full separation (`NEXT_VISUAL_REALISM_SPEC.md`, P1 hot-staging). In every Starship hot
stage the Ship's Raptors light while the stages are still joined, and their exhaust can only
leave through the vented hot-stage ring as luminous radial jets.

## Fixture

A temporary, untracked harness released the default Flight 7 stack from the pad, seeded
65 km, 2 300 m/s and a 25° flight-path angle, ran the 33 booster engines to steady state for
4 s, then called the production `SimulationBridge.BeginHotStageOverlap`. Separation followed
from the production overlap-completion path. Telemetry: 33 lit engines before, 39 during
overlap, 6 after separation. Compatibility renderer, 1280×720, Xvfb/llvmpipe.

## Finding

The overlap frame was pixel-identical in character to the pre-overlap frame. The existing
effect is a grey soot proxy: `hotstage_gas.gdshader` uses alpha mixing with opacity capped at
0.22, decaying with (1−t)² over 1.75 s, so ~0.6 s into the overlap it sat near 0.1 and could not
be seen at chase distance. Nothing represented the luminous Ship exhaust venting through the
ring.

## Change

- `assets/shaders/hotstage_flame.gdshader`: additive, unshaded vented-exhaust proxy, white-hot
  at the vent root cooling to orange at the tip, soft silhouette, simulation-time flicker.
- `HotStageFlashController`: twelve radial jets at the hot-stage interface (vent radius
  ~4.5 m), canted slightly aft. Their level follows the live mean chamber-pressure fraction of
  the Ship's Raptors while `IsHotStageOverlapping`, rising quickly and collapsing in ~0.5 s after
  separation. The grey soot puffs are kept as the residue. Exposed as
  `VentedExhaustIntensity` (0.55 measured 0.6 s into the overlap).

During development the jets first rendered fully transparent: a left-handed jet basis flipped
the normals that the shader's soft edge depends on. The basis is now right-handed.

## Evidence

`exports/hotstage-vent-exhaust-2026-10-31/`:

| File | Shows |
| --- | --- |
| `before_overlap_chase.png`, `before_overlap_below.png` | Overlap before the change: indistinguishable from pre-overlap |
| `after_overlap_chase.png`, `after_overlap_below.png` | Overlap after: ring of luminous vent jets while attached |
| `after_separation_chase.png` | After separation: jets gone, both vehicles visible |

`tools/tests/hotstage_visual_anchor_contract_test.sh` and the contract suite pass.

## Remaining (not changed here)

- **High-altitude main plumes.** At 65–70 km the 33-engine booster plume renders as a narrow
  pale column about one vehicle wide, and the Ship's six-engine plume after separation is barely
  visible. Near-vacuum Raptor exhaust is strongly under-expanded and should read several times
  wider than the vehicle. This affects every ascent above ~40 km and the existing launch and
  orbital-plume gates, so it needs its own pass.
  Addressed by `high_altitude_plume_bloom_2026-10-31.md`.
- Booster engine count at hot staging: the overlap lights all 33 booster engines plus the Ship's
  six. Flown hot stages throttle Super Heavy down to its centre engines before Ship ignition.
- Comparison against an actual IFT frame once a reference can be acquired.
