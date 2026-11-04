# Fidelity phase gates

Each phase of `rigid_body_fidelity_plan.md` closes on two kinds of evidence:

- **analytic** — the model reproduces a closed-form or textbook result (conservation laws,
  slender-body theory, Newtonian plates). These pin the physics itself.
- **historical** — a flown vehicle reproduces its published outcome (insertion orbit,
  propellant at insertion, entry corridor). These pin the model against reality.

A phase is `closed` only when it has at least one gate of each kind. The table below is the
single source of truth: `FidelityGateRegistryTests` fails if a row names a test that does not
exist or lacks the matching `[Trait("FidelityGate", …)]`/`[Trait("GateKind", …)]`, if a tagged
test is missing from the table, or if a closed phase lacks either kind. Run one phase's gates
with `bash tools/fidelity_gate.sh F1`.

## Phases

| Phase | Status | Scope |
| --- | --- | --- |
| F0 | closed | Coupled 6-DoF in production, one skirt-datum frame, exact separation |
| F1 | closed | Distributed per-part aerodynamics, flight-control inversion |
| F2 | closed | Physical body flaps on measured crossflow drag (Flight 14 descent and belly-flop speed) |
| F3 | planned | Actuator-only attitude control |
| F4 | planned | Jet damping, gravity gradient |
| F5 | planned | Propellant slosh |
| F6 | planned | Historical recalibration of every profile |

## Gates

| Phase | Kind | Test | Evidence |
| --- | --- | --- | --- |
| F0 | analytic | `CompositeMassPropertiesTests.TwoCylindersEndToEndEqualOneCylinderOfTwiceTheLength` | parallel-axis composite inertia |
| F0 | analytic | `CompositeMassPropertiesTests.MemberTensorIsRotatedIntoThePrimaryFrame` | R·I·Rᵀ tensor transport |
| F0 | analytic | `CompositeMassPropertiesTests.OffsetMemberAddsProductsOfInertia` | products of inertia |
| F0 | analytic | `StageSeparationConservationTests.StagingConservesLinearMomentum` | separation conserves momentum |
| F0 | analytic | `StageSeparationConservationTests.StagingConservesCentreOfMass` | separation conserves centre of mass |
| F0 | analytic | `StageSeparationConservationTests.StagingConservesAngularMomentumAboutTheCombinedCentreOfMass` | separation conserves angular momentum |
| F0 | historical | `SaturnVAscentScenarioTests.As506ReachesParkingOrbitWithTheTranslunarPropellant` | AS-506 parking orbit and S-IVB propellant |
| F1 | analytic | `DistributedAerodynamicsTests.UniformCylinderBroadsideIsCrossflowDragAtItsCentre` | Allen–Perkins crossflow |
| F1 | analytic | `DistributedAerodynamicsTests.NoseOnFlightIsPureAxialDrag` | axial drag, no moment |
| F1 | analytic | `DistributedAerodynamicsTests.SmallAngleNormalForceSlopeIsTwoBaseAreas` | Munk C_Nα = 2 |
| F1 | analytic | `DistributedAerodynamicsTests.BareBodyOfRevolutionIsStaticallyUnstableAndAFlareStabilisesIt` | Munk moment, flare stability |
| F1 | analytic | `DistributedAerodynamicsTests.PitchRateIsDampedByTheLocalFlow` | rate damping from v + ω×r |
| F1 | analytic | `DistributedAerodynamicsTests.TailFirstFlightReversesTheNoseLiftContribution` | tail-first reversal |
| F1 | analytic | `DistributedAerodynamicsControlTests.SingleEngineWithoutRuntimeIsAllocatedThroughItsPartGimbal` | gimbal allocation torque |
| F1 | analytic | `DistributedAerodynamicsControlTests.BluntBodiesKeepTheBluntModel` | slender-body validity bound |
| F1 | analytic | `CrossflowDragTests.SutherlandViscosityMatchesTheStandardAtmosphereTable` | USSA-1976 viscosity table |
| F1 | analytic | `CrossflowDragTests.SubcriticalCylinderIsWieselsbergersValue` | Jorgensen fig. 2 subcritical C_dn |
| F1 | analytic | `CrossflowDragTests.DragCrisisDropsAndTranscriticalFlowRecovers` | Jorgensen figs. 2–3 drag crisis |
| F1 | analytic | `CrossflowDragTests.CompressibilityRemovesTheReynoldsDependence` | Jorgensen fig. 1 critical-Re data |
| F1 | analytic | `CrossflowDragTests.HypersonicCrossflowApproachesModifiedNewtonian` | Jorgensen fig. 1 Newtonian limit |
| F1 | analytic | `CrossflowDragTests.FiniteLengthFactorFollowsFinenessAndIsUnitySupersonic` | Jorgensen figs. 4–6 η |
| F1 | historical | `MercuryFriendship7Tests.Friendship7PoweredAscentClimbsAndReachesOrbitalEnergy` | MA-6 insertion 161 × 261 km |
| F1 | historical | `SaturnVAscentScenarioTests.ClosedLoopUpperStagesReachTheParkingOrbit` | AS-506 parking orbit through an S-II separation transient |
| F2 | analytic | `BodyFlapAerodynamicsTests.StagnationPressureCoefficientMatchesRayleighPitot` | Rayleigh pitot Cp,max |
| F2 | analytic | `BodyFlapAerodynamicsTests.ExtendedFlapBroadsideToABellyFirstFlowIsAFlatPlate` | Hoerner broadside plate |
| F2 | analytic | `BodyFlapAerodynamicsTests.FoldedFlapCarriesNoLoadInABellyFirstFlow` | edge-on plate |
| F2 | analytic | `BodyFlapAerodynamicsTests.ASymmetricPairOnlyPitchesAndADifferentialPairRolls` | symmetric and differential pairs |
| F2 | analytic | `BodyFlapAerodynamicsTests.FlapsDampARotationThroughTheLocalFlow` | plate rate damping |
| F2 | analytic | `HeaderTankTests.HeaderMassSitsAtItsStation` | header propellant at its station (centre of mass) |
| F2 | analytic | `HeaderTankTests.HeaderPointMassAddsItsParallelAxisInertia` | header point-mass inertia |
| F2 | analytic | `HeaderTankTests.EnginesDrainTheMainTanksBeforeTheHeaders` | feed order and mass conservation |
| F2 | analytic | `HeaderTankTests.HeaderLoadsSurviveASave` | header state persistence |
| F2 | historical | `Flight14DescentHistoricalTests.PhysicalFlapEntryReproducesTheBroadcastDescentAndBellyFlopSpeed` | Flight 14 broadcast descent displays and 363 km/h belly flop |
