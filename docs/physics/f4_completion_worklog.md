> Reverification found Flight 14 altitude/descent failures in the incoming F4 baseline.
> The full run was interrupted after those failures for targeted debugging. Flow-stop,
> off-axis opening and surface-velocity regressions reproduced three defects. Directed
> corrections pass; full mission revalidation is in progress. F4 is open until that validation passes.

# F4 completion record

The resource-window WIP at `d130a70` was continued by `e2e1c27`, which implements the
remaining production F4 work and begins an isolated F5 slosh model. The old unchecked
implementation checklist is superseded; its content remains in Git history.

Delivered in the incoming implementation:

- One mass geometry shared by PartGraph, parts and candidate sampling, with separate dry
  structure and settled resource columns, dry gyration and header points.
- Resource/outlet flow groups, moving-CoM linear/angular transport, per-knot RK4 and
  momentum-conserving changes of internal flow.
- Coupled-path activation by default, persistent transport momentum and explicit fallback
  reasons; legacy/water remain on the documented frozen-mass paths.
- AS-506 geometry/provenance, analytic transport/column gates and a historical S-IC
  jet-damping comparison to flight-reconstructed mass/flow data.
- Flight 14 roll-rate damping after the new draining-column model exposed roll spin-up.

Incoming baseline full CI: verification in progress. The previous incomplete run reported
1428/1430 before the roll correction; the handoff's later targeted passes do not replace a
complete run. Current local verification log: `/tmp/exo-f4-handoff-verification.log`.

The maintained contracts are `PHYSICS_MODEL.md`, `variable_mass_torques.md`,
`variable_mass_resource_adapter.md` and `fidelity_gates.md`. F5's current design is
`propellant_slosh.md`; slosh production coupling remains open. The unrelated `.atl/` edits
are preserved. Do not mark a phase verified from this record alone.

## Continuation diagnostics (2026-11-06)

Red/green regressions establish three corrections in `VariableMassFlowTransitionTests`:
flow-stop momentum transfer, the off-axis opening datum reaction, and material velocity
for surface forces. The directed transport/registry group passes 15/15; combined F4/F5
foundation/provenance checks pass 70/70.

The diagnostic with the first two corrections reaches entry (+132/+229 m display
residuals) and descent (+444/+1038/-1348 m, peak rate 0.0621 rad/s). Those numbers do not
validate the third correction. Full CI with all three corrections still reports
-1942/-1836 m in the 30 FPS return case; it was interrupted to evaluate the estimated
periapsis command before repeating the complete run. No tolerance or geography is relaxed.

Calibration-only diagnostics remove released, noninteracting payloads from propagation
after their real separation; the controller retains every release record and the loaded
carrier is never reset. Gravity depends on celestial bodies, not released vessels. The
uncalibrated diagnostic must first reproduce the complete-run residuals. These diagnostics
never populate the checkpoint fixture cache and do not replace all-vehicle mission tests.

The 30/120 FPS checks now live in separate xUnit classes and call one unchanged assertion
body, allowing their independent full missions to run in parallel. Both remain discoverable
with the `FullyQualifiedName~Flight14ReturnTests` filter; no cadence or assertion is removed.

## Interim profile candidate (superseded by finite-startup corrections)

Keep the original T+7905 command, 110 km radial ceiling, 0.0049 descent fraction,
aerodynamic coefficients, angle-of-attack schedule, water geography and all acceptance
limits. Change only the radial periapsis command from 104.4 to 106.2 km and the estimated
bank-corridor response from 20 to 24 s. Source/status/uncertainty records are updated in
`rigid_body_fidelity_2026.json`; `Flight14CalibrationProvenanceTests` pins the four related
numeric fields to their records. Additional historical displays remain explicit F6 checks,
not silently discarded observations.

The pruned-payload calibration diagnostic reproduces the complete 30 FPS failure within
about one metre. At 106.2 km, the entry residuals are -0.2/+122.3 m. With a 24 s response,
the original fraction gives descent residuals +687/+1222/-676 m, peak angular rate
0.058752 rad/s, and 101.745 m/s density-adjusted terminal speed at 1.2 km (broadcast
100.833 m/s). Repeating that candidate reproduces the result. A nearby fraction 0.0051
at the same response also stays inside all descent gates; fraction changes alone at 20 s
exposed rate/altitude failures and were rejected. These are numerical engineering fits,
not measurements or a unique reconstruction of flown control software.

Directed checks pass 74/74. Full verification uses the real all-vehicle mission paths in
`/tmp/exo-f4-f5-calibrated-ci.log`. A separate moving-solar-system diagnostic is also
checking the physical water continuation; its pruned payloads do not replace the complete
exploration regression. No checkpoint fixture cache is populated from calibration runs.

## Remaining moving-world ignition failure

The calibrated full CI completes 1473/1474 tests: 30/120 FPS return and F2 historical
descent pass. Only the moving-solar-system preview fails at `landing-restart-budget-exhausted`.
`/tmp/exo-f4-moving-starts-probe.log` traces the cause: insertion leaves starts 2/1/1;
deorbit starts at T+7905.34 (3/1/1), shuts down at T+7907.10 solely because rate 0.031356
exceeds the pre-ignition 0.03 gate (alignment 0.997215 is inside 5 degrees), then restarts
at T+7941.82 (4/1/1). Landing correctly rejects a fifth attempt; counters/metadata must not
be reset or enlarged.

`Flight14OrbitalBurn` now commits startup once initial settling passes. An ongoing burn
keeps the same 5-degree pointing bound and the existing 0.15 rad/s continuous mission rate
bound; a violation blocks the burn rather than automatically relighting. The initial
0.03 gate remains unchanged. Four regression cases reproduce the distinction, including
permanent abort and unchanged initial-settling behavior (three failed before the fix).
The targeted orbital/provenance/transport/registry group passes 55/55. Policy constants
are registered as engineering estimates in `flight14_orbital_ignition_2026.json`, not
measured SpaceX limits. Moving and isolated diagnostics are running with the latest code;
another complete CI pass is required before closure.

The first latched moving-world diagnostic correctly aborts at T+7907.56 instead of
relighting: committing nearly on the 5-degree boundary leaves no finite-startup margin.
Ignition now requires 2 degrees (tighter), retaining the 5-degree bound in flight and
0.03/0.15 rad/s initial/ongoing rate bounds. The added 3-degree startup regression fails
before that refinement and passes afterward; the full directed group is 57/57.
`/tmp/exo-f4-moving-margin-probe.log` captures engine counts and preserves a real
pre-integration landing-start save if reached, without resetting any start/resource state.
The isolated and moving diagnostic trajectories are being rechecked before full CI.

## Original profile retained after complete controller corrections

With committed ignition and the 2-degree startup cone, the original 104.4 km return
command passes the unchanged display band: -298.7/-210.7 m. The original 20 s descent
response gives +33.5/+658.2/-2547.3 m, maximum descent rate 0.062605 rad/s and terminal
speed 101.757 m/s at 1.2 km equivalent density. A 24 s sensitivity run also passes,
but no numeric return/descent refit is needed. Retain T+7905, 104.4 km, 0.0049 and 20 s;
the interim 106.2 km and 24 s changes have been removed.

The 106.2/24 diagnostic reached the sea plane at 22.473102 N, 155.403348 W, just outside
the old estimated operating rectangle. The NOAA primary service confirms that extending
the minimum latitude to 22.3 retains complete all-ocean coverage: 15,361,920 native cells,
skipX=skipY=1, maximum -66.552223 m. Current pinned request/statistics are in
`FLIGHT14_OCEAN_COVERAGE_2026-11-06.json`; old evidence remains unchanged. The coverage
contract still requires every cell below sea level and excludes Kauai, Nihoa and Oahu.
A new regression includes the formerly excluded F4 crossing.

Replaying the real pre-integration landing-start save reproduces altitude and speed with
zero initial error, preserves starts 3/1/1 and reaches accepted water contact at 4.474260
m/s, alignment 0.998792, reserve 1537.55 kg, starts 4/2/2. It completes ten seconds of
water response. This diagnostic uses a pruned-payload prefix and is not the final all-
vehicle acceptance result. The original-profile moving trajectories and final CI remain
pending; F4 is not closed yet.

## Final candidate: original guidance numbers and disjoint verified ocean coverage

The original 104.4 km / 20 s profile passes isolated entry (-298.7/-210.7 m) and historical
descent (+33.5/+658.2/-2547.3 m), rate 0.062605 rad/s and terminal 101.757 m/s. Keep the
original command time and descent fraction too. The 24 s sensitivity case also passes
those isolated checks, but is not the selected profile.

The original-profile moving diagnostic reaches a western surface crossing at 21.504359 N,
163.085303 W with starts 4/2/2. It is outside the main coverage box. A second NOAA-native
screen verifies 20..22 N, 164..162 W: all 230,400 cells, skipX=skipY=1, maximum elevation
-1786.885132 m. `AdditionalWaterRegions` represents the disjoint union; its enclosing box
is not ocean coverage. The coverage contract validates the full raster of every region,
keeps island exclusions and rejects invalid additions. Forces, contact-speed/attitude
bounds and resources are unchanged.

Replaying that real pre-integration landing-start save has zero altitude/speed restore
error and reaches water at 4.454311 m/s, upright alignment 0.998801, reserve 1502.15 kg,
starts 4/2/2, then ten seconds of finite water response. No counter/resource/kinematic
state is reset. The unselected 24 s moving case remains outside the declared domains;
it is not used as acceptance evidence. Directed orbital/coverage/provenance/transport/
water checks pass 106/106. Final all-vehicle CI is running in
`/tmp/exo-f4-f5-final-ci.log`; F4 stays open until it passes, including Godot checks.


## Fragmentation momentum correction (2026-11-06)

Final review reproduced a separate angular-momentum defect at stage separation. A stock
stack with 4 kg m/s of saved relative feed momentum and zero relative angular momentum
introduced 164.998404 kg m²/s about the original CoM: the aggregate was retained about a
changed centre rather than partitioned/referenced with the split. Linear momentum was
unchanged. The previous final CI was interrupted to repair this physical defect.

`OpenSystemTransport.SampleMomentumByComponent` now records the same route contributions
per debited store about the datum. Fragmentation partitions by owning part and translates
the angular reference; old aggregate-only saves retain an unowned primary-body share.
Unknown flow remains unknown. Additive Save V2 fields carry the ledger with finite/owner
validation. No feed pause, resource reset or engine-counter reset is introduced.

Directed verification passes 29/29 cases, including geometric reconstruction of mixed
routes at both one-sided boundaries, actual production RCS ownership, staging/joint/payload
splits, save/load, invalid persisted owners/components and unknown flow. The independent
stock-stack reproduction now has exactly zero linear and angular residual. The full
validation for this version is `/tmp/exo-f4-f5-ledger-ci.log`; its result remains pending.
Earlier flight diagnostics in this log predate this fragmentation repair and are not
claimed as evidence for the repaired trajectory.


The repaired continuous diagnostics both reach the sea plane east of Kauai (moving world:
22.131289 N, -158.401513; isolated Earth: 22.176061 N, -158.100198), outside the first two
verified rectangles. The guard correctly blocks them. An additional independent NOAA
native-grid request verifies 22..22.3 N, 159..158 W: all 17,280 cells, no skips/missing cells,
maximum elevation -1692.27 m. The disjoint domain is added with its pinned response hash;
Kauai and unscreened gaps remain excluded. The preceding full CI was interrupted before
changing that runtime domain. Final validation is `/tmp/exo-f4-f5-ocean-ci.log`.


Final directed ocean/ledger/provenance verification passes 59/59. Both real terminal-start
replays reproduce their saved altitude/speed exactly and reach water with unchanged
restart budgets, then complete ten seconds of coupled water response: moving world
4.458111 m/s, alignment 0.998795, 1503.34 kg; isolated Earth 4.422946 m/s,
alignment 0.998877, 1753.13 kg. Sea-level starts are 3/1/1 -> 4/2/2.
The isolated replay requires its isolated Earth world; trying that save in the solar-system
world correctly fails the handoff consistency assertion and is not a physical result.

The staged F4 unit was exported independently to `/tmp/exo-f4-index-check`; its production
library and tests compile without the new F5 files, and 82 directed cases pass (registry,
provenance, flow/fragmentation, orbital ignition and ocean coverage). This verifies the
planned commit boundary; it does not replace complete CI. Log: `/tmp/exo-f4-index-check.log`.


## Feed stop before analytic warp

A minimal vacuum-orbit reproduction at x10 warp demonstrated that Kepler dispatch could
bypass the known no-flow transfer: 4 kg m/s remained stored, leaving 0.001882353 m/s and
0.000376471 m discrepancy after 0.2 s versus the physically settled state. Requires-off-rails
classification now includes retained moments and nonempty per-store shares. The next funded
physical step transfers and clears them, including cancelling aggregates; ordinary Kepler
eligibility resumes afterwards. Unknown flow does not fabricate a wake impulse. The previous
complete CI was interrupted before changing this dispatch boundary. Final complete validation
will be recorded in `/tmp/exo-f4-f5-warp-ci.log`.


The scheduler audit also reproduced unpowered vents and disturbance mass flow being skipped
entirely by analytic warp (300 kg unchanged versus 299.6 kg after the prescribed 0.2 s).
Both live commands now require physical stepping, independently of thrust/attitude commands.
The final directed set passes 54/54, including active/non-active linear/angular feed stops,
cancelling shares, both unpowered inputs and existing coast/warp/tier/interest regressions.
The independent settled-reference reproduction is now within 4.44e-10 m and 4.78e-15 m/s;
recorded momentum is zero and Kepler eligibility resumes. No acceptance bound was relaxed.


All seven new warp cases fail on the isolated pre-fix F4 copy (`/tmp/exo-f4-flow-rails-red.log`)
and pass after repair. The refreshed staged F4 unit compiles independently of the new F5
changes and passes 89/89 directed cases (`/tmp/exo-f4-index-check-final.log`). Production
simulation DLL hashes in both continuous diagnostics match the full CI's compiled DLL.
The authoritative full CI is running in `/tmp/exo-f4-f5-warp-ci.log`; no final closure is
claimed until its complete result and Godot checks are available.


The final continuous isolated-Earth and moving-solar-system diagnostics both complete
`SplashdownReached` after ten seconds of water response. Their landing-entry witnesses
are exactly equal to the previous real terminal-start replay: moving 4.458110590 m/s,
alignment 0.998794597, 1503.340651 kg; isolated 4.422945648 m/s, alignment 0.998876902,
1753.134757 kg. Sea-level starts end at 4/2/2 and booster contact is observed in both.
Logs: `/tmp/exo-f4-moving-warp-probe.log`, `/tmp/exo-f4-isolated-warp-probe.log`.
These diagnostics still prune released noninteracting satellites; complete CI retains all
vehicles and remains the required closure gate.


## Final F4 closure

Complete `bash tools/ci_check.sh` passes 1504/1504 tests with zero skips. All three
builds report zero warnings/errors; contracts, Flight startup, MainMenu/Construction
smoke and graphics capture/reload checks pass. The complete suite retains every vehicle.
Original numeric return/descent profiles and historical/contact/attitude/resource limits
remain unchanged. F4 is closed; F5 remains open for production modal coupling, source-backed
damping, variable-fill/coast transitions and exact persistence. No merge to main.

Evidence: `/tmp/exo-f4-f5-warp-ci.log` and `/tmp/exo-f4-f5-warp-ci.exit`, plus the
directed, independent-index and terminal-replay evidence above.


## 2026-11-07 — exact exhaustion derivative

The independently integrated F5 outlet-momentum balance exposed an F4 endpoint defect.
At exactly zero column fill, `OpenSystemTransport.Sample` skipped the store path because
its loss point coincided with its outlet. The path's left derivative remains nonzero
(q squared / lambda); omitting it introduces an artificial final-stage impulse of order dt.
The sampler now skips only non-column components. A dedicated force/datum-moment
regression and integrated 500/1000/2000-step balances verify the correction without relaxed
acceptance limits. Complete CI with the accompanying F5 kernels: 1559/1559 tests, no skips,
three builds 0 warnings/0 errors, contracts and Godot startup/menu/graphics checks passed.
Evidence: `/tmp/exo-f5-coupled-kernel-ci.log`, exit 0. F4 remains closed; F5 stays open.
