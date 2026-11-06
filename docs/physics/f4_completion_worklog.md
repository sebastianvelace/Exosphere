# F4 completion worklog

User scope: continue until F4 is complete, including production integration and all retained
analytic/historical gates. Do not stop after the next isolated foundation unit. Preserve
mission tolerances and data provenance. The existing unrelated `.atl/` edits are user work.

## Current branch and baseline

- Branch: `codex/f4-completion`, from pushed `d170eb8` (liquid store intervals).
- Previous complete CI: 1391/1391, all builds/contracts/Godot checks passed.
- Current source changes: complete gross resource windows and partial single-resource engine
  exhaustion, with 117/117 targeted cases passing. Full CI interrupted at the user's explicit request to push immediately; checkpoint commit.
- Complete window capture is still off by default. No mission caller uses variable mass yet.

## Required closure work

- [x] Continuous fixed-component mass/CoM/inertia kernel and liquid main/header intervals.
- [x] Whole resource inventory with exact attributed state chains, overlapping engine/vent
  reservations, RCS/disturbance and non-liquid engine debits (targeted validation passed).
- [x] Partial solid/monopropellant engine intervals; preserve independent funded branches.
- [ ] Finish full CI for the checkpoint; fix any failures before treating the unit as validated.
- [ ] One shared dry/resource/header geometry decomposition; correct missing dry-offset
  parallel-axis variance and use EffectiveMassDry consistently, including offsets.
- [ ] Explicit source/outlet geometry and flow assumptions with catalog provenance; no
  inference of nozzle exit area from unverified pressure anchors or visual thrust pivots.
- [ ] Complete moving-CoM linear/angular transport, internal-flow convention and boundary
  momentum handling; declare assistance replenishment as a separate state transition.
- [ ] Wire consistent candidate mass and transport into coupled, water and docked integration;
  retain coherent datum/CoM conversions, contacts/control geometry and coasts.
- [ ] Analytic conservation/cutoff/offset-flow regressions and production-path tests.
- [ ] Saturn V historical vehicle jet-damping magnitude from a primary dynamics source.
- [ ] Full mission regressions, required Godot validation, evidence and final F4 status updates.

## Research so far

Primary PDFs downloaded to `/tmp/`:

- `exo-saturn-flight-control.pdf/.txt`: NASA 20200002830, modern review of Saturn flight
  control. Refers to Frosch and Vallely (1967), without a vehicle jet-damping coefficient.
- `exo-saturn-booster-stability.pdf/.txt`: NASA-CR-98452 / 19690019009. Text searchable;
  no jet-damping term found by initial search.
- `exo-saturn-malfunction.pdf/.txt`: NASA-CR-288 / 19650021972. Initial text search has no
  explicit jet-damping match; large mass-flow model sections remain to inspect.
- NASA 20230009355, M20-7916 paper, cites Barrows/Orr's internal 2017 Jet Damping in FRACTAL
  memo. This is a potential physics reference, not Saturn historical validation.

The 171 ft-lb/rad/s in Apollo GSOP-501 is SPS gimbal-actuator damping and MUST NOT be
used as the Saturn vehicle jet-damping gate. No historical source has been verified yet.
