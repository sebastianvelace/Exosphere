# R11 Agent Log — Systems Connected to Mission Phases (follow-up)

**Branch:** `cursor/r11-systems-command-delay-fdaf`  
**Date:** 2026-08-07

## Prior minimum (already on main)

Eclipse→solar, Idle/Active LS, comms delay display, ControlLimited on geometric LOS / power / crew.

## This tranche

1. **`SystemsMissionPhase`** expanded: `HighLoad`, `Entry`, `PeakHeating` (+ legacy `Idle`/`Active`).
2. **`SystemsPhaseLoads`**: avionics kW + thermal coupling area by phase.
3. **`ThermalSystem`**: free-stream aero heat flux leaks into cabin (phase-scaled area).
4. **`GroundCommandRelay`**: queues HUD attitude/throttle by `SignalDelaySeconds`; drops uplink when `!HasSignal` (includes plasma blackout); onboard guidance bypasses.
5. **`SystemsController`**: richer `MapMissionPhase`, early `ProcessPriority`, flush relay, phase EC to Power.
6. **`HUDController`**: routes WASDQE / ZX through ground relay.
7. **`SystemsHUD`**: `GROUND DELAY` / `BLACKOUT` / `LOS` cues.

## Approximations

- Cabin aero leak fraction (1.5%) is a gameplay dial, not a TPS model.
- Avionics extra loads are order-of-magnitude, not vehicle budgets.
- Delayed throttle under high time-warp may coalesce deltas when many samples flush at once.
- Pad ignition (`Ignite`) stays local (not light-time delayed).

## Tests

Extended `SystemsMissionPhaseTests`: phase EC ordering, thermal aero, relay immediate/delay/drop, power drain with peak avionics.
