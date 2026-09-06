# Contact velocity audit — 2026-09-05

## Reproduced defect

`Universe` integrates the vessel datum position with `Vessel.Velocity`.
`Vessel.GetContactInput` supplied that same velocity as the center-of-mass
velocity, despite supplying a different center-of-mass position. The contact
solver then added angular velocity crossed with the foot-to-CoM lever arm.
This omitted the velocity transport between datum and CoM.

For a fixed body-local CoM offset `c` and orientation `R`, the consistent input is:

```text
position_COM = position_datum + R*c
velocity_COM = velocity_datum + omega_world cross (R*c)
velocity_foot = velocity_COM + omega_world cross (position_foot - position_COM)
```

The final expression reduces to the derivative of
`position_datum + R*foot_local`, as required. The transport is applied in the
shared input builder used by both landing and tower-contact evaluation.

## Evidence

- Baseline suite: 729 passed, zero failed or skipped. Existing isolated solver
  tests did not detect the mismatch at the Vessel/solver boundary.
- New central-difference regression uses all six deployed Starship feet, a
  translating datum, nonzero world angular velocity, and two orientations.
- Before the fix: both cases failed with velocity errors of 2.161984 and
  2.074970 m/s, respectively.
- After the fix: all 731 simulation tests passed, zero failed or skipped.
- Regression: `ContactPointVelocityMatchesDerivativeOfDatumAndRotatedFoot` in
  `ExosphereSimulation.Tests/LandingContactIntegrationTests.cs`.

This verifies instantaneous contact kinematics and existing simulation tests.
It does not establish complete physical fidelity, validate changing-mass CoM
dynamics, or substitute for a flown EDL/tower-catch gameplay run.
