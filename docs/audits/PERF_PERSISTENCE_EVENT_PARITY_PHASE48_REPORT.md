# Phase 48 — persistence and mission-event parity audit

Date: 2026-08-15  
Status: **AUDIT COMPLETE; deferred runtime promotion remains blocked**

## Findings

`SaveGameV2` is the authoritative persistence format. It already round-trips the state
needed by the current physics path: simulation epoch and time scale, vessel kinematics and
orientation, rails state, throttle/attitude, ground hold, destruction state, parts and
resource quantities, engine lifecycle state, crew, docking connections, mission/campaign
metadata, and persistent assets. Existing regression tests cover the stable-id, resource,
docking, legacy-migration, and invalid-save paths.

The format also exposes `SaveGameV2.Systems`, a case-preserving JSON extension dictionary.
`SaveV2SystemsExtensionRoundTripsWithoutSchemaMigration` proves that an unknown future
systems snapshot survives serialize/deserialize without changing schema version or being
silently discarded. This is an extension point, not evidence that the game currently fills
it with authoritative systems state.

## Missing state for deferred vessels

The current Godot `SystemsController` owns one active-vessel instance of life support, power,
thermal, communications, and the ground-command relay. `SaveSystem` does not capture or restore
those objects into `SaveGameV2.Systems`. In particular, a future deferred vessel cannot yet
reconstruct all of the following from a save:

- life-support resources and crew-alive/alert state;
- battery, committed solar sample, and load state;
- cabin temperature and the last thermal input sample used by a deadline projection;
- communications blackout duration and transient link state;
- delayed ground-command queue contents;
- a per-vessel association for any of the above.

The safe next design is a versioned, per-vessel systems snapshot with explicit validation and
an atomic restore boundary. Transient command queues should be cleared or represented as
timestamped events; they must not be silently replayed after a navigation jump or load. The
snapshot must be captured at the same committed simulation epoch as the vessel state.

## Mission callbacks

`MissionManager.SetPhase` assigns `Phase` and immediately emits `PhaseChanged`; launch and
staging notifications are also emitted synchronously. There is no pending callback queue,
sequence number, or serialized callback timestamp. Therefore
`SimulationExternalInterestInputs.HasPendingMissionCallback` remains `false` in the game
adapter. A phase label must not be converted into a synthetic callback deadline.

Before deferred dispatch can be enabled, mission events need a small authoritative queue or
event log with stable IDs, simulation timestamps, delivery/acknowledgement state, and save/load
coverage. The queue must be drained on both the full-physics and deferred paths so event order
is identical.

## Decision and next gate

Keep `SimulationInterestPolicy.EnabledByDefault == false` and keep the existing
`FullPhysics`/mixed-rails dispatcher authoritative. The persistence extension point is safe,
but no runtime optimization may depend on it until a per-vessel systems snapshot, callback
queue, atomic restore, and policy-off versus candidate-universe comparison exist.

Next gate:

1. define the per-vessel systems DTO and validation rules;
2. capture/restore it at a committed simulation epoch;
3. add callback queue ordering and save/load tests;
4. compare a save/load/resume reference against a candidate deferred vessel across a systems
   deadline, a mission event, SOI, staging, docking, and EDL/catch state;
5. run full tests, both builds, and the visual ascent/EDL harness before any scheduler switch.
