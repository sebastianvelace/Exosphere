# Exosphere Physics Deep Audit (Jul 2026)

> **Estado histórico.** La auditoría vigente y sus cierres están en
> [`REALISM_ASTRONAUT_MASTER_PLAN.md`](REALISM_ASTRONAUT_MASTER_PLAN.md). P-A03,
> P-A05 y P-R04 ya no describen el código actual.

**Scope:** Beyond closed items in `PLAN_REALISM.md` (R1–R4, R6–R10, R13).  
**Baseline:** Flight 7 Block 2 per `docs/starship_physics_baseline.md`.  
**Method:** Code + xUnit gap analysis; telemetry references from `PLAN_REALISM.md`, `.atl/agent-hotstaging-log.md`, R13 harness.

---

## Executive summary

| Metric | Value |
|--------|------:|
| **Total gap items** | **28** |
| **Sim-layer fixes** | 14 |
| **Game-layer fixes** | 9 |
| **Data/docs only** | 3 |
| **Accept / defer (R5)** | 2 |

### Top 5 (impact × realism)

1. **P-A03** — Dual/conflicting MECO triggers (`MissionManager` fuel cut vs `AscentController` hot-stage)
2. **P-S01** — Hot-staging is atomic separation, not Ship-ignition-during-booster-carry
3. **P-I01** — Moon transfer uses heliocentric Hohmann radii, not patched-conic TLI
4. **P-O01** — Orbital decay disabled when on-rails at warp ≥10
5. **P-R06** — Structural joint break-up computed but never applied

---

## 1. Ascent profile

### P-A01 — Max-Q occurs ~4 km low vs IFT

| Field | Detail |
|-------|--------|
| **Evidence** | Post-R1 telemetry: q≈33 kPa at **~8 km** (`PLAN_REALISM.md:12-13`). Real IFT Max-Q T+55 s at **12–14 km** (~33 kPa). **Test gap:** no `AscentProfileTests` or harness regression in xUnit. |
| **Real-world** | SpaceX Flight 7 webcast: Max-Q ~12–14 km, ~33 kPa |
| **Root cause** | Aggressive gravity turn + Max-Q throttle dip still builds vertical speed early; q peaks before altitude catches real profile (`scripts/AscentController.cs:59-64`, `:291-292`) |
| **Fix** | **Game:** Shift `GravityTurnElevationDeg` or Max-Q throttle window so q peak lands 12–14 km. **Sim:** none required if aero model is correct. |
| **Acceptance** | Telemetry harness: Max-Q phase at alt **11–15 km**, q **28–38 kPa**, pitch **≥20°** at Max-Q |
| **Impact** | Medium — profile feel | **Effort** | S |

### P-A02 — MECO altitude/speed ~5% below nominal

| Field | Detail |
|-------|--------|
| **Evidence** | `.atl/agent-hotstaging-log.md`: staging **~61 km / ~2.16 km/s** vs real **~65 km / ~2.3–2.4 km/s**. `StagingSpeed=2300`, `StagingMinAlt=45000` (`AscentController.cs:50-51`). **Test gap:** no staging milestone test. |
| **Real-world** | IFT MECO/hot-stage ~65 km, ~2.4 km/s horizontal |
| **Root cause** | Staging speed floor 2300 m/s triggers before altitude target; reserve frac 6% may stage slightly early |
| **Fix** | **Game:** Tune `StagingSpeed`→2400, `StagingMinAlt`→55000; validate against telemetry |
| **Acceptance** | Harness at SEPARATION: alt **62–68 km**, surface speed **2.2–2.5 km/s**, booster prop **5–8%** remaining |
| **Impact** | Low–Med | **Effort** | S |

### P-A03 — Legacy MissionManager MECO conflicts with AscentController

| Field | Detail |
|-------|--------|
| **Evidence** | `MissionManager.cs:204-218` — if SH fuel **<10 t** at alt>55 km: **cuts throttle to 0**, sets MECO. `AscentController.cs:439-445` stages at **2300 m/s OR ≤6% reserve** (~198 t). **Test gap:** none. |
| **Real-world** | MECO is velocity/altitude gated with ~6–8% reserve, not near-empty |
| **Root cause** | R2 fix in AscentController; MissionManager FSM never updated |
| **Fix** | **Game:** Remove fuel<10t throttle-cut; delegate MECO/SEPARATION to AscentController + `NotifyStaged`. Optionally sync on reserve fraction. |
| **Acceptance** | Manual ascent without [G]: no throttle cut at 10 t while SH still attached. xUnit N/A; harness confirms single MECO event. |
| **Impact** | **High** — can fight autopilot | **Effort** | S |

### P-A04 — No ascent profile regression test

| Field | Detail |
|-------|--------|
| **Evidence** | `StarshipRealismTests.cs` covers static mass/thrust/O/F only; no integrated ascent. `PLAN_NEXT_SESSION.md:59-60` describes harness but no committed test. |
| **Real-world** | Full-stack ascent is primary mission validation |
| **Root cause** | Ascent is game-layer + RK4; no sim-level acceptance test |
| **Fix** | **Sim test:** RK4 ascent slice (gravity turn as open-loop pitch schedule) OR **game harness** logged to CI artifact |
| **Acceptance** | New test or harness: Max-Q, MECO, orbit **150 km / 7.6–7.8 km/s** within tolerances |
| **Impact** | High (regression guard) | **Effort** | M |

### P-A05 — Instant attitude snap bypasses rotation dynamics

| Field | Detail |
|-------|--------|
| **Evidence** | `AscentController.cs:335-337`, `EDLController.cs:186-188`: `vessel.Orientation = ShortestArc(...)`, `AngularVelocity = Zero` every frame |
| **Real-world** | Gimbal + aero weathervaning; belly-flop is actively controlled |
| **Root cause** | Autopilot sets orientation directly instead of `PitchYawRoll` / SAS |
| **Fix** | **Game:** Command attitude via existing gimbal/SAS path (assist mode pattern). **Sim:** already has gimbal authority tests (`StarshipRealismTests.cs:140-158`) |
| **Acceptance** | Ascent pitch rate ≤ physically achievable gimbal authority; EDL flip takes **>2 s** |
| **Impact** | Med | **Effort** | M |

---

## 2. Staging / hot-stage physics

### P-S01 — No hot-stage overlap burn

| Field | Detail |
|-------|--------|
| **Evidence** | `SimulationBridge.TriggerStaging()` → `Vessel.Stage()` instant decouple (`SimulationBridge.cs:505-528`, `Vessel.cs:335-350`). Active engines switch post-separation only. **Test gap:** `StagingSwitchesFromThirtyThree...` checks count after stage, not overlap thrust. |
| **Real-world** | Ship Raptors ignite **before** SH separation (Flight 7 hot-stage) |
| **Root cause** | Single-stage engine model; no dual-active-engine window |
| **Fix** | **Game:** Pre-stage: enable Ship cluster throttle while SH still attached (both draw prop from respective stages); then separate. **Sim:** optional brief dual-engine thrust sum (still R5-lite). |
| **Acceptance** | Telemetry: Ship thrust >0 while SH still attached ≥**0.5 s** before SEPARATION; TWR never drops below ~1.0 at handoff |
| **Impact** | **High** — signature Starship physics | **Effort** | M |
| **NON-GOALS** | Full 33+6 independent engines = **R5**; overlap window is in scope without R5 |

### P-S02 — Separated booster inert (no boostback)

| Field | Detail |
|-------|--------|
| **Evidence** | Debris vessel added to universe with no controller (`SimulationBridge.cs:511-525`). R12 blocked on R5 (`PLAN_REALISM.md:223-224`) |
| **Real-world** | SH boostback + landing burn (~13 engines) |
| **Root cause** | No booster AI; R5 blocks engine subset physics |
| **Fix** | **Defer** until R5 or simplified booster autopilot on aggregate engine |
| **Acceptance** | Booster debris executes boostback profile; lands or expends reserve |
| **Impact** | High (mission completeness) | **Effort** | **L** |
| **NON-GOALS** | **R5 + R12** — document as blocked |

### P-S03 — Booster reserve not asserted in tests

| Field | Detail |
|-------|--------|
| **Evidence** | `BoosterReserveFrac=0.06` (`AscentController.cs:52`) — no xUnit. `StarshipRealismTests` has no post-stage mass/prop check |
| **Real-world** | ~6–8% prop for boostback |
| **Root cause** | R3 implemented in controller only |
| **Fix** | **Test:** after simulated MECO trigger, assert detached booster prop mass **≥5%** of capacity |
| **Acceptance** | xUnit: `DetachedBoosterRetainsBoostbackReserve` — prop **165–250 t** on 3300 t stage |
| **Impact** | Low | **Effort** | S |

---

## 3. Aero / heating / reentry

### P-R01 — Peak reentry q ~40% below real

| Field | Detail |
|-------|--------|
| **Evidence** | R13 telemetry peakQ **~21 kPa** (`PLAN_REALISM.md:42`). Real Starship EDL **~50 kPa**. Survivable but thermally conservative. **Test gap:** no end-to-end q peak assertion |
| **Real-world** | Flight 4–7 belly-flop entries ~40–60 kPa |
| **Root cause** | Successful aero-braking + conservative thermal tolerances; possible density/velocity at peak |
| **Fix** | **Sim:** validate thermosphere/ISA at 40–70 km against q target; **Game:** EDL entry angle/speed if needed |
| **Acceptance** | Harness deorbit from 150 km: peakQ **35–55 kPa**, survive with heatRatio **<0.85** |
| **Impact** | Med | **Effort** | M |

### P-R02 — Body lift unused in EDL (R6 game-layer)

| Field | Detail |
|-------|--------|
| **Evidence** | `ComputeLift` exists (`AerodynamicsModel.cs:131-165`, `AerodynamicLiftTests.cs`). EDL commands belly-flop ~90° ⇒ lift≈0 (`EDLController.cs:177-181`, `PLAN_REALISM.md:34-36`) |
| **Real-world** | Starship uses body lift + flaps for range and targeting |
| **Root cause** | R6 sim-only; EDL not updated |
| **Fix** | **Game:** Optional α≈70° glide segments using lift; keep R13 nominal profile as default |
| **Acceptance** | Harness: cross-range **>5 km** with lift enabled; R13 profile unchanged when disabled |
| **Impact** | Med | **Effort** | M |

### P-R03 — No flap / roll control in reentry

| Field | Detail |
|-------|--------|
| **Evidence** | EDL sets full orientation each frame; no flap DOF in sim |
| **Real-world** | Grid fins + flaps for roll and trim |
| **Root cause** | Control surfaces not modeled |
| **Fix** | **Game-layer** first (visual flaps + pitch/yaw trim); **Sim** optional moment from control deflection |
| **Acceptance** | Bad-attitude entry test: roll error correctable before thermal breakup |
| **Impact** | Med | **Effort** | L |

### P-R04 — Thermal windward axis documentation vs code

| Field | Detail |
|-------|--------|
| **Evidence** | Comment says shield on **local -Y ventral** (`ThermalModel.cs:114-116`); `WindwardFactor` uses **+Y** dot (`ThermalModel.cs:126-127`). Tests use `Vector3d.Up` as windward (`PhysicsRegressionTests.cs:224-225`) |
| **Real-world** | Heat shield on windward belly |
| **Root cause** | Local frame convention ambiguous |
| **Fix** | **Sim:** Align comment + `WindwardFactor` with vessel +Y=nose convention; add axis test |
| **Acceptance** | xUnit: belly-flop orientation → windwardFactor **>0.9**; nose-first → **<0.1** |
| **Impact** | Low (works via EDL orientation) | **Effort** | S |

### P-R05 — Heating skipped for on-rails vessels

| Field | Detail |
|-------|--------|
| **Evidence** | Thermal integration only in full RK4 branch (`Universe.cs:218-241`); on-rails path skips |
| **Real-world** | Reentry always heats regardless of time-warp mode |
| **Root cause** | Performance shortcut for warp |
| **Fix** | **Sim:** Apply thermal tick when density>0 even on-rails, or force RK4 below 140 km |
| **Acceptance** | Warp×10 reentry from 150 km: vessel still heats/destroys if belly-flop abandoned |
| **Impact** | Med | **Effort** | M |

### P-R06 — Structural break-up computed but discarded

| Field | Detail |
|-------|--------|
| **Evidence** | `Universe.cs:209-211`: `_ = FindBreakingJoints(...)` — result ignored. `PLAN_NEXT_SESSION.md:172-174` |
| **Real-world** | Max-Q and bad reentry can fail joints |
| **Root cause** | Scaffold never wired |
| **Fix** | **Sim:** Split vessel on breaking joints; **Game:** VFX via `ReentryBreakupController` |
| **Acceptance** | xUnit: overload joint → graph splits; R13 nominal EDL unaffected |
| **Impact** | **High** for failure realism | **Effort** | L |

---

## 4. Orbital decay / SOI

### P-O01 — LEO decay disabled at warp ≥10 on-rails

| Field | Detail |
|-------|--------|
| **Evidence** | `Universe.cs:305-307`: on-rails when `TimeScale≥10`, throttle≈0, density<0.01. Kepler ignores drag. `PLAN_REALISM.md:27-28`. **Test gap:** `OrbitalDecayTests` RK4 only at warp 1 |
| **Real-world** | LEO decay is slow but real; players warp days |
| **Root cause** | Documented limitation |
| **Fix** | **Sim:** Optional drag on rails using thermosphere density; or document + UI warning |
| **Acceptance** | xUnit: decay at warp **1–5** measurable; product decision for warp≥10 |
| **Impact** | Med | **Effort** | M |

### P-O02 — Thermosphere single exponential vs NRLMSISE

| Field | Detail |
|-------|--------|
| **Evidence** | H=45 km tail (`AtmosphereModel.cs:47-57`, `earth.json:16-17`); PLAN notes **2–5×** vs NRLMSISE. Tests check monotonicity only (`AtmosphereThermosphereTests.cs`) |
| **Real-world** | Density varies with solar activity 140–500 km |
| **Root cause** | Acceptable approximation per R7 |
| **Fix** | **Accept** as known approx OR multi-segment tail if decay rate must match USSS |
| **Acceptance** | Optional: 150 km decay rate within **0.5–2×** reference table |
| **Impact** | Low | **Effort** | M–L |

### P-O03 — Thrust pressure zero above 140 km despite residual drag

| Field | Detail |
|-------|--------|
| **Evidence** | `GetPressure` returns 0 above `MaxAltitude` (`AtmosphereModel.cs:106`); thrust uses pressure (`Vessel.cs:155-156`, `:221-222`); drag uses `GetDensity` with tail |
| **Real-world** | Negligible at 150 km but inconsistent internally |
| **Root cause** | R7 split: density tail, pressure vacuum |
| **Fix** | **Sim:** Derive pressure from density×R×T in thermosphere OR document as intentional |
| **Acceptance** | At 150 km: Isp within **1 s** of vacuum; drag still non-zero |
| **Impact** | Low | **Effort** | S |

### P-O04 — `AtmosphereModel.Earth()` factory ≠ `earth.json`

| Field | Detail |
|-------|--------|
| **Evidence** | Factory layers end **71 km** (`AtmosphereModel.cs:229-237`); JSON extends to **140 km** with mesopause layer (`earth.json:24`). Game loads JSON; some tests use factory |
| **Real-world** | ISA to ~86 km; game extends to 140 km |
| **Root cause** | Factory not synced after JSON mesosphere layer |
| **Fix** | **Data/tests:** Sync factory to JSON or remove factory from tests |
| **Acceptance** | `AtmosphereThermosphereTests` boundary density matches JSON-loaded body |
| **Impact** | Low (test drift) | **Effort** | S |

---

## 5. Interplanetary

### P-I01 — Moon transfer uses heliocentric Hohmann radii

| Field | Detail |
|-------|--------|
| **Evidence** | `TransferPlanner.cs:47-60`: `r1`, `r2` = Sun-relative radii. `ROADMAP.md:98`, `CLAUDE.md:127` |
| **Real-world** | Earth departure TLI + lunar SOI capture ≈ patched conic, not Sun Hohmann from 1 AU to 384 Mm |
| **Root cause** | Simplified planner |
| **Fix** | **Sim:** `TransferPlanner` in `ExosphereSimulation/Navigation/` with Earth SOI exit + Moon SOI entry |
| **Acceptance** | xUnit: Earth→Moon Δv **within 10%** of reference (~5.8–6.2 km/s total); time **3–5 days** |
| **Impact** | **High** for Moon missions | **Effort** | L |

### P-I02 — No lunar transfer accuracy tests

| Field | Detail |
|-------|--------|
| **Evidence** | `NavigationRegressionTests.cs` covers Mars/Venus Hohmann + SOI continuity; **no Moon Δv test** |
| **Real-world** | Artemis-class transfers well documented |
| **Root cause** | Known backlog |
| **Fix** | Add patched-conic reference tests |
| **Acceptance** | See P-I01 |
| **Impact** | Med | **Effort** | M |

### P-I03 — Encounter prediction ignores departure SOI burns

| Field | Detail |
|-------|--------|
| **Evidence** | `TrajectoryPrediction.cs` + `TransferPlanner.PredictEncounter` — single heliocentric burn |
| **Real-world** | TMI often includes Earth-periapsis burn + correction |
| **Root cause** | Single-burn Hohmann assumption |
| **Fix** | **Sim:** Multi-leg plan or warn in UI |
| **Acceptance** | Encounter ETA within **5%** of numerical propagation for Moon case |
| **Impact** | Med | **Effort** | L |

---

## 6. Systems (R11 gaps)

### P-Y01 — Eclipse model Earth-only, no Moon penumbra

| Field | Detail |
|-------|--------|
| **Evidence** | `MissionGeometry.cs:13`: "ignores Moon shadow". `.atl/agent-systems-r11-log.md:27` |
| **Real-world** | Lunar orbit has Earth/Moon eclipses |
| **Root cause** | R11 minimum slice |
| **Fix** | **Sim:** Extend umbra test to Moon SOI |
| **Acceptance** | xUnit: vessel behind Moon from Sun → solar **0 kW** |
| **Impact** | Low | **Effort** | M |

### P-Y02 — Systems wired but weak mission coupling

| Field | Detail |
|-------|--------|
| **Evidence** | `SystemsController.cs:42-62` ticks all systems; consequence = `ControlLimited` abort (`:65-78`). `SystemsMissionPhaseTests.cs` (9 tests) cover units only |
| **Real-world** | Power/comms/life affect GO/NO-GO by phase |
| **Root cause** | R11 partial |
| **Fix** | **Game:** Phase-specific loads (ascent high EC, reentry comm blackout) |
| **Acceptance** | Eclipse during **8 h** LEO → battery depletion → control limit; comm delay shown in HUD |
| **Impact** | Med | **Effort** | M |

### P-Y03 — Default crew count hardcoded

| Field | Detail |
|-------|--------|
| **Evidence** | `SystemsController.cs:41`: `crewCount = vessel.Crew.Count > 0 ? ... : 4` |
| **Real-world** | Starship crew varies 0–100+ |
| **Root cause** | Placeholder |
| **Fix** | **Data:** crew from command module JSON |
| **Acceptance** | Zero-crew flight: life support draw **0** |
| **Impact** | Low | **Effort** | S |

---

## 7. Data JSON accuracy

### P-D01 — Per-part `drag_coefficient` unused

| Field | Detail |
|-------|--------|
| **Evidence** | All parts declare 0.2–0.3; trajectory uses `AerodynamicsModel.EffectiveDragCoefficient` (`Vessel.cs:182-183`). `physics_audit.md:18-21` **stale** |
| **Real-world** | Stage-dependent Cd varies |
| **Root cause** | Unified cylinder model (R4) |
| **Fix** | **Accept** + document OR wire JSON as multiplier |
| **Acceptance** | Doc sync; optional test if wired |
| **Impact** | Low | **Effort** | S (doc) / M (wire) |

### P-D02 — Stale audit docs

| Field | Detail |
|-------|--------|
| **Evidence** | `physics_audit.md` claims unused `mixture_ratio`, hardcoded drag in `Vessel`, SH thrust swap — all **fixed** (`PartGraph.cs:313-318`, `super_heavy_booster.json:14-15`) |
| **Real-world** | N/A |
| **Root cause** | Doc not updated Jun→Jul 2026 |
| **Fix** | **Docs:** Refresh or supersede with this audit |
| **Acceptance** | No contradictions with code |
| **Impact** | Low | **Effort** | S |

### P-D03 — Tank load vs `mixture_ratio` consistency guard

| Field | Detail |
|-------|--------|
| **Evidence** | Burn uses engine `MixtureRatio` (`PartGraph.cs:313-318`); tanks loaded at 3.55 (`StarshipRealismTests.cs:44-57`). Mismatch would desync depletion |
| **Real-world** | O/F 3.55 |
| **Root cause** | Dual source of truth |
| **Fix** | **Test:** assert tank LF/Ox ratio matches engine `MixtureRatio` for Flight 7 stack |
| **Acceptance** | xUnit on `BuildFlight7Stack` tank capacities |
| **Impact** | Low | **Effort** | S |

---

## 8. Known approximations — fix vs accept

| ID | Approximation | Verdict | Notes |
|----|---------------|---------|-------|
| **P-X01** | 1 engine part/stage (R5) | **ACCEPT / NON-GOAL** | `CLAUDE.md`, `PLAN_REALISM.md:R5`. Blocks engine-out, boostback, asymmetric thrust |
| **P-X02** | RK4 fixed body positions per 20 ms substep | **ACCEPT** | `Universe.cs:193-194`; error negligible |
| **P-X03** | Cylinder CL = 0.7·sin(2α) | **ACCEPT** | Validated L/D≈0.3 at α=70° (`AerodynamicLiftTests.cs`) |
| **P-X04** | Simplified D-K-R heating, 1 m² part area | **ACCEPT short-term** | Tune k or area if P-R01 q calibration needed |
| **P-X05** | `SoftLandingThreshold` 5 m/s vs EDL 3 m/s | **FIX optional** | `Universe.cs:64` vs `EDLController.cs:24`; `PLAN_NEXT_SESSION.md:P-P1` |
| **P-X06** | Patched-conic SOI (not full n-body) | **ACCEPT** | SOI continuity tested (`NavigationRegressionTests.cs:53-100`) |
| **P-X07** | Min throttle 40% on ascent only | **ACCEPT** | `Part.ApplyThrottleFloor`; EDL bypasses — matches Raptor deep-throttle off/on |

---

## NON-GOALS check (R5 scope)

The following are **explicitly out of scope** for overnight physics fixes unless product reprioritizes:

- **R5** — N physical engines per stage (33/6), engine-out, per-engine gimbal failure
- **R12** — Boostback + Mechazilla (blocked on R5)
- Per-bell visual-only engine meshes (remain cosmetic)
- NRLMSISE-00 full atmosphere model
- Full n-body interplanetary (vs patched-conic)

Items **P-S01** (overlap burn window) and **P-S02** (boostback) are bounded: overlap can ship without full R5; boostback cannot.

---

## Validated closed (do not reopen without regression)

| Item | Evidence |
|------|----------|
| R1–R3 | Gravity turn + MECO staging (`AscentController.cs:46-64`, `:421-446`) |
| R4 | Unified 9 m aero (`Vessel.cs:166-186`) |
| R6 | Body lift (`AerodynamicsModel.cs:104-165`) |
| R7 | Thermosphere decay (`OrbitalDecayTests.cs`, `AtmosphereThermosphereTests.cs`) |
| R8–R10 | Heat shield flag, touchdown, ISP 363 s |
| R13 | Belly-flop EDL survivable (`PLAN_REALISM.md:38-43`) |
| Flight 7 mass/thrust/O/F | `StarshipRealismTests.cs`, `super_heavy_booster.json` |

---

## Suggested execution order

1. P-A03 (MECO conflict) — quick, high impact  
2. P-S01 (hot-stage overlap) — signature physics  
3. P-A04 + ascent harness — regression guard  
4. P-I01/P-I02 — Moon transfer  
5. P-O01 — warp decay policy  
6. P-R06 — structural breakup  
7. P-P1 tail P-X05 — landing threshold harmonization  

---

## Deliverable status

| Requested | Status |
|-----------|--------|
| `docs/audits/PHYSICS_DEEP_AUDIT.md` | **Created** |
| Branch `docs/deep-audit-overnight-jul2026` | **Created** |
| Commit `docs(audit): physics deep improvement plan` | See overnight loop log |
| **Item count** | **28** |
| **Top 5** | P-A03, P-S01, P-I01, P-O01, P-R06 |
