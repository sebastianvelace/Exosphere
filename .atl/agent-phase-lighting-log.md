# Phase Lighting V2 Agent Log — Reentry + Cockpit

**Branch:** `feat/visual-phase-lighting-reentry`  
**Date:** 2026-07-02

## Baseline

- `bash tools/ci_check.sh` — pass (build 0/0, tests pass, Godot smoke OK).

## Implemented

### `PhaseLightingController.cs` (V2 overlay)

- Keeps V1 altitude blend (70→130 km): ambient 0.45→0.12, sun 1.5→1.95, glow 0→0.6.
- Adds `reentryFactor ∈ [0,1]` from `ThermalModel.ComputeHeatFlux` (thresholds 5e4 / 6e5 W/m², same as `ReentryPlasmaController`).
- Mission-phase floor during atmospheric descent: ENTRY 0.30, PEAK_HEATING 0.50, AERO 0.20, FINAL 0.12 (only when `vUp < -20` and alt < 120 km).
- Reentry overlay lerps: ambient → 0.10, sun → 0.90, glow → 0.80, warm ambient tint `(0.82, 0.42, 0.20)`.
- Pad/orbit path unchanged when `reentryFactor == 0` (no heat, not in descent).

### `CameraController.cs` (cockpit sub-blend)

- `IsCockpitView` + `EnterCockpitView()` for harness/debug.
- During reentry in cockpit: +0.08 ambient cap, −0.18 glow to preserve dash/HUD contrast.

## Xvfb captures (1920×1080, Flight.tscn, synthetic reentry frozen @ timeScale=0)

| Milestone | Path | Telemetry |
|-----------|------|-----------|
| Pad | `/tmp/exosphere_phase_pad_v2.png` | alt=12 m, flux=0, PRE_LAUNCH |
| Orbit | `/tmp/exosphere_phase_orbit_v2.png` | alt=250 km, spd=7567 m/s, ORBIT |
| Reentry (belly-flop synthetic) | `/tmp/exosphere_phase_reentry_v2.png` | alt=42 km, spd=7202 m/s, flux=3.67e6, PEAK_HEATING |
| Reentry cockpit | `/tmp/exosphere_phase_reentry_cockpit_v2.png` | same state, FPV |

Harness: temporary `_PhaseLightingShot.cs` autoload (deleted; `project.godot` restored).

## Self-grade

| Criterion | Score | Notes |
|-----------|-------|-------|
| Pad vs baseline | 9/10 | V1 pad constants untouched; capture shows daylight stack + legible HUD. No separate V1 PNG on disk to diff; mean lum 136.8. |
| Orbit (no regression) | 9/10 | Metallic ship + Earth limb readable; not subexposed (mean lum 51.2). |
| Reentry plasma read | 7/10 | Warm overlay + PEAK_HEATING banner; synthetic freeze shows belly-flop frame. Plasma cap modest at harness attitude — needs live EDL harness for full belly-flop tuning. |
| HUD/cockpit legibility | 8/10 | Cockpit capture: HUD panels readable; windshield bloom present but dash text OK. |
| Scope | 10/10 | Only `PhaseLightingController.cs`, `CameraController.cs`; no VFX/material edits. |

**Overall:** 8.6/10 — shippable V2; live EDL capture still recommended per `PLAN_PLAYTEST.md` B1.

## Files changed (committed)

- `scripts/PhaseLightingController.cs`
- `scripts/CameraController.cs`
- `.atl/agent-phase-lighting-log.md`
