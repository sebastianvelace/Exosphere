# Exosphere — Next Session Improvement Plan (Jul 2026)

> **Mission:** Make the player *feel* they are flying a real Starship/Super Heavy mission — not a toy rocket with SpaceX paint. Every item below must pass the realism filter: *"How does this make the player FEEL a real space mission?"*
>
> **Evidence base:** `PLAN_REALISM.md`, `PLAN_VISUAL_REALISM.md`, `PLAN_PLAYTEST.md`, `ROADMAP.md`, `.atl/DELEGATION_JUL2026.md`, `.atl/OVERENGINEERING_AUDIT_JUL2026.md`, git history through `4906dfd` (Jul 2026 session).
>
> **Verification standard:** Visual items → xvfb PNG capture criteria (state-gated, never frame-count). Physics items → xUnit + telemetry harness. Gameplay items → end-to-end play harness milestone.

---

## Executive Summary

Exosphere has closed the core physics arc: Flight 7 baseline (mass, thrust, O/F, 33+6 engine counts), gravity-turn ascent with hot-staging at MECO, survivable belly-flop EDL, body lift, thermosphere decay, and patched-conic navigation. The visual arc has a strong first pass — procedural Starship/Super Heavy, deluge plume, startup ramp, hot-stage flash, vacuum plume, localized reentry glow, and altitude-blended space lighting — but most VFX is **implemented, not reference-verified**.

The next session should **not** open large new systems. Highest ROI is:

1. **See the whole mission** — a state-gated play harness that captures pad → liftoff → Max-Q → hot-staging → orbit → deorbit → EDL with PNG + telemetry at each milestone.
2. **Close the V0.5 audit gap** — compare hot-staging, startup, and reentry captures against real IFT/Flight 7 references; tune only what the diff shows.
3. **Ship reentry lighting + zone charring** — blocked today because the harness cannot produce a belly-flop EDL frame for before/after verification.

Physics backlog (R5 multi-motor, R11 systems, R12 boostback) stays deferred until the visual tranche is reference-stable. Over-engineering cleanup (dead NavBall, duplicate warp) is a low-risk parallel lane.

---

## Completed This Session

Evidence from git log and plan audits (no `.atl/agent-*-log.md` files existed at plan time).

| Area | Deliverable | Evidence |
| --- | --- | --- |
| Physics | Starship Flight 7 calibration | `75cf9a8` — `StarshipRealismTests.cs` (234 lines), `docs/starship_physics_baseline.md`, `PartGraph` mixture ratio + engine count, unified aero envelope |
| Physics | R6 body lift / AoA | `68379e5` — `AerodynamicsModel.ComputeLift`, `AerodynamicLiftTests.cs` |
| Physics | R7 thermosphere / LEO decay | `5e88294` — `AtmosphereThermosphereTests`, `OrbitalDecayTests` |
| Visual | Phase lighting V1 (pad → space) | `8a5c4a9` — `PhaseLightingController.cs` altitude blend 70→130 km; xvfb pad identical, orbit metallic contrast |
| Visual | Hot-staging VFX | `4948077` — `HotStageFlashController.cs`; multiframe `/tmp/exosphere_hotstage_after_*.png` |
| Visual | Engine startup ramp | `f95392e` — `EngineStartupController.cs`; multiframe `/tmp/exosphere_startup_*.png` |
| Visual | Vacuum plume clean + SL punch | `390a7ce`, `98b43b8`, `4d971ad` — `PlumeSystem.cs`, `raptor_plume.gdshader` |
| Visual | Reentry localized glow V1 | `97bba94` — `ReentryPlasmaController.cs` nose/belly/flap edges |
| Visual | Grid fins + Starship close-up cues | `c7d47d4`, `fb8a01b` — `VesselRenderer.cs` |
| Visual | Coastal pad polish | `380ecf2`, `3649778` — `LaunchPadController.cs` |
| UI | Monochrome menu + HUD reorganize | `d95003a` — `scenes/ui/MainMenu.tscn`, HUD scripts |
| Docs / ops | Playtest harness design | `c0b9559`, `4906dfd` — `PLAN_PLAYTEST.md` reentry lighting design + deorbit unblock |
| Docs | Plan sync audit | `.atl/DELEGATION_JUL2026.md` — verified R1–R13 status vs code |
| Refactor | Over-engineering audit | `.atl/OVERENGINEERING_AUDIT_JUL2026.md` — P1 dead NavBall + duplicate warp identified |

**Already closed (do not reopen without regression proof):** R1–R4, R6–R10, R13 ascent/EDL, V1 exterior first pass, V2 plume/pad first pass.

---

## Visual Track (Prioritized)

Each item: **evidence → owner files → acceptance (xvfb) → realism rationale**.

### V-P1. End-to-end play harness with milestone captures

| | |
| --- | --- |
| **Evidence** | `PLAN_PLAYTEST.md` §1 documents the pattern; hot-staging and reentry lighting are blocked without state-gated DEORBIT→EDL. Frame-count captures overshoot Max-Q (`PLAN_PLAYTEST.md:27-30`). |
| **Owner** | Temp `scripts/_PlaytestShot.cs` (untracked), `SimulationBridge.cs` (read-only API), launch via `res://scenes/flight/Flight.tscn` |
| **Acceptance** | `/tmp/exo_play.log` + PNGs for: pad, liftoff (alt 80–350 m), Max-Q (phase `MAX_Q`), SEPARATION/hot-stage, ORBIT, deorbit heat rise, EDL belly-flop, touchdown. Each line records `alt, spd, q, heatRatio, phase`. Harness deleted; `git status` clean. |
| **Realism feel** | The team (and player) can *see* the same mission arc SpaceX webcasts — not infer it from logs. Unblocks every remaining visual item. |

### V-P2. Hot-staging reference compare (real ascent)

| | |
| --- | --- |
| **Evidence** | `HotStageFlashController` verified with forced trigger only (`PLAN_VISUAL_REALISM.md:149-156`). V0.5 matrix row: IFT T+2:39/T+2:40 vs `/tmp/exosphere_hotstage_*.png`. |
| **Owner** | `scripts/HotStageFlashController.cs`, `scripts/PlumeSystem.cs`, `scripts/VesselRenderer.cs`, `scripts/SimulationBridge.cs` (VFX hooks only) |
| **Acceptance** | Multiframe capture during `[G]` ascent at `SEPARATION`: flash/plume between stages visible; booster ring scorched; Ship engines lit; HUD legible. Side-by-side notes vs IFT reference (link in `PLAN_VISUAL_REALISM.md:60-61`). Tune only flash intensity, soot ring, encuadre. |
| **Realism feel** | Hot-staging is Starship's signature maneuver — the moment the player knows this is not Kerbal staging. |

### V-P3. Startup/ramp reference compare

| | |
| --- | --- |
| **Evidence** | `EngineStartupController` multiframe exists; pending IFT chill/startup timing compare (`PLAN_VISUAL_REALISM.md:144-147`). |
| **Owner** | `scripts/EngineStartupController.cs`, `scripts/PlumeSystem.cs`, `scripts/LaunchEffectsController.cs` |
| **Acceptance** | Capture T-3s hold-down through liftoff: progressive ignition (not off→full), vapor/chill visible, no theatrical overshoot. Compare against Flight 4–7 webcast startup frames. |
| **Realism feel** | Real launches have tension before release — the player should feel hold-down, not an instant teleport to full thrust. |

### V-P4. Reentry lighting overlay (PhaseLightingController reentry phase)

| | |
| --- | --- |
| **Evidence** | Designed in `PLAN_PLAYTEST.md` B1; reverted because harness could not produce belly-flop frame. Space blend V1 shipped (`PhaseLightingController.cs`). |
| **Owner** | `scripts/PhaseLightingController.cs`, `scripts/ReentryPlasmaController.cs` (read flux thresholds) |
| **Acceptance** | Requires V-P1 milestone 7 first. Before/after xvfb: belly-flop EDL shows warm emissive-dominant look (ambient ↓, glow ↑) without washing HUD/cockpit. Pad and orbit captures unchanged vs baseline. |
| **Realism feel** | Reentry is fire and steel — the scene should go dark except the windward fireball, like cockpit footage. |

### V-P5. Reentry VFX tuning + zone charring

| | |
| --- | --- |
| **Evidence** | Flux-driven plasma works; synthetic capture only (`/tmp/exosphere_reentry_edges.png`). Pending: nominal vs bad-attitude compare, per-zone charring (`PLAN_VISUAL_REALISM.md:190-196`). |
| **Owner** | `scripts/ReentryPlasmaController.cs`, `scripts/VesselRenderer.cs` (tile charring), `ReentryBreakupController.cs` |
| **Acceptance** | Two captures: (a) belly-flop nominal — protected, controlled; (b) forced bad attitude — localized nose/flap heating before breakup. Nose/flaps/belly char at different rates tied to `Part.ThermalDamage` zones. Plasma/wake do not obscure navball/HUD. |
| **Realism feel** | Flight 4–6 tile damage showed that orientation *matters* — the player should read danger before telemetry screams. |

### V-P6. V0.5 reference audit sweep (remaining matrix rows)

| | |
| --- | --- |
| **Evidence** | `PLAN_VISUAL_REALISM.md:68-80` matrix; several rows still `implementado` not `comparado contra referencia`. |
| **Owner** | Per-row owners in matrix (`VesselRenderer`, `PlumeSystem`, `PlanetMaterials`, `SkyController`, etc.) |
| **Acceptance** | Fill the matrix diff column for: pad lateral, liftoff, orbit burn, orbit/map beauty, touchdown/flip. Each row gets before/after PNG + one-sentence diff. |
| **Realism feel** | Stops "looks good to me" drift — the ship reads as 9 m × 121 m at every phase. |

### V-P7. CI visual artifacts (V5)

| | |
| --- | --- |
| **Evidence** | CI has Xvfb smoke only; no PNG artifacts (`.atl/DELEGATION_JUL2026.md:33,39`). |
| **Owner** | `.github/workflows/ci.yml`, `tools/ci_check.sh` |
| **Acceptance** | CI job produces downloadable PNG artifacts; heuristic fails on >95% black frame or zero non-background pixels in vessel bbox. Harness stays untracked — CI invokes ephemeral script. |
| **Realism feel** | Regressions that make the rocket invisible ship before players do. |

### V-P8. Camera / atmosphere polish (V4 tail)

| | |
| --- | --- |
| **Evidence** | Reentry/cockpit lighting pending; horizon/atmosphere gradient basic (`PLAN_VISUAL_REALISM.md:227-237`). |
| **Owner** | `scripts/CameraController.cs`, `scripts/SkyController.cs`, `scripts/SunController.cs`, `scripts/PlanetMaterials.cs` |
| **Acceptance** | xvfb: pad/ascent/reentry/cockpit each distinguishable by color/exposure without filters; stack full-frame at liftoff; cockpit instruments legible at Max-Q plume brightness. |
| **Realism feel** | Scale reads correctly — pad feels huge, orbit feels silent and contrasty, reentry feels hot. |

---

## Physics Track (Prioritized)

Each item: **evidence → owner files → acceptance (xUnit + telemetry) → realism rationale**.

### P-P1. Landing damage threshold harmonization (R9 tail) — DONE

`Universe.SoftLandingThreshold` now aliases `AscentStagingPolicy.SoftLandingSpeedMps = 3.0`.
Covered by `SoftLandingThresholdTests`.

### P-P2. EDL lift-aware guidance (R6 game-layer) — DONE

`EDLController` aims via `AerodynamicsModel.ComputeLiftUpEntryAxis` (~70° AoA lift-up)
instead of pure broadside. Do not reopen without regression proof against R13 telemetry.

### P-P3. Long-cruise / warp decay documentation + test

| | |
| --- | --- |
| **Evidence** | Orbital decay works in RK4; on-rails at warp ≥10 skips decay (`PLAN_REALISM.md:27-28`). |
| **Owner** | `ExosphereSimulation/Universe.cs`, `ExosphereSimulation.Tests/OrbitalDecayTests.cs` |
| **Acceptance** | Document warp gate in `PLAN_REALISM.md`; test confirms decay at warp 1–5 at 150 km; no behavior change unless product decides otherwise. |
| **Realism feel** | LEO isn't forever — the player learns orbits need maintenance (even if slow). |

### P-P4. Interplanetary lunar transfer refinement

| | |
| --- | --- |
| **Evidence** | Hohmann heliocentric simplification; ROADMAP lists imprecise Moon transfer. |
| **Owner** | `ExosphereSimulation/Navigation/TransferPlanner.cs`, tests in `ExosphereSimulation.Tests/` |
| **Acceptance** | xUnit: Earth→Moon transfer Δv and time within ~10% of patched-conic reference; existing Hohmann tests still pass. |
| **Realism feel** | Going to the Moon feels like a real transfer window, not a straight line cheat. |

### P-P5. Structural break-up completion (P2-C scaffold) — DONE

| | |
| --- | --- |
| **Evidence** | `Universe.TryStructuralBreakup` consumes `FindBreakingJoints`; `PartGraph.SplitAtJoint` / `Vessel.BreakAtJoint` spawn debris; `StructuralBreakupTests`. |
| **Owner** | `ExosphereSimulation/Physics/StressSolver.cs`, `Universe.cs`, `ReentryBreakupController.cs` |
| **Acceptance** | Overload breaks joints; VFX + vessel destruction; xUnit on joint break threshold; R13 nominal EDL unaffected. |
| **Realism feel** | Max-Q and bad reentry can tear the stack — failure is physical, not a red screen. |
| **Status** | **DONE** (oleada B1). Control-loss consequences still pending. |

### P-P6. R5 multi-motor model (LARGE — defer unless physics sprint)

| | |
| --- | --- |
| **Evidence** | CLAUDE.md contract: 1 physical engine part/stage. Flight 7 adds `EngineCount` for thrust scaling but not per-engine state. |
| **Owner** | `ExosphereSimulation/Parts/*`, `data/parts/*.json`, render sync |
| **Acceptance** | Shutting one engine reduces thrust proportionally and shifts TCE; engine-out test; staging unchanged for nominal 33/6. |
| **Realism feel** | Enables engine-out, asymmetric thrust, real boostback — the next leap in "this could fail like real life." |
| **Status** | **Defer** until visual tranche stable (`.atl/DELEGATION_JUL2026.md:94`). |

---

## Gameplay Track (Save/Load, VAB, Missions)

### G-P1. Wire SaveSystem to UI + mission persistence — DONE

| | |
| --- | --- |
| **Evidence** | `MissionSaveSerializer` in `ExosphereSimulation/Persistence`; `SaveSystem` wraps I/O + phase/warp; MainMenu Continue + HUD F5/F9; `MissionSaveLoadTests` mid-orbit roundtrip. |
| **Owner** | `ExosphereSimulation/Persistence/*`, `scripts/SaveSystem.cs`, `MainMenu.cs`, `HUDController.cs`, `SimulationBridge.cs` |
| **Acceptance** | Main menu Continue when slots exist. In-flight F5/F9. Reload restores vessel kinematics, fuel, time, phase; ActiveVessel Id stable. |
| **Realism feel** | Real missions span hours — the player can pause life and resume the same orbit tomorrow. |

### G-P1b. EDL mission-phase UX cues — DONE (C3)

| | |
| --- | --- |
| **Evidence** | `MissionPhaseTrack` + HUD dots ORBIT→COAST→RETRO_BURN→ENTRY…; actionable cue “ENTRY INTERFACE in ~Xm” / “DEORBIT BURN”; event log + light SFX for ENTRY/RETRO. `MissionPhaseTrackTests`. |
| **Owner** | `ExosphereSimulation/Flight/MissionPhaseTrack.cs`, `scripts/HUDController.cs`, `MissionManager.cs`, `AudioManager.cs` |
| **Acceptance** | Phase track lights deorbit/EDL slots; COAST driven by AutopilotController (unchanged); THERMAL panel untouched. |
| **Realism feel** | After SECO the player still sees a mission arc through entry — not a silent coast into fire. |
| **Status** | **DONE** (oleada C3 + control-loss). Oleada B+C landed: save/load, deorbit→ENTRY, phase cues, structural breakup, LEO warp decay, hot-stage overlap, control-loss authority. **Remaining:** visual oleada A, R5. |

### G-P2. VAB pre-launch validation pass

| | |
| --- | --- |
| **Evidence** | VAB V1.5 has save/load/browser; ROADMAP lists "validacion visual de crafts guardados antes de launch." |
| **Owner** | `scripts/ConstructionController.cs`, `scenes/construction/Construction.tscn` |
| **Acceptance** | Launch blocked with readable errors: no engine, no decoupler, CoM outside pad, TWR < 1. Preview shows same mesh as flight (`VesselRenderer` path). |
| **Realism feel** | You don't fly a craft that can't physically leave the pad — same gate as real range safety (simplified). |

### G-P3. Mission objectives scaffold (first milestone)

| | |
| --- | --- |
| **Evidence** | `MissionManager.cs` tracks phases (LIFTOFF…ORBIT…ENTRY); C2 now reaches ENTRY via map deorbit. Still no win/lose objectives. |
| **Owner** | `scripts/MissionManager.cs`, `HUDController.cs` |
| **Acceptance** | One scripted mission: "Reach 150 km orbit and deorbit to soft landing." Success/fail banner with phase checklist. Telemetry log exported on completion. *(Prerequisite path ORBIT→deorbit→ENTRY is C2 DONE.)* |
| **Realism feel** | A mission has a beginning, middle, and end — like Flight 7's objective, not sandbox forever. |

### G-P4. Systems tied to mission phases (R11)

| | |
| --- | --- |
| **Evidence** | `SystemsController.cs` simulates life/power/comms/thermal; not phase-wired (`.atl/DELEGATION_JUL2026.md:22`). |
| **Owner** | `ExosphereSimulation/Systems/*`, `scripts/SystemsController.cs` |
| **Acceptance** | Eclipse → solar power drop; comms delay scales with distance; HUD shows consequences. xUnit on power in shadow. |
| **Realism feel** | Orbital ops have constraints — the player manages power and comms like a real flight director. |

### G-P5. VAB UX backlog (lower priority)

| Item | Owner | Acceptance | Realism feel |
| --- | --- | --- | --- |
| Drag/rotate gizmos | `ConstructionController.cs`, `VabPickingLayer.cs` | Reposition part in preview; export matches | Build the rocket you imagine, not just stack up |
| Compatible node feedback | `VabPickingLayer.cs` | Green/red node highlights on hover | Attachment rules match real stacking constraints |
| Dedicated menu flow | `MainMenu.cs`, `Construction.tscn` | VAB from menu without flight detour | Professional vehicle assembly building |

---

## Over-Engineering Follow-Ups

From `.atl/OVERENGINEERING_AUDIT_JUL2026.md`. Safe parallel lane — does not change mission feel if done correctly.

| ID | Action | Owner | Acceptance | Notes |
| --- | --- | --- | --- | --- |
| P1-A | Delete dead `NavBallController` | `scripts/NavBallController.cs`, `Flight.tscn` | `ci_check.sh` green; navball still works via `AttitudeNavball` | −86 LOC |
| P1-B | Consolidate warp input | Remove `TimeWarpController`; extend `WarpController` | `[`/`]`/`Backspace` warp unchanged; Godot smoke pass | −45% warp LOC |
| P2-G | Refresh `docs/physics_audit.md` | docs only | Drag delegation + mixture ratio usage documented | Doc drift, not runtime |
| P2-A–F | Defer | — | — | Need realism calibration or future features |

**Rule:** No ascent `[G]`, EDL R13, or hot-staging timing changes in refactor lane.

---

## Explicit NON-GOALS (Next Session)

| Non-goal | Why |
| --- | --- |
| **R5 full multi-motor / engine-out gameplay** | Largest sim contract break; blocks on visual stability; 33/6 remain visual-only |
| **R12 boostback + Mechazilla catch** | Depends on R5 |
| **VAB rewrite** | V1.5 works; gizmos are incremental |
| **Global tonemap / ACES experiments** | Proven to subexpose orbit (`PLAN_PLAYTEST.md` B1) |
| **Retune drag/heating for VFX** | Physics serves telemetry, not screenshots |
| **External GLB assets for Starship** | Procedural mesh meets scale; avoid asset weight |
| **Draggable maneuver nodes (full)** | Map UX large; Hohmann core exists |
| **Crew EVA gameplay** | Out of realism tranche scope |
| **Committing visual harnesses** | CI guard rejects `_PlaytestShot.cs`, `*VerifyShot*` |

---

## Suggested Agent Delegation Matrix (Next Loop)

One agent per row; fetch + rebase before push. See `.atl/DELEGATION_JUL2026.md` for full ownership table.

| Agent slot | Branch prefix | Primary deliverable | Depends on | Verification |
| --- | --- | --- | --- | --- |
| **Capture lead** | `feat/visual-capture-*` | V-P1 play harness + milestone PNG/log | — | xvfb `/tmp/exo_play.*`; clean git status |
| **Hot-stage visual** | `feat/visual-hotstage-*` | V-P2 reference compare + tune | Capture lead (real ascent frame) | Multiframe vs IFT |
| **Plume/launch visual** | `feat/visual-plume-*` | V-P3 startup compare | Capture lead | Startup multiframe |
| **Reentry visual** | `feat/visual-reentry-*` | V-P4 + V-P5 lighting + charring | Capture lead (EDL frame) | Nominal + bad-attitude PNGs |
| **Lighting/atmosphere** | `feat/visual-lighting-*` | V-P8 cockpit/reentry tail | Reentry visual (V-P4) | Per-phase xvfb |
| **CI visual** | `feat/visual-capture-*` | V-P7 CI artifacts | V-P1 pattern proven | CI artifact download |
| **Physics polish** | `feat/physics-*` | P-P1 landing threshold | — | xUnit + EDL harness |
| **Refactor** | `refactor/simplify-*` | P1-A, P1-B dead code | — | `ci_check.sh` |
| **Gameplay** | `feat/gameplay-*` | G-P1 save UI (if visual tranche done) | — | Save/load roundtrip |
| **Docs** | `docs/*` | Plan checkbox updates with evidence | Any agent | PR links to PNG/test names |

**Coordination:** `SimulationBridge.cs` is shared boundary — announce new signals before merge.

**Minimum ship bar (`.atl/DELEGATION_JUL2026.md` §5):** realism ≥3, tests ≥3, no-regression ≥3, docs updated — all on 1–5 scale; `ci_check.sh` green.

---

## Recommended Session Order

1. **Capture lead** ships V-P1 (unblocks everything visual).
2. **Hot-stage + plume** agents run V-P2/V-P3 in parallel against harness output.
3. **Reentry visual** ships V-P4/V-P5 once milestone 7 exists.
4. **Refactor** agent lands P1-A/P1-B if CI bandwidth allows (orthogonal).
5. **Physics polish** P-P1 only if no EDL regression in harness.
6. **Gameplay** G-P1 only after visual acceptance matrix has ≥5 rows verified.

---

## Quick Reference

```bash
# Pre-flight
bash tools/ci_check.sh

# Flight scene (not MainMenu)
GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" \
  --path . --rendering-driver opengl3 res://scenes/flight/Flight.tscn

# Harness cleanup (mandatory)
git checkout project.godot && git status  # no _*Shot.cs tracked
```

**Plans:** `PLAN_REALISM.md` · `PLAN_VISUAL_REALISM.md` · `PLAN_PLAYTEST.md` · `ROADMAP.md` · `.atl/DELEGATION_JUL2026.md`
