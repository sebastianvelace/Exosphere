# Data & Documentation Drift Audit — Exosphere

**Wave:** 1 (overnight Jul 2026)  
**Scope:** `docs/physics_audit.md`, `PLAN_REALISM.md`, `ROADMAP.md`, `data/parts/*.json`, schema honesty

---

## Executive summary

`docs/physics_audit.md` (2026-06-13) contains **material drift** from current code and data. Several JSON fields are **documentation-only** or **unused by runtime**. This audit tracks drift; fixes are doc-only in this overnight pass.

| Priority | Count |
|----------|-------|
| P0 | 0 |
| P1 | 2 |
| P2 | 5 |
| P3 | 2 |

---

## Findings

### DD-01 — `docs/physics_audit.md` claims hardcoded inline drag
| | |
|---|---|
| **Priority** | **P1** |
| **Score** | I=4 R=5 F=5 → **100** |
| **Evidence** | `docs/physics_audit.md:18-21` ("hardcoded… `AerodynamicsModel` not used"); **actual:** `Vessel.cs:164-186` delegates to `AerodynamicsModel` |
| **Fix** | Update or supersede with `docs/audits/PHYSICS_AUDIT.md` |

### DD-02 — `docs/physics_audit.md` claims `mixture_ratio` unused
| | |
|---|---|
| **Priority** | **P1** |
| **Evidence** | `docs/physics_audit.md:14-17`; **actual:** `PartGraph.cs:313-318`, `Part.cs:199-201` use `MixtureRatio` when >0 |
| **Fix** | Document dual contract: tank capacities = load; engine field = burn O/F |

### DD-03 — SH thrust table stale (69/74.4 MN)
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `docs/physics_audit.md:32-33,59-61`; **actual:** `super_heavy_booster.json:14-15` (74.4 / 83.5 MN) |

### DD-04 — Starship `isp_vac` listed as 380 s
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `docs/physics_audit.md:39`; **actual:** `starship_engines.json:14` (`isp_vac`: 363) |

### DD-05 — "No decay above 140 km" obsolete post-R7
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `docs/physics_audit.md:51`; **actual:** `earth.json:16-17`, `AtmosphereModel.cs:164-168` thermosphere tail |

### DD-06 — Per-part `drag_coefficient` JSON unused
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | All `data/parts/*.json`; `PartDefinition.cs:32`; `AerodynamicsModel.cs:98-101` (Cd in code) |
| **Fix** | Wire or mark deprecated in data schema docs |

### DD-07 — `PLAN_REALISM.md` R11 "systems disconnected" partially stale
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `PLAN_REALISM.md:217-221`; **actual:** `SystemsController.cs`, `SystemsHUD`, tests exist |
| **Fix** | Reframe as per-phase tuning, not greenfield |

### DD-08 — Decoupler reference uses `decoupler_medium` not stack default
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `docs/physics_audit.md:44` (60 kg); stack uses `decoupler_heavy.json:6` (5000 kg in 200 t SH dry) |

### DD-09 — AscentController comment vs gravity-turn law
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `AscentController.cs:384-386` comment "~10% vertical"; code uses `GravityTurnElevationDeg` at `59-64` |

---

## Parts data vs real Starship (cross-ref Wave 2)

| Segment | Sim (m) | Render (m) | Real (~m) | Severity |
|---------|---------|------------|-----------|----------|
| Ø | 9.0 | 9.0 (BodyR 1.607) | 9.0 | ✅ |
| Booster | 71.0 | ~61.6 | ~71 | P1 skew |
| Ship | 52.1 | ~59.5 | ~50 | P1 skew |
| Total | 123.1 | ~121.1 | ~121 | P2 split |

Evidence: part JSON lengths; `VesselRenderer.cs:41-63`; `StarshipRealismTests.cs:23`.

---

*Links: `PHYSICS_AUDIT.md`, `VISUAL_UX_AUDIT.md`, `MASTER_IMPROVEMENT_INDEX.md`*
