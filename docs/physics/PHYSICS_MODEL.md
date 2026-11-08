# Exosphere physics model

> **Current technical source of truth — 2026-11-06**
>
> This document describes the physics that is implemented today, the deliberately simplified
> parts, and the evidence gates for the production coupled 6-DoF path.
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

### F4 variable-mass and tidal dynamics (production)

Mass geometry. One decomposition (`Parts/PartMassGeometry`) feeds the composite mass
properties, each part's own centre and the continuous candidate sampler: the dry structure
at its declared centre (`dry_mass_center_y_m`) with its declared central gyration
(`dry_gyration_m`) or the part's uniform cylinder; each main resource as a settled liquid
column on its declared tank's equivalent flat bottom (`propellant_tanks`: aft end, top,
radius, density; NASA SP-106 equal-volume bottom) or, undeclared, the part's uniform
cylinder; header tanks as point masses. Every component keeps its own centroid, so the
dry/propellant parallel-axis spread is retained.

Transport. Each coupled off-rails step (`Universe.VariableMassTransportEnabled`, default on)
builds a `Physics/VariableMassStepModel` from the step's complete resource window: piecewise-
affine store masses from the funded debits, flow groups (an engine feed pool, solid/mono
engines, the main vent, RCS, a stuck thruster) and their outlets (engine exit = mount thrust
point less the model's declared exit offset, optional exit diameter; RCS nozzle points;
declared vent and stuck-thruster outlets). RK4 samples m, c, I at every stage and splits at
every knot. The integrated state is the instantaneous centre of mass; the open-system force
F = m(c̈ + 2ω×ċ) − ṗ_rel − 2ω×p_rel and moment −İω − J_out ω − ω×h_rel − ḣ_rel − ċ×p_rel
(`Physics/OpenSystemTransport`) act beside thrust. Flow runs from each store's loss point
(free surface or centroid) to its outlet and straight to the exit, which satisfies
continuity exactly (p_rel = m ċ + Σ Q (e − c)). Across a flow change (ignition, cutoff,
throttle, an exhaustion knot) P = m v_material + R p_rel and H = I ω + h_rel are continuous:
the feed's momentum change reacts on the structure. The datum is rebuilt with its moving
offset, v_datum = v_c − ω×(R c) − R ċ. Practice replenishment, an undeclared outlet or an
incomplete window fall back to the frozen post-debit snapshot for that step
(`Vessel.LastVariableMassStatus`); the legacy split path and water motion stay frozen.

A known stopped feed transfers retained internal momentum to the structure before its
stored flow momentum becomes zero. At an off-axis opening, the angular reaction also
changes datum velocity by `-R*(deltaOmegaBody cross c)`. Aerodynamic/contact evaluators
use material velocity at the candidate centre, excluding `R*cdot` from its coordinate
velocity; otherwise moving liquid invents surface airspeed. Centroid position integration
and telemetry retain coordinate velocity. `VariableMassFlowTransitionTests` covers these
transitions and stationary drainage in atmosphere versus vacuum.

Each flow route also retains its linear momentum and angular momentum about the fixed
body datum, owned by its debited store. Fragmentation partitions these shares by part and
translates the angular reference with the datum; total P/H about a common inertial point
is unchanged. This is a source-store ownership approximation for the declared route, not
a severed-pipe transient solver. Optional Save V2 shares preserve this state on reload;
older known aggregates remain an unowned primary-fragment share. Unknown flow stays
unknown. `TransportMomentumSplitTests` covers staging, breakage, payload release and reload.
Retained moments/nonempty shares also require one funded physical step before analytic
warp/coast dispatch, including non-active vessels, so a flow stop is not deferred or
revived by a later split. Live vent/disturbance flow commands also prevent Kepler from
skipping their first debit. Once inactive and cleared, the ordinary Kepler policy resumes.

Consequence: a draining liquid vehicle spins up in roll (İ_axial = ṁ R²/2 outweighs the
near-axis exhaust's J_out), so mission controllers that must hold roll damp the roll rate
(`AttitudeGuidance.ComputeAxisPointingCommand(rollDampingGain)`).

Flight 14 orbital startup is committed after pointing/rate settling (2 degrees, 0.03 rad/s).
The running burn retains the existing 5-degree pointing and 0.15 rad/s mission bounds;
a violation aborts rather than cycling into automatic relight. The tighter initial cone
reserves travel for finite spool/gimbal response. These are provenance-backed engineering
policies, not measured SpaceX control limits. Original return/descent numeric settings
remain T+7905 earliest command, 104.4 km radial target, fraction 0.0049 and response 20 s.

Tides. `GravityGradient` moments act in coupled, water and Kepler-attitude propagation from
every attractor with the integrated central tensor (A7).

Gates: analytic (`SettledColumnMassTests`, `OpenSystemTransportTests`, uniform-burn cylinder,
tidal libration) and historical (`SaturnVJetDampingHistoricalTests`: AS-506 S-IC J_out and
İ against MPR-SAT-FE-69-9 mass characteristics). Details: [`variable_mass_torques.md`](variable_mass_torques.md)
and [`variable_mass_resource_adapter.md`](variable_mass_resource_adapter.md).

### Integration policy

The production scheduler defaults to `Universe.Coupled6DofIntegrationEnabled = true`.
The coupled solver handles off-rails physics and evolves Euler/tidal attitude along Kepler
coasts. Setting the switch to false retains the legacy split solver and constant-world-rate
coast as a diagnostic baseline. The centre-of-mass conic remains Keplerian in both modes.

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
and cut off on the flown periapsis (`SaturnVAscentScenarioTests`: 181 x 195 km at T+700 s with
~67.8 t left for TLI). The open-loop S-IC elevation program it replaced paid ~660 m/s of steering
loss; transonic drag costs only ~35 m/s.

Each Saturn V flies its own ascent schedule (`SaturnVAscentSchedule`). Apollo 11 was flown on
AS-503's clock. That cut AS-506's S-IC 8 s early with ~160 t of propellant aboard, left it low
and flat on AS-503's pitch-over, and the S-II then paid ~1 km/s of gravity loss. The S-IVB
reached TLI with 30 t instead of ~72 t, ran dry halfway through the injection, and
`ProgradeEnergyBurn` kept the throttle up on an empty stage. The fixes:

- AS-506 uses the flown event times.
- A residual-based S-IC cutoff also responds when either main/header feed constituent
  is exhausted. Otherwise unusable oxidizer just above the aggregate residual could trap
  the schedule until its clock guard, leaving about 13 s without first-stage thrust.
  `SaturnVAscentScheduleTests` covers the inventory cases and the actual staged ascent;
  it preserves the residual, source separation delay and clock-only AS-503 schedule.
- Its S-IC burns down to its flown 32.0 t cutoff residual, since OECO was LOX depletion.
- Its pitch-over retains the existing 80-degree calibration. Native cutoff trajectory,
  flight mixture variation and ground-start inventory remain F6 reconstruction work.
- The S-II centre engine cuts at T+460.6 s, and the S-II separates as soon as it burns out
  instead of coasting to the clock.
- Its J-2s use the flight reconstruction (S-IVB 4204 N·s/kg and 901 kN; S-II 4150 N·s/kg and
  5.14 MN; MPR-SAT-FE-69-9), not the 421 s catalogue rating.
- A burn now stops with `propellant-depleted` when thrust vanishes.
- The 4.3 t "derived allocation" that closes NASA's ignition total moved from the SLA to the
  S-IC. In the SLA it rode all the way to TLI, which the flight's 135,669 kg insertion mass
  rules out; the SLA itself weighed 4,009 lb.

`SaturnVAscentScenarioTests` checks that AS-506 inserts at T+684 s with 72.9 t in the S-IVB,
3,380 m/s for a 3,182 m/s TLI (flight: T+709 s, 75.7 t).

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

The translunar corrections aim at a perilune altitude, not at the point the TLI plan stored:
`TranslunarCorrection` re-solves the Earth-centred Lambert arc from the flown state and secant-
iterates the lunar B-plane offset until the patched-conic encounter predicts 111 km, and a trim
inside the Moon's sphere of influence corrects the Moon-centred hyperbola directly
(`PeriapsisCorrection`). Aiming at the stored point had left a 211 km perilune; the flown LOI is
now 108 × 312 km (Apollo 8: 111 × 312). The prograde TLI still leaves a ~140 m/s first
correction (Apollo: ~6 m/s); a prograde-then-Lambert hybrid cut it to ~8 m/s but diverged near
the 180° Lambert singularity, so iterative (IGM-style) injection guidance remains open.

Spacecraft translation uses reaction control independent of the engines: `TranslationCommand`
per body axis is a fraction of the declared thrusters' force along that axis
(`rcs_thrusters` on the part: position, direction, thrust, Isp), allocated together with any
attitude moment and burned at F / (Isp g0) from the owning system's store (`RcsTranslationTests`,
`RcsAllocationTests`). With vessels hard-docked, the jets are allocated about the docked
stack's centre of mass and weighted by the stack's inertia (`Vessel.DockedStackCenterOfMassLocal`,
refreshed by `Universe` with the docked mass), since that is the body they turn. About the
primary's own centre, a jet between the two centres has its moment reversed: CSM-107's SM quads
(X_A 959 in) lie forward of the CSM's centre (934 in) but aft of the CSM+LM stack's (1038 in),
so docked pitch/yaw commands turned the stack backwards and a large-angle slew diverged
(`Apollo11SpsBurnScenarioTests.DockedStackTurnsTheWayItsRcsCommandAsks`). A command is a fraction
of the moment available about that centre. Gemini 8's rendezvous flies on it: a terminal-phase-
initiation Lambert intercept (`RendezvousPlanner`, 130° transfer, aimed 500 m above the Agena),
replanned from the flown states 15 min before the burn because the coast is integrated with
oblateness and drag; a mid-transfer correction re-aimed from the Agena's current state; then
`ProximityApproach` brakes to a waypoint on the docking axis and closes at 0.12 m/s into the
real `TryDock` capture envelope (`Gemini8RendezvousScenarioTests`: TPI 12.3 m/s, 143 kg of the
286 kg OAMS load).

The OAMS thruster 8 failure is a `DisturbanceTorqueBody` that bypasses the control-rate
envelope and drains monopropellant (116 N·m, calibrated to the post-undock 20 → 296 °/s growth
on the model's roll inertia, which is one 25 lbf thruster on a ~1 m arm). Recovery is the
re-entry RCS rings (two rings of eight 25 lbf thrusters with their own store, armed by the
crew; the roll is stopped with ~68 % of it, flight ~75 %) damping the roll, and the
retrograde section is a solid-motor part fired retrograde instead of an impulsive Δv
(`Gemini8AnomalyScenarioTests`), jettisoned at burnout to expose the heat shield
(`Gemini8ReturnScenarioTests`: 8.6 g entry, 8.9 m/s splashdown on the 8.4 ft drogue at 15.2 km
and the main at 3.2 km).

Removing the rendezvous teleport exposed that GLV-8 never reached orbit: its open-loop stage two
stalled at 115 km. Titan now flies the historical stage-one program to propellant depletion
(BECO was depletion, not a clock), then closed-loop stage-two insertion into the Agena's plane
with time-to-go from the rocket equation (a mass ratio of ~5 made the constant-acceleration
estimate 2.3x too long), cut on the flown periapsis or at burnout (`Gemini8AscentScenarioTests`:
145 x 152 km at T+347 s). Large OAMS burns point the aft thrusters along the velocity to gain
(firing several axes at once costs |x|+|y|+|z|), the TPI is centred on its impulsive epoch, and
proximity operations take over where braking the planned arrival speed at 80% of the OAMS
deceleration ends at the target, with the port facing the line of sight so braking is axial.

The AS-506 catalogue uses the published Block II SM/CM structural spans, with the SPS
overlapping the SLA instead of adding a serial metre to the stack. Tower reference bounds
overlap the CM; its effective aerodynamic shape and internal mass distribution remain
estimates. The nominal SM RCS quad plane is 41.10 in aft of the CM interface (local SM
Y=1.01346 m), and SPS thrust acts at CSM X_A=833.2 in, 0.12192 m aft of the SM structure.
The runtime adds the part's axial thrust offset to its cluster mount. Individual RCS
nozzle offsets, the SPS logical 1 m envelope and its point-outlet fallback remain
unresolved; the gimbal station does not establish the physical bell exit. See
`as506_spacecraft_reconstruction.md` for source revisions and mass-property limits.

`PeriapsisCaptureBurn` opens its constant-mass half-burn window using
`TranslationalMassKg`: the primary and its hard-docked secondaries. Using the primary
alone delayed the controlled CSM-107/LM-5 SPS ignition by about 75 s and left a 55.28 km
perilune. The corrected controlled-arrival fixture reaches 101.94 x 312.02 km with the
same orbit bounds; an independent 180 s ignition-window regression catches the error.
The two docked cases and four existing CSM-103 cases pass. This extends controller
coverage; Apollo 11's lunar target still uses the declared Apollo 8 proxy.

Apollo 11's transposition, docking and extraction use the same `ProximityApproach` on the SM RCS
(four quads: 1780 N per axis, Isp 290 s, 610 kg). The CSM leaves the S-IVB/SLA on the
separation impulse, turns end over end to face Eagle's drogue and closes to a real capture
(`Apollo11TranspositionDockingScenarioTests`: latest AS-506 reference-geometry gate docks
344 s after separation on 86.6 kg of RCS; flight: about 7 min). The consumption is a model
observation, not a validated historical residual; native CSM mass distributions remain open. Both docking ports face out of their vehicle's +Y top. The LM drogue was
declared along −Y, which let the teleported CSM dock without turning around and, under RCS,
docked it from inside Eagle; the test now requires a nose-to-nose finish and a probe that never
crosses the drogue plane. Armstrong and Aldrin move to Eagle through the tunnel only while
docked (`Universe.TransferCrew`, limited to the destination's `max_crew` seats). Campaign crew
survival counts every intact vessel, since the crew is split between Columbia and Eagle.

Eagle's descent is `LunarPoweredDescent`. DOI is a retrograde DPS burn at 15% throttle that
lowers the perilune to 15 km. PDI fires at that perilune. The braking phase flies explicit
linear-acceleration guidance (the LGC's E-guidance form) to a low gate 150 m up at 3 m/s down.
Vertically, a = 6(h_t − h − v·T)/T² − 2(v_t − v)/T, plus gravity net of the centrifugal relief.
Horizontally, a constant deceleration nulls the speed and leaves downrange free. The time-to-go
T is re-solved every step, so the demand stays at 85% of the DPS. Below the gate the LM lets
down vertically, at a rate proportional to altitude, to 1 m/s at contact
(`Apollo11PoweredDescentScenarioTests`: DOI 50 s; PDI to contact 646 s, flight 756 s; 1.0 m/s
touchdown; ~1 t of DPS propellant left). There is no landing-site targeting or terrain yet. The
Moon is a sphere, and contact uses the vessel datum like every other surface contact.

Heating: a part in the wake of a heat-shielded part that leads into the flow (downstream and
no wider) receives `StressSolver.AfterbodyHeatingFraction` (5%) of the stagnation flux. Every
part used to take the full flux, which burned Gemini's docking nose through behind its shield.

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

### Overlapping axial assemblies

`PartGraph.VehicleLength` uses the exterior axial bounds when declared connections overlap,
leave a gap or branch; enclosed payloads do not extend those bounds. Ordinary serial stacks
retain the original length summation, including its floating-point order. Unknown connection
geometry retains the legacy estimate. The result is cached until topology changes.

For overlapping coaxial cylinders, `DistributedAerodynamics` loads the union of exposed
sections. Hidden hardware adds neither planform area nor spurious diameter steps. Munk
area changes follow that exterior section rather than the ordering of component centroids;
their sum equals the aft section area. The existing ogive approximation is retained only
when its nose region is not overlapped. Off-axis assemblies and overlaps in an unresolved
ogive region retain the previous approximation. Station comparisons absorb eight floating-point
steps to avoid treating roundoff at an ordinary joint as an overlap; no flight gate changes.

`PartGraphEnvelopeTests` covers overlaps, gaps, folded chains, branches, enclosed payloads
and topology invalidation. `OverlappingAerodynamicsTests` checks exposed area, the section
balance, force/moment invariance with hidden hardware, and exact ordinary-joint compatibility.
This model support does not adopt the pending AS-506 catalogue reconstruction.

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

## Propellant slosh (F5, production)

`Physics/PropellantSlosh` implements the first lateral cylindrical-tank mode from NASA
SP-8009/SP-106. Its isolated planar carrier coupling conserves lateral momentum; optional
internal viscous damping removes energy without an external reaction. The modal mass is
withdrawn from the same liquid inventory rather than added to vehicle mass. Registered
analytic limits and an AS-501 probe-frequency comparison cover this foundation.

F5 closed on 2026-11-07. `Universe.PropellantSloshEnabled` defaults on; declared liquid
tanks participate in the funded coupled rigid/modal solve, with source-owned state through
separation, docking and Save V2 persistence. One F4 funding window supplies the coupled
body/modal step without a duplicate resource debit. Undeclared stores keep the settled
or fallback shape. Zero-g and rapid-drain continuations preserve the mechanical state but
do not extend the validated small-amplitude settled-fluid domain. Current implementation,
conservation evidence and those limits are detailed in
[`propellant_slosh.md`](propellant_slosh.md#active-production-integration).

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
- Attitude control is actuator-only (fidelity phase F3): the pilot/guidance command, SAS rate
  damping and the 20°/s pilot envelope only set a desired angular acceleration, which the
  gimbals, the declared RCS thrusters (`Physics/RcsAllocation`) and the body flaps deliver as
  far as they physically can. Nothing writes the angular velocity, and a vehicle with no
  thrusters and no lit engine has no attitude authority in vacuum.
- Landing contact uses multiple points, spring/damper normal response, friction, lever arms,
  torque and suspension travel.
- Tower catch uses explicit pin/contact geometry, relative velocity, alignment and a moving
  cradle target.
- Surface settling is a gameplay/simulation boundary for low-speed support; orbital-speed
  impacts remain destructive.

## Current realism status

The coupled off-rails adapter is enabled by default (`Universe.Coupled6DofIntegrationEnabled`),
with distributed aerodynamics, physical body flaps and funded actuator control. Legacy
integration remains available for comparison. F4 transport acts on supported coupled steps;
its unsupported/legacy/water boundaries are documented above. F5 currently supplies an
isolated first-mode foundation, without production modal state or coupling. Authoritative
phase status is in `fidelity_gates.md`; migration evidence is in `coupled_6dof_migration.md`.

### Early migration evidence (historical snapshot, 2026-09-25)

The table below records the early migration's narrow gates and then-open limits. Its
opt-in/open labels describe that historical checkpoint, not current activation switches.

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

That original migration sequence led to default activation (step 30 in
`coupled_6dof_migration.md`). New shared-physics changes still require their specific
numerical/mission gates and complete CI; the old opt-in labels above are historical.

## Flight 14 bounded water-entry estimate

The playable engineering exploration opts into `WaterContactSolver` during its
terminal flip. The older powered-return diagnostic still stops at 100 m by default.
No shared vessel receives water loads without explicit configuration and a matching
Earth body-fixed region. Coverage is a union of source-verified rectangles: the main
22.3..35 N, 161..140 W domain and an additional 20..22 N, 164..162 W western domain, plus 22..22.3 N, 159..158 W east of Kauai.
`Flight14LandingDefinition.AdditionalWaterRegions` supplies disjoint additions; combining
their enclosing bounds would incorrectly enable water across islands and gaps.
NOAA ETOPO 2022 native 15 arc-second statistics cover every cell of each rectangle
(15,361,920 / 230,400 / 17,280 cells, no skips/missing cells; maxima -66.55 / -1786.89 / -1692.27 m).
Pinned requests/statistics are in `../research/FLIGHT14_OCEAN_COVERAGE_2026-11-06.json`.
The coverage is simulator operating context, not a bathymetric collision mesh, navigation
product, measured SpaceX coordinates or geographic steering target. Physical contact
criteria and nearby-island exclusions remain independent of this coverage extension.

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
