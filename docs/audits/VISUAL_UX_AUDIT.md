# Visual & UX Realism Audit — Exosphere

**Wave:** 1–2 (overnight Jul 2026)  
**Scope:** `scripts/VesselRenderer.cs`, `ReentryPlasmaController.cs`, `PlumeSystem.cs`, `assets/shaders/`, lighting/camera/pad  
**Reference plan:** `PLAN_VISUAL_REALISM.md`

---

## Executive summary

First-pass Starship exterior (V1), plumes/pad (V2), and reentry VFX (V3 partial) are implemented. Highest-impact gaps: **render proportion skew (booster vs ship)**, **orphan reentry shader**, **no CI PNG capture**, **reentry tuning blocked on playtest harness**, and **thin audio**.

| Priority | Count | Theme |
|----------|-------|-------|
| P0 | 0 | — |
| P1 | 4 | Proportions, CI gate, harness dependency, hot-stage reference compare |
| P2 | 12 | Shaders, reentry edge glow, audio spatial, sim/render length split |
| P3 | 8 | Shader polish, generic vessel scale, perf micro-opts |

---

## Shaders (`assets/shaders/`)

Nine shaders reviewed. Key findings:

### VS-01 — Orphan `reentry_glow.gdshader`
| | |
|---|---|
| **Priority** | P1 |
| **Score** | I=4 R=4 F=4 → **64** |
| **Evidence** | File exists (`assets/shaders/reentry_glow.gdshader:1-77`); zero C#/scene references (grep); `ReentryPlasmaController.cs:44-83` uses `StandardMaterial3D` |
| **Gap** | Designed mesh plasma (UV noise, vertex halo) never wired. |
| **Fix direction** | Wire to shock mesh OR delete asset and document StandardMaterial3D path |

### VS-02 — `raptor_plume.gdshader` duplicate uniform paths
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `raptor_plume.gdshader:36-49,94-102`; driven from `PlumeSystem.cs:143-149` |
| **Gap** | `expansion`/`atmo_pressure`, `throttle`/`throttle_level` redundant — tuning fragility |

### VS-03 — Plume overdraw at pad (depth_draw_never + additive)
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `raptor_plume.gdshader:33`; 4 merged SH plume units (`PlumeSystem.cs:62-82`) |
| **Gap** | Z-sorting/exposure wash at liftoff |

### VS-04 — `steel.gdshader` pseudo-anisotropic brush
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `steel.gdshader:65-69,85-88` — azimuth ripple on roughness, not anisotropic GGX |
| **Gap** | Brushed 304L highlights don't streak like reference |

### VS-05 — `earth_ground.gdshader` alpha without transparent render mode
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `earth_ground.gdshader:14,106` sets ALPHA but opaque render_mode |
| **Gap** | Horizon fade may not blend correctly |

### VS-06 — `earth_surface.gdshader` no normal map
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `earth_surface.gdshader:85-90` — FBM albedo-only |
| **Gap** | Close orbital approach looks flat |

### VS-07 — `atmosphere.gdshader` heuristic limb (no ray-march)
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `atmosphere.gdshader:58-66` |
| **Gap** | Acceptable for scaled backdrop; not integrated with in-atmo views |

### VS-08 — `planet_body.gdshader` crater loop cost
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `planet_body.gdshader:74-85` — 27-cell neighborhood per fragment |
| **Gap** | Fine for distant bodies; costly at close range |

---

## Visual scripts

### VS-09 — Booster vs ship vertical proportion skew
| | |
|---|---|
| **Priority** | P1 |
| **Score** | I=5 R=5 F=3 → **75** |
| **Evidence** | Layout `VesselRenderer.cs:52-63`; SH body y=2→20 + skirt; ship o+22→o+43.25 |
| **Real** | Booster ~71 m (58.7%), Ship ~50 m (41.3%) |
| **Render** | Booster ~61.6 m (22 u), Ship ~59.5 m (21.25 u) |
| **Gap** | Booster reads short; ship elongated vs IFT references |

### VS-10 — Sim stack 123.1 m ≠ render ~121.1 m
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `StarshipRealismTests.cs:23`; `VesselRenderer.cs:41-42` |
| **Gap** | Physics drag uses JSON length; mesh is artistic layout |

### VS-11 — Full-stack reentry disables localized edge glows
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `ReentryPlasmaController.cs:260-266` |
| **Gap** | Nose/flap edge cues lost when SH still attached |

### VS-12 — Reentry VFX tuning pending capture validation
| | |
|---|---|
| **Priority** | P1 (blocked on GU-01) |
| **Evidence** | `PLAN_VISUAL_REALISM.md:192-203`; flux-driven plasma works (`ReentryPlasmaController.cs:109-115`) |
| **Gap** | Alpha/timing/zone charring need DEORBIT→EDL harness |

### VS-13 — Hot-staging reference compare not in real ascent
| | |
|---|---|
| **Priority** | P1 |
| **Evidence** | `PLAN_VISUAL_REALISM.md:45-46`; `HotStageFlashController.cs` exists; no IFT multiframe in CI |
| **Gap** | Code + local multiframe only |

### VS-14 — Generic fallback vessels ~1.25 m effective Ø
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `VesselRenderer.cs:655-658` |
| **Gap** | Non-Starship craft toy-scale vs 9 m standard |

### VS-15 — Liftoff plume legibility at tower clearance
| | |
|---|---|
| **Priority** | P3 |
| **Evidence** | `PLAN_PLAYTEST.md:134-139` (~84 m weak capture) |
| **Gap** | Camera/plume column tuning |

---

## Lighting & camera (V4)

### VS-16 — Phase lighting altitude blend shipped; reentry overlay reverted
| | |
|---|---|
| **Priority** | P2 |
| **Evidence** | `PhaseLightingController.cs` via `SimulationBridge.cs:74-75`; `PLAN_PLAYTEST.md:107-130` |
| **Gap** | Reentry exposure blocked on playtest milestone 7 |

---

*Links: `GAMEPLAY_AUDIT.md` (GU-01 harness), `CROSSCUTTING_AUDIT.md` (CI/audio), `MASTER_IMPROVEMENT_INDEX.md`*
