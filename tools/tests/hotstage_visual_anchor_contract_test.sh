#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HOTSTAGE="$ROOT/scripts/HotStageFlashController.cs"
HARNESS="$ROOT/tools/visual_playtest.sh"

rg -Fq 'public const float HotStageInterfaceRenderY = 71f / 2.8f' "$HOTSTAGE" \
  || { echo "FAIL hot-stage interface anchor is not tied to Flight-7 render scale" >&2; exit 1; }
rg -Fq 'HotStageInterfaceRenderY - 0.3f' "$HOTSTAGE" \
  || { echo "FAIL gas is not anchored at the separation plane" >&2; exit 1; }
if rg -q 'TorusMesh|CylinderMesh|QuadMesh|EmissionEnergyMultiplier' "$HOTSTAGE"; then
  echo "FAIL solid emissive ring/card returned to hot-stage gas" >&2; exit 1
fi
rg -Fq '_frameName = "SHDebris_" + detachedVesselId[..8]' "$HOTSTAGE" \
  || { echo "FAIL residual gas follows rebased Ship instead of booster" >&2; exit 1; }
rg -Fq 'if (!_overlapBurstStarted) StartBurst();' "$HOTSTAGE" \
  || { echo "FAIL separation restarts the overlap burst" >&2; exit 1; }
rg -Fq 'blend_mix, depth_draw_never, cull_back' "$ROOT/assets/shaders/hotstage_gas.gdshader" \
  || { echo "FAIL gas transparency/depth contract changed" >&2; exit 1; }
rg -Fq 'ALPHA = opacity * soft_edge * wisps' "$ROOT/assets/shaders/hotstage_gas.gdshader" \
  || { echo "FAIL gas lost soft silhouette" >&2; exit 1; }
rg -Fq 'private void SyncToVesselFrame()' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX has no floating-origin frame synchronization" >&2; exit 1; }
rg -Fq 'Position = _vesselFrame!.Position' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not follow vessel render position" >&2; exit 1; }
rg -Fq 'Quaternion = _vesselFrame.Quaternion' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not follow vessel attitude" >&2; exit 1; }
rg -Fq 'IsHotStageOverlapping == true' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not observe the live overlap transition" >&2; exit 1; }
rg -Fq '_overlapBurstStarted = true' "$HOTSTAGE" \
  || { echo "FAIL hot-stage overlap transition does not start the burst" >&2; exit 1; }

bash -n "$HARNESS"
rg -Fq 'QueueCapture("hotstage_separation")' "$HARNESS" \
  || { echo "FAIL harness has no post-separation hot-stage capture" >&2; exit 1; }
rg -Fq 'hotStageOverlap={vessel.IsHotStageOverlapping}' "$HARNESS" \
  || { echo "FAIL capture telemetry does not expose physical overlap state" >&2; exit 1; }
rg -Fq 'VISUAL_HOTSTAGE slug=' "$HARNESS" \
  || { echo "FAIL harness has no hot-stage spatial telemetry" >&2; exit 1; }
rg -q 'interfaceY=25\\.36' "$HARNESS" \
  || { echo "FAIL verification does not gate the interstage anchor telemetry" >&2; exit 1; }

echo "hotstage_visual_anchor_contract_test: PASS (interstage anchor, frame sync and separation evidence)"
