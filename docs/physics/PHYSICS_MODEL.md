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

### Body spin axis

Orbital elements use the J2000 ecliptic frame (+X vernal equinox, +Z ecliptic north).
`CelestialBody.RotationAxis` is the ecliptic normal tilted by `axial_tilt` toward +Y,
which for Earth is the north celestial pole. Seasonal solar declination, launch-site
daylight and the Moon's 18–29° inclination to the equator follow from it
(`PlanetaryAxisOrientationTests`). Other planets share Earth's tilt azimuth: their
obliquity is exact, their pole longitude is an approximation. Before 2026-10-18 the axis
was tilted from +Y and therefore lay in the orbital plane.

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
the adapter does not reduce the scheduler's normal coast/warp step. The `[G]` ascent and `[H]`
assist use the same clock, and an idle pilot stick does not overwrite an engaged autopilot's
command between control epochs.

Vessels integrated off rails work in their reference body's translating frame and subtract that
body's full rail acceleration, summed along its parent chain (the Moon's frame includes Earth's
acceleration toward the Sun). A 111 km lunar orbit integrated at real time holds within 241 m
over one revolution (`LunarFrameAccelerationTests`).

### Guided burns

`ProgradeEnergyBurn`, `LambertGuidedBurn` and `PeriapsisCaptureBurn` (in `Flight/`) fly
injections, course corrections and capture/circularization burns by writing only throttle and
attitude. Apollo 8 and Apollo 11 use them for TLI, a mid-course correction, LOI and lunar
circularization; `TranslunarInjectionScenarioTests` flies parking orbit to lunar orbit with no
state assignment. Historical launches release their hold-downs under the shared
`HoldDownReleasePolicy`. The Saturn V S-IC flies a pitch-over and then a zero-angle-of-attack
gravity turn (`GravityTurnGuidance`); the S-II/S-IVB fly time-to-go `ClosedLoopInsertionGuidance`
and cut off on the flown periapsis (`SaturnVAscentScenarioTests`: 181 x 195 km at T+714 s with
~67 t left for TLI). The open-loop S-IC elevation program it replaced paid ~660 m/s of steering
loss; transonic drag costs only ~35 m/s.

Lunar launches hold on the pad for a launch window (`Navigation/LunarLaunchWindow`). Flights
start at J2000, not on the historical date, so the Moon's geometry at [L] is arbitrary. A
coplanar TLI needs a parking orbit whose plane contains the Moon at arrival; at liftoff that
plane must also contain the pad, which fixes it, and Earth's rotation sweeps the required
azimuth through the day. The countdown resumes once the azimuth enters Apollo's 72°–108°
range. The S-IC flies along that plane and the S-II/S-IVB steer the horizontal velocity to be
gained in it, which also removes the Earth-rotation cross-track share. TLI is then planned from
the flown orbit (`LunarLaunchWindowTests`: azimuth 72.1°, plane error 0.44°, TLI 3.18 km/s).
Only a resumed ascent, which does not persist the window, still rotates the parking orbit into
the lunar plane by script, and logs it.

The return is flown the same way. `TransearthInjectionPlanner` searches one lunar revolution for
the burn epoch whose prograde-burn asymptote (rotated ν∞ = acos(−1/e) ahead of the burn point)
matches the v∞ an Earth-centred Lambert arc to the entry corridor needs; the SPS burn is cut on
that Moon-relative energy. The return coast is searched up to 1.5 × the historical 57.4 h: a
longer coast needs a smaller v∞, and with the SPS budget the flown sequence leaves (20.25 t at
TEI) the historical coast does not close. Three `TransearthCorrection` burns then retarget the
vacuum perigee to 38 km (the perigee of a 121.92 km, −6.5° corridor entry): one after leaving
the Moon's sphere of influence, trims 8 h and 2 h out where the corridor is progressively less
sensitive to velocity error (`TransearthReturnScenarioTests`: TEI 869 m/s with 0.9 t of SPS
propellant to spare, perigee 38.9 km, entry 11.0 km/s at −6.68°).

A trimmed blunt capsule flying alone (`capsule_trim_angle_of_attack_deg`, Apollo CM 16.7°) uses
capsule aerodynamics: heat-shield pressure acts along the body axis, so the force is q·S·C_A
along the axis and the trim tilt gives L/D = tan α ≈ 0.3; the restoring moment centres on the
trim axis, the effect of the real CM's offset centre of mass. `CapsuleEntryGuidance` holds the
trim attitude before the air bites, then only rolls the lift vector: its vertical share rises
when drag exceeds 4 g and falls while the capsule climbs, so it neither overloads nor skips out.
Apollo 8's entry peaks at 7.1 g (flight: 6.8 g) versus 16 g flown ballistically.

Pointing loops are linear PD laws on the attitude error, so their damping depends on the
vehicle's angular authority a: ω_n² = a·Kp and 2ζω_n = a·Kd. Gains tuned for a booster leave
a low-authority upper stage underdamped; the S-IVB (~0.09 rad/s² of J-2 TVC) at Kd 1.2 sits
at ζ ≈ 0.1, and a 30° / 0.3 rad/s separation transient grew into a saturated tumble that
reached orbit with 28 t instead of 67 t. Upper stages size Kd with
`AttitudeGuidance.DampingGainFor` from `Vessel.GetPitchYawAngularAuthority` (ζ ≈ 0.8).

### Low-orbit time warp

Low Earth orbits stay inside the residual thermosphere, so they are integrated off rails. An
idle coast whose periapsis clears the aerodynamic boundary by 10 km, with q < 0.5 Pa and
free-molecular stagnation heating below 2 kW/m² (`Universe.ResidualCoastMaxHeatFluxWPerM2`,
about 1.5 × the solar constant), uses 2 s coast steps and full warp. A 191 km parking orbit
sees ~0.9 kW/m²; the earlier 500 W/m² gate capped every orbit below ~225 km at x3.
`ResidualThermosphereWarpTests` checks that coast steps reproduce the 20 ms drag decay at
191 km and 400 km.

## Gravity and orbital mechanics

- Point-mass gravity uses \(\mathbf a=-\mu\mathbf r/r^3\).
- Earth data can include first-order zonal \(J_2\) gravity in the body equatorial frame,
  using the equatorial radius rather than the mean radius.
- Surface altitude/contact use the configured sphere or oblate WGS-style ellipsoid.
- Celestial bodies and vessels outside active force-sensitive conditions may use Keplerian
  on-rails propagation and patched-conic sphere-of-influence transitions.
- On-rails vessels advance attitude using their inherited world-space angular velocity with
  a constant-axis spin approximation. Gravity-gradient torque and torque-free Euler
  precession are not modeled on rails; bodies with significant tumble require full propagation.
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
- transonic/hypersonic continuity handling, with Mach from each atmosphere's own gas
  (`heat_capacity_ratio` and `molar_mass`; CO2 on Mars/Venus carries sound ~22% slower
  than Earth air at the same temperature);
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
mass, layout, schedule and impulse are explicit estimates. Released unpowered
satellites initially request central-body Kepler propagation; the normal force guard
wakes them to RK4 wherever residual atmospheric drag requires it. Active satellite
orbit raising and complete historical mission acceptance remain pending.

The exploration's **SKIP TO ENTRY** shortcut unlocks after all releases and advances
the existing controls through coast/deorbit to the first observed 120 km geodetic
entry interface, then pauses. It does not assign a checkpoint, refill resources or
change the original stages' 20 ms physics/control cadence. Each bounded batch may
integrate safe passive single-part payloads at <=0.5 s, retaining RK4 gravity and
residual drag; commands, entry/contact envelopes, high angular rates, propulsion,
crew and coupled-6-DoF mode fall back to normal integration. Payload states are
materialized at the committed epoch before rendering and systems sampling. Return
controllers do not query these released payloads during a batch. This is a numerical
coast approximation for the presentation shortcut, not a historical playback.
`PassivePayloadBatchTests` compare it against 20 ms propagation over both short and
100-minute moving-Earth coasts and exercise mid-batch wake and interrupted resume.

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
- Joint loads are quasi-static rigid-body statics: each joint carries
  `m_child·a − ΣF_ext(child side)`, with engine thrust on its engine parts and the
  remaining non-gravitational force spread by part length. Axial load is split into
  tension and compression (each with its own strength); lateral load is shear. Bending
  moment from aerodynamic angle of attack is not modeled.
- Legacy SAS damps body rates only through available authority (RCS floor or live
  differential TVC); it cannot remove a tumble faster than those actuators allow.
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
Projected wet-section widths apply quadratic cross-flow drag at each wet section
centroid. The width is the clipped circle's silhouette perpendicular to local
cross-flow, rather than diameter multiplied by submerged area fraction. Separate axial nozzle drag ramps
over an estimated 0.5 m entry depth. This is an engineering strip approximation,
not a calibrated hydrodynamic coefficient distribution. Point velocity includes
angular transport; rotating water includes body translation and rotation once.
Drag force and torque form a dissipative wrench in the mass-centre frame. Before
accepted contact, buoyancy/drag use the existing legacy translation/angular path.
After accepted contact, the water-only RK4 path jointly advances mass-centre
translation, orientation and body angular velocity. Each candidate state supplies
its own attitude and angular transport to the water solver. Actual part-graph mass
and inertia include remaining propellant; engines/resources prepare once per step.
`WaterMotionFrame` converts the rendered skirt datum to/from the mass centre,
including `omega cross offset` point velocity and the part-root-to-skirt offset.
Gravity, prepared thrust and aerodynamic loads use the candidate mass-centre state.
Water forces also contribute to proper-acceleration and structural diagnostics.
The generic rigid-floor clamp is bypassed only for actual opted-in wet contact.

This is a first-order inclined sealed-cylinder estimate, not slamming CFD, flooding
or a resolved nozzle/skirt/nose/flap geometry. The skirt offset is fixed at handoff;
the water-motion mass centre follows actual part-graph mass properties.
The dry-floor bypass uses either the exposed nozzle or lowest cylindrical hull
support, so side contact does not require the axial nozzle to be submerged. Sea swell is optical only; its
mean plane remains at geodetic altitude zero. The generic global coupled 6-DoF
configuration is still rejected by this controller; only the explicit post-contact
`WaterMotionEnabled` path is active. No global ascent/EDL mode is changed.

Three delivered sea-level engines must have performed the flip. Control progressively
removes lateral tilt below the diagnostic height and retains engine throttle floors,
monotone selection and original fuel. Water entry is accepted only while descending,
within 10 degrees upright and below 5 m/s exposed-point speed in the declared region.
Shutdown is commanded on that physical witness; spool-down still integrates. After
ten seconds the preview freezes as `SplashdownReached`, meaning observed bounded
water entry, not a stable floating vessel, recovered hardware or full mission success.
Free-motion tests preserve mass-centre velocity, angular momentum and rotational
energy. A ten-second production water fixture compares 20/10/5 ms steps and retains
wet contact through heeling. This supports the bounded sealed-hull estimate; it does
not validate real flooding, capsize timing, slamming/added mass, sea-state forces or
independent booster recovery. See the
[2026-10-03 water-motion audit](../audits/flight14_water_motion_2026-10-03.md).

## Flight 14 independent booster return estimate

The exploration factory now attaches independent Starship and Super Heavy
controllers to the same deterministic 20 ms pre-integration boundary. Separation
continues the original booster object, resources, engine failures and inertial
state. Camera observation never changes Universe ownership or the ship systems
runtime. Main-feed LOX exhaustion and Gulf-region low-speed contact are physical
transition gates; published clocks do not assign kinematics.

Protected landing LF/LOX remains in wet mass and is excluded from main-feed
consumption. Opening that feed only removes the partition; it adds no propellant
and repairs no failed engines. Both save formats preserve this inventory. The
current 200 t resource budget is an unvalidated engineering assumption within
the original 3.6 Mt launch loading, not measured header-tank capacity.

Intact root integrated avionics enables standalone booster command authority.
The optional standalone aerodynamic-centre offset overrides the attached-stack
selection only for that root; the estimated +5.44 m offset is not resolved
grid-fin actuator aerodynamics. Engine-first landing guidance subtracts actual
aerodynamic up-force from its thrust demand and conservatively retains capacity
for the later low-drag portion when selecting 11 → 5 → 3 engines. Actual lifecycle
and positive thrust witness delivery. Estimated boostback steering sweeps its
commanded elevation from +89° to −78° at 40% throttle, driven by normalized ideal
rocket delta-v progress `ln(m_start/m) / ln(m_start/m_end)`. The main-feed budget
sets `m_end`; the protected landing partition stays in wet mass. These angles and
throttle are engineering calibration, not measured SpaceX controls. A coarse
variable-mass, constant-gravity forecast selects only the horizontal bearing;
actual attitude, propellant and flight remain propagated by existing actuators
and forces. Exhausted feed cannot advance the sweep or certify thrust delivery.
No altitude/velocity is assigned or clamped. The landing deceleration margin and nominal arm altitude
are profile estimates, with a physical stopping-distance/startup gate. Lateral
landing damping uses commanded thrust acceleration rather than gravity.
Gravity, drag coefficients and engine
rating remain unchanged.

Accepted water contact opts into the existing bounded sealed-hull solver for
three estimated seconds. FTS then records retirement; explosive energy, fragments
and measured FTS delay are not simulated. The full mission and original stage
continuity are tested; matching the flown boostback altitude, descent timing,
landing inventory and near-water visuals remains open. See
[the booster return audit](../audits/flight14_booster_return_2026-10-03.md) and
[the subsequent guidance calibration](../audits/flight14_booster_calibration_2026-10-03.md),
and [the coast/reference refinement](../audits/flight14_booster_coast_calibration_2026-10-04.md).
Terminal HUD altitude and speed use the immutable retirement witness. Undefined
force/orbit diagnostics display as unavailable; the inertial wreck is not
re-evaluated against a later moving Earth. Returning observation to Starship
restores live instrumentation without changing the physics owner.
