# Oleada B+C — Coordination

Integrates physics (B) and mission UX (C) without file collisions.
Merge order into `main` is fixed: **B3 → B2 → B1 → C1 → C2 → C3**.

Started from tip `91563e8` (atmosphere V3 merge). One integrator owns merges/CI/docs;
feature agents stay inside ownership locks below.

## Status

| ID | Branch | Status | Notes |
|----|--------|--------|-------|
| Coord | `main` | DONE | This file + stale SoftLanding / EDL-lift sync |
| B3 | `feat/physics-leo-decay-rails` | WIP | LEO decay under warp/on-rails |
| B2 | `feat/physics-hotstage-overlap` | pending | Dual-thrust overlap window |
| B1 | `feat/physics-structural-breakup` | pending | Wire `FindBreakingJoints` → split |
| C1 | `feat/gameplay-save-load` | pending | Usable mission save/load |
| C2 | `feat/gameplay-deorbit-entry` | pending | Orbit → deorbit → ENTRY |
| C3 | `feat/ux-edl-mission-phases` | pending | Phase track + EDL cues |

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
