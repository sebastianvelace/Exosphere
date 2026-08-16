# Phase 48 — persistence and mission-event parity audit

Date: 2026-08-15  
Status: **PARTIAL IMPLEMENTATION; deferred runtime promotion remains blocked**

## Findings

`SaveGameV2` is the authoritative persistence format. It already round-trips the state
needed by the current physics path: simulation epoch and time scale, vessel kinematics and
orientation, rails state, throttle/attitude, ground hold, destruction state, parts and
resource quantities, engine lifecycle state, crew, docking connections, mission/campaign
metadata, and persistent assets. Existing regression tests cover the stable-id, resource,
docking, legacy-migration, and invalid-save paths.

The format exposes both `SaveGameV2.Systems`, a case-preserving JSON extension dictionary, and
the typed `SaveGameV2.VesselSystems` map. The extension test proves unknown future data
survives serialize/deserialize, while phase-49 tests cover typed state. The typed map is
validated against vessel identity and the exact save epoch before restore.

## Missing state for deferred vessels

The current Godot `SystemsController` owns one active-vessel instance of life support, power,
thermal, communications, and the ground-command relay. The active vessel's life-support,
power, thermal, and communications state is now captured into `VesselSystems` at the same
committed epoch and restored after the vessel state. A future deferred vessel still cannot yet
reconstruct all of the following from a save:

- delayed ground-command queue contents;
- a live systems controller for every non-active vessel;
- mission callback delivery state associated with those vessels.

The typed map is versioned and keyed per vessel, but the game layer currently writes only the
active vessel because only that controller exists. Transient command queues are cleared on
restore; they must later be represented as timestamped events if deferred vessels retain them.
Every snapshot is required to use the same committed simulation epoch as the vessel state.

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
`FullPhysics`/mixed-rails dispatcher authoritative. The persistence extension point and
active-vessel restore are safe, but no runtime optimization may depend on them until every
deferred vessel has authoritative systems state, a callback queue, and a policy-off versus
candidate-universe comparison.

Next gate:

1. instantiate/capture the typed systems map for every vessel that can be deferred;
2. add callback queue ordering and save/load tests;
3. compare a save/load/resume reference against a candidate deferred vessel across a systems
   deadline, a mission event, SOI, staging, docking, and EDL/catch state;
4. run full tests, both builds, and the visual ascent/EDL harness before any scheduler switch.
