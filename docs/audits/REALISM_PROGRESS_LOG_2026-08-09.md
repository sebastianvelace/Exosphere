# Realism program progress log — 2026-08-09

This is the integration ledger for the ten-hour execution plan. Every entry names the
evidence that justified the next tranche; a failed visual gate is recorded as a failure,
not converted into a passing screenshot.

## Completed tranches

| Commit | Tranche | Evidence |
| --- | --- | --- |
| `2042121` | Shared thermodynamic density profile (`P/T`, thermosphere fallback) | 8 profile tests |
| `45f0991` | Bounded aerosol/climate CPU state | 8 aerosol tests |
| `909b77d` | Visual baseline and matrix gates | 16/16 atmosphere milestones |
| `feab576` | Flight 7 ascent baseline | `ASCENT_ORBIT_OK`, stable orbit |
| `eb3c438` | Reproducible EDL failure record | entry→flip→crash evidence |
| `99878e4` | LUT and CPU profile parity | LUT/profile round-trip tests |
| `05dd4ec` | Continue selects newest valid save | corrupt-slot/order tests |
| `9704f2d` | Do not cut thrust on a single landing foot | 5/5 contact tests |
| `afe8812` | Stage-aware engine telemetry | shell contract 33→39→6 |
| `25f068a` | Failed engines excluded from rated thrust | 4 focused + suite 532/532 |
| `121bc7b` | Profile-aware optical transport overload | optics 35/35 + suite 534/534 |
| `85030aa` | Two-engine final authority floor | EDL focused 6/6 |
| `8701f32` | Avoid three-engine minimum-throttle hover | EDL focused 6/6 |
| `aab7e7b` | Record EDL v3–v5 gate evidence | logs and acceptance criteria |
| `4d36b73` | Gate low-energy single-engine final descent | focused EDL 7/7; EDL v6 still fails with 106.31 m/s contact |
| `a13fc02` | Keep upright authority through final lateral recovery | focused EDL 8/8 + Godot build; v7 visual gate pending while another harness owns the lock |
| `ce4a64b` | Opt-in data-driven aerosol climate profiles | JSON boundary tests + bounded AOD/Mie normalisation |
| `2115102` | Bind aerosol climate to realtime sky transport | shader profile/Ångström/vertical Mie path; legacy switch retained |
| `ce97d59` | Calibrate Earth coastal aerosol reference | v1 optical gate failure recorded; v2 clipping below threshold |
| `6d71902` | Document aerosol renderer evidence | v2 `ATMOSPHERE_OK`, 16/16, 1157 frames |
| `c8a7558` | Transition to one engine before final hover | EDL source contracts updated; visual v9 incomplete before final descent |
| `9e5c924` | Cap final lateral braking demand | EDL source contracts + 8/8 focused tests; v8 reduced rebound but remained in two-engine hover |
| `ac4a08e` | Bias one-engine final profile toward descent | EDL source contracts + 8/8 focused tests + build; visual validation remains pending |

## Current gates

- Flight 7 stage-aware ascent: `stage-ascent-v1` passed `ASCENT_ORBIT_OK`; log contains
  `ENGINE_STAGE` rows for booster 33, hot-stage 39 and Ship 6.
- Atmosphere matrix: `baseline-atm-v1` passed `ATMOSPHERE_OK` with 16/16 milestones. The
  post-integration `aerosol-v2` matrix also passed `ATMOSPHERE_OK` with 16/16 and 1157 frames;
  the `aerosol-v1` clipping failure is retained in its audit as a non-passing run.
- EDL v3 reproduced a one-engine lateral rebound; v4 reproduced a three-engine hover;
  v5 reproduced a two-engine minimum-throttle rebound at ~20 m; v6 reproduced a late
  two-engine bounce and 106.31 m/s overload at contact. EDL touchdown remains open.
- EDL v7 removed the final-axis reversal but oscillated at roughly 272–335 m; v8 reduced the
  lateral-braking rebound but hovered at roughly 211–230 m; neither produced contact.
- `edl-one-engine-v9` reached `FINAL_DESCENT` and selected one engine around `t=356,6 s`/
  `alt≈226 m`, but then hovered around 220–240 m until it was stopped. `ac4a08e` adds a bounded
  one-engine descent bias; because no visual run has validated it yet, there is still no
  touchdown artifact or passing EDL summary. Keep the physical gate open.
- The test suite is green on the latest completed commits; rerun the full suite after the
  one-engine/low-thrust EDL and menu dossier tranches merge.

## Rules for the next agents

1. Keep each implementation commit narrow and independently revertible.
2. Never commit `scripts/_*Shot.cs`, a temporary autoload, or a modified `project.godot`
   harness. The visual runner must restore the project before the work is considered done.
3. A visual gate needs telemetry and framebuffer evidence. Unit tests alone cannot close EDL.
4. If a physical gate fails, append the run-id, last telemetry, and a proposed invariant to
   this log before changing the controller.
