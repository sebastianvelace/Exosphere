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
13. `test(physics): add Flight 7 ascent parity gate` — reuses the real Flight 7 Block 2 part
    variant, including 33+6 engine configuration, production data masses, spool and fuel flow.
14. `test(physics): exercise controlled Flight 7 ascent parity` — adds a constant pitch command
    under the real Earth atmosphere, surface rotation and live gimbal servo state.
15. `test(physics): exercise closed-loop Flight 7 elevation parity` — follows a deterministic
    elevation ramp with attitude error and angular-rate feedback for five simulated seconds.
16. `test(physics): exercise Flight 7 engine-out parity` — fails one off-axis booster Raptor after
    spool-up and checks real asymmetric torque, angular response and legacy/coupled agreement.
17. `feat(physics): add deterministic engine-out recovery guidance` — detects an active-stage
    engine-out, computes an axis-pointing feedback command with rate limiting, and proves that
    recovery reduces angular rate without breaking legacy/coupled parity.
18. `feat(physics): add delayed engine-out sensor` — debounces the active-stage observation for
    100 ms before enabling recovery, then verifies the latency and long-window parity on Flight 7.

## Runtime contract

`Universe.Coupled6DofIntegrationEnabled` is disabled by default. It must remain disabled
until deterministic telemetry comparisons and real framebuffer flight gates establish parity
for ascent, coast, and EDL. On-rails propagation is not changed by this switch.

## Validation

- Full simulation suite: 864/864 tests passing after the delayed-sensor gate.
- Physics parity tests: 8/8 passing (coast, powered ascent, Flight 7 ascent, controlled pitch,
  closed-loop elevation, engine-out, immediate recovery and delayed-sensor recovery).
- Godot project build: 0 warnings, 0 errors.
- `git diff --check`: clean before each commit.

The first parity probe compares identical no-atmosphere initial states for 50 × 20 ms steps.
It remains within 0.1 m position error and 0.1 m/s velocity error, with finite attitude and
angular-rate errors. This is a coast sanity gate, not evidence of ascent or EDL parity.

## Current boundary

The simplified powered fixture is intentionally smaller than the production Starship vehicle.
The Flight 7 fixture adds the real repository configuration, part masses, 33+6 engine data,
spool and propellant flow for a short open-loop ascent. Both gates prove that the paths
consume equivalent propellant and integrate a thrusting rigid body within their declared
tolerance; neither proves guidance, actuator, long-duration atmospheric or landing parity.
The controlled gate is deliberately narrower: it holds a fixed pitch command for 1 second
near the surface, confirms live gimbal deflection and checks legacy/coupled attitude parity;
it does not yet prove a closed-loop guidance law.
The new closed-loop gate follows a five-second elevation ramp with attitude and rate feedback;
it is a deterministic attitude-reference exercise, not a complete ascent guidance, navigation
or dispersion model.
The engine-out gate removes one off-axis booster Raptor after 3 seconds of spool-up and checks
the resulting real per-mount torque rather than treating the failure as proportional thrust
loss. The recovery gate then detects the failed/live engine mix, writes a bounded axis-pointing
command and verifies lower angular rate against an identical no-recovery baseline. The delayed
sensor gate holds that command for four 20 ms samples and enables it on the fifth, representing
100 ms of onboard detection latency. The command deadband is numerical (`1e-6`), so a physically
small correction still reaches TVC allocation. This remains a deterministic injected-failure
test: it does not yet prove automatic onboard failure diagnosis, fault isolation, full
SAS/flap/RCS recovery or dispersed production ascent.

## Remaining work

- Extend the short Flight 7 comparison into a controlled, longer-duration production ascent
  fixture and then controlled EDL.
- Port or reconcile flap control, SAS/rate limiting, and RCS authority around the recovery
  controller, including automatic fault isolation and longer-duration ascent cases.
- Add controlled ascent and EDL telemetry gates before enabling the adapter in production.
