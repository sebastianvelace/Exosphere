# Starship gameplay diagnostics

## Findings

### Engine HUD and plume

The screenshot combined three independent signals:

- `THR 100%` is the commanded throttle, not delivered chamber thrust.
- `ENG 0/33` was the current-stage telemetry during chill, spin-prime and ignition; the
  ascent harness also selected the pre-staging `ship_engines` part by definition instead of
  reading the active engine part.
- Red engine dots represented `FailureCode`, so showing red during normal startup was
  misleading. A startup lifecycle state is now amber; red is reserved for an actual engine
  failure. The plume is gated by `ActiveEngineCount`, so throttle alone cannot create a full
  thrust plume after an engine-out.

The engine model remains deliberately stateful: command throttle can be non-zero while the
engine is chilling, priming, igniting, ramping or failed. The HUD now exposes that distinction.

### Map jump and interplanetary transfer

`J`/map jumps could leave a vessel with stale `IsOnRails`, conic elements, angular velocity,
catch/contact state and reference-body data. On the next physics tick those stale states could
fight the new position and make the ship tumble or become effectively uncontrollable.
`Vessel.PrepareForTeleport()` now clears the stale dynamic state; the bridge then assigns the
new body, tangent orientation, zero throttle and SAS state explicitly.

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
flight and ordinary unscripted reentries continue to use the physical attitude controller.
The catch radius was not widened and `IsCaught` is never set by the presentation path.

## Validation matrix

| Check | Result |
|---|---|
| Targeted engine, catch, teleport and runtime tests | 18/18 passed |
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
- Mars/Venus and ordinary unassisted Starship reentries are not changed by the scripted catch
  stabilizer and require their own visual matrix in a later pass.
