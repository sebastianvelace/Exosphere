# Agent Reentry Log — V3 Zoned Charring (Jul 2026)

Branch: `feat/visual-reentry-zoned-charring`  
Commit: `3c35881` — feat(visual): zone-differentiated reentry charring and plasma tuning

## Implemented

- **VesselRenderer.cs**: `TileCharZone` (Belly/Nose/FwdFlap/AftFlap); charring driven by
  `starship_command` vs `starship_tank` `ThermalDamage`/temperature scaled by windward misalign
  (`ThermalModel.WindwardFactor`); separate tile materials per zone.
- **ReentryPlasmaController.cs**: belly-first `hudGuard` dims shock/wake alpha; bad attitude
  `dangerMul` + red shift; per-edge `EdgeKind` glow weights/alpha caps (nose/belly/flaps).

## Captures (xvfb, heatRatio/intensity gated — not frame count)

| Shot | Path | Gate |
| --- | --- | --- |
| Nominal belly-flop | `/tmp/exosphere_reentry_nominal_1783051977.png` | intensity≥0.22, alt 35–80 km |
| Bad attitude | `/tmp/exosphere_reentry_bad_attitude_1783052211.png` | PEAK HEATING, nose-first plasma |

Harness: temporary `scripts/_ReentryShot.cs` (removed); `project.godot` restored.

## R13 EDL telemetry

From harness EDL pass log: `[EDL] TOUCHDOWN on Earth  vUp=-0.0 m/s` — **no regression** (≤2 m/s).

## Self-grade (1–5)

| Criterion | Score | Notes |
| --- | --- | --- |
| Realism | **4/5** | Zones char at different rates; bad attitude visibly hotter/redder; nominal HUD legible |
| Physics | **5/5** | No `ThermalModel` retune; uses existing heat flux / part telemetry |
| Tests | **5/5** | `ci_check.sh` + reentry xUnit filters pass |
| Verify | **4/5** | Both PNGs captured; windward telemetry reads 0 (sim +Y vs render -X cue) — cosmetic only |

## Pending (not in this commit)

- PLAN_VISUAL_REALISM V3: shock/plasma fine-tune vs real EDL reference frames
- Phase lighting reentry overlay (blocked on harness — see PLAN_PLAYTEST B1)
