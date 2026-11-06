# F4: variable-mass and tidal torque kernels

Status (2026-11-06): F4 is closed. Tidal torque acts in coupled off-rails, water-motion and
Kepler-attitude propagation; variable-mass transport acts on the coupled off-rails path by
default (`Universe.VariableMassTransportEnabled`). See "Production activation" below.

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
no mission caller has been switched to it. Liquid-engine store intervals now convert to
candidate components with caller-supplied geometry; assistance and unrepresentable intervals
reject conversion. Full source coverage and overlapping vent allocation remain pending.
The adapter's resource ledger, candidate mass sampling and moving-CoM convention are specified in
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


## Production activation (2026-11-06)

### Shared mass geometry

`Parts/PartMassGeometry` is the single decomposition of a part: dry structure (centre
`dry_mass_center_y_m`, gyration `dry_gyration_m` or the part's uniform cylinder, always
`EffectiveMassDry`), each main resource (a settled column on its declared tank's equivalent
flat bottom, `propellant_tanks`, or the part's uniform cylinder) and header points (an
undeclared saved header slot stays at the part centre). `PartGraph.BuildMassProperties`,
`Part.MassCenterOffsetYM` and the step model read it; the former shifted-lump model, which
dropped the dry/propellant spread (30 kg m² in the 10 kg / 30 kg fixture), is gone.
`FixedShapeMassComponent.SettledColumn` gives the sampler a column whose centroid rises
h/2 = m/(2λ) above its floor and whose transverse specific inertia is (3R² + h²)/12, with
analytic ċ, c̈ and İ (m c̈ = Σ 2 ṁ_i ċ_i − 2 ṁ ċ; İ adds m_i J_i' and m_i P'(d_i, ċ_i)).

### Outlets and internal flow

- Engines: the mount's thrust point (every engine part places it at its aft end) less the
  engine model's `NozzleExitAxialOffsetM`; `NozzleExitDiameterM` adds the uniform-disk term,
  otherwise a point outlet. Provenance `rigid-body-mass-flux/nozzleExitStationConvention`;
  for the AS-506 F-1 the station is verified against the MSFC drawing (−115.36 in).
- RCS: the declared nozzle points; vents: `vent_outlet` (Starship, estimated); a stuck
  thruster: its declared thruster (`Gemini8FlightProfile.StartStuckThruster`).
- Feed path (`internalFlowConvention`): store loss point (free surface or centroid) → store
  outlet (column floor or centroid) → straight to the exit; open outlets share their group's
  store outflow by weight, so mass leaving the stores leaves the vehicle exactly.

### Integration

`Universe.IntegrateVesselVariableMass` integrates the instantaneous centre of mass with
stage-sampled mass properties, splits at every knot (store debits, outlet intervals, engine
exhaustion) and recovers each sub-interval's exact end so the one-sided rates belong to it.
At a knot or between steps P = m v_material + R p_rel and H = I ω + h_rel are continuous
(`Vessel.TransportRelativeMomentumBody` carries the previous step's values; saved in
SaveGameV2 so checkpoints resume bit-exactly; null in older saves means no impulse). Docked
members enter as constant components and contacts use the own-body centre at each stage.
Frozen fallback (status in `Vessel.LastVariableMassStatus`): no flow, practice assist, an
undeclared outlet, an incomplete window, the legacy split path and water motion.

### Historical gate: AS-506 S-IC jet damping

No directly measured Saturn vehicle jet-damping coefficient was found (GSOP-501's
171 ft·lbf/(rad/s) is SPS gimbal-actuator damping and is not used). The coefficient
D = İ + J_out is fixed by flight-reconstructed quantities, which the gate checks:

| Quantity | Flight (MPR-SAT-FE-69-9) | Model | Band |
| --- | --- | --- | --- |
| CG at hold-down release (X-station) | 30.276 m | 30.244 m | ±0.3 m |
| J_out at release (5 F-1, Table 5-I flows) | 14.388·10⁶ kg m²/s | 14.471·10⁶ | ±3 % |
| CG at outboard cutoff | 46.453 m | 46.956 m | ±0.6 m |
| J_out/q lever at cutoff | 2449 m² | 2500 m² | ±2.5 % |
| ΔI pitch, release → cutoff | −473.8·10⁶ kg m² | −500.2·10⁶ | ±15 % |

States are matched by mass (the model's pad start-up burns less before release than the
6.7 s F-1 sequence). Bands come from the declared approximations, not from fitting: ±1 m
S-II/S-IVB tank stations, ±2 m suction-line stations, equal-volume tank cylinders, the 1 %
flowrate spread, and the weight of İ in D (|İ| < 0.1 J_out at release, so ±15 % on İ keeps D
within ±5 %). At cutoff the flowrates are not tabulated (F-1 flow rises with the pumps'
acceleration head), so the geometric lever is compared.

Data added for the gate (`data/provenance/saturn5_as506_mass_geometry_1969.json`): S-IC,
S-II and S-IVB dry centres and gyrations derived from Table 20-10; S-IC tanks from the MSFC
drawing (dome depth 137 in, equivalent flat bottoms); S-IC LOX/RP-1 suction lines as header
stores (Table 20-1 "below tank" masses); S-II/S-IVB tanks estimated. Known data gaps (F6):
spacecraft module stations (the model's spacecraft CG is 2.3 m above the FER's 91.658 m),
S-II/S-IVB tank stations, the F-1 high-altitude flow, and the AS-503 vehicle (not yet given
the same geometry).
