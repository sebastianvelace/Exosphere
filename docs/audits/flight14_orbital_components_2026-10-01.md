# Flight 14 mission reference and orbital components

Date: 2026-10-01. Scope: source-backed reference data, comparison tooling and
isolated orbital maneuver components. This is not a playable Flight 14 mission or
an end-to-end reconstruction. The featured menu action remains unavailable.

## Reference contract

The [SpaceX mission report](https://www.spacex.com/launches/starship-flight-14),
read on 2026-10-01, identifies the September 28 launch and the early northern-Pacific
return. Its published approximate timeline describes the original long mission;
it does not establish the actual early deorbit or landing clocks.

`data/flight_profiles/starship_flight14_2026.json` separates that planned table from
the manually reviewed video windows in
`docs/research/STARSHIP_FLIGHT14_OBSERVED_ANCHORS_2026-10-01.json`.
The profile records 26 payload deployments, the actual reported engine sequences,
both water returns and the deliberately LOX-limited boostback. It is explicitly
`comparisonOnly`. It is not a replacement vehicle definition or campaign mission.

The reviewed video brackets insertion at T+1519–1544 s and the early deorbit
interval at T+7884–7904 s. These are observed views, not measured ignition and
shutdown endpoints. The reviewed atmospheric view is at T+9884 s; late powered
landing views are at T+11300–11304 s. The subsequent ocean view does not resolve
water-contact speed or outcome. The broadcast's speed frame and altitude datum
remain unspecified, and none of these display values owns simulator state.

## Implemented orbital component

`ExosphereSimulation/Flight/Flight14OrbitalBurn.cs` issues controls to a detached
ship. Insertion points prograde and increases periapsis; deorbit points retrograde
and decreases it. Periapsis comes from the propagated body-relative inertial state.
The component never writes position, velocity or orientation.

The caller supplies target periapsis, minimum geodetic burn altitude, duration
limits, engine model identity and the number of healthy sea-level engines needed
for the go decision. The burn selects one sea-level engine and rejects mount orders
that would instead select a vacuum engine. Completion requires delivered thrust,
not an engine request, a video timestamp or an already-satisfied target.

Before ignition it commands a physical attitude turn and waits for pointing and
angular settling. It uses the existing orbital maneuver damping because an RCS
turn has less angular authority than powered TVC. Both alignment and burn duration
have finite limits. Damaged vehicles, absent engine authority, invalid conics and
out-of-envelope starts fail closed with a diagnostic reason.

Scheduling, tank loading, burn-site targeting, staging, payload release, faults
and the complete mission state machine remain the caller's responsibilities. The
component has not yet been wired into the Godot Flight 14 entry point.

## Numerical evidence and its limits

Ten xUnit cases cover reference separation, integrated insertion/deorbit,
continuous insertion/coast/deorbit, failed sea-level/vacuum engines, mount-order
safety, already-satisfied targets, physical attitude commands and alignment timeout.
The integration tests use the production Universe controller boundary at 20 ms.

The diagnostic fixture uses the existing Flight 12 engineering vehicle, a ship at
276 km radial altitude, an isolated Earth and 300 t of estimated remaining
propellant. These are test inputs, not historical Flight 14 loading measurements.
The insertion target is 200 km periapsis and the deorbit target is 60 km. The fixture
starts only once; the continuous test keeps the same state, parts and tanks across
both burns and the 60 s coast, including the physical reversal for deorbit.

These fixtures check single-engine delivered thrust, consumed propellant, the
correct sign of orbital energy change and periapsis outside/inside the atmosphere.
They do not certify historical burn duration, position, engine restarts or a
three-hour mission. The existing engine definition's restart limit is not enforced
by the current runtime; this remains a mission fidelity gap.

## Display comparison and initial ascent discrepancy

`tools/compare_flight14_telemetry.py` compares actual sampled simulator rows to the
reviewed HUD anchors without interpolation, extrapolation or frame conversion.
It reports inertial and atmosphere-relative speeds separately, and radial and
geodetic altitude separately. Missing data remains missing, never a passing match.
Input hashes, video-frame hashes, clock uncertainty and sample offsets are retained.
Its nine Python tests run in `tools/ci_check.sh`.

The updated visual harness records the actual `LaunchCommitted` callback epoch,
mission-relative time, body-relative inertial speed and radial altitude in addition
to the existing altitude and atmosphere-relative speed fields. It does not change
the generic ascent or its acceptance gates.

The initial diagnostic uses a preserved **historical generic V3 ascent**, not a
fresh Flight 14 run:

```bash
python3 tools/compare_flight14_telemetry.py \
  exports/ascent-compositing-fix/continuous-ascent/telemetry.log \
  --liftoff-epoch 12.5 --clock-uncertainty 3 --maximum-sample-offset 6 \
  --output exports/flight14-mission-review/legacy-v3-display-comparison.json
```

Here 12.5 s is the first recorded ascent state at 42 m, a manual launch-clock proxy,
not a recovered exact liftoff event. Its declared uncertainty is ±3 s. Only 3 of
11 reference anchors have sample coverage; later phases were not flown in that log.

| Reference T+ | Nearest-sample offset | Geodetic altitude residual | Air-relative speed residual |
| ---: | ---: | ---: | ---: |
| 44 s | +1.1 s | +5223.4 m | +141.0 m/s |
| 124 s | +1.6 s | +22866.8 m | +971.7 m/s |
| 164 s | +2.2 s | Not displayed | +1021.7 m/s |

These are display residuals, not physical acceptance criteria. The historical
trace does not contain inertial speed or radial altitude, and the reference does
not identify its frames. The size of the discrepancy supports building a dedicated
ascent profile; it does not justify adjusting gravity or assigning broadcast speed
to the vessel. Generated JSON and Markdown are under
`exports/flight14-mission-review/`.

## Subsequent component evidence

The continuous isolated-Earth pad-to-insertion diagnostic is now documented in
[`flight14_continuous_launch_2026-10-01.md`](flight14_continuous_launch_2026-10-01.md).
It adds a dedicated estimated ascent policy; payload masses, gameplay integration
and both controlled water returns remain open. The historical generic comparison
above is preserved as the initial baseline, not the current diagnostic.

## Next integration gate

1. Give Flight 14 its own dated vehicle/resource assumptions and control policy.
   Preserve unknown dry mass, tank loads, payload masses, fault identities/times,
   feed routing and splashdown coordinates as labeled estimates or unresolved data.
2. Fly continuously from Starbase through suborbital ascent cutoff and insertion.
   Verify the coast reaches the insertion region without an atmospheric encounter;
   compare actual samples at the reviewed ascent and orbital anchors.
3. Release 26 physical payloads with conserved mass/momentum, then target early
   return using the same propagated state and remaining resources.
4. Implement water-return policies for both stages, including booster propellant
   exhaustion/feed behavior. The existing tower-catch controller is not this mission.
5. Gate the menu action on a continuous launch-to-water run, event/resource evidence
   and framebuffer comparisons. Generic orbit success alone is insufficient.

## Validation result

- Both C# projects build with zero warnings and errors; the aggregate CI passed
  all 936 xUnit cases, startup checks and existing source contracts.
- Nine Python comparator tests passed. The final precision/conflicting-clock test
  was rerun after the aggregate check; it does not change the C# components.
- `visual_playtest.sh --smoke --resolution 640x360 --run-id flight14-component-smoke`
  compiled the temporary harness and passed its pad-framebuffer gate (`SMOKE_OK`).
  Its preserved evidence is `/tmp/exo_play-flight14-component-smoke/`. This is a
  harness compile/startup check, not ascent-clock runtime coverage or Flight 14
  visual/trajectory acceptance.

## Validation commands

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --filter FullyQualifiedName~Flight14OrbitalBurnTests
python3 -m unittest discover -s tools/tests -p test_flight14_telemetry.py
bash tools/ci_check.sh
```
