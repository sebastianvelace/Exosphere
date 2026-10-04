# Flight 14 independent Super Heavy return — 2026-10-03

Status: playable engineering estimate. Engine sequence and recovery mode are source-backed; the flown trajectory and exact event clocks are not reconstructed.

## Primary reference and interpretation

The [SpaceX Flight 14 post-flight account](https://www.spacex.com/launches/starship-flight-14%20) describes a directional flip, 31 operating boostback engines, intentionally depleted main-tank LOX, a landing sequence of 11 → 5 → 3 operating engines, a Gulf splashdown, and FTS activation after landing shutdown. This is an expendable ocean return, so the generic boostback/entry-burn/tower-catch controller is excluded from this preview.

The page's approximate timeline lists separation at T+142 s, boostback from T+147 to 187 s, and landing burn from T+396 to 421 s. These are comparison anchors, not verified video clocks; the same page retains the superseded nine-hour ship plan. A partial manual browser transcription is now recorded in [the booster reference anchors](../research/STARSHIP_FLIGHT14_BOOSTER_BROWSER_ANCHORS_2026-10-03.json). The original video track and these browser screenshots have separate provenance.

The [user-provided mission video](https://www.youtube.com/watch?v=lw0nj_XiUoU) shows the following rounded Super Heavy altitude displays:

| Broadcast clock | Booster altitude display | Engine board |
| --- | ---: | --- |
| T+6:33 | 4.1 km | Unlit |
| T+6:38 | 2.4 km | Lit |
| T+6:43 | 1.2 km | Lit |
| T+6:48 | 0.5 km | Lit |
| T+6:53 | 0.1 km | Lit |
| T+6:58 | 0.0 km | Lit |

These are manually read presentation anchors, not physical telemetry: datum, rounding and feed latency are unknown. They bracket a visible engine-board change at a few kilometres, whereas the current model ignites at 25.4 km. The right-hand speed display belongs to **Starship**, so it must not calibrate booster descent. A displayed 0.0 km does not establish contact, shutdown or FTS time. Exact engine counts and a complete boostback/landing clock reconstruction remain pending.

## Continuous physical authority

`Flight14LaunchDiagnostic.CreateWithBoosterReturn` starts with the original loaded pad stack in the moving-Earth universe. Separation hands the existing booster object and original engine states to an independent controller. Ship and booster receive commands at the same deterministic 20 ms pre-integration boundary. No return state is seeded, no velocity/orientation is assigned by guidance, and no rail or global integration-mode switch is added.

The detached aggregate originally lost control authority because it contained no command-category part. Its integrated avionics are now declared in the V3 part; this grants authority only when that intact part is its vessel root. The estimated +5.44 m aerodynamic-centre offset applies only to that standalone root, stabilizing engine-first descent. Attached-stack aerodynamic-centre selection is unchanged. This remains an aggregate static-margin estimate, not resolved grid-fin actuator aerodynamics.

The inherited **3,600,000 kg launch inventory is unchanged**. The profile partitions **200,000 kg protected landing propellant** at the inherited mixture ratio before liftoff. Main-feed consumption includes that reserve in wet mass but cannot draw it. Opening the landing feed creates no mass and does not repair failed engines; both save formats retain the partition. This is an engineering resource budget, **not a measured Flight 14 header-tank capacity, geometry, pressure or slosh model**. The corrected-guidance sensitivity runs found that a 60 t budget exhausts during braking, while 150 t and 200 t estimates reach water contact at approximately T+722 s and T+582 s respectively. The 200 t estimate is the current playable budget, not a fitted or published header capacity. A trial with 3.3 Mt initial loading returned the booster but displaced Starship outside its existing ocean envelope; that load change was rejected. The final controller keeps the original stack's fuel, oxidizer and mass. Protected inventory only changes which feed is available later.

The powered flip uses the centre cluster before the 31-engine boostback. A resource-budget-based estimated pointing direction is held through boostback; remaining main-feed LOX, rather than a reference timer, ends the burn. Descent uses the original gravity, atmosphere-relative aerodynamics and attitude actuation. Landing start depends on the descending state and available braking thrust; engine count decreases monotonically after delivered ignition is witnessed. The ignition gate accounts for available maximum braking acceleration and startup travel instead of imposing a fixed altitude on every descent. Throttle demand subtracts the aerodynamic deceleration already delivered by the production force model. Engine downselection retains capacity for the later low-drag portion of the burn: a transient high-speed drag peak cannot certify that three engines can complete the maneuver. This prevents both the early braking/climb and premature irreversible downselection observed during development. Throttle observes the installed minimum, and contact requires a bounded speed, upright attitude and actual Gulf-region crossing.

The existing opted-in water solver acts for three estimated seconds after nozzle contact. The terminal FTS record then retires the stage and hides its hull. Explosive energy and fragments are **not** simulated; the generic crash explosion must not hide Starship or invent an FTS blast. Water forces use the original hull geometry, mass centre and motion; flooding and waves remain outside the model.

## Observation and systems ownership

After separation, **SUPER HEAVY** switches the production chase camera, flight telemetry, engine board and observed-event timeline to the booster. **STARSHIP** returns to the continuing ship. Universe ownership and the ship's systems runtime remain with Starship: changing the camera is not a control-authority or consumable-runtime handoff. Ship systems are hidden while viewing the booster rather than presented as its own. Pad deluge, ejecta and the wide skirt fan are now gated by launch-site visibility and actual horizontal proximity (120 render units, approximately 336 m); sea-level return must not create another pad cloud offshore. Ocean spray remains driven by the existing landing/water visual controller. Uncrewed vehicles no longer receive a crew-load warning, although proper acceleration remains visible.

Observing the booster pauses at its terminal observation boundary. Returning to Starship releases only that automatic pause. A retired booster cannot be selected later as if its old inertial position were a current physical trajectory. Ship payload/deorbit/entry progression remains independently authoritative.

## Numerical evidence and known discrepancy

The continuous moving-Earth probe is reproducible with:

```bash
dotnet run --project tools/Flight14TrajectoryProbe/Flight14TrajectoryProbe.csproj -- data exports/flight14-booster-return-review --booster
```

The current local evidence is in `exports/flight14-booster-return-review/`: per-second telemetry, transition witnesses, summary, production-render captures and validation logs. The ignored export directory is not a versioned telemetry archive; this audit records the acceptance scope and discrepancies.

| Event | Propagated estimate | Published approximate comparison |
| --- | ---: | ---: |
| Separation | T+142.02 s | T+142 s |
| Boostback ignition transition | T+151.26 s | T+147 s |
| Main-feed exhaustion | T+173.42 s | T+187 s |
| Landing ignition transition | T+542.16 s | T+396 s |
| First water contact | T+581.92 s | T+421 s landing shutdown |

Water entry is approximately **4.13 m/s**, with 11/5/3 delivered thrust observed, approximately **81.7 t remaining propellant**, and an estimated body-fixed contact near **25.8050° N, 95.8598° W**. The booster reaches an estimated **207.0 km apogee**. These are simulation results, not measured Flight 14 values. The water event is about **161 s later** than the published landing-shutdown comparison; boostback exhausts its main feed about **14 s earlier** than that page's cutoff comparison. The landing burn starts at approximately **25.4 km**, far earlier in altitude than a reference reconstruction can currently justify. This increment establishes a bounded continuous return, **not timing, ascent-profile or landing-profile agreement**.

Separation retains the original stack result: approximately **46.36 km geodetic altitude** and **1.81 km/s atmosphere-relative speed**. Initial loading, tank partition, boostback attitude/budget, coast apogee and landing ignition must be calibrated together against actual telemetry/video. A protected 200 t feed is a major unvalidated assumption; a successful landing is not evidence that the flown booster used that inventory. Engine selection reproduces observed successful counts; unidentified ignition failures are not assigned invented engine identities.

## Validation scope and next work

Behavior checks cover original-object continuity, monotonically consumed inventory, reserve isolation and round trips, actual 31 and 11/5/3 delivered thrust, inherited engine failure, local booster failure without ship takeover, camera selection and pause boundaries, and the complete moving-Earth ship mission with both vehicles. The five booster behavior tests pass. Clean simulation/game/probe builds complete with zero warnings and errors. The full xUnit suite passes: **1037 passed, zero failed, zero skipped**. `bash tools/ci_check.sh` exits zero, including startup, main menu, construction scene and graphics-menu checks. Clean builds after capture-helper removal also pass. Real-framebuffer capture completes with seven milestones using the production camera/HUD and integrated graphics preset; the final frame verifies camera and telemetry return to the continuing Starship. Temporary capture helpers are removed. Snapshots are presentation checks, not reference-image acceptance.

The next fidelity pass should transcribe the real boostback/landing footage and compare altitude, atmosphere-relative velocity, burn duration and attitude against this trace. Grid-fin deflections and thrust steering need reference-constrained calibration. The completed production captures show a narrow landing plume, modest contact spray and no invented launch-pad fan offshore. They also show an unresolved distant surface silhouette and regularly spaced low-cloud coverage. The paused boostback capture displays THR 0% alongside 31 running engines and nonzero TWR; requested-throttle presentation versus delivered engine state needs an explicit follow-up check before broadcast telemetry fidelity is accepted. Near-water plume brightness, illumination, spray density and sea response still need footage-constrained review; a successful splashdown gate alone cannot establish resemblance to the flown vehicle.
