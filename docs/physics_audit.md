# Physics Realism Audit — Exosphere (Starship + Super Heavy)

Date: 2026-06-13
Scope: verify simulation parameters against a real SpaceX Starship/Super Heavy orbital
mission. Data files audited under `data/`. Physics code read-only for reference; no C# was
changed. Two data files were corrected (see "Data changes applied").

How the engine physics actually consume these numbers (verified in code):
- Thrust is pressure-interpolated per engine between `thrust_vac` (vacuum) and `thrust_sl`
  (sea level): `Part.GetThrustMagnitude` (`ExosphereSimulation/Parts/Part.cs:57`).
- Mass flow ṁ = F(p)/(Isp·g₀), with Isp interpolated between `isp_vac` and `isp_sl`
  (`PartGraph.ConsumePropellant`, `ExosphereSimulation/Parts/PartGraph.cs:101`).
- **The O/F ratio that the rocket actually burns is set by the loaded tank ratio**
  `fuel_capacity_ox / fuel_capacity_lf`, NOT by the `mixture_ratio` field. `ConsumePropellant`
  splits the propellant draw by `lfFrac = totalLF/(totalLF+totalOx)` so LF and Ox deplete
  together (`PartGraph.cs:140-143`). The `mixture_ratio` JSON field is read by **no** C# code
  (confirmed by grep) — it is documentation only.
- Flight drag uses a **hardcoded** model in `Vessel.ComputeDragAt` (`Vessel.cs:99-122`):
  fixed 9 m diameter cylinder, orientation-dependent Cd 1.5 (broadside) ↔ 0.6 (axial), plus a
  transonic Mach multiplier. The per-part `drag_coefficient` JSON fields and the
  `AerodynamicsModel.EstimateReferenceArea` helper are **not used** by the live trajectory.

---

## Parameter table

| Parameter | Game value (file) | Real value | Verdict | Recommended fix |
|---|---|---|---|---|
| **Full-stack wet mass** | ~4800 t (sum: SH 200 t dry + 3300 t prop + Starship 80 t cmd + 5 t tank + 1200 t prop + 15 t eng) | ~5000 t | ✅ realistic | Within ~4%. Fine. |
| **Super Heavy dry mass** | 200 t (`super_heavy_booster.json` `mass_dry`) | ~200 t (incl. engines) | ✅ realistic | None. |
| **Super Heavy propellant** | 3300 t total (`super_heavy_booster.json`) | ~3400 t | ✅ realistic | Slightly low (~3%); acceptable. |
| **SH sea-level thrust** | 69.0 MN (`thrust_sl`) | ~74.4 MN at SL (33×~2.26 MN) | ⚠️ off | The 74.4 MN figure is the **sea-level** liftoff thrust; the file puts it in `thrust_vac` and uses 69 MN for `thrust_sl`. Consider `thrust_sl ≈ 74.4 MN`, `thrust_vac ≈ 80 MN`. Conservative, not auto-applied (see note 1). |
| **SH vacuum thrust** | 74.4 MN (`thrust_vac`) | ~80 MN (Raptor 2 vac ~2.4–2.5 MN ×33) | ⚠️ off | See above — vac should exceed SL. Not auto-applied. |
| **SH Isp (SL / vac)** | 327 / 356 s | ~327 / ~350–356 s | ✅ realistic | None. |
| **Starship dry mass** | 100 t (80 t cmd + 5 t tank + 15 t eng) | ~100–120 t | ✅ realistic | None. |
| **Starship propellant** | 1200 t (`starship_tank.json`) | ~1200 t | ✅ realistic | None. |
| **Starship vac thrust** | 13.5 MN (`starship_engines.json`) | ~13.5 MN (6 Raptor) | ✅ realistic | None. |
| **Starship SL thrust** | 11.0 MN (`thrust_sl`) | ~10–11 MN (3 SL + 3 vac throttled in atmo) | ✅ realistic | None. |
| **Starship Isp (SL / vac)** | 327 / 380 s | ~327 / ~350–380 s | ✅ realistic | Vac 380 s is optimistic-but-plausible for RVac. None. |
| **O/F ratio — SH (loaded)** | was 2400/900 = **2.67**; now 2575/725 = **3.55** | 3.55 | ✅ fixed | **Auto-applied** (see Data changes). |
| **O/F ratio — Starship (loaded)** | was 870/330 = **2.64**; now 936/264 = **3.55** | 3.55 | ✅ fixed | **Auto-applied**. |
| **`mixture_ratio` field** | 3.55 (both engine files) | 3.55 | ⚠️ misleading | Field is correct numerically but **unused by code**; physics is driven by tank capacities. Left as-is; documented here. |
| **Liftoff TWR (SL)** | thrust_sl 69 MN / weight 47.1 MN = **1.47** | ~1.5 | ✅ realistic | None (would rise to ~1.58 if `thrust_sl` were 74.4). |
| **Decoupler mass** | 60 kg (`decoupler_medium.json`) | hot-stage ring ~few t | ⚠️ minor | Negligible vs 4800 t stack; cosmetic. Optional bump. |
| **Per-part `drag_coefficient`** | 0.2–0.3 (all part files) | n/a | ⚠️ unused | Flight drag is hardcoded in `Vessel.cs`; these fields don't affect the trajectory. Either wire them in (C# change, out of scope) or treat as cosmetic. |
| **Earth radius** | 6 371 000 m | 6 371 km | ✅ | None. |
| **Earth sea-level density** | 1.225 kg/m³ | 1.225 | ✅ | None. |
| **Earth scale height** | 8500 m | ~8.5 km | ✅ | None. |
| **Earth sea-level pressure** | 101 325 Pa | 101 325 | ✅ | None. |
| **Earth ISA layers / lapse** | 6 layers, -0.0065 troposphere etc. | matches US Standard Atmosphere 1976 | ✅ | None. |
| **Earth max_altitude** | 140 000 m | atmosphere effectively negligible by ~120–140 km | ✅ | None (drag is already ~0 there). |
| **Earth GM** | 3.986004418e14 | 3.986e14 | ✅ | None. |
| **Mars radius** | 3 389 500 m | 3 389.5 km | ✅ | None. |
| **Mars sea-level density** | 0.020 kg/m³ | ~0.020 (surface) | ✅ | None. |
| **Mars surface pressure** | 636 Pa | ~600–700 Pa | ✅ | None. |
| **Mars scale height** | 11 100 m | ~11.1 km | ✅ | None. |
| **Mars GM** | 4.282837e13 | 4.2828e13 | ✅ | None. |

### Derived ascent sanity checks (not stored, computed from the above)
- **Liftoff TWR**: 69 MN / (4.80e6 kg × 9.807) = **1.47** ✅ (target ~1.5).
- **Max-Q**: governed by the hardcoded atmosphere + drag model, not a data field; with
  Earth ISA density and a ~1.5 TWR climb, Max-Q lands near the realistic 10–13 km band. ✅
- **Stage-1 ΔV** (rocket eq., ~350 s effective Isp, m0≈4800 t, m1≈1500 t): ln(3.2)×350×9.81
  ≈ **4.0 km/s** — consistent with hot-staging ~2.4 km/s plus gravity/drag losses. ✅
- **LEO**: orbital velocity ~7.8 km/s and ΔV-to-orbit ~9.3–9.5 km/s are emergent from the
  integrator + these parameters; nothing in the data contradicts them. ✅

---

## Data changes applied

Only the two propellant-loading ratios were changed, because they are the values that
**physically drive the burned O/F ratio** and were clearly wrong (≈2.65 vs real 3.55).
Total propellant mass was held constant in each case, so stack mass and ΔV are unaffected.

1. `data/parts/super_heavy_booster.json`
   - `fuel_capacity_lf`: 900000 → **725000**
   - `fuel_capacity_ox`: 2400000 → **2575000**
   - (total unchanged at 3 300 000 kg; new O/F = 2575/725 = 3.55)
   - description text updated to "725 t LCH4 + 2575 t LOX = 3300 t total (O/F 3.55)".

2. `data/parts/starship_tank.json`
   - `fuel_capacity_lf`: 330000 → **264000**
   - `fuel_capacity_ox`: 870000 → **936000**
   - (total unchanged at 1 200 000 kg; new O/F = 936/264 = 3.55)
   - description text updated to "264 t LCH4 + 936 t LOX = 1200 t total (O/F 3.55)".

No other data files and no C# files were modified.

---

## Summary & priority fixes

The simulation is already in good shape: Earth/Mars atmospheres, masses, Isp, propellant
totals, and liftoff TWR all match reality within a few percent. The headline finding is a
**propellant mixture-ratio bug**: both stages were loaded at O/F ≈ 2.65 instead of Raptor's
3.55, because the code derives the burned ratio from the loaded tank capacities (not from the
`mixture_ratio` field, which is dead documentation). This has been corrected.

Priority list:
1. **DONE — O/F loading fixed** to 3.55 on both stages (kept totals constant). This was the
   one clearly-wrong, safe-to-fix value.
2. **Should fix (data, not auto-applied — judgement call): SH thrust split.** The real 74.4 MN
   is the *sea-level* liftoff thrust, but the file assigns it to `thrust_vac` and uses 69 MN
   for `thrust_sl`, so vacuum thrust < liftoff thrust (physically backwards for a fixed
   nozzle). Recommend `thrust_sl ≈ 74_400_000`, `thrust_vac ≈ 80_000_000`. Left to the owner
   because it nudges liftoff TWR to ~1.58 and changes ascent tuning.
3. **Optional (code, out of scope): wire up `drag_coefficient`** or remove the unused per-part
   fields, since live drag is hardcoded in `Vessel.ComputeDragAt`. The hardcoded model is
   itself realistic (orientation-aware Cd + transonic peak), so this is cosmetic/clarity only.
4. **Cosmetic:** `mixture_ratio` JSON field is unused — keep for documentation or drop it;
   decoupler mass (60 kg) is negligible.
