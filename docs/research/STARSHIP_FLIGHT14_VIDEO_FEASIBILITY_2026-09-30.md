# Flight 14 video references and end-to-end feasibility

Status: reference extraction verified; mission-specific simulation not implemented.

## What the sources establish

The [SpaceX mission page](https://www.spacex.com/launches/starship-flight-14),
read in a rendered browser on 2026-09-30, dates launch to September 28 at
07:48 Central. Its outcome describes orbital insertion after a safe initial
suborbital ascent, 26 deployed satellites and an early return to the **northern
Pacific**, prompted by caution about an ascent engine issue. Both stages
splashed down; the booster did not attempt a tower catch. The page's approximate
timeline still puts deorbit at 08:52:37 and entry at 09:28:56. Those entries
must not be treated as the observed schedule of the early return.

The [user-selected video](https://www.youtube.com/watch?v=lw0nj_XiUoU) is a
The Launch Pad rebroadcast, not the SpaceX channel. The retrieved 720p H.264
video track lasts 11,474.867 seconds, approximately 3 h 11 min 15 s, including
prelaunch and post-splashdown footage. Video time and flight duration differ.
No usable chapter list was supplied by its metadata.

Manual clock anchors are consistent with approximately
`mission_elapsed_seconds = video_seconds - 16`. Integer-second display
rounding and frame quantization imply approximately ±1 s, before event ambiguity.
This is a local alignment checked at sampled anchors, not an automatic proof
that every part of the video is uncut.

| Video position | Visible broadcast clock | Observation, not an inferred full flight state |
| --- | --- | --- |
| 00:01:00 | T+00:00:44 | Onboard ascent, 4.1 km and 900 km/h displayed |
| 00:25:35 | T+00:25:19 | Insertion interval; a single-engine indication appears |
| 00:35:00 | T+00:34:44 | Orbital cloud-deck view; this frame alone does not verify a payload release |
| 02:12:00 | T+02:11:44 | Single-engine deorbit footage; subsequent displayed speed decreases |
| 02:45:00 | T+02:44:44 | Luminous entry flow, displayed altitude 94.6 km |
| 03:08:36 | T+03:08:20 | Powered landing, displayed altitude 0.3 km |
| 03:08:47 | T+03:08:31 | Ocean camera; vehicle no longer visible above spray/horizon |

The final sequence supports a flight ending around **T+3:08**, rather than
equating the file's 3:11 duration to flight duration. Exact water-contact time
is obscured by spray, camera switching and rounded altitude. A video containing
water is not itself proof of a resolved touchdown contact state.

## Extraction that is already usable

`tools/extract_reference_frames.py` requires Python 3.11+ and FFmpeg/ffprobe.
It accepts a local video and a JSON plan.
It makes JPEGs at nominal seek times, an HTML gallery and an index with source
URL, source-file SHA-256, video time and separately labelled manual mission time.
It refuses mission alignment without clock evidence and rejects a different
source track when the plan pins its SHA-256. It neither downloads video
nor invents telemetry through OCR.

`STARSHIP_FLIGHT14_FRAME_WINDOWS_2026-09-30.json` selects 334 one-second
samples: initial ascent/hot staging, insertion, deorbit, entry illumination and
powered landing/ocean footage. A sparse five-minute scout of the complete
video identifies phases; denser event scouting narrows relevant intervals.
Local review images stay under ignored `exports/`, not in Git. The checked-in
plan pins the selected 720p video track by SHA-256; a different encoding needs
its own reviewed clock alignment and plan.

```bash
python3 tools/extract_reference_frames.py /path/to/reference.mp4 \
  docs/research/STARSHIP_FLIGHT14_FRAME_WINDOWS_2026-09-30.json \
  /tmp/flight14-frames
python3 tools/tests/reference_frame_extraction_test.py
```

Frames can calibrate cloud silhouette and coverage, sunlit versus shaded surfaces,
plume placement, horizon geometry and camera composition. Match camera type,
field of view, geographic location, altitude and exposure before judging a pair.
Ground cameras and onboard cameras cannot be compared as interchangeable views.
Glare, codec corruption, saturated plasma and lens droplets are not physical
radiance measurements. The video contains visibly corrupted or obstructed
onboard areas; reject them for cloud-colour calibration.

## What the existing simulator can support

| Requirement | Current foundation | Flight 14 work still needed |
| --- | --- | --- |
| Stage separation and engine faults | Staging, per-engine lifecycle, fault isolation/recovery | Independent B21/S41 variant and sourced fault identities/times; retain unknowns explicitly |
| Safe initial ascent then insertion | Full force integration, orbit elements, maneuver execution | Suborbital cutoff policy followed by a separate one-engine insertion; existing ordinary ascent targets parking orbit |
| Satellite deployment | `Vessel.DeployPayload` and `SimulationBridge.DeployPartAsVessel` preserve carrier/payload split kinematics | 26 payload subtrees, sourced masses/geometry and deployment sequence; no Flight 14 satellite catalog yet |
| Orbital coast then Pacific return | Kepler/RK4 propagation, planner, rotating atmosphere, entry guidance | Real landing-zone coordinates/uncertainty and a targeted early-return burn |
| Booster return | `BoosterReturnController` / `BoosterReturnGuidance` | Existing policy uses a 13-engine boostback and tower catch; Flight 14 needs a separate offshore profile and observed engine selection |
| Resource boundaries | LF and oxidizer inventories and mixture-ratio funding | Main/header routing and deliberately exhausting main LOX need separate validation; the current exhaustion branch drains residual resources together |
| Water arrival | Aggregate impact/splashdown contracts, historical capsule capability | Starship/booster water-contact policy and outcome evidence; current V3 files provide catch pins, not a validated Flight 14 splashdown model |
| Optical comparison | Real-framebuffer harness with phase captures | Mission-clock milestones, onboard camera placement and reference pairing across the full mission |

Relevant code: `scripts/HistoricalFlightProfileController.cs` currently supports
Mercury/Gemini/Apollo IDs; it has no Flight 14 controller. The closest V3 vehicle
data is explicitly Flight 12. Renaming that preset would not establish Flight 14
mass, engine, cargo or thermal accuracy. The earlier continuous V3 engineering
run reached the aggregate landing event; its headless dynamics evidence was
separate from real-renderer acceptance and did not validate stable water contact.

## Viability and implementation order

A continuous, physically integrated **engineering reconstruction is feasible**.
An exact trajectory/weather reconstruction from these images alone is not:
the footage does not uniquely identify mass history, thrust/Isp, throttle,
attitude, winds, inertial velocity frame, landing coordinates, optical exposure
or TPS heat distribution. Rounded speed should not be silently interpreted as
inertial or atmosphere-relative speed. Camera colours cannot determine density
or heat flux. Published values, measured HUD samples and calibrated assumptions
need separate provenance and uncertainty.

1. Finish the source register and annotate event intervals in one-second frames.
   Add observations only where the clock, vehicle and readout are unambiguous.
2. Define a separate Flight 14 vehicle/mission contract with measured versus
   estimated parameters, and explicit unknown payload/propellant quantities.
3. Implement ascent cutoff, insertion, payload splits and early deorbit as
   commands with physical state guards. Event times are comparison targets,
   never position/velocity assignments or fuel reseeds.
4. Add offshore booster/Ship targets and distinct water-arrival outcomes. Test
   resource accounting, momentum at each split, restart selection and entry
   attitude/control authority before tuning against observations.
5. Fly continuously under warp only where physically allowed; capture equivalent
   reference milestones on both renderers. A rendered optical fixture, telemetry
   trace and end-to-end mission pass are three different kinds of evidence.

Acceptance should require 26 detached payload vessels, a bound orbit following
insertion, a safe forecast before entry, physically delivered burn thrust,
bounded entry loads/thermal state and independently verified water outcomes for
both stages. Keep the legacy coupled-6DoF switch unchanged until its own parity
gates pass. Exact Flight 14 engine-failure and FTS behavior remains a separate
modeling requirement; visual resemblance cannot certify those systems.
