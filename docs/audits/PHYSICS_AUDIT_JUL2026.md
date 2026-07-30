# Exosphere Physics Audit — Jul 2026 (engines, staging, Earth motion, reentry)

> **Scope.** Requested audit of engine physics, stage-separation physics, Earth's
> orbital/rotational motion, and reentry realism. Source of truth after this pass:
> code + tests + harness gate results, then this document. Where an earlier
> analysis pass (this same session) proposed a fix that turned out to be wrong on
> closer inspection, that is recorded here too — an audit that only lists what got
> fixed and hides what didn't is an audit that lies by omission.
>
> **Method.** Read every cited file directly; verified every numeric claim against
> the actual code, not against docs describing it. Every landed fix was gated by
> `bash tools/ci_check.sh` plus `tools/visual_playtest.sh --ascent --flight7` and
> `--edl` (the two contract-enforcing harness modes documented in `AGENTS.md` and
> `tools/lib/playtest_contracts.sh`).

---

## Summary of what landed this pass

| # | Area | Status | Commit |
|---|------|--------|--------|
| 1 | Earth's J2000 mean anomaly (1.1° / ~1.1 day phase error) | ✅ Fixed | `fix(data): correct Earth J2000 mean anomaly and thermal substep bound` |
| 2 | Thermal sub-step/scheduler coupling | ✅ Documented + hardened (was never actually reachable) | same commit as #1 |
| 3 | Stage separation: CoM teleport + missing angular-momentum transport | ✅ Fixed | `fix(physics): conserve CoM and angular momentum across stage separation` |
| 4 | Sutton-Graves nose radius fixed regardless of attitude | ✅ Fixed (additive, broadside untouched) | `feat(physics): attitude-aware Sutton-Graves stagnation radius` |
| 5 | Mach-5 hypersonic drag discontinuity (4.8% step) | ✅ Fixed | `fix(aero): remove the Mach-5 drag discontinuity` |
| 6 | Engine torque wired as unconditional attitude disturbance (R5c) | See §6 — decided during this pass, filled in below | — |

Three premises from the initial design pass turned out to be **wrong**, and are recorded in full below rather than quietly dropped, because acting on them would have made the simulator worse:

- A "Newtonian ~1.7 hypersonic drag plateau" was considered for the Mach-5 fix and rejected — it would have exceeded the flat-plate Newtonian drag maximum and broken a correct existing test (§5).
- A warp-clamp mechanism was considered for the thermal sub-step cap and was unnecessary — the failure mode it would have guarded against is unreachable by the actual scheduler (§2).
- The Sutton-Graves nose-radius error was believed to under-predict broadside heating; it actually over-predicts it, and by construction the fix does not touch the broadside case at all (§4).

---

## 1. Earth's J2000 orbital phase — fixed

**Claim in code before this pass:** `data/bodies/earth.json` declared `mean_anomaly_at_epoch: 358.617`.

**Evidence.** Earth's own `longitude_of_node` (−11.26064) + `argument_of_periapsis`
(114.20783) sum to 102.94719 — exactly Standish's J2000 longitude of perihelion
(ϖ) for Earth/EMB. So the file already agreed on where perihelion is; only the
mean anomaly disagreed with it. The correct value is `M = L − ϖ = 100.46435 −
102.94719 = 357.517°` (mod 360). The file had **358.617°**, off by **1.100°**
(≈1.116 days of orbital phase).

**Verdict.** Real, isolated data bug. Every other body in `data/bodies/*.json`
already follows the `M = L − ϖ` convention (checked all of them: Mercury,
Venus, Mars agree to within 0.05°; Jupiter and Saturn are ~0.3–0.5° off and are
explicitly out of scope — see below). Earth was the only inner-planet outlier.

**Fix.** `357.517`. Also deleted a dead root-level `"surface_gravity": 9.807` key
that duplicated (and disagreed with) the one actually read, `atmosphere.surface_gravity:
9.80665` — confirmed by grep that `CelestialBody.LoadFromJson` never reads the
root-level key; only `AtmosphereModelJson` reads the nested one.

**Test.** `EphemerisPhaseTests.cs` — `[Theory]` checks `M₀ ≈ L − ϖ` for the four
inner planets (tolerance 0.05°), plus a second check that `Ω + ω` matches the
published ϖ (so an offsetting Ω/ω error can't quietly satisfy the first check).

**Out of scope, left alone.** Jupiter (~0.35° off) and Saturn (~0.33° off) are
outside the 0.05° tolerance and were deliberately excluded rather than loosening
the test to pass them silently — they need their own correction, not a wider net.

**Known limitation not touched by this fix (see §7):** the orbital *position* is
now correctly phased at J2000, but there is still no barycentric correction (Earth
is treated as massless relative to the Sun; the Moon is massless relative to
Earth — no ~4700 km Earth wobble about the Earth-Moon barycenter), and Earth's
*rotational* phase at t=0 is arbitrary (see §7.2).

---

## 2. Thermal sub-step cap — the premise was wrong

**Original concern:** `ThermalModel`'s 256-substep cap at 0.02 s looked reachable
above a 5.12 s integration tick, which would silently integrate the stiff T⁴
radiation term at too coarse a step.

**What the code actually does.** The sub-step math is only ever reached via
`StressSolver.ApplyThermalLoads` ← `Universe.ApplyPostIntegrationPhysics` ←
`IntegrateVesselOffRails`, and the `dt` handed in there is capped by
`Universe.Tick`'s step scheduler. The loosest of those caps —
`Universe.MaxCoastStep = 2.0 s` — is the largest `dt` any caller can ever
deliver. **5.12 s is unreachable by a factor of 2.5×.**

The sub-step itself also has far more stability margin than the original 256-step
budget implied: explicit Euler on the radiative term is stable for
`h < 2c/(4εσT³)`, which comes out to ≈8.6 s for a 7 kJ/(m²·K) TPS face at 2000 K
— **~430× margin** over the actual 0.02 s sub-step.

**Verdict.** Not a live bug. There was no "if a warp tick exceeds 5.12 s"
scenario to guard against, because the scheduler structurally cannot produce
one. Building a warp-clamp for this would have been solving a problem that does
not exist, at the cost of perturbing warp behavior on the ascent path for
nothing.

**What was actually done.** Raised `MaxSubSteps` 256 → 2048 anyway (headroom is
cheap), made both constants and `Universe.MaxCoastStep` `public`, and added
`ThermalSubstepTests.cs` that explicitly asserts `MaxSubStep · MaxSubSteps ≥
Universe.MaxCoastStep` — so if a future change raises the scheduler's step cap
past what the thermal integrator can safely absorb, a test fails immediately
instead of the physics silently degrading.

---

## 3. Stage separation: CoM teleport and missing angular momentum — fixed

**Evidence (bug, now fixed).** `TriggerStaging` teleported the surviving vessel
UP by the full detached stage's length (`ActiveVessel.Position += axis *
separationHeight`) with **no complementary offset on the debris** — injecting
potential energy and discontinuously shifting altitude at every staging event
(≈70 m for Flight 7's booster). There was also no `ω × r` velocity-transport
term, so angular momentum about the combined center of mass silently leaked:
both fragments correctly inherited the same angular velocity (that part of rigid
-body kinematics was never wrong), but neither fragment's CoM velocity got the
correction that a real offset produces.

Three separate split implementations (`Vessel.Stage`, `Vessel.BreakAtJoint`,
`Vessel.DeployPayload`) had grown inconsistent — one of which lived partly in
the Godot layer (`scripts/SimulationBridge.cs`), so it produced **zero**
separation velocity when exercised from a pure-sim xUnit test, meaning the bug
was structurally untestable before this fix.

**Fix.** One shared helper, `Vessel.ApplyMassSplitKinematics`, used by all three
paths: splits the geometric offset and the opening impulse by mass ratio
(preserving the exact renderer gap the old one-sided offset produced — verified
algebraically: `L·m_d/M + L·m_s/M = L` regardless of mass ratio) and adds the
missing `ω × r` transport term. A new `PartDefinition.SeparationImpulseNs`
property lets a decoupler declare a real impulse later; left unpopulated on
every current part so every vehicle takes the same 1.0 m/s fallback the old
code always used — this is what keeps the change analyzable in isolation rather
than also silently retuning every vehicle's staging dynamics.

**Test that would have caught the original bug:**
`StageSeparationConservationTests.StagingConservesAngularMomentumAboutTheCombinedCentreOfMass`
— asserts `L` before staging equals `L` after (both fragments' spin + transport
contributions) to 1e-6 relative, with a nonzero seeded `AngularVelocity`.

**A test that was pinning the bug, now corrected:**
`StarshipRealismTests.StagingPreservesDetachedStageRigidBodyMotion` used to
assert `Assert.Equal(vessel.Position, detached.Position)` — i.e., it locked in
the exact defect. Rewritten to assert what its name actually claims: shared
orientation/angular velocity/reference body, and *conserved* (not identical)
momentum.

**Magnitude at Flight 7 staging.** Ship ≈1.5×10⁶ kg, booster ≈3.3×10⁶ kg. Old
split: ship +67.5 m, booster +0. New split: ship +46.4 m, booster −21.1 m — the
70.9 m renderer gap is unchanged, but the ship starts its post-stage insertion
~21 m lower than the old (buggy) code had it. Against LEO-scale energies this is
a ~2×10⁻⁵ relative perturbation.

**Harness result.** `--ascent --flight7` reached `ASCENT_ORBIT_OK` (orbit
189×145–192×148 km across repeated runs — see §8 on run-to-run variance in this
environment). `--edl` reached `LANDED` (6 settled contacts) — unaffected by
construction, since `BeginReentryDemonstration` overwrites the vessel's position
and velocity unconditionally after calling `TriggerStaging`, so this bug could
never have reached the EDL contract in the first place.

---

## 4. Sutton-Graves nose radius — the error direction was backwards

**Original concern:** every caller of `ThermalModel.ComputeHeatFlux` passed
`MaximumDiameter / 2` (4.5 m for Starship) regardless of vehicle attitude,
believed to *under*-predict peak flux for a belly-flopping cylinder.

**What's actually true.** Sutton-Graves is a sphere stagnation-point
correlation. For a cylinder in **crossflow** (broadside — the actual attitude
every contract-bearing test and harness path flies, since EDL's belly-flop
holds the long axis perpendicular to the velocity vector), the real 2-D
stagnation-*line* correlation (Reshotko–Beckwith) predicts roughly **1/√2 of the
sphere value at equal radius** — i.e. the fixed hull-radius value **over**-predicts
broadside heating by ~1.41×, not under-predicts it. The genuine gap is
specifically the **nose/tail-on** case, where the true radius of curvature is
the nosecone tip (~3 m for Starship), not the 4.5 m hull.

**Fix, scoped narrowly.** Added `PartDefinition.NoseRadiusM` (declared as 3.0 m
on `starship_command.json`) and `ThermalModel.EffectiveNoseRadius(hullRadius,
noseRadius, cosAlpha)`, blended by `cos²α` — the same blend shape
`AerodynamicsModel` already uses for its area/Cd mix, so both models agree on
what "broadside" means. At `cosAlpha = 0` this returns the hull radius
bit-for-bit.

**Explicitly not done, and why:** the 1/√2 broadside correction described above
was **not** applied in this pass. It's a legitimate follow-up, but it would cool
the belly-first entry ~8% (helps the `PeakStructure < 900 K` survival margin)
while narrowing the belly-vs-tail temperature gap the destruction tests rely on
(`tail.PeakStructure > belly.PeakStructure + 800`). That interaction needs its
own measured re-baseline, not a bundle with an unrelated attitude-blend fix.
Recorded here as an open follow-up (cite: Reshotko & Beckwith, stagnation-line
heat transfer for 2-D and axisymmetric bodies).

**Verification (measured, not assumed).** Both `OrbitalReentrySurvivalTests`
scenarios (belly-first, "tail-first" — which is actually shield-away-but-still-
broadside, since the long axis is perpendicular to the flow in both cases) were
replayed with instrumentation recording `max(|cosAlpha|)` across the full ~900 s
entry: **< 1e-9** for both. So every existing `PeakSkin`/`PeakStructure`/`Damage`
number is unaffected to within double-precision noise, confirmed by direct
comparison, not inferred from "the test still passes."

**A separate, pre-existing bug found and deliberately left alone:**
`scripts/VesselRenderer.cs`'s heat-flux call passes **no radius argument at
all**, silently defaulting to `noseRadius = 1.0` m — sharper than even
Starship's declared 3 m nose, and inconsistent with every other call site's
hull-radius convention. Wiring it into the new attitude blend would move a
*broadside* value (from Rn=1.0 to Rn=4.5, roughly halving that call site's
flux) — out of scope for an additive fix. Filed here as a follow-up.

---

## 5. Mach-5 hypersonic drag discontinuity — fixed; a tempting wrong fix rejected

**Evidence.** `AerodynamicsModel.GetMachDragMultiplier` reached 1.05 as Mach
approached 5 from below, then dropped to exactly 1.0 at Mach 5 — a genuine 4.8%
step discontinuity, not a physical transition.

**The wrong fix, considered and rejected during design.** A ~1.7 "Newtonian
hypersonic plateau" multiplier looked like the obvious completion of the curve.
It isn't: `ComputeReentryDrag`'s broadside coefficient is already `cd = 1.5`,
which **already exceeds** the real Newtonian crossflow limit for a circular
cylinder (`4/3 ≈ 1.33`). Multiplying by an additional ~1.7 bluffness factor
would give `Cd = 2.55` — above the flat-plate Newtonian maximum of 2.0,
physically impossible for any convex body — and would fail
`AerodynamicLiftTests.LiftOverDragIsRealisticAtStarshipEntryAttitude`, which
correctly asserts `0.2 < L/D < 0.45` and encodes Starship's real hypersonic
L/D ≈ 0.3. That test was right; the plateau idea was wrong.

**Actual fix.** Smoothed only the discontinuity: the curve now ramps linearly
from the Mach-5 value (1.05) down to exactly 1.0 over `5.0 ≤ Mach < 8.0` — which
is also approximately the physical band where Oswatitsch's Mach-independence
principle predicts pressure coefficients genuinely stop varying with Mach.
Below M5 and above M8, the function is bit-identical to before. Maximum change
anywhere on the curve: 5%, confined to that one band. The plateau being exactly
1.0 is documented in the method itself as intentional — "by construction, not
by omission" — specifically so a future pass doesn't reintroduce the rejected
multiplier.

**Verification.** This is the one fix in this pass that reaches both harness
contracts directly: `--edl`'s demo seeds entry at ~M6.1, squarely inside the
changed band, and still reached `LANDED` (6 settled contacts) across repeated
runs. `--ascent --flight7` passes through the same band briefly during ascent
and reached `ASCENT_ORBIT_OK` each time. No `OrbitalReentrySurvivalTests` or
`AerodynamicLiftTests` numeric assertion needed to change.

---

## 6. Engine torque as an unpiloted attitude disturbance (R5c) — done, scoped narrower than proposed

**Question going in.** `PartGraph.GetTotalTorque`/`GetPitchYawRollAngularAcceleration`
(from a prior session) are correct and tested — zero for a symmetric firing
cluster, correctly signed and nonzero for an off-axis engine failure — but were
never consumed by `Vessel.Tick`. The angular-acceleration block there only ran
when `hasInput` was true, so an engine-out on an idle, unpiloted vessel produced
**zero** attitude effect despite genuine torque being computable. The plan's
framing was "wire it in unconditionally"; investigation found a real reason not
to do that literally.

**What investigation found.** `GetTotalTorque` reads each engine instance's
*live* `EngineInstanceState.GimbalDeg` (`Part.GetEngineInstanceThrustGeometry`),
which the gimbal servo model (`Part.AdvanceGimbal`) drives toward the commanded
`GimbalOffset` — the same field `Vessel.Tick`'s existing pilot-authority block
sets whenever `hasInput` is true. That block already applies an *idealized*
full-authority estimate of the resulting torque
(`GetPitchYawAngularAcceleration`/`GetRollAngularAcceleration`, which use each
engine's maximum `GimbalRange`, not live servo state) for that same commanded
deflection. Wiring the real per-mount torque in for **both** branches would
therefore double-count the same causal chain — pilot commands gimbal → thrust
vector shifts → net torque — through two different models at once, while the
vessel is actively being flown.

**Resolution.** Scoped the new term to the `else` (no pilot input) branch only.
This is not "fully unconditional" as first framed, but it closes the actual
gap the plan cared about — an idle, unpiloted vessel with a failed engine now
genuinely tumbles under RK4 — without touching, or double-applying alongside,
the piloted path at all.

**Why this is provably safe for every scenario already in the harness/test
suite.** `GetTotalTorque` is exactly zero for any symmetric firing cluster
(`EngineTorqueTests.NominalSymmetricCluster_ProducesZeroNetTorque`), and neither
`AscentController`, `EDLController`, nor any mission scenario used by
`--ascent`/`--edl` ever calls `FailEngine`/`ScheduleEngineFailure` during
nominal flight — only a manual debug-injection API does. So the new term is a
no-op everywhere it could have been reached by anything already
contract-tested; it only ever fires in the new scenario (unpiloted +
asymmetric thrust) that didn't produce correct behavior before.

**Verification.** `UnpilotedEngineOutTests.cs`: nominal symmetric thrust with
no input produces no rotation; an asymmetric engine failure with no input
produces observable, correctly-signed rotation (same `sh-outer-01` mount used
by `EngineTorqueTests`); the existing piloted attitude-authority path was
pinned with a real before/after comparison — the implementing pass `git
stash`-ed just the change, reran the piloted-authority test against the
pre-change code, restored the change, and confirmed the value was identical
both times, rather than trusting a single post-hoc assertion.

**Harness.** `--ascent --flight7` reached `ASCENT_ORBIT_OK` at 178×147 km.
`--edl` reached `LANDED` with 6 settled contacts. Both expected inert under
this change (neither path injects an engine failure) and confirmed inert.

**Status of R5b (differential per-mount TVC commanding), still open.**
Unrelated to R5c and not attempted this pass: `Vessel.Tick` still mirrors one
commanded `GimbalOffset` to every gimballed mount in a part rather than
commanding each mount independently to null a target torque. That remains the
larger, separate piece needed before real boostback/hover-slam attitude control
(R12) is meaningful.

---

## 7. Known limitations — not addressed this pass, listed so they aren't rediscovered as "new" bugs

### 7.1 No J2 oblateness
Gravity everywhere is pure inverse-square point mass (`CelestialBody.GetGravityAt`).
No nodal regression for LEO orbits, no sun-synchronous or frozen-orbit
mechanics, no correct ground-track drift over multiple orbits.

### 7.2 No sidereal spin phase at epoch
`CelestialBody`'s own code comment states it directly: longitude is measured
from an arbitrary fixed prime meridian, not a true sidereal phase at t=0. So
absolute launch-site longitude, local solar time, and day/night alignment at
epoch are not physically real — only relative geometry between sites is.
`SimulationBridge.BeginReentryDemonstration` has to manually relocate the
reentry demo to the daylight side rather than inheriting whatever local solar
time the pad happens to have at J2000, which is a direct symptom of this gap.

### 7.3 No barycentric correction
Earth is treated as massless relative to the Sun; the Moon is massless
relative to Earth. No ~4700 km Earth wobble about the Earth-Moon barycenter.

### 7.4 Moon on a fixed osculating conic
No evection, no nodal regression (18.6-year period), no apsidal precession
(8.85-year period). Already flagged in `CLAUDE.md`'s Known Limits as "dated
lunar ephemerides."

### 7.5 Thermosphere has no solar-activity variability
The thermosphere density tail is a static curve — no F10.7/diurnal-bulge
variation, which in reality can swing density by an order of magnitude. Entry-
interface drag is therefore fully deterministic run to run (aside from the
environment-level non-determinism noted in §8).

### 7.6 No dynamic-pressure structural failure mode
`GetDynamicPressure` is computed and displayed, but nothing breaks from q alone
— structural failure only comes from acceleration-derived joint loads
(`StressSolver.FindBreakingJoints`).

### 7.7 EDL is a scripted autopilot, not a guidance law
No bank-angle modulation for downrange/crossrange targeting, no drag-
acceleration tracking, no target landing site — lift is always "up," never
rolled for range control. The real Apollo/Shuttle-style entry guidance problem
is unaddressed.

### 7.8 `VesselRenderer.cs`'s heat-flux call defaults to `noseRadius = 1.0` m
Noted in §4 — a separate, pre-existing inconsistency, not touched by this pass
since fixing it would move a broadside numeric value.

---

## 8. A methodological note: run-to-run variance in `--ascent`

Across five separate `--ascent --flight7` harness runs during this pass (before
any change, and after each of the four physics fixes), the resulting orbit
varied: 186×147, 196×148, 173×145, 189×148, 181×148, 192×148, 189×145 km. Every
run passed the contract (`pe ≥ atmoTop` with several km of margin each time),
but the apoapsis in particular swings by tens of km between runs with **no
relevant code change between some of those runs at all** — i.e., this variance
is inherent to this sandboxed headless/xvfb environment (most plausibly frame-
timing-dependent integration under variable real-wall-clock scheduling), not
introduced by any fix in this pass. This matters for interpreting future gate
results: a single run's apoapsis shifting by 10–20 km against a prior baseline
is not, by itself, evidence of a regression — only a periapsis approaching the
atmosphere-top margin, or the contract itself failing, is.
