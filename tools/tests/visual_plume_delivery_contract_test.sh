#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PLUME="$ROOT/scripts/PlumeSystem.cs"
RENDERER="$ROOT/scripts/VesselRenderer.cs"

fail() {
  echo "visual_plume_delivery_contract_test: FAIL: $*" >&2
  exit 1
}

has() { rg -qF "$1" "$2" || fail "$3"; }

has 'public void Update(float superHeavyThrottle, float shipThrottle' "$PLUME" \
  "plume system has no dual-stage delivery API"
has 'shipThrottle > 0.01f' "$PLUME" \
  "ship plume is still gated by Super Heavy presence"
has 'BuildEngineVisualGroups(vessel)' "$RENDERER" \
  "renderer does not classify engine rows by vehicle role"
has 'ComputeDeliveredPlumeThrottles' "$RENDERER" \
  "renderer has no per-stage delivered-throttle reduction"
has 'VISUAL_PLUME overlap=' "$RENDERER" \
  "renderer has no hot-stage plume telemetry"
has 'body.Atmosphere.GetPressure(0.0)' "$RENDERER" \
  "plume pressure ratio is not normalized by the active body"
has 'layer_opacity' "$PLUME" \
  "plume layers do not expose bounded optical density"
has 'CoreMat' "$PLUME" \
  "plume has no separate axial core layer"
has 'float coreTailRadius = sh' "$PLUME" \
  "plume core geometry still reuses the broad outer sheath"
has 'Mesh             = coreMesh' "$PLUME" \
  "plume core geometry is not bound to its narrow mesh"
has 'layer_opacity' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "plume shader cannot distinguish core and outer sheath opacity"
has 'vacuumCoreAlpha' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "vacuum plume has no readable axial emission floor"

if rg -q 'GD\.Randf\(\)' "$PLUME"; then
  fail "plume motion still uses frame-rate-dependent random flicker"
fi

echo "visual_plume_delivery_contract_test: PASS (dual-stage delivery, per-body pressure, smooth modulation)"
