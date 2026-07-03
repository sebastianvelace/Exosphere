# Exosphere — Delegation Matrix (Jul 2026)

> Plan sync audit: 2026-07-03. Source of truth after this pass: code + tests, then these plan docs.
> Audited by grep/read against `main`-era codebase on branch `docs/plan-sync-jul2026`.

---

## 1. Verified plan status

### Physics (`PLAN_REALISM.md`)

| ID | Doc claim | Code evidence | Verdict |
| --- | --- | --- | --- |
| R1–R3 | Ascent gravity turn + hot-staging at MECO | `AscentController.cs` MECO by speed/reserve, `TriggerStaging` | ✅ Done |
| R4 | Unified 9 m aero area | `AerodynamicsModel.EstimateReferenceArea` + `EffectiveArea`; `Vessel.ComputeDragAt` | ✅ Done (section was stale → fixed) |
| R5 | Multi-motor model | Still 1 engine part/stage per CLAUDE.md | ⬜ Pending (large) |
| R6 | Body lift / AoA | `ComputeLift`, `AerodynamicLiftTests.cs` (4 tests) | ✅ Done |
| R7 | Thermosphere / orbital decay | `AtmosphereModel` tail, `AtmosphereThermosphereTests`, `OrbitalDecayTests` | ✅ Done |
| R8 | `has_heat_shield` data-driven | `PartDefinition.HasHeatShield`, `ThermalModel`, `PhysicsRegressionTests` | ✅ Done (section was stale → fixed) |
| R9 | Touchdown ≤2 m/s | `EDLController.TouchdownVel = 3.0`; R13 telemetry ~0–1.5 m/s | ✅ Done (`SoftLandingThreshold` still 5.0 — damage gate, optional tighten) |
| R10 | ISP cluster ~363 s | `starship_engines.json` `isp_vac: 363` | ✅ Done (section was stale → fixed) |
| R11 | Systems tied to mission phases | `Systems/*` exist, not phase-wired | ⬜ Pending |
| R12 | Boostback / tower catch | Depends on R5 | ⬜ Blocked |
| R13 | Survivable belly-flop EDL | `EDLController` belly-flop until ~800 m flip; R13 telemetry in plan header | ✅ Done |

**Discrepancies fixed this session:** R4/R8/R9/R10 detail sections still read as open fixes despite header marking them done. `ROADMAP.md` still listed R6 lift and R7 thermosphere as pending — corrected.

### Visual (`PLAN_VISUAL_REALISM.md`)

| Track | Status | Evidence |
| --- | --- | --- |
| V0 capture harness | ✅ Working locally | `xvfb-run` + temp autoload pattern documented |
| V0 CI PNG artifacts | ⬜ Pending | CI has Xvfb smoke only, no PNG harness/artifacts |
| V1 exterior | ✅ First pass + close-ups | `VesselRenderer.cs` grid fins, serial bars, tiles, engine bay |
| V2 plumes / pad | ✅ Mostly done | `PlumeSystem`, `LaunchEffectsController`, `EngineStartupController`, `HotStageFlashController` |
| V2 hot-stage ref compare | ⬜ Pending | Code + local multiframe; no IFT reference compare in real ascent |
| V3 reentry VFX | ✅ Partial | `ReentryPlasmaController` flux-driven + localized glows; tuning/captures pending |
| V4 phase lighting | ✅ Space blend | `PhaseLightingController` altitude 70→130 km; reentry/cockpit overlay pending |
| V5 CI visual automation | ⬜ Pending | `.github/workflows/ci.yml` guard + Xvfb smoke, no capture artifacts |

### Playtest (`PLAN_PLAYTEST.md`)

| Item | Status |
| --- | --- |
| Main scene = `MainMenu.tscn`, flight via `Flight.tscn` | ✅ Verified in `project.godot` |
| `PhaseLightingController` V1 | ✅ Wired in `SimulationBridge` |
| Reentry lighting overlay | ⬜ Designed, reverted — blocked on DEORBIT→EDL harness milestone |
| End-to-end `_PlaytestShot.cs` pattern | 📋 Documented, not checked in (by design) |

---

## 2. Branch ownership matrix

Use **one agent per row** per session. Fetch before push; rebase if behind `main`.

| Branch prefix / focus | Owns (exclusive) | Do NOT touch |
| --- | --- | --- |
| `feat/visual-vessel-*` | `scripts/VesselRenderer.cs`, `data/parts/starship_*.json`, `super_heavy_booster.json` | `AscentController`, `EDLController`, sim physics |
| `feat/visual-plume-*` | `scripts/PlumeSystem.cs`, `LaunchEffectsController.cs`, `assets/shaders/raptor_plume.gdshader` | `VesselRenderer` mesh layout |
| `feat/visual-hotstage-*` | `scripts/HotStageFlashController.cs`, staging VFX hooks in `SimulationBridge.TriggerStaging` | Ascent staging logic (`AscentController`) |
| `feat/visual-reentry-*` | `scripts/ReentryPlasmaController.cs`, `ReentryBreakupController.cs`, reentry materials in `VesselRenderer` | `ThermalModel`, `EDLController` guidance |
| `feat/visual-lighting-*` | `scripts/PhaseLightingController.cs`, `SunController.cs`, `SkyController.cs`, `PlanetMaterials.cs` | Global blind tonemap changes (see PLAYTEST B1) |
| `feat/visual-capture-*` | Temp `scripts/_*Shot.cs`, `tools/ci_check.sh`, `.github/workflows/ci.yml` capture steps | Committed harness files (CI guard fails) |
| `feat/physics-*` | `ExosphereSimulation/**`, `ExosphereSimulation.Tests/**`, `data/bodies/*.json` | `Godot` imports in sim |
| `feat/flight-edl-*` | `scripts/EDLController.cs`, `AscentController.cs`, `MissionManager.cs` | Visual-only VFX unless telemetry proves regression |
| `feat/vab-*` | `scenes/construction/**`, `scripts/Construction*.cs`, `VabPickingLayer.cs` | Flight controllers |
| `feat/ui-*` | `scenes/ui/**`, `scripts/UI/**`, `MainMenu.cs`, HUD scripts | Sim bridge core |
| `docs/*` | `*.md`, `.atl/**` | Code unless fixing doc/code drift |

**Shared boundary:** `SimulationBridge.cs` — coordinate if multiple agents need new signals or API.

---

## 3. Shared rules (all agents)

1. **CI gate:** After any C# change run `bash tools/ci_check.sh` (or the three `dotnet` commands in `CLAUDE.md`).
2. **Harness cleanup:** Never commit `scripts/_*Shot.cs`, `*VerifyShot.cs`, `scenes/*VerifyShot.tscn`, or temp autoload edits in `project.godot`. Delete + `git checkout project.godot` before push.
3. **Visual validation:** Smoke = `--headless`. PNG proof = `xvfb-run` + real framebuffer (see `visual-testing` skill).
4. **Realism filter:** Do not retune drag/heating/EDL for VFX alone if xUnit or R13 telemetry breaks. Physics changes → add/update tests + optional `physics-reviewer`.
5. **Do not break [G] ascent or R13 EDL** without new telemetry harness comparing before/after.
6. **One coherent commit per task;** no generated `.godot/`, `bin/`, `obj/`, `*.uid`.
7. **Capture gating:** Gate screenshots on mission phase / altitude / physics state — never raw frame counts (see `PLAN_PLAYTEST.md`).
8. **Worktrees:** Rebase onto current `main` before merge; 3-way diff if base was stale.

---

## 4. Realism-first priority (next session)

Ranked by impact × evidence × not already closed:

1. **Hot-staging + startup reference compare (V2)** — Code exists; highest visual ROI. Real ascent multiframe capture vs IFT T+2:39/T+2:40. Owner: visual-hotstage + capture harness.
2. **DEORBIT→EDL playtest harness (PLAYTEST §1 milestone 7)** — Unblocks reentry lighting overlay and V3 nominal/failure captures. Owner: visual-capture (temp harness only).
3. **Reentry VFX tuning vs real EDL (V3)** — Flux-driven plasma works; pending alpha/timing/zone charring. Owner: visual-reentry. Requires harness from #2.
4. **R5 multi-motor (physics backlog)** — Largest remaining physics simplification; blocks R12 boostback. Defer until visual tranche stabilizes unless explicitly prioritizing physics.
5. **Harmonize landing damage threshold (R9 tail)** — Optional: lower `Universe.SoftLandingThreshold` from 5.0→~3.0 m/s to match EDL setpoint; needs regression test only if touched.

**Explicitly NOT next:** VAB rewrite, engine-out gameplay, global tonemap experiments, retuning R13 EDL without telemetry.

---

## 5. Self-grade rubric (per agent, end of session)

Copy into PR or session notes; score 1–5 each dimension.

| Dimension | 1 (fail) | 3 (acceptable) | 5 (excellent) |
| --- | --- | --- | --- |
| **Realism** | Worse vs reference/telemetry | Neutral / plausible | Matches reference or plan acceptance numbers |
| **Tests** | Broke CI or removed coverage | Existing tests still green | New meaningful xUnit or capture criterion added |
| **No-regression** | [G] or EDL broken | Untested but likely OK | Harness/telemetry shows parity or improvement |
| **Docs** | Plans drift further | No doc update needed | Plan checkbox/status updated with evidence |

**Minimum ship bar:** all dimensions ≥3, none at 1, `ci_check.sh` green.

---

## 6. Quick reference

- Plans: `PLAN_REALISM.md`, `PLAN_VISUAL_REALISM.md`, `PLAN_PLAYTEST.md`, `ROADMAP.md`
- Ops: `CLAUDE.md`
- Godot: `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64`
- Main menu scene: `res://scenes/ui/MainMenu.tscn` · Flight: `res://scenes/flight/Flight.tscn`
