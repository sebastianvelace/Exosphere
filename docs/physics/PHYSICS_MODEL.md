# Exosphere physics model

> **Current technical source of truth — 2026-09-25**
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

Atmospheric EDL control is evaluated on a separate, explicit 20 ms simulation-time clock. When
that controller is active, `Universe` splits a shorter or longer outer-frame request at every
control boundary and calls `IPhysicsStepController.BeforePhysicsStep` before force integration.
The Godot `SimulationBridge` is only the adapter; the render loop no longer advances EDL timers
or writes its flight commands. Consequently, 30, 60 and 120 FPS outer ticks produce the same
guidance epochs and hold each command for one declared control interval. When EDL is inactive,
the adapter does not reduce the scheduler's normal coast/warp step.

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

### Orbit and entry diagnostics

`EntryFlightDiagnostics` keeps body-centred inertial quantities separate from rotating-
atmosphere quantities. It exposes point-mass specific energy and angular momentum alongside
Mach, dynamic pressure, flight-path angle, angle of attack, aerodynamic bank, modeled L/D,
load and stagnation heat flux. These values are diagnostics, not extra forces. The normal
orbital-reentry harness logs them together with EDL command cadence so timing-dependent
guidance cannot pass as a stable physical trajectory.

Undefined aerodynamic directions are reported as `NaN`, not zero. A presentation fixture
that assigns attitude directly cannot satisfy a physics acceptance gate.

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
- aggregate four-body-flap authority driven by dynamic pressure and the shared
  `FlapActuatorState` in both legacy and coupled paths;
- pressure-corrected engine thrust and Isp.

The residual thermosphere keeps pressure and density coupled through the ideal-gas law
(p=\rho R T/M). This matters even when the aerodynamic force is small: the same ambient
state must drive drag, engine pressure thrust and plume expansion without an artificial
pressure discontinuity at the nominal 140 km flight boundary. The tail remains a
mission-scale approximation; it is not an NRLMSISE atmosphere.

This is a mission-scale aerodynamic model. It is not a Navier–Stokes solution and does not
resolve boundary-layer transition, local shock interactions or plume impingement.

The renderer's exposed stainless-steel shader uses Godot's opaque metallic PBR path, with
Fresnel handled by the engine and a bounded roughness variation for rolled/brushed stock.
Anisotropic highlights are intentionally not enabled until the procedural meshes provide
tangents; enabling that output without tangents makes the renderer fall back with warnings.
This is an optical material approximation, not a measured Starship BRDF. Heat-shield tiles
remain dielectric and high-roughness; their emissive floor is only a bounded readability
fallback for the compatibility renderer.

### Evidence basis and deliberate boundaries

The implementation is constrained by the following primary references:

- [NASA Dynamic Pressure](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/dynamic-pressure-2/)
  for (q=\frac{1}{2}\rho V^2) and the Max-Q interpretation;
- [NASA Rocket Aerodynamics](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/rocket-aerodynamics/)
  for drag/lift integration over the vehicle and the centre-of-pressure/centre-of-mass
  moment;
- [NASA GSFC Planetary Spectrum Generator](https://psg.gsfc.nasa.gov/helpatm.php) for
  bidirectional reflectance/BRDF as a function of incidence, emission and phase angle;
- [NASA Plume Surface Interaction](https://www.nasa.gov/plume-surface-interaction/) and
  [NASA landing-gear impulse/momentum analysis](https://ntrs.nasa.gov/citations/19930083424)
  for the unresolved plume–surface and touchdown-load work.

Those references justify the current force and material contracts; they do not justify
inventing an uncalibrated plume back-pressure force, a measured Starship BRDF, or hardware
landing-leg coefficients. The simulator therefore keeps plume impingement as an explicit
open boundary and labels landing gear values as simulator estimates in the part data.

### Body-flap actuator contract

The four flaps are represented by three semantic channels (`pitch`, `yaw`, `roll`) because the
current mission-scale force model computes their combined hinge authority rather than resolving
each hinge and local panel flow independently. `FlapActuatorState` stores normalized deflection
in `[-1, 1]`; `FlapActuatorState.Advance` moves each channel toward the command with a rate
limit and saturates it before the aerodynamic torque is evaluated. The aggregate model uses a
52° maximum geometric envelope and a 25°/s actuator-rate estimate. The envelope matches the
current renderer geometry; the rate is explicitly a simulator estimate, not a published SpaceX
specification.

The state advances once per physics tick before coupled RK4 candidate evaluation. Consequently,
all four RK4 stages read the same physical actuator position, while the legacy path applies the
same position to its angular-acceleration term. Releasing the command does not teleport the
flaps to neutral: residual deflection slews back and can continue producing authority during
that interval. The renderer now consumes the shared pitch/roll actuator state for control
deflection; aerodynamic base deployment remains a presentation approximation driven by the
same dynamic-pressure and belly-alignment signals. EDL's scripted catch corridor may provide a
temporary `FlapCommandOverride` while TVC/RCS is neutralized for its attitude snap, so the
surface state remains physically observable instead of being silently forced to zero. Visual
pose parity still requires a real framebuffer gate.

### Entry lift policy

Atmospheric interface starts from the nominal 70° **lift-up** attitude. The corridor law keeps
that state inside its crossrange/downrange dead bands, banks the lift vector toward a measured
crossrange miss and reverses lift toward the body only when the predicted footprint has passed
the target. Continuous lift-down is not a neutral catch posture: the 120 km physical-entry gate
measured a 29.65 g plunge and excessive residual speed at 30 km. The lift-up policy passes the
bounded entry envelope without changing aerodynamic coefficients.

The physical-entry fixture declares a body-centred inertial speed. Earth rotation is
already contained in that vector. Adding it again on an eastward track lifts the vehicle
above circular speed and the entry skips near 80 km. The footprint is then propagated
with the resulting atmosphere-relative speed.

Catch guidance predicts that footprint with `EntryCorridorPropagation`: a spherical-planet
point mass at the nominal 70° angle of attack, using the same `AerodynamicsModel` drag and
lift as the vessel. The footprint is a central angle, compared with the target chord
through arcsin, so a vehicle still short of the site is not treated as an overflight.
Curvature uses inertial speed, airspeed plus local eastward rotation. Bank zero is the
lift-up reference. Latitude is held for the duration of one prediction. The vacuum
`EstimateTimeToGround` remains only the no-drag bound. A vertical-lift floor still limits
how far an overflight command may bank toward the body. The rendered non-demo catch and
the coupled 6-DoF migration remain open.

The trajectory envelope also bounds the requested **vertical lift fraction** using measured
radial speed and the local ballistic radial acceleration
`-mu/r^2 + v_inertial_tangent^2/r + a_drag_radial`. A 20 s feedback horizon targets
`-max(100 m/s, 0.035 * airspeed)` with a 150 m/s descent deadband for footprint steering.
The lift fraction remains in [-0.15, 1]; below 1 km/s the terminal EDL policy owns attitude.
These are reduced-order guidance settings, not measured Flight 14 parameters. The existing
attitude reference slew, flap limits and force integrator still own the response; no position,
velocity, gravity or aerodynamic force is clamped. An excessively steep entry may still have
unsafe loads, and physically commanded skip entries remain possible.

The lateral bank fallback is the current `up × airflow` direction, with its sign retained
from the previous lift reference. Removing only the up component from a previous lift vector
is insufficient: as airflow turns, that vector can retain axial direction which the aerodynamic
model later removes, silently erasing bank authority. The reference is retained across control
epochs instead of being cleared before bank-side continuity is evaluated.

The production entry controller holds this outer-loop footprint prediction for at
most 0.2 simulation seconds above 25 km. Vessel/body changes, time rewind or a
body-relative displacement over 2 km refresh it immediately. Below 25 km it refreshes
every control epoch. Attitude commands, flap actuators and forces continue at their
existing 20 ms cadence; the hold changes forecast latency, not force integration.
See the [2026-09-30 continuous-entry audit](../audits/atmosphere_reentry_visual_physics_2026-09-30.md)
for the validated envelope and remaining landing boundaries.

## Enclosed payloads and release

A part with `internal_payload: true` is enclosed while it is a non-root child of
its carrier. It contributes mass, CoM and inertia, but not outer length, diameter,
nose radius, aerodynamic coefficient or direct free-stream thermal heating.
Internal heat conduction is not modeled. Root status exposes a separated payload
without a separate runtime shielding flag; all other parts retain prior behavior.

`Vessel.DeployPayload` transfers the original subtree into an independent vessel,
using mass-weighted position offsets and equal/opposite opening impulses. Linear
momentum and aggregate point-mass center are conserved, with inherited spin
transport included in relative velocity. This does not establish conservation of
full rigid-body angular momentum for a resolved conveyor/door mechanism.

The Flight 14 diagnostic carries 26 payload parts through powered ascent, then
releases them only after orbit, shutdown and angular-rate checks. Per-satellite
mass, layout, schedule and impulse are explicit estimates; released unpowered
satellites use the existing central-body Kepler approximation. Gameplay/save
integration, active satellite orbit raising and complete mission return are pending.

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
| Flight 7 booster engine-out | 150 × 20 ms warm-up + 100 × 20 ms asymmetric response | First engine-out parity gate |
| Flight 7 engine-out recovery | Same warm-up/response window; active-stage detection, axis feedback and command slew | First deterministic recovery gate only |
| Flight 7 delayed engine-out recovery | Same window; 100 ms onboard detection latency before feedback | First sensor-latency recovery gate only |
| EDL guidance cadence | Fixed 20 ms simulation clock before force integration; 30/60/120 FPS epoch parity | Pure scheduler gate; rendered physical entry pending |
| Entry footprint prediction | Spherical point-mass propagation with the production drag/lift model | Reduced-order; no winds, no 6-DoF, rendered catch still open |
| Controlled Starship ascent/EDL parity | Not yet closed | Open |
| Flap actuator state and torque in legacy/coupled paths | Shared state, rate limit and saturation; aggregate four-surface model | Unit + controlled-pitch gate |
| Renderer flap pose parity | Renderer still derives a separate pose from q/belly/input | Open |
| SAS and RCS in coupled path | Not yet fully equivalent | Open |
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

## Flight 14 bounded water-entry estimate

The playable engineering exploration opts into `WaterContactSolver` during its
terminal flip. The older powered-return diagnostic still stops at 100 m by default.
No shared vessel receives water loads without explicit configuration and a matching
Earth body-fixed region. The current region (25..35 N, 165..140 W) is open northern
Pacific context, not measured SpaceX coordinates or geographic targeting.

The skirt datum matches the standalone renderer. The lowest exposed nozzle witness
is at -3.78 m; a sealed cylinder uses the declared vehicle diameter/length,
with its centre-of-mass lever derived from the part graph at landing handoff.
Circular sections are clipped against the local geodetic mean sea plane at the
actual orientation, including horizontal and inverted poses. Sixteen-point
Gauss-Legendre quadrature splits the axial integration at dry/full-section
boundaries to retain shallow penetration at near-vertical attitude. Displaced
volume and its first moment set `Fb = rho * displacedVolume * g` and its lever arm.
Projected side strips (`2R dy`, weighted by wet section fraction) apply quadratic
cross-flow drag at each wet section centroid. Separate axial nozzle drag ramps
over an estimated 0.5 m entry depth. This is an engineering strip approximation,
not a calibrated hydrodynamic coefficient distribution. Point velocity includes
angular transport; rotating water includes body translation and rotation once.
Drag force and torque form a dissipative wrench in the mass-centre frame. Buoyancy and drag are evaluated
at RK4 translation stages; external torque uses the existing legacy angular tick.
Water forces also contribute to proper-acceleration and structural diagnostics.
The generic rigid-floor clamp is bypassed only for actual opted-in wet contact.

This is a first-order inclined sealed-cylinder estimate, not slamming CFD, flooding
or a resolved nozzle/skirt/nose/flap geometry. Lever geometry is frozen at handoff.
The dry-floor bypass uses either the exposed nozzle or lowest cylindrical hull
support, so side contact does not require the axial nozzle to be submerged. Sea swell is optical only; its
mean plane remains at geodetic altitude zero. Coupled 6-DoF water parity is unverified
and the Flight 14 controller rejects that configuration. No global mode is changed.

Three delivered sea-level engines must have performed the flip. Control progressively
removes lateral tilt below the diagnostic height and retains engine throttle floors,
monotone selection and original fuel. Water entry is accepted only while descending,
within 10 degrees upright and below 5 m/s exposed-point speed in the declared region.
Shutdown is commanded on that physical witness; spool-down still integrates. After
one second the preview freezes as `SplashdownReached`, meaning observed bounded
water entry, not a stable floating vessel, recovered hardware or full mission success.
Flooding, capsize, sea-state forces and independent booster recovery remain open.
Inclined force geometry alone does not validate capsize: the legacy integration
advances the skirt datum independently of the angular step. A longer post-contact
motion needs mass-centre translation/rotation conservation and time-step convergence
before extending the current one-second preview. See the
[2026-10-03 inclined-water audit](../audits/flight14_inclined_water_2026-10-03.md).
