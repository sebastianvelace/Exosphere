# Phase 46 — FullPhysics / deferred-rails parity gate

Date: 2026-08-15  
Status: **PASS for the covered wake fixtures; runtime interest promotion remains blocked**

## Scope

This gate compares the existing deferred-rails path with an always-checked `FullPhysics`
reference at the same simulation epoch. It does not enable `SimulationInterestPolicy` or
change the dispatch policy. The candidate is a non-active coasting vessel whose conic is
projected until a physical command wakes it; the reference uses the same vessel state as the
active vessel and therefore remains on RK4.

The comparison is intentionally stateful. It checks more than finite coordinates:

- inertial position and velocity;
- wake telemetry and rails exit;
- attitude command, angular velocity and orientation;
- liquid fuel and oxidizer consumption;
- total mass after a powered wake.

## Focused results

`PhysicsSchedulerPerformanceTests` deferred-rails filter: **6/6 passed**.

| Fixture | Required result | Result |
|---|---|---|
| Projection before a deadline | public state remains phase-correct | PASS |
| Periapsis deadline | deadline is serviced without analytic tunnelling | PASS |
| Throttle wake | deferred conic is caught up before RK4 | PASS |
| Attitude/TVC/RCS wake | non-zero `PitchYawRoll` exits rails even at zero throttle | PASS |
| Powered wake | fuel/oxidizer draw matches `FullPhysics` | PASS |
| SOI/force/contact guards | unsafe rails state uses the conservative path | PASS |

The new attitude fixture requires `DeadlineCatchUpDispatches > 0`, a subsequent full-physics
dispatch, `IsOnRails == false`, position/velocity bounds of `1e-4 m / 1e-9 m/s`, angular-rate
error below `1e-10 rad/s`, and exact orientation equality for the deterministic pair.

The powered fixture uses the Flight 7 engine stack. After a `0.05` throttle command, both
liquid-fuel and oxidizer deltas match to `1e-10 kg`, total mass matches to `1e-9 kg`, and the
trajectory remains within `1e-4 m / 1e-8 m/s`. The fixture was corrected to keep the auxiliary
active vessel above the modeled thermosphere; otherwise the global candidate step cap was
being changed by an unrelated vessel and the test compared different integrator schedules.

## Decision

**Keep deferred interest experimental and observational.** These fixtures prove that the
existing rail projection can wake safely for the covered trajectory, command, SOI, contact,
staging and engine cases. They do not prove that a fleet-wide `EventDriven` or `Dormant` tier
may skip all physics.

Promotion remains blocked until the next gate covers:

- life-support, power, thermal and communications resource deadlines;
- queued mission callbacks and save/load while a vessel is deferred;
- undocking, multi-vessel staging and Starbase catch wake paths;
- a policy-off versus candidate-universe comparison over several deadlines;
- Godot visual telemetry proving no lost EDL/catch or mission state.

The official runtime remains `FullPhysics`/existing mixed rails dispatch. No frame cost is
added by these tests or by the observational interest API.
