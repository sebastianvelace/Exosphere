# Next Visual Realism Specification

**Date:** 2026-08-20  
**Status:** Ready for next implementation session after `172dab7` and `a7176c7`.

## Goal

Push Exosphere from "recognizable Starship simulator" toward "credible real-world
flight footage": Starbase daylight launch, hot-staging, orbital Earth, EDL plasma,
catch/landing, cockpit and VAB must each have reference-backed visual acceptance.

This is not a request for more random detail. Each item below needs:

- a real reference target,
- a deterministic capture,
- a code owner,
- a measurable gate,
- and one human screenshot review.

## Sources Consulted

- SpaceX Starbase launch page: `https://www.spacex.com/launches/starbase`
- SpaceX Starship Flight 12 page: `https://www.spacex.com/launches/starship-flight-12`
- SpaceX Starship Flight 11 page: `https://www.spacex.com/launches/starship-flight-11`
- FAA Boca Chica Starship/Super Heavy archive:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship/activity_archive`
- FAA Starship/Super Heavy vehicle summary:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship/starship_super_heavy`
- FAA LC-39A Starship/Super Heavy EIS:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship_ksc`
- NASA Earth at Night:
  `https://science.nasa.gov/earth/earth-observatory/earth-at-night/`
- NASA airglow explainer:
  `https://www.nasa.gov/solar-system/why-nasa-watches-airglow-the-colors-of-the-upper-atmospheric-wind/`
- NASA Earth limb / atmosphere visual reference:
  `https://svs.gsfc.nasa.gov/11901`

## Current Baseline

Validated locally:

- `dotnet build Exosphere.csproj --nologo -v quiet` -> 0 warnings / 0 errors.
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`
  -> 703/703 pass.
- `bash tools/ci_check.sh` -> pass.
- `bash tools/visual_playtest.sh --launch --run-id pad-tower-v11-launch2 --skip-build`
  -> `LAUNCH_OK`, pad/liftoff PNGs verified.

Important limitations:

- Launch/catch captures still happen in dark/twilight conditions too often for
  fine structural review.
- Starbase tower V1.1 loads and is functional, but not yet compared side-by-side
  against daylight Starbase footage.
- Reentry visuals have shock/plasma/charring, but the "real footage" contrast,
  flow direction and heating progression still need a tighter acceptance pass.
- Earth/atmosphere is better, but still reconstructed, not photogrammetric.

## P0 — Daylight Reference Capture Matrix

### Why

The project has good runtime gates, but too many visual claims still rely on dark
or obstructed screenshots. Realism work cannot keep advancing from low-contrast
evidence.

### Implementation

- Add deterministic sun/time controls to `tools/visual_playtest.sh`:
  - `--sun-elevation DEG` for pad/launch/catch/ship/orbit modes.
  - `--camera-preset pad_side|tower_side|tracking|orbit_beauty|edl_side`.
  - write `VISUAL_SUN elevationDeg=... phase=...` and `VISUAL_CAMERA preset=...`.
- Default visual acceptance captures should use daylight unless a night case is
  explicitly being tested.
- Store run output in `/tmp/exo_visual_<topic>_<id>/`.

### Files

- `tools/visual_playtest.sh`
- `scripts/SunController.cs`
- `scripts/CameraController.cs`
- `scripts/SkyController.cs`

### Acceptance

- `--launch --sun-elevation 35 --camera-preset tower_side` captures pad/liftoff
  with tower, stack and plume readable.
- `--hotstage --sun-elevation 35` captures hot-stage overlap and separation.
- `--edl --sun-elevation 25 --camera-preset edl_side` captures entry, retro burn
  and caught/landing with vehicle silhouette readable.
- Add contract: visual modes that claim reference acceptance must log sun and
  camera preset.

## P1 — Starbase Pad 2 / Tower Fidelity

### Reference Target

FAA/SpaceX public material confirms Boca Chica Starship/Super Heavy operations,
modern deluge/water system context and launch-site infrastructure. Recent public
Starship flights also moved into V3/Pad 2 era. The in-game Starbase should read as
post-deluge Starbase, not a generic launch tower.

### Implementation

- Extend `LaunchComplexSpec` with Pad 1 vs Pad 2 visual profiles.
- Add:
  - Pad 2 civil footprint option,
  - wider catch-arm carriage housing,
  - visible SQD/BQD umbilical heads with hoses,
  - deluge plate/nozzle field detail visible at pad distance,
  - catch-arm inner pads with distinct material from structural steel,
  - service platforms at Ship QD and carriage levels.
- Keep it procedural. Do not introduce heavy mesh assets until the procedural
  silhouette fails a side-by-side review.

### Files

- `scripts/LaunchPadController.cs`
- `docs/audits/STARBASE_RECONSTRUCTION_V1.md`
- `PLAN_VISUAL_REALISM.md`

### Acceptance

- Daylight side capture identifies OLM, OLIT, chopsticks, SQD/BQD, deluge deck
  and tank farm without zooming into source code.
- `launch_pad_performance_contract_test.sh` still passes.
- No pad geometry appears in orbital ship captures unless a catch approach is
  active.

## P1 — Hot-Staging Reference Pass

### Reference Target

SpaceX Flight 11 and Flight 12 timelines list hot-staging shortly after MECO,
with Ship ignition and stage separation occurring within seconds. In-game visuals
must make this event readable in one frame and correct across a short sequence.

### Implementation

- Use `--hotstage` as the acceptance harness, not synthetic staging only.
- Capture at least:
  - `hotstage_pre`,
  - `hotstage_overlap`,
  - `hotstage_separation`,
  - `booster_flip`.
- Tune:
  - interstage flash duration,
  - plume origin and scale,
  - soot/haze around hot-stage ring,
  - exposure so Ship and Booster do not vanish into black.

### Files

- `scripts/HotStageFlashController.cs`
- `scripts/PlumeSystem.cs`
- `scripts/VesselRenderer.cs`
- `tools/visual_playtest.sh`

### Acceptance

- A static `hotstage_overlap` screenshot clearly shows Ship thrust before full
  separation.
- Log includes finite vehicle states, engine counts and `IsHotStageOverlapping`.
- No pad smoke style appears on vacuum/upper-stage plume.

## P1 — Reentry Plasma And Thermal Damage V2

### Reference Target

SpaceX flight writeups emphasize heatshield performance, structural stress, flap
limits, dynamic banking and guided flap-controlled descent. NASA reentry/airglow
references help distinguish atmospheric glow/limb effects from vehicle plasma.

### Implementation

- Separate visual regimes:
  - upper atmosphere faint shock layer,
  - peak heating windward plasma,
  - post-peak wake thinning,
  - retro burn plume interaction,
  - final catch/landing dust/steam.
- Add per-zone thermal presentation:
  - nose,
  - windward belly,
  - forward flaps,
  - aft flaps,
  - leeward stainless body.
- Drive color and alpha from heat flux, density and local flow incidence.
- Keep structural failure physics separate from visual charring.

### Files

- `scripts/ReentryPlasmaController.cs`
- `scripts/ReentryBreakupController.cs`
- `scripts/VesselRenderer.cs`
- `scripts/EDLController.cs`
- `tools/visual_playtest.sh`

### Acceptance

- Nominal belly-first: windward plasma and tile glow, leeward side mostly readable.
- Bad attitude: nose/flap off-axis heating clearly stronger before failure.
- `--reentry-compare` produces nominal/bad-attitude PNGs with non-overlapping
  thermal signatures.
- HUD remains legible during peak heating.

## P1 — Orbital Earth / Night / Airglow Pass

### Reference Target

NASA Earth-at-night and airglow references show that orbital night is not pure
black: city lights, airglow and limb scattering remain visible, while the surface
should not become a flat bright texture.

### Implementation

- Add a thin airglow shell or sky/planet limb term distinct from Rayleigh daytime
  atmosphere.
- Add optional low-resolution night-light texture path using NASA Black Marble
  derived assets only if licensing and asset size are acceptable.
- Improve exposure adaptation so:
  - daylight Earth does not clip,
  - night Earth has city/airglow cues,
  - stars remain visible without overpowering the planet.

### Files

- `assets/shaders/planet_body.gdshader`
- `assets/shaders/earth_surface.gdshader`
- `assets/shaders/space_sky.gdshader`
- `scripts/PlanetMaterials.cs`
- `scripts/SkyController.cs`
- `scripts/SunController.cs`

### Acceptance

- `--orbit --sun-elevation -35` shows a readable night limb without broad white
  clipping.
- `--atmosphere-ground` keeps sunrise/sunset monotonic and no negative radiance.
- `space_sky_banding_contract_test.sh` and `planet_body_lighting_contract_test.sh`
  pass.

## P2 — Real Camera Language

### Implementation

- Add capture presets matching real footage:
  - long-lens pad side,
  - tracking ascent,
  - staging telephoto,
  - orbital chase,
  - EDL ground/telemetry view,
  - cockpit handheld/seat vibration.
- Add camera metadata logging:
  - FOV,
  - target,
  - distance,
  - mode,
  - sun elevation.

### Acceptance

- Captures are comparable across runs without manual camera guesswork.
- No UI panel hides the exact structure being reviewed.

## P2 — VAB And Mission Presentation

### Implementation

- Make VAB lighting/materials match the flight renderer: stainless steel, tile
  side, flaps, grid fins and engine bells should be recognizable in the preview.
- Add multi-select/gizmo screenshots to acceptance.
- Improve main-menu/mission briefing art so the first frame signals the actual
  playable simulator, not a generic menu.

### Acceptance

- `tools/capture_vab.gd` or an equivalent harness captures empty VAB, selected
  stack and invalid attachment state.
- `vab_preview_lighting_contract_test.sh` and `vab_picking_alignment_contract_test.sh`
  pass.

## Anti-Goals

- Do not replace physics with visual shortcuts.
- Do not add heavy external assets unless the asset has clear licensing, size
  budget and a visible quality win.
- Do not claim realism from static contracts alone; contracts protect regressions,
  screenshots prove presentation.
- Do not tune night exposure to make one screenshot pretty while breaking orbital
  darkness, stars or cockpit readability.

## Recommended Next Commit Sequence

1. `test(visual): add deterministic daylight capture presets`
2. `polish(pad): refine Starbase tower and deluge fidelity`
3. `polish(staging): match hot-staging reference sequence`
4. `polish(reentry): add thermal zone presentation v2`
5. `polish(orbit): add airglow and night Earth cues`
6. `docs(visual): record next realism evidence matrix`

## Verification Gate

Before closing the next session:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
bash tools/ci_check.sh
bash tools/visual_playtest.sh --launch --run-id next-launch --skip-build
bash tools/visual_playtest.sh --hotstage --run-id next-hotstage --skip-build
bash tools/visual_playtest.sh --reentry-compare --run-id next-reentry --skip-build
bash tools/visual_playtest.sh --orbit --run-id next-orbit --skip-build
```

Manual review must inspect the PNGs, not just command exit codes.
