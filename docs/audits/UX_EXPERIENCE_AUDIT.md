# UX & Experience Audit — Exosphere

**Date:** 2026-07-02  
**Scope:** Menu → VAB → launch → flight → map → warp → telemetry → EDL → failure feedback  
**Method:** Code/scene review of `scenes/ui/`, `scripts/UI/`, `MainMenu.cs`, `ConstructionController.cs`, `HUDController.cs`, `SystemsHUD.cs`, `EngineGridHUD.cs`, `AttitudeNavball.cs`, `CameraController.cs`, `MissionManager.cs`, `AscentController.cs`, `EDLController.cs`, `MapViewController.cs`, `WarpController.cs`, `PLAN_PLAYTEST.md`  
**Goal:** Identify what breaks the feeling of a coherent SpaceX-style mission arc.

**Item count:** 24 (UX-001 … UX-024)

---

## Top 5 (highest impact on “SpaceX real mission” feel)

1. **UX-001** — No onboarding; launch controls (`L` / hold-`Z` / `G` / `H`) undiscoverable  
2. **UX-010** — EDL is fully autopiloted; player is spectator during belly-flop / flip-and-burn  
3. **UX-007** — No orbit → deorbit → entry player workflow (only debug `O` / forced physics)  
4. **UX-004** — Main menu mission card is fiction; not wired to VAB craft, site, or destination  
5. **UX-009** — Phase banner/track stops at ORBIT; EDL arc invisible in primary mission UI  

---

## 1. Onboarding / Flow

### UX-001 — No first-flight onboarding or controls overlay

| Field | Detail |
|-------|--------|
| **Evidence** | `MainMenu.cs` only hints "Press Enter"; flight has no help panel. Controls live in `README.md` (~L111+) but not in-game. `HUDController._UnhandledInput` binds 15+ keys with zero UI discovery. |
| **User pain / realism break** | New player lands on pad with SpaceX-style HUD but no idea whether to press `L`, hold `Z`, or `G`. Feels like a dev build, not a launch broadcast. |
| **Proposed solution** | First-run overlay + collapsible "FLIGHT CONTROLS" panel (pad-only → hide after liftoff). Context tips at PRE-LAUNCH: "L = GO FOR LAUNCH", "Z = MANUAL IGNITION". |
| **Acceptance** | Play harness milestone 1 (pad pre-launch PNG): overlay visible; after first liftoff, overlay stays dismissed in same session. |
| **Impact** | Critical |
| **Effort** | M |
| **Dependencies** | None |

### UX-002 — Two incompatible launch paths without explanation

| Field | Detail |
|-------|--------|
| **Evidence** | `MissionManager.StartCountdown()` (`L`) vs `BeginFlight()` via hold-`Z`/`Ignite()` (`SimulationBridge`, `HUDController._Process`). Countdown UI in `HUDController.UpdateCountdown`; manual path skips COUNTDOWN/IGNITION phases. |
| **User pain / realism break** | SpaceX webcast has one GO sequence; here the player can accidentally skip T-10 callouts or get stuck at T-0 if TWR < 1.02 with no UI explanation. |
| **Proposed solution** | Unify UX copy: countdown path = "AUTO SEQUENCE"; hold-Z = "MANUAL STARTUP". Show hold-down/TWR gate on countdown stall ("HOLD — INSUFFICIENT THRUST"). |
| **Acceptance** | Harness: force low throttle at T-0 → HUD shows hold reason; manual Z path prints distinct event-log line. |
| **Impact** | High |
| **Effort** | S |
| **Dependencies** | UX-001 |

### UX-003 — Escape from flight drops to menu with no pause/confirm

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController._UnhandledInput`: `Escape` → `MainMenu.tscn` immediately. No pause, no "abort mission?" |
| **User pain / realism break** | Accidental Esc ends the mission silently; no SpaceX-style "mission abort" beat. |
| **Proposed solution** | Pause modal: RESUME / MAIN MENU / VAB. Optional "hold Esc 1s" for menu. |
| **Acceptance** | Single Esc opens pause; flight sim frozen; resume restores warp/throttle state. |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | None |

### UX-004 — Main menu "CURRENT VEHICLE" card is hardcoded fiction

| Field | Detail |
|-------|--------|
| **Evidence** | `MainMenu.BuildMissionCard()`: "STARSHIP IFT-7", "STARBASE", "LOW EARTH ORBIT" static strings. `SimulationBridge.SpawnStarshipStack()` names vessel "Starship IFT-7" but card does not read VAB craft or `CraftLaunchRequest`. |
| **User pain / realism break** | Menu promises a specific flight test; VAB-built craft or START FLIGHT default feel disconnected from the briefing card. |
| **Proposed solution** | Bind card to last saved craft + launch site JSON + optional destination from map plan. Fallback: "DEFAULT STACK" with honest labels. |
| **Acceptance** | Build craft in VAB → menu shows craft name, part count, SL TWR before START FLIGHT. |
| **Impact** | High |
| **Effort** | M |
| **Dependencies** | UX-005 |

---

## 2. HUD Information Architecture

### UX-005 — No dedicated throttle readout on primary HUD

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController` shows speed/alt/T+ and propellant bars; throttle only implied via `EngineGridHUD` lit dots and thrust kN. No "THROTTLE 78%" row. |
| **User pain / realism break** | Webcast always shows engine/throttle context during ascent; player flying manual Z/X lacks a central throttle gauge. |
| **Proposed solution** | Add throttle % + engine mode to bottom band or propulsion panel; amber during ramp/spool. |
| **Acceptance** | Harness liftoff frame: throttle visible and matches `ActiveVessel.Throttle`. |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | None |

### UX-006 — SystemsHUD overlaps right stack; easy to miss alerts

| Field | Detail |
|-------|--------|
| **Evidence** | `SystemsHUD._Ready`: anchored TopRight `OffsetTop = 340` under orbit panel (`HUDController` right panel ~278px wide). `SystemsController` sets `ControlLimited` but only small red text in SystemsHUD. |
| **User pain / realism break** | Life support / comms / power — part of "keep crew alive" pillar — are visually secondary and overlap-prone on 1080p. |
| **Proposed solution** | Collapse systems into right panel tabs OR single "SYSTEMS" row with alert badges on phase banner. Surface CONTROL LIMITED as banner-level alert. |
| **Acceptance** | Trigger comms loss → top banner + audio cue; no overlap with Ap/Pe at 1920×1080. |
| **Impact** | Medium |
| **Effort** | M |
| **Dependencies** | None |

### UX-007 — No orbit → deorbit → entry workflow in HUD/map

| Field | Detail |
|-------|--------|
| **Evidence** | `MapViewController` plans Hohmann/transfers; no "deorbit burn" preset. `PLAN_PLAYTEST.md` B1: harness can't drive DEORBIT→EDL yet. `HUDController`: `O` jumps to orbit debug. |
| **User pain / realism break** | After ORBIT, player has no mission-like path to reentry except debug keys or dying to physics. Breaks full arc in playtest doc milestones 7–8. |
| **Proposed solution** | Map action "DEORBIT TO [site]" (retrograde node at Pe). HUD cue: "ENTRY INTERFACE in ~Xm". Wire to play harness milestone 7. |
| **Acceptance** | Harness gates PNG on `[Mission] → ENTRY` after planned retro burn (no `JumpToOrbit` cheat). |
| **Impact** | Critical |
| **Effort** | L |
| **Dependencies** | PLAN_PLAYTEST harness milestone 7 |

### UX-008 — Unit inconsistency (km/h bottom, m/s left panel)

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController`: `_bigSpeed` in km/h; `_vspeedValue` in m/s; `EngineGridHUD` thrust kN; map `Fmt` mixed km/Mm. |
| **User pain / realism break** | SpaceX webcast uses consistent units per phase; mixed units increase cognitive load. |
| **Proposed solution** | Profile-based units: ascent = km/h + ft/s optional; orbital = m/s; EDL = m/s vertical. One-line unit prefs. |
| **Acceptance** | Screenshot review: bottom band and left panel use documented default set. |
| **Impact** | Low |
| **Effort** | S |
| **Dependencies** | None |

---

## 3. Controls / Input

### UX-009 — Phase progress track omits EDL and COAST

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController.PhaseSequence` ends at `ORBIT`. `MissionPhase` enum includes ENTRY…LANDED/CRASHED (`MissionManager.cs`) but dots don't advance through reentry. |
| **User pain / realism break** | After SECO, mission UI feels "done" while the hardest phase (EDL) uses a separate overlay (`EDLController`). |
| **Proposed solution** | Extend phase track: ORBIT → COAST → ENTRY → PEAK HEATING → … → LANDED. Grey upcoming dots pre-landing. |
| **Acceptance** | Belly-flop harness: phase dots show ≥3 EDL milestones lit sequentially. |
| **Impact** | High |
| **Effort** | S |
| **Dependencies** | UX-007 |

### UX-010 — EDL removes player agency (full autopilot)

| Field | Detail |
|-------|--------|
| **Evidence** | `EDLController.AdvancePhase`: sets `vessel.Orientation`, zeroes rates, closed-loop `Throttle` for RETRO/FINAL. No `[G]`-style EDL assist or manual override flag. |
| **User pain / realism break** | Player watches belly-flop/flip-and-burn as cinema, not flight. Unlike ascent ([G]/[H]/manual), EDL doesn't match "pilot the ship" fantasy. |
| **Proposed solution** | EDL assist mode: autopilot attitude only, player throttle; or manual flip timing with tolerance bands. Keep full-auto as default for [G]-parity. |
| **Acceptance** | Assist mode: player can fail touchdown (>3 m/s) if throttle mismanaged; auto still achieves ≤2 m/s in harness milestone 8. |
| **Impact** | Critical |
| **Effort** | L |
| **Dependencies** | UX-007 |

### UX-011 — Debug keys exposed in production UX (`O`, `J`, map cheats)

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController`: `O` → `JumpToOrbit()`. `MapViewController`: `J` → `JumpToBody`. No dev-gating. |
| **User pain / realism break** | One key skips ascent/orbit insertion — destroys mission pacing and any sense of earned orbit. |
| **Proposed solution** | Gate behind `ProjectSettings` debug flag or hold-modifier; replace with diegetic "MISSION SIM" menu in dev only. |
| **Acceptance** | Release build: O/J no-op with tooltip "disabled in mission mode". |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | None |

### UX-012 — Map footer is unreadable; hidden affordances

| Field | Detail |
|-------|--------|
| **Evidence** | `MapViewController.DrawFooter`: single 10px line listing 12 bindings. Panel 460×460; transfer list competes for space. |
| **User pain / realism break** | Interplanetary planning exists but feels like a programmer's tool, not a flight director map. |
| **Proposed solution** | Two-line footer + icon legend; contextual hints when node selected ("⏎ EXECUTE BURN"). |
| **Acceptance** | Usability pass: new tester executes transfer burn without README. |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | None |

---

## 4. Mission Phase Communication

### UX-013 — Event log too small; no audio for most milestones

| Field | Detail |
|-------|--------|
| **Evidence** | `HUDController.UpdateEventLog`: max 5 lines, 11px font. `AudioManager`: liftoff/staging/countdown only (`MissionManager.SetPhase`). MAX-Q, MECO, SECO silent. |
| **User pain / realism break** | SpaceX webcast is callout-driven (Max-Q, MECO, stage, SECO). Here milestones are easy to miss. |
| **Proposed solution** | Scrolling event ticker + optional announcer lines for MAX-Q, MECO, SEP, SECO, ENTRY, TOUCHDOWN. |
| **Acceptance** | Harness milestones 3–5: each triggers distinct audio/log line verifiable in `/tmp/exo_play.log`. |
| **Impact** | High |
| **Effort** | M |
| **Dependencies** | PLAN_PLAYTEST harness milestones 3–5 |

### UX-014 — Ascent/assist banners compete with phase banner (incl. cockpit clutter)

| Field | Detail |
|-------|--------|
| **Evidence** | `AscentController._Draw` status at ~21.5% viewport height; `HUDController` phase banner at top center; EDL banner at 16% (`EDLController.DrawPhaseBanner`). `CameraController` cockpit mode (`[C]`) hides exterior mesh but leaves full webcast HUD (`HUDController`, `EngineGridHUD`, `AttitudeNavball`) on top of `CockpitInstruments` PFD screens. |
| **User pain / realism break** | Three overlay voices during ascent/EDL; unclear which is "official" mission state. Cockpit mode promises diegetic instruments but still shows full webcast overlay — cluttered, not airliner/ship diegetic. |
| **Proposed solution** | Single `MissionCalloutController`: phase owns headline; autopilot subscribes as secondary line. Cockpit mode: minimal diegetic HUD (warnings only) + screen instruments; chase mode: full webcast overlay. |
| **Acceptance** | `[G]` engaged: one primary + one sub caption; no vertical collision at 1080p. Cockpit screenshot: no navball/engine grid; warnings + screen PFD only. |
| **Impact** | Medium |
| **Effort** | M |
| **Dependencies** | None |

---

## 5. VAB UX Gaps

### UX-015 — VAB UI not using `InterfaceTheme` (visual/UX discontinuity)

| Field | Detail |
|-------|--------|
| **Evidence** | `ConstructionController.BuildUi` uses raw `ItemList`/`Button`; no `InterfaceTheme.StyleButton`. `MainMenu`/`HUD` use glass monochrome aesthetic. |
| **User pain / realism break** | VAB feels like internal tooling; breaking immersion between menu and flight. |
| **Proposed solution** | Apply `InterfaceTheme` to VAB panels/buttons; match typography and spacing to MainMenu. |
| **Acceptance** | Side-by-side screenshot: menu ↔ VAB ↔ pad share palette and button styles. |
| **Impact** | Medium |
| **Effort** | M |
| **Dependencies** | None |

### UX-016 — No pre-launch validation gate from VAB

| Field | Detail |
|-------|--------|
| **Evidence** | `ConstructionController.OnLaunch` exports craft and jumps to flight; no TWR/atmosphere/staging checks UI. Stats show SL TWR but don't block launch. |
| **User pain / realism break** | Player can launch unflyable stacks — failure feels like sim bug, not user error. |
| **Proposed solution** | Launch checklist modal: TWR>1, engine present, decoupler order, Δv to LEO estimate. Warnings vs hard stops. |
| **Acceptance** | Craft with TWR<1 shows blocking dialog; default Starship passes. |
| **Impact** | High |
| **Effort** | M |
| **Dependencies** | UX-015 |

### UX-017 — VAB lacks gizmos and compatible-node feedback (ROADMAP-known)

| Field | Detail |
|-------|--------|
| **Evidence** | `ROADMAP.md` VAB pendientes; `ConstructionController` hint mentions click attach only. No drag/rotate gizmos. |
| **User pain / realism break** | Building the iconic stack is tedious vs SFS/KSP expectation; incompatible nodes fail via status text only. |
| **Proposed solution** | Highlight compatible nodes green/red; ghost preview on hover; post-MVP gizmos. |
| **Acceptance** | Select tank → only valid stack nodes pulse; invalid click explains why. |
| **Impact** | Medium |
| **Effort** | L |
| **Dependencies** | VAB picking layer |

---

## 6. Map / Orbit UX

### UX-018 — Map hidden by default; no orbit-insertion cue to open it

| Field | Detail |
|-------|--------|
| **Evidence** | `MapViewController._Ready`: `Visible = false`. No auto-open on ORBIT phase. |
| **User pain / realism break** | Orbit is a major milestone but map (planning surface) isn't part of the celebration beat. |
| **Proposed solution** | On SECO/ORBIT: subtle prompt "M — ORBITAL MAP"; optional auto-open first orbit only. |
| **Acceptance** | First orbit: prompt shown once; map toggle works. |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | UX-007 |

### UX-019 — Maneuver node editing requires hidden chord knowledge

| Field | Detail |
|-------|--------|
| **Evidence** | `MapViewController.DrawFooter` documents wheel/Shift/Alt; drag places node but prograde-only unless Alt held. |
| **User pain / realism break** | KSP-trained players expect gizmo handles; others never discover radial/normal. |
| **Proposed solution** | On-node UI toggles PRO/RAD/NML; show burn vector arrow on ship map icon. |
| **Acceptance** | User adds 100 m/s radial burn without keyboard modifiers. |
| **Impact** | Medium |
| **Effort** | M |
| **Dependencies** | UX-012 |

---

## 7. Warp UX

### UX-020 — Warp restricted but poorly explained when clamped

| Field | Detail |
|-------|--------|
| **Evidence** | `SimulationBridge._Process` clamps `MaxAllowedWarpIndex` (atmo x3, thrust x10). `WarpController` shows MAXIMUM but not *why* capped. `HUDController._warpValue` duplicates info. |
| **User pain / realism break** | Player hits `,`/`.` and warp doesn't rise — feels broken without "ATMOSPHERIC LIMIT" or "THRUST ACTIVE" reason. |
| **Proposed solution** | When clamped, show reason string on WarpController; merge duplicate TIME WARP row in HUD or link them. |
| **Acceptance** | Ascent at x3 with throttle up: HUD says "WARP LIMITED — THRUSTING". |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | None |

### UX-021 — No warp-to-node / warp-to-apoapsis UX

| Field | Detail |
|-------|--------|
| **Evidence** | `MapViewController` computes `TimeToNode()`; no warp helper. README promises scripting API `WARP_TO_APOAPSIS()` — not exposed in HUD. |
| **User pain / realism break** | Long coasts require manual warp stepping — breaks mission rhythm after orbit. |
| **Proposed solution** | Map button "WARP TO NODE" with auto-clamp to safe physics mode; cancel on SOI/atmo entry. |
| **Acceptance** | Plan node 45m ahead → warp stops ≤30s before node with throttle forced 0. |
| **Impact** | High |
| **Effort** | L |
| **Dependencies** | UX-019 |

---

## 8. Failure / Destruction Feedback

### UX-022 — Crash/endgame has no recovery flow

| Field | Detail |
|-------|--------|
| **Evidence** | `MissionManager` → CRASHED logs + `ExplosionController` VFX. `HUDController` event "VEHICLE LOST". No modal, retry, or return to menu prompt. `Esc` only path. |
| **User pain / realism break** | Hard impact ends with explosion and frozen sim — no "FTS"/investigation beat, no quick restart. |
| **Proposed solution** | Full-screen MISSION OUTCOME: impact speed, phase, RESTART / MAIN MENU / VAB. Delay 2s for VFX. |
| **Acceptance** | Harness crash scenario → outcome card shows impact m/s; Restart places on pad. |
| **Impact** | High |
| **Effort** | M |
| **Dependencies** | UX-003 |

### UX-023 — Thermal breakup vs hard impact indistinguishable to player

| Field | Detail |
|-------|--------|
| **Evidence** | `ReentryBreakupController` + `ExplosionController`; both end at `IsDestroyed`. Event log always "VEHICLE LOST". |
| **User pain / realism break** | SpaceX test program distinguishes RUD reasons; player can't learn heat-shield vs velocity failure. |
| **Proposed solution** | Destruction cause enum → distinct copy ("THERMAL BREAKUP" vs "HARD IMPACT") + color on outcome card. |
| **Acceptance** | Force heat failure vs pad impact → different outcome strings in log and UI. |
| **Impact** | Medium |
| **Effort** | S |
| **Dependencies** | UX-022 |

---

## 9. Accessibility / Readability

### UX-024 — Small text and no scalable UI / colorblind-safe accents

| Field | Detail |
|-------|--------|
| **Evidence** | Event log 11px; map footer 10px; `InterfaceTheme` grays-only accents (Alert/Warning red/amber). No UI scale project setting. |
| **User pain / realism break** | 1440p/4K and accessibility: critical alerts (suborbital IMPACT, CONTROL LIMITED) hard to read stream-style on laptop. |
| **Proposed solution** | Global UI scale 100–125%; minimum 12px body; add iconography beside color-only alerts. |
| **Acceptance** | 125% scale: no clipped panels at 1920×1080; IMPACT row shows ⚠ icon. |
| **Impact** | Medium |
| **Effort** | M |
| **Dependencies** | None |

---

## 10. Diegetic vs Overlay Balance

Cockpit-vs-webcast overlay tension is folded into **UX-014** (unified callout + cockpit mode hides non-diegetic HUD). Additional diegetic opportunities:

- Chase camera: full webcast stack (navball, engine grid, phase track).
- Cockpit camera: screen-bound PFD/attitude only; warnings as minimal strip.
- Map and warp panels remain overlay in both modes until dedicated MFD surfaces exist.

---

## Cross-cutting: Docs vs playable reality

| Issue | Evidence |
|-------|----------|
| README lists Lua SAS modes, EVA, 9-readout HUD | Features section vs no MoonSharp/Lua in `scripts/`; SAS is binary damping (`Vessel.cs`); no EVA UX |
| ROADMAP "Menu principal dedicado" still open | `MainMenu` exists but minimal — no settings, continue, mission select |

**Recommendation:** Add `docs/PLAYABLE_TRUTH.md` or trim README features to match shipped UX — reduces expectation gap that hurts "real mission" feel.

---

## Suggested implementation order (UX-only)

1. UX-001, UX-002, UX-004 (flow honesty)  
2. UX-007, UX-009, UX-010 + PLAN_PLAYTEST harness milestone 7–8  
3. UX-013, UX-022 (callouts + outcomes)  
4. UX-015, UX-016 (VAB polish)  
5. UX-014, UX-020, UX-021 (cockpit/callout/warp/orbit QoL)

---

## Play harness milestones (from `PLAN_PLAYTEST.md`)

| Milestone | UX items validated |
|-----------|-------------------|
| 1 Pad pre-launch | UX-001, UX-004, UX-015 |
| 2 Liftoff | UX-002, UX-005, UX-013 |
| 3 Max-Q | UX-013, UX-014 |
| 5 Orbit | UX-009, UX-018 |
| 7 Deorbit + reentry | UX-007, UX-009 |
| 8 EDL touchdown | UX-010, UX-022 |
| Crash | UX-022, UX-023 |

---

## Evidence anchors (quick reference)

- Menu fiction: `scripts/MainMenu.cs` L172–179  
- Phase track ends at ORBIT: `scripts/HUDController.cs` L74–79  
- EDL autopilot: `scripts/EDLController.cs` L186–214  
- Deorbit harness gap: `PLAN_PLAYTEST.md` L126–130  
- VAB no theme: `scripts/ConstructionController.cs` (no `InterfaceTheme` usage)  
- Debug orbit jump: `scripts/HUDController.cs` L696–698  
