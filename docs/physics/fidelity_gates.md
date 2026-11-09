# Fidelity phase gates

Each phase of `rigid_body_fidelity_plan.md` (F0–F6) and of `environment_and_recalibration_plan.md`
(E1–E5) closes on two kinds of evidence:

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
| E1 | open | Dated time and Earth orientation (environment plan): UTC/TAI/TT/TDB, the IERS 2010 chain behind `Universe.UseIersEarthOrientation` and sourced mission epochs; closes when missions fly dated (E5) |
| E2 | open | Dated DE440 ephemerides behind `Universe.UseDatedEphemeris`; historical gate (Apollo 11 TLI/LOI with dated guidance) arrives with E5 |
| E3 | open | Spherical-harmonic gravity (GRGM1200A lunar, EGM2008 Earth) and the DE440 lunar principal axes behind `Universe.UseLunarGravityField`/`UseEarthGravityField`; historical lunar-orbit evolution gate (SP-238) pending its initial state |
| E4 | open | Measured launch-day wind (`Universe.UseWindProfile`, AS-506 IGRA sounding) HWM14 above it, dated NRLMSIS 2.1 thermosphere (`Universe.UseThermosphere`) and seeded von Kármán turbulence (`Universe.UseTurbulence`); historical AS-506 α/Qα gate needs the S-IC's historical open-loop tilt program (the simulation flies a zero-α turn that aligns with the wind) |

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
| F6 | historical | `ApolloCsmMassPropertiesHistoricalTests.IsolatedModulesCloseTheirDerivationRows` | CM-107/SM-107 dry centres and gyrations derived from SP-238 table B-I entry and SM-separation rows with tables 8-X/8-XI/8-XII residuals |
| F6 | historical | `ApolloCsmMassPropertiesHistoricalTests.CsmCompositionMatchesTheIndependentTranspositionAndTransearthRows` | CSM at transposition and TEI ignition (not used by the derivation): CG +0.08/+0.15 m, gyration +6.3/+5.1 % and +3.1/+1.5 % |
| F6 | historical | `Apollo11SpsBurnScenarioTests.DockedStackShapesTheFlownNonCircularLunarOrbit` | SP-238 circularization: 53.75 x 65.71 n mi in 16.3 s with 45.4 m/s gained vs flown 53.8 x 65.7 n mi, 16.7 s, 159.3 ft/s |
| E1 | analytic | `IersEarthOrientationTests.RotationMatchesErfaAndSpiceFrom1962To2026` | GCRS→ITRS within 0.21 mm of ERFA's IAU 2006/2000A + C04 chain at 128 instants 1962–2026; SPICE ITRF93 0.19 m median, within 3σ of each epoch's C04 uncertainty |
| E1 | historical | `DatedEarthOrientationTests.LaunchPadsSitWhereSpicePutsThemOnTheDate` | LC-39A at the Apollo 11 launch 2.17 m from SPICE (3σ of 1969 C04: 3.36 m), LC-14 at Friendship 7 2.06 m, Starbase at Flight 4 0.13 m; pad velocity within 0.15 mm/s |
| E2 | analytic | `DatedEphemerisTests.ChebyshevStatesMatchSpiceFrom1960To2050` | Barycentric DE440 states at 70 instants 1960–2050 (span boundaries included): geocentric Moon 0.17 mm, Earth 3.4 mm, planets ≤ 6 mm, velocities ≤ 1.6 µm/s; Moon at the Apollo 11 launch 5.8 cm from Horizons DE441 |
| E3 | analytic | `SphericalHarmonicGravityTests.RecursionMatchesTheDirectLegendreReference` | Cunningham V/W recursion vs direct 4π Legendre sums at 40 digits (degree 12 and 60, 100 points each, poles included): 3.3e-15 of the point-mass acceleration; degree-2 field equals the zonal J2 model to 8e-16 |
| E4 | analytic | `WindFieldTests.AerodynamicsAreGalileanUnderWind` | A Saturn-class stack resting in the AS-506 11 km wind (7.0 m/s) feels the drag of moving at −W through still air to 1e-9 relative |
| E4 | analytic | `NrlmsisAtmosphereTests.TabulatedThermosphereFollowsNrlmsis` | Tabulated NRLMSIS 2.1 for the Apollo 11 days vs the model at 300 free points (120–950 km): density 2.9 % worst / 0.42 % mean, temperature 0.67 %, pressure 1.7 % |
| E4 | analytic | `TurbulenceTests.TurbulenceHasTheVonKarmanStatistics` | 800-seed ensemble of the NASA/TM-2008-215633 von Kármán turbulence at 10 km (severe): component variances within 0.7 % of the table, longitudinal and transverse correlations within 0.012 of von Kármán's closed forms at 0.1–4 L |

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
