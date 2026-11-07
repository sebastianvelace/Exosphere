# F5 closure and F6 continuation

Updated 2026-11-07. User scope: complete F5, then F6; commit/push authorized; do not merge
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

Additional matrix: `/tmp/exo-f6-profile-final/report.json`, 50/50 cases over nine profiles.
`python3 tools/physics_profile_audit.py --output /tmp/exo-f6-profile-final --verify-only`
passes with unchanged source/data/test assembly identities. Its full TRX retains numeric
success-state output and explicit scope/geometry coverage, not just pass/fail labels.

Actual Godot Flight 7 configured ascent: `/tmp/exo_play-f5-flight7-integrated/run-summary.txt`,
PASS, all seven milestones, `ASCENT_ORBIT_OK`, 1,925 frames, about 150 x 161 km above mean
Earth radius. Captures (including orbit) were inspected. It ran in the isolated project
`/tmp/exo-f5-godot-validation` with the supported integrated graphics preset at 640x360;
physics/guidance and the original orbit gates were unchanged. This is a nominal successful
vehicle branch, not a reproduction of Flight 7's actual loss. Earlier high-resolution runs
were stopped for CPU rendering cost and are not acceptance evidence.

## F6 remains open

F6 tooling is verified and ready for its own audit commit: `tools/physics_profile_audit.py`, the standalone
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

## Workflow

F5 production is committed/pushed at `ca7949d`; closure registry passes 2/2. The F6 audit
unit has a zero-warning probe build, three passing evidence-guard tests and a passing full
contract suite. New runs clear previous evidence before launch, preventing stale success
reports after interruption or empty test discovery. The tool build and guards are now wired
into CI. Commit/push the audit unit, then continue the source-backed reconstruction.
Keep `.atl/.skill-registry.cache.json` and `.atl/skill-registry.md` out of every commit:
they were dirty before this work. No main merge. Continue F6 after the F5 checkpoint;
no historical/conservation/contact tolerance may be relaxed to force a pass.

No Engram/runtime memory tools may be called until the host re-registers an authoritative
runtime session identity. No agents have been spawned. All current verification runs have
finished successfully; the active task is continuing F6 after the pushed F5 closure.
