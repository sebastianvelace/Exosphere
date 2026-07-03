# Exosphere Visual Deep Audit — Jul 2026

**Scope:** Starship/Super Heavy exterior, plumes, pad, reentry, orbit/space, camera, lighting, planets/atmosphere, HUD vs render.  
**Evidence base:** `PLAN_VISUAL_REALISM.md`, `PLAN_NEXT_SESSION.md`, `ROADMAP.md`, `PLAN_PLAYTEST.md`, source scan of render scripts + `assets/shaders/`.  
**Verification standard:** xvfb PNG at 1920×1080, state-gated captures (altitude/phase, not frame count).

---

## Executive summary — top 10 by impact × effort

| Rank | ID | Item | Impact | Effort | Why first |
|------|-----|------|--------|--------|-----------|
| 1 | **V-050** | State-gated play harness (pad→EDL PNG+log) | 5 | M | Unblocks every reference compare and reentry lighting verify |
| 2 | **V-024** | Belly-flop EDL reference capture (nominal) | 5 | M | No repo baseline for reentry VFX/lighting acceptance |
| 3 | **V-017** | OLM centre hole sized for legacy 1.15u hull, not 9 m | 4 | S | Pad reads "toy mount" vs real OLM interface |
| 4 | **V-009** | Deluge cloud vs stack silhouette at liftoff | 4 | S | Known open item in `PLAN_VISUAL_REALISM.md`; N5 cloud is strong |
| 5 | **V-012** | Hot-staging side-by-side vs IFT T+2:39 | 4 | S | Implemented + ascent capture done; intensity/encuadre still open |
| 6 | **V-007** | Zone charring (nose/belly/flap) — WIP in tree | 4 | S | `VesselRenderer.cs` diff implements zones; needs EDL xvfb + commit |
| 7 | **V-028** | Reentry lighting overlay verify | 4 | S | `PhaseLightingController.cs:39-83` coded; blocked without EDL frame |
| 8 | **V-039** | SkyController vs PhaseLighting ambient fight | 3 | S | Both write `_env.AmbientLightEnergy` each frame |
| 9 | **V-001** | Grid fin proportion reference compare | 3 | S | Close-up xvfb exists; fine IFT/Starbase diff not closed |
| 10 | **V-051** | CI visual PNG artifacts (V5) | 4 | L | Prevents silent render regressions |

---

## Vehicle exterior

### V-001 — Grid fin proportions unverified
- **Evidence:** `VesselRenderer.cs:260-317` (`AddSHGridFins`); `PLAN_VISUAL_REALISM.md:105-107` pending reference compare; capture `/tmp/exosphere_gridfin_closeup.png` cited but not in repo.
- **Realism gap:** Fins are the booster's signature silhouette; wrong chord/hinge breaks "this is Super Heavy at Starbase."
- **Proposed solution:** Side-by-side xvfb close-up vs IFT/Starbase still; tune `rootChord`/`tipChord`/`height` only if diff visible.
- **Acceptance test:** `/tmp/exo_gridfin_ref.png` — trapezoidal plate + hinge drum recognizable at 80 m pad distance without looking rectangular.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk [G]/EDL:** None

### V-002 — Nose ogive / flap scale fine-tune
- **Evidence:** `VesselRenderer.cs:396-471` (ogive segments, flap sizes); `PLAN_VISUAL_REALISM.md:117-120` acceptance "pad lateral immediately Starship/Super Heavy."
- **Realism gap:** Medium-distance pad shot still reads "generic rocket" if nose is too blunt or aft flaps dominate.
- **Proposed solution:** Pad lateral capture vs SpaceX vehicle page; adjust `AddFlap` dimensions ±5% max.
- **Acceptance test:** `/tmp/exo_pad_lateral.png` — nose, 4 grid fins, 4 flaps identifiable in one frame.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-003 — Tile layout density vs Flight 4–6 TPS
- **Evidence:** `VesselRenderer.cs:396-399` (`AddTileBand` staves); black tile band on -X only.
- **Realism gap:** Real windward TPS has recognizable panel boundaries and nose transition; uniform staves look procedural.
- **Proposed solution:** Add 2–3 distinct tile "zones" (nose cap, belly center, flap edges) via band breaks only — no new assets.
- **Acceptance test:** Close-up windward capture shows ≥3 distinguishable tile regions.
- **Impact:** 3 | **Effort:** M | **Dependencies:** None | **Risk:** None

### V-004 — Service markings too subtle at mission scale
- **Evidence:** `VesselRenderer.cs:852-853` ("Minimal serial-style bars… avoids fake logos").
- **Realism gap:** Real stacks have legible (but not logo) marking cues; player lacks "flight hardware" read at pad distance.
- **Proposed solution:** Slightly raise contrast/width of serial bars on upper steel; keep leeward-only.
- **Acceptance test:** Pad lateral 1920×1080: marking cue visible without zoom harness.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-005 — Booster steel warmth vs Ship brightness
- **Evidence:** `VesselRenderer.cs:142-146` (SH warmer/duller steel); `steel.gdshader` `base_tint` per section.
- **Realism gap:** Post-staging, booster debris should read sootier/handled vs clean upper stage.
- **Proposed solution:** Reference compare at hot-staging frame; tune SH `soot_y0/y1` only.
- **Acceptance test:** Hot-stage multiframe: separated SH top ring + scorch reads darker than Ship.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-012 | **Risk:** None

### V-006 — Payload door cues first-pass only
- **Evidence:** `VesselRenderer.cs:824` ("Subtle leeward payload-bay/maintenance panel outline").
- **Realism gap:** Orbital Starship identity includes payload door region; absent cue weakens "operational vehicle."
- **Proposed solution:** One faint leeward door line + hinge hint; no animation.
- **Acceptance test:** Ship-only orbit capture: door cue visible on sunlit leeward side.
- **Impact:** 2 | **Effort:** S | **Dependencies:** None | **Risk:** None

### V-007 — Per-zone tile charring (WIP)
- **Evidence:** Git diff `VesselRenderer.cs:17-33,557-618` — `ThermalCharZone` Nose/Belly/Flap; uncommitted. `PLAN_VISUAL_REALISM.md:194-195` pending.
- **Realism gap:** Uniform charring hides orientation consequences (Flight 4–6 tile damage narrative).
- **Proposed solution:** Commit zone charring; capture bad-attitude vs belly-flop char progression.
- **Acceptance test:** Two EDL captures: belly char lags nose/flap on bad attitude.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-024, V-025 | **Risk EDL:** Low — render-only, maps existing `Part.ThermalDamage`

### V-008 — Heat-shield border readability
- **Evidence:** `AddHeatShieldBorder` calls `VesselRenderer.cs:399,441`; windward -X convention.
- **Realism gap:** Tile-to-steel transition is a strong real-world cue; weak border → "painted black side."
- **Proposed solution:** Reference compare; bump border thickness/roughness 10% if needed.
- **Acceptance test:** Pad lateral: black tile band clearly separated from steel.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

---

## Plumes / VFX

### V-009 — Deluge cloud occludes stack silhouette
- **Evidence:** `PLAN_VISUAL_REALISM.md:160-161` open; `LaunchEffectsController.cs:32-35` (`TriggerCeilingM=550`, `FullIntensityM=140`); 5-layer N5 cloud.
- **Realism gap:** Real liftoff shows stack *through* steam, not hidden behind it; player loses vehicle state read.
- **Proposed solution:** Lateral liftoff capture; if stack >40% occluded, reduce `_steamCore` alpha/amount 10–15% only — do not rewrite N5.
- **Acceptance test:** `/tmp/exo_liftoff_lateral.png` — full stack silhouette + HUD readable.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-050 | **Risk [G]:** None

### V-010 — Ground cloud "floats" as pad recedes
- **Evidence:** `LaunchEffectsController.cs:16-19` ground anchor `-up * (altitude/MetresPerUnit)`; `PLAN_VISUAL_REALISM.md:160-161`.
- **Realism gap:** Cloud should stay on pad while rocket climbs; floating cloud breaks ground contact.
- **Proposed solution:** Capture liftoff sequence 0–300 m; tune fade vs altitude if cloud tracks vessel.
- **Acceptance test:** Multiframe 80–350 m: cloud remains at ground Y, stack separates upward.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-011 — Startup/ramp timing vs IFT chill
- **Evidence:** `EngineStartupController.cs:43-49` (ground-held + throttle); `PLAN_VISUAL_REALISM.md:144-147` pending IFT compare.
- **Realism gap:** Instant full plume breaks pre-launch tension; real hold-down has progressive ignition.
- **Proposed solution:** Multiframe T-3s→liftoff via harness; adjust `_intensity` ramp rates only.
- **Acceptance test:** No frame jumps 0→full; vapor/chill visible before release.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-050 | **Risk [G]:** None

### V-012 — Hot-staging IFT intensity/encuadre
- **Evidence:** `HotStageFlashController.cs:13,47-68`; ascent capture noted `PLAN_VISUAL_REALISM.md:154-158`; side-by-side IFT still open.
- **Realism gap:** Hot-staging is Starship's signature; "almost right" still feels gamey.
- **Proposed solution:** Side-by-side notes vs IFT T+2:39; tune flash energy/soot ring scale ±15%.
- **Acceptance test:** `[G]` ascent at SEPARATION: flash, ring scorch, Ship lit, HUD legible in one frame.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-050 | **Risk [G]:** None — VFX only

### V-013 — Vacuum plume reference at orbital altitude
- **Evidence:** `PlumeSystem.cs:172-178` smoke attenuation; `raptor_plume.gdshader:174-191` `vacuumDim`/`vacuumAlpha`; `PLAN_VISUAL_REALISM.md:135-143`.
- **Realism gap:** Orbital burn must read blue/tenue vs SL fat flame; wrong read = "still on pad."
- **Proposed solution:** Capture at 120–152 km post `[G]`; compare to upper-stage references; tune uniforms only.
- **Acceptance test:** Orbit PNG: thin blue core, minimal smoke/soot, Earth backdrop visible through plume edges.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-014 — Max-Q plume brightness vs HUD
- **Evidence:** `PlumeSystem.cs:201-208` omni lights up to 9.0 energy SH; `HUDController` glass panels `InterfaceTheme.GlassPanel(0.62-0.76)`.
- **Realism gap:** Max-Q is high-drama; washed HUD breaks mission-control feel.
- **Proposed solution:** Capture at `MissionPhase.MAX_Q`; if HUD clips, reduce omni `groundBoost` 5% or raise outline alpha — not global tonemap.
- **Acceptance test:** Max-Q PNG: altitude/speed/Q labels readable, plume still brilliant.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk [G]:** None

### V-015 — Plume pad lighting doesn't reach tower
- **Evidence:** `PlumeSystem.cs:195-209` omni at nozzle only; Mechazilla at `-35m` X `LaunchPadController.cs:639`.
- **Realism gap:** Liftoff lights tower/legs in real footage; flat tower breaks night-launch realism.
- **Proposed solution:** Optional second weak omni aimed at tower base during pad phase only.
- **Acceptance test:** Liftoff lateral: tower near side brighter than ambient on steel edges.
- **Impact:** 2 | **Effort:** S | **Dependencies:** None | **Risk:** None

### V-016 — Mach diamond readability in ascent band
- **Evidence:** `raptor_plume.gdshader:126-142` diamond logic; SL energy 4.6 SH per plan bitacora.
- **Realism gap:** Diamonds sell "supersonic exhaust"; missing read = generic flame cone.
- **Proposed solution:** Max-Q capture; if diamonds absent, bump `diamond_count` or `sharp` at `effExpansion<0.3` only.
- **Acceptance test:** Ascent 15–25 km capture shows ≥2 visible diamond nodes in core.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

---

## Pad / environment

### V-017 — OLM centre hole radius stale (critical scale bug)
- **Evidence:** `LaunchPadController.cs:463-464` `innerR = 3.6f * U` comment "booster R≈1.15u"; `VesselRenderer.cs:46-48` `BodyR=1.607f` (9 m Ø).
- **Realism gap:** Mount interface doesn't match vehicle; undermines real-scale credibility at pad.
- **Proposed solution:** Set `innerR ≈ (BodyR + clearance) * U` (~4.5–5.0 m); rescale hold-down ring/clamps consistently.
- **Acceptance test:** Top-down/lateral pad: booster skirt fills OLM hole without clipping or huge gap.
- **Impact:** 4 | **Effort:** S | **Dependencies:** None | **Risk:** None

### V-018 — Mechazilla/chopsticks static
- **Evidence:** `LaunchPadController.cs:631-746` procedural tower; no animation/state.
- **Realism gap:** Starbase identity includes chopsticks; static is acceptable V1 but reads diorama.
- **Proposed solution:** Defer motion; add subtle night lighting + cable detail pass only.
- **Acceptance test:** Pad dawn capture: tower scale plausible vs ~121 m stack (tower ~145 m).
- **Impact:** 2 | **Effort:** M | **Dependencies:** None | **Risk:** None

### V-019 — No human/vehicle scale cues on apron
- **Evidence:** Coastal site roads/berms `LaunchPadController.cs:128-197`; no figures/vehicles.
- **Realism gap:** 9 m × 121 m stack needs scale anchors; empty apron feels empty sim, not range.
- **Proposed solution:** Optional 2–3 static truck/person silhouettes near apron edge (low poly, no gameplay).
- **Acceptance test:** Pad wide shot: at least one scale cue without cluttering stack.
- **Impact:** 2 | **Effort:** M | **Dependencies:** None | **Risk:** None

### V-020 — Starbase-specific landmarks generic
- **Evidence:** Tank farm, lightning towers `LaunchPadController.cs:863-990`; not labeled Starbase-accurate layout.
- **Realism gap:** Player familiar with IFT won't recognize "Starbase," only "industrial pad."
- **Proposed solution:** Document as V2; adjust tank farm cluster spacing from reference photo if cheap.
- **Acceptance test:** Optional compare matrix row filled with one-sentence diff.
- **Impact:** 2 | **Effort:** L | **Dependencies:** Reference gather | **Risk:** None

### V-021 — Flame trench read from pad camera
- **Evidence:** `LaunchPadController.cs:95-127` trench geometry; deflector `528-533`.
- **Realism gap:** Liftoff drama includes trench/deflector; invisible trench weakens pad realism.
- **Proposed solution:** Pad preset yaw toward +Z trench; darken trench walls 10% for contrast.
- **Acceptance test:** Liftoff capture includes trench opening under plume.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-022 — Deluge infrastructure vs cloud disconnect
- **Evidence:** Deluge outlets `LaunchPadController.cs:235`; deluge tank `359-388`; cloud in `LaunchEffectsController`.
- **Realism gap:** Physical deluge cues exist but player may not connect water → horizontal cloud.
- **Proposed solution:** Ensure startup steam originates near deck outlets (align `EngineStartupController` steam to OLM deck Y).
- **Acceptance test:** Startup frame: vapor at deck level before full deluge bloom.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-011 | **Risk:** None

---

## Reentry

### V-023 — Plasma alpha tuning unverified (WIP diff)
- **Evidence:** Uncommitted `ReentryPlasmaController.cs` reduces shock alpha `0.36→0.26`, edge glow emission `(2+8*k)→(1.6+5.5*k)`.
- **Realism gap:** Too dim = reentry feels cold; too bright = arcade fireball.
- **Proposed solution:** Before/after xvfb on same EDL milestone; keep change only if HUD improves without losing shock read.
- **Acceptance test:** Belly-flop peak: shock visible on windward; navball not obscured >30%.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-024 | **Risk EDL:** None — VFX only

### V-024 — No nominal belly-flop EDL capture baseline
- **Evidence:** `PLAN_VISUAL_REALISM.md:187-188,193-196`; `PLAN_PLAYTEST.md` harness blocks milestone 7; synthetic only `/tmp/exosphere_reentry_edges.png`.
- **Realism gap:** Team cannot close reentry visual acceptance; player can't "see" validated EDL.
- **Proposed solution:** V-050 harness → DEORBIT→EDL → PNG at `PEAK_HEATING` + `AERO_DESCENT`.
- **Acceptance test:** `/tmp/exo_edl_belly.png` + telemetry log line `heatRatio, phase`.
- **Impact:** 5 | **Effort:** M | **Dependencies:** V-050 | **Risk EDL:** None if read-only capture

### V-025 — Bad-attitude reentry compare missing
- **Evidence:** `ReentryPlasmaController.cs:165-167` bad attitude redder/hotter; `PLAN_VISUAL_REALISM.md:78,191`.
- **Realism gap:** Player should *see* danger before breakup; only belly-flop looks "safe."
- **Proposed solution:** Harness forced high AoA entry; capture before `ReentryBreakupController` fires.
- **Acceptance test:** Bad-attitude PNG: nose/flap localized glow >> belly; charring asymmetric (V-007).
- **Impact:** 4 | **Effort:** M | **Dependencies:** V-024, V-007 | **Risk EDL:** Low if telemetry-only attitude override in harness

### V-026 — Localized edge glows ship-only
- **Evidence:** `ReentryPlasmaController.cs:260-266` hides edge glows when `hasSH`.
- **Realism gap:** Full-stack reentry N/A for Starship ops; acceptable. Document as intentional.
- **Proposed solution:** No change unless full-stack reentry becomes a scenario.
- **Acceptance test:** N/A (document only).
- **Impact:** 1 | **Effort:** S | **Dependencies:** None | **Risk:** None

### V-027 — `reentry_glow.gdshader` unused
- **Evidence:** Shader exists `assets/shaders/reentry_glow.gdshader`; controller uses `StandardMaterial3D` meshes `ReentryPlasmaController.cs:44-83`.
- **Realism gap:** Missing turbulence/noise halo reads smoother than real ionized shock.
- **Proposed solution:** Optional: apply shader to shock cap mesh with `heat_level=intensity` — scoped single-mesh swap.
- **Acceptance test:** Side-by-side shock cap: visible turbulence at peak, not smooth orange ball.
- **Impact:** 3 | **Effort:** M | **Dependencies:** V-024 | **Risk EDL:** None

### V-028 — Reentry phase lighting unverified
- **Evidence:** `PhaseLightingController.cs:39-83,98-127` reentry overlay + cockpit boost; `PLAN_VISUAL_REALISM.md:226-227` pending.
- **Realism gap:** Reentry should feel dark except windward fire; without verify may still look daytime sky.
- **Proposed solution:** Before/after EDL capture; pad/orbit baselines unchanged.
- **Acceptance test:** Peak heating: ambient warm/dim; steel visible; HUD legible.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-024 | **Risk EDL:** None

### V-029 — Breakup VFX without per-piece structural breakup
- **Evidence:** `ReentryPlasmaController.cs:92` hosts `ReentryBreakupController`; `ROADMAP.md:90-91` sim gap.
- **Realism gap:** Destruction reads VFX-only, not structural — limits failure realism.
- **Proposed solution:** **Out of visual tranche** — document boundary; keep thermal breakup VFX.
- **Acceptance test:** N/A this audit.
- **Impact:** 3 (gameplay) | **Effort:** L | **Dependencies:** Physics | **Risk:** High if sim touched

### V-030 — Cockpit view at peak heating untested
- **Evidence:** `CameraController.cs:192+` cockpit; `PhaseLightingController.cs:79-83` cockpit reentry boost.
- **Realism gap:** FPV reentry is peak mission drama; untested combo may blind player.
- **Proposed solution:** `[C]` during EDL milestone capture; tune `CockpitGlowReduction` only.
- **Acceptance test:** Cockpit PNG at peak: forward window shows glow, instruments readable.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-024 | **Risk EDL:** None

---

## Orbit / space look

### V-031 — Terminator consistency Earth backdrop vs local ground
- **Evidence:** `PlanetMaterials.cs:30-45` earth shader `sun_dir`; `FloatingOrigin.cs:19` backdrop at 50k u; `EarthGroundController` separate patch.
- **Realism gap:** Terminator jump at 14–26 km fade breaks "one Earth" illusion.
- **Proposed solution:** Capture ascent through fade band; align `DefaultSunDir` with `SunController` feed if mismatch found.
- **Acceptance test:** 20 km altitude: no double-terminator seam in direction of travel.
- **Impact:** 3 | **Effort:** M | **Dependencies:** V-050 | **Risk:** None

### V-032 — Star texture runtime load fragility
- **Evidence:** `SkyController.cs:151-161` loads `starmap_milkyway_8k.jpg` at runtime; fallback 1×1 black.
- **Realism gap:** Missing file → black space silently; breaks orbital beauty row.
- **Proposed solution:** CI heuristic: orbit PNG mean brightness > threshold; document path in audit matrix.
- **Acceptance test:** Orbit capture shows stars + Milky Way structure, not pure black.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-051 | **Risk:** None

### V-033 — Orbital metallic contrast (closed — regression guard)
- **Evidence:** `PhaseLightingController.cs:30-37,63-67`; validated per `PLAN_VISUAL_REALISM.md:221-225`.
- **Realism gap:** Regression would subexpose steel in orbit (prior ACES failure).
- **Proposed solution:** Add orbit PNG to harness baseline; fail if vessel bbox too dark.
- **Acceptance test:** Orbit PNG: hull not crushed black; Earth limb visible.
- **Impact:** 4 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-034 — Map/orbit beauty row open
- **Evidence:** `PLAN_VISUAL_REALISM.md:80` matrix row unverified.
- **Realism gap:** Map view sells interplanetary sim; unverified = unknown quality.
- **Proposed solution:** `[Tab]` capture at 200 km with UI visible.
- **Acceptance test:** Map PNG: vessel marker, Earth terminator, UI readable.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

---

## Camera / framing

### V-035 — EDL flip / touchdown framing
- **Evidence:** `PLAN_VISUAL_REALISM.md:79`; `CameraController.cs:110-162` no EDL-specific mode; `EDLController.cs` phases exist.
- **Realism gap:** Flip-and-burn and touchdown are webcast hero shots; chase cam may clip ship.
- **Proposed solution:** Optional `CameraMode.Edl` with lookAt ship centroid + min distance during `FINAL_DESCENT`.
- **Acceptance test:** Touchdown PNG: full Ship + plume-ground interaction in frame.
- **Impact:** 3 | **Effort:** M | **Dependencies:** V-024 | **Risk EDL:** Low — camera only

### V-036 — Pad presets limited (2 angles)
- **Evidence:** `CameraController.cs:36-41` `PadPresets` array length 2.
- **Realism gap:** Reference compares need consistent lateral + tower-side views.
- **Proposed solution:** Add third preset: tower-side liftoff (yaw toward Mechazilla).
- **Acceptance test:** Cycle pad presets includes tower-side liftoff framing.
- **Impact:** 2 | **Effort:** S | **Dependencies:** None | **Risk:** None

### V-037 — Chase/pad lookAtY discontinuity
- **Evidence:** `CameraController.cs:118-121,156-161` Pad lookAtY=22 vs Chase lookAtY=0; switch at 700 m.
- **Realism gap:** Visible camera jump when modes flip early ascent.
- **Proposed solution:** Smooth lookAtY lerp 700–1200 m altitude.
- **Acceptance test:** Ascent recording: no snap at mode boundary.
- **Impact:** 2 | **Effort:** S | **Dependencies:** None | **Risk [G]:** None

### V-038 — Cockpit during liftoff plume
- **Evidence:** `CameraController.cs:192+`; plume omni energy high at pad.
- **Realism gap:** FPV liftoff is iconic; bloom may wash interior.
- **Proposed solution:** Capture `[C]` at liftoff; tune cockpit interior exposure if needed.
- **Acceptance test:** Cockpit liftoff: exterior plume visible, HUD readable.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

---

## Lighting / exposure

### V-039 — Dual ambient writers (Sky vs Phase)
- **Evidence:** `SkyController.cs:130-134` sets ambient; `PhaseLightingController.cs:85` sets ambient; both `_Process` every frame.
- **Realism gap:** Nondeterministic last-writer-wins → orbit/pad lighting may drift by node order.
- **Proposed solution:** Single owner: PhaseLighting sets energy; SkyController sets color only — or explicit process priority.
- **Acceptance test:** Orbit + pad PNGs identical across 10 restarts (pixel diff <1%).
- **Impact:** 3 | **Effort:** S | **Dependencies:** None | **Risk:** Medium — test pad/orbit baselines

### V-040 — No global tonemap experiments (guardrail)
- **Evidence:** `PLAN_VISUAL_REALISM.md:214-218`; `PLAN_PLAYTEST.md` B1 ACES revert.
- **Realism gap:** Global ACES darkens orbit — breaks mission read.
- **Proposed solution:** Document only; enforce phase-based tuning.
- **Acceptance test:** N/A — policy item.
- **Impact:** 5 (if violated) | **Effort:** S | **Dependencies:** None | **Risk:** High if ignored

### V-041 — Cockpit ambient boost during reentry only
- **Evidence:** `PhaseLightingController.cs:46-47,79-83`.
- **Realism gap:** Without boost, FPV reentry unreadable; with too much, loses fireball drama.
- **Proposed solution:** Tune with V-030 captures.
- **Acceptance test:** See V-030.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-024 | **Risk EDL:** None

### V-042 — Env glow at pad stays zero
- **Evidence:** `PhaseLightingController.cs:67` glow lerps from 0 below 70 km.
- **Realism gap:** Plume brilliance is shader-local; acceptable. Optional subtle glow at Max-Q only if captures flat.
- **Proposed solution:** Defer unless Max-Q looks dull after V-014.
- **Acceptance test:** Max-Q plume still HDR-bright without env glow.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-014 | **Risk:** None

---

## Planet / atmosphere backdrop

### V-043 — Ground patch → backdrop handoff seam
- **Evidence:** `EarthGroundController.cs:39-41` fade 14–26 km; `SkyController` TRANS 10–80 km.
- **Realism gap:** Visible crossfade seam breaks altitude continuity during ascent.
- **Proposed solution:** Align fade bands (single doc constant); capture 15–25 km sweep.
- **Acceptance test:** No horizontal color seam at horizon in ascent multiframe.
- **Impact:** 3 | **Effort:** M | **Dependencies:** V-050 | **Risk:** None

### V-044 — Atmospheric rim strength vs scaled planet
- **Evidence:** `PlanetMaterials.cs:40-41` `atmosphere_strength=0.8`; scaled-space at 50k u per `FloatingOrigin.cs:19`.
- **Realism gap:** Limb glow may look oversized/small vs local sky — breaks scale illusion in orbit.
- **Proposed solution:** Orbit capture; tune `atmosphere_strength` ±0.1 only.
- **Acceptance test:** Orbit: blue limb visible, not cartoon halo.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-045 — Mars visual path rarely validated
- **Evidence:** `SkyController.cs:36-41,104-110` Mars palette; mission default Earth.
- **Realism gap:** Multi-body sim advertises Mars; untested look erodes trust if used.
- **Proposed solution:** One `JumpToBody("mars")` harness capture.
- **Acceptance test:** Mars surface PNG: butterscotch sky + rust ground.
- **Impact:** 1 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

---

## UI vs render clash

### V-046 — "Glass" HUD is fake (no backdrop blur)
- **Evidence:** `scripts/UI/InterfaceTheme.cs:6-8` explicit comment.
- **Realism gap:** SpaceX-style UI expects crisp telemetry over fiery scene; flat charcoal panels can feel pasted-on.
- **Proposed solution:** Stronger text outline + slightly lower panel opacity in flight only (not menu).
- **Acceptance test:** Liftoff + reentry: all primary telemetry passes WCAG-like contrast vs bright background.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-014, V-023 | **Risk:** None

### V-047 — HUD center stack opacity vs plume
- **Evidence:** `HUDController.cs:176` center panel `GlassPanel(0.62f)`.
- **Realism gap:** Semi-transparent dark box over white deluge may look muddy, not glass.
- **Proposed solution:** Flight-phase opacity table: pad 0.72, ascent 0.85, space 0.62.
- **Acceptance test:** Liftoff: phase label readable on deluge without solid box feel.
- **Impact:** 3 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-048 — Navball contrast in orbit
- **Evidence:** `AttitudeNavball.cs:20-30` white horizon on `GlassStrong` 0.92 alpha.
- **Realism gap:** White-on-black space navball good; over bright Earth limb edge may wash.
- **Proposed solution:** Orbit capture; add 1px dark outline on horizon ring if needed.
- **Acceptance test:** Orbit PNG: navball horizon line visible against Earth.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-050 | **Risk:** None

### V-049 — Countdown overlay vs pad composition
- **Evidence:** `HUDController.cs:296-298` 48pt countdown center.
- **Realism gap:** Large countdown obscures hold-down drama in startup ramp.
- **Proposed solution:** Fade countdown alpha after T-10 unless user wants full SpaceX overlay.
- **Acceptance test:** T-5 capture: startup VFX visible under countdown.
- **Impact:** 2 | **Effort:** S | **Dependencies:** V-011 | **Risk:** None

---

## Infrastructure / process

### V-050 — End-to-end play harness (blocker)
- **Evidence:** `PLAN_NEXT_SESSION.md:54-61`; `PLAN_PLAYTEST.md:16-34`.
- **Realism gap:** Cannot *see* full mission arc — inference from logs ≠ webcast mission feel.
- **Proposed solution:** Temp `_PlaytestShot.cs` state-gated PNGs + `/tmp/exo_play.log`; mandatory cleanup.
- **Acceptance test:** PNG set: pad, liftoff, Max-Q, SEPARATION, ORBIT, EDL belly, touchdown; clean `git status`.
- **Impact:** 5 | **Effort:** M | **Dependencies:** None | **Risk:** None

### V-051 — CI visual artifacts absent
- **Evidence:** `PLAN_VISUAL_REALISM.md:37-38,247-257`; `ROADMAP.md:109-114`.
- **Realism gap:** Render regressions ship silently.
- **Proposed solution:** CI xvfb job + non-black heuristic + artifact upload; harness ephemeral.
- **Acceptance test:** CI artifact downloadable; intentional black frame fails job.
- **Impact:** 4 | **Effort:** L | **Dependencies:** V-050 pattern | **Risk:** None

### V-052 — Tracked harness in working tree (guard violation)
- **Evidence:** `project.godot:38` `_ReentryShot="*res://scripts/_ReentryShot.cs"`; git status `M project.godot`.
- **Realism gap:** Process risk — accidental commit breaks CI guard / pollutes repo.
- **Proposed solution:** Delete harness + restore `project.godot` before any merge; never commit `_*Shot.cs`.
- **Acceptance test:** `git status` clean; CI harness guard passes.
- **Impact:** 4 (process) | **Effort:** S | **Dependencies:** None | **Risk:** CI failure

---

## Reference compare matrix — IFT / Flight 7 gaps not yet closed

| Phase | Reference | Current capture | Gap (observable) | Owner | Status |
|-------|-----------|-----------------|------------------|-------|--------|
| Pad lateral | Starbase stack side view | `/tmp/exosphere_pad_*.png` (local) | Fine flap/nose/tile proportions vs 9 m×121 m | `VesselRenderer`, `LaunchPadController`, `CameraController` | **Partial** — first pass done, fine compare open (V-001, V-002, V-017) |
| Liftoff | IFT daylight, 33 Raptors + deluge | Local liftoff PNGs | Deluge may occlude stack; OLM hole scale | `PlumeSystem`, `LaunchEffectsController`, `LaunchPadController` | **Partial** (V-009, V-010) |
| Startup/ramp | IFT T-3s chill→release | `/tmp/exosphere_startup_*.png` | Timing/intensity vs real progressive ignition | `EngineStartupController`, `PlumeSystem` | **Implemented, not reference-closed** (V-011) |
| Hot-staging | IFT T+2:39/T+2:40 | `/tmp/exosphere_hotstage_ascent_*.png` (jul 2026) | Side-by-side intensity/soot ring/encuadre | `HotStageFlashController`, `VesselRenderer` | **Verified ascent, IFT compare open** (V-012) |
| Orbit burn | Vacuum Raptor refs | `/tmp/exosphere_orbit_plume_clean.png` | Reference compare at 120–152 km | `PlumeSystem`, `raptor_plume.gdshader` | **Tuned, reference open** (V-013) |
| Reentry nominal | Starship belly-flop webcasts | Synthetic `/tmp/exosphere_reentry_edges.png` only | No real EDL multiframe; alpha/timing | `ReentryPlasmaController`, `PhaseLightingController` | **Open** (V-024, V-028) |
| Reentry failure | Flight 4–6 tile/flap damage | None in repo | Bad attitude not captured | `ReentryPlasmaController`, `VesselRenderer` | **Open** (V-025, V-007) |
| Touchdown/flip | Flip-and-burn footage | None in repo | Camera framing not validated | `EDLController`, `CameraController`, `PlumeSystem` | **Open** (V-035) |
| Orbit/map beauty | Earth terminator + steel glint | Not in repo | Map tab + orbit beauty unverified | `PlanetMaterials`, `SkyController`, HUD | **Open** (V-034, V-033) |

**Closed since IFT baseline (do not reopen without regression proof):** SL plume width/energy (`PlumeSystem`), vacuum smoke attenuation, hot-staging VFX existence, altitude space lighting V1 (`PhaseLightingController`), procedural stack V1 exterior, coastal pad V1.

---

## Recommended implementation order

1. **V-052** — clean harness from tree  
2. **V-050** — play harness (unblocks all compares)  
3. **V-017** — OLM hole scale (quick win)  
4. **V-009 / V-010** — deluge silhouette  
5. **V-012 / V-011** — IFT compare tunes  
6. **V-024 / V-028 / V-023 / V-007** — EDL reentry package  
7. **V-039** — ambient ownership fix  
8. **V-051** — CI artifacts  
