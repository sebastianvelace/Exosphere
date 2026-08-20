#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EDL="$ROOT/scripts/EDLController.cs"
CAMERA="$ROOT/scripts/CameraController.cs"

fail() {
  echo "edl_visual_presentation_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$EDL" ]] || fail "EDLController.cs missing"
[[ -f "$CAMERA" ]] || fail "CameraController.cs missing"

# The camera may consume presentation state, but EDL must not expose a guidance/contact
# command through this path.
rg -q 'public bool IsPresentationActive => _phase != Edl\.Inactive' "$EDL" \
  || fail "EDL presentation state is not explicitly bounded to the visual phase"
rg -q 'public bool IsCatchPresentation => _phase is Edl\.Catch or Edl\.Caught' "$EDL" \
  || fail "catch presentation state is missing"
rg -q 'EdlPresentationDistance = 28f' "$CAMERA" \
  || fail "EDL exterior frame distance is not bounded"
rg -q 'EDLController\.Instance\?\.IsPresentationActive == true' "$CAMERA" \
  || fail "camera does not gate the closer frame on EDL presentation"
rg -q 'requestedDistance = Mathf\.Min\(requestedDistance, EdlPresentationDistance\)' "$CAMERA" \
  || fail "EDL frame does not preserve a minimum-distance bound"
rg -q 'CameraFrameBlend\(delta\)' "$CAMERA" \
  || fail "EDL frame transition bypasses the existing smoothing path"

# At 1920x1080 the reference HUD scale must not grow to 1.4x and cover the vehicle.
rg -q 'viewport\.X / 1280f, viewport\.Y / 720f\), 0\.85f, 1\.00f' "$EDL" \
  || fail "large viewport EDL HUD scale is not capped at the reference composition"

echo "edl_visual_presentation_contract_test: PASS (bounded EDL framing, smooth transition, reference HUD scale)"
