# Navigation Audit — Exosphere

**Wave:** 1 (overnight Jul 2026)  
**Scope:** `ExosphereSimulation/Navigation/`, `scripts/TransferPlanner.cs`, `MapViewController.cs`, `AutopilotController.cs`

---

## Executive summary

Hohmann core, patched-conic SOI transitions, encounter prediction, and map autopilot execution are **implemented and tested**. Gaps: **lunar transfer model**, **second burn orchestration**, **long-cruise soak tests**, **timeline maneuver nodes**.

**Update 2026-07-28:** the Godot-free lunar transfer core and its accuracy/warp
regressions are now implemented. `LunarTransferPlanner` solves a moving-target,
Earth-centred Lambert arc and a focused lunar approach. The remaining NV-01 gap
is product wiring: `scripts/TransferPlanner.cs` must select this path for `moon`
instead of its generic heliocentric Hohmann branch.

| Priority | Count |
|----------|-------|
| P0 | 0 |
| P1 | 0 |
| P2 | 3 |
| P3 | 1 |

---

## Findings

### NV-01 — Hohmann uses instantaneous heliocentric radii
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=4 R=4 F=3 → **48** |
| **Evidence** | `TransferPlanner.cs:47-51` (`r1 = |vessel−sun|`, `r2 = |target−sun|`); `HohmannTransferPlan.cs:14-44` |
| **Gap** | **Partially closed:** `LambertSolver.cs` + `LunarTransferPlanner.cs` provide the patched Earth–Moon model; map/UI selection still routes Moon through the old Sun-centred snapshot |
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

---

*Links: `MASTER_IMPROVEMENT_INDEX.md`*
