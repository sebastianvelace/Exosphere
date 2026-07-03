# Physics Realism Audit — Exosphere (Starship + Super Heavy)

> **Source of truth (Jul 2026):** This file is a **historical parameter audit** (original run
> 2026-06-13). For current physics status, implementation notes, and open work, read in this
> order:
>
> 1. [`PLAN_REALISM.md`](../PLAN_REALISM.md) — validated realism work (R1–R14) and telemetry
> 2. [`docs/starship_physics_baseline.md`](starship_physics_baseline.md) — Flight 7 data baseline
> 3. [`docs/audits/PHYSICS_DEEP_AUDIT.md`](audits/PHYSICS_DEEP_AUDIT.md) — deep physics backlog
> 4. [`docs/audits/DATA_DRIFT_AUDIT.md`](audits/DATA_DRIFT_AUDIT.md) — doc/code drift tracker
>
> **Refresh:** Jul 2026 (DOC-01) — stale claims below corrected to match current code/data.

Date: 2026-06-13 (original audit); refreshed 2026-07-02
Scope: verify simulation parameters against a real SpaceX Starship/Super Heavy orbital
mission. Data files audited under `data/`. Physics code read-only for reference.

How the engine physics actually consume these numbers (verified in code, Jul 2026):
- Thrust is pressure-interpolated per engine between `thrust_vac` (vacuum) and `thrust_sl`
  (sea level): `Part.GetThrustMagnitude` (`ExosphereSimulation/Parts/Part.cs`).
- Mass flow ṁ = F(p)/(Isp·g₀), with Isp interpolated between `isp_vac` and `isp_sl`
  (`PartGraph.ConsumePropellant`, `ExosphereSimulation/Parts/PartGraph.cs`).
- **Burn O/F:** when an engine declares `mixture_ratio > 0`, `PartGraph.ConsumePropellant` and
  `Part.ConsumePropellant` split the liquid draw by that oxidizer/fuel ratio
  (`lfFrac = 1/(1+mixture_ratio)`). If `mixture_ratio` is zero or missing, the code falls back
  to the **loaded tank ratio** `totalLF/(totalLF+totalOx)` so legacy parts still deplete
  together. Tank capacities set the **load** O/F at launch; engine `mixture_ratio` sets the
  **burn** O/F when declared (Raptor parts use 3.55).
- **Flight aero:** `Vessel.ComputeDragAt` delegates to `AerodynamicsModel` — orientation-dependent
  drag on a 9 m cylinder (Cd blend axial ↔ broadside, transonic Mach multiplier) plus body lift
  (`CL = 0.7·sin(2α)`). Per-part `drag_coefficient` JSON fields are still **not** wired into
  the live trajectory (Cd is in code; see P-D01 in `PHYSICS_DEEP_AUDIT.md`).
- **Thermosphere (R7):** above `max_altitude` (140 km) `GetDensity` applies an exponential tail
  (H=45 km to 1000 km) so RK4 LEO decay is non-zero; pressure stays vacuum above 140 km.

---

## Parameter table

| Parameter | Game value (file) | Real value | Verdict | Recommended fix |
|---|---|---|---|---|
| **Full-stack wet mass** | ~4800 t (sum: SH 200 t dry + 3300 t prop + Starship 80 t cmd + 5 t tank + 1200 t prop + 15 t eng) | ~5000 t | ✅ realistic | Within ~4%. Fine. |
| **Super Heavy dry mass** | 200 t (`super_heavy_booster.json` `mass_dry` + ring) | ~200 t (incl. engines) | ✅ realistic | None. |
| **Super Heavy propellant** | 3300 t total (`super_heavy_booster.json`) | ~3400 t | ✅ realistic | Slightly low (~3%); acceptable. |
| **SH sea-level thrust** | **74.4 MN** (`thrust_sl`) | ~74.4 MN at SL (33×~2.26 MN) | ✅ fixed | Was 69 MN / swapped with vac in Jun audit; corrected in data. |
| **SH vacuum thrust** | **83.5 MN** (`thrust_vac`) | ~80 MN (Raptor 2 vac ~2.4–2.5 MN ×33) | ✅ realistic | Slightly high vs public estimates; acceptable. |
| **SH Isp (SL / vac)** | 327 / 356 s | ~327 / ~350–356 s | ✅ realistic | None. |
| **Starship dry mass** | 100 t (80 t cmd + 5 t tank + 15 t eng) | ~100–120 t | ✅ realistic | None. |
| **Starship propellant** | 1200 t (`starship_tank.json`) | ~1200 t | ✅ realistic | None. |
| **Starship vac thrust** | 13.5 MN (`starship_engines.json`) | ~13.5 MN (6 Raptor) | ✅ realistic | None. |
| **Starship SL thrust** | 11.0 MN (`thrust_sl`) | ~10–11 MN (3 SL + 3 vac throttled in atmo) | ✅ realistic | None. |
| **Starship Isp (SL / vac)** | 327 / **363 s** | ~327 / ~350–380 s | ✅ realistic | Was listed as 380 s in Jun audit; data uses 363 s (R10 cluster calibration). |
| **O/F ratio — SH (loaded)** | 2575/725 = **3.55** | 3.55 | ✅ fixed | **Auto-applied** Jun 2026 (see Data changes). |
| **O/F ratio — Starship (loaded)** | 936/264 = **3.55** | 3.55 | ✅ fixed | **Auto-applied** Jun 2026. |
| **`mixture_ratio` field** | 3.55 (engine files) | 3.55 | ✅ used | Drives burn split when >0; tank capacities set load O/F. |
| **Liftoff TWR (SL)** | thrust_sl 74.4 MN / weight 47.1 MN = **~1.58** | ~1.5 | ✅ realistic | Updated from 1.47 after SH `thrust_sl` fix. |
| **Decoupler mass** | 60 kg (`decoupler_medium.json`) | hot-stage ring ~few t | ⚠️ minor | Default stack uses `decoupler_heavy` (~5 t in SH dry); negligible vs 4800 t wet. |
| **Per-part `drag_coefficient`** | 0.2–0.3 (all part files) | n/a | ⚠️ unused | Live drag uses `AerodynamicsModel` constants, not per-part JSON. |
| **Earth radius** | 6 371 000 m | 6 371 km | ✅ | None. |
| **Earth sea-level density** | 1.225 kg/m³ | 1.225 | ✅ | None. |
| **Earth scale height** | 8500 m | ~8.5 km | ✅ | None. |
| **Earth sea-level pressure** | 101 325 Pa | 101 325 | ✅ | None. |
| **Earth ISA layers / lapse** | 6 layers, -0.0065 troposphere etc. | matches US Standard Atmosphere 1976 | ✅ | None. |
| **Earth max_altitude** | 140 000 m (aero boundary) | atmosphere negligible by ~120–140 km for pressure | ✅ | Density tail extends to 1000 km (R7); pressure zero above 140 km. |
| **Earth GM** | 3.986004418e14 | 3.986e14 | ✅ | None. |
| **Mars radius** | 3 389 500 m | 3 389.5 km | ✅ | None. |
| **Mars sea-level density** | 0.020 kg/m³ | ~0.020 (surface) | ✅ | None. |
| **Mars surface pressure** | 636 Pa | ~600–700 Pa | ✅ | None. |
| **Mars scale height** | 11 100 m | ~11.1 km | ✅ | None. |
| **Mars GM** | 4.282837e13 | 4.2828e13 | ✅ | None. |

### Derived ascent sanity checks (not stored, computed from the above)
- **Liftoff TWR**: 74.4 MN / (4.80e6 kg × 9.807) = **~1.58** ✅ (target ~1.5).
- **Max-Q**: governed by `AerodynamicsModel` + Earth ISA density; with ~1.5 TWR climb, Max-Q
  lands near the realistic 10–13 km band. ✅
- **Stage-1 ΔV** (rocket eq., ~350 s effective Isp, m0≈4800 t, m1≈1500 t): ln(3.2)×350×9.81
  ≈ **4.0 km/s** — consistent with hot-staging ~2.4 km/s plus gravity/drag losses. ✅
- **LEO**: orbital velocity ~7.8 km/s and ΔV-to-orbit ~9.3–9.5 km/s are emergent from the
  integrator + these parameters; nothing in the data contradicts them. ✅

---

## Data changes applied (Jun 2026 original audit)

Only the two propellant-loading ratios were changed in the original pass, because they were
clearly wrong (≈2.65 vs real 3.55). Total propellant mass was held constant in each case.

1. `data/parts/super_heavy_booster.json`
   - `fuel_capacity_lf`: 900000 → **725000**
   - `fuel_capacity_ox`: 2400000 → **2575000**
   - (total unchanged at 3 300 000 kg; new O/F = 2575/725 = 3.55)

2. `data/parts/starship_tank.json`
   - `fuel_capacity_lf`: 330000 → **264000**
   - `fuel_capacity_ox`: 870000 → **936000**
   - (total unchanged at 1 200 000 kg; new O/F = 936/264 = 3.55)

**Later (post-Jun 2026, on `main`):** Super Heavy `thrust_sl`/`thrust_vac` corrected to
74.4 / 83.5 MN; Starship cluster `isp_vac` set to 363 s; drag moved from inline hardcode to
`AerodynamicsModel`; `mixture_ratio` wired into propellant consumption.

---

## Summary & priority fixes

The simulation matches reality within a few percent on masses, Isp, propellant totals, atmospheres,
and liftoff TWR. The Jun 2026 headline finding — **propellant load O/F ≈ 2.65 instead of 3.55**
— was corrected in data; burn O/F now also respects engine `mixture_ratio` when declared.

Priority list (Jul 2026 status):
1. **DONE — O/F loading fixed** to 3.55 on both stages (Jun 2026).
2. **DONE — SH thrust split.** `thrust_sl` 74.4 MN, `thrust_vac` 83.5 MN (`super_heavy_booster.json`).
3. **DONE — Aero model.** `Vessel.ComputeDragAt` delegates to `AerodynamicsModel` (drag + lift).
4. **DONE — `mixture_ratio` consumption.** Engine field drives burn split when >0.
5. **Open (doc/data):** per-part `drag_coefficient` still unused (P-D01); optional wire or deprecate.
6. **Open (sim):** thermosphere density tail vs zero pressure above 140 km (P-O03); on-rails warp
   skips decay (documented in `PLAN_REALISM.md` R7).

For remaining physics backlog see [`docs/audits/PHYSICS_DEEP_AUDIT.md`](audits/PHYSICS_DEEP_AUDIT.md).
