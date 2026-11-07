> F4 reverified and closed: complete CI passes 1504/1504 tests without skips;
> three builds report zero warnings/errors, with contracts and Godot checks green.

# F4 production resource adapter

Implementation delivered in `e2e1c27`; this continuation repairs and revalidates it. F4's
maintained model and validation are in `PHYSICS_MODEL.md`, `variable_mass_torques.md` and
`fidelity_gates.md`. This document replaces the pre-activation WIP adapter plan.

## Preparation and immutable inventory

A physical step funds resources once. `ResourceFundingWindow` copies the initial and final
gross main/header loads, exact attributed debit chains, liquid and single-resource engine
consumers, venting and RCS receipts. Disturbance withdrawal is a separate source. Coverage
rejects unrecorded store changes or topology changes. A coverage result is not an outlet
geometry certificate. Debug receipt flags remain optional and observational; production
transport captures the window it needs independently.

Liquid engines sharing a stage-local pool burn simultaneously until its first limiting
reactant. Main stores precede headers independently for fuel and oxidizer; landing reserves
remain mass. Solids and monopropellant engines use their own partial funded intervals.
An exhausted resource no longer cancels independent funded engine branches. Runtime
instance flows survive aggregate depletion. RCS/disturbance keep atomic whole-step funding.

Engine-priority budget reservation is explicit: a vent reserves post-engine open inventory
but its prescribed interval starts at step-local zero. Actual store debits remain distinct;
engine and vent receipts do not count one debit twice. Assistance receipts describe gross
withdrawals before replenishment, not net physical mass loss.

## Shared geometry and candidate sampling

`PartMassGeometry` supplies the same dry/resource/header decomposition to PartGraph,
part centres and `VariableMassStepModel`. Each component retains its own station and
central tensor, preserving dry/propellant parallel-axis spread. Effective dry mass includes
`MassDryOffset`. Declared dry gyration replaces the envelope-cylinder approximation.

A declared `propellant_tanks` store is a settled cylindrical liquid column on an equivalent
flat bottom: depth h=m/(rho*pi*R^2), centroid bottom+h/2 and transverse specific inertia
(3R^2+h^2)/12. Undeclared main stores use the part cylinder. Headers are point masses at
their station; undeclared saved slots retain mass at the part centre. This is a documented
geometry approximation, not a dome/free-surface or slosh solver.

`BodyMassDistribution` samples m, c, I and their body-frame derivatives from immutable
mass profiles, including moving column centroids/shapes. Internal knots are continuous in
mass but may change rate. Split at every knot; use the ending side for k4 and the following
side for the next k1. Material time stays local to the step; epoch+local time is used
separately for ephemerides. Sampling never spends resources or advances controls.

## Outlets, transport and flow cuts

`VariableMassStepModel` groups all debits with the stores and declared outlets that fund
them. Engine exits use declared axial offset/diameter relative to the thrust mount; RCS
uses declared nozzle points, vents and disturbance sources use declared outlets. Unverified
pressure-derived nozzle areas or visual meshes are not substituted for declared geometry.

The internal-flow convention runs from each store's loss point (column free surface or
fixed centroid), through its store outlet (column floor), then straight to the group exits.
Outlet weights distribute actual store outflow while original funding residuals remain
available; this does not alter the ordinary thrust force. The convention satisfies
p_relative=m*c_dot+sum(Q*(exit-c)); angular relative momentum depends on the declared route.
It is a model of feed momentum, not a claim that real pipes are straight.

`OpenSystemTransport` supplies the linear and angular transport beside ordinary thrust:

```
F_transport = m*(c_ddot + 2*omega cross c_dot) - p_relative_dot - 2*omega cross p_relative
T_transport = -I_dot*omega - J_out*omega - omega cross h_relative
              - h_relative_dot - c_dot cross p_relative
```

Across flow changes the structure receives the internal feed impulse while total P and H
remain continuous. Transport momentum persists across steps and save/load. Datum velocity
includes the moving offset: V_datum=V_C-omega_world cross (R*c)-R*c_dot. The one-sided
smooth derivatives alone are not a substitute for the momentum transition.

## Fragmentation and persistence

The step records each debited store's routed linear momentum and angular momentum about
its fixed construction datum. Contributions sum to the transport sample: `h_C = h_datum
- c cross p`. Stage separation, joint breakage and payload deployment move the contributions
with their owning store and translate `h_datum` by `-datum_shift cross p`. This preserves
total momentum about a common inertial origin without an artificial feed stop or pose reset.
Assigning a complete route to its source store is an explicit approximation for fragmentation;
severed-pipe fluid dynamics are not resolved.

Save V2 adds optional per-store shares, validates finite components and owner membership,
and preserves them on reload. Older known aggregates become an unowned datum share retained
by the primary fragment; unknown flow stays unknown on both fragments. Frozen untracked
paths clear the ledger. A new funding window replaces it with measured route contributions.
No tank mass, resource debit or engine restart is duplicated.

Before analytic warp/coast scheduling, retained nonzero flow moments or nonempty
store shares require a funded physical step. This settles a feed stop and clears even
shares whose aggregate cancels, so a later split cannot revive stale internal flow.
Unpowered vent/disturbance flow commands also require physical integration from their
first tick. Unknown flow does not create a retained-momentum wake condition. Active and non-active vessels follow
the same barrier; once settled they can return to the existing Kepler policy.

## Activation boundary

`Universe.VariableMassTransportEnabled` defaults true on supported coupled off-rails
steps, including rigid attached-member mass. `Vessel.LastVariableMassStatus` reports active
transport or the reason for frozen-mass fallback. Assistance replenishment, incomplete
coverage and undeclared outlets retain the frozen snapshot for that step. Legacy split
translation and water motion remain frozen. Tidal torque remains active on its existing
coupled/water/Kepler paths.

These are explicit scope limits. F5 must add modal fluid momentum and reaction consistently
with this contract; it must not add propellant mass, exhaust a second time, discard flow-cut
impulses or turn a fallback into undocumented transport. See `propellant_slosh.md`.

## Evidence

The incoming F4 suite contains settled-column geometry/rate/parity tests, continuity,
co-rotating removal, rocket-equation/feed impulses and independent jet-damping integration,
plus earlier tidal/uniform-burn gates. The historical AS-506 S-IC comparison uses the
flight-evaluation mass history/flowrates and F-1 exit station, rather than SPS actuator
coefficients. Complete CI confirms these gates and the unchanged historical/loaded Flight 14 mission
limits; see the closure record and phase registry for scope and evidence.
