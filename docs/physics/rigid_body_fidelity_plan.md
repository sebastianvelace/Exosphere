# Rigid-Body and Aerodynamic-Moment Fidelity Plan

Goal: every force and moment on a flying vehicle comes from a physical model of its parts,
evaluated on the true rigid body, with no tuned constants standing in for physics. Decision
rule for every trade-off in this track: choose the more physically faithful option and
recalibrate profiles against historical data, never against the previous model.

## Audit (2026-10-31)

The coupled 6-DoF integrator (`RigidBody6DofIntegrator`) is sound: RK4 on the rigid body at
the centre of mass, per-part parallel-axis inertia tensor, Euler gyroscopic term, candidate-
state contacts. Every Starship segment has a legacy/coupled parity gate
(`coupled_6dof_migration.md`, steps 20–29). What is not yet physics:

| # | Current model | Physical model |
| --- | --- | --- |
| A1 | Production flies the legacy split tick; forces sampled at the aft datum | Coupled RK4 in production; forces at the centre of mass |
| A2 | Aero moment = total drag × one centre-of-pressure guess (`0.08·L`, clamped) | Distributed normal force per part (slender-body + viscous crossflow), moment Σ rᵢ×Fᵢ about the centre of mass, Mach-dependent |
| A3 | Pitch damping `0.55·q/30 000` s⁻¹, capped at 1.25 | Emerges from the local flow velocity v + ω×rᵢ at each part |
| A4 | Flaps: one aggregate surface, coefficient 3.0, cap 4 rad/s² | Four surfaces with area, centroid, hinge axis and deflection; local incidence → normal force; hinge-moment-limited servos |
| A5 | SAS overwrites ω after the step; universal 0.01 rad/s² "RCS floor" | SAS is a controller that commands TVC/RCS/flaps; RCS torque only from declared thrusters (thrust × lever arm) |
| A6 | No jet damping, no internal-flow Coriolis | Variable-mass rigid body: jet-damping torque −ṁ(rₑ² ω⊥) and COM drift rate |
| A7 | No gravity-gradient torque | 3μ/r⁵ · r × (I r) |
| A8 | No propellant slosh | Pendulum/spring slosh modes per tank, fill-fraction dependent |

## F0 findings (2026-10-31)

Turning the coupled path on by default exposed a frame error underneath both paths, fixed
before any recalibration:

- **One vessel origin.** `PartGraph` measured parts from the root (the top of every stack:
  escape tower, docking nose, payload) while the renderer, landing gear, water contact and pad
  placement all used the stack's lowest point. Every catalogue vehicle's centre of mass sat
  23–116 m *below* its skirt. The part frame now has its origin at the lowest point
  (`AssignSkirtDatumPositions`), shared by the tick cache and every query (closes ROADMAP
  blocker 5). With the true centre of mass, Mercury-Atlas, Saturn V AS-506 and Flight 14
  calibrations pass without retuning; the Atlas had been flying with vacuum thrust from a
  centre of mass 11 m underground.
- **Atmosphere below the reference surface.** Pressure and density returned zero below 0 m;
  the base layer now extrapolates to −5 km as USSA-76 defines.
- **Exact separation.** Staging, structural breakup and payload release keep every part in
  place and give each fragment the rigid-body velocity v + ω×Δ (mass, centre of mass, linear
  and angular momentum conserved to 1e-9), replacing the mass-ratio "gap" heuristic.
- **Docked stacks** integrate as one rigid body (composite mass, centre and R·I·Rᵀ tensor).
- **Every scheduler uses the coupled path.** The warp-mixed scheduler and deferred catch-up
  called the split tick directly; one `IntegrateVesselFullPhysics` entry point now dispatches.
- **Water contact** is co-integrated on the coupled path (pre-entry and floating).
- **On-rails conics describe the centre of mass** (`Vessel.ComputeCenterOfMassOrbit`); the
  datum is rebuilt from the coasted attitude.
- **Navigation state is the centre of mass.** `Vessel.GetSurfaceVelocity` and orbital burn
  guidance (Lambert, prograde-energy, periapsis, translunar/transearth corrections, insertion,
  SAS holds) read centre-of-mass position and velocity; a rotating skirt's ω×r had steered the
  Flight 14 flip off vertical and kept a 0.06 m/s Apollo correction from ever aligning.
- **Latent errors found on the way:** the engine-out isolator combined part-relative mount
  positions with the vessel centre of mass; the RCS floor torque was scaled by the docked
  inertia ratio twice; anchored vessels corotated their datum, not their centre of mass.

## F1 findings (2026-10-31)

`DistributedAerodynamics` replaces the centre-of-pressure guess and the damping constant for
every slender stack (`Universe.DistributedAerodynamicsEnabled`, on by default):

- **Model.** Per part: Allen–Perkins viscous crossflow (C_dc = 1.5 on the planform, ogive
  noses at 2/3·D·L), Munk slender-body normal force 2ΔA·sinα·cosα at the leading nose
  (Barrowman ogive CP at 0.466·L_n, `nose_length_m`) and at every coaxial diameter step, and
  axial C_A·A_max·cos²α on the axis. Each element sees v + ω×r, so pitch damping and
  tail-first reversal emerge. Analytic gates: broadside drag, nose-on drag, C_Nα = 2 per base
  area, bare-body instability vs flare stability, rate damping, tail-first reversal.
- **Validity boundary.** Slender-body theory needs a slender body: stacks with
  L/D < 3 (Mercury, Gemini, Apollo capsules) keep the blunt/trimmed model until a Newtonian
  blunt-body model lands; the trimmed-capsule and Super Heavy standalone-margin models stay.
- **Finless launchers are unstable, and are flown that way.** With a physical centre of
  pressure, the Atlas, Saturn V and the VAB starter are statically unstable (the Munk nose
  lift sits ahead of the centre of mass); at Atlas max-Q, Mα/I ≈ 2.2 rad/s² per rad exceeded
  the guidance loop's stiffness (1.85), so it diverged. Flight control now inverts the
  predicted static aerodynamic moment (evaluated with ω = 0, so natural damping remains) in
  the gimbal allocation, as it already inverted the engines' zero-gimbal moment; the result is
  still clipped to the real gimbal range. SAS with no input holds attitude against it the same
  way. Engines without a per-instance runtime now join the allocator through their part
  gimbal, and their zero-gimbal geometry no longer feeds back the current deflection.
- **Centre-of-mass consistency.** `PartGraph.CenterOfMass` and the tick centre of mass weight
  only parts placed in the stack frame, matching `BuildMassProperties`; an unattached part had
  shifted one centre by 0.31 m and the contact datum against the integrator's datum.
- **Recalibration.** Mercury-Atlas loses ~40 m/s less to drag by BECO than under the
  aggregate model (whose broadside blend over-charged the tape's 4–6° angle of attack); the
  pitch tape is re-closed at 23.2° BECO / −17.9° insertion: 159.5 × 267.8 km against the
  published 161 × 261 km (aggregate fit: 157 × 276). Saturn V, Freedom 7, Gemini 8 and the
  lunar launch window pass unchanged.
- **Open gaps.** S-IC fins and Super Heavy grid fins are not yet surfaces (grid fins keep the
  declared margin); nose lengths for Starship, Falcon/New Glenn fairings and Apollo LES are
  estimates; the Atlas sustainer's 625 kN / 350 s effective rating (hardware ≈ 363 kN /
  309 s) remains an F6 item; the flight-control inversion is model-based (modern vehicles);
  historical gain schedules belong to F3.

## F2 findings (2026-10-31, in progress)

`BodyFlapAerodynamics` models Starship's four body flaps as hinged plates from part data
(`body_flaps`: hinge station, side, length, span, travel, neutral, servo rate; planforms are the
renderer's image-derived Flight 7 proportions, forward ≈ 5.7 × 2.55 m, aft ≈ 11.1 × 3.7 m).
Each plate carries a normal force at its centroid from the local flow v + ω×r with a
Mach-dependent coefficient: low-aspect-ratio plate law (π/2·AR·sinα·cosα + 1.2·sin²α) below
Mach 0.8, modified Newtonian Cp,max(M)·sin²α (Rayleigh pitot) above Mach 1.5, blended between.
Each servo slews at its own rate within its travel; flight control allocates pitch/yaw/roll by
least squares on the finite-difference torque Jacobian and inverts the predicted static moment,
and the flaps are locked while the ship rides the booster. Analytic gates: Rayleigh-pitot Cp,max,
broadside plate, folded plate, symmetric/differential pairs, rate damping.

The switch (`Universe.PhysicalBodyFlapsEnabled`) stays off because the physics exposed a mass
gap: broadside, the hull's crossflow centre of pressure sits at 23.9 m (V2 ship) and the dry
centre of mass at 24.7 m, but the model carries the landing propellant in the main tank
(y = 18.8 m), pulling the entry centre of mass to 22.2 m. That leaves ≈ −2400 q of
destabilising pitch moment at 70° against at most ≈ +1300 q of flap authority, so no flap
setting trims the entry. The aggregate flap coefficient (3.0, capped at 4 rad/s²) had been
covering it. The real ship lands on header-tank propellant (LOX header in the nose), which is
what makes it trimmable.

**Header tanks (2026-11-04).** Parts declare `header_tanks` (propellant, capacity, station).
Header propellant is a point mass at its station (centre of mass and parallel-axis inertia),
counts in the part mass and delta-v, feeds the engines only after the main tanks are empty, and
survives saves (old saves restore empty headers). Starship carries a 23.5 t LOX header in the
nose and a 6.5 t CH4 header in the tank, carved out of the main capacities so the launch mass
is unchanged (30 t total, calibrated against the Flight 14 landing within its declared ±50 %
uncertainty). The Flight 14 return opens a main-tank vent valve (100 kg/s, estimated) at the
entry coast; the vented mass is accounted in `Vessel.VentedPropellantKg`, so the ship enters on
header propellant only. Consequences: the heavier-than-real 70 t main-tank landing load had
hidden an inefficient landing; with the real header load a 1.2 km flip killed the velocity too
high and hovered on one engine until dry. The flip moved to 600 m (500 m arrived tilted 14°,
outside the 10° envelope). The continuous chain then exposed a guidance limit cycle the seeded
bench had hidden by phase luck: the tilt-to-kill-drift loop (0.5/s) was nearly as fast as the
single-engine attitude loop and oscillated ±11° near the 100 m gate. At 0.2/s it stays a
proper cascade (≤ 3° below 200 m) at the same propellant cost. Deorbit
ignition re-closed at T+7883 s (observed window 7864–7925 s): broadcast altitude residuals
−36 / +132 m.

**Physical flaps on, measured crossflow drag (2026-11-04).** The broadcast shows the belly flop
at 363 km/h (≈101 m/s) at 1.2 km; the model fell at ~65 m/s. The crossflow coefficient was a
constant 1.5 times the aggregate transonic multiplier, a subcritical laboratory value. It is now
η·C_dn from Jorgensen, NASA TR R-474 (figs. 1–6): C_dn against crossflow Mach and Reynolds
number (drag crisis near Re_n 4·10⁵, transcritical ≈0.6 above 10⁷, transonic peak ≈2, Newtonian
limit), η against fineness and crossflow Mach (unity supersonic), with Sutherland viscosity per
atmosphere (air USSA-1976; CO₂ and H₂ from White). Without tuning the V3 ship now falls at
~102 m/s (hull + flaps at neutral). `PhysicalBodyFlapsEnabled` is on by default.

Flying the entry on physical flaps exposed control and mass issues, each fixed physically:
- Flap allocation now inverts the nonlinear plate moments by Gauss–Newton (the neutral
  linearisation left a 5–8° steady AoA error) with redistributed-pseudo-inverse saturation
  handling (Virnig & Bodden). A normalised flap command stays a fraction of full flap
  authority (the convention every actuator follows); the descent attitude loop schedules its
  gains by the flaps' angular authority (ωn 0.3 rad/s, ζ 0.8) so its bandwidth does not grow
  with dynamic pressure.
- Dry structure centres: a nose section is a shell whose area concentrates near its base
  (`dry_mass_center_y_m`, −2.07 m nose, −0.70 m tank); headers resized to 20 t for the
  observed ~19 s landing burn with margin. With 30 t the CoM sat ~4 m ahead of neutral
  trim and the flaps saturated.
- Even so, in the Mach 0.5–0.9 band the hull's crossflow peak lets the flaps trim only
  ~75–85°. Commanding untrimmable angles saturated pitch; folded aft flaps carry no load, so
  roll about the long axis (no hull stiffness) departed. An onboard AoA limiter now evaluates
  the vessel's own hull/flap model and commands the nearest angle trimmable with 15° of
  travel to spare; a reference governor keeps the reference within 6° of the attitude. The
  descent now flies with flaps near neutral and peak rate ~0.03 rad/s.
- The 55° hypersonic AoA had been a guidance experiment: with distributed aero L/D ≈ cot α,
  so 55° glided far too high. Between the coast and 90 km the ship now flies the same
  59.5° with its lift banked 90° (a lift-up entry slowed the sink to ~32 m/s against the
  ~63 m/s the T+9986 88.1 km display implies); the deorbit periapsis target is 104.4 km.
  Residuals: −0.44 / −0.32 km at T+9884/9894, +0.5 km at T+9986, +1.30 / +1.36 / +0.92 km
  at T+10369 / 10541 / 10961 (speed 7291 vs 9982 km/h at the last); the flip gate is
  reached ~7 s before the broadcast. 59.5° sits half a degree below the 60–70° band a
  secondary source reports; the descent is very sensitive to it (57° arrives 100 s late).
  The 102 km display stays −1.9 km: the slope through 94 km is within the unspecified display
  datum (ellipsoid vs sphere moves it ~1–2 km over this track), so it is not chased.
- **F2 closed.** Historical gate `Flight14DescentHistoricalTests`: the descent on physical
  flaps reproduces the broadcast descent displays within the 3 km datum band, and the belly
  flop scaled to 1.2 km falls at 101.1 m/s against the broadcast 100.8 m/s (363 km/h).
- The physics change exposed a latent S-IVB limit cycle in the Saturn V separation stress
  case: the J-2 gimbal rate was an unsourced 2.5°/s. The published S-II actuator rate is
  8°/s (Rocket Propulsion Evolution 8.20); S-II and S-IVB now use it (S-IVB derived), and the
  stress case leaves 66.4 t for TLI (was 65.3 t before this block).

Landing (broadcast and the SpaceX timeline: ignition at 1.1–1.2 km, flip +2 s, 3→2 engines
+10 s, 2→1 +17 s, splashdown +19 s). The flip now starts at 1.1 km; the braking target is two
constant-deceleration segments through the broadcast 0.8 km/101 m/s and 0.3 km/56 m/s; the
guidance is a powered-descent thrust vector (profile feed-forward, drift damping inside a 30°
cone, minimum-thrust surplus steered into drift braking); engines are selected with 80 %
throttle headroom and a 2× gimbal-moment margin over the predicted aerodynamic moment, which
reproduces three→two→one. Mission controllers fly without the 20°/s pilot-authority envelope
(`Vessel.PilotRateEnvelopeEnabled`), so the flip turns at its gimbal-limited rate (~0.55 rad/s).
Ignition to 100 m now takes ~21 s (was 40+).

## F3 findings (2026-11-04, closed)

- **Every moment comes from an actuator.** The flight-control law (pilot or guidance command,
  SAS rate damping, the 20°/s pilot envelope) only produces a desired body angular
  acceleration; the gimbals (`SolveDifferentialGimbal`), the declared RCS thrusters and the
  body flaps deliver what they physically can. Nothing writes ω: the coupled post-step SAS and
  envelope edits, the legacy post-aero envelope and the universal 0.01 rad/s² floor are gone.
  A vehicle without thrusters or lit engines has no attitude authority in vacuum.
- **Thrusters per part** (`rcs_thrusters`: position, direction, thrust, Isp). `RcsAllocation` solves the bounded duties (0 ≤ u ≤ 1) by a
  redistributed pseudo-inverse polished with projected coordinate descent, rows weighted by
  inverse principal inertia (angular-acceleration error, not N·m). Attitude-only requests are
  lexicographic: pure couples first, and only the moment couples cannot give is taken from
  jets that also translate (the Apollo CM's one-sided jets). Authority is the moment that
  allocation actually reaches, cached on a 25 cm centre-of-mass grid so a resumed run computes
  it bit for bit. Translation commands go through the same allocation.
- **Propellant per system.** A part with a store feeds its own jets, others draw on the shared
  feed, and a valved-off system's store leaves it. Starship's cold gas is a usable tank-gas
  store (ship 4 t, booster 10 t, estimated from tank volume and an ~2.5 → 1.2 bar usable
  ullage range), separate from the main-liquid feed. This closure leaves the existing
  Starship gas, liquid and dry allocations unchanged; it introduces no new mass deduction.
  After the pre-entry vent the tanks are
  dry of liquid but still hold their gas. A 1 t store ran dry in the entry turn (the deorbit
  burn and the turn take ~0.9 t) and left the ship without RCS at entry interface. Arming is
  physical: `rcs_arms_with_stage` (S-IVB APS, Gemini OAMS, Starship cold gas) switches on once
  when the stage below separates; `rcs_arms_when_isolated` (Apollo CM, Mercury H2O2) when no
  other attitude actuator remains (thrusters or a liquid engine).
- **Blending.** The flaps are credited with as much of the request as their present authority
  allows; the jets only fill the rest. Without this the ship's RCS chased the flap command and
  burned 200–400 kg of gas per minute of entry. Under thrust a slewing gimbal cluster (≥ 1°/s)
  owns the axes it can cover, so the jets do not chase servo lag (the S-IVB APS flies only roll
  during J-2 burns); a trim gimbal does not (the LM DPS at its published 0.2°/s only trims, and
  the jets fly the attitude, as the LM DAP did).
- **Envelope at the command.** The pilot envelope removes only the part of the command that
  would spin the body faster past 20°/s; it never orders braking. A dead-beat braking version
  limit-cycled the Titan II second stage against its 4°/s LR91 gimbal.
- **Data** (`data/provenance/reaction_control_2026.json`): S-IVB APS 2 × 3 × 147 lbf (pitch
  radial, yaw/roll tangential, NASA TN / NASM), Apollo SM 4 quads × 4 R-4D canted 10°
  (RPE 9.32), CM 12 × 93 lbf, LM 4 × 4 R-4D, Gemini OAMS (8 × 25 lbf attitude + translation)
  and re-entry rings (2 × 8 × 25 lbf), Mercury H2O2 24/6 lbf, Starship/Super Heavy cold gas
  (estimated 1.5 kN, Isp 60 s, ±50 %), starter pod generic. The LM DPS gimbal rate is now the
  published 0.2°/s trim rate (was an estimated 2°/s). Propellant moved out of the dry
  allocations where the published masses already carried it.
- **Recalibration.** Titan II stage two flies an authority-scheduled pointing loop (ωn 0.6 rad/s
  inside the LR91 gimbal bandwidth): insertion T+367 s (historical SECO T+337.5 s), 145 × 154 km,
  0.32° off the Agena plane. Gemini 8's re-entry rings now spend 68 % of their load recovering
  from the thruster-8 roll (flight: ~75 %); the stuck thruster drains the OAMS (8.0 kg). Apollo 8
  enters at −6.79° and splashes at 8.3 m/s on the CM's own jets. Starship's coast
  reorientation takes minutes on cold gas (the old floor did it in ~35 s). Flight 14: the jets
  correcting the deorbit burn put the periapsis 370 m lower and entry 20 s earlier; the
  ignition lever moved T+7883 → T+7905 s (inside the observed window). The descent guidance
  normalises its demand by the ship's
  whole authority (`Vessel.GetAttitudeAuthorityBody`: flaps, jets, gimbals).
- **Gimbals own the axes they can cover under thrust**, if they slew ≥ 1°/s; a trim gimbal does
  not (the LM DPS rate is now the published 0.2°/s). The jets credit the flaps with what they were
  commanded, capped at the request. A thruster whose own store is empty is unavailable.
- **Performance.** `RcsAllocation` was ~60 % of Flight 14 simulation time while the booster jets
  chased gimbal servo lag; with the gimbal rule and an early exit when couples suffice it is
  ~24 %. A cold Flight 14 pad-to-entry flight takes ~9.5 min (≈7 before F3).

### Flight 14 closure evidence

- **Exact checkpoint restore.** The first command difference came from normalising an
  already-unit saved quaternion again, rather than from the RCS authority cache. Its norm
  was one rounding unit below one; changing its last bits led to 3.4 m divergence after
  60 s. `QuaternionSaveV2` preserves unit attitudes within floating-point round-off while
  still normalising non-unit legacy saves. The continuation gate now requires exact
  equality of time, position, velocity, orientation, angular rate, propellant and flap state;
  position and velocity residuals are both zero. A warm-cache/gas-consumption bench also
  checks restored actuator authority exactly.
- **Physical blending and coupled stability.** Staged engines remain in the active-engine
  list after shutdown; TVC owns trim only while thrust is actually delivered. Otherwise
  the jets duplicated the flaps' static trim. The flap-credit rule stays "commanded share,
  capped at the request". At hypersonic speed the pitch/yaw loop is critically damped at
  0.15 rad/s, below the trim-limited servo bandwidth; roll retains 0.3 rad/s and damping
  0.8. The transverse loop blends back to 0.3/0.8 between 1000 and 500 m/s. A constant-wind
  coupled bank-reversal regression retains finite servos and the original 0.15 rad/s gate.
- **Consistent descent guidance.** The bank predictor uses the distributed hull and
  physical-flap force model, with zero angular rate for the equilibrium estimate, plus
  centre-of-mass orbital kinematics. Low-pressure handoff retains the lift direction
  already flown instead of commanding an extra roll toward local lift-up. The estimated
  downward-speed fraction is recalibrated from 0.006 to 0.0049; the 59.5-degree AoA,
  actuator limits, force coefficients and physical acceptance limits are unchanged.
- **Historical descent reclosed.** Residuals at T+10369/10541/10961 are +332/+932/-1697 m
  within the original 3 km altitude-datum band. Peak angular rate is 0.066539 rad/s,
  below 0.15; the original 20 t header reserve remains at the 3 km witness. Belly-flop
  speed is 111.4 m/s at 3 km, equivalent by density scaling to 101.8 m/s at the 1.2 km
  broadcast datum (100.8 m/s observed). The unspecified display speed frame and late
  hypersonic speed mismatch remain F6 work; altitude agreement is not full trajectory validation.
- **Catalog RCS completed.** Falcon 9 GN2 stages, New Glenn forward/aft systems and Agena's
  six nitrogen jets now have declared geometry, finite stores and three-axis authority.
  Unpublished thrust, Isp, layout and loads are explicitly estimated in
  `reaction_control_2026.json`. Stores are reallocated from existing estimated inert
  allocations, preserving loaded mass; Starship's gas/liquid/dry allocations are unchanged.
- **Ocean-domain evidence.** NOAA ETOPO 2022 native-grid geographic screening is retained
  in `docs/research/FLIGHT14_OCEAN_COVERAGE_2026-11-04.json`. It defines where the existing
  flat-water model may operate, not measured targeting or bathymetric collision loads.

### Closure validation

The continuous moving-Solar-System preview reached 22.527910 N, 160.292445 W, with
peak descent angular rate 0.063216 rad/s. A checkpoint captured before descent handoff
reproduced its contact coordinates, altitude, peak rate and 100 m witness exactly.
The contact lay outside the previous operating domain. NOAA native-grid screening now
covers 22.5..35 N, 161..140 W: all 15,120,000 cells are below sea level (-7024..-66.55 m),
including both propagated contacts while excluding Kauai, Nihoa and Oahu. This changes
geographic water-model coverage, not the 5 m/s contact limit, attitude gate or water loads.
The resumed moving-world continuation reaches water at 4.414 m/s, upright alignment
0.998740, with 1672 kg liquid reserve, and completes ten seconds of coupled water response.
The isolated-Earth continuation reaches water at 4.422 m/s.

`bash tools/ci_check.sh` passes: 1259/1259 tests, no skipped tests, all three builds at
0 warnings / 0 errors, Flight startup, MainMenu and Construction smoke tests, and the
real-framebuffer graphics-settings capture/reload check. F3 is closed with the analytic
RCS/SAS gates, the Gemini 8 historical recovery gate, exact checkpoint continuation and
the original Flight 14 descent/contact limits. F4–F6 remain planned.

## Phases

**F0 — Coupled 6-DoF in production (A1).** Flip `Coupled6DofIntegrationEnabled` on by
default; legacy stays as an opt-out. Every failing calibration is re-fitted against its
historical source (AS-503/506 insertion, Flight 14 broadcast anchors), never by loosening a
tolerance. Godot headless smoke and `[G]` ascent/EDL harnesses with the switch on.

**F1 — Distributed aerodynamics (A2, A3).** Per-part geometry (length, diameter, nose/flare
shape) gives normal-force slope and crossflow area; each part sees its own local velocity
including ω×r, so static stability and damping are one model. Removes the CP clamp and the
damping constant. Validation: Saturn V CP-vs-Mach and Cmα from NASA wind-tunnel reports, slender
cone/cylinder analytic cases, Starship belly-flop trim angle.

**F2 — Physical body flaps (A4).** Four flap parts with geometry from the Ship definition, local
incidence from orientation, deflection and ω×r, Newtonian/flat-plate normal force at each
centroid, servo rate and hinge-moment limits. Removes the control coefficient and the authority
cap. Validation: 55–70° entry trim, belly-flop hold, flap asymmetry for roll.

**F3 — Actuator-only attitude control (A5).** SAS becomes a rate/attitude controller writing
commands; RCS thrusters are declared per part (thrust, position, direction) and produce torque
and force; no direct ω edits; the floor disappears. Vehicles without RCS have none.
*Closed 2026-11-04: analytic and historical gates, exact checkpoint/descent regressions,
catalog RCS and the full loaded moving-world return pass (see F3 findings).*

**F4 — Variable-mass and environment torques (A6, A7).** Jet damping from mass flow and nozzle
exit position; gravity gradient. Validation: analytic spin/precession cases, Saturn V jet-damping
magnitude from Apollo dynamics reports.

**F5 — Propellant slosh (A8).** First lateral slosh mode per tank (mass, frequency, damping from
fill height and tank radius). Validation: analytic pendulum frequency, published Saturn S-IC
slosh frequencies.

**F6 — Historical recalibration.** Re-run every historical profile (Mercury, Gemini, Apollo 8/11,
Starship Flight 7/14, Falcon, New Glenn) against its source data after F0–F5; record residuals.

Each phase lands as reviewable commits with xUnit tests, an entry in
`coupled_6dof_migration.md` or `PHYSICS_MODEL.md`, and a full `tools/ci_check.sh` run.
