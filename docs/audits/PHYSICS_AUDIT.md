# Physics Realism Audit — Exosphere

**Wave:** 1 (synthesized overnight Jul 2026)  
**Scope:** `ExosphereSimulation/`, `data/parts/`, `data/bodies/`  
**Mode:** Read-only; no C# changes in this audit pass  
**Supersedes in part:** `docs/physics_audit.md` (2026-06-13) — see `DATA_DRIFT_AUDIT.md` for stale claims

---

## Executive summary

Core ascent (R1–R3), shared aero (R4/R6), thermosphere decay (R7), heat-shield data (R8), and belly-flop EDL (R13) are **closed** with test/telemetry evidence. Residual gaps cluster around **multi-motor propulsion (R5)**, **thermal/structural granularity**, **EDL lift steering**, and **on-rails decay limits**.

| Priority | Count | Theme |
|----------|-------|-------|
| P0 | 0 | No open physics regressions if R1–R3/R13 telemetries hold |
| P1 | 0 | — |
| P2 | 6 | Propulsion model, thermal area, break-up, lift EDL, landing threshold, decay-on-rails |
| P3 | 2 | Boostback (blocked), atmosphere pressure tail |

---

## How physics consumes data (current, verified)

- Thrust: pressure-interpolated `thrust_sl` ↔ `thrust_vac` per engine (`Part.GetThrustMagnitude`, `Part.cs:57`).
- Mass flow: ṁ = F/(Isp·g₀) with Isp interpolated (`PartGraph.ConsumePropellant`, `PartGraph.cs:101`).
- **Load O/F:** tank capacities set initial propellant split; engine `mixture_ratio` drives burn split when >0 (`PartGraph.cs:313-318`, `Part.cs:199-201`).
- **Drag/lift:** `Vessel.ComputeDragAt` delegates to `AerodynamicsModel` (`Vessel.cs:164-186`) — orientation Cd 1.5↔0.6 + R6 lift.
- **Per-part `drag_coefficient`:** deserialized but **not read** by live aero (`PartDefinition.cs:32`; `AerodynamicsModel.EffectiveDragCoefficient`).

---

## Parameter sanity (Starship stack)

| Parameter | Source | Real target | Verdict |
|-----------|--------|-------------|---------|
| Ø 9 m | All Starship part JSON | 9 m | ✅ |
| Wet mass ~4800 t | Part sum + tests | ~5000 t | ✅ (~4%) |
| SH thrust SL/vac | `super_heavy_booster.json:14-15` | ~74.4 / ~80 MN | ✅ (74.4 / 83.5 MN) |
| Starship isp_vac | `starship_engines.json:14` | ~363 s | ✅ (R10) |
| Stack length (sim) | Part lengths sum | ~121 m | ⚠️ 123.1 m (`StarshipRealismTests.cs:23`) |
| O/F load | Tank capacities | 3.55 | ✅ (fixed Jun 2026) |

---

## Findings

### PG-01 — Single physical engine cluster per stage
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=5 R=5 F=2 → **50** |
| **Evidence** | `PartGraph.cs:289-307`; `super_heavy_booster.json:21` (`engine_count`: 33 cosmetic); `PLAN_REALISM.md` R5 |
| **Gap** | No per-motor throttle, gimbal asymmetry, or engine-out. Blocks R12 boostback. |
| **Realism filter** | Propulsion fidelity — largest remaining physics simplification |

### PG-02 — Orbital decay disabled on-rails at high warp
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=3 R=4 F=4 → **48** |
| **Evidence** | `Universe.cs:305-307` (on-rails when warp ≥×10, throttle ~0, ρ<0.01); R7 thermosphere in RK4 only |
| **Gap** | Long LEO coast at warp skips drag decay entirely. |
| **Realism filter** | Document as known limit or integrate decay into on-rails |

### PG-03 — Body lift in sim; EDL never uses it for guidance
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=4 R=4 F=3 → **48** |
| **Evidence** | `AerodynamicsModel.cs:104-165`; `Vessel.cs:182-186`; `EDLController.cs:177-181` (belly-flop only) |
| **Gap** | Real Starship EDL uses lift + flaps; game-layer holds broadside (R13). |
| **Realism filter** | Optional α<90° segments without R13 regression |

### PG-04 — Landing damage threshold (5 m/s) vs EDL setpoint (3 m/s)
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=3 R=4 F=5 → **60** |
| **Evidence** | `EDLController.cs:24` (`TouchdownVel = 3.0`); `Universe.cs:64` (`SoftLandingThreshold = 5.0`) |
| **Gap** | Manual/failed EDL at 3–5 m/s survives impact gate. |
| **Realism filter** | Harmonize to ~2–3 m/s with regression test |

### PG-05 — Thermal model uniform 1 m² surface per part
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=3 R=4 F=3 → **36** |
| **Evidence** | `ThermalModel.cs:74-78` (`surfaceArea = 1.0`, `specificHeat = 800`) |
| **Gap** | Per-zone charring in renderer outpaces per-part flux granularity. |
| **Realism filter** | Tie flux area to part envelope |

### PG-06 — No per-piece structural break-up (thermal destroy only)
| | |
|---|---|
| **Priority** | P2 |
| **Score** | I=4 R=3 F=2 → **24** |
| **Evidence** | `Universe.cs:236-238`; `ReentryBreakupController.cs:9-20` (VFX only); `ROADMAP.md:90-91` |
| **Gap** | Binary whole-vessel destroy; no progressive separation. |
| **Realism filter** | Per-piece detach before new gameplay systems |

### PG-07 — Pressure vacuum vs density tail mismatch above 140 km
| | |
|---|---|
| **Priority** | P3 |
| **Score** | I=2 R=3 F=4 → **24** |
| **Evidence** | `earth.json:15` (`max_altitude`: 140000); `AtmosphereModel.cs:151-168` (ρ tail continues) |
| **Gap** | Isp sees vacuum while ρ>0 in thermosphere tail. |
| **Realism filter** | Document or add thin pressure tail |

### PG-08 — Boostback / booster recovery not simulated
| | |
|---|---|
| **Priority** | P3 (blocked on PG-01) |
| **Score** | I=5 R=5 F=1 → **25** |
| **Evidence** | `AscentController.cs:52` (`BoosterReserveFrac = 0.06`); `PLAN_REALISM.md` R12 |
| **Gap** | No boostback, entry, or tower capture. |
| **Realism filter** | Defer until R5 multi-motor |

---

## Closed — do not reopen without regression telemetry

- R1–R3 ascent + hot-staging: `AscentController.cs:59-64`, `426-446`
- R4/R6 unified aero: `Vessel.cs:166-186`
- R7 thermosphere: `AtmosphereModel.cs:227-228`, `earth.json:16-17`
- R8 heat shield: `ThermalModel.cs:26`, `decoupler_heavy.json:12`
- R13 EDL: `EDLController.cs:124-214`
- R10 ISP: `starship_engines.json:14`

---

*Links: `DATA_DRIFT_AUDIT.md` (stale `docs/physics_audit.md`), `MASTER_IMPROVEMENT_INDEX.md`*
