# Flight 14 Entry Plasma Comparison — 2026-10-31

## Scope

Presentation-only comparison of the Starship peak-heating sheath against the four Flight 14
onboard frames already in the repository
(`docs/research/flight14_descent_browser_captures_2026-10-02/`). No trajectory, heating,
aerodynamic or thermal physics changed.

## Reference

The frames show the onboard view of a body flap and hull edge:

| Frame | Conditions | Appearance |
| --- | --- | --- |
| `video_09805.jpg` | early entry | Pink/magenta haze; white-pink band along the flap/body edge |
| `video_10384.jpg` | T+02:52:49, 74.1 km, 25 369 km/h | Lavender-white skirt glow; violet engine bay |
| `video_10556.jpg` | peak | Saturated violet field; blinding white-lavender core on the windward edge |
| `video_10976.jpg` | late | Near-dark ambient; magenta streaks only on flap leading edges |

In every frame the sheath is a continuous gradient with fine streaking. None shows large dark
patches on the glowing gas.

## Fixture

A temporary, untracked capture harness seeded the production reentry demonstration, then set
74 km altitude and 7 000 m/s air-relative speed (the `video_10384` conditions), froze time,
and entered `PEAK_HEATING`. Production `ReentryPlasmaController` read 205.6 kW/m² stagnation
flux, flux intensity 0.657 and visual intensity 0.761. Rendered with the Compatibility
renderer at 1280×720 under Xvfb/llvmpipe; not physical-GPU evidence.

The full `--reentry-compare` mode was not used for this pass: its demonstration seeds 70 km at
1.8 km/s, far below the reference's heating regime, and under llvmpipe it advanced at about
0.03× real time.

A second fixture at 45 km, 2 500 m/s and `AERO_DESCENT` (61.0 kW/m², visual intensity
0.217) stands in for the late frame. It is an assumed late-entry state, not a reconstructed
Flight 14 trajectory point.

## Findings

The palette was already calibrated to these frames (violet/rose line emission around a white
core; the orange bow cap is only used for non-Ship hulls). Two mismatches remained:

1. **Texture at peak.** The hull sheath (`effect_kind 3`) and halo (`effect_kind 4`) sampled
   isotropic noise at 3×1 cycles over the whole barrel, so each blob spanned metres, and the
   strand floor of 0.18 gave roughly 6:1 contrast. The windward side read as dark camouflage
   patches rather than luminous gas.
2. **Fade order late in entry.** At visual intensity 0.217 the whole windward hull stayed
   tinted violet, while the flap leading-edge glows had already dropped below the shader's
   onset (their delay and weight left a heat level of ~0.08). The reference is the opposite:
   dark hull, magenta streaks only on the flap edges. Stagnation heating scales roughly with
   1/√(nose radius), so small-radius flap edges should outlast the broad barrel sheath.

## Change

`assets/shaders/reentry_glow.gdshader` and `scripts/ReentryPlasmaController.cs`:

- Sheath and halo use a flow-aligned streak sample, long around the barrel (the broadside flow
  direction) and fine along it, with the brightness floor raised to 0.60 (sheath) and 0.55
  (halo).
- Sheath and halo coverage now fade with `smoothstep(0.20, 0.50, heat_level)`; peak heating
  (shock heat level 0.665) is unaffected.
- Flap leading edges receive a 3.5× heat concentration and a magenta-to-white edge palette
  (`edge_magenta`, flap edges only). Nose/belly edges and capsule paths are unchanged.

Onset thresholds, the bow cap, wake, flux mapping and all physics are unchanged.

Captures in `exports/flight14-entry-plasma-2026-10-31/`:

| File | Shows |
| --- | --- |
| `before_belly_close.png` / `after_belly_close.png` | Peak, broadside chase: camouflage patches → continuous streaked sheath |
| `before_onboard_flap.png` / `after_onboard_flap.png` | Peak, hull-mounted view toward the aft flap: white windward band preserved |
| `late_before_onboard_flap.png` / `late_after_onboard_flap.png` | Late fixture: violet hull and no flap glow → dark hull with a magenta flap streak |

## Remaining

- The `video_10556` view is filled edge to edge by violet glow because the camera sits inside
  the shock layer. The game has no hull-mounted camera mode, so this is not reproduced.
- The late fixture is an assumed state; a capture from a continuous production entry at the
  matching phase would confirm timing of the sheath fade.
- Repeat on a physical GPU before judging brightness or bloom.
