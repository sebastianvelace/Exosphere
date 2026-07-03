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
7. **Deorbit + reentry plasma** — drain propellant to reserve, retro-burn, capture
   while `heatRatio` climbs (watch `ThermalModel` heat / `maxT`).
8. **EDL** — belly-flop → flip-and-burn → touchdown (target ≤ 2 m/s).

Record per milestone: `alt, spd, vSpeed, q, g, phase, heatRatio, maxT` → dump to
`/tmp/exo_play_*.png` + a `/tmp/exo_play.log`.

**Cleanup is mandatory** (untracked harness): delete `scripts/_PlaytestShot.cs`
(+`.uid`), `git checkout project.godot`, confirm `git status` is clean. See the
`visual-testing` skill for the autoload-registration + teardown pattern.

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
**Next:** (a) a reentry phase — warm, dimmer exposure tied to `ThermalModel` heat so
plasma reads without washing the cockpit/HUD; (b) tune the ascent mid-blend against a
real Max-Q capture; (c) optional per-phase color grade (cooler in space).

### B2. Liftoff plume visibility — MED

At `alt ≈ 84 m` the exhaust plume is barely visible once the stack clears the
tower (camera angle + plume column height). Consider a taller/brighter sea-level
plume column, or a liftoff-tracking camera that frames the engine exhaust. Belongs
to V2 in `PLAN_VISUAL_REALISM.md` (plumes), not lighting.

### B3. (seed more here as future loops find them)

Keep this list append-only and evidence-backed: each item needs a concrete
observation (a capture, a telemetry number, a `file:line`) and an acceptance
criterion, so the next loop can act without guessing.

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
