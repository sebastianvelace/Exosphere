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
19. `feat(physics): integrate telemetry fault isolation` — samples live peer-engine telemetry,
    preserves the stage demand after a failed engine, and requires a persistent mount-geometry
    torque residual before the delayed recovery sensor can enable automatic response.

20. `feat(physics): port SAS, RCS floor and pilot rate envelope to the coupled path` — the
    coupled force evaluator adds the reaction-control floor as a body torque (only the gap
    the live engine moment does not cover, converted with the integrator's own inertia
    tensor), and `Vessel.ApplyCoupledControlPostStep` applies SAS damping and the 20°/s
    pilot envelope once per control step after integration. Before this, a coasting coupled
    vessel ignored attitude commands, SAS never damped a residual rate, and a held full
    command spun Flight 7 to 0.87 rad/s instead of saturating at 0.35 rad/s.

21. `test(physics): add touchdown and tower-catch contact parity gates` — the six-leg Starship
    gear (nominal drop and the measured EDL pre-contact state with lateral drift and 2.5° tilt)
    and the two-pin tower cradle (centred and 1.2 m offset) settle on both paths: final altitude
    or position within 0.25 m, settled attitude within 0.02 rad, peak foot load within ±25%,
    and the same caught/settled gates. These are contact gates from a short pre-contact state;
    they do not yet replace a controlled descent-to-contact parity run.

## Inertia-model divergence (blocks longer parity gates)

A 30 s guided Flight 7 ascent with SAS windows ends with matching attitude (0.0025 rad) and
rate, but passes through a 0.23 rad transient and finishes 392 m apart. The coupled body
turns ~1.34× faster for the same engine torque: the legacy tick divides by an envelope
inertia (`PartGraph.TransverseMomentOfInertia`, a uniform solid cylinder of the full stack),
while the coupled integrator uses the per-part, parallel-axis tensor about the true centre of
mass. Legacy-envelope / coupled-tensor pitch inertia ratios on the shipped vehicles:

| Vehicle | Pitch/yaw | Roll |
| --- | --- | --- |
| Starship Flight 7 / Flight 12 V3 | 1.35 / 1.34 | 1.00 |
| Falcon 9 Block 5 standard / extended | 1.62 / 1.82 | 1.97 |
| New Glenn 7×2 | 1.90 | 1.00 |
| Titan II GLV-8 (Gemini 8) | 1.68 | 1.00 |
| Mercury-Redstone 3 / Mercury-Atlas 6 | 1.83 / 2.28 | 1.35 / 1.02 |
| Saturn V AS-503 / AS-506 | 2.33 / 2.35 | 1.04 |
| Agena 5003 / LM-5 Eagle | 1.08 / 1.10 | 1.00 / 1.05 |

The tensor is the physically representative model; the envelope over-states pitch inertia
on every launcher. Longer controlled-ascent and EDL parity cannot close until both paths read
one inertia model. Moving the legacy path onto the tensor makes every launcher 1.3–2.3× more
responsive in pitch, so it must be done together with re-validating `[G]` ascent, EDL and the
historical directors, not as a tolerance change.

## Runtime contract

`Universe.Coupled6DofIntegrationEnabled` is disabled by default. It must remain disabled
until deterministic telemetry comparisons and real framebuffer flight gates establish parity
for ascent, coast, and EDL. On-rails propagation is not changed by this switch.

## Validation

- Full simulation suite: 868/868 tests passing after telemetry fault-isolation integration.
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
small correction still reaches TVC allocation. The sensor now also needs live peer-thrust
diagnosis and a persistent mount-geometry torque residual; the gate still uses a deterministic
injected failure and does not prove dispersed production ascent, full SAS/flap/RCS recovery or
long-duration fault management.

## Remaining work

- Extend the short Flight 7 comparison into a controlled, longer-duration production ascent
  fixture and then controlled EDL.
- Unify the legacy envelope inertia with the coupled per-part tensor (see the inertia-model
  divergence above) and re-validate guidance gains before extending the duration of gates.
- SAS, RCS floor and the pilot rate envelope are ported (step 20). The legacy post-aero rate
  envelope is intentionally not ported; EDL parity must confirm the RK4 integrator does not
  need it under high dynamic pressure.
- Add controlled ascent and EDL telemetry gates before enabling the adapter in production.
  Contact gates (gear and cradle) are closed (step 21); a controlled descent from the flip to
  contact still needs the inertia unification first.
