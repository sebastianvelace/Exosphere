# Jul 2026 Agent Branch Integration

> Prepared: 2026-07-03  
> Integration branch: `integrate/jul2026-realism-loop`  
> Base: `main` @ `4906dfd` (up to date with `origin/main`)

## Summary

All six target branches integrated cleanly on `integrate/jul2026-realism-loop`. Final `bash tools/ci_check.sh`: **PASS** (68 tests, 0 warnings, Godot smoke OK).

| # | Branch | Method | Conflicts | CI after step |
|---|--------|--------|-----------|---------------|
| 1 | `refactor/simplify-dead-controllers` | FF merge | None | PASS (59 tests) |
| 2 | `feat/physics-systems-mission-phases` | Merge commit | None | PASS (68 tests) |
| 3 | `feat/visual-hotstaging-ascent-capture` | Cherry-pick `13c9577` | None | (included in final) |
| 4 | `feat/visual-phase-lighting-reentry` | Cherry-pick `751e5bf` | None | (included in final) |
| 5 | `docs/plan-sync-jul2026` | Cherry-pick `fd8c36c` only | None | (included in final) |
| 6 | `docs/plan-next-session-jul2026` | Cherry-pick `96f544a` only | None | (included in final) |

**Excluded (in progress):** `feat/visual-reentry-zoned-charring` — other agent; local `project.godot` had unstaged `_ReentryShot` autoload harness (restored before final CI).

---

## Merge order rationale

1. **Refactor first** — removes `NavBallController`, `TimeWarpController`; consolidates warp into `WarpController`. Downstream branches share `086b3c7`.
2. **Physics systems** — builds on refactor tip; adds R11 mission-phase wiring (+9 tests → 68 total).
3. **Hot-staging docs** — docs-only; no warp duplicate.
4. **Phase lighting** — code change to `PhaseLightingController.cs`; cherry-picked single commit to avoid re-landing refactor commits already on integration.
5. **Plan sync docs** — cherry-pick **`fd8c36c` only**; skipped **`03fea18`** (duplicate warp, same diff as `1e32896` on refactor branch).
6. **Next-session plan** — cherry-pick **`96f544a` only**; branch tip also contained `03fea18` + `fd8c36c` (already integrated).

### Duplicate warp commits (do not merge twice)

| Commit | Branch | Status |
|--------|--------|--------|
| `1e32896` | `refactor/simplify-dead-controllers` | ✅ On integration (via #1) |
| `03fea18` | `docs/plan-sync-jul2026`, `docs/plan-next-session-jul2026` | ⛔ Skipped (identical file diff to `1e32896`) |

---

## Integration branch log

```
b35a820 docs: add next-session improvement plan for realism track
ec3d99d docs: sync visual and realism plans with codebase state
b70be7a feat(visual): phase lighting for reentry and cockpit readability
d42ea6b docs(visual): hot-staging acceptance notes
74add3d merge: feat/physics-systems-mission-phases into integration
5dd50b4 fix(systems): use surface-relative comm delay at Earth
d860c95 test(systems): add phase power and comm delay coverage
3e4f3fd feat(systems): connect power and comms to mission geometry
fe302ac Add Jul 2026 over-engineering audit with evidence and deferrals.
1e32896 Consolidate duplicate time-warp input into WarpController.
086b3c7 Remove dead NavBallController superseded by AttitudeNavball.
```

**Diff vs `main`:** 22 files, +1158 / −289 lines.

---

## CI notes

- **Harness guard:** Uncommitted local edit on `project.godot` (`_ReentryShot` autoload from reentry agent) caused one CI failure; restored with `git checkout project.godot`. Not part of integration commits.
- **Final run:** builds (sim + Godot), 68/68 tests, headless Flight + VAB smoke — exit 0.

---

## Slot: `feat/visual-reentry-zoned-charring`

When the reentry agent finishes:

1. Rebase onto `integrate/jul2026-realism-loop` (or `main` after this integration lands).
2. Confirm no harness files (`scripts/_*Shot.cs`, temp autoload in `project.godot`).
3. Run `bash tools/ci_check.sh`.
4. Cherry-pick or merge as a **separate PR** after phase-lighting (#4) to reduce `PhaseLightingController` / `ReentryPlasmaController` overlap conflicts.

---

## PR preparation

`gh` CLI not available in prep environment; no `.github/PULL_REQUEST_TEMPLATE.md`. Per **branch-pr** skill, each PR needs an approved issue (`Closes #N`) + one `type:*` label — **create/link issues before opening PRs**.

Recommended **stacked merge order into `main`** (one PR per work unit):

### PR 1 — `refactor/simplify-dead-controllers`

**Title:** `refactor(flight): remove dead NavBall and consolidate warp input`

**Label:** `type:refactor`

**Body draft:**

```markdown
Closes #TBD

## Summary
- Remove superseded `NavBallController` (AttitudeNavball owns navball UI).
- Delete duplicate `TimeWarpController`; warp keys live in `WarpController`.
- Add Jul 2026 over-engineering audit (`.atl/OVERENGINEERING_AUDIT_JUL2026.md`).

## Test plan
- [x] `bash tools/ci_check.sh` — 59 tests pass on this branch alone
- [x] Godot headless Flight + VAB smoke
```

**Files:** `NavBallController.cs` (deleted), `TimeWarpController.cs` (deleted), `WarpController.cs`, `Flight.tscn`, `.atl/OVERENGINEERING_AUDIT_JUL2026.md`

---

### PR 2 — `feat/physics-systems-mission-phases`

**Title:** `feat(systems): wire power and comms to mission geometry (R11)`

**Label:** `type:feature`

**Depends on:** PR 1 merged (or branch rebased onto post-PR-1 `main`)

**Body draft:**

```markdown
Closes #TBD

## Summary
- Add `MissionGeometry` and `SystemsMissionPhase` for phase-aware systems.
- Connect `PowerSystem`, `CommsSystem`, `LifeSupportSystem` to mission geometry.
- Surface-relative comm delay at Earth; +9 xUnit tests (68 total).

## Test plan
- [x] `dotnet test ExosphereSimulation.Tests` — 68 passed
- [x] `bash tools/ci_check.sh`
```

**Files:** `ExosphereSimulation/Systems/*`, `SystemsController.cs`, `SystemsMissionPhaseTests.cs`, `.atl/agent-systems-r11-log.md`

---

### PR 3 — `feat/visual-hotstaging-ascent-capture` (docs)

**Title:** `docs(visual): hot-staging acceptance notes`

**Label:** `type:docs`

**Body draft:**

```markdown
Closes #TBD

## Summary
- Document hot-staging visual acceptance criteria and agent log.
- Update `PLAN_VISUAL_REALISM.md` / `ROADMAP.md` for V2 hot-stage track.

## Test plan
- [x] Docs-only; CI guard + builds unchanged
```

**Commit:** `13c9577` (cherry-pick onto current `main` stack)

---

### PR 4 — `feat/visual-phase-lighting-reentry`

**Title:** `feat(visual): phase lighting for reentry and cockpit readability`

**Label:** `type:feature`

**Body draft:**

```markdown
Closes #TBD

## Summary
- Extend `PhaseLightingController` for reentry phase and cockpit readability.
- Minor `CameraController` exposure hook for interior contrast.

## Test plan
- [x] `bash tools/ci_check.sh`
- [ ] Manual reentry flight check (lighting transition DEORBIT→EDL)
```

**Commit:** `751e5bf` only (do not merge full branch — includes refactor commits)

---

### PR 5 — `docs/plan-sync-jul2026`

**Title:** `docs: sync visual and realism plans with codebase state`

**Label:** `type:docs`

**Body draft:**

```markdown
Closes #TBD

## Summary
- Sync `PLAN_REALISM.md`, `PLAN_VISUAL_REALISM.md`, `ROADMAP.md` with verified code state.
- Add delegation matrix (`.atl/DELEGATION_JUL2026.md`).

## Test plan
- [x] Docs-only
```

**Commit:** `fd8c36c` only — **NOT** `03fea18`

---

### PR 6 — `docs/plan-next-session-jul2026`

**Title:** `docs: add next-session improvement plan for realism track`

**Label:** `type:docs`

**Body draft:**

```markdown
Closes #TBD

## Summary
- Add `PLAN_NEXT_SESSION.md` with prioritized realism/visual follow-ups.

## Test plan
- [x] Docs-only
```

**Commit:** `96f544a` only

---

## Alternative: single integration PR

If the team prefers one review:

**Title:** `chore: integrate Jul 2026 realism loop (refactor + R11 + visual docs/lighting)`

**Branch:** `integrate/jul2026-realism-loop` → `main`

**Label:** `type:chore` (or split by policy — may need multiple linked issues)

**Body:** Reference this file; list all six work units; note reentry branch slot.

---

## Blockers / follow-ups

| Item | Status |
|------|--------|
| `gh` CLI for PR creation | Not installed locally — push branch + open PRs manually |
| Approved GitHub issues | TBD — required before PR merge per branch-pr skill |
| `feat/visual-reentry-zoned-charring` | In progress — integrate after this stack |
| Source branches 3–6 | Tips contain duplicate commits; use cherry-pick strategy above for individual PRs |
| Push integration branch | Run: `git push -u origin integrate/jul2026-realism-loop` |

---

## Commands used

```bash
git fetch --all --prune
git checkout main && git pull --ff-only
git checkout -B integrate/jul2026-realism-loop main
git merge refactor/simplify-dead-controllers          # FF
bash tools/ci_check.sh                                 # PASS 59
git merge feat/physics-systems-mission-phases         # merge commit
bash tools/ci_check.sh                                 # PASS 68
git cherry-pick 13c9577 751e5bf fd8c36c 96f544a
git checkout project.godot                             # drop local reentry harness
bash tools/ci_check.sh                                 # PASS 68
```
