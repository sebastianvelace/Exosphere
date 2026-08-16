# CPU interest policy — phase 45

## Scope

This phase adds a pure C# policy primitive and focused tests for deterministic simulation
interest classification. The implementation is deliberately not wired into `Universe`, the
Godot layer, project settings, or the current scheduler. `EnabledByDefault` is `false`; no
runtime behavior changes in this commit.

## Contract

`SimulationInterestInputs` is an immutable value snapshot. It has explicit inputs for:

- active, pilot-controlled, mission-controlled, selected, and mission-critical state;
- thrust and pending commands;
- docking/contact and atmosphere/reentry state;
- pending SOI transitions and an optional next deadline;
- optional distances to the active vessel and the nearest interaction anchor.

`SimulationInterestPolicy.Classify` has fixed precedence:

| Condition | Tier | Wake flags |
| --- | --- | --- |
| Controlled, selected, or mission-critical | `Active` | selection/mission flags are retained |
| Any fail-closed event, or either distance is at/below the proximity radius | `Proximity` | applicable event flags |
| No wake flag and a known deadline outside the wake window | `EventDriven` | none |
| No wake flag and no known deadline | `Dormant` | none |

The default thresholds are `250000 m` for proximity and `60 s` for the deadline wake
window. Both boundaries are inclusive. A deadline exactly at the wake window and a distance
exactly at the proximity radius remain responsive.

## Wake-up safety

The flags are `[Flags]` and cover `Thrust`, `Command`, `DockingContact`,
`AtmosphereReentry`, `SoiDeadline`, `Selection`, and `MissionCriticalState`. Any applicable
event flag prevents `EventDriven`/`Dormant` classification. Flags compose deterministically,
so a caller can audit why a deferred candidate was promoted.

Malformed numeric input is fail-closed: non-finite or negative distances, deadlines, or
policy thresholds produce `Active` plus `InvalidInput`. The separate `Validate()` methods
throw `ArgumentOutOfRangeException` for fail-fast callers. No invalid value is interpreted as
an infinite distance or a distant deadline.

## Verification

Focused command:

```text
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --filter FullyQualifiedName~SimulationInterestPolicyTests
```

Result: **10/10 passing**, 0 failed, 0 skipped, duration 34 ms (`net8.0`).

The pure simulation project build and the Godot project build remain separate checks. This
policy has no Godot dependency and does not require a scene or smoke test.

## Promotion boundary

This is classification-only. It does not materialize snapshots, schedule deadlines, wake
rails, advance time, or reduce existing physics work. Promotion requires a later phase to
prove epoch-based parity for thrust, commands, staging, docking/contact, atmosphere/reentry,
SOI/deadline events, and mission systems while keeping the default mode disabled.
