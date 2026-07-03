# Master Improvement Index — Exosphere

**Wave:** 3 (overnight Jul 2026)  
**Branch:** `docs/deep-audit-overnight-jul2026`  
**Scoring:** Impact (1–5) × Realism (1–5) × Feasibility (1–5) = **1–125**; tier **P0–P3** for delegation

---

## Audit index

| Document | Domain | Items |
|----------|--------|-------|
| [PHYSICS_AUDIT.md](./PHYSICS_AUDIT.md) | Wave 1 physics residuals | 8 |
| [PHYSICS_DEEP_AUDIT.md](./PHYSICS_DEEP_AUDIT.md) | Wave 1 deep physics (ascent, staging, aero) | 28 |
| [VISUAL_UX_AUDIT.md](./VISUAL_UX_AUDIT.md) | Wave 2 shaders + visual scripts | 16 |
| [VISUAL_DEEP_AUDIT.md](./VISUAL_DEEP_AUDIT.md) | Wave 1 visual deep (V-001…V-051) | 51 |
| [UX_EXPERIENCE_AUDIT.md](./UX_EXPERIENCE_AUDIT.md) | Wave 1 menu/HUD/flow (UX-001…024) | 24 |
| [GAMEPLAY_AUDIT.md](./GAMEPLAY_AUDIT.md) | Wave 1 playtest/systems | 8 |
| [GAMEPLAY_MISSION_AUDIT.md](./GAMEPLAY_MISSION_AUDIT.md) | Wave 1 mission arc (G-001…018) | 18 |
| [NAVIGATION_AUDIT.md](./NAVIGATION_AUDIT.md) | Transfers, map, SOI | 4 |
| [DATA_DRIFT_AUDIT.md](./DATA_DRIFT_AUDIT.md) | Doc/code/schema drift | 9 |
| [CROSSCUTTING_AUDIT.md](./CROSSCUTTING_AUDIT.md) | CI, audio, perf, merges | 14 |
| [../physics_audit.md](../physics_audit.md) | Legacy parameter audit — **refreshed Jul 2026** (DOC-01) | — |
| [.atl/OVERENGINEERING_AUDIT_JUL2026.md](../../.atl/OVERENGINEERING_AUDIT_JUL2026.md) | Dead code | 13 |
| [.atl/OVERNIGHT_LOOP_LOG.md](../../.atl/OVERNIGHT_LOOP_LOG.md) | Orchestrator progress log | — |

**Raw item count (all audits):** ~181  
**Total unique improvement items (deduplicated, realism-filtered):** **62**

---

## Tier definitions

| Tier | Score range | Meaning |
|------|-------------|---------|
| **P0** | ≥80 or validation blocker | Next session must address first |
| **P1** | 60–79 | High ROI realism/visual |
| **P2** | 36–59 | Important; schedule after P0/P1 |
| **P3** | ≤35 | Polish, blocked, or doc-only |

---

## Top 10 unified backlog (P0 tier)

| Rank | ID | Item | I×R×F | Owner branch | Evidence |
|------|-----|------|-------|--------------|----------|
| 1 | **VAL-01** | DEORBIT→EDL playtest harness + phase-gated xvfb PNG matrix | 100 | `feat/visual-capture-*` | `PLAN_PLAYTEST.md:58-130`; unblocks VS-12, GU-05, CC-01 |
| 2 | ~~**DOC-01**~~ | ~~Supersede stale `docs/physics_audit.md`~~ — **DONE** (`docs/refresh-realism-docs`) | 100 | `docs/*` | `DATA_DRIFT_AUDIT.md` DD-01–05 |
| 3 | **VIS-01** | Rebalance render booster/ship vertical split (71/50 m) | 75 | `feat/visual-vessel-*` | `VesselRenderer.cs:52-63,140-407` |
| 4 | **CC-01** | CI PNG capture + non-black heuristic + artifacts (V5) | 60 | `feat/visual-capture-*` | `ci.yml:103-124`; `PLAN_VISUAL_REALISM.md:241-257` |
| 5 | **PG-04** | Harmonize `SoftLandingThreshold` 5→~3 m/s with EDL | 60 | `feat/physics-*` | `Universe.cs:64`; `EDLController.cs:24` |
| 6 | **VS-01** | Wire or delete orphan `reentry_glow.gdshader` | 64 | `feat/visual-reentry-*` | Shader file; zero references |
| 7 | **VIS-03** | Hot-staging multiframe vs IFT T+2:39 reference | 64 | `feat/visual-hotstage-*` | `PLAN_VISUAL_REALISM.md:45-46`; V-012 |
| 8 | **V-017** | OLM centre hole legacy 1.15u not 9 m BodyR | 72 | visual-pad | `VISUAL_DEEP_AUDIT.md` V-017 |
| 9 | **V-024** | Belly-flop EDL nominal reference capture | 80 | visual-capture | `VISUAL_DEEP_AUDIT.md` V-024 |
| 10 | **PHYS-01** | MissionManager MECO fuel cut vs AscentController | 75 | `feat/flight-edl-*` | `MissionManager.cs:204-218`; P-A03 |

*Merged aliases (not separate P0): GU-01→VAL-01, DD-01→DOC-01, VS-09→VIS-01.*

---

## Full prioritized backlog (deduplicated)

### P0 — Next session (9 items — realism-filtered; mission P0s deferred per ROADMAP)

| ID | Summary | Domain | Agent |
|----|---------|--------|-------|
| VAL-01 | Playtest harness: pad→orbit→reentry→touchdown + `/tmp` log + PNG matrix | Validation | visual-capture |
| ~~DOC-01~~ | ~~Refresh physics audit / drift docs~~ — **DONE** Jul 2026 | Docs | docs |
| VIS-01 | VesselRenderer 71/50 m vertical proportions | Visual | visual-vessel |
| CC-01 | CI Xvfb PNG gate (V5) | CI | visual-capture |
| PG-04 | Landing threshold harmonization | Physics | physics |
| VS-01 | Reentry shader strategy (wire or delete) | Visual | visual-reentry |
| VIS-03 | Hot-stage IFT reference compare | Visual | visual-hotstage + capture |
| V-017 | OLM centre hole sized for legacy hull (9 m mismatch) | Pad | visual-pad |
| V-024 | Belly-flop EDL reference capture baseline | Visual | visual-capture |
| PHYS-01 | Single MECO authority (remove MissionManager fuel cut) | Gameplay | flight-edl |

### P1 — High (8 items)

| ID | Summary | Score |
|----|---------|-------|
| VS-12 | Reentry VFX alpha/timing (needs VAL-01) | 64 |
| VS-13 | Hot-stage in real ascent capture | 64 |
| DD-02 | Document mixture_ratio dual contract | 100→doc |
| CC-08 | Spatial engine audio (`AudioStreamPlayer3D`) | 48 |
| CC-09 | Reentry / Max-Q / hot-stage audio events | 48 |
| HYG-01 | Remove dead NavBall + duplicate warp input | 48 |
| VS-10 | Document or align sim 123.1 m vs render 121.1 m | 48 |
| VS-11 | Re-enable edge glow full-stack reentry | 36 |

### P2 — Medium (22 items)

| ID | Domain |
|----|--------|
| PG-01 | Multi-motor / R5 |
| PG-02 | On-rails decay at warp |
| PG-03 | EDL lift steering |
| PG-05 | Thermal surface area per part |
| PG-06 | Structural break-up physics |
| GU-02 | CI Flight.tscn smoke matrix |
| GU-03 | Save/load UI wiring |
| GU-04 | Systems per-phase mapping |
| GU-05 | Reentry lighting overlay |
| NV-01 | Lunar / patched transfer model |
| NV-02 | Second burn orchestration |
| NV-03 | Long-cruise soak test |
| DD-03–07 | Remaining doc/data drift |
| CC-02, CC-03, CC-06, CC-07 | CI local gap, manual skill, audio baseline |
| VS-02–03, VS-05–07, VS-14 | Shader/script polish |
| CC-11, CC-14 | Plume merge doc, ComputeLoads CPU |

### P3 — Low (10 items)

| ID | Domain |
|----|--------|
| PG-07, PG-08 | Atmo pressure tail; boostback (blocked R5) |
| GU-06, GU-07, GU-08 | Phase noise; EDL assist; plume legibility |
| NV-04 | Timeline maneuver nodes |
| DD-08, DD-09 | Decoupler doc; comment drift |
| VS-04, VS-08, VS-15 | Steel aniso; crater cost; liftoff camera |
| CC-05, CC-12, CC-13 | Construction Xvfb; particles; procedural cost |

---

## Agent delegation matrix (next session)

| Agent focus | Branch prefix | Pull first | Deliverables |
|-------------|---------------|------------|--------------|
| **Capture / validation** | `feat/visual-capture-*` | `main` | VAL-01 harness (temp, untracked); CC-01 CI step design doc |
| **Vessel proportions** | `feat/visual-vessel-*` | `main` | VIS-01 before/after xvfb pad lateral |
| **Hot-staging** | `feat/visual-hotstage-*` | capture branch | VIS-03 IFT compare PNG set |
| **Reentry VFX** | `feat/visual-reentry-*` | capture branch | VS-01 decision + VS-12 tuning |
| **Docs sync** | `docs/*` | this branch | DOC-01 **done** — legacy audit banner + drift fixes |
| **Physics threshold** | `feat/physics-*` | `main` | PG-04 + xUnit regression |
| **Audio** | `feat/audio-*` | `main` | CC-08/09 event surface (after VAL-01 for EDL cues) |
| **Navigation** | `feat/nav-*` | `main` | NV-01 lunar model spike (defer if visual tranche open) |
| **Hygiene** | `refactor/simplify-*` | `main` | HYG-01 dead controllers |

**Parallelization rule:** Only one agent per row in `.atl/DELEGATION_JUL2026.md` §2 per session. Capture agent unblocks reentry + lighting + CI PNG.

---

## Explicit NON-GOALS (overnight audit consensus)

Do **not** prioritize without new telemetry harness and explicit user request:

1. **VAB rewrite** — gizmos/menu are P2 UX; not realism-critical
2. **Engine-out gameplay** — breaks one-engine-per-stage contract (`CLAUDE.md`)
3. **R5 multi-motor** — large physics project; blocks boostback but defer until visual tranche stable
4. **Global tonemap / exposure experiments** — reverted once (`PLAN_PLAYTEST.md` B1); needs VAL-01
5. **Retuning R13 EDL guidance** — belly-flop telemetry is closed baseline
6. **HLS / lunar lander variant art** — wrong vehicle unless new part variant (`PLAN_VISUAL_REALISM.md:62-64`)
7. **Committed capture harness files** — CI guard forbids (`ci.yml:23-37`)
8. **Per-part drag_coefficient wiring** — realism change needing calibration (PG + DATA-01)
9. **Boostback / Mechazilla / tower catch** — blocked on R5
10. **Broad new gameplay** (missions, progression) — after visual acceptance matrix (ROADMAP order)

---

## Wave 4+ placeholder

Append new findings to `AUDIT_WAVE_N.md` when re-reading codebase. Re-score and update this index; one coherent commit per wave on `docs/deep-audit-overnight-jul2026`.

---

*Generated by overnight loop orchestrator — docs only, no code changes.*
