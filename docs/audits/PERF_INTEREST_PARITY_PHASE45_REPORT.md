# Phase 45 — simulation-interest parity gate

Date: 2026-08-15  
Status: **PASS for observational adapter; runtime promotion remains blocked**

## Scope

This gate connects the pure `SimulationInterestPolicy` to an authoritative, read-only
query on `Universe`:

```csharp
universe.GetSimulationInterestDecision(vessel)
```

The adapter snapshots only state that already belongs to the CPU simulation: active vessel
ownership, finite kinematics, engine/throttle wake state, docking connections, surface/catch
contact, atmospheric force sensitivity, structural control loss, and the existing scheduler's
periapsis safety plan. The Godot boundary now has a second, explicit read-only input contract
for mission/controller and systems state: mission callback, descent/entry, life-support,
power, thermal, and geometric communications alerts. Plasma blackout is intentionally not
treated as geometric control loss because onboard entry guidance must continue through the
blackout.

The query does not advance `CurrentTime`, change `IsOnRails`/`OrbitalState`, consume resources,
or skip any scheduler work. `SimulationInterestPolicy.EnabledByDefault` remains `false`; the
existing `Universe.Tick` dispatcher is still the official runtime path.

## Fixtures and results

`ExosphereSimulation.Tests/SimulationInterestUniverseParityTests.cs` exercises eleven tests,
including a five-row policy matrix for the requested transitions:

| Scenario | Expected result | Covered contract |
|---|---|---|
| Active vessel | `Active` | pilot/selection stays full resolution |
| Coasting rail vessel | `Dormant`, no wake flags | query is read-only and safe to defer later |
| Staging fragment / new command | `Proximity` | thrust and command wake immediately |
| Docked secondary | `Proximity` | connection is visible to the adapter |
| Periapsis/SOI boundary | `Proximity` + `SoiDeadline` | no conic deadline is hidden by deferral |
| Tower-catch EDL | `Active` + mission-critical | contact, atmosphere and catch remain protected |
| Invalid numeric state | `Active` + `InvalidInput` | fail-closed, never deferred |
| Systems mission-critical matrix row | `Active` | future systems scheduler must preserve critical state |
| Attitude command at zero throttle | `Proximity` + `Command` | TVC/RCS control cannot be deferred |

Focused result: **11/11 passed** for the Universe parity fixtures, plus three pure-policy
fixtures for external mission/systems state.

The official visual ascent harness also passed with the attitude wake guard enabled:
`--ascent --flight7 --run-id phase46-attitude-wake --skip-build` reached
`ASCENT_ORBIT_OK`, captured liftoff, max-Q, hot-stage, separation and orbit, and reported
33 booster engines / 6 ship engines with zero engine failures in its trace.

The fixture caught and corrected a subtle policy error: the existing `DeferredRails` interval
(`2 s`) is a bounded projection cadence, not a physical SOI deadline. Feeding it into the
60-second wake window incorrectly promoted every coasting rail vessel to `Proximity`. The
adapter now leaves the optional physical-deadline field empty for that cadence and uses the
explicit `PeriapsisEvent` reason for the currently modeled safety boundary.

The next safety check also found that the scheduler's wake predicate only considered throttle
and active-engine demand. It now treats a finite non-zero `PitchYawRoll` command as a wake
condition and rejects non-finite/out-of-range throttle or attitude state as invalid. This keeps
TVC/RCS corrections observable even when a vessel is coasting with engines closed.

The external-state adapter is deliberately scoped to the active vessel. It reports mission
phase ownership and system alerts without creating system controllers for distant vessels.
`SimulationBridge.GetSimulationInterestDecision(vessel)` overlays that snapshot only when
`vessel == ActiveVessel`; distant vessels continue through the pure CPU snapshot. This avoids
the false optimization of treating an active vessel's life-support alert as a wake reason for
the whole fleet.

## What this does not prove

This is not yet permission to skip physics for distant vessels. The fixtures do not establish
parity for every event that can mutate a fleet: all staging topology variants, undocking,
resource starvation deadlines, mission callback queues, SOI transfer materialization, and
Godot-side EDL presentation state still need promotion tests at the dispatcher boundary.
The external contract is now present, but its `HasPendingMissionCallback` input remains false
until a real queued callback source exists; the adapter does not invent callbacks from a phase
label.

In particular, the policy's `EventDriven` tier is covered by the pure policy tests, but the
current `Universe` deadline planner does not expose a future physical timestamp for a safe
coasting rail vessel. The observational adapter therefore returns `Dormant` for that state
instead of inventing a deadline.

## Decision

**Keep the runtime interest policy off for this phase.** The adapter and fixtures are suitable
telemetry for the next scheduler-integration phase. A later promotion must compare a policy-off
reference universe against an opt-in universe at identical epochs and prove staging, docking,
SOI, EDL, resource/systems, save/load, wake-up, and mission-event parity before any work is
actually deferred.
