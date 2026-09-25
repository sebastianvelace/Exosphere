# Orbital and reentry physics audit — 2026-09-25

## Executive diagnosis

The orbit propagator is not the largest realism gap. Its double-precision RK4 and Kepler
paths already have useful invariant tests. The visibly artificial behavior is concentrated
at the boundary between the Godot frame loop, EDL guidance and attitude-dependent forces:

1. EDL commands are refreshed once per rendered frame, after the physics update. One command
   is then held across all fixed physics substeps. Control behavior therefore varies with
   frame rate and always enters physics one rendered frame late.
2. The deterministic EDL presentation fixture directly assigns attitude and zeroes angular
   velocity every frame. It proves presentation/capture states, not physical controllability.
3. The entry-corridor predictor estimates time to ground from a bounded vacuum/free-fall
   expression. It does not propagate drag, lift, bank, Mach, energy or heating.
4. Production still uses the legacy split attitude/translation integrator. The coupled 6-DoF
   solver exists, but controlled EDL parity is not yet sufficient to enable it by default.
5. Starship aerodynamics remain an explicit engineering approximation: generic cylindrical
   area, a broadside coefficient, a sinusoidal lift law and aggregate flap authority instead
   of four local surface forces and a Mach/AoA/sideslip/deflection database.

These are distinct problems. Tuning lift or drag before removing frame-rate dependence would
fit coefficients to a moving numerical target and hide the root cause.

## Code evidence

| Priority | Finding | Evidence | Consequence |
|---|---|---|---|
| P0 | Guidance depends on render cadence | `SimulationBridge._Process` advances `Universe.Tick`; `EDLController` runs later at priority 200 and consumes `LastProcessedSimulationSeconds` | Different FPS can produce different attitude commands, entry footprints and thermal histories |
| P0 | Demo attitude is kinematic | `EDLController.AdvancePhase` assigns `vessel.Orientation` and clears `AngularVelocity` when `IsTowerCatchDemonstration` is true | The most repeatable visual entry can look smooth while bypassing rotational physics |
| P0 | Corridor prediction omits entry dynamics | `EntryCorridorGuidance.Predict` uses projected target motion and free-fall time-to-ground | Bank demand can oscillate or arrive late because predicted range ignores energy dissipation |
| P1 | Force and attitude stages are split by default | `Universe.Coupled6DofIntegrationEnabled` defaults off; legacy RK4 samples one orientation through its translational stages | Fast attitude changes do not alter force direction continuously within the same RK4 step |
| P1 | Aero model lacks Starship database dimensions | `AerodynamicsModel` uses estimated `Cd`, `CL = CLmax sin(2 alpha)` and aggregate dimensions | Trajectory trends can be causal, but absolute footprint/heating/control authority is uncertain |
| P1 | Space rotation has a fixed authority floor | `Vessel.ReactionControlAuthority = 0.01 rad/s²` | Coast rotations do not derive from thruster geometry, propellant or changing inertia |
| P2 | Atmosphere is climatological, not meteorological | standard layers plus a configured thermosphere tail; no winds or turbulence | Crossrange and heating dispersion are systematically underrepresented |

## What is already physically sound

- SI units and double precision in the pure simulation layer.
- Body-centred inertial orbital state and atmosphere-relative aerodynamic velocity.
- Rotating-body surface velocity, WGS-style altitude, J2 gravity on the active numerical path,
  variable mass, thrust/pressure coupling and thermal state.
- Candidate-state translational RK4 force sampling and an opt-in coupled rigid-body solver.
- Sutton–Graves-based stagnation heating with attitude-dependent effective radius.

## New observable contract

`EntryFlightDiagnostics` now samples both frames without mutating the vessel:

- point-mass specific orbital energy and specific angular momentum from the body-centred
  inertial state;
- atmosphere-relative speed, Mach and dynamic pressure;
- flight-path angle, angle of attack and aerodynamic bank angle;
- separated modeled drag and lift, L/D, aerodynamic load and stagnation heat flux.

The normal `--orbital-reentry` trace records those values plus the EDL guidance update period
and update count. An undefined angle is emitted as `NaN`; diagnostics never fabricate a zero
that could turn missing geometry into apparent success.

## First measured correction

A new 120 km / 7.6 km/s / -1.6° full physical-entry gate flew the real attitude controller,
flap actuator, atmosphere, force integrator and thermal model at 50 Hz. It does not assign
orientation or clear angular velocity.

The previous continuous **lift-down** catch-entry family reached 30 km with:

- peak aerodynamic load: **29.65 g**;
- peak dynamic pressure: **124,683 Pa**;
- peak stagnation heat flux: **785,772 W/m²**;
- speed still **3,674 m/s** at the 30 km gate.

That result fails the bounded 8 g acceptance envelope and explains an important part of the
unnatural plunge. The production handoff now starts lift-up. `SelectLiftDirection` retains
lift-up inside its dead bands, banks toward crossrange error and reverses lift downward only
for a measured predicted overflight. The same physical gate now passes survival, lower-
atmosphere arrival, heating pulse, dynamic-pressure, load, energy-dissipation and windward-TPS
requirements. This is a trajectory-guidance correction; no `Cd`, `CL`, heat or mass
coefficient was tuned to make the test pass.

The render-cadence coupling and reduced-order footprint predictor remain open P0 items.

## Verification ladder

### Gate 1 — numerical invariants

- Multi-orbit vacuum coast at multiple fixed steps.
- Bound point-mass energy and angular-momentum drift.
- Repeat the same initial state with 30, 60 and 120 Hz command sampling; final orbital state
  must be identical before atmosphere and bounded through entry.

### Gate 2 — full physical entry

- Start at an explicit 120 km entry interface with recorded inertial state.
- No direct orientation assignment, no angular-velocity reset and no hidden force override.
- Record at <= 2 s simulated intervals: altitude, velocity, Mach, `q`, flight-path angle,
  AoA, bank, energy, angular momentum, L/D, load and heat flux.
- Run nominal, high/low density, coefficient sensitivity and control-authority cases.

### Gate 3 — gameplay evidence

- Real Xvfb framebuffer milestones bound to the same run manifest and telemetry.
- Demonstrate orbital coast, entry interface, peak heating, transonic, subsonic belly-flop,
  flip and physical landing/catch.
- A capture fixture may remain for art review, but it cannot satisfy a physics gate.

## Ordered remediation

1. **Complete the rendered baseline:** the real llvmpipe run proved coast telemetry and a real
   orbit framebuffer, but its 3300 s coast cannot reach atmospheric interface within the
   practical wall-time budget. Add a non-demo entry-interface fixture bound to the same state
   and provenance contract.
2. **Remove render-rate coupling:** move EDL guidance into a deterministic pure-simulation
   control cadence evaluated before force integration; hold commands only for a declared
   control period.
3. **Replace vacuum corridor prediction:** propagate a reduced-order entry state containing
   energy, flight-path angle, lift/drag and bank; use bank reversals with dead bands for
   crossrange instead of continuously steering at the point target.
4. **Close coupled 6-DoF parity:** add a full controlled-entry fixture, then enable the
   coupled path only after legacy/coupled envelopes and contacts pass.
5. **Calibrate uncertainty, not a magic curve:** introduce data tables over Mach, AoA,
   sideslip and four flap deflections; until validated data exists, publish ranges and run
   sensitivity ensembles rather than claiming exact Starship coefficients.
6. **Model dispersions:** winds, density variation, mass properties, actuator lag/failure and
   RCS hardware authority.

## Acceptance rule

No coefficient retuning, visual smoothness or deterministic screenshot can close this audit.
Closure requires frame-rate-independent control, a physically flown orbital entry, bounded
numerical diagnostics, sensitivity evidence and synchronized real-framebuffer captures.

Research provenance: [`docs/research/REENTRY_PHYSICS_SOURCES_2026-09-25.md`](../research/REENTRY_PHYSICS_SOURCES_2026-09-25.md).
