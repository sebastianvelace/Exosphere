# F4 completion handoff

## User scope and checkpoint

Continue until F4 is closed, then start F5 according to the instructions below. The user
explicitly requested an immediate push for transfer to another agent. This is a WIP checkpoint,
not a claim that F4 or the new unit passed full CI.

Branch: `codex/f4-completion`, based on pushed `d170eb8` (`codex/f4-liquid-store-intervals`).
Keep the unrelated pre-existing `.atl/.skill-registry.cache.json` and `.atl/skill-registry.md`
edits untouched and unstaged. Do not merge into main without authorization.

Read `AGENTS.md`, `CLAUDE.md`, `ROADMAP.md`, `docs/physics/PHYSICS_MODEL.md`,
`docs/physics/rigid_body_fidelity_plan.md`, `docs/physics/variable_mass_resource_adapter.md`,
`docs/physics/variable_mass_torques.md`, `docs/physics/fidelity_gates.md` and
`docs/physics/f4_completion_worklog.md` before continuing.

## Verified baseline

- `d170eb8`: liquid-engine per-store intervals and explicit-geometry mass component conversion;
  complete CI 1391/1391, three builds 0 warnings/errors, contracts and Godot passed.
- `9487678`: pure fixed-shape component mass/CoM/inertia sampler, derivatives and one-sided cuts.
- `154e966`: immutable liquid-engine and main-vent funding receipts, off by default.
- Earlier F4 work includes atomic RCS funding, liquid engine exhaustion, variable-mass/tidal
  kernels and default-on tidal torque in coupled/water/Kepler attitude.
- F3 is closed; its historical tolerances and calibration must not be loosened.

## New work in this checkpoint

`ExosphereSimulation/Physics/ResourceFundingWindow.cs`:

- Complete immutable resource subinventory for one preparation: all four main resources and
  every saved header slot, including undeclared slots as explicit untouched mass.
- Exact attributed before/after chains for liquid engines, single-resource engines, main vents,
  RCS and disturbance flow; invalid loads, unrecorded changes and topology changes invalidate
  coverage. Coverage does not certify outlet geometry or interval representability.
- Captures existing immutable liquid/vent/RCS receipts internally without enabling their older
  public debug flags. `OtherEngines` retains funded solid/mono engine instance flows and cuts.
- Builds fixed-shape store components from the union of debit intervals, with actual start/end
  loads. Positive remaining debit contributions avoid subtracting bulk loads near exhaustion.
  Invalid/increasing/unrepresentable profiles reject conversion. No complete vessel geometry
  or outlet model is inferred. Assisted windows reject physical component conversion until
  an explicit replenishment transition is implemented.
- Engine/vent overlap uses explicit engine-priority budget reservation: the vent reserves the
  post-engine open load but its prescribed interval starts at local zero. This preserves
  nonnegative inventory; it is a simulator allocation convention, not a measured valve model.

`Vessel.CaptureResourceFunding` / `LastResourceFunding`:

- Optional, off by default. Starts before physical preparation and completes after RCS, before
  infinite-propellant restoration; works on coupled and legacy paths.
- Records disturbance consumption separately, and clears readouts when capture is disabled.
- New accounting is observational; exact captured/uncaptured state parity is tested.

`PartGraph.FundSingleResourcePool`:

- Solid and monopropellant engines now fund partial stage-local burn intervals, debit the
  usable mass and use the existing engine-boundary splitter for the actual impulse.
- Fixed the prior failure where insufficient non-liquid fuel rejected the whole partial burn
  and could disable independently funded liquid engines in the same pool.
- Prepared runtime instance identity/flow survives aggregate depletion. Does not introduce a
  persistent engine-failure state. RCS/disturbance keep their atomic whole-step funding policy.
- `tools/tests/engine_failure_disabled_contract_test.sh` now checks that solids and mono use
  resource boundaries. The old literal `engine.MarkFuelDepleted()` guard rejected the new
  correct implementation; the updated guard passes and still forbids failure injection.

## Validation and immediate next action

**117/117 targeted tests passed**, including new `ResourceFundingWindowTests` and
`SingleResourceExhaustionTests`, plus existing funding, exhaustion and mass sampler cases.
Targeted log: `/tmp/exo-resource-window-tests.log` (temporary, may not survive).
The new distance oracle uses absolute error 1e-14 to avoid decimal midpoint rounding artifacts;
no historical acceptance band was changed.

The first full CI stopped in the outdated static engine-failure contract, before xUnit.
That contract was fixed and passes on its own. The restarted full CI was interrupted because
the user requested an immediate push. **Full CI is pending. Do not report it as passed.**
Logs: `/tmp/exo-resource-window-ci.log` and `/tmp/exo-resource-window-ci-final.log`.
Run `bash tools/ci_check.sh` first, inspect every failure, and finish full validation before
promoting this checkpoint to a verified unit. Full CI typically takes about 38 minutes.

No production mission path has switched to continuous mass or variable-mass transport.

## Remaining work to close F4

1. Complete validation of this checkpoint.
2. Share one dry/resource/header geometry decomposition across PartGraph, candidate mass,
   contacts/control, rails, docking and water. Current BuildMassProperties shifts one wet
   cylinder and omits dry/propellant parallel-axis variance. Existing synthetic fixture:
   dry 10 kg at 2 m, fuel 30 kg => omitted 30 kg m2. Use EffectiveMassDry consistently; account
   for MassDryOffset and undeclared header mass. Changing the model requires mission validation.
3. Declare actual outlet stations, exit disk/flow assumptions and source geometry with
   provenance. A gimbal pivot/thrust point is not a verified exit plane. Pressure-derived exit
   areas for some Raptors are explicitly unverified; do not reuse them as measured geometry.
   Vents and disturbance sources still lack a complete verified outlet/internal-flow model.
4. Implement matching linear/angular transport and moving-CoM datum conversion. The maintained
   adapter document has the smooth-interval equations. Do not activate I_dot alone or simply
   replace the mass callback without matching all outflow terms. Relative internal momentum
   and flux-cut boundary handling remain unresolved. If c_dot/p_relative/h_relative jump, prove
   momentum continuity across the cut; one-sided RK4 derivatives alone do not prove it.
   A conserved-momentum formulation was considered but NOT implemented or validated.
5. Handle practice replenishment as an explicit external mass transition. Existing assistance
   restores four main resources, not headers; do not infer exhaust from net assisted mass.
   Handle staging/topology changes with separate windows and preserve docked/water conventions.
6. Add permanent conservation, off-axis internal-flow, flow-switch and production-path tests.
   Retain all analytic/mission/historical tolerances; keep provenance and estimates register
   synchronized for any estimated/derived model/data changes.
7. Verify the Saturn V vehicle jet-damping magnitude against a primary historical dynamics
   source, add/register the historical gate, run full CI/Godot validation, then update the
   fidelity registry, PHYSICS_MODEL, plan, ROADMAP and handoff before declaring F4 closed.

## Historical research status

**No historical Saturn vehicle jet-damping coefficient was verified.** The 171 ft-lb/rad/s
in GSOP-501 is SPS gimbal-actuator damping, not vehicle jet damping. Do not use it to close F4.
NASA's uniform-burn cylinder is an analytic benchmark, not a historical gate.

Primary sources inspected/downloaded (temporary PDF/text files under `/tmp/`):

- NASA 20200002830, Saturn flight-control review:
  https://ntrs.nasa.gov/api/citations/20200002830/downloads/20200002830.pdf
  `/tmp/exo-saturn-flight-control.pdf/.txt`; cites Frosch/Vallely, no verified jet coefficient.
- NASA-CR-98452 / 19690019009, Saturn booster stability:
  https://ntrs.nasa.gov/api/citations/19690019009/downloads/19690019009.pdf
  `/tmp/exo-saturn-booster-stability.pdf/.txt`; initial search found no jet-damping term.
- NASA-CR-288 / 19650021972, Saturn malfunction dynamics model:
  https://ntrs.nasa.gov/api/citations/19650021972/downloads/19650021972.pdf
  `/tmp/exo-saturn-malfunction.pdf/.txt`; mass-flow sections remain to inspect in depth.
- NASA 20230009355 / M20-7916 cites an internal 2017 Barrows/Orr Jet Damping in FRACTAL memo:
  https://ntrs.nasa.gov/api/citations/20230009355/downloads/M20-7916_Paper.pdf
  Search result inspected; paper not downloaded locally. Not a Saturn historical gate.
- General derivation: NASA/CR-1998-208246, Eke, chapters 2-3:
  https://ntrs.nasa.gov/api/citations/19980210404/downloads/19980210404.pdf

## How to begin F5 after F4 actually closes

F5 is the first lateral propellant slosh mode per tank. Start by reading the F5 requirements
and fidelity gates, then verify primary tank/slosh references and define per-tank fill, slosh
mass, frequency and damping with provenance and uncertainty. Write an explicit coupling design
and analytic pendulum/conservation tests before enabling it in missions. The slosh mass must be
part of the existing propellant inventory, not extra mass. The current fixed-shape sampler does
not support moving component centres/shape derivatives; extend its contract consistently with
F4 transport, backreaction and save/load. Add the required published Saturn S-IC slosh gate.
Do not mark F4 closed to bypass missing evidence or begin F6 recalibration to hide model errors.

## Operating constraints

Pure C#, double precision, SI/J2000; no Godot imports in the sim. No historical tolerance
relaxation. Keep generated/harness files out of commits. Conventional commits only, no AI or
Co-Authored-By attribution. Explicit author and committer:
Sebas <sebastianvelace@gmail.com>. Full CI before ordinary verified commits/pushes; this WIP
checkpoint is the user's explicit immediate-transfer exception. No main merge is authorized.
Runtime identity hook has not registered an authoritative Engram session; do not invent an ID
or invoke agent-attributed memory writes until the host re-registers it.
