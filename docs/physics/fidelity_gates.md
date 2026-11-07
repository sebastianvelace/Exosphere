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
| F3 | closed | Actuator-only attitude control, declared catalog RCS, exact Flight 14 checkpoint and historical descent; full CI 1259/1259 |
| F4 | closed | Variable-mass transport (shared mass geometry, settled tank columns, outlets, momentum-conserving flow cuts) on the coupled path; tidal moments; AS-506 S-IC jet-damping historical gate |
| F5 | closed | Production funded modes, conservative coasts, damping provenance, saves/topology verified; complete CI 1588/1588 and Godot stable-orbit ascent pass |
| F6 | open | Profile matrix and source audit underway; confirmed MA-6 mass-definition defect and native Atlas reconstruction remain open |

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
| F3 | analytic | `RcsAllocationTests.AThrusterMomentIsLeverCrossForce` | thruster moment r×F and flow F/(Isp·g0) |
| F3 | analytic | `RcsAllocationTests.ARollRequestFiresACoupleWithNoNetForce` | pure couple, duty-weighted propellant |
| F3 | analytic | `RcsAllocationTests.ATranslationRequestFiresForceWithNoNetMoment` | pure translation |
| F3 | analytic | `RcsAllocationTests.ADemandBeyondAuthoritySaturatesAtFullDuty` | duty bounds and authority |
| F3 | analytic | `RcsAllocationTests.ABrokenOrDisabledPartDoesNotFire` | no thrusters, no moment |
| F3 | analytic | `StabilityAugmentationAuthorityTests.SasCannotStopAFastTumbleFasterThanItsThrusters` | SAS bounded by thruster moment, propellant at F/(Isp·g0) |
| F3 | analytic | `StabilityAugmentationAuthorityTests.WithoutThrustersSasCannotDampAnUnpoweredTumble` | no generic RCS floor |
| F3 | analytic | `StabilityAugmentationAuthorityTests.SasStillSettlesASmallResidualRate` | SAS rate damping as a thruster command |
| F3 | analytic | `StabilityAugmentationAuthorityTests.WithoutSasAnUnpoweredTumbleConservesItsRate` | no control, no moment |
| F3 | historical | `Gemini8AnomalyScenarioTests.StuckThrusterSpinsTheSpacecraftAndReentryRcsRecoversIt` | Gemini 8 recovery on the re-entry rings (~68 % simulated propellant use, ~75 % in flight) |
| F4 | analytic | `GravityGradientTests.CircularOrbitDumbbellLibratesAtTheAnalyticSmallAngleFrequency` | circular-orbit small-angle tidal libration |
| F4 | analytic | `VariableMassRotationalDynamicsTests.UniformBurnCylinderMatchesTheNasaClosedForm` | NASA uniform-burn cylinder: constant axial spin and analytic transverse decay |
| F4 | analytic | `SettledColumnMassTests.AColumnStandsOnItsTankFloorWithTheSolidCylinderInertia` | settled liquid column centroid and solid-cylinder inertia |
| F4 | analytic | `SettledColumnMassTests.DryStructureAndColumnKeepTheirSeparation` | dry/propellant parallel-axis spread retained |
| F4 | analytic | `SettledColumnMassTests.ADrainingColumnHasAnalyticCentroidAndInertiaRates` | moving-centroid ċ, c̈ and İ against centred differences |
| F4 | analytic | `SettledColumnMassTests.TheSamplerMatchesTheGraphAtEveryLoad` | one shared decomposition for graph and sampler |
| F4 | analytic | `SettledColumnMassTests.ATankTooSmallForItsLoadIsRejected` | tank capacity contract |
| F4 | analytic | `OpenSystemTransportTests.TheFeedPathSatisfiesContinuityExactly` | p_rel = m·ċ + Σ Q (e − c) |
| F4 | analytic | `OpenSystemTransportTests.MassShedAtItsOwnStationCarriesNoMoment` | co-rotating removal: İ and exit flux cancel |
| F4 | analytic | `OpenSystemTransportTests.ABurnFollowsTheRocketEquationWithItsFeedImpulses` | rocket equation and momentum-conserving feed impulses |
| F4 | analytic | `OpenSystemTransportTests.JetDampingFollowsTheIndependentOpenSystemRotationLaw` | production jet damping against an independent integration |
| F4 | historical | `SaturnVJetDampingHistoricalTests.As506SicJetDampingMatchesTheFlightReconstructedMassHistory` | AS-506 S-IC jet damping (J_out, İ) from flight-reconstructed mass characteristics and F-1 flowrates (MPR-SAT-FE-69-9) |
| F4 | analytic | `OpenSystemTransportTests.AnExhaustedColumnsLeftDerivativeDoesNotDisappearWithItsZeroLengthFeedPath` | one-sided feed force and datum moment remain continuous at exact column exhaustion |
| F5 | analytic | `PropellantSloshTests.TheModeRootIsTheFirstZeroOfTheBesselDerivative` | independently evaluated J1 derivative root |
| F5 | analytic | `PropellantSloshTests.TheModelKeepsTheColumnsMassAndCentre` | one store mass split, unchanged liquid centroid and pendulum frequency |
| F5 | analytic | `PropellantSloshTests.DeepAndShallowLimitsMatchTheirClosedForms` | deep/shallow cylindrical first-mode limits |
| F5 | analytic | `PropellantSloshTests.ACoupledPendulumConservesMomentumAndEnergy` | nonlinear isolated planar carrier-pendulum conservation |
| F5 | analytic | `PropellantSloshTests.ACoupledPendulumRunsAtTheFreeFreeFrequency` | free-carrier frequency shift |
| F5 | analytic | `PropellantSloshTests.InternalDampingRemovesEnergyWithoutChangingMomentum` | internal viscous damping removes energy, preserves momentum |
| F5 | historical | `PropellantSloshTests.As506SicSloshFollowsTheAs501MeasuredFrequencies` | AS-506 cylindrical frequencies vs AS-501 dotted probe curves at 10/60/100/130 s; 10 percent cross-variant comparison band, not amplitude/damping validation |

F3 regression coverage also includes `Flight14EntryCheckpointTests` (exact 60-second
continuation), `AttitudeActuatorBlendingTests` (shutdown trim, coupled bank reversal,
restored authority and low-pressure handoff), `ReactionControlCatalogTests` (finite stores,
three-axis authority and mass accounting) and the unchanged Flight 14 historical descent gate.
Full validation: `bash tools/ci_check.sh`, 1259/1259 tests, three builds at 0 warnings /
0 errors, Godot startup/scene smoke tests and the graphics capture/reload check.

| F5 | analytic | `Slosh6DofDynamicsTests.ZeroDisplacementRetainsTheLockedMassCentroidAndFullTensorExactly` | full locked mass, centroid and tensor identity |
| F5 | analytic | `Slosh6DofDynamicsTests.AUniformGravitationalFieldDoesNotRestoreSloshInFreeFall` | uniform-field free fall has no invented restoring acceleration |
| F5 | analytic | `Slosh6DofDynamicsTests.PositiveLockedInertiaDoesNotHideAnIndefiniteCoupledKineticMatrix` | complete generalized kinetic matrix positivity |
| F5 | analytic | `Slosh6DofIntegratorTests.CoupledIntegrationConservesIndependentMomentaAcrossTheChartBoundary` | independent 3D P/H/energy through both hemispheres, with internal damping |
| F5 | analytic | `Slosh6DofIntegratorTests.SmoothIntegrationConvergesAtFourthOrder` | augmented constrained RK4 convergence |
| F5 | analytic | `Slosh6DofIntegratorTests.IntegratedDrainingMomentaEqualOnlyTheOutletFluxIncludingTheEmptyFlowCut` | independent integrated outlet flux and conservative empty-store retirement |
| F5 | analytic | `SloshMomentumTransferTests.PrescribedFlowCutsConserveAllSixInertialMomentaWithoutMovingTheHull` | internal parameter/flow handoffs retain P/H and datum pose |

| F5 | analytic | `PropellantSloshUniverseTests.ProductionAdvancesTheModeAndConservesIndependentClosedSystemMomenta` | production modal integration and boundary regression |
| F5 | analytic | `PropellantSloshUniverseTests.KeplerCoastAdvancesFluidAndRebuildsTheDatumFromTheMovingMassCenter` | production modal integration and boundary regression |
| F5 | analytic | `PropellantSloshUniverseTests.SaveDuringDrainThenFlowStopContinuesBitForBit` | production modal integration and boundary regression |
| F5 | analytic | `PropellantSloshUniverseTests.OlderSavesWithoutModalFieldsStartWithTheLockedDistribution` | production modal integration and boundary regression |
| F5 | analytic | `PropellantSloshUniverseTests.FundedRcsDatumTorqueDoesNotMoveWithTheLiquidCentroid` | production modal integration and boundary regression |
| F5 | analytic | `PropellantSloshUniverseTests.SavingAMovingKeplerModeContinuesAtTheSameCommittedEpoch` | active and deferred modal coast resumes at the committed save epoch |
| F5 | analytic | `PropellantSloshUniverseTests.ExhaustionFundsOnlyTheActualStoreAndRetiresItsModeAfterTheStoppedFlowHandoff` | finite exhaustion, one resource debit and conservative mode retirement |
| F5 | analytic | `PropellantSloshUniverseTests.ConfiguredDampingUsesActualProperAccelerationAndActsOnlyOnRelativeTangentMotion` | declared damping acts on relative tangent motion using actual proper acceleration |
| F5 | analytic | `PropellantSloshUniverseTests.PositiveAccelerationLocatesTheAdiabaticBoundaryWithoutAParameterValueJump` | analytic-to-held curvature cut has continuous geometry |
| F5 | analytic | `PropellantSloshUniverseTests.NonsettlingDrainKeepsBothHemispheresFiniteThroughExhaustionAndReload` | finite conservative drain continuation in both hemispheres and exact reload |
| F5 | analytic | `PropellantSloshUniverseTests.DockCaptureAndUndockPreserveMovingLiquidMomentumAndCaptureDissipatesEnergy` | coupled capture and release conserve independent P/H; capture dissipates energy |
| F5 | analytic | `PropellantSloshUniverseTests.ModesFollowTheirSourcePartsAndSeparationImpulsesConserveTotalMomentum` | staging, breakup and payload release retain store ownership and P/H |
| F5 | analytic | `PropellantSloshUniverseTests.InvalidSavedModesAreRejectedBeforeTheLiveWorldChanges` | invalid source, constraint and coefficient snapshots reject atomically |
