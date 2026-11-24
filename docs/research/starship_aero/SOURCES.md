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
