# Exosphere physics model

> **Current technical source of truth — 2026-09-22**
>
> This document describes the physics that is implemented today, the deliberately simplified
> parts, and the gates required before the experimental coupled 6-DoF path can become default.
> It complements the code and tests; it does not replace them.

## Design contract

Exosphere is a real-time mission simulator, not a CFD or finite-element solver. Its goal is
physical causality at mission scale: the vehicle should respond to the right forces, moments,
mass changes, frames, atmosphere, contact geometry and thermal loads, with approximations
visible and testable rather than hidden behind visual effects.

The simulation layer is pure C# and obeys these invariants:

- SI units: metres, metres per second, kilograms, seconds and newtons.
- `double` precision for state, time, geometry and integrals.
- Internal angles in radians; JSON presentation/data angles may be degrees.
- `Universe.CurrentTime` is seconds from J2000.
- No Godot types or renderer state in `ExosphereSimulation/`.
- A force evaluator may inspect a candidate RK4 state, but must not mutate engines,
  propellant, thermal state, controls, contacts or the simulation clock.

## Simulation architecture

```text
JSON bodies/parts
        │
        ▼
CelestialBody + PartGraph ──► mass / CoM / inertia / engine geometry
        │                                      │
        ▼                                      ▼
Universe scheduler ───────────────► Vessel state and force evaluation
        │                                      │
        ├── Kepler/on-rails propagation       ├── legacy translational RK4
        │                                      └── opt-in coupled rigid-body RK4
        ▼
SimulationBridge ──► Godot presentation, HUD, camera and VFX
```

The game layer reads physical state and sends commands. It does not own the authoritative
orbital or rigid-body integration.

## Frames and state

### Inertial/world frame

Celestial bodies and vessels use an inertial simulation frame. A vessel's legacy
`Position`/`Velocity` are attached to its construction datum. Celestial bodies have their own
position and velocity and are propagated at the simulation epoch.

### Body frame and orientation

`Quaterniond` maps body-local vectors into world vectors. The vehicle longitudinal axis is
local `+Y`; thrust, drag attitude and contact lever arms are transformed through that
orientation. Angular velocity for the coupled solver is stored in the body frame so Euler's
rigid-body equation can use the body inertia tensor directly.

### Datum versus centre of mass

The legacy gameplay contract keeps `Vessel.Position` as a datum. The coupled solver integrates
the physical centre of mass (CoM):

\[
  \mathbf r_{CoM}=\mathbf r_{datum}+R(q)\,\mathbf r_{CoM,body}
\]

\[
  \mathbf v_{CoM}=\mathbf v_{datum}+\boldsymbol\omega\times
  (R(q)\,\mathbf r_{CoM,body})
\]

After the coupled step the datum is reconstructed. This prevents an attitude change from
silently moving the physical contact or thrust point.

## Equations of motion

### Translation

For a candidate state, the net force is assembled in world coordinates:

\[
  m\,\dot{\mathbf v} =
  \mathbf F_{gravity}+\mathbf F_{thrust}+\mathbf F_{aero}+\mathbf F_{contact}
  +\mathbf F_{external}
\]

The reference-body rail acceleration is removed from the relative off-rails state where the
scheduler uses a moving body frame. The final absolute state is reconstructed from the body
state at the evaluated epoch, not from a stale end-of-tick body position.

### Rotation

The coupled path uses Newton–Euler rigid-body dynamics:

\[
  \dot{\mathbf q}=\frac{1}{2}\,\mathbf q\otimes(0,\boldsymbol\omega)
\]

\[
  I\,\dot{\boldsymbol\omega}+
  \boldsymbol\omega\times(I\boldsymbol\omega)=\boldsymbol\tau
\]

The attitude quaternion is normalized after each RK4 intermediate state. The inertia tensor
is assembled from the active part graph and translated to the CoM using the parallel-axis
theorem.

### Integration policy

The production scheduler currently keeps the legacy path as the default. The experimental
`Universe.Coupled6DofIntegrationEnabled` switch selects the coupled solver only for off-rails
physics; it never changes Kepler/on-rails propagation.

Both paths use fourth-order Runge–Kutta at the normal 20 ms physics step. The coupled evaluator
is called at all four RK4 stages so attitude-dependent aerodynamics, thrust direction and
contact geometry respond to the candidate state.

## Gravity and orbital mechanics

- Point-mass gravity uses \(\mathbf a=-\mu\mathbf r/r^3\).
- Earth data can include first-order zonal \(J_2\) gravity in the body equatorial frame,
  using the equatorial radius rather than the mean radius.
- Surface altitude/contact use the configured sphere or oblate WGS-style ellipsoid.
- Celestial bodies and vessels outside active force-sensitive conditions may use Keplerian
  on-rails propagation and patched-conic sphere-of-influence transitions.
- On-rails Kepler propagation intentionally ignores live \(J_2\) perturbations; active RK4
  vessels feel `CelestialBody.GetGravityAt`. This is an explicit model boundary, not an
  unreported contradiction.

## Atmosphere and aerodynamics

Atmospheric force uses relative air velocity, including the rotating body's surface velocity:

\[
  \mathbf v_{air}=\mathbf v_{vessel}-\mathbf v_{body}-\boldsymbol\omega_{body}\times\mathbf r
\]

Dynamic pressure is:

\[
  q=\frac{1}{2}\rho\lVert\mathbf v_{air}\rVert^2
\]

The current model includes:

- data-driven atmospheric layers and planet-specific composition/gravity inputs;
- a residual thermosphere density tail above the nominal atmosphere boundary;
- orientation-dependent cylinder drag with axial/broadside area blending;
- transonic/hypersonic continuity handling;
- body lift driven by angle of attack, with `CL` proportional to `sin(2α)`;
- aerodynamic-centre attitude torque in the coupled evaluator;
- pressure-corrected engine thrust and Isp.

This is a mission-scale aerodynamic model. It is not a Navier–Stokes solution and does not
resolve boundary-layer transition, local shock interactions or plume impingement.

## Propulsion and mass flow

Engine and vehicle data live in JSON. At a given ambient pressure, the engine model applies a
pressure-corrected thrust envelope between sea-level and vacuum values. Mass flow follows:

\[
  \dot m=\frac{F}{I_{sp}\,g_0}
\]

The current propulsion model also includes:

- engine spool/startup and shutdown transients;
- mixture-ratio-driven liquid-fuel/oxidizer consumption;
- discrete engine-instance lifecycle and selected engine count;
- gimbal servo state and differential TVC allocation;
- per-mount thrust vectors and genuine \(\boldsymbol\tau=\mathbf r\times\mathbf F\);
- hot-stage overlap before mechanical separation;
- fuel depletion as a resource boundary rather than a fake persistent engine failure.

The VAB still represents a stage as an engine part while the simulation expands its configured
engine count into physical instances. This is a deliberate data/UI boundary, not a claim that
all motors are one physical point.

## Thermal, structural and contact physics

- Reentry heating is driven by atmospheric density, speed and a Sutton–Graves-style stagnation
  correlation with attitude-dependent characteristic radius.
- TPS skin and load-bearing structure are separate thermal nodes coupled by conduction and
  radiative loss.
- Thermal damage can trigger progressive per-part breakup and control-authority loss.
- Landing contact uses multiple points, spring/damper normal response, friction, lever arms,
  torque and suspension travel.
- Tower catch uses explicit pin/contact geometry, relative velocity, alignment and a moving
  cradle target.
- Surface settling is a gameplay/simulation boundary for low-speed support; orbital-speed
  impacts remain destructive.

## Current realism status

| Area | Current state | Confidence |
|---|---|---|
| Orbital gravity, SOI and Kepler rails | Implemented and regression-tested | High within patched-conic scope |
| WGS-style surface and Earth `J2` | Implemented for active RK4 bodies | High; rails boundary documented |
| Mass, CoM and inertia | Derived from `PartGraph` | High for current part primitives |
| Coupled 6-DoF RK4 | Opt-in, with force/contact stages | Proven in isolated and powered Flight 7 fixture |
| Legacy vs coupled coast | 50 × 20 ms parity gate passes | Narrow gate only |
| Legacy vs coupled powered ascent | 25 × 20 ms powered gate passes | Simplified fixture only |
| Legacy vs coupled Flight 7 ascent | 100 × 20 ms gate passes with real parts and propellant | Open-loop short ascent only |
| Flight 7 controlled pitch in Earth atmosphere | 50 × 20 ms gate passes with surface rotation and gimbal | First controlled gate only |
| Flight 7 closed-loop elevation program | 250 × 20 ms gate passes with attitude/rate feedback | First deterministic guidance gate only |
| Controlled Starship ascent/EDL parity | Not yet closed | Open |
| SAS, flaps and RCS in coupled path | Not yet fully equivalent | Open |
| CFD, plume-flow interaction and slosh | Out of current solver scope | Deliberate approximation |

## Evidence gates

Run the pure simulation checks before changing shared physics:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --no-restore
dotnet build Exosphere.csproj --nologo -v quiet
```

For the coupled migration, the required order is:

1. finite-state and force-evaluator unit tests;
2. mass/CoM/inertia invariants;
3. coast parity from identical initial states;
4. powered ascent parity with propellant depletion;
5. Starship hardware ascent parity;
6. controlled EDL parity;
7. real-framebuffer confirmation after numerical parity, never before.

The coupled path remains disabled by default until the final three gates are closed.
