#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HOTSTAGE="$ROOT/scripts/HotStageFlashController.cs"
HARNESS="$ROOT/tools/visual_playtest.sh"

grep -q 'public const float HotStageInterfaceRenderY = 71f / 2.8f' "$HOTSTAGE" \
  || { echo "FAIL hot-stage interface anchor is not tied to Flight-7 render scale" >&2; exit 1; }
grep -q 'Position = new Vector3(0f, HotStageInterfaceRenderY - 0.55f, 0f)' "$HOTSTAGE" \
  || { echo "FAIL hot-stage plume is not anchored at the separation plane" >&2; exit 1; }
grep -q 'Position = new Vector3(0f, HotStageInterfaceRenderY - 0.25f, 0f)' "$HOTSTAGE" \
  || { echo "FAIL hot-stage shock ring is not anchored at the separation plane" >&2; exit 1; }
grep -q 'private void SyncToVesselFrame()' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX has no floating-origin frame synchronization" >&2; exit 1; }
grep -q 'Position = _vesselFrame.Position' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not follow vessel render position" >&2; exit 1; }
grep -q 'Quaternion = _vesselFrame.Quaternion' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not follow vessel attitude" >&2; exit 1; }
grep -q 'IsHotStageOverlapping == true' "$HOTSTAGE" \
  || { echo "FAIL hot-stage VFX does not observe the live overlap transition" >&2; exit 1; }
grep -q '_overlapBurstStarted = true' "$HOTSTAGE" \
  || { echo "FAIL hot-stage overlap transition does not start the burst" >&2; exit 1; }

bash -n "$HARNESS"
grep -q 'QueueCapture("hotstage_separation")' "$HARNESS" \
  || { echo "FAIL harness has no post-separation hot-stage capture" >&2; exit 1; }
grep -q 'hotStageOverlap={vessel.IsHotStageOverlapping}' "$HARNESS" \
  || { echo "FAIL capture telemetry does not expose physical overlap state" >&2; exit 1; }
grep -q 'VISUAL_HOTSTAGE slug=' "$HARNESS" \
  || { echo "FAIL harness has no hot-stage spatial telemetry" >&2; exit 1; }
grep -q 'interfaceY=25\\.36' "$HARNESS" \
  || { echo "FAIL verification does not gate the interstage anchor telemetry" >&2; exit 1; }

echo "hotstage_visual_anchor_contract_test: PASS (interstage anchor, frame sync and separation evidence)"
