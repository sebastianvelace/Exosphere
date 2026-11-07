# F6 historical recalibration

Status: open. F5 production acceptance passes 1,588 tests without skips and the Godot
stable-orbit ascent;
the profile matrix and source checks below do not establish complete historical fidelity.
Original trajectory, conservation and contact tolerances remain unchanged.

## Reproducible evidence

After building the current tests, run:

```sh
python3 tools/physics_profile_audit.py --output /tmp/exo-f6-profiles --no-build
python3 tools/physics_profile_audit.py --output /tmp/exo-f6-profiles --verify-only
dotnet run --project tools/PhysicsProfileProbe -- data /tmp/exo-f6-engineering/report.json
```

The matrix preserves TRX, per-test output, missing selectors, outcomes and source/data/test
assembly hashes. Empty selections, skipped/failed tests, a nonzero test exit or changed
inputs cannot pass. Each new run clears its own previous TRX/exit/report artifacts before
building, so an interrupted run or a successful process that emits no TRX cannot certify
old results. Three focused Python regressions exercise these evidence failures.
`--verify-only` reads preserved evidence without launching a simulation.
The engineering probe is separate: it flies three 30 s vertical climbs, measures current
mass properties, and explicitly reports that historical recalibration is incomplete.
Its success flag applies to those engineering checks only.

| Profile | Acceptance exercised | Historical limitation |
| --- | --- | --- |
| Freedom 7 | Redstone arc, separation, entry and splashdown | Estimated aerodynamic and component allocations remain |
| Friendship 7 | Atlas ascent, three orbits, retrofire and splashdown | Existing ascent passes with an effective sustainer; source-backed mass/propulsion reconstruction remains open |
| Gemini 8 | Titan insertion into the Agena plane and emergency return | Simplified controller and estimated component allocations |
| Apollo 8 | AS-503 insertion and finite TLI/correction/LOI | No declared cylindrical tank geometry on AS-503; F5 is not inferred from AS-506 |
| Apollo 11 | AS-506 insertion, mass history, probe frequencies, RCS docking, SPS capture and LM descent | Upper-stage tank stations and damping remain estimated/cross-variant |
| Starship Flight 7 | Declared configuration and controlled ascent parity | Parity is not a measured Flight 7 trajectory residual |
| Starship Flight 14 | Broadcast descent anchors and continuous pad-to-terminal-burn acceptance | Display speed frame/datum and late hypersonic speed mismatch remain unresolved |
| Falcon 9 | May 2025 ratings, engine acceptance, both fairings and 30 s engineering climbs | No mission-specific source trajectory, injection or recovery is claimed |
| New Glenn | Advertised upgraded 7x2 envelope, funded engines and 30 s engineering climb | This dated upgrade configuration is not a reconstruction of a particular flown mission |

Only the three AS-506 main tank parts currently declare cylindrical geometry, with six
resource modes. Headers, solid propellant and uniform fallback stores receive no invented
free surface. The matrix report lists this coverage per source part/resource so a catalogue
test cannot silently certify liquid dynamics on the other vehicles.

## MR-3 propellant identity correction

The A-7 engine and its cluster feed now identify the fuel as ethyl alcohol/water, with
LOX as oxidizer. [NASA TM X-53107](https://ntrs.nasa.gov/api/citations/19670028606/downloads/19670028606.pdf),
section 3, printed p. 3-2 (PDF p. 28), explicitly describes selecting alcohol for
Mercury-Redstone. The original scan was inspected. [NASA's Redstone history](https://www.nasa.gov/history/70-years-ago-first-redstone-launch-from-cape-canaveral/)
describes the mixture as 75 percent ethyl alcohol and 25 percent water, without specifying
the percentage basis. The catalogue does not infer density or thermochemistry from that
statement. The [2016 NASA vehicle summary](https://www.nasa.gov/history/mercury-redstone-launch-vehicle/)
labels the fuel LOX/RP-1; that summary conflicts with the original program report and is
the source of the former catalogue label. The original report takes precedence.

This correction leaves the separately estimated Isp, mixture ratio and tank loads at their
existing values. Production stores carry fuel mass in kg, not a species-dependent volume;
renaming both engine and feed preserves the same funded mass-flow calculation. The existing
Freedom 7 launch-to-splashdown regression retains its original acceptance limits and now
writes its numeric observations to the audit TRX on success, as well as on failure.
This closes the fuel-identity defect only; it does not validate all MR-3 mass allocations.

After the correction, the complete profile audit passes 50/50 cases over the same nine
profiles, with no skips or missing selectors. New evidence is retained in
`/tmp/exo-f6-profile-redstone/report.json` and `profiles.trx`; `--verify-only` confirms the
current source/data/test identities. The earlier F5-production report remains a separate
baseline and is not relabelled as current evidence after the input hashes change.
The complete CI for this unit also passes: 1,588/1,588 tests, no skips, four builds with
zero warnings/errors, flight startup and Godot main/VAB/graphics checks. Its successful
log is `/tmp/exo-f6-redstone-ci-retry.log` with exit artifact 0; the earlier SIGTERM
interruption is not acceptance evidence.

The corrected-data Freedom 7 run passes 6/6 tests. Its retained launch-to-splashdown
observations are 185.279 km apogee, 452.963 km maximum ground range and 911.1 s duration.
Against NASA SP-4012 volume II, Table 2-30 (187.42 km, 487.26 km and 922 s), the residuals
are -2.141 km, -34.297 km and -10.9 s. The test computes altitude at the vessel datum and
ground range from body-fixed directions times the mean Earth radius; these definitions
are retained rather than claiming an exact historical ellipsoidal ground-track match.
Peak model inertial speed is 2,236.7 m/s and peak proper load 12.61 g. Original gates pass;
the range deficit and approximate component/aerodynamic allocations remain visible F6 work.

## MA-6 mass-definition defect

The [NASA MA-6 results report](https://www.nasa.gov/wp-content/uploads/2026/01/ma06-tec.pdf),
printed p. 7, distinguishes the launch spacecraft mass (4,265 lb = 1,934.571458 kg) from
its orbital insertion mass (2,987 lb = 1,354.880409 kg). The current catalogue instead keeps
1,934.7 kg in capsule plus retro package, then adds a separate 460 kg tower. The orbital
assembly is therefore 579.819591 kg too heavy before accounting for the small ascent
consumables. This is a mass-definition error, not a reason to increase engine performance.

The same report describes fixed-thrust Atlas operation and reports ascent acceleration
reaching 7.7 g. The current 625 kN / 350 s effective sustainer and its late-burn throttle cap
are documented prior calibration. They must be replaced through a consistent reconstruction
of the launch mass, stage-and-half jettison, actual engine family, separate verniers and
guidance. Matching the existing orbit alone does not validate those hardware parameters.

[NASA SP-4012 volume II](https://ntrs.nasa.gov/api/citations/19880016045/downloads/19880016045.pdf)
also states in its launch-vehicle table conventions that vehicle weight includes the payload.
Thus the cited 117,979 kg generic Atlas launch weight cannot simply be assumed to exclude
the separately added Mercury spacecraft. The Atlas table headings and half-stage description
contain editorial inconsistencies; the flight-specific report takes precedence over an
unqualified transcription of that summary table.

An isolated native-rating/mass hypothesis fails the original orbital acceptance. Its
unverified booster/tank allocations and cross-variant ratings have not been adopted.
The permanent probe records the current discrepancy; it does not change the catalogue or
claim that this source defect has already been corrected.

### Located Atlas preflight ledger

The original [Detailed Test Objectives for NASA Mission MA-6](https://prdownloads.sourceforge.net/mscorbaddon/Mercury_AtlasTestObjectives.pdf?download=),
TOR-930(2101)-3, 10 November 1961, Appendix B (PDF pp. 89-92), was downloaded and the
scanned tables inspected. Its cover bears the later declassification marking. It provides
a coherent **preflight simulation**, not a measured postflight reconstruction:

| Quantity | Original preflight value | SI conversion |
| --- | --- | --- |
| Liftoff vehicle mass | 260,131 lb | 117,993.437 kg |
| Available LOX / fuel | 168,714 / 73,502 lb | 76,527.383 / 33,339.946 kg |
| Jettisoned booster / tower | 7,100 / 1,064 lb | 3,220.506 / 482.622 kg |
| Capsule at separation | 2,951 lb, including 40 lb growth allowance | 1,338.551 kg |
| Liftoff total thrust / flow | 366,172 lbf / 1,497 lb/s | 1,628,814.205 N / 679.028 kg/s |
| Booster / sustainer sea-level Isp | 252 / 217 s | Source heading is propulsion system MA-5 |
| Booster mixture ratio | 2.273 | Source value, not the current 2.25 assumption |
| Ignition to liftoff | 5.00 s | Loading and trajectory epochs must be kept distinct |
| BECO | 131.3 s; 207,347 ft; 10,343 ft/s; 64,365 lb | 63.199 km; 3,152.546 m/s; 29,195.473 kg |
| Booster / tower jettison | 134.1 / 154.1 s | BECO and booster jettison are separate events |
| SECO / VECO | 303.8 s; 528,427 ft; 25,695 ft/s; 10,465 lb | 161.065 km; 7,831.836 m/s; 4,746.844 kg |

Appendix B gives first-stage pitch rates (deg/s): 0 through 15 s; 0.96 from 15-27 s;
0.76 from 27-39 s; 0.64 from 39-64 s; 0.72 from 64-79 s; 0.68 from 79-89 s;
0.40 from 89-105 s; 0.28 from 105-120 s; 0.24 thereafter to staging. Its second-stage
open-loop turn is 2 deg/s from BECO+5 to BECO+24, before closed-loop guidance. These
are rates in the original program's frame, not elevations above the moving geodetic
horizon. Copying their integrated angles into the existing elevation API would change
their meaning. The source's flight-path convention also has 90 degrees at horizontal
insertion; it must not be confused with the usual zero-degree flight-path angle.

The published [NASA MA-6 results report](https://www.nasa.gov/wp-content/uploads/2026/01/ma06-tec.pdf),
Table 6-I, printed p. 71 (PDF p. 74), lists cutoff altitude 528,381 ft (161.051 km),
speed 25,730 ft/s (7,842.504 m/s), and flight-path angle -0.047 degrees; its Table 6-II
distinguishes planned from actual event times. The current profile comment's
82.6 km / 2.00 km/s BECO and 185.8 km cutoff figures are not supported by these
tables. Preserve original acceptance limits while replacing those unsourced anchors
with explicit preflight/measured quantities during the native reconstruction.

The archival [MA6 technical-report scan](https://prdownloads.sourceforge.net/mscorbaddon/MA6_TechnicalReport.pdf?download=)
is a 126-page proposed/review memorandum, not the final NASA TM X-563. Its PDF p. 17
independently lists 4,265.26 lb at launch and 2,986.78 lb in orbit, consistent with the
rounded published mass ledger above. Its draft status and preliminary landing time must
remain visible; it must not silently supersede the published results. Both scans are
image-only: `pdftotext` returning form feeds does not mean their tables are empty.

## AS-506 remaining mass-property residual

[MPR-SAT-FE-69-9](https://www.ibiblio.org/apollo/Documents/lvfea-AS506-Apollo11.pdf),
Table 20-10, printed p. 20-13 (PDF p. 245), gives actual aggregate spacecraft mass
48,626 kg and longitudinal CG X-station 91.658 m. Direct production mass decomposition
currently gives 49,821.435 kg and 94.005595 m: residuals +1,195.435 kg and +2.347595 m.
The probe uses the documented F-1 exit X-station -2.930 m to express the same datum.
The report's S-IC burn mass ledger (Table 20-1) instead lists 49,735 kg for the spacecraft.
That mass-basis difference must be reconciled before treating the Table 20-10 mass residual
as a launch-loading correction; it is retained here as a table-specific comparison.

One aggregate CG constraint does not identify separate CM/SM/LM/SLA/LES stations or their
inertia tensors. Shifting every module until that one number agrees would not establish
physical component geometry. Component stations, the estimated S-II/S-IVB equivalent tank
locations, high-altitude F-1 flow and the separate AS-503 geometry remain source-data work.

Subsequent primary-source work independently identifies a 3.453503 m error at the CM aft
interface and derives additional phase mass-property constraints. The read-only geometry
experiment reduces the aggregate error without changing component masses. See the
[AS-506 reconstruction worksheet](as506_spacecraft_reconstruction.md) for source frames,
the measured/postflight table, numerical subtractions and remaining adoption checks.

## Modern public envelopes

The dated [SpaceX May 2025 guide](https://www.spacex.com/assets/media/falcon-users-guide-2025-05-09.pdf)
supports 845 kN per sea-level Merlin, 7,605 kN for the nine-engine stage, and the preset's
981 kN MVac rating. The same guide's first-stage summary table instead prints 7,686 kN
next to the same 1,710,000 lbf; this is an internal source inconsistency. The preset retains
the explicit 9 x 845 kN statement rather than averaging incompatible figures.
The [Blue Origin New Glenn page](https://www.blueorigin.com/new-glenn)
supports seven 2,846 kN BE-4s and two 890 kN BE-3Us for the advertised configuration.
The [20 November 2025 upgrade announcement](https://www.blueorigin.com/news/new-glenn-upgraded-engines-subcooled-components-drive-enhanced-performance)
explicitly describes phased upgrades, which must not be substituted for an earlier mission.

The probe's loaded stage masses, Isp estimates and vertical flight program remain model
inputs. A finite climb verifies production funding and integration; it does not measure
mass-to-orbit capability or establish a historical trajectory residual.


## Verified F5-production profile snapshot (2026-11-07)

The nine-profile matrix passed 50/50 cases, with no skips or missing selectors. Preserved
TRX and report identities were checked again using `--verify-only`. These numbers are
observations of the current model, not revised acceptance bands:

| Quantity | Model observation | Reference / residual |
| --- | --- | --- |
| Gemini 8 insertion | T+347 s; 144 x 154 km; plane error 0.30 deg | Historical SECO 337.5 s: about +9.5 s; original orbit/plane gates pass |
| AS-503 nominal insertion | T+700 s; 181 x 195 km; S-IVB 67.8 t | Historical first cutoff about 685 s: +15 s; original propellant/orbit gates pass |
| AS-503 disturbed insertion | T+706 s; S-IVB 66.5 t | The separation perturbation is a robustness case, not a flight observation |
| AS-506 insertion | T+719 s; S-IVB 69.0 t; vehicle 128.1 t | FER about 709 s / 75.7 t / 135.7 t: +10 s / -6.7 t / -7.6 t |
| AS-506 TLI capacity | 3,254 m/s | Passes the original >3,250 m/s margin; not a measured TLI burn |
| S-IC release CG | 30.244 m | FER 30.276 m: -0.032 m |
| S-IC cutoff CG | 46.953 m | FER 46.453 m: +0.500 m |
| S-IC release outflow tensor | 14.471e6 kg m2/s | Reconstructed 14.388e6: +0.58 percent |
| S-IC pitch inertia change | -500.04e6 kg m2 | FER -473.78e6: magnitude +5.54 percent |
| S-IC first-mode frequencies | All eight LOX/RP-1 samples pass | AS-501 cross-variant comparison: original 10 percent band; native curvature retained |
| Flight 14 descent altitude | +333 / +935 / -1,655 m | Original broadcast anchors at T+10369 / 10541 / 10961; unchanged 3 km datum band |
| Flight 14 scaled belly-flop speed | 101.8 m/s at 1.2 km | Broadcast 100.8 m/s: about +1.0 m/s |
| Apollo 11 LM touchdown | 1.00 m/s; 649 s from PDI; 980 kg DPS remaining | Finite original landing scenario passes; not an exact lunar-site trajectory reconstruction |

The actual Godot Flight 7 configured ascent passed all seven capture milestones and the
stable-orbit gate, at approximately 150 x 161 km above mean Earth radius. This is the
nominal configured vehicle's successful branch, not a reproduction of Flight 7's loss.
The frame-buffer evidence was inspected. Presentation used the supported integrated preset
at 640x360; the physical scheduler and guidance were unchanged.

The Falcon/New Glenn engineering probe also passes all three 30 s climbs. It reports the
source/current spacecraft mass residuals above and explicitly sets historical calibration
complete to false. The original Mercury snapshot did not log numeric success-state residuals; its
pass result must not be promoted to a measured residual. Subsequent MR-3 runs now retain
their numeric observations, as documented above.
