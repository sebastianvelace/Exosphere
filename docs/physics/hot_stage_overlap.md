# Hot-stage overlap burn model (P-S01 — DONE, B2)

## Real vehicle behavior

SpaceX hot-stages Starship by igniting the upper-stage Raptors **before** the mechanical
separation while Super Heavy is still thrusting. Both stages contribute thrust for a short
overlap window (~1–2 s on Flight 7 class missions).

## Implemented model (B2)

1. `AscentController` opens the window via `SimulationBridge.BeginHotStageOverlap()` when
   `AscentStagingPolicy.ShouldHotStageSuperHeavy` fires (MECO speed/altitude or booster reserve).
2. `Vessel.BeginHotStageOverlap(AscentStagingPolicy.HotStageOverlapSeconds)` sets
   `PartGraph.HotStageOverlapActive` for **1.5 s** sim time.
3. During overlap:
   - `ActiveEngines` includes both Super Heavy and Starship engine parts.
   - `ConsumePropellant` drains booster tanks and Ship tanks independently (no cross-feed).
   - Throttle/spool still follow `Vessel.Throttle`.
4. When the timer expires, `Vessel.HotStageOverlapCompletedPending` is set;
   `SimulationBridge._Process` calls `TriggerStaging()` for the mechanical decouple.
5. Manual/instant `TriggerStaging()` clears any remaining overlap state.

## Tests

- [x] `HotStageOverlapTests.OverlapAddsUpperStageThrustWhileBoosterStillAttached`
- [x] `HotStageOverlapTests.OverlapDrainsBothStageTanksIndependently`
- [x] `HotStageOverlapTests.OverlapEndsWithSingleActiveStageAfterMechanicalSeparation`
- [x] Staging band still gated by `AscentStagingPolicy` (~2.3 km/s, ≥45 km)

## MECO authority note (PHYS-01)

`AscentController` remains the sole `[G]` authority for MECO/separation via
`AscentStagingPolicy.ShouldHotStageSuperHeavy`. `MissionManager` must not cut throttle or
enter `MissionPhase.MECO` based on fuel depletion.

## Out of scope

- Boostback / landing burn for the detached booster.
- R5 per-bell multi-motor / engine-out.
