# Flight 14 Super Heavy guidance calibration — 2026-10-03

This increment follows [the independent return implementation](flight14_booster_return_2026-10-03.md). That audit records the earlier T+582 s / 207 km baseline; its numbers are historical evidence, not this revision's result.

## Reference and acceptance scope

Two additional paused frames of the [user-provided rebroadcast](https://www.youtube.com/watch?v=lw0nj_XiUoU) are manually transcribed in [the browser anchor file](../research/STARSHIP_FLIGHT14_BOOSTER_BROWSER_ANCHORS_2026-10-03.json): T+248 s displays 98.8 km and 1010 km/h for Super Heavy; T+308 s displays 78.5 km and 2510 km/h. Only the explicitly labelled left-hand booster speed display is used. The right-hand speed instrument belongs to Starship. Browser screenshot hashes identify the local evidence separately from the earlier downloaded video track. Display datum, rounding, velocity frame and feed latency are unknown.

The [SpaceX postflight account](https://www.spacex.com/launches/starship-flight-14%20) supports 31-engine boostback until main-tank LOX exhaustion, 11 → 5 → 3 successful landing engines, Gulf splashdown and subsequent FTS. Its approximate planned clocks do not prove measured ignition/contact timing. A 0.0 km broadcast readout does not certify contact.

## Physical cause and control change

The former boostback solver converted ideal delta-v left after its horizontal targeting estimate into upward thrust. With the inherited stage inventory, this produced a 207 km apogee and prolonged coast. The new attitude command bounds that upward component using an estimated coast-apogee parameter and finite-burn travel/gravity losses. For remaining burn duration t, start vertical speed v and local gravity g, the approximate end speed w solves `H = h + (v+w)t/2 + w²/(2g)`. The allowed upward impulse fraction is `(w-v+gt)/deltaV`, bounded to [0,1] and capped against the existing targeting direction. This is an open-loop steering estimate, not a hard altitude constraint or a reconstructed reference trajectory. Flip travel, variable mass/thrust, curvature and drag leave residual errors.

The current profile uses a 102 km guidance parameter, 6 km nominal landing arming altitude and 1.3 braking margin. These are explicitly estimated control settings. Stopping distance and startup travel can arm braking sooner than the nominal altitude. The braking margin reduces the demand before final monotone engine downselection; it changes thrust commands, not acceleration or velocity directly.

Horizontal damping now divides requested lateral acceleration by commanded thrust acceleration instead of gravity. The earlier gravity denominator overcommanded tilt during high-thrust braking: the same candidate reached water at 7.81 m/s, mostly lateral. Correcting the denominator reduces contact speed to 2.45 m/s. Existing attitude/gimbal response, lifecycle, pressure-corrected thrust, gravity and aerodynamic coefficients remain authoritative.

Neither total launch loading nor the protected 200 t landing resource partition changes. The partition remains a major unvalidated hardware assumption, not measured header-tank capacity. No pose, velocity or trajectory is assigned by guidance; no timed contact/shutdown is inserted. Ship ownership and its independent mission continue.

## Continuous moving-Earth evidence

Run the original loaded stack, without an entry seed:

```bash
dotnet run --project tools/Flight14TrajectoryProbe/Flight14TrajectoryProbe.csproj -- data exports/flight14-booster-calibration/final-candidate --booster
```

The probe now records commanded throttle alongside delivered engine counts. Original-stage witnesses:

| Event | Earlier baseline MET / height | This increment MET / height |
|---|---:|---:|
| Separation | 142.02 s / 46.36 km | 142.02 s / 46.36 km |
| Boostback main-feed cutoff | 173.42 s / 72.52 km | 174.18 s / 64.94 km |
| Coast apogee | 207.0 km | 96.95 km |
| Landing arming | 542.16 s / 25.39 km | 397.06 s / 5.99 km |
| Select five engines | 557.80 s / 5.64 km | 410.52 s / 0.409 km |
| Select three engines | 578.34 s / 0.126 km | 415.32 s / 0.042 km |
| Accepted water entry | 581.92 s | 418.38 s |
| Abstracted FTS retirement | 584.94 s | 421.40 s |

This probe's contact witness is 2.437 m/s; the following per-step sample prints 2.45 m/s. Contact is descending, near 25.8456° N, 96.7969° W, with approximately 125.4 t remaining propellant. The difference between a transition witness and the following integrated sample is intentional. These are simulation results, not flown measurements.

| Broadcast MET | Booster reference altitude / speed | Model altitude / atmosphere-relative speed |
|---|---:|---:|
| 248 s | 98.8 km / 1010 km/h | 96.64 km / 1236 km/h |
| 308 s | 78.5 km / 2510 km/h | 84.11 km / 2149 km/h |
| 393 s | 4.1 km, board unlit | 9.56 km, before delivered landing thrust |
| 398 s | 2.4 km, board lit | 5.25 km, landing commanded |
| 403 s | 1.2 km, board lit | 2.39 km, landing commanded |
| 408 s | 0.5 km, board lit | 0.84 km, landing commanded |
| 413 s | 0.1 km, board lit | 0.16 km, five selected |
| 418 s | 0.0 km, board lit | 0.003 km, three selected |

Height/time agreement improves substantially; exact reference reconstruction is NOT established. Boostback still ends earlier than the published planned cutoff, and the middle descent remains too high. Exact ignition/transitions, protected inventory and target location need joint calibration with resolved engine/fuel and grid-fin aerodynamics. Similar final clocks do not validate those assumptions.

## Validation

The moving-Earth behavior gate checks original-stage continuity, monotone consumed inventory, isolated reserve, delivered 31 and 11/5/3 thrust, descending landing without a climb, at least one second of delivered three-engine braking, low-speed regional contact and continuing Starship orbital insertion. Broad apogee/contact-time regression bounds reject the former trajectory without claiming reference measurement tolerances. Contract cases reject nonfinite apogee/braking settings, an apogee below landing altitude and margins outside [1,2]. Booster observation also checks the snapshot's vessel identity and actual commanded throttle.

The live boostback diagnostic verifies vessel throttle = HUD snapshot throttle = 0.6; there is no reproduced zero-throttle control fault in this revision. Broadcast telemetry is brighter/larger, and explicit numeric percent formatting avoids locale-dependent spacing. The diagnostic instrument state exposes commanded throttle separately from delivered engine-board brightness. The real scene also revealed that an earlier trajectory alert remained latched despite an already-supported controlled-descent phase. That phase now clears the latch; an uncontrolled-coast transition still raises it again, covered by a regression test.

Targeted booster checks pass: 11 cases. The production framebuffer sequence completes all seven milestones using the original loaded mission. Actual command and HUD snapshot agree at boostback (0.6), eleven-engine braking (0.4), three-engine braking (approximately 0.98) and cutoff (0). The refreshed gallery frame is T+418.84 s, after accepted contact; its 21 km/h display is subsequent water motion, not the 2.437 m/s transition witness. The stale coast alert is visibly cleared. Camera-return logging observes Starship continuing at T+471.42 s; that unpaused frame's HUD shows T+449 s. The helper sampled after the last rendered snapshot, so this frame proves camera return/continued ascent, not synchronized trajectory telemetry. The retained water gallery is captured while paused. Temporary helpers are removed and `project.godot` is unchanged.

`bash tools/ci_check.sh` exits zero: **1044 passed, zero failed, zero skipped**, with the test phase taking 24 m 55 s. Simulation, game and probe builds report zero warnings/errors. Flight startup, main-menu/construction smoke and graphics-menu checks pass. The full moving-Earth Flight 14 test includes the continuing ship's payload/return/water observation alongside the independent booster; it is a model-behavior gate, not reference-fidelity acceptance. The clean game build after helper removal also reports zero warnings/errors. All captured production source hashes still match the final code.
