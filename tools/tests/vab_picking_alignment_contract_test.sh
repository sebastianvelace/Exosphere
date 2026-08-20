#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "vab_picking_alignment_contract_test: FAIL: $*" >&2; exit 1; }

layer="$ROOT/scripts/VabPickingLayer.cs"
controller="$ROOT/scripts/ConstructionController.cs"

rg -q 'private const float MetresPerUnit = 2\.8f' "$layer" \
  || fail "picking layer has no explicit render-scale conversion"
rg -q 'BuildProceduralStarshipMap' "$layer" \
  || fail "procedural Starship has no renderer-aligned anchor map"
rg -q 'render\[pair\.Key\] = pair\.Value / MetresPerUnit \+ Vector3\.Up \* datumShift' "$layer" \
  || fail "generic preview does not mirror the renderer datum shift"
rg -q 'ToRenderUnits\(node\.Position\)' "$layer" \
  || fail "attachment markers still use raw metre coordinates"
rg -q 'def\.DiameterM / MetresPerUnit' "$layer" \
  || fail "generic picking radius is not in preview units"
rg -q 'viewport\.World3D = new World3D\(\)' "$controller" \
  || fail "VAB preview does not provision a physics world for raycasts"
rg -q 'Preview physics is not ready yet' "$controller" \
  || fail "VAB raycast has no fail-safe guard for an unavailable world"

echo "vab_picking_alignment_contract_test: PASS (generic scale/datum and Starship procedural anchors)"
