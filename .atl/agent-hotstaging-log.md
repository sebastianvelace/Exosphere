# Hot-Staging Real Ascent Validation — Jul 2026

**Branch:** `feat/visual-hotstaging-ascent-capture`  
**Harness:** temporary `scripts/_HotstageShot.cs` (deleted; not committed)  
**Capture:** xvfb Flight.tscn + `[G]` AscentController, gated on `MissionPhase.SEPARATION`

## Baseline

- `bash tools/ci_check.sh` — green (build 0/0, 59 tests pass, Godot smoke OK)

## Capture run

```
engage frame=90
MAX_Q frame=413 alt=8029m q=33.0kPa spd=355m/s
SEPARATION frame=969 alt=60996m q=0.6kPa spd=2145m/s throttle=1.00
capture 00..13 @ ~61–65 km, ASCENT_SHIP phase
```

**PNG paths:** `/tmp/exosphere_hotstage_ascent_00.png` … `_13.png`  
**Log:** `/tmp/exosphere_hotstage_ascent.log`

## Before (prior session)

- Hot-staging VFX validated with **forced** `TriggerStaging` harness only (`/tmp/exosphere_hotstage_after_00..11.png`).
- Flash/lume/soot burst worked locally but **not** verified inside real `[G]` ascent arc.

## After (this session)

Real ascent multiframe capture at SEPARATION:

| Frame | Read |
| --- | --- |
| `_00` | Two stages visible; bright yellow-orange ring flash at interface; Ship engines lit; HUD shows `SEPARATION` / `STAGE SEP`. |
| `_03` | Booster receding; persistent white-orange shock ring; Ship exhaust impinging booster top; plume column between stages. |
| `_07` | Well separated; translucent brown-orange Starship plume; dark scorched hot-stage ring on booster aft; event log `SHIP IGNITION` + `STAGE SEP`. |

**Reference semantics (IFT T+2:39/T+2:40):** A static frame reads as “Ship ignites before/at separation, flash between stages, booster shows scorched ring.” **Met.**

## Telemetry sanity ([G] no-regression)

| Metric | Target | Observed | OK? |
| --- | --- | --- | --- |
| Max-Q q | ~30 kPa | 33.0 kPa @ 8 km | ✓ |
| MECO/staging alt | ~65 km | ~61 km | ~ (slightly low) |
| Staging speed | ~2.3 km/s | 2.15 km/s | ~ (slightly low) |
| Throttle at sep | Ship relight | 1.00, 6/6 engines | ✓ |

Ascent profile unchanged; no sim or controller edits this session.

## Visual tuning

**None applied.** `HotStageFlashController` / `PlumeSystem` left as-is — capture comparison did not show clear gaps requiring code changes.

## Self-grade (rubric)

| Axis | Score | Notes |
| --- | --- | --- |
| realism | **4/5** | Flash/plume/ring readable in chase cam; side-by-side IFT frame compare not done this session |
| tests | **5/5** | ci_check green; no new xUnit (sim untouched) |
| no-regression | **4/5** | Max-Q on target; staging alt/speed ~5% below nominal MECO refs |
| verify | **5/5** | 14 xvfb PNGs captured; frames 00/03/07 reviewed with Read tool |

**Overall:** pass (all axes ≥4). No iteration pass required.

## Remaining gaps (next session)

1. Side-by-side still vs IFT webcast frame (T+2:39) for flash intensity/timing fine-tune.
2. Optional chase-camera preset closer to separation ring for hero frame.
3. Confirm booster debris renderer keeps scorched ring visible longer as it tumbles away.
4. MECO altitude/speed telemetry vs real IFT — sim/AscentController tuning if product wants tighter ~65 km / 2.3 km/s match (out of visual scope).

## Harness cleanup

- [x] `scripts/_HotstageShot.cs` deleted
- [x] `project.godot` restored (no autoload)
- [x] `git status` clean of harness artifacts
