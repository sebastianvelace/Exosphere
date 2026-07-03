# Gameplay & UX Audit — Exosphere

**Wave:** 1 (overnight Jul 2026)  
**Scope:** `scripts/MissionManager.cs`, `AscentController.cs`, `EDLController.cs`, `SystemsController.cs`, `SaveSystem.cs`, HUD/UI, playtest plans

---

## Executive summary

Ascent [G] and EDL R13 are validated. Gameplay gaps center on **validation infrastructure**, **save/load wiring**, **systems phase mapping**, and **player agency on EDL**.

| Priority | Count |
|----------|-------|
| P0 | 0 |
| P1 | 1 |
| P2 | 4 |
| P3 | 3 |

---

## Findings

### GU-01 — No committed end-to-end playtest harness
| | |
|---|---|
| **Priority** | **P1** (validation gate) |
| **Score** | I=5 R=5 F=4 → **100** |
| **Evidence** | `PLAN_PLAYTEST.md:58-77` (temp `_PlaytestShot.cs`, cleanup mandatory); `PLAN_PLAYTEST.md:113-130` (reentry lighting blocked); `ROADMAP.md:109-114` |
| **Gap** | Cannot prove pad→orbit→reentry→touchdown regressions with telemetry + xvfb PNGs gated on physics state. |
| **Realism filter** | Blocks all visual acceptance claims |

### GU-02 — Main menu entry; flight not default smoke path
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `project.godot:19` (`MainMenu.tscn`); CI smoke loads main scene only |
| **Gap** | Automated runs must explicitly target `Flight.tscn` for mission path |

### GU-03 — Save/load implemented but not wired to UI
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `SaveSystem.cs:24-51`; no callers outside `SaveSystem.cs` (grep) |
| **Gap** | Player cannot quicksave mid-mission; `MissionPhase` not first-class in save |

### GU-04 — Systems partially connected; binary phase mapping
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `SystemsController.cs:42-60,81-85`; `SystemsMissionPhaseTests.cs` |
| **Gap** | Idle vs Active only; not per `MissionPhase` (ascent/orbit/EDL loads) |

### GU-05 — Phase lighting reentry overlay blocked
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `PLAN_PLAYTEST.md:107-130` |
| **Gap** | Cannot tune reentry exposure without GU-01 harness |

### GU-06 — Dual "in orbit" criteria (MissionManager vs AscentController)
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `MissionManager.cs:236-238` (alt>150km && speed>7500); `AscentController.cs:259-270` (periapsis) |
| **Gap** | HUD phase noise, not trajectory |

### GU-07 — EDL fully autonomous; no [H] assist
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `EDLController.cs:88-100` auto-arms; `AscentController.cs:162-185` has [H] for ascent |
| **Gap** | Player cannot assist flip-and-burn |

### GU-08 — Over-engineering: dead NavBallController + duplicate warp input
| | |
|---|---|
| **Priority** | P2 (code hygiene) |
| **Evidence** | `.atl/OVERENGINEERING_AUDIT_JUL2026.md` P1-A/B — `NavBallController.cs`, `TimeWarpController.cs` vs `WarpController.cs` |
| **Gap** | Per-frame dead work; duplicate `[.]`/`[,]` handlers |
| **Note** | Fix branch `refactor/simplify-dead-controllers` may exist |

---

*Links: `VISUAL_UX_AUDIT.md`, `CROSSCUTTING_AUDIT.md`, `MASTER_IMPROVEMENT_INDEX.md`*
