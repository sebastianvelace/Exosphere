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

## Inertia-model divergence (resolved by step 22)

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

22. `fix(physics): read legacy inertia from the per-part tensor` — `PartGraph`
    `TransverseMomentOfInertia` (mean of I_xx and I_zz) and `AxialMomentOfInertia` (I_yy) now
    come from the same parallel-axis tensor the coupled integrator uses, so both paths divide
    torque by one model. Re-validation across the full suite: 1166/1174 passed on the first run;
    the eight failures and their resolutions:
    - Saturn V ascent (AS-503, disturbed S-II separation, AS-506, lunar launch window): the S-IC
      used a fixed damping of 1.2 tuned to the over-stated inertia. Every stage now uses an
      authority-scaled gain at ζ 0.7 (`Apollo8FlightProfile.AscentDampingGain`), in both the
      test flight and `HistoricalFlightProfileController`. AS-503 inserts at T+696 s with
      68.5 t (historical ~T+685 s, ~70 t); AS-506 at T+696 s with 70.4 t and 3 299 m/s of TLI
      capacity (flight: T+709 s, 75.7 t). ζ 0.6 loses the disturbed separation; ζ ≥ 0.8 costs
      4–20 t.
    - Starship Ship-only gimbal authority rises from ~0.09 to 0.216 rad/s² because its mass sits
      low; the test range was moved and its ordering assertions kept.
    - Stage-separation conservation fixture parts had no attachment nodes, so every part sat at
      the root and the tensor lost the stack's parallel-axis term; the fixture now declares
      physical stack nodes (every catalog part already does).
    - Flight 14 return: the within-model periapsis calibration against the T+9884 s broadcast
      anchor was re-fitted for the new dynamics (see the return estimate JSON).

23. `test(physics): add 30 s guided Flight 7 ascent parity gate` — closed-loop pitch program with
    one-second SAS windows every ten seconds under the real Earth atmosphere. Before step 22 it
    ended 392 m apart with a 0.23 rad transient; it now stays within 100 m, 10 m/s and 0.05 rad
    throughout.

24. `test(physics): add terminal flip and landing-burn parity gate` — the catalog Ship
    (command, tank, engines, six-leg gear) starts at 600 m, 70 m/s down and 60° from vertical,
    and both paths fly the same EDLController-shaped law: axis pointing with lateral-drift cant,
    descent-rate profile with gravity feed-forward, and the monotone 3→2→1 engine step-down.
    After the flip both agree within about 1 m of altitude and 0.1 m/s of descent rate at
    T+40 s; both touch down softly (1.3 and 2.4 m/s), settle upright and stay inside the gear's
    2.5 MN load limit. Settle time is not compared: the throttle floor and discrete engine
    step-down turn a one-metre difference into several seconds of hover.

    During rapid rotation the two paths also report different reference-point velocities: the
    legacy tick rotates the vessel about its datum and integrates the datum as if it were the
    centre of mass, while the coupled path rotates about the true centre of mass and derives the
    datum velocity with ω×r. This shows up mid-flip (several m/s) and vanishes once rotation
    stops; making legacy rotate about the centre of mass is the next fidelity item.

25. `fix(physics): rotate the legacy tick about the centre of mass` — in free flight the legacy
    off-rails step now carries the centre-of-mass state through the attitude update and the
    translational RK4 and rebuilds the datum with the step-start offset (datum = COM − q·c₀,
    v_datum = v_COM − ω×q·c₀), the same convention as the coupled path; propellant drain that
    moves the centre of mass does not move the structure. Under landing, catch or water contact
    the datum stays the pivot: the split legacy step applies contact torque once per step,
    outside RK4, and rotating about a centre of mass ~20 m above the feet drove them into the
    ground (the terminal-descent gate tipped 0.35 rad and overloaded the gear). A vehicle on its
    legs or pins pivots about the loaded contact anyway. The terminal gate now agrees within
    3 cm and 0.001 m/s at T+40 s (was ~0.9 m), with a new mid-flip check on the datum velocity
    (≤ 0.25 m/s at T+2 s while rotating); its tolerances were tightened to 0.5 m / 0.1 m/s.

26. `test(physics): add flap-controlled entry parity gate` — the Flight 12 V3 Ship enters at
    75 km, 7.3 km/s and −1°, belly-first at 70° with lift up, engines off and SAS off; the four
    body flaps with the Flight 14 static-moment trim are the only attitude actuators. After
    60 s (~420 km down-range, dynamic pressure building to ~2.2 kPa) the paths end 7 m apart in
    altitude, 0.9 m/s in speed and ~25 m in position, both hold the trim within 0.93°, and peak
    angular rates match to 0.0001 rad/s. Gates: 50 m, 5 m/s, 150 m, 3° tracking, 2% peak q.
    This is the first evidence that the coupled RK4 does not need the legacy post-aero rate
    envelope under entry dynamic pressure. A second case flies the subsonic belly-flop: 3 km,
    80 m/s straight down, broadside, 20 s on flaps to ~1.56 km at ~3 kPa. The paths end 0.14 m
    apart in altitude, 0.003 m/s in speed and ~2 m in position, with peak tracking 4.3° / 4.1°,
    final 0.70° / 0.65° and peak rates 0.090 / 0.088 rad/s. The flip and landing burn that
    follow are covered by step 24.

27. `test(physics): add continuous entry-to-belly-flop parity gate` — the production Flight 14
    descent law (`Flight14DescentController.ComputeGuidanceCommand`, now a public pure step:
    bank-limited hypersonic corridor, 55°→90° angle schedule, slewed belly-first reference,
    flap trim) flies the V3 Ship from the 90 km handoff (7.3 km/s, −1.5°) to the 3 km
    belly-flop endpoint on both paths, through Mach 1. Measured: 913.5 / 913.9 s to the endpoint,
    Mach 1 at 752.0 / 752.3 s, peak q 6 347 / 6 330 Pa, peak load 1.950 / 1.945 g, peak heat
    flux within 0.13%, peak and transonic angular rates equal to 1e-4 rad/s, end speed equal to
    0.001 m/s, ground track 1.9 km apart after ~5 000 km. Gates: 3 s, 2% on peaks, 0.005 rad/s,
    1 m/s, 5 km. With step 24 this covers entry to touchdown, in two segments.

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
- With one inertia model (step 22), the 30 s ascent gate (step 23), the terminal descent gate
  (step 24), the centre-of-mass pivot (step 25) and the 60 s flap entry gate (step 26), remaining
  gate is a full production ascent through staging. Entry is covered from the 90 km handoff to
  the belly-flop (step 27) and from the flip to touchdown (step 24); the hand-off between them
  (the flip itself with engine relight) is compared only from the 60° post-flip state.
- SAS, RCS floor and the pilot rate envelope are ported (step 20). The legacy post-aero rate
  envelope is intentionally not ported; the flap entry gates (steps 26–27, including the
  transonic band) show the RK4 integrator does not need it.
- Add controlled ascent and EDL telemetry gates before enabling the adapter in production.
  Contact gates (gear and cradle) are closed (step 21); a controlled descent from the flip to
  contact still needs the inertia unification first.
