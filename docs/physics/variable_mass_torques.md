# F4: variable-mass and tidal torque kernels

Status: analytic foundation implemented; **not enabled in production missions**. F4 stays
open until the resource/geometry/frame adapters and a historical gate are verified.

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

## Integration boundary

The new `RigidBody6DofIntegrator.Step` overload samples mass and inertia at all four RK4
stage times and passes the same snapshot to the force callback. The callback remains pure.
The constant-mass production overload retains its numerical operations and has an exact
equivalence regression against a constant sampler. The variable-mass kernel also checks
the rocket equation, integrated burn distance and angular momentum under internal inertia
redistribution.

No production caller has been switched to the new overload. Before doing so:

1. Derive resource loss and its central inertia derivative from the same funded consumption
   step, preserving tick-cache consistency, header tanks, stage-local feed and RCS stores.
2. Declare/verify actual outlet planes and exit-area flow models. A thrust application point
   or gimbal pivot does not establish a nozzle exit plane. Do not invent one from the visual mesh.
3. Handle vents, stuck-thruster flow, startup/shutdown, fuel exhaustion and replenishment assists
   explicitly; a mass delta alone does not establish an outflow location or momentum flux.
4. Transport the construction datum, instantaneous CoM and relative-flow momentum consistently.
   The new rotational kernel does not silently supply the translational/internal-flow model.
5. Evaluate tidal torques at candidate states, including the composite inertia of docked stacks;
   define the retained on-rails attitude approximation and the water-motion adapter boundary.
6. Obtain a correctly identified Saturn vehicle jet-damping reference and re-run the historical
   mission gates and full CI before enabling F4. An SPS **gimbal actuator** damping coefficient
   is not a whole-spacecraft jet-damping measurement.
   A candidate is [Frosch and Vallely, Saturn AS-501/S-IC flight control system design](https://ntrs.nasa.gov/citations/19670056079).
   NTRS currently provides metadata without a downloadable report; that metadata alone
   is not sufficient evidence for a numerical historical gate.

The two geometric constants and the transport convention are registered in
`data/provenance/rigid_body_f4_2026.json`. No mission calibration or tolerance has changed.

## Foundation validation

`bash tools/ci_check.sh` passes 1281/1281 tests with no skips; all three builds report
0 warnings and 0 errors. Godot Flight/MainMenu/Construction smoke tests and the graphics
capture/reload check pass. The focused analytic/provenance/registry run passes 52 tests.
The rebuilt Flight 14 checkpoint's reference state is exactly equal to the F3 reference.
This validates the foundation unit; it does not enable or close production F4.
