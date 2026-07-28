# Navigation Audit — Exosphere

**Wave:** 1 (overnight Jul 2026)  
**Scope:** `ExosphereSimulation/Navigation/`, `scripts/TransferPlanner.cs`, `MapViewController.cs`, `AutopilotController.cs`

---

## Executive summary

Hohmann core, patched-conic SOI transitions, encounter prediction, lunar Lambert
planning and map execution are **implemented and tested**. Remaining gaps:
**second-burn orchestration**, **multi-day lunar window search** and
**timeline maneuver nodes**.

**Update 2026-07-28:** the Godot-free lunar transfer core, accuracy/warp
regressions and product wiring are implemented. Selecting `moon` now produces a
moving-target Earth-centred Lambert arc, focused lunar approach, TLI window and
LOI estimate instead of entering the heliocentric Hohmann branch.

| Priority | Count |
|----------|-------|
| P0 | 0 |
| P1 | 0 |
| P2 | 3 |
| P3 | 1 |

---

## Findings

### NV-01 — Hohmann uses instantaneous heliocentric radii — CLOSED
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=4 R=4 F=3 → **48** |
| **Evidence** | `TransferPlanner.PlanTransfer` dispatches `moon` before the heliocentric branch; `LunarTransferPlanner`, `LunarTransferPlannerTests`, `visual_playtest.sh --lunar-map` |
| **Gap** | Closed. The map shows the Earth-relative conic, lunar SOI, focused perilunium and TLI/LOI dossier. A 4.5 km/s practical-window guard prevents datum/plane mismatches from being presented as valid transfers |
| **Realism filter** | `LunarTransferPlannerTests`: Apollo-class TLI 2.8–3.5 km/s, lunar insertion estimate 0.7–1.5 km/s, SOI arrival 2–3.5 days and safe lunar perilunium |

### NV-02 — Second Hohmann burn not orchestrated
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=3 R=4 F=3 → **36** |
| **Evidence** | `TransferPlanner.cs:77-78,187-188` (`SecondBurnDv` stored); `AutopilotController.cs:8` (single node) |
| **Gap** | Player must manually replan/exec arrival circularization |

### NV-03 — Long-cruise / warp SOI not soak-tested — CLOSED
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=3 R=4 F=4 → **48** |
| **Evidence** | `NavigationRegressionTests.cs:43` (unit SOI tests); `ROADMAP.md:97` |
| **Gap** | Closed by long-cruise energy/finite-state regression plus the Apollo-class Earth→Moon maximum-warp SOI integration test |

### NV-04 — Maneuver nodes: local drag only, no mission timeline
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `MapViewController.cs:190-219` (mouse drag); `ROADMAP.md:99` partially stale |
| **Gap** | Chained nodes with ETAs not implemented |

---

## Closed baseline

- Hohmann + encounter: `HohmannTransferPlan.cs`, `TrajectoryPrediction.cs`
- Lunar Lambert + focused patched conic: `LambertSolver.cs`,
  `LunarTransferPlanner.cs`, `LunarTransferPlannerTests.cs`
- On-rails SOI: `Universe.cs:381+`, `NavigationRegressionTests.cs`
- Map execution: `MapViewController.cs`, `AutopilotController.cs`
- Lunar map framebuffer acceptance: `tools/visual_playtest.sh --lunar-map`

---

*Links: `MASTER_IMPROVEMENT_INDEX.md`*
