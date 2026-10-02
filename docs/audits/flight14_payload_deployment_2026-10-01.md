# Flight 14 loaded ascent and sequential payload release

Date: 2026-10-01. Scope: numerical held-pad launch, physical staging, single-engine
insertion and 26 sequential payload splits using the same carrier and resources.
This extends the [launch diagnostic](flight14_continuous_launch_2026-10-01.md),
not the playable mission or the controlled water returns.

## Reference and estimate boundary

[SpaceX's Flight 14 report](https://www.spacex.com/launches/starship-flight-14)
confirms 26 Starlink V3 satellites and successful communication after deployment.
Neither it nor the consulted [Starlink V3 update](https://starlink.com/ls/updates/starlink-version-3-satellites)
establishes a flown mass or an ejection impulse. The source-backed manifest count
is checked against the estimate before a diagnostic can launch.

`starlink_v3_flight14_estimate.json` uses **2000 kg per satellite (52000 kg total)**.
This is a configurable engineering assumption, not a sourced Flight 14 measurement.
All-up mass is lumped into dry mass; onboard propellant, thrusters, deployed arrays,
communication and subsequent orbit raising are not modeled. The 6.5 m equivalent
stowed disk and 0.45 m thickness are also estimates, not recovered flight geometry.

`starship_flight14_payload_estimate.json` describes a single internal rack with 26
positions, 0.6 m spacing, and a 1 m/s opening velocity along carrier local +X.
Fit checks use the declared cylindrical nose envelope; actual ogive taper and packing
remain unverified.
This abstracts conveyor/door transport and does not resolve hull collisions.
Scheduled first/last releases T+2047/3879 s are derived from the **published planned
schedule**, not observed actual release timestamps. The controller records real
release times and may delay a release until its physical gates are satisfied.

## Physical implementation

The 26 individually identified part instances are attached before pad initialization.
Their mass and positions participate in the carrier's mass, CoM and inertia through
the production part graph, including during booster ascent and hot staging. No
resource is refilled when insertion or deployment begins.

The new opt-in `internal_payload` content field identifies payloads enclosed while
they are non-root children. They contribute neither additional external hull length,
diameter, nose radius nor Cd, and receive no direct free-stream thermal flux. Becoming
a detached graph root exposes the payload automatically: there is no separate
hidden runtime flag. Saving this diagnostic is not implemented; a future adapter
must preserve the estimated rack nodes as well as the topology. This models enclosure,
not internal conduction from a heated hull. Existing parts default to the original
exposed behavior.

`Flight14PayloadDeploymentController` wraps the launch controller. It accepts the
integrated orbit, commands coast and alignment, waits for shutdown and limits release
angular rate. It blocks release if the carrier orbit enters the atmosphere, the
ship is damaged, ownership changes, a payload is missing/broken, or the bounded
release sequence cannot complete. Each release uses production `DeployPayload`:

- the original part object moves to an independent vessel with the same stable ID;
- carrier mass decreases by the satellite's mass;
- mass-weighted positions and equal/opposite impulses preserve aggregate center and
  linear momentum;
- relative opening velocity accounts for the inherited spin transport term;
- each satellite joins the same Universe and receives a Kepler coast state derived
  from its real post-split position and velocity.

Conservation witnesses are for the existing **aggregate point-mass split model**.
This work does not claim rigid-body angular momentum conservation across the actual
Flight 14 payload conveyor/door geometry. Detached payloads use the existing central-
body Kepler approximation; the off-rails carrier retains production perturbations.
An osculating insertion periapsis target is checked by the burn, not assumed constant
through the subsequent perturbed coast. The deployment safety envelope is separately
set at 220 km radial periapsis and 180 km geodetic altitude.

## Reproduction and acceptance

```bash
dotnet run --project tools/Flight14TrajectoryProbe/Flight14TrajectoryProbe.csproj -- \
  data exports/flight14-mission-review/payload-loaded
python3 tools/compare_flight14_telemetry.py \
  exports/flight14-mission-review/payload-loaded/telemetry.jsonl \
  --output exports/flight14-mission-review/payload-loaded/display-comparison.json
```

The probe now continues through all 26 releases and a 60 s coast tail. It includes
the payload-profile/part hashes, remaining carrier mass/resources, release identity,
actual time, individual conservation residuals and release orbit in its output.
`COMPONENT_PASS` explicitly retains `missionAcceptance: false`. Diagnostic clocks
still commit whole 20 ms steps with retained frame remainder; gameplay/warp integration
and dated illumination remain pending.

## Verification results

The final 60 FPS probe completed with `COMPONENT_PASS` on the loaded model:

| Measurement | Result |
| --- | --- |
| Initial stack mass / represented payload mass | 5387000 / 52000 kg |
| Physical separation | T+142.02 s |
| Upper-stage cutoff | T+494.58 s |
| Cutoff radial apoapsis / periapsis | 281.85 / 61.18 km |
| Minimum unpowered ascent-coast geodetic altitude | 207.88 km |
| Insertion controller start / target achieved | T+1516.32 / 1521.02 s |
| Delivered insertion engine count | 1 |
| First / last physical payload release | T+2047.02 / 3879.02 s |
| Independent payloads / attached payloads after release | 26 / 0 |
| Remaining carrier mass / propellant | 193721.82 / 72721.82 kg |
| Maximum measured physical angular rate | 0.03476 rad/s |
| Minimum satellite release radial periapsis | 238.33 km |
| Largest per-split mass residual | 0 kg at recorded precision |
| Largest aggregate center residual | 1.05e-9 m |
| Largest linear-momentum residual | 2.67e-7 kg m/s |
| Largest spin-corrected opening-velocity residual | 1.35e-12 m/s |

Payload mass changes cutoff from T+479.48 s in the unloaded baseline to T+494.58 s,
and reduces remaining propellant from about 127.35 t to 72.72 t. The insertion lead
was recalibrated from 20 to 14 s as an explicit engineering parameter for the loaded
model; ignition still depends on propagated time to apoapsis and real altitude.
The T+1519 sample is now in the insertion phase with one engine delivering thrust;
T+1544 is unpowered coast. This resolves the earlier sampled phase mismatch, not
all timing/model uncertainty.

The reference comparator covers five of eleven anchors. Speed frame and altitude
datum are still unspecified; all six later rows remain missing, without extrapolation.

| Reference T+ | Geodetic altitude residual | Atmosphere-relative speed residual | Inertial speed residual |
| --- | --- | --- | --- |
| 44 s | +0.16 km | -27.0 m/s | +294.0 m/s |
| 124 s | -3.98 km | -41.9 m/s | +353.4 m/s |
| 164 s | No altitude anchor | +192.1 m/s | +604.4 m/s |
| 1519 s | +0.18 km | +19.9 m/s | +453.5 m/s |
| 1544 s | +0.20 km | -27.6 m/s | +406.0 m/s |

New tests cover enclosed mass/CoM/hull and free-stream shielding, exposure after
separation, all 26 individual split conservation witnesses with nonzero spin,
original-object identity/no duplicate releases, continuous pad-to-release propagation,
coast resource conservation, ownership cancellation and invalid impulse envelopes.
The existing cadence test now uses the loaded manifest at 30/120 FPS.

Full-project CI passed: 949 xUnit tests, nine comparator tests, source-contract
checks, all three builds with zero warnings/errors, and flight/menu/VAB Godot startup
smokes. The loaded 30/120 FPS cadence test passed. No Flight 14 framebuffer replay
or satellite/conveyor rendering acceptance is claimed.

## Next scope

Compare actual release observations from the broadcast rather than treating the
planned timetable as flown truth. Resolve mass/impulse uncertainty and onboard
propulsion. Implement early deorbit and both water-return policies from this loaded
and then depleted carrier state; integrate the mission into Godot with real frame
comparisons before enabling its menu action.
