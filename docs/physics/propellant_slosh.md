# F5 propellant slosh: first-mode foundation and integration design

Status: F5 closed on 2026-11-07. Production integration includes funded variable-fill
coupling, conservative coasts, Save V2, separation/docking impulses and source-recorded
tank damping. Complete CI passes 1,588 tests with no failures/skips, three zero-warning
builds and Godot startup/menu/graphics checks. The additional profile matrix passes 50
cases; the actual Godot Flight 7 ascent reaches a stable orbit. F6 source-backed historical
recalibration remains open. Earlier derivation checkpoints below are retained as history;
the production integration section describes current behavior and validity limits.

## Source and model scope

Primary sources:

- NASA SP-8009, *Propellant Slosh Loads* (1968), table I, printed p. 12 (PDF p. 19):
  https://ntrs.nasa.gov/api/citations/19690005221/downloads/19690005221.pdf
- NASA SP-106, *The Dynamic Behavior of Liquids in Moving Containers* (1966), chapter 6,
  especially the discussion accompanying figure 6.4/table 6.1, printed pp. 203-204:
  https://ntrs.nasa.gov/api/citations/19670006555/downloads/19670006555.pdf
- AS-501 flight evaluation report, MPR-SAT-FE-68-1, figure 11-15, printed p. 11-20
  (PDF p. 282). Dots are measured probe-derived frequencies; solid curves are predictions:
  https://www.ibiblio.org/apollo/Documents/lvfea-AS501-Apollo4.pdf

Use the SP-8009 table-I first-mode cylindrical approximation, with a settled free surface
and small lateral motion. A tank's effective radius/depth and the axial *specific force*
are inputs. Gravity alone is not a settling force in free fall. Baffles primarily change
damping; their geometry and measured damping still need per-tank data. Neither the
rounded table coefficients nor the mode frequencies establish a nonlinear free-surface model.

For radius R, depth h, liquid mass m, positive axial specific force a and the first root
xi = 1.8411837813406593 of J1'(xi):

```
w1^2 = (xi*a/R)*tanh(xi*h/R)
m1/m = 2*tanh(xi*h/R)/(xi*(xi^2-1)*(h/R))
L1 = a/w1^2
z1 = h - (2*R/xi)*tanh(xi*h/(2*R))
m0 = m-m1
z0 = (m*h/2-m1*z1)/m0
```

Heights z are measured above the equivalent flat floor; SP-8009's h1 and h0 are depths
below the free surface. Do not mix the two origins or substitute a hinge coordinate for
the modal mass station. SP-106 discusses different mechanical analogues and inertia
choices; identical mass/frequency alone do not establish identical moment response.

The model must reject unrepresentable parameters instead of returning NaN/infinity. The
shallow limit needs stable evaluation of `1-tanh(x)/x`; bulk subtraction loses the small
positive modal station. Frequency evaluation must avoid overflowing a/L before the square
root. Tests must cover these representability limits as well as ordinary tank dimensions.

## Mass, centroid and locked inertia

The oscillating mass comes from the same store as F4: m0+m1=m. There is one modal mass with
two lateral coordinates (body X/Z); allocating m1 independently to two full pendula would
count it twice. At equilibrium, m0*z0+m1*z1=m*h/2.

Preserving mass and centroid is not sufficient. Replacing the column with two point masses
changes its central tensor. Retain the F4 locked-state tensor explicitly. For one tank,
let C be the column centroid and r1 the equilibrium modal point. A modal displacement dr
has the following *correction* to the original distribution:

```
Delta first moment = m1*dr
Delta inertia about C = m1*(P(r1+dr-C)-P(r1-C))
```

Recenter the complete vessel after applying those corrections. At dr=0 the total mass,
centroid and full tensor must equal F4 exactly. The residual locked inertia in the modal
analogue is a hydrodynamic coefficient, not a new physical part or a separately validated
material mass distribution. Do not feed a signed inertia correction to
`FixedShapeMassComponent` as though it were an ordinary positive-density rigid component.
Prove that the complete generalized kinetic matrix is positive definite over the declared
small-amplitude domain; mass/centroid checks alone do not prove that.

## Coupled dynamics and damping

Start with the isolated planar carrier-pendulum oracle already outlined in
`SloshPendulumCoupling`. For carrier mass M, bob mass m1 and fixed L:

```
P_lateral = (M+m1)*V + m1*L*cos(theta)*theta_dot
(M+m1)*V_dot + m1*L*cos(theta)*theta_ddot = m1*L*sin(theta)*theta_dot^2
L*theta_ddot + V_dot*cos(theta) + a*sin(theta) = 0
w_free^2 = (a/L)*(1+m1/M)
```

An internal damper must conserve total momentum and remove mechanical energy. In the
small-amplitude first-mode model, c=2*zeta*m1*w1 for lateral displacement, or rotational
coefficient c_theta=2*zeta*m1*L^2*w1. Register the damping source per tank; an unsourced
universal damping ratio cannot close F5. Apply its reaction to the carrier through the
same coupled equations, rather than adding an independent body-rate damping gain.

For production, solve the body twist and all active modal accelerations together from one
symmetric generalized mass matrix, using candidate attitude/rates/accelerations. Add the
modal relative linear/angular momentum and their derivatives to F4's open-system balance.
Do not apply the carrier reaction a second time as an extra external force/torque.

For a concrete assembly, use one bob position `r_i(q_i)` and its 3-by-2 tangent
Jacobian `E_i = dr_i/dq_i` per tank. With the body twist measured at the skirt datum,
the instantaneous symmetric blocks follow from the same kinetic energy:

```
M_body = [ m*Identity    -cross(S) ]     S = sum(m_j*r_j)
         [ cross(S)      I_datum  ]
B_i = [ m_i*E_i ; m_i*cross(r_i)*E_i ]
D_i = m_i*transpose(E_i)*E_i
M_generalized = [ M_body  B_1 ... B_n ; transpose(B_1) D_1 ... ; ... ]
```

`M_body` includes the locked-inertia correction and current modal displacement. Its
carrier remainder is not a separately reconstructed rigid part. `D_i` is a single
2-by-2 block for the one bob, not a second copy of its mass. Assemble Coriolis,
centrifugal, moving-parameter and rotational-frame terms from the same kinetic model;
solving these blocks with a force vector alone would omit the inertial bias terms.
The blocks are an assembly specification, not evidence that a production solver or
its positive-definiteness proof already exists. Require independent conservation tests
before choosing an activation domain or connecting this solve to funded outflow.

### Constant-fill oracle before funded outflow

The first 6-DoF implementation unit should keep tank fill and geometric parameters fixed.
Let `v` be inertial datum velocity expressed in body coordinates, `omega` the body angular
rate, and `u = q_dot` all modal rates. With the blocks above, derive one kinetic energy
`T = 0.5*[v,omega,u]^T*M_generalized(q)*[v,omega,u]`. Its conjugate momenta satisfy:

```
pi_v     = m*v + omega cross S + sum(m_i*E_i*u_i)
pi_omega = S cross v + I_datum*omega + sum(m_i*r_i cross (E_i*u_i))
d(pi_v)/dt_body     + omega cross pi_v = F_body
d(pi_omega)/dt_body + omega cross pi_omega + v cross pi_v = torque_about_datum
 d(dT/du)/dt - dT/dq = Q_modal
```

Expand every derivative from the same candidate `q,u,v,omega` to assemble the acceleration
matrix and bias. A body-frame derivative of inertial velocity is not inertial acceleration:
the `omega cross pi_v` term is required. The last angular equation uses the datum moment,
not an external torque already reduced to the CoM. Compare reconstructed P/H about one
fixed inertial origin and total energy against an independent closed-system integration.

For regular two-coordinate charts, require each `D_i` positive definite and the Schur
complement `M_body - B*D_inverse*transpose(B)` positive definite. Merely checking that the
6-by-6 locked body block has positive inertia does not establish a valid coupled model.
Define a verified amplitude/chart domain; reject an invalid matrix explicitly rather than
clamping an eigenvalue or adding artificial mass. This remains a design requirement, not
a claimed proof that the chosen mechanical analogue satisfies it for every tank/fill.

In a prescribed uniform gravity field, use total gravity force `m*g_body`, datum gravity
moment `S cross g_body` and modal generalized force `m_i*transpose(E_i)*g_body` consistently.
A free-falling unforced carrier must have no gravity-driven restoring frequency. Under an
externally accelerated carrier, the effective settling force emerges from this coupled
solve. Do not also add a separate potential based on that same effective acceleration;
that would count the acceleration response twice. Candidate tank-local acceleration and
nonuniform gravity must be derived consistently before production activation. The current
`FirstMode` frequency function is a parameter diagnostic, not an extra external force.

Only after this fixed-fill oracle passes should the funded adapter introduce time-varying
coefficients and open-system fluxes. F4's existing locked-column transport and the modal
kinetic bias cannot both supply the same inertia/redistribution term. Derive the combined
balance explicitly and keep the fixed-fill oracle as the independent limiting case.

As a store drains, m1, L, its hinge/equilibrium station and the locked distribution change.
Their time derivatives and internal redistribution are part of the model. Switching mass
between m0 and m1 is not exhaust and must not create another resource debit. Preserve P/H
at activation, exhaustion and geometry/flow boundaries using the F4 transition convention.
For a<=0 the settled-column approximation is outside its stated free-surface domain: report
that state explicitly and preserve the existing modal momentum; do not silently substitute
1 g, reset the oscillator or create an unmodelled impulse.

## Persistence and verification before activation

Identify each tank state by part instance and resource/tank identity, with two modal
coordinates and rates, activation/regime metadata and the quantities needed to reconstruct
its momentum. Stage/dock/breakup/load must preserve or explicitly transfer the state.
Save/load regression must reproduce the uninterrupted run, not just restore an angle.

Required tests include independent Bessel-root/frequency limits; mass/centroid/locked-tensor
identity; nonlinear planar momentum and energy; free-carrier frequency; internal damping
energy loss without momentum loss; zero excitation recovering F4; two-tank reaction;
fill-dependent coefficients and flow switches; coast/regime changes; and exact save/load.

The frequency gate compares an AS-506 simulation to AS-501 probe data. Treat
that as a cross-variant check with explicit uncertainty, not validation of a complete AS-501
vehicle. Verify the late shallow-fuel regime against the actual free-surface geometry before
accepting it: equal-volume flat bottoms reproduce neither dome geometry nor every slosh
frequency. Do not widen a tolerance, remove a late sample or relabel predicted curves as
measurements to conceal that limitation. F5 stays open until its production coupling and
historical frequency/amplitude/damping evidence meet the maintained phase requirements.

## Foundation evidence (2026-11-06)

The permanent suite covers 32 cases: analytic mode limits, nonlinear isolated momentum and
energy conservation, free-carrier frequency, internal damping and numerical input limits,
plus four AS-501 frequency samples at 10, 60, 100 and 130 seconds. The historical comparison
uses the vessel's reported axial proper acceleration, not gravity or a nonexistent
telemetry property. This bulk thrust/drag/contact estimate is sufficient for the stated
frequency comparison; production must evaluate the candidate tank-local acceleration,
including rotational and internal-flow contributions, in its coupled solve.
The 10% comparison band is unchanged from the draft; graph reading uncertainty is recorded
separately. The late LOX sample is approximately 0.46 Hz on the measured dotted curve, not
0.50 Hz from the prediction/earlier peak. Neither the late sample nor the shallow-fuel
regime is omitted. See `data/flight_profiles/saturn_as501_slosh_reference.json` and
`data/provenance/propellant_slosh_f5_2026.json`. A source-frequency pass does not validate
production body reaction, locked inertia, changing fill, unsteady coasts or save/load.

## Constant-fill coupled kernel (implementation checkpoint)

`Slosh6DofDynamics` now assembles the complete 6+2N kinetic matrix. It integrates no
resource inventory and does not change Universe dispatch yet. Cartesian modal coordinates
use one spherical bob per tank; positivity of the full scaled matrix is checked, without
adding artificial inertia or clipping eigenvalues. Equilibrium sampling retains F4 mass,
centroid and every inertia entry exactly.

The kernel solves absolute datum acceleration A, body angular acceleration alpha and all
modal accelerations simultaneously. For S=sum(m*r), p=sum(m*r_dot), datum inertia I_D,
and each relative acceleration r_ddot=E*q_ddot+b:

```
m*A - cross(S)*alpha + sum(m_i*E_i*q_ddot_i)
    = F - omega cross (omega cross S) - 2*omega cross p - sum(m_i*b_i)
cross(S)*A + I_D*alpha + sum(m_i*cross(r_i)*E_i*q_ddot_i)
    = T_D - omega cross (I_D*omega)
      - sum(m_i*cross(r_i)*(2*omega cross r_dot_i+b_i))
m_i*transpose(E_i)*(A - cross(r_i)*alpha + E_i*q_ddot_i)
    = Q_i + m_i*transpose(E_i)*(g - omega cross (omega cross r_i)
      - 2*omega cross r_dot_i - b_i)
```

Here F includes uniform total gravity m*g, T_D includes S cross g, and Q_i contains
internal generalized damping or other declared modal loads. No separate axial-acceleration
potential or k3 torque is added. Tests reconstruct momenta/energy independently by removing
each locked point bob and inserting its actual world-space moving counterpart.

Initial directed validation passes 79 cases across the new kernel, the existing slosh
foundation and provenance. It covers 3D two-mode free motion, dissipative internal forces,
free fall in a rotated frame, analytic free-carrier frequency, exact locked identities,
rigid Euler recovery, chart rejection and an indefinite coupled matrix despite positive
locked inertia. Log: `/tmp/exo-f5-sixdof-kernel.log`. This is not full production validation.

Additional primary references under review for the production adapter:

- NASA AAS 24-012, *New Perspective on Slosh Dynamics in High-Gravity Regimes for Lunar
  Missions* (2025), especially model-location comparison and inherent axial torque:
  https://ntrs.nasa.gov/api/citations/20250000708/downloads/AAS_2025_v11.pdf
- Orr, AAS 21-433, *Modeling and Simulation of Rotary Sloshing in Launch Vehicles*:
  https://ntrs.nasa.gov/api/citations/20210000785/downloads/Orr_AAS_2021_rotary_paper.pdf
- SwRI mechanical-model derivations and errata:
  https://www2.swri.org/www2/fluid-slosh/slosh-dynamics.htm

The 2025 NASA paper explicitly distinguishes bob and hinge locations and warns against
adding an axial-offset torque already implicit in a multibody pendulum. Its damping
comparison also identifies unresolved model differences; it is not used as a measured
universal damping ratio. The currently implemented SP-8009 modal station remains unchanged
while the force/moment mappings and draining/low-g assumptions are checked.


Catalog equilibrium checks pass for AS-506 at full, 50% and 1% main-store fill, preserving
locked mass, centroid and tensor exactly. A provisional Starship check initially failed
because its mode list was empty, not because its kinetic matrix was indefinite: the V3
Ship and booster have no `propellant_tanks` declarations. Their F4 stores still use uniform
part cylinders. The catalog test is therefore restricted to actual declared tank geometry.
Production must report this missing geometry and obtain source-backed or explicitly
estimated equivalent tank parameters; it must not pretend Starship already has validated
cylindrical slosh geometry or silently infer measured tank stations from the render mesh.


## Spherical continuation and parameter derivatives

The fixed-fill kernel now also accepts a unit direction and tangent direction rate per
mode (`SloshSphericalState`). A local orthonormal basis is used only in the acceleration
solve, and the returned acceleration is a physical vector. Candidate-stage retraction
maintains the unit/tangent constraints. A two-mode conservation test crosses the old
lower-hemisphere boundary without resetting momentum or adding a restoring field. This
is a conservative mathematical continuation; no low-g free-surface reconstruction is
claimed. The original Cartesian model remains an independently integrated coordinate
comparison within its valid chart.

`SloshParameterSchedule.cs` samples the SP-8009 modal mass, reciprocal length (curvature)
and equilibrium height with analytic first/second time derivatives from the funded mass
history. Curvature remains finite at exhaustion, unlike pendulum length. A deep-tank
sech-squared evaluation retains derivatives lost by `1-tanh(x)^2`. Empty-store left-limit
rates are retained until the actual flow cut; stopped rates are zero.

Directed validation now passes 93 cases (kernel, spherical continuation, AS-506 catalog,
parameter derivatives, prior foundation and provenance). Logs:
`/tmp/exo-f5-spherical-kernel.log` (84 cases), `/tmp/exo-f5-parameter-jets.log` (93 cases).
No production adapter or full CI has been run for this active continuation yet.

### Variable-fill balance to implement and verify next

The following is the derived adapter design, not an implemented production force law.
Use a fixed datum. Let the F4 baseline mass distribution have S0=m*c0 and I0 about the
datum. For a scheduled modal mass mu, equilibrium point r0, displacement d and r=r0+d:

```
DeltaS = mu*d
DeltaI_D = mu*(P(r)-P(r0))
p_s = d(DeltaS)/dt = mu_dot*d + mu*d_dot
h_s = mu*(r cross d_dot + d cross r0_dot) + mu_dot*(r0 cross d)
```

The last term chooses a straight source-to-mode virtual redistribution route, analogous
to F4's declared feed convention. It must be registered as an approximation, not measured
fluid velocity. With d_ddot=E*a_modal+b, the known derivatives are:

```
p_s_dot_known = mu_ddot*d + 2*mu_dot*d_dot + mu*b
h_s_dot_known = mu*(r cross b + d cross r0_ddot)
                 + mu_dot*((2*r0+d) cross d_dot) + mu_ddot*(r0 cross d)
DeltaI_D_dot = mu_dot*(P(r)-P(r0))
                 + mu*(B(r,d_dot)+B(d,r0_dot))
B(r,v) = 2*(r dot v)*Identity - r*v^T - v*r^T
```

The acceleration-dependent terms are mu*E*a_modal in linear momentum and
mu*cross(r)*E*a_modal in angular momentum, so the same symmetric kinetic matrix remains.
Reynolds balance about the datum, with applied thrust already included, is:

```
m*A - cross(S)*alpha = F - omega cross (omega cross S) - p_dot - 2*omega cross p
cross(S)*A + I_D*alpha = T_D - (I_D_dot+J_out_D)*omega
                        - omega cross (I_D*omega+h) - h_dot
```

The F4 central transport wrench can be converted without reconstructing outlet tensors:
`F_feed_D = F_transport_C - m*(c0_ddot+2*omega cross c0_dot)` and
`T_feed_D = T_transport_C + c0 cross F_feed_D`. Add only the modal corrections above;
do not reapply F4 inertia loss or its feed impulse. Independent finite-difference and
open-system momentum tests must validate this design before production use.

For settled Cartesian displacement q=(x,z), inverse length k avoids an infinite-length
singularity: `s=sqrt(1-k^2*(x^2+z^2))`, `d_y=k*(x^2+z^2)/(1+s)`. Its derivatives include
`d_y_k=(x^2+z^2)/(s*(1+s))`, `d_y_xk=x/s^3`, and the corresponding z expression. This
keeps geometry finite at the empty-store left limit k=0. Zero modal mass must be handled
as a limit/retirement boundary, not by adding mass to make a singular matrix invertible.
The modal equation, nonsettling continuation, geometry scheduling, damping, topology and
persisted state still require an explicitly verified production implementation.


## Combined funded-balance kernel — implemented, not activated

`SloshTransportCorrection` now implements the modal moment/derivative expressions above;
independent time differences include nonzero modal acceleration and the empty-store left
limit. `SloshVariableMassDynamics` combines those corrections with the existing F4 central
transport wrench, converts it once to the fixed datum, and uses the shared symmetric solve
in `SloshKineticSystem`. Zero modal masses are omitted from the kinetic matrix and their
acceleration-per-unit-mass limit is recovered afterwards; no artificial mass is introduced.

The directed set passes 100 cases (`/tmp/exo-f5-combined-reynolds.log`). An independent
world-space point-replacement/route reconstruction verifies that the combined total linear
and angular momentum derivatives equal only the prescribed co-rotating outlet flux.
Constant-fill accelerations recover the conservation-tested kernel. No Universe production
caller, topology/persistence integration or tank damping/geometry activation has changed.

The scheduled mechanical analogue assumes participating mass leaves co-moving with that
virtual mode as the resource drains. Its internal return route is straight from the
modal point to its equilibrium point, then follows the baseline feed convention. This is
an explicit reduced-model approximation; the algebraic conservation check is not empirical
validation of rapidly draining, nonlinear or low-g liquid motion.

### Coordinate representation for the production state (next)

A useful global representation avoids infinite pendulum length: store displacement d and
its derivative v relative to the scheduled equilibrium point, with curvature k=1/L.
There are two physical coordinates and two physical rates, represented by constrained
three-vectors. The complete spherical constraint, including both hemispheres, is:

```
k*(d dot d) - 2*d_y = 0
n = k*d - up                         (unit normal on the constraint)
n dot v = -0.5*k_dot*(d dot d)
n dot d_ddot = -k*(v dot v) - 2*k_dot*(d dot v) - 0.5*k_ddot*(d dot d)
```

Two orthonormal tangents provide the unknown acceleration columns. The prescribed normal
acceleration is the final scalar above times n. At k=0 the chart becomes the plane d_y=0
with finite one-sided derivatives, so no infinite length or zero-mass regularization is
needed. Candidate retraction must use stable lower-hemisphere sag rather than subtracting
unit directions at small curvature. This representation was subsequently implemented in the production continuation below.

Nonsettling and large-amplitude motion remain outside the settled-fluid model's stated
physical domain. A conservative mathematical continuation must be explicitly labelled as
such and preserve existing modal momentum. It must not invent a 1-g restoring field or
silently zero oscillator state. If scheduled geometry would become unrepresentable outside
that domain, a bounded held-geometry continuation needs an explicit regime and conservative
transition; it must not masquerade as a validated low-g fluid solution. Parameter values,
rate changes, modal state and retained internal momenta must survive save/load and splits.


## Global state and coupled RK4 — implemented, not activated

`SloshDisplacementState` implements the curvature constraint above with exact candidate
retraction, a stable lower-hemisphere sag and the finite empty-store plane limit.
`SloshTransportCorrection.SampleDisplacement` supplies an orthonormal tangent basis and
normal acceleration bias. These recover the earlier Cartesian physical accelerations,
including changing fill; tangent components themselves are coordinate-dependent.

`Slosh6DofIntegrator` advances datum position/velocity, attitude/body rate and every modal
state together. All four candidates see their own projected geometry, mass and loads.
Profiles and load callbacks are pure; the caller must split at coefficient/flow knots.
Independent world-particle reconstruction verifies all six inertial momenta and kinetic
energy through both hemispheres, internal damping decreases energy without momentum loss,
and time refinement establishes fourth-order convergence. Smooth draining integration
matches independently accumulated material outlet flux through the exact empty endpoint.

`SloshMomentumTransfer` conserves P/H during an internal rate/geometry handoff at fixed hull
pose and fixed total mass. It keeps live modal momentum on both sides, so a stopped feed
cannot erase a moving mode. This is an internal momentum transition, not a license to
replace a physical mass debit or hide an arbitrary geometry/energy jump.

The integrated exhaustion test exposed a pre-existing F4 endpoint defect:
`OpenSystemTransport.Sample` skipped the internal column path when loss and outlet points
coincided at zero fill. Its left-hand path derivative is still nonzero (q squared/lambda),
so the last RK stage lost it and introduced an artificial impulse proportional to dt.
Only non-column components now skip this derivative. A dedicated force/datum-moment
regression and 500/1000/2000-step full momentum balances pass at unchanged tight tolerances.
The directed set passes 129 cases. Complete CI subsequently passed 1559/1559 tests without skips,
three 0-warning/0-error builds, contracts and Godot startup/menu/graphics checks
(`/tmp/exo-f5-coupled-kernel-ci.log`, exit 0, 2026-11-07).

No production Universe caller, per-store modal persistence/topology, tank damping data or
nonsettling regime policy is activated by these kernels. F5 remains open; F6 follows it.


## Active production integration

The Universe path now advances the datum, body attitude and source-owned modal states from
one funded F4 profile. Optional Save V2 fields retain modal coordinates/rates and ending
one-sided parameter jets. Tank frames rotate with docked members. Surface/aero forces use
material velocities, while the conic uses the moving mass's mean velocity. Deferred rail
anchors include immutable source-owned modal snapshots; saving a modal coast commits and
resets its anchor so reload and continued integration use the same next partition.

Separation impulses act at the original declared joint point. The complete kinetic matrix
supplies rigid and modal velocity jumps; fallback opening-speed commands use joint mobility,
while a declared impulse keeps its physical N s magnitude. Inelastic docking solves common
rigid motion with all bob tangential inertial velocities continuous. The resulting capture
conserves P/H and dissipates energy. Modes remain with their stores through splitting and
undocking. Independent particle reconstructions verify these assertions in production tests.

### Bounded drain continuation

A spherical analogue with the native inverse length k approaching zero cannot retain an
upper-hemisphere trajectory with finite geometry. Resetting its state or silently dropping
momentum is not an acceptable empty-store policy. The production adapter now selects an
explicit conservative continuation outside its quasi-static domain:

* Under a nonpositive actual axial proper acceleration, a draining mode holds its current k.
* Under positive axial proper acceleration a, the scheduling indicator is
  epsilon=abs(k_dot)/(k*sqrt(a*k)). The estimated policy limit is 0.1; it is not a measured
  hardware tolerance or a change to a historical acceptance band. For a constant funded
  withdrawal, epsilon increases monotonically as fill decreases: with t=tanh(beta*m), it
  is proportional to t^(-3/2)-t^(1/2). Locate the unique crossing within the already funded
  smooth engine/flow interval, split there, and retain the continuous k value.
* Continue the native modal-mass and equilibrium-height jets. At the k-rate cut, transfer
  internal momenta conservatively and preserve the dynamic tangent velocity. Do not create
  an extra mass debit or a restoring field in free fall.
* Latch the held curvature until that store empties; save its positive value and reason,
  plus the ending left-side jets. No automatic geometry teleport or state reset is used to
  claim reacquisition of a settled-fluid model. The status identifies the continuation.

This is a bounded mechanical fallback, not an empirically validated rapid-drain, nonlinear
wave or low-g fluid solution. NASA 20120010782 explicitly notes limited validation of
pendulum models for draining tanks. Positive coupled kinetic energy and independent P/H
balance remain required. Both hemispheres now reach exhaustion with finite state and exact
save/reload in directed production tests. The analytic-to-held boundary has equal parameter
values and its own one-sided rates. The original pure native-coefficient tests still verify
the empty-store plane limit; no coefficient formula or historical tolerance was changed.

The bounded-drain regressions pass in both hemispheres. Tank damping is now declared only
for the three AS-506 tank parts, with explicit source limitations and estimated provenance
in `data/provenance/saturn5_slosh_damping_1968_1969.json`. S-IC uses the lower envelope of
nominal MSFC design damping; the S-II LH2 estimate comes from AS-504 probe decay. These
are neither AS-506 measured damping nor validated low-g free-surface behavior.

RCS wrenches retain their allocation-time centroid, before the funded monopropellant draw;
each RK4 candidate translates that same physical wrench to its instantaneous centroid.
An independent nozzle-force reconstruction first failed by 0.000204 N m and now passes.
Eligible vessels initialize zero-displacement/rate modes when added to the Universe, so
separation before the first tick also uses the coupled impulse response. Older saves start
at that same locked distribution. Contact inputs continue to use the locked part centroid
as their geometric reference, with the wrench shifted to the instantaneous liquid centroid.

Complete production CI passed on 2026-11-07: 1,588/1,588 tests, no skips, three builds
with zero warnings/errors, startup plus menu/graphics checks. Test time was 49 m 37 s while
the independent profile matrix and CPU-rendered Godot validation ran concurrently.
The matrix passed 50 cases and its preserved evidence passes `--verify-only` with matching
source/data/test-assembly hashes. The isolated Godot Flight 7 ascent passed all original
state gates: 1,925 frames, all seven capture milestones, `ASCENT_ORBIT_OK`, final conic
approximately 150 x 161 km above mean Earth radius. Its supported integrated presentation
preset and 640x360 framebuffer change rendering only; the actual run used the production
physics scheduler and guidance. The main project autoload was restored and rebuilt clean.

F6 remains open: passing the old Atlas trajectory does not validate its prior effective
engine or mistaken spacecraft mass. Its source audit and remaining residuals are separate
from the verified F5 production integration.
