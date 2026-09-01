#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "visual_camera_transition_contract_test: FAIL: $*" >&2; exit 1; }

camera="$ROOT/scripts/CameraController.cs"

rg -q 'CameraFrameTransitionSeconds = 0\.14f' "$camera" \
  || fail "camera transition has no bounded smoothing interval"
rg -q 'CameraFrameBlend\(delta\)' "$camera" \
  || fail "rendered camera frame does not use the transition blend"
rg -q '_smoothedFramePosition\.Lerp\(targetCamPos, blend\)' "$camera" \
  || fail "camera position is not eased across event changes"
rg -q '_smoothedFrameTarget\.Lerp\(targetLookTarget, blend\)' "$camera" \
  || fail "camera look target is not eased across event changes"
rg -q '_presentationDistanceTarget = 38f' "$camera" \
  || fail "staging distance is not represented as a presentation target"
if rg -q '_distance = 38f' "$camera"; then
  fail "staging still overwrites the manual distance directly"
fi
rg -q '_presentationDistanceTarget = null' "$camera" \
  || fail "manual camera input cannot clear the presentation distance target"
rg -q 'camera\.Position = surfaceFrame \* _smoothedFramePosition' "$camera" \
  || fail "camera left the floating-origin surface frame"
rg -q 'Vector3 lookTarget = surfaceFrame \* _smoothedFrameTarget' "$camera" \
  || fail "look target left the floating-origin surface frame"

if rg -q 'camera\.Position = .*_distance' "$camera"; then
  fail "event transition still assigns the camera transform directly from distance"
fi
if rg -q 'camera\.Position = surfaceFrame \* camPos' "$camera"; then
  fail "camera uses the unsmoothed frame position"
fi

rg -q '_yaw\s*-= mm\.Relative\.X \* OrbitSensitivity' "$camera" \
  || fail "manual exterior orbit control was removed"
rg -q '_distance = Mathf\.Clamp\(_distance \* ZoomSensitivity' "$camera" \
  || fail "manual exterior zoom control was removed"
rg -q 'surfaceFrame = BuildSurfaceFrame' "$camera" \
  || fail "floating-origin surface frame lookup was removed"

echo "visual_camera_transition_contract_test: PASS (Pad/Chase/staging frames are eased in floating-origin space)"
