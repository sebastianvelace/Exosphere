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
has 'vec3 albedoColor' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "mix-blended plume has no non-black Compatibility source colour"
has 'float sheathAlpha' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "plume shader does not bound sheath density separately from the core"
has 'float coreAlpha' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "plume shader does not bound core density separately from the sheath"
has '0.0, 0.62' "$ROOT/assets/shaders/raptor_plume.gdshader" \
  "plume alpha is not capped below the former solid-cone limit"
has 'Mathf.Lerp(0.90f, 0.48f, expansion)' "$PLUME" \
  "near-field core opacity does not thin toward vacuum"
has 'camera.GlobalPosition.Length()' "$PLUME" \
  "plume far-field LOD is not measured from the viewing camera to the vessel origin"
has 'PresentationCamera' "$PLUME" \
  "plume far-field LOD does not prefer the production chase/pad camera"

if rg -q 'ALBEDO[[:space:]]*=[[:space:]]*vec3\(0\.0\)' \
    "$ROOT/assets/shaders/raptor_plume.gdshader"; then
  fail "mix-blended plume still composites a black source colour"
fi

if rg -q 'GD\.Randf\(\)' "$PLUME"; then
  fail "plume motion still uses frame-rate-dependent random flicker"
fi

echo "visual_plume_delivery_contract_test: PASS (layered Compatibility colour, bounded alpha, orbital thinning)"
