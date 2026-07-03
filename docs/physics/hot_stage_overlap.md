# Hot-stage overlap burn model (P-S01 — deferred)

## Real vehicle behavior

SpaceX hot-stages Starship by igniting the upper-stage Raptors **before** the mechanical
separation while Super Heavy is still thrusting. Both stages contribute thrust for a short
overlap window (~1–2 s on Flight 7 class missions).

## Current simulation model

- `PartGraph.ActiveEngines` only includes engines in `CurrentStageParts()` — the bottom
  stage below the lowest active decoupler.
- `Vessel.Stage()` / `FireNextStage()` is instantaneous: the decoupler fires, the booster
  subtree detaches, and only then does the upper stage become the burning stage.
- Each stage is modeled as **one physical engine part** (Super Heavy cluster, Starship cluster).

There is no hook for “upper stage lit while lower stage still attached” without either:

1. Cross-stage engine activation and propellant drain (violates current stage-graph invariant
   documented in `PartGraph.CurrentStageParts()`), or
2. The R5 multi-engine / per-cluster rewrite that gives independent throttle per visual bell.

## Why not implemented in P0

Implementing overlap safely requires:

- Temporary dual-stage thrust summation with distinct propellant sinks
- CoM / TWR / gimbal torque during asymmetric dual burn
- Staging timing relative to `[G]` MECO authority in `AscentController`
- Regression tests for insertion profile **and** hot-stage ring thermal loads

That scope exceeds P0 and touches stage-graph invariants explicitly marked out of bounds for
this pass.

## Proposed future model (sketch)

1. Add an explicit `HotStageOverlapWindow` state on `Vessel` or `SimulationBridge` triggered
   by `AscentController` **before** `TriggerStaging()`.
2. During overlap: activate both `super_heavy_booster` and `starship_engines` thrust in
   `ComputeThrust`, drain both fuel tanks, cap overlap duration (~2 s sim time) or until
   decoupler fires.
3. Fire decoupler at overlap end; booster continues on boostback reserve profile (future work).

## Test TODO

- [ ] Unit test: overlap window adds upper-stage thrust while SH still attached without
      double-counting mass or draining only one tank.
- [ ] Telemetry harness: `[G]` ascent records SH+Ship combined TWR spike at MECO, separation
      still at ~61–68 km / ~2.3 km/s.
- [ ] Assert `PartGraph` invariants restored after overlap ends (single active stage).

## MECO authority note (PHYS-01)

`AscentController` is the sole `[G]` authority for MECO/separation via
`AscentStagingPolicy.ShouldHotStageSuperHeavy`. `MissionManager` must not cut throttle or
enter `MissionPhase.MECO` based on fuel depletion — that legacy path fought the velocity-based
hot-stage profile.

Manual flight: player retains throttle; staging phase banners follow `NotifyStaged()` from
manual stage commands.
