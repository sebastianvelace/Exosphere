# Crosscutting Audit — Exosphere

**Wave:** 2 (overnight Jul 2026)  
**Scope:** CI/visual testing, performance vs visual quality, audio, docs drift, over-engineering, duplicate backlog items  
**Method:** Cross-reference Wave 1 domain audits + targeted repo grep/read

---

## Executive summary

Cross-domain gaps cluster in **validation infrastructure** (no CI PNG gate), **audio realism** (procedural-only, non-spatial), **doc drift** (stale physics audit), and **sim/render dimension split**. Performance tradeoffs (merged plumes, procedural shaders) are intentional and documented in `CLAUDE.md`.

| Domain | P1 | P2 | P3 |
|--------|----|----|-----|
| CI / visual testing | 1 | 3 | 1 |
| Audio | 0 | 4 | 0 |
| Performance | 0 | 2 | 3 |
| Docs drift | 2 | 5 | 2 |
| Code hygiene | 0 | 1 | 0 |

---

## CI & visual testing

### CC-01 — No automated PNG capture or image assertions in CI
| | |
|---|---|
| **Priority** | **P1** |
| **Score** | I=5 R=4 F=3 → **60** |
| **Evidence** | `.github/workflows/ci.yml:103-124` (Xvfb smoke only, no harness/PNG); `PLAN_VISUAL_REALISM.md:37-38,241-257`; `ROADMAP.md:109-114` |
| **Gap** | Visual regressions won't fail CI; V5 not started |
| **Acceptance** | Xvfb + temp harness in CI job + non-black/heuristic + artifact upload |
| **Owner** | `feat/visual-capture-*` branch per `.atl/DELEGATION_JUL2026.md` |

### CC-02 — `tools/ci_check.sh` skips Xvfb when Godot local
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `ci_check.sh:28-42` documents Xvfb; runs headless smoke only (`:32-36`) |
| **Gap** | Local "full CI" ≠ visual validation |

### CC-03 — Headless smoke uses dummy renderer (no pixels)
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `ci_check.sh:32-36`; `.claude/skills/visual-testing/SKILL.md:8-9` |
| **Gap** | Compile/load only; by design per `CLAUDE.md` |

### CC-04 — Anti-harness guard works
| | |
|---|---|
| **Priority** | ✅ Closed |
| **Evidence** | `ci.yml:23-37`; `ci_check.sh:8-18` |
| **Note** | Prevents committed `_Shot` / `VerifyShot` scaffolding |

### CC-05 — Xvfb CI omits Construction scene
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `ci.yml:123` (main only) vs `ci.yml:100-101` (headless includes Construction) |

### CC-06 — Visual-testing skill is manual-only
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `.claude/skills/visual-testing/SKILL.md:13-38` — temp autoload + `/tmp` PNG |
| **Gap** | No golden-image diff; no phase-gated harness in repo |

---

## Audio

### CC-07 — Zero shipped audio assets
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | No `.ogg`/`.wav`/`.mp3` in repo; `AudioManager.cs:6-8` |
| **Gap** | All runtime synthesis via `AudioStreamGenerator` |

### CC-08 — No spatial engine audio (2D players only)
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `AudioManager.cs:37-40,126-133` — bus named `Engine3D` but no `AudioStreamPlayer3D` |
| **Gap** | Pad/chase cam lacks directional exhaust |

### CC-09 — No reentry / Max-Q / hot-staging audio
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `AudioManager.cs:337-352` events: countdown, liftoff, staging, crash only; `MissionManager.cs:251-252` |
| **Gap** | EDL and hot-stage visually rich, audibly thin |

### CC-10 — Engine audio follows sim throttle/density (good)
| | |
|---|---|
| **Priority** | ✅ Positive |
| **Evidence** | `AudioManager.cs:154-179` |
| **Note** | Reasonable physics coupling; extend event surface, don't replace |

---

## Performance vs visual quality tradeoffs

### CC-11 — 33+6 Raptor bells visual; 4+2 merged plume units (intentional)
| | |
|---|---|
| **Priority** | P2 (document) |
| **Evidence** | `VesselRenderer.cs:172-199`; `PlumeSystem.cs:54-97`; `CLAUDE.md` known limits |
| **Tradeoff** | Merged SL liftoff silhouette vs 33 independent plumes |
| **Realism filter** | Accept if IFT reference compare passes (VS-13) |

### CC-12 — Particle budget ~180 per SH ring unit
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `PlumeSystem.cs:335-339` |
| **Tradeoff** | Bounded cost; smoke legibility vs GPU |

### CC-13 — Procedural shaders vs textures (Earth, steel, planets)
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `earth_surface.gdshader`, `steel.gdshader`, `planet_body.gdshader` |
| **Tradeoff** | No heavy assets; FBM/crater loops cost at close range (VS-06, VS-08) |

### CC-14 — `ComputeLoads` every RK4 tick without break-up consumption
| | |
|---|---|
| **Priority** | P2 (CPU) |
| **Evidence** | `Universe.cs:208-211`; `.atl/OVERENGINEERING_AUDIT_JUL2026.md` P2-C |
| **Tradeoff** | Forward scaffolding vs per-tick cost; defer until break-up ships |

---

## Docs drift (merged from domain audits)

Duplicate items consolidated here — see `DATA_DRIFT_AUDIT.md` for full detail.

| ID | Item | Priority |
|----|------|----------|
| DD-01 | Stale drag claims in `docs/physics_audit.md` | P1 |
| DD-02 | Stale `mixture_ratio` unused claim | P1 |
| DD-03–09 | Thrust/ISP/decoupler/R11/comment drift | P2–P3 |

**Recommendation:** Mark `docs/physics_audit.md` superseded by `docs/audits/PHYSICS_AUDIT.md` header note (doc-only, no code).

---

## Duplicate backlog merge (cross-audit)

| Merged ID | Sources | Single owner |
|-----------|---------|--------------|
| **VAL-01** | GU-01, CC-01, CC-06, VS-12, V-050, V-024 | Playtest + CI capture harness |
| **VIS-01** | VS-09, DD stack table, V-002 proportions | `VesselRenderer.cs` layout |
| **VIS-02** | VS-01, VS-11, V-028 | Reentry VFX + lighting overlay |
| **VIS-03** | VS-13, CC-11, V-012 | Hot-stage IFT reference compare |
| **DOC-01** | DD-01, DD-02, P2-G overengineering | Refresh physics audit trail |
| **HYG-01** | GU-08, overengineering P1 | Dead controllers refactor |
| **DATA-01** | DD-06, PG unused drag, P2-A | Schema honesty for `drag_coefficient` |
| **PHYS-01** | P-A03, GU-06, MissionManager MECO | Single MECO authority |
| **UX-DEFER** | G-001…G-007 P0 mission items, UX-001…010 | **NON-GOAL** until VAL-01 + visual matrix (ROADMAP order) |

**Wave 1 parallel audits merged:** `PHYSICS_DEEP_AUDIT.md`, `VISUAL_DEEP_AUDIT.md`, `UX_EXPERIENCE_AUDIT.md`, `GAMEPLAY_MISSION_AUDIT.md`.

---

## Realism filter (crosscutting)

Every item above was scored against:

1. **Would a Starship engineer notice?** (visual/audio/proportion gaps = yes; dead NavBall = no)
2. **Does it block proving other claims?** (CI/harness = yes → elevated priority)
3. **Is the tradeoff documented?** (merged plumes, single engine/stage = yes in CLAUDE.md)
4. **Does fixing it risk R1–R3 / R13 telemetry?** (physics retunes = require harness first)

---

*Links: all domain audits, `MASTER_IMPROVEMENT_INDEX.md`*
