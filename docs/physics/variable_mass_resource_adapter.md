# Next F4 unit: funded resource snapshots and continuous mass properties

This is a working design, not an activated production model.

## Evidence from the current code

- PartGraph consumes liquid propellant by engine-instance demand, sorted ordinally; tank
  pools are stage-local and hot-stage overlap funds bottom/upper pools separately.
- Main fuel is drawn before headers; partial exhaustion currently removes the remaining
  resources and marks the engine depleted. A future adapter must expose the funded time
  interval rather than infer delivered thrust from that final mass delta.
- Vessel draws disturbance flow and allocated RCS after propulsion preparation. RCS funding
  is per owning store; the force, torque and flow are applied only if funding succeeds.
- PartGraph central inertia uses a non-header uniform-cylinder remainder and point headers.
  The dry-mass centroid offset shifts that cylinder's centroid as resources drain. This is
  a retained mass-distribution approximation, not a physical liquid free-surface model.
- Engine models can declare exit area for the nozzle equation; mount positions establish
  thrust application locations, and do not establish a physical exit-plane station.

## Proposed separation

1. Immutable tick-start resource loads and body-frame mass components.
2. A funded loss ledger recording store, kg, source identity, engine/RCS/vent outlet and
   the actual active time interval. No ledger entry for failed funding, refill or reseeding.
3. Pure candidate sampling of m, c, central I and their body derivatives. Resource stores
   are committed once; RK4 force evaluation must never mutate or independently consume them.
4. Outlet geometry and normal/flux profile validated separately from thrust application point.
5. Relative internal flow and construction-datum transport derived under one declared control
   volume convention; changing I without matching exit flux is forbidden.

## Candidate pure mass distribution kernel

For constant body-frame component stations r_i, fixed specific central tensors C_i and
piecewise affine mass m_i(t), define

  m = sum m_i; S = sum m_i r_i; A = sum m_i (C_i + P(r_i))
  P(r) = (r dot r) identity - outer(r,r)
  c = S/m; c_dot = (S_dot - m_dot*c)/m
  c_ddot = -2*m_dot*c_dot/m
  I = A - m*P(c)
  I_dot = A_dot - m_dot*P(c) - m*P_dot(c,c_dot)

These formulas follow from the parallel-axis theorem and differentiation. They model
fixed shape/station mass depletion only. They must not be used to assert that a draining
liquid column retains its centroid or that actual nozzle geometry is known. Components
with moving centroids or shape tensors require their corresponding derivatives.

## Necessary regressions before production activation

- Pure candidate samples exactly reproduce existing PartGraph snapshots at interval endpoints.
- Two-store asymmetric depletion reproduces independently calculated c_dot and I_dot; symmetric
  depletion has no artificial central-origin drift.
- Co-rotating mass release at its own station cancels I_dot and outlet angular-momentum flux.
- Off-axis steady internal-flow paths satisfy linear and angular Reynolds balances about moving CoM.
- Finite fuel/header exhaustion, multiple engines, mixed clusters and RCS funding consume only
  the same funded mass once and split the physical active interval at exhaustion.
- Vents, disturbance/stuck jets and replenishment assists are explicit sources/sinks.
- Full mission/analytic/historical gates retain their existing tolerances.

Primary derivation reference: NASA/CR-1998-208246, chapters 2-3,
https://ntrs.nasa.gov/api/citations/19980210404/downloads/19980210404.pdf.
The proposed component formulas above are a derivation, not a quotation from that report.

## Moving-CoM translational convention

Let p_relative = integral rho*w dV and c_dot denote centroid motion in material
body coordinates. Continuity requires p_relative = m*c_dot + sum(q*(r_exit-c)).
For the actual coordinate acceleration of the remaining-system centroid, the additional
body-frame force beside ordinary thrust is

  F_transport = m*(c_ddot + 2*omega cross c_dot)
                - p_relative_dot_body - 2*omega cross p_relative.

This follows by differentiating P = m*(V_C - R*c_dot) + R*p_relative and applying
Reynolds momentum balance with outward mass flux. It reduces to zero for closed-system
internal redistribution, where p_relative=m*c_dot and total mass is fixed.
A temporary independent RK4 prototype reproduces co-rotating point removal and closed
internal redistribution with residuals below 1e-10 in SI units. These are provisional
synthetic checks, not mission/historical gates; port them into permanent regression tests
before activating an adapter. NASA chapter 3.1 supplies the reference Coriolis and
internal-flow momentum terms in its material-origin convention.
The datum conversion must use V_C = V_datum + R*(omega cross c + c_dot), including
the one-sided c_dot at each funded-flow interval boundary. Subtracting only omega cross c
would introduce a spurious datum velocity as the resource centroid moves.

## Mass-distribution issue to resolve before interpolation

Current BuildMassProperties represents the non-header remainder as one cylinder whose
centroid is shifted by dryMass*dryOffset/remainderMass. That preserves its first moment,
but omits the transverse parallel-axis variance between dry structure at dryOffset and
main propellant at the part centre. For dry mass 10 kg, propellant 30 kg and a 2 m dry
centroid offset, the omitted variance is 10*30/(10+30)*2^2 = 30 kg m^2. This is a synthetic
algebraic example, not a vehicle calibration. Separating those components changes the
physical inertia model and needs targeted tests and mission revalidation; an affine
sampler must not silently perpetuate the shifted-lump approximation.
