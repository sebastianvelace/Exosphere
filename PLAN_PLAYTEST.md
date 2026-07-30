# Exosphere — End-to-End Playtest Harness & Future-Work Backlog

This doc gives any loop iteration two things:

1. A **repeatable way to "play" a full mission headless and SEE it** (a temporary,
   untracked autoload harness driven through the real `SimulationBridge` API).
2. A **prioritized, evidence-backed backlog** so a loop picks high-impact work
   without re-deriving context.

It complements `PLAN_REALISM.md` (physics audit) and `PLAN_VISUAL_REALISM.md`
(visual track). It does not replace them — it is the cross-cutting "how to drive
and observe the whole game" layer plus a living TODO seeded with real findings.

---

## 1. End-to-end play harness (headless, untracked)

### Environment gotchas (verified this session)

- **Main scene is now `res://scenes/ui/MainMenu.tscn`.** To exercise flight you
  MUST launch the flight scene explicitly:
  ```bash
  GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
  xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" \
    --path . --rendering-driver opengl3 res://scenes/flight/Flight.tscn
  ```
- **Headless runs UNCAPPED (no vsync).** The sim advances far faster than
  wall-clock, so a fixed-frame capture (`_frames == 480`) will overshoot — the
  stack was already past Max-Q by the time a naive frame counter fired.
  **Gate every capture on physical state** (altitude, mission phase, speed),
  never on frame counts.
- `--headless` uses the dummy renderer → no real PNG. Always capture under a real
  framebuffer via `xvfb-run` (see `visual-testing` skill). Autoloads still load
  when a scene is launched explicitly.

### Driving API (all on `SimulationBridge.Instance`)

| Call | Effect |
| --- | --- |
| `Ignite()` | Spool up throttle; releases hold-down at TWR > 1.02 (real liftoff ramp) |
| `SetThrottle(double t)` | Set active-vessel throttle 0..1 |
| `ReleaseGroundHold()` | Force-release the pad clamp |
| `SetSAS(bool)` | Toggle SAS |
| `JumpToOrbit(double alt = 200_000)` | Teleport to a circular orbit (deterministic framing) |
| `JumpToBody(string id, double alt = 300_000)` | Teleport to another body's orbit |
| `SetTimeScale(double)` | Warp |
| `Universe.GetDominantBody(pos)` | SOI body for a position |
| `ActiveVessel.GetAltitude(body)` | Altitude above a body (m) |
| `ActiveVessel.Throttle` | Read/write throttle |

- The **[G] ascent autopilot** is `AscentController.Engage()` (child node created
  by the bridge). Use it to fly a realistic gravity turn instead of going straight
  up. Straight-up (`Ignite()` only) is fine for a quick plume shot.
- Mission phases print `[Mission] → PHASE` (LIFTOFF, ASCENT_SH, MAX_Q, SEPARATION,
  ORBIT, ENTRY, …). Watch these to gate milestone captures.

### Milestone walk (one temporary `scripts/_PlaytestShot.cs` autoload)

Step through the whole arc, saving a PNG + a telemetry line at each milestone,
each gated on physical state (pseudocode):

1. **Pad pre-launch** — capture immediately (alt ≈ 12 m).
2. **Liftoff plume** — `Ignite()`, capture when `alt ∈ [80, 350] m`.
3. **Max-Q** — capture on `[Mission] → MAX_Q` (or q peak, ~8–12 km).
4. **Staging / hot-stage** — capture on `SEPARATION` (booster + Ship split).
5. **Orbit insertion** — capture on `SECO / ORBIT` (apoapsis ≈ periapsis).
6. **Orbit beauty** — `JumpToOrbit(250_000)` for a deterministic Earth+ship frame.
7. **Deorbit + reentry plasma** — from ORBIT open map `[M]`, press `[B]` for the
   deorbit preset (Pe≈80 km), `⏎` to arm/execute; then capture while `heatRatio`
   climbs (watch `ThermalModel` heat / `maxT`). Prefer this over teleport demo.
8. **EDL** — belly-flop → flip-and-burn → touchdown (target ≤ 2 m/s).

Record per milestone: `alt, spd, vSpeed, q, g, phase, heatRatio, maxT` → dump to
`/tmp/exo_play_*.png` + a `/tmp/exo_play.log`.

**Cleanup is mandatory** (untracked harness): delete `scripts/_PlaytestShot.cs`
(+`.uid`), `git checkout project.godot`, confirm `git status` is clean. See the
`visual-testing` skill for the autoload-registration + teardown pattern.

### Implemented runner — `tools/visual_playtest.sh` (VAL-01, Jul 2026)

Reusable local tool that generates the temporary autoload, registers it, runs Godot
under `xvfb-run`, captures state-gated PNGs, writes `/tmp/exo_play.log`, and
**always** removes the harness + restores `project.godot` on exit (trap).

```bash
# Full Flight 7 acceptance (pad → natural orbit → entry → landing; CPU rendering is slow)
bash tools/visual_playtest.sh --flight7 --run-id agent-vp1

# CI / quick pipeline check (pad capture only, ~60 s)
bash tools/visual_playtest.sh --smoke --run-id agent-smoke

# Focused physics diagnosis (pad → natural stable orbit; no EDL or teleport)
bash tools/visual_playtest.sh --ascent --flight7 --run-id agent-ascent

# Re-run gates against preserved artifacts without launching Godot
bash tools/visual_playtest.sh --flight7 --run-id agent-vp1 --verify-only

# Useful options: --run-id ID  --max-runtime SEC  --out-dir DIR  --log FILE  --skip-build
```

**Outputs**

| File | Content |
| --- | --- |
| `/tmp/exo_play/exo_play_<milestone>.png` | Viewport PNG per milestone |
| `/tmp/exo_play.log` | Structured `CAPTURE` / `TRACE_ASCENT` / transition / invariant evidence |
| `/tmp/exo_play.log.console` | Separate Godot stdout/stderr; never shares a writer with telemetry |
| `/tmp/exo_play/run-summary.txt` | Compact PASS/FAIL, milestones and terminal diagnostics |

The focused ascent contract requires Coast and Insert transitions, at least five diagnostic
samples, finite state, intact vehicle/control, continuous insertion thrust, measurable physics
progress, no fallback, and an orbit capture whose periapsis clears the modeled atmosphere.
`tools/tests/visual_playtest_contract_test.sh` exercises one valid and eleven invalid synthetic
logs (false orbit, missing insertion, non-finite/destroyed/stalled state, fallback, writer
corruption and duplicate run boundaries). `tools/ci_check.sh` runs this contract test.

Harness ownership is exclusive: `flock` prevents two scripts from mutating the temporary
autoload simultaneously, a per-process environment token makes unrelated Godot instances
ignore it, and only the lock owner may restore/delete generated resources.

**Milestone status (verified Jul 2026 on `integrate/jul2026-realism-loop`)**

| Milestone | Slug | Status |
| --- | --- | --- |
| Pad pre-launch | `pad` | ✅ state-gated (alt ≈ 12 m) |
| Liftoff plume | `liftoff` | ✅ alt 80–350 m + LIFTOFF/ASCENT_SH |
| Max-Q | `maxq` | ✅ `MissionPhase.MAX_Q` |
| Hot-stage overlap | `hotstage` | ✅ `Vessel.IsHotStageOverlapping` while both stages remain attached |
| Mechanical separation | `separation` | ✅ SEPARATION / ASCENT_SHIP after overlap |
| Orbit insertion | `orbit` | ✅ natural [G] autopilot; full gate rejects fallback |
| Orbit beauty | `orbit_beauty` | ✅ `JumpToOrbit(250 km)` |
| Entry interface | `entry` | ✅ after retro burn (peri ≈ 80 km) |
| Peak heating | `peak_heating` | ✅ real `MissionPhase.PEAK_HEATING`, RK4 dense entry |
| Retro burn | `retro_burn` | ✅ low-altitude physical flip ignition, ×1 powered descent |
| Touchdown | `touchdown` | ✅ `SUMMARY reason=LANDED`; six-foot contact in `--edl`, ≤3 m/s core impact path for historical gearless Flight 7/12 |

**Resolved tail and remaining visual findings (2026-07-30)**

- **Deorbit → EDL is closed:** full Flight 7 generated all 11 captures and LANDED.
  Dense entry remains RK4 at ×3, powered descent returns to ×1, and full mode has a
  3600 s wall budget. A timeout now prints last-state diagnostics rather than a bare failure.
- **Engine lifecycle regression is closed:** the simulated mission needs four center-Raptor
  starts (hot-stage, insertion after coast, deorbit, landing). Flight 7 SL restart envelope
  and a data regression test cover it; landing selection is monotonic 3→2→1.
- **Historical landing contract is explicit:** Flight 7/12 intentionally contain no fictional
  landing gear. Full mode verifies the core physical soft-impact path (≤3 m/s and settled);
  deterministic `--edl` continues to require at least three persistent foot contacts.
- **V-024 is unblocked:** `peak_heating`, `retro_burn` and touchdown frames now exist.
  Reference comparison/tuning remains V-P4/V-P5 work, not part of V-P1.

**CI:** `build-test` job runs `--smoke` under Xvfb and uploads `exo_play_pad.png`
as an artifact. Full PNG matrix remains a **local acceptance** step until CC-01
(non-black heuristic + full artifact matrix) lands.

---

## 2. Future-work backlog (evidence-backed, prioritized)

### B1. Phase-based lighting controller — V1 DONE (altitude blend); reentry/cockpit pending

**Evidence (this session, before/after xvfb captures, reverted — NOT shipped):**
- A global `tonemap_mode = ACES` + `tonemap_white = 2.0` **darkened the ship in
  orbit** → subexposed steel. `PLAN_VISUAL_REALISM.md` explicitly forbids "la nave
  subexpuesta contra espacio", so it was rejected.
- **Glow-only** (keep Filmic + HDR bloom) showed **no visible win** on the pad,
  orbit, or liftoff@84 m frames — those scenes have no blown-out HDR hotspots, so
  bloom has nothing to act on. Glow only pays off on bright emissive (ascent
  plume, reentry plasma) and needs a good frame to verify.
- **Root cause:** lighting is currently *global*, but it must be **per-phase**.
  The sky-sourced bluish ambient (`Color(0.55,0.70,1.0)` @ `energy 0.45`) is
  correct on the daylit pad but **wrong in orbit** — space has no blue fill, so
  the ship reads flat/matte instead of high-contrast metallic.

**Task:** a `PhaseLightingController` (game layer) that drives the `WorldEnvironment`
+ `DirectionalLight3D` per mission phase (pad / ascent / space / reentry / cockpit):
ambient source/energy/color, tonemap curve, exposure, and glow. In space: kill the
blue ambient, raise contrast, add HDR glow so the sun and steel specular pop. On
the pad: keep the current daylight look. **Verify each phase with the play harness
above** — this is exactly what makes it safe to change global lighting without
regressing the pad or washing the UI (UI is on a separate `CanvasLayer`, unaffected
by env glow).

**V1 DONE** (`scripts/PhaseLightingController.cs`, wired in `SimulationBridge`): blends
by ALTITUDE (smoothstep 70→130 km) — ambient energy 0.45→0.12, sun energy 1.5→1.95,
HDR glow 0→0.6, Filmic kept. `SunController` still owns light orientation (never
touches energy) so there is no conflict. Xvfb-verified: pad identical to baseline,
orbit gains metallic contrast without subexposing the ship or washing Earth.

**Reentry phase — DESIGNED, blocked on a harness capability (attempted, reverted).**
The design: add a `reentryFactor ∈ [0,1]` from the SAME convective flux the plasma
uses — `ThermalModel.ComputeHeatFlux(body.GetAtmosphericDensity(pos), surfaceSpeed)`,
thresholds `5e4`/`6e5` W/m² (mirror `ReentryPlasmaController`). When it fires, lerp the
overlay by `reentryFactor`: ambient → ~0.10, sun energy → ~0.9, glow → ~0.8, so the
emissive fireball dominates without washing the cockpit/HUD. It only activates on heat,
so it CANNOT regress the pad/orbit look.
**Why it was reverted (honesty):** it could not be visually verified headless. Forcing
reentry via `JumpToOrbit(85_000)` gives an AXIAL attitude (tiny windward plasma cap)
and at moderate flux the light shift is imperceptible against the bright Earth; pushing
to saturated flux burns the ship up first. The dramatic case — a belly-flop EDL with a
large windward cap — is exactly what the overlay is for, but the harness can't produce
it yet.
**Unblock first, then ship:** teach the play harness a real DEORBIT → EDL path (milestone
7): set a retrograde attitude + throttle burn to drop periapsis into the atmosphere so
`EDLController` auto-engages belly-flop (it fires on `vUp < -20 && inAtmo && speed >
EntrySpeed`), giving a big windward plasma to capture and tune the overlay against. With
that frame, re-add the reentry overlay and verify before/after.
**Also next:** tune the ascent mid-blend against a real Max-Q capture; optional cooler
color grade in space.

### B2. Liftoff plume visibility — MED

At `alt ≈ 84 m` the exhaust plume is barely visible once the stack clears the
tower (camera angle + plume column height). Consider a taller/brighter sea-level
plume column, or a liftoff-tracking camera that frames the engine exhaust. Belongs
to V2 in `PLAN_VISUAL_REALISM.md` (plumes), not lighting.

### B3. Full-flight visual/HUD findings — NEW

- **Hot-stage framing is too distant.** Evidence:
  `/tmp/exo_play-vp1-acceptance/exo_play_hotstage.png` shows the state-correct overlap,
  but the vehicle occupies too few pixels for IFT plume/ring comparison. Acceptance:
  a state-gated close chase/telephoto capture shows attached booster + Ship, interstage
  plume and soot ring without clipping the stack or HUD.
- **Expected vacuum-engine retirement is reported as a critical failure.** Evidence:
  peak-heating/retro/touchdown frames display `CRITICAL ENGINE OUT 3 FAILED / 6
  INSTALLED / LIMIT 0 FAILED`; telemetry proves those are the three unselected vacuum
  Raptors after their mission-use restart envelope, while all three center Raptors reach
  `Running` with `starts=4`. Acceptance: phase-aware HUD distinguishes unavailable/
  intentionally retired engines from unexpected loss of required landing authority.
- **Gearless Flight 7 says “LEGS DOWN.”** Evidence:
  `exo_play_touchdown.png` plus `StarshipFlight7DataTests.HistoricalVariantIsDatedAndDoesNotIncludeFictionalLandingGear`.
  Acceptance: EDL copy derives from actual `Landing` parts/deployment; historical gearless
  touchdown never claims legs, while the deterministic six-foot demo still does.

---

## 3. How a loop should use this doc

- **Before picking work**, skim this backlog + `PLAN_REALISM.md` +
  `PLAN_VISUAL_REALISM.md`. Prefer items that already have an acceptance test you
  can verify (a physics xUnit test, or an xvfb capture criterion).
- **Run the play harness** whenever a change could touch the mission arc (physics,
  staging, EDL, lighting, plume). SEE it end-to-end; do not assume.
- **Coordination:** physics work lives in `ExosphereSimulation/` + flight
  controllers; UI/start-menu work lives under `scenes/ui/` + `scripts/UI/` +
  `scripts/MainMenu.cs` and the HUD scripts. When multiple agents are active,
  prefer a focus that doesn't overlap the others, and always `git fetch` +
  `ci_check` + confirm 0-behind before pushing `main`.
