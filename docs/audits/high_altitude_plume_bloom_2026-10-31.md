# High-Altitude Plume Bloom — 2026-10-31

## Scope

Follow-up to `hotstage_vent_exhaust_2026-10-31.md`, which found that at 65–70 km the
33-engine booster plume rendered as a pale column about one vehicle wide. Presentation only:
thrust, Isp, mass flow and the pressure ratio fed to the renderer are unchanged.

## Reference

No new IFT frame could be acquired here (YouTube requires sign-in). The target is the
well-documented appearance of every high-altitude ascent from Saturn V to Starship: between
roughly 35 and 100 km the exhaust is strongly under-expanded (exit pressure 10³–10⁵ times
ambient), turns outward just past the nozzle and drives a luminous interaction layer into the
residual air, so the plume reads several vehicle diameters wide and widens downstream. In
orbit there is no air to light up and the plume becomes faint again.

## Finding

The plume already had a vacuum mode, but it treated 65 km and 200 km alike: `expansion` is a
smoothstep of 1 − p/p₀, which is ~1 at both. The cone meshes also converge (Super Heavy tail
radius 0.36 against a 0.5 mouth), and the outer sheath alpha fell to ~0.02 in near-vacuum, so
only the narrow core was visible. The orbital-plume gate deliberately pins sheath opacity at
0.10–0.14 at 200 km, so raising the vacuum sheath globally would have been wrong.

## Change

- `PlumeSystem.PlumeBloom(p/p₀)`: a weight that rises between p/p₀ 10^−6.5 and 10^−5
  (~100 → ~80 km) and falls between 10^−2.2 and 10^−1.2 (~35 → ~20 km). Zero at sea level
  and in orbit. Exposed as `LastBloom`.
- `raptor_plume.gdshader`: new uniforms `plume_bloom` and per-layer `vacuum_flare`. The vertex
  stage multiplies the ring radius by `1 + (flare − 1)·bloom·a^0.55` (fast opening past the
  exit plane, continued widening downstream). Under bloom the radial falloff softens to 1.5
  and the sheath alpha floor rises to 0.17.
- Flare per layer: booster sheath 4.2, booster core 1.8, single-nozzle sheath 2.6, core 1.5,
  ground skirt 1.0. Sheath layer opacity under bloom: 0.62 (booster), 0.42 (others);
  `LastSheathOpacity` reports the same blend.

## Evidence

`exports/high-altitude-plume-bloom-2026-10-31/`, temporary seeded harness (Flight 7 stack
released from the pad, placed at 65 km climbing to 68.9 km, 2 300 m/s, 25° flight-path angle,
33 engines at full throttle), 1280×720, Compatibility renderer, Xvfb/llvmpipe.
p/p₀ = 6.1·10⁻⁵, bloom 1.0, sheath opacity 0.42.

| File | Shows |
| --- | --- |
| `before_69km_below.png` | Before: a narrow white column about one vehicle wide |
| `after_69km_below.png` | After: the sheath opens downstream to roughly three vehicle widths in frame |
| `after_69km_side.png` | After, wider view: a soft widening fan, no hard polygon edges |
| `after_72km_ship_post_separation.png` | Ship alone at 72.4 km, 3 s after the production hot-stage separation: six plumes merge into one fan several hull diameters wide; the separated booster is in frame below |

Sea-level (bloom ≈ 0.01 at 20 km, 0 below) and orbit (bloom 0 at 200 km) take the previous
paths unchanged; `tools/ci_check.sh` (contract checks including the orbital-plume gate
values, simulation build/tests, Godot build, headless smoke) passes.

## Remaining

- Comparison against an actual IFT frame at 60–70 km once a reference can be acquired; the
  flare factors are an engineering estimate, not a measured fit.
- Ship plumes after separation: captured (bloom 1.0, sheath opacity 0.42); they read as a
  single under-expanded fan, so no Ship-specific change was made.
- Real-time `[G]` ascent capture on a machine with a GPU (llvmpipe runs ~0.03× real time).
