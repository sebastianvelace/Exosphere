# Starship aerodynamics and control: research register

Data from researchers and from flight observations, organised so that the physics model can
be checked against it. The model must follow the data; data must not be fitted to the model.

## Layout

| File | Contents |
| --- | --- |
| [SOURCES.md](SOURCES.md) | Annotated sources: what each supports, what it does not, how Exosphere uses it, and its confidence. |
| [MODEL_COMPARISON.md](MODEL_COMPARISON.md) | The model against these data, with open discrepancies and the work each one implies. |
| [data/li2022_starship_aero_layout.json](data/li2022_starship_aero_layout.json) | RANS study of the Starship layout (Ma 12, 2 and 0.8; α 0–120°): trim ranges, C_L and C_D, and stability, as stated in the text. |
| [data/chen2025_s31_subsonic_rans.json](data/chen2025_s31_subsonic_rans.json) | S31 prototype subsonic RANS: C_L, C_D and C_M against α from 0 to 350°, digitised from the published plot; vehicle and initial-state table. |
| [data/cylinder_crossflow_reference.json](data/cylinder_crossflow_reference.json) | Circular-cylinder crossflow drag by Reynolds regime; Jorgensen (in the code) and Achenbach. |
| [data/flat_plate_normal_force_reference.json](data/flat_plate_normal_force_reference.json) | Flat-plate normal force at high incidence and low aspect ratio (Hoerner, Torres & Mueller). |
| [data/ift_terminal_descent_telemetry.json](data/ift_terminal_descent_telemetry.json) | IFT-4/5/6 terminal belly flop from webcast OCR: speed against altitude, measured ballistic ratio m/(C_D S) and landing-burn durations. Reduced CSVs and their attribution are in `data/ift_terminal_descent/`; the script is `scripts/ift_terminal_descent.py`. |
| [data/v3_ship_geometry_photogrammetry.json](data/v3_ship_geometry_photogrammetry.json) | Starship V3 ship geometry measured from photos (community compendium): forward- and aft-flap hinge lines, planform and azimuth, nosecone, catch pins and RCS, with the model comparison. |
| [data/ift_entry_corridor.json](data/ift_entry_corridor.json) | IFT-4/5/6 ship entry corridor (speed at each altitude from 120 to 20 km) and the IFT-5 Super Heavy return and tower catch, from the full-flight 1 Hz extracts in `data/ift_terminal_descent/`. |
| [data/ift_booster_descent.json](data/ift_booster_descent.json) | Super Heavy unpowered descent in IFT-4/5/6: speed and deceleration by altitude and the implied ballistic ratio (2,000–3,000 kg/m² between 20 and 2 km; peak about 4.5–5 g). |
| [data/faa_lc39a_noise_eis_2025.json](data/faa_lc39a_noise_eis_2025.json) | FAA LC-39A EIS noise study (SpaceX-supplied trajectories): ship landing thrust over the last ~550 m; ship supersonic to ~22.9 km; booster landing thrust in the last ~1.5 km; ascent timeline. |
| [data/raptor3_reference.json](data/raptor3_reference.json) | Raptor 3 thrust, Isp, throttle, gimbal and mass by confidence level, compared with the model's ship engine (gimbal 6° modelled against 15° reported). |
| [data/starship_flight_observations.json](data/starship_flight_observations.json) | Observed flight values and timelines (broadcasts, SpaceX statements), and mass estimates. |

The Flight 14 broadcast anchors with frame hashes stay in
[../STARSHIP_FLIGHT14_DESCENT_BROWSER_ANCHORS_2026-10-02.json](../STARSHIP_FLIGHT14_DESCENT_BROWSER_ANCHORS_2026-10-02.json).
The general entry-physics register is
[../REENTRY_PHYSICS_SOURCES_2026-09-25.md](../REENTRY_PHYSICS_SOURCES_2026-09-25.md).

## Confidence scale

Every source and every dataset entry carries one of these levels:

| Level | Meaning |
| --- | --- |
| `primary` | NASA/NACA report, peer-reviewed measurement, or SpaceX statement of fact. |
| `peer-reviewed-cfd` | Journal CFD on a public-information geometry, not SpaceX data. |
| `preprint` | Not peer reviewed. |
| `secondary` | A value quoted by another paper or by the press, not read in the original. |
| `digitised` | Read off a published plot. Each such dataset states its reading error. |
| `estimate` | Community or press estimate with no stated method. Use only as a range. |

## Rules for using these data

- **Provenance.** A model constant taken from here needs a provenance record in
  `data/provenance/*.json` that names the file and the entry. The status is `published`,
  `derived` or `estimated` according to the level above.
- **Reference frames.** Coefficients are only comparable after matching the reference area
  and length, the moment centre, the Mach number, the Reynolds number and the flap
  configuration. Every dataset records these, or says that they are unknown.
- **Gates.** A gate built on these data cites the file and states its tolerance from the
  data's own uncertainty, not from what the model happens to reach.
- **Gaps.** Unknown means unknown. When a needed value is missing, record it as an open
  item in MODEL_COMPARISON.md rather than inventing one.
