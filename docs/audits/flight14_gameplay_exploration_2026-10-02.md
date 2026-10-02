# Flight 14 gameplay exploration

The user explicitly requested a playable way to inspect the current progress. The
featured briefing therefore exposes **Start Exploration**, not completed mission
acceptance. The UI discloses an estimated V3 baseline, guidance and 26 payloads.
Ocean targeting, ship splashdown and independent booster recovery remain pending.

## Physical continuity and ownership

`Flight14LaunchDiagnostic.CreateWithPoweredReturn(data, universe)` builds the same
loaded stack/controller chain in an empty gameplay universe. It retains the game's
Earth, Sun and other bodies, ephemerides and epoch. Initial Starbase Pad 2 placement
is the only seeded state; ascent, insertion, deployments, deorbit, entry and flip
keep their original vessel and resources. The default isolated diagnostic factory
remains available for previous reproducibility checks.

`Flight14Exploration.AdvanceFrame` accumulates whole 20 ms steps. Acceleration requests
more full-physics steps; it never enlarges actuator integration or switches the carrier
to rails. Each rendered frame processes at most 50 steps (one simulation second),
with less than one step of retained remainder. An overloaded machine runs slower
than requested acceleration; the telemetry clock shows actual committed physics.
The external warp value is restored after each batch. Systems consume the batch's
committed interval once, rather than only the last substep or wall time.

The pure controller chain exclusively owns flight commands. Generic ascent, EDL,
historical and booster catch controllers are not installed in this mode. Manual
attitude/throttle, staging, SAS, debug jump/demo, quicksave/load and map burn execution
are inhibited. The legacy post-frame staging handler also leaves the pending
hot-stage completion flag entirely to the pure controller, including when the
overlap ends on the last step of a render batch. Camera and read-only orbital map remain usable. Detached booster and
satellite renderers are synchronized to real split vessels; carrier geometry is rebuilt
after split events. Controller singleton references are cleared when their scenes exit.

The preview stops processing physics immediately after the diagnostic 100 m witness
or a physical failure/control block. It never emits a landed/caught success for this
boundary. Pause, resume, fresh loaded-pad restart and menu return remain available.
The broadcast band uses the controller's actual liftoff epoch and observed orbit,
separation and release milestones. The final milestone is flip, not an invented landing.

## Verification

The five focused exploration tests passed. They check retained moving Earth/Sun
state and initial pad-relative velocity, 30/120 Hz render cadence and x10 warp
against identical whole-step ignition/ascent, bounded CPU demand without queued
latency, pause behavior, and the complete loaded moving-body sequence to the 100 m
boundary (all 26 releases, original vessel, no destruction and a frozen terminal state).

Real-framebuffer menu checks with the **Integrated GPU** preset passed for the
centred briefing, enabled route, launch, pause/resume/restart/menu and ignored
manual/staging/SAS/debug/save/load input. A continuous rendered ascent capture at
T+200.84 s shows the separated ship at 74.364 km and both physical vessels/renderers.
The final legacy-staging ownership guard was compiled and separately checked by
a new continuous gameplay run through separation: T+200.82 s, 74.354 km, two
physical vessels and matching renderers.
This checks the selected preset under Xvfb/OpenGL software rendering, not GPU
performance certification. The clicked-menu gameplay run also reached `TerminalReached` at T+11394.08 s,
99.743 m, 26 released payloads, 28 physical vessels and 28 synchronized renderers,
with no control block. Eight subsequent frames left the committed epoch unchanged.
Intermediate rendering was disabled for this long software-renderer run; the original
physical sequence and all scene processes continued, then rendering was re-enabled
for a real terminal capture. This is not a continuously rendered full-mission visual
comparison. Aggregate `tools/ci_check.sh` passed: 993 xUnit tests, all three
builds with zero warnings/errors, flight startup and graphics-menu checks. The
final game-only staging guard and briefing wording were subsequently compiled
with zero warnings/errors and checked through the fresh menu/separation captures;
the tested simulation library and physical controls were unchanged.

Evidence is preserved in `/tmp/flight14-preview-controls/`,
`/tmp/flight14-preview-final-separation/`, `/tmp/flight14-preview-final-briefing/`
and `/tmp/flight14-preview-terminal/`. The terminal capture began before the last
legacy-staging guard; that guard was separately checked in the final separation run.
It changes pending-flag ownership during hot staging, not the already separated
carrier's subsequent orbit/return controls or physical data.

## Remaining work

This preview does not certify Flight 14 reconstruction, proprietary aerodynamics,
actual vehicle hardware/attitude or weather. The terminal capture has a flat
olive/brown background with no readable ocean/sky horizon; this is an unresolved
near-surface presentation gap, not reference-matched visual acceptance. Its cause
has not been isolated. Satellite visual geometry still uses
the generic part renderer and needs its own reference-backed refinement. The full goal still requires geographic
return control, contact/ocean outcomes for both vehicles and reference-matched complete
mission captures on the target renderer/hardware.
