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
