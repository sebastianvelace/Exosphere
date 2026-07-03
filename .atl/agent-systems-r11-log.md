# R11 Agent Log — Systems Connected to Mission Phases

**Branch:** `feat/physics-systems-mission-phases`  
**Date:** 2026-07-02

## Audit (before)

| Component | Status |
|-----------|--------|
| `PowerSystem` | Solar + battery model existed; eclipse passed as bool from game layer |
| `CommsSystem` | Delay = distance/c, LOS check, signal strength — implemented but untested |
| `LifeSupportSystem` | O2/CO2/H2O/food per crew; no phase gating, no EC tie-in |
| `SystemsController` | Wired tick loop; eclipse math inline in Godot layer |
| `SystemsHUD` | Already shows COMM bar + `Dt` delay label |

## Implemented (R11 minimum slice)

1. **`MissionGeometry`** (sim): Earth umbra cone test + shared signal-delay helper.
2. **`SystemsMissionPhase`** (sim): `Idle` vs `Active`; mapped from `MissionPhase` in controller.
3. **Power**: `extraLoadKw` from life support; solar forced to 0 in eclipse.
4. **Life support**: EC load scales with crew × phase; resource drain only in `Active`.
5. **Comms**: delay uses `MissionGeometry.SignalDelaySeconds` (HUD unchanged).
6. **Controller**: uses sim geometry; passes phase + LS EC load to power tick.

## Approximations (documented)

- Earth umbra only (point Sun, no Moon penumbra).
- Fixed panel area/efficiency; no body-attitude solar pointing.
- Comms delay is one-way geometric distance / c; no relay scheduling.
- Life support EC: 0.45 kW/crew active, 0.15 kW standby (not ISS-calibrated).

## Tests added

`ExosphereSimulation.Tests/SystemsMissionPhaseTests.cs`:

- `EarthUmbra_DetectsVesselInShadow`
- `EarthUmbra_SunlitSideHasNoShadow`
- `PowerSystem_EclipseZeroesSolarOutput`
- `PowerSystem_LifeSupportLoadDrainsBatteryInEclipse`
- `CommsDelay_ScalesWithEarthDistance`
- `CommsSystem_ReportsRoundTripDelayFromPosition`
- `LifeSupport_ActivePhaseDrawsMoreEcThanIdle`
- `LifeSupport_IdlePhaseDoesNotConsumeOxygen`
- `LifeSupport_ActivePhaseConsumesOxygen`

## Deferred

- Night-side-only gate without full umbra (umbra chosen instead).
- Moon/other-body eclipse on solar.
- Two-way comms delay gameplay (read-only HUD sufficient for V1).
- Per-phase thermal/comms degradation beyond existing models.
- RTG / fuel-cell power sources.

## Self-grade

| Criterion | Score | Notes |
|-----------|-------|-------|
| Realism | 7/10 | Core constraints wired; simplified geometry |
| Tests | 9/10 | 9 focused xUnit tests on new sim contracts |
| Scope | 9/10 | No framework; 3 clear behaviors + geometry helper |
