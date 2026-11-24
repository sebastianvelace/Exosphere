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

### 1. Forward-flap hinge geometry

- **Data:** Li 2022 puts the forward hinge along the nose-cone generatrix. SpaceX moved
  the Block 2 forward flaps toward the tip, and NSF describes the move as up and leeward.
- **Model:** all four hinges are parallel to the body axis, and the V3 forward flaps sit
  on the barrel below the 14 m nose.
- **Effect:** each flap's moment about the belly normal is second order at its 45°
  neutral. The plate's force across the flow, C_N cos α, peaks there. The heading
  authority is ≈ 0.03 rad/s² at best, and the allocator saturates.
- **Trial results:**
  - A leeward hinge azimuth was tried and reverted.
  - A forward pair moved onto the ogive (hinge radius 4.0 m), with the hinge tilt derived
    from the ogive (≈ 10°), gives a clean heading channel of the right sign: ±0.03 rad/s².
  - Holding a heading still fails: the roll channel dies when trim saturates the plates.
  - Experiment kept in a stash; not merged.
- **Work:**
  1. Place the forward flaps from imagery.
  2. Re-check the heading channel once trim no longer saturates the plates (item 2).

- **Measured V3 geometry, now available:**
  - **Source:**
    [data/v3_ship_geometry_photogrammetry.json](data/v3_ship_geometry_photogrammetry.json),
    ±0.6 m and ±5°.
  - **Station:** the forward flaps sit on the ogive, centre about 7.6 m above the nose base.
    The model has them about 1.75 m below it, roughly 9 m further aft.
  - **Hinge and plane:** hinge tilted about 19°; azimuth about 98° and 262°; planes turned
    14° leeward; swept-trapezoid planform. The area, about 14 m², already matches the
    model's 14.5 m².
  - **Ship length:** the model stack totals 54.0 m (engines 4.5 + tank 30.5 + command
    19.0), against the published 52.1 m from skirt edge to tip. The definition of the
    datum needs checking.
- **Work:** move and reshape the forward flaps from these data. The plate model needs:
  - a hinge tilt, already prototyped;
  - a hinge azimuth (prototyped and reverted for heading, but justified here by geometry);
  - a plane rotation;
  - a non-rectangular planform, or at least the centroid and area of the trapezoid.

  Then re-measure the trim range, the heading authority and every Flight 14 and Flight 15
  gate.

### 2. Trim-feasibility check (bug, fixed, not merged)

- **Bug:** `Flight14DescentController.TrimFeasibleAngleOfAttack` tested the moment about
  body X. In a belly-first −X attitude the angle-of-attack moment acts about body Z, and
  the X moment is exactly zero up to rounding, so feasibility was decided on the sign of
  rounding noise.
- **Fix:** use the moment about body axis × flow. The limit becomes physical: 71° minimum
  with full deflection, and higher angles are kept.
- **Consequence:** the Flight 14 historical gate fails.
  - Residuals: +6.3 km at T+10369, +7.2 km at T+10541, +11.8 km at T+10961.
  - Previously: +0.1 km, +0.7 km and −2.2 km.
- **Not yet explained:** both runs fly about 60–61° through the hypersonic phase. The
  difference is not the angle of attack; the descent stays higher from the first minutes.
  The next step is to trace bank and lift direction side by side.
- **Status:** fix uncommitted; recalibration in progress.

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

### 6. Raptor 3 gimbal range

- **Data:** ±15° (reported, not tied to a version, not SpaceX-published).
- **Model:** 6° per axis at 10°/s, provenance `estimated` (±2°).
- **Effect:** the catch burn saturates the gimbals (8.5° combined is 6° on each axis), so
  the flip and braking authority may be understated by more than half.
- **Work:** find a version-specific or primary figure before changing the model, then
  re-run the catch benches.
- **Data:** [data/raptor3_reference.json](data/raptor3_reference.json).

### 7. Data still missing

- Flap chord, span, area and hinge station of the V3 forward flaps (imagery).
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
- **Terminal ballistic ratio.**
  - Requirement: the steady belly-flop ratio m/(C_D S) between 2 and 12 km lies in
    491–649 kg/m².
  - Source: IFT-4/5/6 webcast OCR, `data/ift_terminal_descent_telemetry.json`.
  - The flights' own spread sets the band. Mass and drag area are not separable.
- **Terminal speed.**
  - Requirement: within ±10 % of 363 km/h at 1.2 km, for the modelled mass.
  - Source: Flight 14 broadcast. Already a gate.
