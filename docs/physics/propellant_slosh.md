# F5 propellant slosh: first-mode foundation and integration design

Status: permanent first-mode and isolated carrier model with registered analytic/historical
gates, source records and regression tests. F5 is open; no production slosh state is
integrated or saved yet. F4 mission revalidation is complete; F5 production activation
still requires the coupled and historical evidence below. The obsolete uncompiled test draft has been replaced by
`ExosphereSimulation.Tests/PropellantSloshTests.cs`.

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
