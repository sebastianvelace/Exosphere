# F5 closure and F6 continuation

Updated 2026-11-08. User scope: complete F5, then F6; commit/push authorized; do not merge
main. F5 is pushed at `ca7949d` on `codex/f5-slosh-production`. Active continuation branch:
`codex/f6-historical-recalibration`. Author and committer: Sebas
<sebastianvelace@gmail.com>, conventional commits, no AI attribution.

## Verified F5 closure

F4 is closed at `6dd3f7e`; the F5 kernels and exact empty-column derivative correction
are pushed in `0905a38` and `a3f3f84`. Production F5 is complete and committed in `ca7949d`:

- Universe uses one funded F4 window for the full rigid/modal solve, with exact engine,
  flow and continuation cuts. No duplicate resource debit or modal mass is introduced.
- Declared main liquid cylinders get source-owned modes; only AS-506's three main tank
  parts currently declare geometry (six resources). No surfaces are inferred for headers,
  solids or fallback shapes. Damping has explicit NASA/MSFC provenance and uncertainty.
- Zero-g/full-sphere and rapid-drain paths are conservative mechanical continuations,
  explicitly outside the validated settled small-amplitude fluid domain. No invented 1 g.
- Coasts advance attitude/modes with the mean moving-mass conic. Deferred anchors, Save V2,
  one-sided coefficient jets and exact reload preserve state at committed epochs.
- Separation, breakup, payload release, docking and undocking retain store ownership;
  the complete kinetic matrix supplies impulse response. Independent P/H/energy tests pass.
- RCS retains the allocation-time torque reference before monopropellant consumption.
  An independent nozzle reconstruction first reproduced a 0.000204 N m defect, then passed.
- No-mode vessels keep the original centroid summation order. Replacing it by the equivalent
  component sum changed last bits seen by the entry controller and broke its original
  parity limit; the compatibility correction passes without changing that limit.

Complete CI: `/tmp/exo-f5-production-final-ci.log`, exit file 0. **1,588/1,588 tests,
no skips**, test time 49 m 37 s; three builds 0 warnings/errors, startup and Godot
menu/graphics PASS. The clean game was rebuilt after the temporary main harness was removed:
`/tmp/exo-f5-clean-game-build.log`. No harness/autoload changes remain in the main project.

F5-production baseline matrix: `/tmp/exo-f6-profile-final/report.json`, 50/50 cases over
nine profiles. Its full TRX retains numeric success-state output and explicit scope/geometry
coverage. It was identity-verified before the MR-3 correction. The new fuel label and test
output intentionally change source/data/test identities: preserve this baseline and generate
fresh matrix evidence before calling a report current; do not reuse its old success flag.

Fresh corrected-data matrix: `/tmp/exo-f6-profile-redstone/report.json` and `profiles.trx`,
50/50 cases, no skips or missing selectors, process exit 0. `--verify-only` passes with
the current source/data/test identities. The Freedom 7 numeric output is present in this
new TRX. This matrix is current for the Redstone identity unit; it does not close F6.

Actual Godot Flight 7 configured ascent: `/tmp/exo_play-f5-flight7-integrated/run-summary.txt`,
PASS, all seven milestones, `ASCENT_ORBIT_OK`, 1,925 frames, about 150 x 161 km above mean
Earth radius. Captures (including orbit) were inspected. It ran in the isolated project
`/tmp/exo-f5-godot-validation` with the supported integrated graphics preset at 640x360;
physics/guidance and the original orbit gates were unchanged. This is a nominal successful
vehicle branch, not a reproduction of Flight 7's actual loss. Earlier high-resolution runs
were stopped for CPU rendering cost and are not acceptance evidence.

## F6 remains open

F6 audit tooling is committed and pushed in `86b5a82`: `tools/physics_profile_audit.py`, the standalone
`tools/PhysicsProfileProbe` and `docs/physics/historical_recalibration.md`. The new C# tool
must be excluded from Godot's recursive compile in `Exosphere.csproj` (already edited).
Its build is 0/0 and `/tmp/exo-f6-engineering-final/report.json` passes all three 30 s Falcon
standard/extended and New Glenn engineering climbs. It explicitly reports historical
calibration incomplete; those public envelopes are not mission-specific trajectories.

Confirmed primary-source MA-6 issue: the NASA results report, printed p. 7, gives 4,265 lb
at launch (1,934.571458 kg), 2,987 lb at orbital insertion (1,354.880409 kg). The current
catalogue keeps 1,934.7 kg in capsule + retro alone, then adds a 460 kg tower. Orbital mass
is +579.819591 kg. NASA SP-4012 volume II's introduction also says vehicle launch weights
include payload; the old 117,979 kg Atlas value was assumed to exclude Mercury without
justification. The summary Atlas table headings/half-stage text have editorial errors;
use flight-specific definitions. The old 625 kN / 350 s effective sustainer and late-burn
throttle cap remain prior calibration, not native hardware acceptance ratings.

Do not claim that old ascent passing closes these defects. Reconstruct mass, stage-and-half
jettison, the actual engine family, separate verniers and guidance together against primary
flight data. Do not copy the unverified experiments in `/tmp/exo-f6-atlas-sandbox` into the
repository. A native approximate-rating experiment with corrected capsule mass still
misses orbital energy. Its actual attitude tracks the command (about 0.2 deg), so it is
not just a failure to point the engine. The experimental booster/tank mass allocations,
ratings and fuel loads are hypotheses. `/tmp/exo-f6-atlas-native-tracking.log` has the
full trace. Source report records fixed thrust and peak ascent about 7.7 g.

AS-506 spacecraft probe: CG 94.005595 m vs FER Table 20-10 91.658 m (+2.347595 m).
Aggregate mass is 49,821.435 kg vs that table's 48,626 kg; Table 20-1 instead lists 49,735 kg,
so reconcile the mass basis before changing launch load. Do not shift every module to fit
one aggregate CG. Upper-stage tank stations, F-1 high-altitude flow and AS-503 geometry
remain source work. The detailed F6 document records current profile residuals and limits.

Useful primary local artifacts:
- `/tmp/exo-f6-ma6-report.pdf` / `.txt`: NASA ma06-tec.pdf.
- `/tmp/exo-f6-nasa-historical-book.pdf` / `.txt`: NTRS 19880016045.
- `/tmp/exo-f6-as506-fer.pdf` / `.txt`: AS-506 FER; Table 20-10 PDF p. 245 visually checked.
- `/tmp/exo-f6-spacecraft-cg.png`: that original table.
- `/tmp/exo-as504-fer.pdf` / `.txt`: S-II LH2 damping estimate.
- `/tmp/exo-saturn-load-relief-1969.pdf` / `.txt`: nominal MSFC damping curves, not flight validation.

## Current F6 work unit

Redstone identity correction is committed in `409348e`; the source worksheet is a separate
documentation unit on the same F6 branch.

The MR-3 A-7 and cluster feed were incorrectly labelled RP-1, inherited from the NASA
2016 vehicle summary. The original NASA TM X-53107, section 3, p. 3-2 (PDF p. 28), confirms
alcohol for Mercury-Redstone. The engine/feed now say ethyl alcohol/water, with source
conflict and composition-basis uncertainty recorded in provenance. No Isp, mixture ratio,
load, trajectory limit or integrator changed. Original scan:
`/tmp/exo-f6-redstone-project.pdf`; visually inspected page:
`/tmp/exo-f6-redstone-original-fuel.png`.

Freedom 7 passes 6/6 original tests, confirmed with process exit 0. Its successful full
flight now writes numeric TRX output: `/tmp/exo-f6-redstone-identity/freedom7.trx`.
Observed apogee 185.279 km, maximum ground range 452.963 km, duration 911.1 s, peak
inertial speed 2236.7 m/s and proper load 12.61 g. Against SP-4012 Table 2-30 the first
three residuals are -2.141 km, -34.297 km and -10.9 s; datum/range definitions are explicit
in the F6 document. Full CI for this work unit passed with exit 0 in `/tmp/exo-f6-redstone-ci-retry.log`:
1588/1588 tests, zero skips/failures, test time 46 m 24 s; four builds with zero warnings
and errors; flight startup, Godot main/VAB and graphics-1280 PASS. Final exit artifact:
`/tmp/exo-f6-redstone-ci-retry.exit`. The first attempt was terminated with SIGTERM during
tests and is not acceptance evidence. All current CI, matrix, OCR and isolated probe runs
have now finished; no background verification task remains.

Located the actual Atlas preflight source via the Meadville archival index:
https://www.ibiblio.org/mscorbit/document.html (MA-6 Atlas Test Objectives), original PDF
https://prdownloads.sourceforge.net/mscorbaddon/Mercury_AtlasTestObjectives.pdf?download=
Report TOR-930(2101)-3, November 10, 1961, Appendix B trajectory simulation. Download:
`/tmp/exo-f6-ma6-atlas-test-objectives.pdf` (complete: 16,778,570 bytes, 153 pages). The secondary
Braeunig curve only located this document; it is not accepted aerodynamic data. Also
retrieving the primary technical report from the same index:
`/tmp/exo-f6-ma6-technical-report.pdf` (complete: 15,417,371 bytes, 126 pages). It is a
proposed/review memorandum, not the final NASA TM X-563. Its p. 17 weight table agrees
with the published mass ledger. Both PDFs are image-only; `pdftotext` yields form feeds.
Original Appendix B pp. B-1 through B-4 (PDF pp. 89-92) have been visually inspected;
SI conversions, loading, propulsion and pitch-program details are recorded in the F6 doc.
Important correction: original preflight BECO is 63.199 km / 3152.546 m/s, not the
82.6 km / 2000 m/s claimed by the current profile comment. Published NASA results
Table 6-I, p. 71 (PDF p. 74), gives cutoff 161.051 km / 7842.504 m/s, not 185.8 km.
Do not confuse preflight pitch rates in the program frame with geodetic elevations.
Inspect the actual scans and flight/preflight basis
before changing Atlas masses, engine ratings or aerodynamic curves.

Two further isolated benchmarks use the primary preflight loads/jettison masses, a
source-defined liftoff resource epoch with running engines, and the still approximate
native engine ratings. The first retains the old smooth guidance and fails at about
152.8 km / 6.78 km/s (`/tmp/exo-f6-atlas-preflight-load.log`); the second applies the
original first-stage pitch-rate program in a launch-fixed inertial frame and retains the
old approximate post-BECO guidance, failing at about 122.6 km / 6.97 km/s
(`/tmp/exo-f6-atlas-preflight-pitch.log`). Neither is a complete source reconstruction or
accepted production data. The restored liftoff loading is an initial-condition benchmark,
not a claim that real ground refilling continued through ignition. Current experimental
files remain in `/tmp/exo-f6-atlas-sandbox`; previous versions are saved in
`/tmp/exo-f6-atlas-preflight-backup`. Do not copy either experiment wholesale.

The original source distinguishes BECO 131.3 s from booster jettison 134.1 s; the current
profile jettisons at BECO. Separate verniers, aggregate-vs-engine flow definitions and
actual pre-ignition/liftoff loading epochs must be reconciled before adopting ratings.
OCR tooling was unpacked only under `/tmp/exo-f6-ocr` (no system installation); the
single-thread reader is `/tmp/exo-f6-ocr-atlas.py`, pages `/tmp/exo-f6-atlas-ocr/`, final
text `/tmp/exo-f6-ma6-atlas-ocr.txt`. Validate adopted numeric values against the scans.

## AS-506 independent geometry breakthrough

See `docs/physics/as506_spacecraft_reconstruction.md` for the verified source worksheet.
New primary artifacts: `/tmp/exo-f6-apollo11-mass-report.pdf` (NASA SP-238, 1971), table
B-I at PDF p. 224, visually checked in `/tmp/exo-f6-apollo11-mass-table.png`; and
`/tmp/exo-f6-csm-data-book.pdf` (SNA-8-D-027(I), revision 3, April 1970), figure 2-2 at
PDF p. 16, inspected in `/tmp/exo-f6-csm-stations.png`. Both have extracted `.txt` files.

The drawing fixes CM aft at launch station 3756.555 in = 95.416497 m, whereas production
puts it at 98.870 m: an independent +3.453503 m interface defect. SM structural span is
4.1148 m; production serializes 6.5 m SM + 1 m SPS rather than overlapping the SPS inside
SLA. LES attachment also lies above its lower bound on the CM. Almost-correct whole-stack
height currently masks the internal station error. No production geometry was edited.

The read-only helper `/tmp/exo-f6-module-probe/` changes catalogue objects only in memory,
using the built production DLL. Baseline components are in `/tmp/exo-f6-module-stations.log`;
`/tmp/exo-f6-module-stations-source-geometry.log` gives CG 94.005595 -> 92.425324 m from
source interfaces; with the independently derived jettison CG it is 92.451338 m. Mass stays
49821.434822 kg. CM aft then sits at 95.4848 m; the remaining 68 mm offset is in the unchanged
lower assembly/ring datum. No flight acceptance is claimed by this static probe.

SP-238 table B-I gives liftoff 109666.6 lb / X_A 847.0 in; orbit 100756.4 / 807.2;
CSM TD&E 63473.0 / 934.0. Figure 2-2 maps CSM X_A=1000 to launch station 3756.555 in,
so X_LV_m=(X_A_in+2756.555)*0.0254. Liftoff CG becomes 91.530297 m, mass 49743.933004 kg.
Phase subtraction derives LES/BPC CG at 102.961764 m and a remaining LM/SLA-associated
group at 85.036273 m (16911.465768 kg). Source-frame central inertia diagonals for that
group are about 43897.56/55410.07/55312.03 kg m2. The worksheet records equations, source
numbers and phase/ownership uncertainty. Do not treat the remaining group as LM alone or
silently double-count the separate model ring; map source axes explicitly. These additional
constraints make a geometry-based correction possible without shifting every module to fit
one FER CG. Next physical unit should add independent interface regressions, provenance,
then validate ascent/TLI/docking/conservation and rendering before adoption.

## Exterior-envelope work unit and held interface candidate

Implementation commits: `7ef197d` (physical exterior envelope) and `f7ce888` (exhausted
SIC feed cutoff). The unapplied interface candidate and source liquid ledger are committed
in `cd76fce`. All three belong to `codex/f6-historical-recalibration`; main is untouched.

The source checkpoint above is pushed in `63b4fdd`. A subsequent interface implementation
passed static checks but failed the unchanged AS-506 TLI capacity gate: 3222.114 m/s
versus a required capacity greater than 3250 m/s. The matrix was cancelled after that
confirmed failure; `/tmp/exo-f6-profile-interfaces/console.log` is partial failure evidence,
not a completed successful matrix. The catalogue edits were removed from production and
preserved in `docs/physics/candidates/as506_structural_interfaces.patch`. `git apply --check`
passes. The patch includes provenance, regressions and the renderer/matrix changes.

The candidate exposed independent model defects. `PartGraph.VehicleLength` previously
summed component lengths even when they overlap. `DistributedAerodynamics` counted
buried hardware and ordered diameter steps by component centres: an overlapping SPS
created two boat-tails and a Munk-area sum 65.921452 m2 versus the aft section's
80.118467 m2. The working model correction uses the physical axial/exposed-section
envelope, preserving ordinary serial-stack summation and aerodynamic element order.
It supports cylindrical overlaps with uncovered aggregate ogives; off-axis/overlapped-
ogive geometry keeps the previous approximation. No data, engine rating, load, controller
gain or historical/conservation/contact limit changes in this unit.

Independent regressions first failed: folded length 10 m versus 6 m, duplicated planform
12 m2 versus 8 m2, hidden-engine section/force errors. The corrected targeted run passes
26/26, no skips: `/tmp/exo-f6-overlap-core-green.log`. A full catalogue compatibility
check shows all 12 variants retain every aerodynamic
element component bit for bit against the previous builder, including order
(`/tmp/exo-f6-catalogue-aero-compat.log`, exit 0). The isolated old-catalogue ascent
retains exactly T+719.06 s / 69005.111 kg / 3253.890 m/s TLI capacity. With both corrected
envelope and source interfaces it gives T+721.56 s / 68469.194 kg / 3236.267 m/s: still
FAIL. Roll damping alone does not recover the gate and was not adopted. Numeric
comparisons are in `/tmp/exo-f6-as506-diagnostic/*-hull2.log` and `*-roll.log`.

Actual candidate pad views `/tmp/exo-f6-as506-geometry-view-{0,1,2}.png` were captured
under Xvfb at 1000x900 in `/tmp/exo-f5-godot-validation`; views 0 and 1 were inspected.
The isolated helper/autoload were removed; the main project never acquired a harness.
These views establish visible connections only, not ascent/docking acceptance.

The later source trace identified an additional production cutoff trap: all SIC fuel,
including suction-line headers, is gone by T+164 s, leaving 32059.2 kg of oxidizer above
the configured 31995 kg cutoff. The residual cannot decrease, so the schedule waits until
T+176.63 s. The actual trace is `/tmp/exo-f6-as506-diagnostic/baseline-fueltrace.log`.
The schedule now reacts to either exhausted constituent while retaining header feed,
the source residual/separation delay, final clock guard and AS-503 clock-only policy.
Two unit cases first failed. Final targeted tests pass 33/33, no skips, process exit 0:
`/tmp/exo-f6-overlap-cutoff.log`, TRX `/tmp/exo-f6-overlap-cutoff/overlap-cutoff.trx`.
The actual ascent regression verifies separation within the source delay plus two physics
steps of feed exhaustion, instead of waiting for the guard.

The isolated old-catalogue ascent with the cutoff correction gives T+688.60 s / 72040.549 kg
/ 3351.957 m/s capacity. With source interfaces and exposed-envelope correction it gives
T+689.52 s / 71843.331 kg / 3345.628 m/s, passing the original capacity gate. This removes
the original TLI blocker, but the candidate remains held: its other flight segments and
newly identified local-frame constraints have not been validated together. The source
mixture/ground-run reconstruction and historical cutoff cause are still open F6 work.

The first envelope-only CI was cancelled to include this confirmed schedule bug; its
`/tmp/exo-f6-core-ci.exit` is 1 and it is not acceptance evidence. **Final CI passed:**
`/tmp/exo-f6-overlap-cutoff-ci.log`, final result `/tmp/exo-f6-overlap-cutoff-ci.exit` = 0.
1607/1607 tests, no skips/failures, 47 m 50 s; all contracts, four zero-warning/error
builds, Flight 60-frame startup, Godot main/VAB and graphics-1280 checks pass.
A temporary `/tmp/exo-f6-overlap-cutoff-ci-bin/dotnet` wrapper only added TRX logging to
the unfiltered full-suite command. The nine-profile matrix was evaluated from that same
TRX using `--verify-only`: **50/50 PASS**, no missing selectors, matching source/data/test
assembly identities. Preserved artifacts:
`/tmp/exo-f6-overlap-cutoff-ci-profiles/{inputs.json,profiles.trx,report.json,test-exit-code.txt}`.
The main AS-506 test reports T+689 s, 72.0 t S-IVB / 131.1 t vehicle and 3352 m/s capacity.
No verification process remains active.

Further source checks, documented in the reconstruction worksheet: amended figure 2-3
(13 April 1971) places the RCS reference at local SM Y=1.01346 m, not the retained 2 m;
SPS gimbal plane is 0.12192 m aft of the SM interface; tower body is 26 in diameter.
It differs from figure 2-2's tower span by about 29 mm. Reconcile dates, individual
nozzle offsets, gimbal/exit geometry and phase mass properties before adopting the patch.

Atlas source section 5.2, printed p. 5-1 (PDF p. 36), explicitly says MA-5 on 109D
is a redesignation with no significant hardware difference from preceding MA-2. The
scan `/tmp/exo-f6-ma6-engine-redesignation.png` was inspected. Do not select a later
MA-5 rating solely by name. A magnified original p. B-4 confirms a fuel residual of
`.312 lb` (0.141521 kg); OCR had incorrectly emitted `-312 lb`. With the correct decimal,
the preflight load/event ledger differs by only 1.688 lb (0.765664 kg), compatible with
integer-pound source rounding. The previously alleged 142.428 kg discrepancy is false.
Original detail: `/tmp/exo-f6-atlas-residual-detail.png`. Preserve this correction when
using `/tmp/exo-f6-ma6-atlas-ocr.txt`; its raw OCR text still has the wrong sign.

The native Atlas follow-up is now documented in
`docs/physics/atlas_ma6_reconstruction.md`. New isolated comparisons use the primary
preflight timing, distinct cutoff/jettison, original open-loop turn and engineering
closed-loop insertion. Original approximate ratings with corrected nominal mixtures reach
161.025 km / 7489 m/s tangential but still fail orbit. The 1961 Atlas-Centaur nominal
envelope, without verniers, reaches 161.101 km / 7537 m/s. An ideal bounded PU allocation
reaches 161.081 km / 7774 m/s / 4809 kg, still failing the 140 km periapsis gate (about
42.9 km). All are explicitly incomplete experiments, not adopted ratings or flight tape.
The final probe mutates a shared definition only in its isolated single-vessel test; do
not copy that technique into production. `PartGraph.ConsumePropellantFromPool` uses
per-instance mass flow but per-part mixture ratio, so a mixed-model cluster alone cannot
represent different sustainer/vernier mixtures. Source valve/rating conditions and funded
runtime control/persistence need a coherent physical unit.

Contemporary handbook original: `/tmp/exo-f6-nasa-launch-handbook-1961.pdf`, 8424426 bytes,
SHA-256 `fd6d790b2c066315f75bc77468f032296715a14193499d3b85388c9e751232d1`,
inspected printed F-7/PDF p. 63. It is Atlas-Centaur preliminary planning, not 109D flight
acceptance. Original scan came from Commons after the old NTRS link failed; the worksheet
records the source URL, numbers, scope, experiment paths and remaining work.

The source SIC event ledger is transcribed in
`docs/physics/reference/as506_sic_liquid_event_history.csv` from the inspected FER
table 20-I ACT columns, printed p. 20-3/PDF p. 235. Liquid totals independently reproduce
table 5-2 reconstructed values. Ground ignition at -6.4 s and holddown-arm release at
+0.3 s differ by 39307 kg of vehicle mass; ullage gas is separate from liquid. This
reference does not change production loading. The stale F5-open paragraph in
`PHYSICS_MODEL.md` and remaining-work note in `CLAUDE.md` now reflect verified F5 closure.

## Workflow

F5 production is committed/pushed at `ca7949d`; closure registry passes 2/2. The F6 audit
unit has a zero-warning probe build, three passing evidence-guard tests and a passing full
contract suite. New runs clear previous evidence before launch, preventing stale success
reports after interruption or empty test discovery. The tool build and guards are now wired
into CI. Continue the source-backed reconstruction after the pushed audit unit.
Keep `.atl/.skill-registry.cache.json` and `.atl/skill-registry.md` out of every commit:
they were dirty before this work. No main merge. Continue F6 after the F5 checkpoint;
no historical/conservation/contact tolerance may be relaxed to force a pass.

No Engram/runtime memory tools may be called until the host re-registers an authoritative
runtime session identity. No agents have been spawned. Both the F5 baseline and the current F6 Redstone work-unit
verifications are complete; the explicit successful retry log/exit and fresh matrix above
are authoritative. Continue F6 after this checkpoint without merging main.
