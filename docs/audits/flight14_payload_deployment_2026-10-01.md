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
The renderer now depicts that estimate as a low-profile rectangular flat-pack proxy,
with two face sections and a small center feature. Its 5.26 x 3.77 x 0.45 m visual
dimensions use an estimated aspect ratio; the planform diagonal remains inside the
6.5 m equivalent-disk envelope. These presentation-only faces do not represent
deployed solar arrays and do not affect mass, drag, heating, or release dynamics.

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
smokes. The loaded 30/120 FPS cadence test passed.

## Payload renderer update (2026-10-14)

The former circular renderer has been replaced with a flat rectangular visual proxy.
`bash tools/visual_playtest.sh --flight14-payload --run-id f14-flat-pack-final
--skip-build` passed with a real Xvfb framebuffer capture and verified that the
low-profile box stays inside the estimated equivalent-disk envelope. This is a
display-only aspect-ratio choice; it adds no deployed-array geometry or physical effect.

`bash tools/visual_playtest.sh --flight14-deployment --run-id f14-flat-pack-live
--skip-build` then ran the loaded mission through the first two actual releases and
passed its framebuffer and physical gates. Release times were T+2047.02 and 2120.30 s;
the largest logged linear-momentum residual was 1.066e-6 kg m/s and the two detached
satellite renderers both contained the flat-pack mesh. In that original close-external
frame, one pack was clear while the other was occluded or outside the view.

The 2026-10-14 follow-up added temporary renderer-only tracking of the carrier and two
newest releases, with a yaw update derived from their actual inertial positions. It does
not change vessel states, release impulses, or coast propagation. The new
`--flight14-deployment --run-id f14-payload-camera --skip-build` E2E passed through the
same two live releases; the log confirms two flat-pack renderers, nonzero focus offset,
automatic tracking, and `physicsReplay=False`. Its real framebuffer shows both packs in
the same view. The second remains small and close to Ship's silhouette, so visibility is
improved but not yet presentation-quality. A follow-up scales only the automatic camera
offset to 78% during payload release; it leaves zoom untouched when the player adjusts
the camera. `--flight14-deployment --run-id f14-payload-close --skip-build` passed on the
same two live releases, and its framebuffer shows the same carriers and payloads with a
closer composition. One pack still projects nearly edge-on, consistent with its vessel
attitude; the renderer does not billboard or rotate payloads to fake visibility. Evidence:
`/tmp/exo_play-f14-payload-close/exo_play_flight14_payload_deployment_e2e.png`.

## On-rails attitude propagation (2026-10-15)

The visual audit exposed a physics omission: `Vessel.DeployPayload` correctly transfers the
carrier's orientation and world angular velocity, but Kepler/on-rails propagation advanced
only position and velocity. A released payload with nonzero spin therefore rendered at a
frozen attitude until it woke to full physics. The on-rails path now advances attitude on its
existing bounded coast slices using the same constant-world-angular-velocity update as the
legacy free-flight integrator. This leaves translational orbital state unchanged. The
approximation omits gravity-gradient torque and general torque-free Euler precession; the
SpaceX Flight 14 report confirms deployment and communications but publishes no satellite
attitude telemetry. The earlier screenshots above predate this correction and must not be
treated as visual verification of the updated spin behavior.

The corrected build passed `--flight14-deployment --run-id f14-attitude-rails-4 --max-runtime
7200 --skip-build` and its verify-only gate. The live mission reached orbit and released both
estimated 2,000 kg payloads at MET 2047.02 s and 2120.30 s. Mass residuals were zero;
momentum residuals were 1.066e-6 and 0 kg m/s; relative-opening residuals were 1.861e-9
and 1.510e-8 m/s. The capture at MET 2123.40 s is only about three seconds after release,
so the small inherited angular rate is not visibly distinguishable from the prior frame.
It verifies the real release/render path, not a visible attitude change. Capture:
`/tmp/exo_play-f14-attitude-rails-4/exo_play_flight14_payload_deployment_e2e.png`.

## Next scope

Add a longer post-release observation window and explicit attitude telemetry to the
deployment harness so a framebuffer can show the on-rails spin without changing the mission
schedule. Compare those poses against video frames without treating camera perspective as
attitude truth. The next Flight 14 physics gaps remain the estimated payload mass/impulse,
onboard propulsion, early deorbit, and both water-return policies.
