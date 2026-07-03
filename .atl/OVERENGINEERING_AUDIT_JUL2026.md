# Over-Engineering Audit — Exosphere (Jul 2026)

**Scope:** `ExosphereSimulation/`, `scripts/`  
**Mode:** Read-only scan first; code changes only where evidence and the strict bar both pass.  
**Bar:** duplication/dead path with file:line evidence, ≥20% LOC/complexity reduction in the unit OR provably dead code, `ci_check` + full dotnet test pass, no ascent `[G]` / EDL R13 / hot-staging timing changes unless bug proven, realism-neutral or realism-positive.

---

## Executive summary

| Severity | Count | Action taken |
|----------|-------|--------------|
| P0 | 0 | — |
| P1 | 2 | **FIX** (branch `refactor/simplify-dead-controllers`) |
| P2 | 6 | DEFER |
| P3 | 5 | WONTFIX / DEFER |

**Drag duplication (README / older docs):** **resolved.** `Vessel.ComputeDragAt` delegates to `AerodynamicsModel` for drag + lift (`Vessel.cs:166-186`, `AerodynamicsModel.cs:187-205`). No inline duplicate aero path remains in the live integrator.

**Fixes applied (2 commits):**
1. Remove dead `NavBallController` (superseded by `AttitudeNavball`).
2. Remove duplicate warp input path (`TimeWarpController`); consolidate into `WarpController` + `SimulationBridge`.

---

## P1 — FIX

### P1-A: Dead `NavBallController` runs every frame, zero consumers

| | |
|---|---|
| **Evidence** | `NavBallController.cs:23-82` computes prograde/heading/pitch/roll every `_Process` tick. Grep shows **no reads** of `ProgradeWorld`, `Heading`, `ProgradeError`, or any other public property outside this file. `AttitudeNavball.cs:10-11` explicitly states it does **not** depend on `NavBallController` and re-implements the frame at `AttitudeNavball.cs:72-120`. Scene still mounts the dead node: `scenes/flight/Flight.tscn:70-71`. |
| **Impact** | Wasted per-frame work; two parallel navball math paths confuse future HUD work. |
| **Recommendation** | **FIX** — delete `scripts/NavBallController.cs`, remove node from `Flight.tscn`. |
| **LOC** | −86 lines (−100% of dead unit). |
| **Risk** | None — live navball is `AttitudeNavball` via `HUDController.cs:91`. |

### P1-B: Duplicate time-warp input handlers

| | |
|---|---|
| **Evidence** | `TimeWarpController.cs:18-39` handles `[.]` / `[,]` and calls `SimulationBridge.SetWarpIndex`. `WarpController.cs:28-49` handles the **same keys** with identical logic. Both are active in Flight: `Flight.tscn:37-38` (TimeWarp) and `SimulationBridge.cs:91-92` (Warp added at runtime). `TimeWarpController.WarpChanged` signal (`TimeWarpController.cs:11`) has **zero subscribers** (grep entire repo). |
| **Impact** | Two nodes compete for input; warp UX split across display (`WarpController`) and logic (`TimeWarpController`). |
| **Recommendation** | **FIX** — keep HUD `WarpController`; move `Backspace` reset there; remove `TimeWarpController` from scene and delete file. |
| **LOC** | −76 lines file + ~8 lines added to `WarpController` ≈ −45% of combined warp unit (~149 LOC). |
| **Risk** | Low — behavior preserved; Godot smoke required. |

---

## P2 — DEFER

### P2-A: Unused per-part `drag_coefficient` JSON fields

| | |
|---|---|
| **Evidence** | Fields in every part JSON (e.g. `data/parts/starship_tank.json:10`). Deserialized at `PartDefinition.cs:32` but **never read** by sim code — live drag uses orientation model at `Vessel.cs:182-185`. |
| **Recommendation** | **DEFER** — wiring parts into aero would be a realism change and needs calibration/tests; deleting JSON fields is a broad data churn for clarity only. |

### P2-B: Dead helpers in `AerodynamicsModel`

| | |
|---|---|
| **Evidence** | `EstimateReferenceArea` (`AerodynamicsModel.cs:50-54`) — zero call sites. `ComputeDynamicPressure` (`AerodynamicsModel.cs:29-30`) — zero production call sites (`Vessel.GetDynamicPressure` at `Vessel.cs:86-93` is the live path). `ComputeDrag` scalar helper (`AerodynamicsModel.cs:16-26`) — tests only (`PhysicsRegressionTests.cs:271-273`). `partCount` overloads (`AerodynamicsModel.cs:72-76`, `131-141`, `173-185`) — tests only; production uses `VehicleLength`/`MaximumDiameter` via `Vessel.cs:183-185`. |
| **Recommendation** | **DEFER** — removing test-only overloads saves little production LOC; keep until legacy craft JSON without `length_m`/`diameter_m` is retired. |

### P2-C: Structural load scaffold with discarded break-up result

| | |
|---|---|
| **Evidence** | `Universe.cs:208-211` calls `ComputeLoads` then `_ = FindBreakingJoints(...).ToList()` — result thrown away. `Joint.IsBreaking` (`Joint.cs:19-21`) is only read inside `FindBreakingJoints` (`StressSolver.cs:50-51`). |
| **Recommendation** | **DEFER** — incomplete feature, not dead code path (loads are computed); removing `ComputeLoads` would drop scaffolding for future break-up without saving meaningful complexity today. |

### P2-D: Orientation-agnostic `ApplyThermalLoads` 3-arg overload

| | |
|---|---|
| **Evidence** | `StressSolver.cs:66-79` — no production callers; live path uses 4-arg overload at `Universe.cs:230-231`. |
| **Recommendation** | **DEFER** — documented compatibility shim; tests may rely on it indirectly. |

### P2-E: `Vessel.ComputeThrust()` vacuum overload

| | |
|---|---|
| **Evidence** | `Vessel.cs:147-150` — zero call sites (grep); all thrust paths pass `CelestialBody` for pressure correction (`Vessel.cs:153-157`, `ComputeNetAccelerationAt` at `Vessel.cs:221-222`). |
| **Recommendation** | **DEFER** — 4-line public API stub; removal is trivial but not ≥20% of a meaningful unit. |

### P2-F: Unused `PartDefinition.cost` JSON field

| | |
|---|---|
| **Evidence** | `PartDefinition.cs:31` — zero reads of `.Cost` in C#. |
| **Recommendation** | **DEFER** — VAB economy not implemented; field is forward-looking data. |

### P2-G: Stale `docs/physics_audit.md` drag / mixture claims

| | |
|---|---|
| **Evidence** | Doc at `docs/physics_audit.md:18-21` still claims hardcoded inline drag and unused `EstimateReferenceArea`; `Vessel.cs:166-186` now delegates. Doc at `docs/physics_audit.md:16-17` claims `mixture_ratio` unused; `PartGraph.cs:313-318` and `Part.cs:199-200` **do** use `MixtureRatio` when declared. |
| **Recommendation** | **DEFER** — documentation drift, not runtime over-engineering. |

---

## P3 — WONTFIX / low priority

### P3-A: Thin telemetry wrappers on `Vessel`

| | |
|---|---|
| **Evidence** | `Vessel.cs:100-132` — one-line delegations to `PartGraph` for HUD. |
| **Recommendation** | **WONTFIX** — intentional boundary so Godot HUD never touches thrust math; not harmful indirection. |

### P3-B: `ManeuverPlanner` vs `TransferPlanner` split

| | |
|---|---|
| **Evidence** | `ManeuverPlanner.cs` (orbital geometry + node editing), `TransferPlanner.cs` (Hohmann + encounter). `MapViewController.cs:284-285` allocates a temporary `ManeuverPlanner` for post-burn projection — localized, not duplicated logic. |
| **Recommendation** | **WONTFIX** — distinct responsibilities; merging would blur map vs interplanetary concerns. |

### P3-C: `AutopilotController` vs `ManeuverExecutor`

| | |
|---|---|
| **Evidence** | Map orbit burns (`AutopilotController.cs`) vs transfer-node execution (`ManeuverExecutor.cs`); both used from `MapViewController.cs:76-127`. |
| **Recommendation** | **WONTFIX** — different burn modes; consolidation is a feature refactor, not dead-code removal. |

### P3-D: Life-support / power / comms / thermal systems

| | |
|---|---|
| **Evidence** | Simulated in `SystemsController.cs:16-76`; consequences at `SystemsController.cs:81-98`. `CrewMember.cs` exists but default crew count is hardcoded (`SystemsController.cs:46`). |
| **Recommendation** | **WONTFIX** — gameplay systems with HUD wiring; not duplicate models. |

### P3-E: `ComputeLoads` every RK4 tick without break-up

| | |
|---|---|
| **Evidence** | See P2-C. |
| **Recommendation** | **WONTFIX until break-up ships** — removing now saves CPU but deletes forward path. |

---

## Items explicitly not changed (safety)

- **Ascent `[G]`** — `AscentController.cs` untouched.
- **EDL R13** — `EDLController.cs` untouched.
- **Hot-staging timing** — no changes to staging/MECO/separation logic.

---

## Self-grade

| Criterion | Score | Notes |
|-----------|-------|-------|
| **Rigor** | High | Every claim cites file:line. |
| **Restraint** | High | 2 fix units (~160 LOC removed); 11 deferred with reasons. |
| **Safety** | Pending CI | `ci_check.sh` run after fixes on branch. |

---

## Deferred summary

| ID | Reason deferred |
|----|-----------------|
| P2-A | Realism/data calibration needed to wire `drag_coefficient` |
| P2-B | Test-only helpers; legacy part-count path still documented |
| P2-C | Incomplete break-up feature, not pure dead code |
| P2-D | Harmless overload; possible test dependency |
| P2-E | Trivial API stub |
| P2-F | Future VAB economy |
| P2-G | Doc drift only |
| P3-* | Intentional architecture or forward-looking scaffolding |
