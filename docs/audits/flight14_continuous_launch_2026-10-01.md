# Flight 14 continuous launch-to-insertion diagnostic

Historical unloaded baseline at `f04a909`. The current tool includes estimated
payload mass and release; see [the payload audit](flight14_payload_deployment_2026-10-01.md).

Date: 2026-10-01. Scope: a reproducible numerical launch, hot-stage, suborbital
cutoff, coast and single-engine insertion. This is a component integration gate,
not a playable Flight 14 mission or a complete end-to-end flight.

## Implemented behavior

`Flight14LaunchController` runs at the production 20 ms controller boundary. It
commands throttle and attitude, releases the hold-downs when delivered thrust can
lift the stack, invokes physical staging and hands insertion to
`Flight14OrbitalBurn`. The entire run uses the same vessel and remaining resources.
Only fixture initialization seeds a pad state; staging uses the existing
mass-conserving split and separation impulse. There are no prescribed broadcast
velocities, gravity changes, trajectory position assignments or tank refills.

The sequence is:

1. Ignition while held on the Starbase Pad 2 frame, inheriting the planet's spin.
2. An estimated booster pitch/throttle program. MECO is a designed command near the
   published planned time, with a minimum staging-altitude guard. Three selected
   booster engines remain during the two-second upper-stage ignition overlap.
3. Physical separation, followed by upper-stage ascent. A design conic and nominal
   coast duration define radial guidance; integrated orbital energy decides cutoff.
4. Cutoff only on an ascending **suborbital** conic with adequate predicted coast
   time and an apoapsis in the insertion region. An already-stable orbit is not this
   phase's successful outcome.
5. Unpowered coast above the modeled atmosphere. The controller starts insertion
   from propagated time to apoapsis and actual altitude, not the video clock.
6. Single-sea-level-engine insertion, completed only after delivered thrust raises
   periapsis above the configured target.

Ignition, powered ascent, alignment/burn and overall launch duration have finite
failure envelopes. Switching the active vessel cancels commands to the owned ship.

The command reference for the initial upper-stage pitch change is rate-limited;
the actual orientation remains actuator-driven. Radial terminal guidance blends
continuously with the earlier ascent law. These avoid the abrupt pitch reversals
that initially produced about 0.20 rad/s while still reaching orbit. Acceptance
also checks the resulting physical rate rather than silently clipping it.

## Explicit assumptions and remaining limits

The [SpaceX report](https://www.spacex.com/launches/starship-flight-14) supplies the
mission facts, but not complete flown loading, performance, fault identities or
fault times. `starship_flight14_2026.json` remains the separate, comparison-only
reference. The new `starship_flight14_guidance_estimate.json` is explicitly marked
`engineering-estimate`; it is not a historical vehicle specification.

The diagnostic deliberately uses the existing estimated Flight 12 V3 part and
engine model: 5.335 Mt initial stack mass and its full modeled tanks. The guidance
program, 100 s booster fault, 200 s vacuum fault, chosen fault mounts and control
parameters are synthetic reconstruction assumptions. The counted engine failures
use real runtime instances; their times and identities are not observed facts.

The 26 satellites' masses and deployment are **not represented**. Both controlled
water returns are also absent. Adding payload mass will change acceleration,
propellant use and cutoff, so this calibration cannot be promoted unchanged to
full mission acceptance. The existing engine restart-limit enforcement gap also
remains open.

`Flight14LaunchDiagnostic` initializes an isolated Earth at simulation epoch zero,
with the production rotating, oblate body and atmosphere. It does not claim dated
Sun/Moon ephemerides, historical illumination or weather. Moving-body gameplay
integration and a continuous launch-to-water run are still required before the
menu's Flight 14 launch action can be enabled.

## Reproduction

```bash
dotnet run --project tools/Flight14TrajectoryProbe/Flight14TrajectoryProbe.csproj -- \
  data exports/flight14-mission-review/continuous-launch
python3 tools/compare_flight14_telemetry.py \
  exports/flight14-mission-review/continuous-launch/telemetry.jsonl \
  --output exports/flight14-mission-review/continuous-launch/display-comparison.json
```

The probe accepts an optional third argument for outer FPS (default 60).
`Flight14LaunchDiagnostic.AdvanceFrame` retains the frame remainder and commits
whole 20 ms integration steps. Controller sampling alone does not ensure identical
legacy actuator integration: directly feeding 30/120 FPS partial steps to Universe
produced an approximately 11 s coast difference with the refined guidance. The
diagnostic therefore has its own fixed-step driver and explicitly rejects warp.
The game adapter must adopt the same integration policy before this profile is
promoted. This change does not alter the generic Universe scheduler or existing
free-flight behavior. The diagnostic writes
actual mission-relative samples, phase transitions, resource state, delivered
engine counts, radial/geodetic altitude and both speed frames. After insertion it
propagates another 60 s to cover the reviewed post-insertion frame. Source-data and
physics-assembly hashes are included in `summary.json`. Successful output is
`COMPONENT_PASS` with `missionAcceptance: false`, never a Flight 14 mission pass.

Use the existing telemetry comparator without adding Earth rotation or assigning
an altitude datum to the reference. It retains missing coverage and frame ambiguity.
The reviewed broadcast anchors and their hashes remain unchanged.

## Verification

The new tests exercise the production controller from held pad state through
insertion, including continuous propellant consumption, coast shutdown, physical
attitude-rate limits, delivered single-engine thrust, modeled ascent engine losses
and both separated vessels. A second run compares 30 and 120 FPS. Ownership-loss
and dry-booster ignition tests verify bounded failure behavior. Frame-remainder and
invalid-step/warp tests verify the diagnostic clock contract.

Validation passed: all three builds completed with zero warnings/errors; the full
CI check passed 941 xUnit tests, nine telemetry-comparator tests, source-contract
checks and Godot startup smokes. No Flight 14 framebuffer replay was performed.
The 30/120 FPS diagnostic test produced the same cutoff/insertion times and
remaining mass at the same committed epoch.

The 60 FPS probe completed with `COMPONENT_PASS`:

| Integrated event or measurement | Result |
| --- | --- |
| Physical separation | T+142.02 s |
| Upper-stage cutoff | T+479.48 s |
| Cutoff radial apoapsis / periapsis | 282.89 / 61.21 km |
| Minimum coast geodetic altitude | 204.95 km |
| Insertion controller start | T+1524.84 s |
| Insertion target achieved | T+1529.60 s |
| Maximum delivered insertion engine count | 1 |
| Maximum physical angular rate | 0.04157 rad/s |
| Remaining ship propellant after 60 s orbit tail | 127354.94 kg |

The comparator sampled five of eleven reviewed broadcast anchors. Six later
anchors remain outside the run's coverage; no samples were extrapolated. Rounded
signed residuals below are simulation minus displayed reference. The broadcast
speed frame and altitude datum remain unspecified, so these columns are comparison
candidates, not proof of physical agreement.

| Reference T+ | Simulated phase | Geodetic altitude residual | Atmosphere-relative speed residual | Inertial speed residual |
| --- | --- | --- | --- | --- |
| 44 s | Booster ascent | +0.31 km | -20.1 m/s | +297.5 m/s |
| 124 s | Booster ascent | -2.60 km | -12.2 m/s | +381.8 m/s |
| 164 s | Ship ascent | No reference altitude | +237.1 m/s | +648.6 m/s |
| 1519 s | Suborbital coast | +1.18 km | -6.4 m/s | +427.3 m/s |
| 1544 s | Orbit ready | +1.23 km | -28.4 m/s | +405.3 m/s |

The T+1519 s video anchor shows a single-engine indication and plume, while the
diagnostic is still coasting. Its insertion controller starts 5.84 s later than
that observed frame. This phase mismatch is unresolved. The earlier-than-planned
cutoff also cannot be called a reconstruction of the flown cutoff, especially
while payload mass is absent. Numerical component success does not establish
visual fidelity or complete mission acceptance.

## Next gate

Include 26 distinct payloads with explicit estimated mass and deployment impulses,
then repeat the ascent and orbital evidence with that mass budget. Follow with
payload release, early deorbit and water-return policies using the same propagated
state. Integrate into the Godot mission adapter and compare real framebuffer views
before enabling the featured mission launch.

## Subsequent payload integration

The results above preserve the unloaded component baseline at `f04a909`. The
diagnostic now includes 26 estimated-mass satellite parts and continues through
physical release. Its payload-loaded ascent, updated insertion lead and current
verification are documented in [the payload integration audit](flight14_payload_deployment_2026-10-01.md).
The unloaded numbers and reproduction scope above are historical evidence, not
the current tool output.
