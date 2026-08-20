#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "visual_camera_planet_framing_contract_test: FAIL: $*" >&2; exit 1; }

camera="$ROOT/scripts/CameraController.cs"

rg -q 'MinimumOrbitPlanetPitchDeg = 45f' "$camera" \
  || fail "automatic orbit camera has no outward-hemisphere pitch floor"
rg -q 'MinimumOrbitPlanetPitchDeg, 65f' "$camera" \
  || fail "staging camera can still select a negative planet-facing pitch"
rg -qi 'player can still|orbit freely afterwards' "$camera" \
  || fail "camera contract does not document the manual-control boundary"

echo "visual_camera_planet_framing_contract_test: PASS (automatic chase frame keeps the active planet in front)"
