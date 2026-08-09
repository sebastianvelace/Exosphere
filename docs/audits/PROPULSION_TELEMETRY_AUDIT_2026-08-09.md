# Propulsion and stage telemetry audit — 2026-08-09

## Scope

This audit reviews the Flight 7 ascent evidence captured by
`tools/visual_playtest.sh --ascent --flight7` and traces the telemetry fields back to
the runtime engine graph. It is an audit of observability and physical contracts; it
does not change the propulsion solver.

## Finding: the apparent dead booster was a telemetry selection bug

The ascent trace showed `throttle=1`, decreasing propellant and a physically plausible
initial T/W of roughly 1.6, while `runningEngines=0` and `spool=0`. The harness was
querying the component with `vehicle_role == "ship_engines"` for the entire ascent.
Before staging the active cluster is Super Heavy, so the query returned no matching
engines. The same fixed Ship query also made the engine-grid capture report six engines
before staging.

The observed thrust is consistent with the booster: approximately 74.4 MN divided by a
4.8 kt vehicle mass produces the recorded acceleration. This is not evidence of a
propellant or engine ignition failure.

## Required telemetry contract

Telemetry must be derived from the active vessel stage and engine state, not a hard-coded
vehicle role:

| Flight interval | Expected active cluster | Expected engine count |
| --- | --- | ---: |
| Ignition → MECO | Super Heavy | 33 |
| Hot-stage overlap | Super Heavy + Ship | 39 (with state-qualified counts) |
| Separation → orbit insertion | Ship | 6 |

The count should include engines in `Ramp` when chamber pressure is non-zero; a commanded
state of `Ignition` is not the same as a zero-thrust engine. The HUD should expose both
`selected` and `lit` counts so shutdown/purge tails do not look like an engine relight.

## Physics follow-ups

1. Compute gimbal authority only from live, selected mounts that actually have a non-zero
   gimbal range. Fixed outer-ring engines must not inflate flip authority.
2. Exclude failed engines from rated/full-throttle thrust and T/W estimates.
3. Keep propellant mixture and Isp sourced from the runtime `EngineModelDefinition`, so
   future Raptor variants cannot diverge between flow and thrust accounting.
4. Bound residual thrust during shutdown/purge and report it separately from lit engines.

## Acceptance tests

Add focused tests for stage telemetry (booster, hot-stage, Ship), pressure-qualified Ramp
counts, failed-engine exclusion, fixed-ring gimbal authority, runtime mixture parity and
bounded shutdown residuals. The E2E ascent gate should assert the sequence 33 → 39 → 6 and
retain the existing orbit insertion invariant.

## Evidence

- Baseline run: `/tmp/exo_baseline-ascent-v1/run-summary.txt`
- Structured log: `/tmp/exo_baseline-ascent-v1.log`
- Ascent baseline: `docs/audits/REALISM_BASELINE_2026-08-09.md`
