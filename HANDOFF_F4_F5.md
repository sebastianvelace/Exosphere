# F4 to F5 continuation

Published foundation: `codex/f5-slosh-foundation` at `9003d6a`.
Active continuation: `codex/f5-slosh-production`, based on that verified foundation.
The user requested completion of F5 and then F6; no merge to main.
Preserve the pre-existing `.atl/` changes. Author and committer are
Sebas <sebastianvelace@gmail.com>; conventional commits without attribution trailers.

## Current status

F4 is verified and closed: complete `bash tools/ci_check.sh` passes 1504/1504 tests,
no skips, all three builds with zero warnings/errors, contracts and Godot startup/menu/
graphics capture/reload checks. Log: `/tmp/exo-f4-f5-warp-ci.log`.
Do not infer closure from an old directed result. The incoming full CI failed four
mission cases; the earlier continuation's complete run passed 1473/1474, then exposed
orbital restart cycling. Subsequent review reproduced a fragmentation conservation bug.
Both defects are repaired; the final all-vehicle mission suite passes with the original
acceptance bounds and numeric return/descent profiles.

## F4 corrections

- Transfer retained internal-flow momentum when a known feed stops; compensate datum
  velocity for the angular reaction when an off-axis feed opens.
- Use material velocity for aero/contact, excluding the moving-centroid coordinate term.
  Integrated centroid position/velocity telemetry retains its coordinate convention.
- Partition routed momentum by debited store and carry angular shares about the datum
  through staging, joint breaks and payload deployment. Save V2 adds optional shares,
  validates finite values/owners and preserves legacy known/unknown distinctions.
  The independent stock-stack reproduction changed from 164.998404 kg m²/s angular
  error to exactly zero. The initial conservation/persistence set passed 29/29; final independent F4 verification
  now includes the scheduler regressions below.
- Retained flow moments/nonempty shares require one funded physical step before analytic
  warp/coast. Otherwise a known stopped feed remains stored instead of reacting on the body.
  Unpowered vent/disturbance flow commands also wake physical integration from their first tick.
  The minimal vacuum-orbit reproduction left 1.882353 mm/s of velocity discrepancy before
  this barrier; active/non-active and linear/angular regressions cover the transition.
- Commit orbital ignition after initial settling: 2-degree pointing, original 0.03 rad/s
  start rate. Running burns retain 5-degree pointing and the original 0.15 rad/s mission
  envelope; a violation aborts permanently rather than automatically relighting.
- Skip constructing a transport model for a complete no-debit window already discarded
  by Universe. Unknown/assisted/unsupported flow still falls back explicitly.

The original numeric return/descent profiles remain: T+7905 command, 104.4 km radial
target, fraction 0.0049 and 20 s bank response. Interim numeric fits were discarded after
fixing finite startup. Original historical/contact/attitude/resource limits remain.

The ocean operating domain is the union of three NOAA ETOPO 2022 source-verified
rectangles, not their enclosing box: 22.3..35 N / 161..140 W;
20..22 N / 164..162 W; 22..22.3 N / 159..158 W east of Kauai. Every native 15 arc-second
cell was screened (15,361,920 / 230,400 / 17,280 cells, no skips/missing cells;
maxima -66.55 / -1786.89 / -1692.27 m). Requests/statistics/hashes are pinned in
`docs/research/FLIGHT14_OCEAN_COVERAGE_2026-11-06.json`. Nearby islands and unscreened
gaps remain excluded. Geographic coverage supplies neither steering nor measured targeting.

The corrected continuous isolated/moving diagnostics preserve their real carrier, booster,
engine states and resource debits; they prune released noninteracting satellites and do
not populate checkpoint caches. The latest continuous runs, including the warp barrier, both complete `SplashdownReached`
east of Kauai and ten seconds of water response. Their entry states match the earlier
real pre-integration save/replay verification exactly:

| World | Entry speed | Upright alignment at entry | Remaining propellant | Starts before -> after |
| --- | ---: | ---: | ---: | --- |
| Moving solar system | 4.4581 m/s | 0.998795 | 1503.34 kg | 3/1/1 -> 4/2/2 |
| Isolated Earth | 4.4229 m/s | 0.998877 | 1753.13 kg | 3/1/1 -> 4/2/2 |

Both complete ten seconds of coupled water response. Subsequent heeling belongs to the
sealed-hull estimate; it does not validate an upright recovered vehicle or flooding timing.
The full CI retains every vehicle and is the authoritative complete-chain acceptance.
The preceding ocean/ledger/provenance directed verification passed 59/59. The latest staged F4 unit also
compiles independently without the new F5 changes and passes 89 directed tests
(`/tmp/exo-f4-index-check-final.log`). Warp/coast/tier regressions pass 54/54, and all seven
new cases fail on the pre-fix isolated copy (`/tmp/exo-f4-flow-rails-red.log`).

Current continuous evidence: `/tmp/exo-f4-moving-warp-probe.log`,
`/tmp/exo-f4-isolated-warp-probe.log`. Earlier replay/diagnostic evidence:
`/tmp/exo-f4-ledger-moving-landing-replay.log`,
`/tmp/exo-f4-ledger-isolated-landing-replay.log`,
`/tmp/exo-f4-final-ocean-targeted.log` and `/tmp/exo-f4-f5-warp-ci.log`.
Older calibration/probe logs in the worklog predate the latest fragmentation correction.

## F5 foundation

Permanent `PropellantSloshTests` replaces the uncompiled draft: 32 cases cover independent
Bessel root/limits, mass/centroid, isolated nonlinear momentum/energy, free-carrier
frequency, internal damping, numerical extremes and AS-501 probe frequencies. Historical
comparison uses reported axial proper acceleration, all four samples including 130 s,
the measured dotted curves and the original 10% band. Source/uncertainty records and
the estimates register are installed. Registered F5 gates stay open.

No production modal state, full 6-DoF slosh coupling or modal persistence exists yet.
`docs/physics/propellant_slosh.md` defines the next implementation unit: a fixed-fill
6-DoF oracle with one modal mass/two coordinates per tank, locked-inertia identity,
positive generalized kinetic matrix and independent momentum/energy verification.
Then add source-backed damping, funded variable-fill terms, nonsettling/coast regimes,
topology and exact save/load. Do not duplicate liquid mass, invent 1 g in free fall or
count an inertial reaction twice. F6 historical recalibration/geometry gaps follow F5.

See `docs/physics/f4_completion_worklog.md` for the investigation history and
`docs/physics/fidelity_gates.md` for authoritative phase status.


## Active production continuation

The fixed-fill coupled Newton/Euler kernel is implemented in
`ExosphereSimulation/Physics/Slosh6DofDynamics.cs` with independent tests in
`ExosphereSimulation.Tests/Slosh6DofDynamicsTests.cs`. Initial directed run passes 79
cases (new 6-DoF kernel, prior first-mode suite and provenance). Matrix positivity,
exact locked inertia, 3D two-mode momentum/energy, internal damping, free fall and
free-carrier frequency pass. Numerical conditioning policy is registered and the
estimates register regenerated. No new production caller, modal persistence, tank damping
or variable-fill adapter is connected yet; F5 remains open. See the new derivation and
source review at the end of `docs/physics/propellant_slosh.md`.

Next: validate catalog equilibrium kinetic matrices across fill fractions; review NASA
AAS 24-012 / SwRI model equivalence; extend the modal state beyond its lower-hemisphere
chart for nonsettling coast; derive variable-fill transport consistently with F4 rather
than double-applying inertia or axial torque. Then integrate candidate stages, actual
specific force/damping, topology and save/load, and run the complete acceptance suite.
Continue through F6 historical recalibration after F5 is actually closed. Preserve the
pre-existing .atl changes and keep all work on codex branches. Do not commit before full CI.


Further active progress: globally parameterized spherical continuation and analytic
parameter jets are implemented. The current directed set passes 93 cases, including
independent 3D conservation across the old chart boundary, parameter time derivatives,
finite exhaustion limits and real AS-506 equilibrium matrices at full/half/1% fill.
Starship V3 has no declared `propellant_tanks`; provisional nonempty-mode test cases failed
for absent geometry, not negative kinetic energy. This gap is documented rather than
inventing tank stations. Production data/adapter work remains.

The new parameter-transport/datum-wrench derivation is appended to `propellant_slosh.md`.
Implement and independently test it next, then wire the production modal state, actual
forces, finite/coast regimes, damping and exact persistence/topology. The current source
is uncommitted on codex/f5-slosh-production; complete CI is required before committing.
Main and .atl remain untouched. The user's goal is to finish F5 and F6, not stop at this
new kernel milestone. No memory tools may be used until runtime identity registration is
restored by the host hook; do not invent or start a session identity.


Latest active milestone: 100 directed cases pass in `/tmp/exo-f5-combined-reynolds.log`.
Implemented new `SloshTransportCorrection`, `SloshVariableMassDynamics` and shared
`SloshKineticSystem`; scheduled modal momentum/inertia derivatives and independent net
outlet-flux balance pass, including a true zero-modal-mass kinematic limit. The generic
curvature/displacement state constraint and planned production representation are recorded
at the end of `propellant_slosh.md`. Implement that state/integrator and then the Universe,
persistence/topology and damping/data adapters. These remain uncommitted and unactivated.
The user's request remains completion through F5 and F6; do not stop at this kernel milestone.


Latest implementation: global `SloshDisplacementState`, `Slosh6DofIntegrator`, and
`SloshMomentumTransfer` are implemented. Directed validation passed 129 cases in
`/tmp/exo-f5-conservative-empty-flow.log`, including independent integrated outlet P/H
through exhaustion. That test exposed and fixed the F4 zero-length column-path derivative
bug in `OpenSystemTransport.Sample`; old source showed residuals proportional to dt
(3e-5, 1.5e-5, 7.5e-6 kg m/s for 500/1000/2000 steps), corrected source passes 1e-7.
A dedicated F4 gate is registered; selected new F5 analytic gates are now registered too.
No production activation has changed yet. Next: production store-owned state, rate-cut
metadata, Universe/force/rails integration, source/provenance damping and tank geometry,
persistence/splits/docking, full mission acceptance, then F6 recalibration. Do not close
F5 or stop at the new pure-kernel milestone.


Complete CI for the pure-kernel unit is now green (2026-11-07): 1559/1559 tests,
0 skipped, three builds 0 warnings/0 errors, static contracts and Godot startup/menu/graphics.
Log `/tmp/exo-f5-coupled-kernel-ci.log`; `/tmp/exo-f5-coupled-kernel-ci.exit` is 0.

Production continuation is being developed and tested in an isolated source copy at
`/tmp/exo-f5-runtime-sandbox` to keep the completed CI inputs stable. Its 26 directed tests
pass for production P/H/energy conservation, funding/exhaustion, Save V2 exact continuation
across a flow stop, invalid save rejection and old-save compatibility. The new Universe
path, per-store state, tank-frame transforms, optional persistence, and partial coast wiring
compile there. Kepler/deferred coast tests, topology impulses/docking, damping/provenance,
full mission validation and F6 remain. Incorporate the isolated changes only after the
kernel checkpoint is committed; compare against `/tmp/exo-f5-runtime-sandbox-base-hashes.json`
and preserve unrelated edits. The isolated tree is not yet production-verified or published.
