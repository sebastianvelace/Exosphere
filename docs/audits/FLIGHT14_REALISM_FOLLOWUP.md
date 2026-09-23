# Flight 14 realism follow-up

**Status:** active audit, not a claim of Flight 14 mission parity
**Updated:** 2026-09-22
**Scope:** Starship V3 visual fidelity, propulsion fault isolation, controlled ascent and flap actuation.

## Evidence boundary

The two user-provided screenshots are treated as visual evidence only. They are not
implementation instructions and do not establish a physical parameter. The current
Godot framebuffer captures were produced by `tools/visual_playtest.sh` at 1280×1024
with the compatibility renderer.

The official [Flight 14 mission page](https://www.spacex.com/launches/starship-flight-14)
was reachable during this audit, but its mission content was not text-extractable in
the current browser pass. Operational details such as launch date, orbit profile,
payload count and recovery sequence therefore remain provisional until the page or an
official release exposes them in a verifiable form.

The reliable public engineering boundary is the official [SpaceX Updates archive](https://new.spacex.com/updates):
V3 is described there with three larger/re-clocked grid fins, an integrated hot stage,
distributed avionics fault isolation and long-duration propulsion/RCS changes. Public
vehicle dimensions and engine counts are recorded on the official [Starship vehicle page](https://new.spacex.com/vehicles/starship).

## Findings from the current code and captures

| Area | Verified state | Gap | Priority |
| --- | --- | --- | --- |
| Launch plume | 33/33 telemetry and delivered thrust reach 1.0; the real framebuffer shows a visible core and pad steam | At liftoff the exhaust still reads as a narrow white column instead of a broad, layered methalox/steam volume | P0 |
| Upper ship | Continuous barrel, tangent-ogive nose, raceway, payload-door cue and four animated flaps exist | Shadow-side steel/TPS loses surface information at distance; seams and thermal zones need a controlled readability pass | P0 |
| V3 booster | Flight 12 data is selected by the harness; renderer now uses three larger, lower, re-clocked fins only for V3 part IDs | Integrated hot-stage geometry is not yet distinct from the legacy vented interstage in the full-stack renderer | P1 |
| Fault isolation | Engine state and delayed engine-out recovery exist; a pure peer-thrust classifier is now covered by tests | Classifier is not yet wired into onboard guidance and needs persistence/debounce plus residual/torque corroboration | P0 |
| Flaps | Aerodynamic authority is applied in the pure simulation; visual flaps respond to q, belly alignment, pitch and roll | No persistent actuator state/rate limit is shared by physics and renderer | P1 |

## Implemented in this stage

- Added `EngineFaultIsolation.Infer`, which estimates a peer-engine thrust-per-throttle
  baseline using a median and isolates weak engines without reading `FailureCode`.
- Added tests for one failed engine, a healthy cluster and the no-peer single-engine case.
- Added V3-specific grid-fin geometry while preserving the four-fin Flight 7 legacy path.
- Increased only the atmospheric Super Heavy plume's near-field optical envelope and
  core cross-section; vacuum behavior remains pressure-driven.
- Added a bounded cool-sky fill to the black TPS shader so shadow-side surfaces retain
  physical material cues without changing the direct-light response.

## Next work units

1. **Fault isolation runtime:** feed `EngineFaultIsolation` from the same live engine
   telemetry used by the HUD, add a 100–150 ms persistence window and expose a diagnosis
   confidence/engine list to recovery guidance. Add thrust-vector residuals before
   allowing automatic recovery to act.
2. **Controlled ascent:** run the V3 stack through a longer ascent gate with thrust,
   mass, dynamic pressure, attitude error, gimbal and flap-command telemetry. Keep
   Flight 7 legacy as the comparison baseline.
3. **Flap actuator parity:** introduce a physical deflection state, rate limit and
   saturation in the pure simulation; make the renderer consume that state rather than
   deriving a separate pose from pitch/roll.
4. **P0 plume comparison:** add a deterministic close camera preset and compare pad,
   100 m and 1 km captures against the same framing. Tune the layered cone, ground
   interaction and deluge separately; do not use a global exposure or arbitrary bloom
   increase as a substitute for geometry.
5. **V3 hot stage and upper ship:** model the integrated interface as a V3-specific
   continuous shell, then validate nose/TPS/raceway/flap readability in a close-up
   framebuffer before touching global lighting.

## Acceptance gates

- Physics: full xUnit suite, no warnings, deterministic diagnosis under throttle ramp,
  no false positive with healthy peer scatter, and legacy/coupled ascent parity retained.
- Visual: real PNG, telemetry bound to the same run, same camera and renderer for before/
  after comparison, visible delivered-thrust plume at 33/33, no orbital steam carry-over,
  and identifiable V3 fin count in the Flight 12 capture.
- Provenance: every Flight 14-specific parameter must cite an official source or be
  explicitly labelled as a simulator estimate.
