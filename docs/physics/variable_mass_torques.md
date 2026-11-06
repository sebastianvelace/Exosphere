# F4: variable-mass and tidal torque kernels

Status: tidal torque is enabled by default in coupled off-rails, water-motion and
Kepler-attitude propagation. Variable-mass transport is still pending. F4 stays open
until its resource/exit/CoM adapters and the historical gate are verified.

## Rotational balance

The kernel returns the transport part of the body-frame balance:

```text
I omega_dot = torque_external + torque_thrust - omega cross (I omega)
              - I_dot omega - J_out omega
              - omega cross h_relative - h_relative_dot
              - c_dot cross p_relative
```

`I_dot` and `h_relative_dot` are derivatives in body coordinates about the instantaneous
centre of mass. Relative velocities defining `p_relative` and `h_relative` are measured
in the material body frame; `c_dot` is the centroid's velocity within that frame. The
moving-origin term follows from Reynolds angular-momentum balance about the drifting CoM.
Positive outlet mass flow is outward. `J_out` integrates
`mdot * ((r dot r) Identity - r r^T)` over each outlet. A uniform circular exit adds
`mdot * a^2/4 * (Identity + n n^T)`; a point outlet cannot represent axial spin transport.
Zero internal-flow momentum is an explicit symmetry/negligible-flow assumption.

Source: [NASA/CR-1998-208246, section 3.2 and section 4.3.1](https://ntrs.nasa.gov/api/citations/19980210404/downloads/19980210404.pdf).
The uniform-burn cylinder is an **analytic** benchmark, not a Saturn flight-history gate.
Its axial rate stays constant; its transverse rate follows the closed-form mass-ratio law.

The implementation separates transport from `r cross thrust`. Neither the thrust moment
nor Euler's gyroscopic term may be included twice. In particular, removing co-rotating
point mass at its own station gives cancellation between inertia variation and exit flux;
applying a jet-damping term alone fails this case.

## Gravity gradient

The tidal kernel evaluates `3 mu/r^3 * n cross (I n)` with an attractor-to-CoM unit vector
and the complete central inertia tensor in the same body frame. It uses the geocentric
direction, not geodetic surface up. Source: [Yang, equation 5.5](https://ntrs.nasa.gov/api/citations/20240009554/downloads/Space%20Attitude%20Development%20Control.pdf).
The retained approximation is first order in spacecraft size divided by attractor distance.
Tests compare against direct gravity on two point masses and circular-orbit tidal libration.
`RigidBodyForceEvaluator` sums every attractor using each candidate CoM/orientation and
the supplied central tensor, including composite docked inertia. Attractor positions are
the same prepared positions used by the translational gravity evaluator.
`Universe.GravityGradientEnabled` defaults to true; false selects uniform gravity for
isolated comparison tests. The legacy split path retains its diagnostic approximation.

On Kepler rails, `CoastingAttitudeIntegrator` evolves Euler rotation and tidal moments
along the prescribed CoM conic. Both reference-body and attractor positions are sampled
at each candidate epoch through `BodyStateAt`; the orbit receives no artificial attitude
force. Docked primary coasts use the composite central tensor and composite CoM.
Adaptive RK4 step doubling uses a divisor of 15 for the fourth-order local-error estimate,
with per-second orientation and angular-rate budgets of 1e-11, both scaled by angular
rate above 1 rad/s (orientation also has an error budget per swept radian). This avoids
requesting sub-roundoff errors at extreme spin. A separate 0.25 rad
trial-sweep bound prevents large-angle aliasing of the estimate. These are registered
numerical choices, not physical damping or historical calibration.

Deferred scheduler anchors include attitude and angular velocity. Public projections
reconstruct rotation from that same anchor; deadline catch-up restores it before advancing,
so an already projected rotation is never integrated twice. A functional SAS with live RCS
keeps the vessel in funded physical integration even at high warp. Pending RCS arming gets
a normal physics step without mutating systems during classification; depleted or absent
actuators allow a passive tidal coast. Surface-settled, held-down and water-motion states
retain their own anchoring/contact dispatch; enabling idle SAS does not wake an anchored craft. Torque-free momentum/energy, small-angle tidal
libration, high-spin accuracy, fine-step parity and scheduler wake regressions cover this path.

The existing low-spin gate for passive-payload batching is retained. If tidal rotation
exceeds that eligibility bound, the payload returns to normal physical stepping. The
batch regression checks that deferral occurred during the run, while keeping all original
position, velocity, mass and thermal limits; it no longer requires deferral on the last
frame after the model has physically spun up the payload. This conservative fallback can
cost CPU time. Extending batching requires rotational accuracy evidence rather than
removing the tidal torque or raising a mission acceptance tolerance.

## Integration boundary

The new `RigidBody6DofIntegrator.Step` overload samples mass and inertia at all four RK4
stage times and passes the same snapshot to the force callback. The callback remains pure.
The constant-mass production overload retains its numerical operations and has an exact
equivalence regression against a constant sampler. The variable-mass kernel also checks
the rocket equation, integrated burn distance and angular momentum under internal inertia
redistribution.

No production caller has been switched to the variable-mass overload. A6 now has atomic
RCS owning/shared-store funding, in-step liquid-engine exhaustion and optional immutable
RCS/liquid-engine/main-vent receipts. Continuous mass/transport integration and the remaining
source/exit contracts are still pending. `BodyMassDistribution` now supplies a pure
fixed-component candidate sampler with one-sided central derivatives at explicit mass knots;
no mission caller has been switched to it. The next adapter's
resource ledger, candidate mass sampling and moving-CoM convention are specified in
[`variable_mass_resource_adapter.md`](variable_mass_resource_adapter.md). Before activation:

1. Derive resource loss and its central inertia derivative from the same funded consumption
   step, preserving tick-cache consistency, header tanks, stage-local feed and RCS stores.
2. Declare/verify actual outlet planes and exit-area flow models. A thrust application point
   or gimbal pivot does not establish a nozzle exit plane. Do not invent one from the visual mesh.
3. Handle vents, stuck-thruster flow, startup/shutdown, fuel exhaustion and replenishment assists
   explicitly; a mass delta alone does not establish an outflow location or momentum flux.
4. Transport the construction datum, instantaneous CoM and relative-flow momentum consistently.
   The new rotational kernel does not silently supply the translational/internal-flow model.
5. Preserve the candidate-state tidal torque and composite central tensor when replacing
   the frozen-mass adapter, including Kepler attitude and water-motion frame conversions.
6. Obtain a correctly identified Saturn vehicle jet-damping reference and re-run the historical
   mission gates and full CI before enabling F4. An SPS **gimbal actuator** damping coefficient
   is not a whole-spacecraft jet-damping measurement.
   A candidate is [Frosch and Vallely, Saturn AS-501/S-IC flight control system design](https://ntrs.nasa.gov/citations/19670056079).
   NTRS currently provides metadata without a downloadable report; that metadata alone
   is not sufficient evidence for a numerical historical gate.

The geometric constants, transport convention and numerical attitude controls are registered in
`data/provenance/rigid_body_f4_2026.json`. No mission calibration or tolerance has changed.

## Foundation validation

`bash tools/ci_check.sh` passes 1281/1281 tests with no skips; all three builds report
0 warnings and 0 errors. Godot Flight/MainMenu/Construction smoke tests and the graphics
capture/reload check pass. The focused analytic/provenance/registry run passes 52 tests.
The rebuilt Flight 14 checkpoint's reference state is exactly equal to the F3 reference.
This validates commit 14349dd, the analytic foundation unit, before tidal activation.
It does not close F4.

## Tidal activation validation (2026-11-05)

The production tidal/Kepler unit passes `bash tools/ci_check.sh`: 1304/1304 tests, no
skips; all three builds report 0 warnings and 0 errors. Flight reaches its 60-frame
startup gate, MainMenu and Construction boot successfully, and the graphics capture/reload
check passes. The original historical mission limits, exact checkpoint continuation,
Flight 14 loaded return/contact gates and passive-payload numeric limits all remain green.
No mission calibration or acceptance tolerance was changed. The new Flight 14 cache key is
`389B25FCE5132B8C9DAFCE19E1D56EE85FF471DC28701066B8D7134B0060AB3D`.

This completes the planned A7 point-attractor tidal adapter. F4 remains open for A6:
funded variable-mass transport, verified exits/internal-flow geometry and its historical
Saturn jet-damping gate.
