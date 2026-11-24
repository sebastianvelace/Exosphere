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
- **Work:** lower the ignition toward the published profile. That needs the flip and the
  side channel to fit into it, which depends on items 1 and 6.

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
  more robust lateral catch (item 5).
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
