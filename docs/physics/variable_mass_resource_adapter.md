# F4 resource adapter: funded snapshots and continuous mass properties

A6 is in progress. Atomic RCS funding, optional immutable RCS receipts and in-step
liquid-engine exhaustion are implemented; continuous mass properties, engine/vent receipts
and transport forces are not activated.

## Implemented engine exhaustion boundary (2026-11-05)

Liquid-engine exhaustion is now an interval boundary inside the physics step, on the
coupled, legacy and water paths. Started on `codex/f4-engine-exhaustion` (`f1868ef`) and
completed with the regressions and validation below.

- `PartGraph.ConsumePropellantFromPool` sums the step demand of every liquid branch that
  shares a pool (ordinal engine-instance order fixes the summation, never a fuel priority),
  limits the common burn by the pool's first limiting reactant, debits only the funded mass
  and **keeps the unused reactant**. The former consumer dumped whatever remained of both
  reactants into the last step; that propellant left the vehicle without producing thrust.
  Stage-local pools, hot-stage overlap pools, main-before-header feed and the protected
  landing feed are unchanged.
- `PartGraph.EngineFunding.cs` keeps each pending boundary (engine, funded duration) for the
  open tick. The engine is marked depleted only at `EndPhysicsTick`, after the funded impulse
  is integrated. Outside a tick (direct `ConsumePropellant` calls) it is depleted at once.
- `IntegrateEngineIntervals` splits the step only at those boundaries. Each sub-interval is
  one complete RK4 step with a fixed live-engine set (`Part.FundingThrustScale` 1 or 0),
  including its one-sided endpoint sample. Siblings in different pools (hot-stage overlap)
  cut off at their own instants of the same step. Stateful preparation (throttle, spool,
  control allocation, resource debit) runs once per step and is never repeated.
- The legacy explicit-Euler attitude update integrates the funded torque impulse once
  (`GetFundedAngularAcceleration`); its translation now runs inside the open tick, as the
  coupled path already did. Engine performance is evaluated at the prepared throttle and
  then scaled, so no point on a nonlinear throttle map is sampled twice.
- Infinite-propellant assistance keeps its earlier contract: the gross draw and any
  boundary are computed normally and the tank loads are restored afterwards. A full tank
  never reaches a boundary within one step; an assisted near-empty tank still cuts off.
- Mass and inertia remain frozen per step (the post-debit snapshot). Continuous
  mass/CoM/inertia sampling, engine/vent receipts and transport forces are still pending.
  Solid and monopropellant engines keep whole-step exhaustion.

Regressions (`EngineExhaustionTests`, 15 cases on both integration paths): unused-reactant
retention for each limiting reactant; partial-step impulse and position (`a·t_f·(dt − t_f/2)`)
followed by a step with no extra impulse; a step the pool funds exactly; an empty pool with no
impulse and no oxidizer dump; two engines of different size sharing a pool cutting off
together; hot-stage siblings with different cutoff instants in one step; headers extending the
burn while the protected feed is never drawn; and an off-axis engine whose moment acts only for
its funded interval (inertia tensor on the coupled path, scalar inertias on the legacy path).
13 of the 15 fail on the previous consumer; the two exact-exhaustion cases pass on both.

Mission impact: Flight 14's Super Heavy boostback ends on LOX and now keeps the unmatched
methane of its inherited tank mixture (1011 kg) instead of burning it without oxidizer.
`Flight14BoosterReturnTests` replaced its engineering envelope (reserve + up to 1000 kg)
with the exact conservation identity: cutoff propellant = landing reserve + open-feed fuel −
open-feed oxidizer / mixture ratio, to 1 g. No other mission tolerance or calibration changed.

Validation: local `tools/ci_check.sh` passed: **1331/1331** tests, the three builds at 0 warnings / 0 errors and all static contracts. Godot smoke was not available in this environment (no Godot binary); GitHub Actions runs it.

## Continuation checkpoint (2026-11-05)

Completed work: F3 actuator fidelity (`d7fb773`), F4 pure torque foundations (`14349dd`),
production gravity-gradient and Kepler attitude (`44b47b1`), and atomic mixed-feed RCS
funding with optional immutable receipts (`04bffd6`). F4 remains open. The complete CI
for this code passed **1316/1316** tests, three builds with zero warnings/errors, Flight
startup, MainMenu/Construction smoke and graphics capture/reload. The regenerated Flight 14
entry reference state matched A7 exactly; mission tolerances and calibrations were unchanged.

The next implementation unit was engine exhaustion, before enabling transport forces
(implemented — see "Implemented engine exhaustion boundary" above):

1. Add regressions for the limiting-reactant case below, symmetric oxidizer exhaustion,
   exact exhaustion and simultaneous engine branches, including branches on one engine part.
2. Replace the remaining-load dump in `PartGraph.ConsumePropellantFromPool` with a funded
   interval ledger. Preserve unused reactant, stage-local pools, hot-stage overlap and
   main-before-header feed. An ordinal ID establishes deterministic accounting, not a
   physical sequence of simultaneous burns.
3. Preserve prepared per-instance force/flow before `Part.MarkFuelDepleted` clears runtime
   throttle. Integrate the actual delivered interval with consistent boundary forces on both
   coupled and legacy paths; fixing the mass debit alone does not fix the impulse.
4. Verify analytic mass and impulse, surviving sibling thrust, depletion without persistent
   engine failure, and existing mission gates. Run `bash tools/ci_check.sh` before committing.

After engine and vent receipts, complete the continuous component mass/CoM/inertia sampler
and internal-flow convention below, then connect the existing variable-mass integrator.
Declared nozzle points alone are not verified exit area/profile or internal piping.
The Saturn V historical jet-damping gate is still missing: SPS gimbal-actuator damping is
not a vehicle jet-damping coefficient. Do not use it to close that gate or relax tolerances.

## Implemented RCS funding boundary

Owning RCS systems reserve their entire step demand before the shared feed is checked.
Shared consumers use only the unreserved load of stores whose feed valves are open.
If either owning or shared demand cannot be funded, no participating store is charged
for that RCS allocation and no allocated RCS force, torque or mass flow is delivered. The existing whole-step
funding cadence is retained for RCS; fractional exhaustion-time integration exists for liquid
engines only.

This fixes a reproduced overlap bug: two 1 kg/s consumers over 20 ms could both use the
same 0.03 kg store, leaving approximately -0.01 kg while delivering thrust. Their combined
0.04 kg demand is now rejected without spending the 0.03 kg. These are synthetic regression
values, not vehicle calibration inputs.

`Vessel.CaptureRcsFunding` enables `LastRcsFunding`, an immutable
`RcsPropellantFundingSnapshot` for the latest successfully funded RCS allocation. It stores
actual before/after withdrawals keyed by part instance and each funded thruster's stable
part/thruster IDs, declared nozzle point in skirt-datum coordinates, force direction,
average mass flow and step duration. Stores and consumers remain separate; shared funding
does not establish an internal pipe route. The nozzle point does not establish exit area
or velocity profile, and the force direction is opposite to exhaust momentum.

Snapshots are copied before later store mutations or allocator duty-buffer reuse. At the
next physical RCS preparation, no firing, failed funding or disabled capture clears the
readout. Like the existing actuator telemetry, this is a physical-step receipt, not a
render-frame flow source: an adapter consumes it immediately after preparation, never
repeatedly during analytic coasts. Capture is off by default and does not change funding or any physical result. Infinite-
propellant assistance is explicitly marked: the receipt describes gross funded flow before
replenishment, not net vessel mass loss. The signed mass-balance residual exposes floating-
point subtraction differences between the actual store debit and summed allocated flow.
The receipts do not activate variable-mass forces or bypass the remaining source/exit contract.

## Evidence from the current code

- PartGraph consumes liquid propellant by engine-instance demand, sorted ordinally; tank
  pools are stage-local and hot-stage overlap funds bottom/upper pools separately.
- Main fuel is drawn before headers; partial exhaustion keeps the unused reactant and records
  the funded time interval, which the integrators split at. Engine receipts must expose
  that interval rather than infer delivered thrust from the final mass delta.
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

## Next engine boundary: limiting reactant and delivered interval

An independent temporary probe also reproduces the engine exhaustion issue described above.
A synthetic 1 kg/s liquid engine with oxidizer/fuel ratio 3, supplied with 0.1 kg fuel and
10 kg oxidizer for a requested 1 s step, removes 10.1 kg and finishes with zero thrust under
the current consumer. Only 0.4 kg is usable at the declared mixture ratio; the 9.7 kg excess
oxidizer has no funded combustion outlet. Fixed by the engine exhaustion boundary above
(`EngineExhaustionTests.ExhaustionPreservesTheUnusedReactant`).

The engine ledger must distinguish fully funded engine branches, a limiting-reactant
partial interval and unused stores. Capture the prepared per-instance force/flow before
end-of-step depletion flags hide it, and integrate the funded interval with the appropriate
one-sided force at each boundary. Marking an aggregate engine part depleted must not erase
already funded branches' delivered impulse. The source pools remain stage-local, including
hot-stage overlap and main-before-header feed; no new outlet plane is inferred here.

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
