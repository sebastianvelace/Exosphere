# F4 to F5 continuation

Branch: `codex/f5-slosh-foundation`, based on `ea94c64`. No merge to main.
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

## Next unit: F5 foundation

The incoming first-mode kernel and uncompiled draft need permanent regression tests,
source records, registered open gates and an integration design before production slosh.
Do not count a second copy of the liquid mass or substitute gravity for proper acceleration.
Full 6-DoF body/modal coupling, damping data and exact modal persistence remain pending.

See `docs/physics/f4_completion_worklog.md` for the investigation history and
`docs/physics/fidelity_gates.md` for authoritative phase status.
