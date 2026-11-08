# AS-506 spacecraft reconstruction worksheet

Status: source reconciliation in progress. The experiments below do not change production
data and do not close F6. Keep the existing historical acceptance limits.

## Independent module interfaces

[CSM/LM Spacecraft Operational Data Book, volume I, part I, revision 3](https://www.ibiblio.org/apollo/Documents/CSM_Data_Book_Part_I_Rev_3_Partial.pdf),
15 April 1970, figure 2-2, printed p. 2-3 (PDF p. 16), gives both the Saturn V structural
stations and the CSM frame. The original drawing was inspected. Dimensions exclude CM
ablative material; it is a configuration drawing, not a mission-specific mass measurement.

| Interface | Launch-vehicle station (in) | Station (m) |
| --- | --- | --- |
| SM aft / SLA forward | 3594.555 | 91.301697 |
| CM aft | 3756.555 | 95.416497 |
| CM forward | 3890.055 | 98.807397 |
| Tower separation plane on CM | 3841.182 | 97.566023 |
| LES tip | 4240.792 | 107.716117 |

At the CM aft plane, the drawing equates launch station 3756.555 in with CSM X_A=1000 in.
Thus `X_LV_m = (X_A_in + 2756.555) * 0.0254`; these are not interchangeable origins.
The SM structural span is 162 in = 4.1148 m. The SPS nozzle occupies space inside the
SLA rather than adding its entire length between the SLA forward plane and the SM body.
The tower reaches below the CM forward plane; the LES attachment is not its lowest point.
The separation-plane-to-tip span is a tower reference bound; the drawing does not establish
the complete BPC/package envelope or its internal mass distribution from those two stations.

The current catalogue instead places the CM aft plane at X=98.870 m, **3.453503 m too
high**, with a 6.5 m SM and another 1 m serial SPS part. Its LES tip is 107.770 m, so the
nearly correct total height masks the internal interface error. This is an independently
identified geometry defect, not a shift inferred solely from aggregate CG.

A read-only in-memory experiment changes the SM span, overlaps the SPS attachment plane,
uses the published CM span and LES bounds, and retains all production component masses
and their existing internal-CG assumptions. Its observations are:

| Configuration | Spacecraft mass (kg) | CG X (m) | CM aft X (m) | LES tip X (m) |
| --- | --- | --- | --- | --- |
| Production baseline | 49821.434822 | 94.005595 | 98.870000 | 107.770000 |
| Source interface geometry | 49821.434822 | 92.425324 | 95.484800 | 107.784420 |
| Also infer jettison-package CG below | 49821.434822 | 92.451338 | 95.484800 | 107.784420 |

The 68 mm remaining interface offset comes from the unchanged lower assembly/ring datum.
Stations in this worksheet subtract the retained F-1 exit-to-structural-origin offset of
2.930 m from `ComputePartLocalPositions().Y`; graph Y is the skirt datum, not Saturn
structural station zero. The drawing labels the exit at -115.36 in (about -2.930 m).
These are mass-property observations, not a flight acceptance run. Do not copy the probe's
in-memory edits into production without provenance, interface regressions, ascent/docking
checks and visual verification of the overlapping geometry.

## Postflight mass-property constraints

[NASA SP-238, Apollo 11 Mission Report](https://ntrs.nasa.gov/api/citations/19710015566/downloads/19710015566.pdf),
1971, appendix B, printed pp. 211-212 (table B-I, PDF p. 224), states that module weights
and CGs were measured and updated for changes after weighing; inertias were calculated.
The table distinguishes these phases:

| Phase | Mass (lb) | CSM X_A CG (in) |
| --- | --- | --- |
| Liftoff | 109666.6 | 847.0 |
| Earth-orbit insertion | 100756.4 | 807.2 |
| CSM at transposition/docking | 63473.0 | 934.0 |

The liftoff mass is 49743.933004 kg and, using the drawing's frame translation, CG is
91.530297 m. These are additional postflight constraints. FER table 20-10 instead lists
48626 kg / 91.658 m, whereas FER table 20-1 lists 49735 kg. Retain the source/basis
difference; do not alter a launch load to the lighter table merely to reduce a residual.

Subtracting the first two phase mass moments gives a removed group of 8910.2 lb and
X_A=1297.057768 in, or launch X=102.961764 m. Its mass agrees with the catalogue's
8910 lb LES/BPC allocation to 0.2 lb. This is a derived jettison-package CG, subject to
phase consumables, table rounding and the assumption that the mass difference belongs
to that package. It is not a direct measurement of the tower alone.

Subtracting the CSM transposition mass/moment from the insertion total gives a remaining
group of 37283.4 lb = 16911.465768 kg, X_A=591.329763 in, or launch X=85.036273 m.
This group includes LM/SLA-associated hardware. It is not the LM alone. The current
SLA-plus-LM part is a uniform 16879.986457 kg at X=87.070 m and the model also carries a
separate 100 kg ring. Reconcile that ownership and the CSM's intervening consumables
before adopting the subtraction as an exact component allocation.

Using all three CG coordinates and the parallel-axis theorem, the same subtraction gives
remaining-group central inertia diagonals approximately 43897.56 / 55410.07 / 55312.03
kg m2 in source X/Y/Z axes. The corresponding radii of gyration are 1.61113 / 1.81010 /
1.80850 m. The subtraction uses insertion diagonals 67108 / 713136 / 715672 and CSM
diagonals 34445 / 76781 / 79530 slug ft2. Phase and allocation uncertainty also applies
to these derived quantities; map source axes and product-of-inertia signs explicitly
before using them in the simulation's local Y-up frame.

## Interface candidate and acceptance failure

The independently sourced interfaces are preserved in
[`candidates/as506_structural_interfaces.patch`](candidates/as506_structural_interfaces.patch).
It contains the four catalogue edits, provenance, interface regression, visual adjustment
and two additional historical-matrix selectors. It preserves the held candidate at
`be2b81e`; the later implementation below supersedes it. From a clean checkout of
`be2b81e`, reproduce that historical candidate with:

```bash
git apply --check docs/physics/candidates/as506_structural_interfaces.patch
git apply docs/physics/candidates/as506_structural_interfaces.patch
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --filter FullyQualifiedName~SaturnVAscentScenarioTests.As506ReachesParkingOrbitWithTheTranslunarPropellant
```

The original candidate passed its static interfaces but failed the unchanged TLI capacity
gate: 3222.114 m/s against a required capacity greater than 3250 m/s. Insertion occurred at
T+723.56 s with 68040.459 kg of S-IVB propellant. The candidate matrix was cancelled after
this confirmed failure; `/tmp/exo-f6-profile-interfaces/console.log` is failure evidence,
not a completed matrix report. No successful TRX was produced by that cancelled run.

The failure exposed two model assumptions: length was the sum of serial part lengths, and
distributed aerodynamics counted an overlapping SPS as exposed hardware. Its two diameter
steps both became boat-tails when ordered by component centres. At launch the summed Munk
area change was 65.921452 m2 instead of the 80.118467 m2 aft section. The independent
exterior-envelope correction restores that balance and removes duplicate planform.

An isolated comparison using the corrected envelope retains the old catalogue's result
exactly: T+719.06 s, 69005.111 kg and 3253.890 m/s capacity. With the interface candidate
it reaches T+721.56 s, 68469.194 kg and **3236.267 m/s**, still below the original gate.
Adding actuator-based roll damping alone also fails (3221.270 m/s on the uncorrected
envelope); it is not adopted as a solution. These comparisons use identical engine ratings,
loads and historical bounds. Do not retune those values merely to recover this gate.

Three actual Godot pad views were captured in an isolated project using Xvfb at 1000x900:
`/tmp/exo-f6-as506-geometry-view-{0,1,2}.png`; the first two views were inspected. The
temporary helper and autoload were removed. This validates the candidate's visible module
connections only, not ascent, docking or historical mass-property closure.

### Confirmed S-IC cutoff trap

A separate trace of the existing catalogue found LF and header LF exhausted by T+164 s,
while 32059.2 kg of oxidizer remained. The configured aggregate cutoff is 31995 kg;
the remaining oxidizer could never cross it. The schedule waited until its clock guard
at T+176.63 s before separating at about T+177.34 s. The small residual above threshold
therefore created roughly 13 s without S-IC propulsion. Evidence:
`/tmp/exo-f6-as506-diagnostic/baseline-fueltrace.log`.

The schedule now cuts off when either SIC feed constituent is exhausted, counting main
and header stores. It preserves the residual, separation delay, guard and AS-503 clock
policy. This is a model burnout response, not a claim that Apollo 11's actual LOX-level
sensor responded to exhausted fuel. The native varying mixture ratio and ignition-to-
liftoff consumption still require reconstruction; figure 5-3 of the FER (printed p. 5-4,
PDF p. 74) shows a changing flight mixture ratio, unlike the current constant 2.27.

The original FER table 20-I, printed p. 20-3 (PDF p. 235), was also inspected in
`/tmp/exo-f6-as506-sic-mass-events.png`. Its ACT columns are transcribed in
[`reference/as506_sic_liquid_event_history.csv`](reference/as506_sic_liquid_event_history.csv).
Range time is -6.40 s at ground ignition and +0.30 s at holddown-arm release; reported
vehicle mass falls from 2938315 to 2899008 kg, a 39307 kg change over this startup window.
Those epochs cannot share one initial inventory. Summing liquid-in-tank and below-tank
mass reproduces table 5-2's reconstructed quantities at every listed event, including
OECO LOX 18041 kg and fuel 13954 kg: exactly the configured 31995 kg residual.
Ullage gas is a separate mass, not burnable liquid. The CSV preserves source rounding
and event definitions; it is a reference ledger, not a new production loading.

With the cutoff correction, an isolated old-catalogue ascent inserts at T+688.60 s with
72040.549 kg and capacity 3351.957 m/s. The interface candidate with corrected exposed
envelope inserts at T+689.52 s with 71843.331 kg and capacity 3345.628 m/s, passing the
original TLI-capacity bound without a rating, load or guidance change. This supersedes
the TLI failure as an adoption blocker. At that checkpoint the other candidate segments
and source-frame constraints below remained unverified; the patch was held.

Next reconcile component CG/inertia and the remaining lower-assembly datum against the
phase-specific sources above, then rerun ascent/TLI, spacecraft segments, conservation
and rendering. Passing the source-interface unit does not close the aggregate historical
mass-property reconstruction.

### Additional local-frame constraints

The same Data Book PDF, figure 2-3, printed p. 2-4 (PDF p. 17), was inspected in
`/tmp/exo-f6-csm-module-frames.png`. This page explicitly carries an amendment dated
13 April 1971, so keep its configuration date separate from figure 2-2 and the 1969 flight.
It places the RCS-quad reference 41.10 in aft of the CM structural plane. For a 162 in
SM span, that reference is `(81 - 41.10) * 0.0254 = 1.01346 m` above the SM centre.
Retaining the catalogue's local Y=2.0 m after shortening the SM would leave the quad
reference 0.98654 m forward of this drawing. The drawing does not establish each
individual nozzle offset or its thrust direction. Reconcile those measured/estimated
offsets before applying a new local station; do not move only the visible module shell.

Figure 2-3 also identifies the SPS gimbal plane at CSM X_A=833.2 in, 4.8 in
(0.12192 m) aft of the SM structural interface X_A=838.0 in. That is a gimbal reference,
not the exhaust exit. The candidate's retained 1 m SPS part/outlet remains an estimate;
derive its gimbal and exit offset together under the F4 transport convention.

The figure labels the LES motor body diameter as 26 in (0.6604 m), distinct from the
CM/BPC base width. A 3.91 m diameter over the full LES bounds remains an aerodynamic
envelope approximation. It also gives a 400.76 in tower span, whereas figure 2-2's two
stations imply 399.610 in: about 29 mm difference between these nominal drawings.
Do not combine their dimensions as an exact mission-specific survey. These limitations
further prevent treating the interface-only patch as a closed F6 reconstruction.

## Local evidence

- Source report: `/tmp/exo-f6-apollo11-mass-report.pdf` and `.txt`.
- Configuration drawing: `/tmp/exo-f6-csm-data-book.pdf`, `.txt`, and
  `/tmp/exo-f6-csm-stations.png`.
- Read-only helper: `/tmp/exo-f6-module-probe/`; it references the built production DLL.
- Baseline component stations: `/tmp/exo-f6-module-stations.log`.
- In-memory comparison: `/tmp/exo-f6-module-stations-source-geometry.log`.

After the structural-interface unit, apply phase-consistent component mass geometry.
Preserve physical thrust/flow references,
split ownership and the original ascent/TLI/docking/conservation gates.

## Structural and actuator reference implementation (2026-11-08)

The catalogue now adopts figure 2-2's SM/CM spans and overlapping SPS/LES interfaces.
The SM renderer bounds its shell to the part and draws RCS hubs at their catalogue centres.
The 1 m SPS part remains a logical allocation; both stack connections sit at the SM aft
plane. The LES truss renderer derives its feet from the attachment and reference bounds
so the longer part does not pull its truss below the capsule.

Figure 2-3's nominal RCS reference is now local SM Y=1.01346 m. Each simplified quad
still puts its four jets in that plane; radial positions, directions and Isp remain the
prior estimates, with their uncertainty retained. This is not a measured CSM-107 nozzle
layout. SPS thrust is at local engine Y=0.37808 m, or 0.12192 m aft of the SM interface.
`Part` adds this offset to the cluster mount for thrust, gimbal authority, fault isolation
and its outlet fallback; the zero mount offset must not receive the same correction twice.
Section 4.4.4.3 (printed p. 4.4-20 / PDF p. 169) independently places thrust application
and the gimbal plane at X_A=833.2 in; section 4.5 quotes a ±0.25 in gimbal-axis tolerance.

The [Apollo Operations Handbook, Block II, volume 1](https://www.ibiblio.org/apollo/Documents/49176721-Apollo-Operations-Handbook-Block-II-Spacecraft-Volume-1.pdf),
printed p. 2.4-43 / PDF p. 212, basic 15 April 1969, changed 16 July 1969, gives maximum
engine length 159.944 in (4.0625776 m) and nozzle inside exit diameter 98.4 in (2.49936 m).
The original scan was inspected in `/tmp/exo-f6-sps-dimensions-1969.png`. These values
supersede the coarse 1968 News Reference's approximately 7 ft exit diameter as the next
geometry reference, but maximum overall length does not give gimbal-to-exit distance.
No exit offset or finite exit radius is adopted here: the existing point outlet remains
a declared approximation at the thrust point, not the physical bell exit. The reference
interface correction does not establish SPS dry CG/inertia or close F4 nozzle geometry.

Independent regressions first failed the old SM span (6.5 m versus 4.1148 m) and RCS
reference plane. The corrected catalogue passes the initial 26 data, RCS-catalogue and
engine-torque checks. All six targeted segment/controller cases pass in
`/tmp/exo-f6-as506-interface-segments/segments.trx`. Two exercise AS-506: insertion
T+690 s, S-IVB 71.8 t / vehicle 130.9 t, capacity 3346 m/s; docking 344 s / 86.6 kg RCS.
The four `LunarOrbitInsertionBurnTests` use CSM-103 (Apollo 8), so their LOI/circularization
results are shared-controller regression evidence only, not validation of CSM-107's SPS.
A separate docked CSM-107/LM-5 finite-burn scenario is still required.
The higher docking consumption than the prior geometry is retained as an observation,
not calibrated away. Native CG/inertia and nozzle offsets remain open. Full CI and
framebuffer evidence below define acceptance for this interface unit.

### Framebuffer verification

A real production Flight capture was written to
`/tmp/exo_play-f6-as506-interfaces/exo_play_pad.png` at 1920x1080. Its harness reached
`SUMMARY reason=SMOKE_OK frames=50`, but `--verify-only` rejected the generic smoke
contract because it expects the Starbase single-hero complex, not LC-39A. This is a
failed harness run, not full smoke acceptance; its inputs and image remain evidence.
No launch-complex acceptance limit was removed or relaxed.

An isolated scene then compared the original `be2b81e` renderer with the new renderer,
using the same corrected catalogue, camera and lighting, with real Xvfb/llvmpipe pixels.
Six 1280x1000 captures per version cover two oblique views and a close-up for AS-506
and AS-503. Authoritative outputs are `/tmp/exo-f6-as506-render-before-{0..5}.png`
and `/tmp/exo-f6-as506-render-after-rebuilt-{0..5}.png`; AS-506 views 0, 1 and 2 and
AS-503 close-up 5 were inspected. The corrected tower feet stay at the CM attachment
rather than running into its side; the shorter SM shell stays within its axial span;
RCS hubs follow catalogue centres. The AS-503 body/tower retains its earlier bounds,
while its visible RCS hubs now agree with its existing physics catalogue.

The first incremental after-build reused stale output after source timestamps were
copied; those identically rendered frames were discarded. A full isolated `Rebuild`
produced the verified after-rebuilt set. Comparison metadata and renderer hash are in
`/tmp/exo-f6-as506-render-comparison.json`. Both final builds have zero warnings/errors,
no engine/script errors were logged, and the temporary scripts/scenes were removed.
This verifies the affected static meshes, not a complete ascent/return visual mission.

Full CI for this unit passes 1609/1609 tests with zero skips/failures (46 m 57 s), four
zero-warning/error builds, contracts, Flight 60-frame startup and main/VAB/graphics-1280
checks. Result `/tmp/exo-f6-as506-interfaces-ci.exit` is 0. The same unfiltered TRX supplies
`/tmp/exo-f6-as506-interfaces-ci-profiles/report.json`: 53/53 selected cases over nine
profiles pass with no missing selectors; `--verify-only` confirms source/data/assembly
identities. The failed LC-39A generic smoke run above remains separate evidence.


## Docked capture and measured SPS inventory follow-up (2026-11-08)

The controlled arrival now has its own CSM-107/LM-5 fixture using the real `TryDock`
constraint. Before correction, PeriapsisCaptureBurn estimated the ignition window with
only the primary CSM mass: ignition T+2856.30 s, 384.32 s burn, 55.28 x 311.88 km orbit,
failing the existing 81 km minimum perilune. Using `TranslationalMassKg` gives ignition
T+2781.14 s, 380.66 s burn and 101.94 x 312.02 km. The new 180 s ignition-window test
also fails the original controller directly. Two docked and four existing Apollo 8 cases
pass 6/6 in the main tree with the original inventory (2 m 35 s). Lunar targets and the
2580 m/s controlled arrival remain the common Apollo 8 proxy, not an Apollo 11 replay.
