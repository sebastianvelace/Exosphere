# Starship gameplay diagnostics

## Findings

### Engine HUD and plume

The screenshot combined three independent signals:

- `THR 100%` is the commanded throttle, not delivered chamber thrust.
- `ENG 0/33` by itself can be a normal chill/spin-prime/ignition state, but the screenshot's
  red dots are not an "off" style: red is reserved for an actual `FailureCode`. Therefore that
  particular frame represents 33 failed engine instances, not 33 engines waiting to start.
- A startup lifecycle state is amber and the current-stage telemetry reads the active engine
  part. The plume is gated by `ActiveEngineCount`, so throttle alone cannot create a full thrust
  plume after an engine-out.

The engine model remains deliberately stateful: command throttle can be non-zero while the
engine is chilling, priming, igniting, ramping or failed. The HUD now exposes that distinction.
When an engine is genuinely failed, the vehicle strip also reports the first failure family
(`STARVATION`, `FEED LIMIT`, `OVERHEAT` or `RESTART LIMIT`) instead of leaving `ENG 0/N`
without a cause. The exact per-engine code remains available in the simulation telemetry.

### Map jump and interplanetary transfer

`J`/map jumps could leave a vessel with stale `IsOnRails`, conic elements, angular velocity,
catch/contact state and reference-body data. On the next physics tick those stale states could
fight the new position and make the ship tumble or become effectively uncontrollable.
`Vessel.PrepareForTeleport()` now clears the stale dynamic state; the bridge then assigns the
new body, tangent orientation, zero throttle and SAS state explicitly.
The reset also clears delayed ground-link commands and cuts transient chamber pressure/gimbal
state. This closes the two paths that could otherwise re-apply old attitude commands or create
a residual torque on the first destination tick.

### Mechazilla / chopsticks

The catch solver already required two declared pin contacts, a 5 m physical capture radius,
relative speed below 0.5 m/s and 0.5 s of settling. The legacy `starship_command.json` profile
did not declare pins, so its upper section could never reach that solver. It now declares the
same calibrated pin geometry used by the V3 configuration. This is a deterministic simulator
geometry allocation, not measured vehicle hardware data.

The Starship reentry demonstration now:

1. stages the booster before handing the ship to EDL;
2. arms the catch only when the vessel has pins;
3. seeds over the launch-site meridian and tracks the rotating cradle;
4. keeps the aero/retro attitude stable only for the scripted catch demonstration;
5. uses the real drag, engine spool, translational forces, pin contact and settle solver;
6. uses 3→2→1 engine step-down authority so the ship can arrest the entry speed without
   hovering permanently above the arms; and
7. aims the descent datum at the actual pin height rather than the vessel datum.

The scripted attitude stabilization is explicitly gated by `IsTowerCatchDemonstration`. Manual
flight and ordinary unscripted reentries continue to use the physical attitude controller. A
normal Earth return from a Starbase launch site now arms the same physical catch approach for a
catch-capable Starship; the two-pin solver still decides whether it actually settles, and the
existing low-altitude abort diverts to legs when the corridor is missed.
The catch radius was not widened and `IsCaught` is never set by the presentation path.

## Validation matrix

| Check | Result |
|---|---|
| Targeted engine, catch, teleport and runtime tests | 33/33 passed |
| Godot C# build | 0 warnings, 0 errors |
| Gameplay regression contract | PASS |
| Visual harness contract | 1 valid + 11 invalid fixtures passed |
| EDL visual stages | ENTRY, peak heating, flip, retro and caught captures recorded |
| EDL final acceptance | PASS: `CHECK tower_catch caught=True pins=2 relativeSpeed=0.030 angularSpeed=0.0000`; `SUMMARY reason=CAUGHT` |

The visual harness runs under llvmpipe in CI and is intentionally slower than real time. Its
console warnings about X11 input and VSync are environment warnings, not simulation failures.

## Known limits

- Pin dimensions are a calibrated gameplay model because public Starship catch-pin dimensions
  are not present in the repository data.
- The deterministic catch demonstration validates the contact path and does not claim that a
  fully manual return-to-launch-site guidance law is complete.
- The cradle target is sampled once per frame but propagated to each physics substep using its
  inertial velocity; this prevents time-warp frames from comparing against a stale launch-site
  position.
- Mars/Venus and non-Starbase Starship reentries do not arm the Starbase catch policy; they keep
  the ordinary EDL/leg path.
- The launch complex remains visible throughout an Earth Starship EDL/catch attempt and is
  anchored to the vessel actually returning when a booster is the catch candidate, rather than
  always to `ActiveVessel`.
