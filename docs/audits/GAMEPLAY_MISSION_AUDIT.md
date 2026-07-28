# Gameplay & Mission Flow Audit — Exosphere

**Wave:** 1 (overnight Jul 2026)  
**Agent:** 5a1481f7-2524-4897-951c-2b80bd7456a0  
**Date:** 2026-07-02  
**Scope:** ROADMAP pending gameplay, `SaveSystem.cs`, `MissionManager.cs`, `CrewMember`, `ExosphereSimulation/Systems/*`, `lua_scripts/`, `ExosphereSimulation/Construction/*`, pad→orbit→EDL→recovery arc.  
**Mode:** Read-only code/doc scan. No C# changes.

**Sources:** `ROADMAP.md` (Gameplay pendientes), `PLAN_NEXT_SESSION.md` (G-P1–G-P5), `PLAN_REALISM.md` (R11/R12), `.atl/DELEGATION_JUL2026.md`.

---

## Executive summary

| Severity | Count | Theme |
|----------|-------|-------|
| **P0** | 4 | Save/load, objectives, mission arc closure, multi-vessel command |
| **P1** | 7 | Crew, comms gameplay, failure consequences, VAB gates, systems wiring |
| **P2** | 7 | Lua API, debris lifecycle, menu flow, structural break-up, phase FSM gaps |

**Headline:** Physics and phase telemetry (ascent `[G]`, EDL R13) are strong. The **mission fantasy** breaks because there is no persistent mission, no win/lose objective, phantom crew, cosmetic comms, no booster-return arc, and no post-landing recovery loop.

---

## Parameter / capability table

| Capability | Exists | Wired to gameplay | Verdict |
|------------|--------|-------------------|---------|
| Mission phase FSM | `MissionManager.cs` | HUD/audio/lighting | ⚠️ telemetry only |
| Mission save/load | `SaveSystem.cs` | **No UI; incomplete restore** | ❌ broken fantasy |
| VAB craft save | `ConstructionController.cs` | Launch via `CraftLaunchRequest` | ✅ partial |
| Life/power/comms/thermal | `Systems/*` + `SystemsController.cs` | HUD bars; control cut on LOS | ⚠️ partial |
| Crew model | `CrewMember.cs` | **Never populated** | ❌ phantom |
| Multi-vessel sim | `Universe.Vessels`, staging debris | **Active vessel only** | ⚠️ visual debris only |
| Lua mission scripts | `lua_scripts/gravity_turn_launch.lua` | **No runtime** | ❌ dead doc |
| Booster recovery | ROADMAP R12 | Blocked on R5 | ⬜ deferred |

---

## Findings (G-xxx)

### G-001 — Mission save/load incomplete and unwired (P0)

| | |
|---|---|
| **Evidence** | `SaveSystem.SaveGame`/`LoadGame` exist (`scripts/SaveSystem.cs:24-51`) but **zero UI/HUD callers** (grep: only `SaveSystem.cs`). ROADMAP: "Save/load de mision" pending. `PLAN_NEXT_SESSION.md` G-P1. |
| **Gap** | Saves only position, velocity, orientation, `IsOnRails`, `ReferenceBodyId`. **Missing:** propellant, part graph, throttle/SAS, `OrbitalState`, crew, systems reservoirs, `MissionManager.Phase`, warp index, maneuvers, ground-hold. `CurrentTime` serialized but **not restored** (`SaveSystem.cs:104-105`, `Universe.CurrentTime` private set). New vessels get **new GUIDs** on load (`Vessel.cs:16`) so `ActiveVesselId` matching is fragile (`SaveSystem.cs:120-121`). |
| **Impact** | Multi-hour orbital missions cannot be paused/resumed — core real-mission fantasy. |
| **Recommendation** | Extend DTO; expose `Universe.CurrentTime` setter for restore; wire MainMenu Continue + in-flight F5/F9; roundtrip test mid-orbit apoapsis ±1 m/s. |
| **Owner** | `SaveSystem.cs`, `Universe.cs`, `MainMenu.cs`, `HUDController.cs` |

### G-002 — No mission objectives or win/lose progression (P0)

| | |
|---|---|
| **Evidence** | `MissionManager` is a phase FSM (`MissionManager.cs:6-27`) with signals for HUD/audio. No objective types, success/fail flags, or progression store. `MainMenu` shows static "LOW EARTH ORBIT" (`MainMenu.cs:178-179`) — not data-driven. ROADMAP: "Misiones/objetivos de progresion" pending. `PLAN_NEXT_SESSION.md` G-P3. |
| **Gap** | Phases like `ORBIT`, `LANDED`, `CRASHED` have no mission outcome. Player can fly forever with no arc end. |
| **Impact** | Sandbox flight sim, not a SpaceX-style mission with briefing → execution → debrief. |
| **Recommendation** | `MissionDefinition` JSON + `MissionManager` objective evaluator; first mission: "150 km orbit + soft landing"; success/fail banner + telemetry export on completion. |
| **Owner** | `MissionManager.cs`, `HUDController.cs`, `data/missions/` (new) |

### G-003 — Crew exists in sim but is never instantiated (P1)

| | |
|---|---|
| **Evidence** | `CrewMember` has skills, EVA state, risk (`CrewMember.cs`). `Vessel.Crew` is an empty list (`Vessel.cs:51`). **No `new CrewMember` anywhere** in repo. `SystemsController` defaults `crewCount = 4` when list empty (`SystemsController.cs:41`). |
| **Gap** | Life-support drains for phantom crew; no roster, names, injury/death narrative, EVA gameplay. `CrewAlive = false` only contributes to `ControlLimited` — no mission fail. |
| **Impact** | Human spaceflight fantasy absent despite systems HUD showing O2/food. |
| **Recommendation** | Populate crew from mission JSON or Starship command part; crew panel in HUD; mission fail on crew loss for crewed missions. |
| **Owner** | `CrewMember.cs`, `Vessel.cs`, `SimulationBridge.SpawnStarshipStack`, `SystemsController.cs` |

### G-004 — Comms delay and LOS are display-only, not gameplay (P1)

| | |
|---|---|
| **Evidence** | `CommsSystem` computes `SignalDelaySeconds`, LOS, strength (`CommsSystem.cs:16-34`). `SystemsHUD` shows delay label (`SystemsHUD.cs:55-60`). `ControlLimited` triggers on `!Comms.HasSignal` (`SystemsController.cs:67-77`) — binary SAS/autopilot abort, not delayed commands. |
| **Gap** | Player input is instant at Moon distance; delay never affects throttle/attitude commands. No ground-station window, no reentry blackout as distinct comms phase. R11 partially done (eclipse→power) but comms gameplay deferred per `DELEGATION_JUL2026.md:22`. |
| **Impact** | Flight-director tension (one-way light time, blackout) missing. |
| **Recommendation** | Command queue with `SignalDelaySeconds` latency for non-critical inputs; optional "ground assist" mode requiring `HasSignal`; reentry comms fade tied to plasma phase. |
| **Owner** | `CommsSystem.cs`, `SystemsController.cs`, `SimulationBridge.cs` input path |

### G-005 — Failure states lack durable mission consequences (P1)

| | |
|---|---|
| **Evidence** | `CRASHED` phase on `IsDestroyed` (`MissionManager.cs:118-121`). Thermal breakup VFX (`ReentryBreakupController.cs`). Structural loads computed but break-up **discarded**: `Universe.cs:209-211` `_ = FindBreakingJoints(...).ToList()`. `CRASHED` can auto-clear to `ORBIT` if vessel alive again (`MissionManager.cs:128-133`). ROADMAP: "Fallos, damage consequences y recuperacion" pending. |
| **Gap** | No per-piece break-up gameplay; crash does not end campaign; no damage/repair between flights; `LANDED` after hard survival has no inspection consequence. |
| **Impact** | Failures feel like VFX moments, not mission-ending or programmatic events. |
| **Recommendation** | Mission-scoped fail on `CRASHED`/crew loss; wire `FindBreakingJoints` to staged damage; post-landing damage report gates relaunch. |
| **Owner** | `MissionManager.cs`, `Universe.cs`, `StressSolver.cs`, `ReentryBreakupController.cs` |

### G-006 — Multi-vessel world without vessel command switching (P0)

| | |
|---|---|
| **Evidence** | Staging creates debris vessel + renderer (`SimulationBridge.TriggerStaging`: `511-526`). `FloatingOrigin` positions all registered vessels (`FloatingOrigin.cs:82-91`). **No `SetActiveVessel` UI/API** — only implicit assignment at spawn (`SimulationBridge.cs:298,438`). Camera/HUD/autopilot always `ActiveVessel`. |
| **Gap** | Super Heavy falls as inert debris; no boostback, no catch, no switching to track booster. R12 blocked on R5 per ROADMAP. |
| **Impact** | Starship mission is single-entity; real dual-vehicle ops impossible. |
| **Recommendation** | Vessel switcher (map/HUD); debris tagging (booster vs ship); later: R5 + boostback profile for SH. |
| **Owner** | `SimulationBridge.cs`, `FloatingOrigin.cs`, `MapViewController.cs`, `HUDController.cs` |

### G-007 — Pad-to-recovery arc stops at LANDED (P0)

| | |
|---|---|
| **Evidence** | Flow: VAB → `CraftLaunchRequest` → pad (`ConstructionController.OnLaunch`: `440-446`) → countdown/liftoff → orbit → EDL → `LANDED` (`EDLController.Touchdown`: `217-228`). `LANDED` maps to systems **Idle** (`SystemsController.cs:83`). No refuel, pad return, vehicle inspection, or relaunch loop. `MainMenu` has no Continue mission — only New Flight / VAB (`MainMenu.cs:135-143`). |
| **Gap** | Mission has no debrief, no ship turnaround, no booster recovery chapter. Touchdown is a log line + phase label, not a campaign milestone. |
| **Impact** | Arc is launch simulator + landing demo, not full reusable launch cycle. |
| **Recommendation** | Post-`LANDED` mission complete UI; pad reset or VAB return with vessel state carry-forward; optional Starbase recovery site. |
| **Owner** | `MissionManager.cs`, `MainMenu.cs`, `SimulationBridge.cs`, `ConstructionController.cs` |

### G-008 — VAB launch lacks pre-flight validation gate (P1)

| | |
|---|---|
| **Evidence** | `OnLaunch` sets craft and changes scene with try/catch only (`ConstructionController.cs:440-451`). ROADMAP: "Validacion visual de crafts guardados antes de launch." `PLAN_NEXT_SESSION.md` G-P2. Browser shows mass if rebuildable (`ConstructionController.cs:401-414`) but no TWR/engine/decoupler checks. |
| **Gap** | Under-TWR or engine-less stacks can reach pad; preview mesh may diverge from flight `VesselRenderer` path. |
| **Impact** | Range-safety fantasy broken — impossible vehicles launch. |
| **Recommendation** | `VesselAssembly.ComputeMetrics` gate: TWR ≥ 1.0, ≥1 engine, decoupler if multi-stage; block launch with readable errors. |
| **Owner** | `ConstructionController.cs`, `VesselAssembly.cs` |

### G-009 — Systems partially phase-wired; R11 still open (P1)

| | |
|---|---|
| **Evidence** | Eclipse→solar cut tested (`SystemsMissionPhaseTests.cs:34-46`). Life-support idle vs active by `MissionPhase` (`SystemsController.cs:81-85`). `DELEGATION_JUL2026.md:22` still marks R11 ⬜. Thermal not phase-aware; comms not gameplay (G-004). |
| **Gap** | Reentry heating does not drive cabin thermal alerts; long coast does not force power budgeting decisions beyond passive drain. |
| **Impact** | Systems HUD is monitoring, not mission-critical resource management. |
| **Recommendation** | Phase hooks: `PEAK_HEATING` → thermal spike; `ORBIT` coast → eclipse power puzzles; mission fail on prolonged `NoPowerAlert` if objective requires comms. |
| **Owner** | `SystemsController.cs`, `ExosphereSimulation/Systems/*` |

### G-010 — Lua autopilot script is documentation only (P2)

| | |
|---|---|
| **Evidence** | `lua_scripts/gravity_turn_launch.lua` references `THROTTLE()`, `WAIT_UNTIL()`, `WARP_TO_APOAPSIS()`, `EXECUTE_MANEUVER(CIRCULARIZE())`. **No Lua runtime** in codebase (grep: no `LuaScript`, `MoonSharp`, etc.). Ascent automation is C# `[G]` (`AscentController.cs`). |
| **Gap** | Scriptable mission profiles promised by file layout but not executable. |
| **Impact** | Modding / mission-authoring fantasy unsupported. |
| **Recommendation** | Either implement minimal script host bound to `SimulationBridge` or move script to documented pseudocode / retire folder. |
| **Owner** | New `scripts/ScriptHost.cs` or docs cleanup |

### G-011 — `MissionPhase.COAST` defined but never driven (P2) — partially closed by C2/C3

| | |
|---|---|
| **Evidence** | C2: `AutopilotController` sets `COAST` on deorbit arm and post-burn. C3: HUD track includes COAST + cues. Ascent coast-to-apoapsis still uses `AscentController` internal banner only. |
| **Gap** | Coast-to-apoapsis between MECO and circularization remains invisible to mission FSM. |
| **Recommendation** | Optionally set `COAST` between MECO/SEPARATION and circularization; tie objective checkpoint. |
| **Owner** | `MissionManager.cs`, `AscentController.cs` |

### G-012 — Constructed craft spawn always boots default stack first (P2)

| | |
|---|---|
| **Evidence** | `SimulationBridge._Ready`: `SpawnStarshipStack` then `SpawnPendingConstructedVessel` (`130-131`). VAB path removes active vessel in `PlaceConstructedVesselOnPad` (`424-425`) — works but wastes default stack creation every VAB launch. |
| **Gap** | Minor perf/clarity; Continue-save path would need skip-default-spawn logic. |
| **Recommendation** | If `CraftLaunchRequest` pending or save slot loading, skip `SpawnStarshipStack`. |
| **Owner** | `SimulationBridge.cs` |

### G-013 — Save slots have no metadata (name, phase, timestamp) (P2)

| | |
|---|---|
| **Evidence** | `ListSaveSlots` returns filenames only (`SaveSystem.cs:54-59`). No slot preview for MainMenu Continue. |
| **Recommendation** | Embed `MissionPhase`, `ActiveVesselName`, `CurrentTime`, screenshot thumb in save header. |
| **Owner** | `SaveSystem.cs`, `MainMenu.cs` |

### G-014 — `CrewMember` EVA path unused (P2)

| | |
|---|---|
| **Evidence** | `TickEVA`, `ComputeEVARisk`, `CrewStatus` (`CrewMember.cs:51-77`) — no game-layer caller. `PLAN_NEXT_SESSION.md` lists crew EVA as non-goal. |
| **Recommendation** | Defer until crew roster (G-003) ships; then EVA as optional objective type. |
| **Owner** | Future gameplay layer |

### G-015 — No dedicated mission menu / briefing flow (P2)

| | |
|---|---|
| **Evidence** | ROADMAP VAB: "Menu principal dedicado" pending. `MainMenu` → Flight or VAB only; no mission select, difficulty, or range weather. |
| **Recommendation** | Mission browser card replacing static IFT-7 blurb; ties to G-002 objectives. |
| **Owner** | `MainMenu.cs`, `scenes/ui/` |

### G-016 — Interplanetary cruise not tied to mission objectives (P2)

| | |
|---|---|
| **Evidence** | Hohmann/encounter exist (`TransferPlanner.cs`, ROADMAP interplanetario). No mission type "Mars transfer" with encounter win condition. |
| **Recommendation** | After G-002 scaffold, add `MissionType.Interplanetary` with SOI-crossing checkpoints. |
| **Owner** | `MissionManager.cs`, `TransferPlanner.cs` |

### G-017 — Booster return / tower catch absent (P2, blocked)

| | |
|---|---|
| **Evidence** | ROADMAP + R12: depends on R5 multi-motor. Debris SH has renderer (`VesselRenderer.cs` standalone SH) but no guidance. |
| **Recommendation** | Track as post-R5 epic; until then document as known limit in mission briefing. |
| **Owner** | Deferred |

### G-018 — Craft persistence separate from mission persistence (P1)

| | |
|---|---|
| **Evidence** | VAB saves `VesselCraftDefinition` to `user://crafts/` (`ConstructionController.OnSave`). Mission save (`~/.local/share/Exosphere/saves/`) does not include craft definition ID or assembly instance IDs. |
| **Gap** | Reloading mid-mission cannot reconcile custom VAB builds; two parallel persistence models confuse "save craft" vs "save flight". |
| **Recommendation** | Mission save embeds `VesselCraftDefinition` snapshot or craft file hash; VAB "Save as mission template". |
| **Owner** | `SaveSystem.cs`, `VesselCraftDefinition.cs` |

---

## ROADMAP crosswalk

| ROADMAP item | Audit IDs |
|--------------|-----------|
| Save/load de mision | G-001, G-013, G-018 |
| Misiones/objetivos | G-002, G-011, G-016 |
| Recursos vida/energia/comms/termica | G-003, G-004, G-009 |
| Fallos, damage, recuperacion | G-005, G-007 |
| VAB validacion pre-launch | G-008 |
| Menu principal dedicado | G-015 |
| Multi-vessel / boostback | G-006, G-017 |

---

## Recommended execution order

1. **G-001 + G-013 + G-018** — credible mission persistence (unblocks everything else).  
2. **G-002 + G-007 + G-011** — one complete scripted arc with landing success.  
3. **G-008 + G-015** — VAB/menu gates match real pre-launch discipline.  
4. **G-003 + G-004 + G-009** — crew + comms + systems as mission pressure.  
5. **G-005 + G-006** — failures and multi-vessel (G-006 partial before R5).  
6. **G-010, G-014, G-016, G-017** — defer / blocked.

**Explicit non-goals (this audit):** R5 full multi-motor, engine-out gameplay, crew EVA V1, visual-only tranche items in `PLAN_VISUAL_REALISM.md`.

---

## Top 5 (mission-fantasy impact)

1. **G-001** — Mission save/load incomplete + no UI  
2. **G-002** — No objectives / win-lose progression  
3. **G-007** — Pad-to-recovery arc ends at `LANDED` with no turnaround  
4. **G-006** — Multi-vessel sim without command switching or booster ops  
5. **G-004** — Comms delay cosmetic; no flight-director gameplay  

---

## Self-check

- Scanned: ROADMAP, SaveSystem, MissionManager, CrewMember, Systems/*, lua_scripts, Construction/*, SimulationBridge staging, SystemsController consequences, MainMenu, PLAN_NEXT_SESSION G-P track.  
- Template aligned with `docs/physics_audit.md` (evidence table + priority fixes) and `.atl/OVERENGINEERING_AUDIT_JUL2026.md` (severity counts + ID blocks).

---

*Links: `UX_EXPERIENCE_AUDIT.md`, `PHYSICS_DEEP_AUDIT.md`, `MASTER_IMPROVEMENT_INDEX.md`*
