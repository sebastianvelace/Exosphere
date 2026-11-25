# Exosphere against the Starship aerodynamics data

Status on 2026-11-23. The model measurements come from the Flight 15 glide bench
(`Flight15CatchBench`, game state saved at 4 km, 215 t, X_cg ≈ 0.55 L from the nose), unless
noted. The measurement history is in
[../../audits/flight15_capture_and_gpu_2026-11-19.md](../../audits/flight15_capture_and_gpu_2026-11-19.md).

## Agreement

| Quantity | Data | Model | Note |
| --- | --- | --- | --- |
| Subsonic belly-flop attitude | near 90° (Li 2022; Chen 2025) | descent law flies 80° | inside the trim range |
| Subsonic trim range | Ma 0.8, X_cg 0.58: 65–89° on the forward pair, up to 120° with the aft pair (Li) | ≈ 71–77° with full deflection, beyond 100° at the other end | consistent within the geometry and X_cg differences |
| Lift near 90° | C_L ≈ 0 at 90–95° (Li Ma 0.8; Chen S31) | +0.35 m/s² along the nose at 80°, −1.1 at 90° | crosses zero between 80° and 90° |
| Roll versus sideslip | C_lβ < 0, roll-stable (Li) | restoring, 2.2 rad/s² per radian; natural frequency 1.47 rad/s | sign agrees |
| Sideslip side force | not reported | ±0.2 m/s² at ±15° | expected for a round hull |
| Broadside drag regime | transcritical crossflow C_d ≈ 0.5–0.7 (Achenbach; Re ≈ 6–8 × 10⁷) | Jorgensen transcritical | Li's C_D ≈ 10 on base area (≈ 640 m²) does not state its Re |
| Flight 14 terminal speed | 363 km/h at 1.2 km | 101.7 m/s scaled to 1.2 km (historical gate) | agrees |
| Terminal ballistic ratio m/(C_D S) | IFT-4/5/6, steady 2–12 km: 491–649 kg/m², with flight means 520–614 (webcast OCR) | ≈ 600 kg/m² (215 t, drag area ≈ 358 m² at 4 km) | inside the flown range |
| Landing-burn duration, about 93 m/s to below 8 m/s | 11.7, 15.9 and 14.5 s (IFT-4/5/6 ocean landings); Flight 15 catch about 18 s from ignition | single-burn catch about 38 s from ignition, including a 6 s flip | the catch spends its extra time on the side overshoot |

## Discrepancies and open items

### 1. Forward-flap hinge geometry (applied 2026-11-24)

- **Data:** [data/v3_ship_geometry_photogrammetry.json](data/v3_ship_geometry_photogrammetry.json),
  ±0.6 m and ±5°. The V3 forward flaps sit on the ogive: hinge line z 43.3–48.6 m (r 4.0–2.2 m),
  leaning about 19° toward the axis, at azimuth 98°/262°, planes turned 14° leeward, swept
  trapezoid of 13.5 m². Li 2022 also hinges the forward pair along the nose-cone generatrix.
- **Before:** hinges parallel to the axis at 90°/270°, centre 1.75 m below the nose base
  (about 9 m too far aft), 5.7 × 2.55 m rectangle. Each flap's moment about the belly normal
  was second order, so turning about body X (heading, and the X share of a bank about the
  flow at α 60°) had almost no authority.
- **Model now:** `hinge_tilt_deg` 18.8 (`BodyFlapGeometry.HingeDirection`), hinge radius
  3.47 m, and a rectangle (4.3 × 3.14 m) with the measured trapezoid's area and centroid,
  7.23 m below the tip. The ~3 t of hinge hardware moved with the flaps (dry centre −2.07 →
  −1.40 m). Provenance in `data/provenance/rigid_body_fidelity_2026.json`.
- **Not modelled yet:** the 98°/262° hinge azimuth, the 14° leeward plate turn and the
  trapezoid's sweep. The 54.0 m model stack against the published 52.1 m is unresolved.
- **Effect on Flight 14:** see item 2. The bank now follows its reference within about 1°
  through the hypersonic entry, with no flap saturation.

### 2. Trim-feasibility check (bug, fixed 2026-11-24)

- **Bug:** `Flight14DescentController.TrimFeasibleAngleOfAttack` tested the moment about
  body X. In a belly-first −X attitude the angle-of-attack moment acts about body Z, and
  the X moment is exactly zero up to rounding, so feasibility was decided on the sign of
  rounding noise.
- **Fix:** use the moment about body axis × flow.
- **Why the Flight 14 gate first failed (+6 to +12 km):** the old limiter returned a
  random angle between about 52° and 67° almost every step. That jitter kept disturbing the
  attitude and hid a missing control channel. With the fix the commanded angle stays at the
  scheduled 59.5°, and at T+10060 s (85 km, q ≈ 220 Pa) the corridor guidance asked for a
  bank of −99°. The forward flaps saturated in differential (90°/0°), yet the bank drifted
  back toward 0° at about 0.1°/s: with parallel hinges they could not turn the body about
  X. The ship flew lift-up for about 380 s at 81–85 km and stayed 6–12 km high.
- **With the measured flaps (item 1):** the bank tracks the reference and the anchors fall
  inside the 3 km band (see the historical gate output in the commit).
- **Regression tests:** `Flight14TrimLimiterTests` (insensitive to a 1e-9 rad change of the
  lift reference; the limited angle is itself trimmable) and
  `BodyFlapAerodynamicsTests.ATiltedForwardHingeTurnsTheBodyAboutTheBellyNormal`.

### 3. Reference angle of attack in the terminal glide

- **Problem:** the EDL catch glide used the hypersonic 70°, which cannot be trimmed at catch
  mass. The game's demonstration attitude hid this by writing the orientation
  kinematically.
- **Data:** a terminal phase near 85–90° with C_L ≈ 0 (Li; Chen).
- **Work:** fly the terminal glide on the physical descent law.

### 4. Heading guidance architecture

- **Data:** heading is turned by bank-to-turn about the velocity vector at higher speed
  (Li). Near 90° there is little lift, so the terminal phase is nearly ballistic.
- **Model:** the catch glide steers by pointing the nose toward a heading, using the
  weakest axis.
- **Work:**
  1. Do the lateral targeting while lift exists (α 60–70°, faster flight).
  2. Predict a near-ballistic terminal phase.
  3. Use heading only to point the nose at the tower before the flip.

### 5. Catch-burn ignition altitude

- **Data:** the SpaceX-supplied landing profile in the FAA LC-39A EIS applies thrust over
  about the last 550 m. Flown ocean landings take 11.7–15.9 s from about 93 m/s (IFT
  telemetry). Flight 15's catch took about 18 s.
- **Model:** the single-burn catch lights at about 1,425 m of pin clearance at about
  115 m/s and takes about 38 s.
- **Why the model's catch is marginal (2026-11-24 trace, 60 Hz ignition replay):**
  - It lights 1,124 m short of the cradle, and the flip throws the ship toward it: the
    horizontal speed goes from 33 to 87 m/s by flip end, 790 m above the pins.
  - Upright at 50–85 m/s sideways and 30–85 m/s down, the hull meets the air at roughly
    45–70° of crossflow. Holding the tilt the braking needs (about 28°) takes more moment
    than the Raptors give. From two engines on, both gimbals sit at their 8.5° limit (6° per
    axis), the attitude command saturates, and the tilt stays at 10–15° while the aim error
    grows to 55°. The ship crosses the cradle at about 33 m/s some 200 m above it.
  - A 15° gimbal is not a drop-in fix: with the attitude gains tuned for 6° the loop gain
    rises 2.5× and the ship tumbled (97° tilt at 15 s) in all four replays.
  - The flown profile avoids the problem rather than overpowering it: thrust only over the
    last ~550 m, igniting nearly over the cradle, so little sideways speed has to be
    removed upright.
- **Flown flip shape (IFT-4/5/6 webcast speeds):** total speed rises only 0–9 m/s as the
  flip starts, then falls at about 10–11 m/s² for 6–7 s (from about 93 to 17 m/s). In the
  model it stays at 120–135 m/s through the whole 6 s flip: three Raptors idle at their
  40 % floor while the axis still lies across the fall, which throws the ship about
  54 m/s sideways and brakes nothing.
- **Flip experiments (2026-11-24, stashed, not merged):**
  - Throttling up as the axis nears retrograde changes nothing: the flip ends near vertical,
    while the retrograde direction leans 30–45° away from the tower because of the throw.
  - Pitching up on the flaps first and lighting the engines at 70° from vertical cuts the
    throw from about 54 to 22 m/s (87 → 55 m/s sideways at flip end on the replays). The
    flaps alone cannot bring the axis below about 55–70° from every state; below that the
    flip stalls.
  - Attitude gains and roll damping only reshuffle which replay catches; none holds the aim
    (errors of 30–80° in every case).
- **The glide is the bottleneck:** on the physical belly flop from 4 km
  (`glide_4km`, Flight 15 descent law) the glide guidance's closing speed and ignition lead
  change nothing. The ship reaches ignition about 1,290 m from the cradle with the nose 36°
  off the tower line in every run. There is too little lift at that height to move it
  1.3 km.
- **Physical flight from the chapter's 70 km entry (2026-11-24):** the game seed (171.7 km
  upstream, 2.1 km south, 1.8 km/s) is calibrated to the kinematic glide. Flown on the
  physical Flight 15 descent law, the ship reaches 4 km 28.6 km short and 5.2 km south of
  the cradle. From this suborbital state the entry is nearly ballistic. A fixed bank of
  0–150° for the whole entry moves the 4 km point by only about ±1–2 km, and the
  footprint guidance does no better than a fixed bank. A seed of 143.1 km upstream and
  3.1 km north reaches 4 km within 0.1 km downrange and 0.5 km crossrange, nearly at rest
  horizontally; a 2 km seed error stays a 2 km miss.
- **Terminal attitude is the next blocker:** with that seed and ignition armed below
  4 km, the ship lights 290–880 m from the cradle depending on the frame rate. The nose
  points anywhere (cosine to the tower line from −0.54 to 0.94), so the flip takes 12–17 s
  instead of 6, and the flap-first flip stalls. The physical belly flop does not hold the
  heading and belly-down roll that a pitch-plane flip needs.
- **Physical terminal glide prototype (2026-11-24, `stash` "physical catch glide
  prototype", not merged).** Measured on the full chain from the 70 km entry:
  - **Trim and roll.** At catch mass the flaps cannot trim below about 83°. The descent
    schedule's 76–81° drove them to their stops, and the hinge-lean coupling then rolled
    the ship 20–45° off belly-down. Flying the model's zero-lift angle (about 83–85°, lower
    than the 85–95° of Li 2022 and Chen 2025) keeps the roll near 0°.
  - **Heading.** The descent's single 6° reference-lead limit is shared by all axes. An
    uncorrectable pitch error used all of it and dragged the heading reference along with
    the ship, so the nose wandered through 180°. Allowing 45° of lead in the terminal glide
    holds the heading to a cosine of −1.00 with the roll near 0°.
  - **Along-track authority.** Between the zero-lift angle and about 100° the angle of
    attack acts as a throttle toward the tail. That gives a steady drift of 13–15 m/s
    (L/D ≈ 0.1), about 1–1.5 km of reach from 13 km. Toward the nose there is almost no
    authority, because the trim floor is close to the zero-lift angle.
  - **Entry seed.** 143.1 km upstream and 0.05 km south reaches 12 km within about 50 m of
    the cradle.
  - **Dispersion.** The ignition point is chaotic. Shifting the seed by 250 m, or changing
    from 50 to 60 Hz, moves it by 40–1,170 m, more than the terminal reach can absorb.
    A robust catch needs footprint guidance with real authority (a predictor-corrector in
    the supersonic phase), not seed tuning.
  - **Flip on the flaps alone.** It cannot complete: the aerodynamic trim reaches only
    about 100–120° (tilt 70–80°), and a pitch command on the flaps excites the
    1.5 rad/s roll/sideslip mode.
  - **15° gimbal with the catch gains scaled by 6/15** (same loop, more headroom):
    - the flip takes 4.4 s at both frame rates;
    - the braking removes the 46–58 m/s throw in about 5 s, close to the flown
      10–11 m/s²;
    - with a lateral-vertical timing coupling (the descent slowed until the bang-bang
      lateral time fits), 3–4 of 8 configurations catch, in 70–120 s.

    Applied globally, the range breaks the Flight 14 landing burn, whose gains are not
    scaled, and the two game ignition replays.
- **Footprint guidance (2026-11-24, prototype v2 in `stash`, not merged).**
  - **Where the dispersion comes from.** With the recalibrated seed the ship is within
    about 50 m of the cradle at 12 km at both frame rates. The 40–1,170 m ignition
    dispersion is made by the terminal glide law itself, not by the supersonic phase.
  - **The actuator acts one way and turns slowly.** Real side force exists only toward
    the tail. The ship gets about 1 m/s² once the flaps actually trim 97–103°; between
    6 and 10 km they reach only 85–96°, so the authority there is a fraction of that.
    The descent's reference slew of 0.05 rad/s takes about 50 s to swing the nose round.
  - **Designs tried:**

    | Design | Result |
    | --- | --- |
    | 2D, tail along the wanted acceleration, with an integral on the measured tailward acceleration | Diverges when the wanted direction reverses, because the nose must turn 180° |
    | Nose kept in a ±30° cone about the axis, aim biased beyond the ignition point | Catches 4 of 6, in 60–117 s |
    | Axis from cradle to ship, ignition on the far side, faster slew | Catches 0 of 6 |
- **Terminal-glide identification (2026-11-24).** Full record in
  [the Flight 15 audit](../../audits/flight15_capture_and_gpu_2026-11-19.md#terminal-glide-identification-bench-2026-11-24).
  - **Drift plant.** The angle of attack sets a drift speed along the nose (the belly
    flop glides like a sailplane): about 2.4 m/s per degree around 88.5°, lag about 8 s,
    from −15 m/s at 83° to +29 m/s at 101°.
  - **Guidance and its limit.** Drift-speed guidance designed on that plant misses by
    40–640 m from dispersed 12 km states. The limit is the inner attitude loop: 10–15 s of
    angle-of-attack lag, about 8 s of heading delay, and ±20–40° roll excursions.
- **Terminal attitude loop and drift guidance (2026-11-24, in `TowerCatchPhysicalGlide`).**
  Full record in
  [the Flight 15 audit](../../audits/flight15_capture_and_gpu_2026-11-19.md#terminal-attitude-loop-and-drift-guidance-2026-11-24).
  - **Static plant.** At 12 km the flaps trim from 80° to 104°, with margins of about
    ±0.08–0.1 rad/s² in angle of attack, ±0.04 in heading and ±0.8 in roll. The descent
    law's oscillations came from two things:
    - the torque-space flap allocation, which gives up roll first under saturation;
    - step references beyond that margin.

    Neither was a limit of the plant.
  - **Loop.**
    - Allocation in angular acceleration with roll first.
    - Angle and heading references shaped inside the margin, with feedforward.
    - A conditional integral on the angle error.

    Steps of 3–5° settle inside 1° in 1.1–2.5 s at 50, 60 and 120 Hz, with the roll under
    4.4°. The first steps after takeover still take 3.4–5.7 s.
  - **Drift plant with this loop.** α₀ ≈ 89.0°, and τ ≈ 13 s ≈ V/g. It is an acceleration
    along the nose of c (α − α₀) − v g/V, with c ≈ 0.175 m/s² per degree, usable from 80° to
    102°.
  - **Guidance.** Zero-effort miss to the ignition point on the lead circle, with the approach
    axis following the bearing to the cradle and the far side chosen when it is faster. From
    ±600 m / ±300 m at 12.5 km it misses the ignition point by at most 29 m at 50 Hz and
    22 m at 60 Hz, across 15 states each. At 120 Hz 14 of 15 are inside 50 m (the
    largest miss is 84 m).
  - **Not yet done.** The ship arrives with up to 26 m/s of horizontal speed. The game chapter
    still writes the glide attitude.
- **Work:**
  1. Retune every Raptor 3 ship controller for a 15° gimbal (item 6), then adopt it.
  2. Light near overhead, nose toward the cradle; brake the throw on the 15° gimbal.
  3. Lower the ignition toward the flown profile (this item), then move the pins (item 8).
  4. Fly the physical glide in the game chapter and recalibrate the entry seed.

### 6. Raptor gimbal range (all variants)

- **Data:** ±15° on two axes. Several secondary sources agree, and SpaceX footage shows a
  15° firing. It is not SpaceX-published as a specification, and no source gives a rate.
- **Model:** every Raptor variant gimbals 6–7° (Raptor 3 ship: 6° at 10°/s), provenance
  `estimated` (±2°).
- **Effect:** the catch burn saturates the gimbals (8.5° combined is 6° on each axis), so
  the flip and braking authority may be understated by more than half.
- **Work:** find a version-specific or primary figure before changing the model, then
  re-run the catch benches.
- **Data:** [data/raptor3_reference.json](data/raptor3_reference.json).

### 7. Super Heavy descent drag

- **Data:** IFT-4/5/6 give an unpowered ratio m/(C_D S) of 2,200–2,800 kg/m² between 20
  and 2 km, with a peak of 4.5–5 g. With the about 250 t catch mass Musk quoted, that
  implies C_D ≈ 1.4–1.8 on the base area.
- **Model:** `super_heavy_v3_booster_flight12.json` flies an axial C_D of 1.00
  (calibrated estimate) and has no explicit grid fins. At 250 t that gives about
  3,900 kg/m², a booster that brakes less than observed, unless its return mass is higher.
- **Work:**
  1. Measure the model's ratio on the booster return bench.
  2. Compare with the flown band.
  3. Consider grid fins (NASA Orion grid-fin data) if the drag deficit is real.

### 8. Catch pin station

- **Data:** the V3 photogrammetry puts the two catch pins at z 39.0–39.9 m (12.2–13.1 m
  below the tip) at 90°/270°, standing 0.44 m proud, with the forward flaps' lowest corners
  about 2.5 m above them. The Flight 15 reporting places the fittings directly under the
  forward flaps.
- **Model:** `catch_pin_offset_y_m` −9.5 puts them 19 m below the tip, at the base of the
  command section. That station followed the old, too-far-aft forward flaps; since the flaps
  moved to their measured station the pins no longer sit under them.
- **Trial (2026-11-24):** at the measured station (−3.15 m) the 50 Hz ignition replay still
  catches (48.7 s), but the 60 Hz one ends in a missed approach at 22 s. The cradle rises with
  the pins, so the ignition clearance is unchanged (1,425 m) and the two runs differ by only
  0.1 m at 7 s. The difference grows because the lateral channel is marginal: with either pin
  station the ship crosses the cradle at about 33 m/s some 200 m above it. The pins wait for a
  more robust lateral catch (item 5, which records why it is marginal).
- **Work:** make the lateral channel robust, then move the pins to the measured station and
  re-run the catch benches, the replays and the default chapter.

### 9. Data still missing

- Per-degree flap effectiveness and lateral-directional derivatives (figures of Li 2022;
  full text of Zhang 2021).
- The reference area of the Chen 2025 coefficients.
- Starship V3 landing mass and centre of mass. Only estimates exist (75–155 t dry).
- Any primary terminal telemetry beyond the Flight 14 broadcast and timelines.

## Candidate gates

These come from the datasets in `data/`:

- **Subsonic trim range.**
  - Requirement: the trim angle with the forward pair alone lies in 65–89°, and aft
    deflection extends it beyond 100°.
  - Source: Li 2022, Ma 0.8. Compare with a stated X_cg offset.
- **C_L sign change.**
  - Requirement: C_L changes sign between 85° and 100° in the subsonic belly flop.
  - Sources: Li 2022; Chen 2025.
- **Roll stability.**
  - Requirement: the roll moment from sideslip is restoring, with C_lβ < 0.
  - Source: Li 2022.
- **Forward-flap heading authority.**
  - Requirement: first-order and of the right sign in both directions.
  - Source: Zhang 2021, qualitative.
- **Entry corridor.**
  - Requirement: speed at 50, 40, 30 and 20 km within the IFT-4/5/6 spread, widened by
    the display resolution: altitude ±0.5 km; for speed, about ±5 % at 50 and 40 km and
    about ±10 % at 30 and 20 km.
  - Source: `data/ift_entry_corridor.json`. Apply it only to entries from a comparable
    suborbital trajectory: entry speed about 26,700 km/h at 120 km.
- **Super Heavy catch burn.**
  - Reference: the IFT-5 landing burn starts at about 1,200 km/h near 1 km, decelerates at
    about 47 m/s² for about 6 s, then runs a gentle phase of about 14 s to zero.
  - Use it for the shape of the booster catch, not as a pass band, since the booster
    version differs.
- **Super Heavy descent ratio.**
  - Requirement: an unpowered booster m/(C_D S) between 20 and 2 km in 2,000–3,000 kg/m².
  - Source: `data/ift_booster_descent.json`.
- **Terminal ballistic ratio.**
  - Requirement: the steady belly-flop ratio m/(C_D S) between 2 and 12 km lies in
    491–649 kg/m².
  - Source: IFT-4/5/6 webcast OCR, `data/ift_terminal_descent_telemetry.json`.
  - The flights' own spread sets the band. Mass and drag area are not separable.
- **Terminal speed.**
  - Requirement: within ±10 % of 363 km/h at 1.2 km, for the modelled mass.
  - Source: Flight 14 broadcast. Already a gate.
