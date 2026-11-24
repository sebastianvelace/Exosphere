# Annotated sources: Starship aerodynamics and control

Accessed 2026-11-23 unless noted. Each entry gives its confidence level (see README),
what it supports, what it does not, and its use in Exosphere.

## Starship-specific aerodynamics

### Li Z., Zhang L., Li L., Huang B., Yuan H. (2022). Performance and characteristics analysis on the Starship aerodynamic configuration

- **Where:** Acta Aerodynamica Sinica 40(5):1–14, DOI 10.7638/kqdlxxb-2022.0011;
  full text in Chinese at <https://html.rhhz.net/KQDLXXB/2022-05-1.htm>.
- **Confidence:** `peer-reviewed-cfd`. Steady RANS with SST, Roe flux, MUSCL; ~2 M cells;
  turbulence model validated on the S809 airfoil. The geometry comes from public
  information.
- **Supports:**
  - Geometry: 50 m × 9 m, about 18 m span. Four "dihedral" flaps: the forward hinge
    follows the nose-cone generatrix and the aft hinge is parallel to the body axis.
    Deflection 0–80°.
  - Conditions: Ma 12, 2 and 0.8, α 0–120°. X_cg = 0.58 L from the nose; S_ref is the
    base area; Iy/Ix = 7; return mass 140 t.
  - Trim, longitudinal and lateral-directional results (values in the data file):
    - trim ranges by Mach;
    - C_L ≈ 0 at 90° subsonic;
    - roll-stable (C_lβ < 0);
    - statically directional-unstable (C_nβ > 0), but stable on the dynamic criterion at
      Ma 0.8 for α 65–120°.
  - Heading is turned by bank-to-turn, rotating about the velocity vector. Profile:
    hypersonic at 60–70°, subsonic near 90°, and the terminal phase on engines.
- **Does not support:** flap chord, span or area; Reynolds number; per-degree flap
  effectiveness; numeric C_lβ and C_nβ, which are in figures only; yaw damping;
  time-domain control.
- **Use:** trim and stability gates for the subsonic belly flop; forward-hinge geometry;
  guidance architecture (bank-to-turn, near-ballistic terminal phase).
- **Data:** [data/li2022_starship_aero_layout.json](data/li2022_starship_aero_layout.json).

### Zhang Y., Zuo G., Xu Y., Du R., Zhao F., Qu F. (2021). Numerical simulation on aerodynamic characteristics of new type control surface of Starship

- **Where:** Acta Aeronautica et Astronautica Sinica 42(2):624058,
  DOI 10.7527/S1000-6893.2020.24058;
  abstract at <https://hkxb.buaa.edu.cn/EN/abstract/abstract18170.shtml>.
- **Confidence:** `peer-reviewed-cfd`, but only the abstract was read.
- **Supports:**
  - The aft pair drives lift, and pitch and roll almost linearly, with yaw coupled.
  - The forward pair drives drag and, mostly, yaw.
  - At α > 90° all three moments still respond to both pairs. The English abstract calls
    conventional flaperon control "basically ineffective"; the Chinese abstract only says
    it has a high failure probability.
- **Does not support:** any numbers, flow conditions, or a per-axis split at high α.
- **Use:** qualitative check that the forward flaps carry the yaw (heading) channel.
  Exosphere's flaps have only second-order heading authority.

### Chen L., Qin T., Huangfu Z., Li L., Wei W. (2025). Optimization of flip-landing trajectories for Starship based on a deep learned simulator

- **Where:** arXiv:2508.06520, <https://arxiv.org/abs/2508.06520>.
- **Confidence:** `preprint`. The coefficient curves are `digitised`.
- **Supports:**
  - S31-prototype RANS (Fluent, ~4 M cells), incompressible (Ma < 0.3). Forward flaps
    deployed flat, aft flaps folded to 80°. α 0–350° in 10° steps.
  - C_L, C_D and C_M curves.
  - Vehicle table: L_ref 50 m; CG 0.60 L from the nose; J_z 1.25–3.2 × 10⁷ kg·m²; mass
    135 t wet, 120 t dry. Terminal state at the start of the flip: 18.8 m/s horizontal and
    106.7 m/s down.
  - Flip 2.4 s maximum; 2-D planar dynamics; gimbal ±10°.
- **Does not support:** reference area (not stated); flap deflection variation;
  lateral-directional data.
- **Use:** shape of the subsonic C_L, C_D and C_M curves for one fixed flap set; the order
  of magnitude of the flip entry state.
- **Data:** [data/chen2025_s31_subsonic_rans.json](data/chen2025_s31_subsonic_rans.json).

### Chen L., Qin T. (2026). Saturation-aware robust trajectory optimization for reusable launch vehicles via differentiable physics

- **Where:** arXiv:2607.09736, <https://arxiv.org/abs/2607.09736>.
- **Confidence:** `preprint`. Only the abstract was read; the PDF exceeds the fetch limit.
- **Supports:** the high-α flip is limited by actuator saturation and aerodynamic
  uncertainty. This is the same group as the paper above.
- **Use:** pending a full read.

## Starship design and flight observations

### Block 2 forward-flap relocation (Flight 7)

- **Where:**
  - SpaceX, via Spaceflight Now:
    <https://spaceflightnow.com/2025/01/16/live-coverage-spacex-to-launch-its-starship-rocket-on-a-7th-suborbital-test-flight-from-starbase/>.
  - NASASpaceflight: <https://www.nasaspaceflight.com/2025/01/starship-flight-7-block-2/>.
- **Confidence:** `primary` (SpaceX statement) and `secondary` (NSF).
- **Supports:** the forward flaps were made smaller and moved toward the tip, away from the
  heat shield. NSF adds that the Block 1 flaps were 180° apart and that the Block 2 forward
  flaps moved up and leeward.
- **Does not support:** the size of the shift.

### Flight timelines and broadcast values

- **Where:**
  - Wikipedia flight pages (Flight 5, Flight 11).
  - The Flight 14 broadcast anchors, held in the repo with frame hashes.
  - The Flight 15 burn timeline as reported in the press and recorded in
    `data/flight_profiles/starship_flight15_catch_burn_estimate.json`.
- **Confidence:**
  - Flight 14 anchors: `primary` (broadcast frames), with the altitude datum unspecified.
  - Timelines: `secondary`.
- **Data:** [data/starship_flight_observations.json](data/starship_flight_observations.json).

### IFT-4, IFT-5 and IFT-6 webcast telemetry (OCR)

- **Where:** SofieBrink, StarshipTelemetryExtractor (MIT licence),
  <https://github.com/SofieBrink/StarshipTelemetryExtractor>, `Examples/`.
- **Confidence:** `secondary`. This is a third-party OCR of the primary broadcast
  telemetry, with automatic gap filling and outlier removal.
- **Supports:**
  - Per-frame Starship altitude (1 km display resolution) and speed (1 km/h) through the
    terminal belly flop, the flip and the landing burn of three flights.
  - Measured steady ballistic ratio m/(C_D S) between 12 and 2 km:

    | Flight | Range | Mean |
    | --- | --- | --- |
    | IFT-4 | 562–649 kg/m² | 614 kg/m² |
    | IFT-5 | 491–538 kg/m² | 520 kg/m² |
    | IFT-6 | 536–582 kg/m² | 566 kg/m² |

  - Landing burn from about 93 m/s to below 8 m/s: 11.7 s (IFT-4), 15.9 s (IFT-5) and
    14.5 s (IFT-6).
- **Does not support:**
  - Mass and C_D·S separately.
  - Attitude or flap positions.
  - Mission-elapsed time: the time is video time.
- **Use:**
  - A historical gate on the model's terminal ballistic ratio.
  - A reference duration for the landing and catch burn.
- **Data:**
  - [data/ift_terminal_descent_telemetry.json](data/ift_terminal_descent_telemetry.json);
  - reduced CSVs in `data/ift_terminal_descent/` with `ATTRIBUTION.md`;
  - analysis in `scripts/ift_terminal_descent.py`.

Other telemetry sources checked:

- shahar603/Telemetry-Data covers Falcon 9 to 2020. It also holds the telemetry of the 2017
  BFR EDL animation, which is an older design concept.
- The Launch Dashboard API has no Starship data.
- chrisjbillington/starship_telemetry and osajus/starship_telemetry cover early IFT ascent
  or tools only.

### Flap actuation

- **Where:** Teslarati and Inverse reports; a May 2026 V3 summary on Slashdot, citing
  Teslarati.
- **Confidence:** `secondary`.
- **Supports:**
  - The flaps are driven by electric actuators with Tesla drive motors and battery packs.
  - V3 changed from two actuators per flap to one actuator with three motors.
- **Does not support:** actuation rate or torque. The model's 25°/s remains a simulator
  estimate.

### Starship V3 dimensions compendium (photogrammetry)

- **Where:** spacecraft-models/starship, `dimensions.md`,
  <https://github.com/spacecraft-models/starship/blob/main/dimensions.md> (last pushed
  2026-11-02). It cites NASASpaceflight photos, Ringwatchers, NSF reports and Wikipedia.
- **Licence:** none. Only the measured facts are recorded, paraphrased and cited; no text
  is copied.
- **Confidence:** `estimate`. Each value is labelled Published, Reported or Estimated with
  its error, typically ±0.6 m and ±5°.
- **Supports:**
  - V3 ship height 52.1 m, from skirt edge to tip.
  - Nosecone: tangent ogive about 13.7 m ± 1 m long.
  - Forward-flap geometry:
    - hinge at about 98° and 262° from windward, running up the ogive from z 43.3 m
      (radius 4.0 m) to 48.6 m (radius 2.2 m), which makes a tilt of about 19° to the axis;
    - planes turned about 14° leeward;
    - swept trapezoid of about 14 m².
  - Aft flaps: hinges at 90° and 270°, leading-edge root at about z 15 m, about 4 m reach,
    thickness tapering from 0.7 to 0.35 m.
  - Catch pins at z ≈ 39.0–39.9 m.
  - The V3 RCS layout (reported).
- **Does not support:** flap deflection ranges or rates, centre of mass, mass.
- **Use:** correcting the model's V3 flap stations and hinge geometry.
- **Data:**
  [data/v3_ship_geometry_photogrammetry.json](data/v3_ship_geometry_photogrammetry.json).

### IFT-4/5/6 full-flight extracts: entry corridor and booster catch

- **Where:** same OCR source as above, reduced to 1 Hz for the whole flight
  (`data/ift_terminal_descent/ift*_full_flight_1hz.csv`); analysis in
  `scripts/ift_entry_and_booster.py`.
- **Confidence:** `secondary`.
- **Supports:**
  - The ship entry corridor (speed at each altitude), which repeats closely across three
    flights:

    | Altitude | Speed |
    | ---: | --- |
    | 50 km | 12,500–12,900 km/h |
    | 40 km | 6,600–6,900 km/h |
    | 30 km | about 2,900–3,000 km/h |
    | 20 km | about 1,000–1,080 km/h |

    Mach 1 falls at 22–23 km.
  - The IFT-5 Super Heavy return and first tower catch:

    | Event | Value |
    | --- | --- |
    | Peak ascent speed | 5,274 km/h at 64 km |
    | Boostback minimum | 1,203 km/h at 91 km |
    | Apogee | 96 km |
    | Peak descent speed | 4,394 km/h at 26 km |
    | Landing burn | starts at about 1,209 km/h near 1 km; hard phase to 239 km/h in about 6 s (≈ 47 m/s²); zero speed 24.8 s after the start |

- **Use:**
  - A speed-against-altitude gate for entry recalibration (the Flight 14 and Flight 15
    descents). It does not depend on time alignment.
  - Booster catch-burn references.
- **Data:** [data/ift_entry_corridor.json](data/ift_entry_corridor.json).

### Mass estimates

- **Where:** press and community estimates (Payload Space, New Space Economy, r/spacex).
- **Confidence:** `estimate`.
- **Supports:** only ranges. Dry mass about 100 t (V1, per Musk 2021), about 155 t (Block 2,
  a community estimate from flight data) and about 75 t (V3, a guide's estimate).
- **Use:** bound the mass when comparing terminal speed. Never use these as model inputs.

## Reference fluid dynamics

### Jorgensen L. H. (1977). Prediction of static aerodynamic characteristics for slender bodies alone and with lifting surfaces to very high angles of attack

- **Where:** NASA TR R-474 (1977), cited by report number as in
  `data/provenance/rigid_body_fidelity_2026.json`. The NTRS link is not yet verified.
- **Confidence:** `primary`.
- **Supports:**
  - Crossflow drag C_dn against crossflow Mach and Reynolds (figs. 1–3), including the drag
    crisis.
  - The finite-length factor η (figs. 4–6).
- **Use:** already implemented (`CrossflowDrag`, gates F1).

### Achenbach E. (1968). Distribution of local pressure and skin friction around a circular cylinder in cross-flow up to Re = 5 × 10⁶

- **Where:** J. Fluid Mech. 34(4):625–639. Values below are quoted by later work, for
  example <https://www.sciencedirect.com/science/article/pii/S0167610524001843>.
- **Confidence:** `secondary`.
- **Supports:**
  - C_D ≈ 0.2 in the supercritical plateau.
  - C_D ≈ 0.7 in the transcritical range.
  - Transcritical drag rises with roughness (Achenbach 1971).
- **Does not support:** Re ≈ 10⁷–10⁸, Starship's terminal crossflow regime, which lies
  beyond the 5 × 10⁶ range.
- **Use:** bound the broadside drag at transcritical Reynolds numbers.
- **Data:** [data/cylinder_crossflow_reference.json](data/cylinder_crossflow_reference.json).

### Hoerner S. F. — Fluid-Dynamic Drag (1965) and Fluid-Dynamic Lift (1985)

- **Confidence:** `secondary`. Values are quoted, for example, by Monash (17th AFMC),
  <https://www.flair.monash.edu.au/intranet/proceedings/17afmc_proceedings/PDF/212_Paper.pdf>.
- **Supports:** broadside plate C_N at 90° of 1.18 for a square plate and 1.21 for side
  ratio 4.
- **Use:** already the broadside value of `BodyFlapAerodynamics` (1.2).

### Torres G. E., Mueller T. J. (2004). Low-aspect-ratio wing aerodynamics at low Reynolds numbers

- **Where:** AIAA J. 42(5):865–873. Parameters are quoted in
  <https://doi.org/10.3390/aerospace10070597>.
- **Confidence:** `secondary`.
- **Supports:**
  - Rectangular flat plates, 0.5 ≤ AR ≤ 2, α ≤ 50°, Re about 7–10 × 10⁴.
  - For AR 0.5, α_m ≈ 35°, K_p = 0.831 and K = 0.67; edge-vortex lift dominates.
- **Use:** check the plate lift term at low aspect ratio. The Starship flaps have
  AR ≈ 0.45–0.65, at far higher Reynolds numbers.

### Finite plates at high incidence

- **Where:** J. Fluid Mech. 846:82–120 (2018); AR ≈ 4,
  <https://yakari.polytechnique.fr/Django-pub/documents/yuelong2018rp-1pp.pdf>.
- **Confidence:** `peer-reviewed` measurement, but for a pitching plate, not a static one.
- **Supports:** C_N rises to a plateau for 30° < α < 70°, then falls slightly toward 90°.
- **Use:** the shape of the plate normal force law between 30° and 90°.

## Earlier register

The general entry-physics sources are in
[../REENTRY_PHYSICS_SOURCES_2026-09-25.md](../REENTRY_PHYSICS_SOURCES_2026-09-25.md): the
U.S. Standard Atmosphere, Sutton–Graves heating, Calhoun on entry flight control, and the
NASA 6-DoF check cases.
