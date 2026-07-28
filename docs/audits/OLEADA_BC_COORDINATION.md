# Oleada B+C — Coordination

Integrates physics (B) and mission UX (C) without file collisions.
Merge order into `main` is fixed: **B3 → B2 → B1 → C1 → C2 → C3**.

Started from tip `91563e8` (atmosphere V3 merge). One integrator owns merges/CI/docs;
feature agents stay inside ownership locks below.

## Status

| ID | Branch | Status | Notes |
|----|--------|--------|-------|
| Coord | `main` | DONE | This file + stale SoftLanding / EDL-lift sync |
| B3 | `feat/physics-leo-decay-rails` | DONE | `RequiresOffRailsPhysics` keeps residual thermosphere in RK4 |
| B2 | `feat/physics-hotstage-overlap` | DONE | 1.5 s dual-thrust; physics-review CORRECTO CON NOTAS |
| B1 | `feat/physics-structural-breakup` | DONE | `FindBreakingJoints` → `SplitAtJoint` / debris; physics-review CORRECTO CON NOTAS (inherited StressSolver mass-side) |
| C1 | `feat/gameplay-save-load` | DONE | Usable mission save/load; MissionSaveSerializer + F5/F9 + Continue |
| C2 | `feat/gameplay-deorbit-entry` | DONE | Orbit → map `[B]` deorbit → COAST/RETRO → ENTRY via EDL |
| C3 | `feat/ux-edl-mission-phases` | DONE | Phase track COAST/RETRO + ENTRY cues; `MissionPhaseTrack` tests |

## B3→C3 summary (oleada complete)

| Lane | Landed | Still open |
|------|--------|------------|
| **B3** | LEO warp/on-rails residual-thermosphere decay | — |
| **B2** | Hot-stage 1.5 s dual-thrust overlap | — |
| **B1** | Structural breakup from overloaded joints | Control-loss consequences |
| **C1** | Usable mid-mission save/load (F5/F9 + Continue) | Slot metadata polish |
| **C2** | Orbit → map deorbit → ENTRY (no teleport) | Mission objectives scaffold |
| **C3** | HUD phase track + deorbit/EDL cues + light SFX | — |

**Follow-on:** control-loss ✅; visual oleada A ✅ (plasma phase alpha, flap/nose proportions, deluge silhouette). Still open: IFT side-by-side reference compare, R5 multi-motor.

## File locks

| Branch | Exclusive ownership | Must not touch |
|--------|---------------------|----------------|
| `feat/physics-leo-decay-rails` | rails policy in `Universe.cs`, `OrbitalDecayTests` | `ApplyPostIntegrationPhysics` |
| `feat/physics-hotstage-overlap` | `AscentStagingPolicy`, `AscentController`, overlap in `PartGraph`/`Vessel`, `docs/physics/hot_stage_overlap.md` | breakup path |
| `feat/physics-structural-breakup` | `StressSolver` consumers, split API, `ApplyPostIntegrationPhysics`, `ReentryBreakupController` | ascent staging policy |
| `feat/gameplay-save-load` | `SaveSystem.cs`, MainMenu/HUD save keys | EDL guidance |
| `feat/gameplay-deorbit-entry` | Map deorbit preset, Bridge deorbit API, deorbit→ENTRY tests | R13 guidance retune |
| `feat/ux-edl-mission-phases` | HUD phase track/cues, `AudioManager` phase SFX | physics staging |

## Already closed (do not reopen)

- Soft landing damage gate = `AscentStagingPolicy.SoftLandingSpeedMps` = **3.0 m/s** (Universe aliases it).
- EDL game-layer lift-up ~70° via `AerodynamicsModel.ComputeLiftUpEntryAxis` in `EDLController`.

## Verification after each merge

```bash
bash tools/ci_check.sh
```

Physics-reviewer required for B2 and B1; recommended for B3.
