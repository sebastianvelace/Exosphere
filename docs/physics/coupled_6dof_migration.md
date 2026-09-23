# Coupled 6-DoF migration

This document records the reviewable work units used to introduce a coupled rigid-body
integrator without changing the production scheduler by default.

## Completed sequence

1. `feat(math): add double-precision 3x3 matrix primitives` — matrix multiplication,
   transpose, determinant, and inverse for inertia tensors.
2. `feat(physics): define rigid-body 6dof state` — inertial position/velocity, normalized
   orientation, and body-frame angular velocity.
3. `feat(parts): derive center-of-mass inertia properties` — mass, center of mass, and
   parallel-axis inertia assembled from the active PartGraph.
4. `feat(integrator): add coupled rigid-body rk4` — translational and rotational RK4 with
   Euler rigid-body angular dynamics.
5. `feat(vessel): prepare stateful inputs for coupled physics` — one-per-tick propulsion,
   propellant, hot-stage, crew, and gimbal preparation plus candidate-attitude drag.
6. `feat(physics): evaluate candidate-state forces and torques` — pure gravity, thrust,
   aerodynamic force, engine torque, aerodynamic attitude torque, and external wrench.
7. `feat(universe): wire opt-in coupled 6dof integration` — COM-based off-rails adapter and
   candidate-state landing/catch contacts; legacy and rails paths remain unchanged.
8. `test(universe): cover coupled 6dof dispatch` — verifies the opt-in path completes a
   scheduler tick without destroying the vessel or producing non-finite state.
9. `feat(telemetry): expose coupled 6dof state evidence` — records COM state, orientation,
   angular velocity, mass, and step timing for old/new parity work.
10. `test(physics): compare legacy and coupled coast states` — adds a COM-normalized parity
    comparator and a deterministic 50-step coast gate.
11. `docs(physics): record first parity probe` — records the coast evidence and the explicit
    boundary between a numerical sanity check and a production flight gate.
12. `test(physics): add powered ascent parity gate` — compares legacy and coupled paths for
    a deterministic thrust-and-propellant fixture, including mass depletion and telemetry.

## Runtime contract

`Universe.Coupled6DofIntegrationEnabled` is disabled by default. It must remain disabled
until deterministic telemetry comparisons and real framebuffer flight gates establish parity
for ascent, coast, and EDL. On-rails propagation is not changed by this switch.

## Validation

- Full simulation suite: 858/858 tests passing after the powered parity gate.
- Physics parity tests: 2/2 passing (coast and simplified powered ascent).
- Godot project build: 0 warnings, 0 errors.
- `git diff --check`: clean before each commit.

The first parity probe compares identical no-atmosphere initial states for 50 × 20 ms steps.
It remains within 0.1 m position error and 0.1 m/s velocity error, with finite attitude and
angular-rate errors. This is a coast sanity gate, not evidence of ascent or EDL parity.

## Current boundary

The powered fixture is intentionally smaller than the production Starship vehicle. It
proves that both paths consume the same propellant and integrate a thrusting rigid body
without a state divergence beyond the declared tolerance; it does not prove guidance,
actuator, atmospheric, or landing parity.

## Remaining work

- Extend the comparison to the production Starship ascent fixture and controlled EDL.
- Port or reconcile flap control, SAS/rate limiting, and RCS authority.
- Add controlled ascent and EDL telemetry gates before enabling the adapter in production.
