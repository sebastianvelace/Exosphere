#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

bridge="scripts/SimulationBridge.cs"
import="assets/textures/saturn_ring.png.import"
shader="assets/shaders/saturn_ring.gdshader"
harness="tools/visual_playtest.sh"

if rg -n '^\s*(var img = Image\.LoadFromFile|img\.GenerateMipmaps|.*ImageTexture\.CreateFromImage)' "$bridge"; then
  echo "FAIL Saturn ring still performs CPU image staging" >&2
  exit 1
fi
if ! rg -n 'GD\.Load<Texture2D>\("res://assets/textures/saturn_ring\.png"\)' "$bridge" >/dev/null; then
  echo "FAIL Saturn ring does not use the imported Texture2D resource" >&2
  exit 1
fi
if ! rg -n '^mipmaps/generate=true$' "$import" >/dev/null; then
  echo "FAIL Saturn ring import does not provide mipmaps for the shader sampler" >&2
  exit 1
fi
if ! rg -n 'filter_linear_mipmap' "$shader" >/dev/null; then
  echo "FAIL Saturn ring shader no longer declares mipmap filtering" >&2
  exit 1
fi
if ! rg -n -- '--saturn\)' "$harness" >/dev/null || ! rg -n '_mode == "saturn"' "$harness" >/dev/null; then
  echo "FAIL Saturn visual acceptance mode is missing" >&2
  exit 1
fi
if ! rg -n 'SetExternalChaseFrame\(0f, 70f, 38f\)' "$harness" >/dev/null; then
  echo "FAIL Saturn visual acceptance does not frame the dominant body" >&2
  exit 1
fi

echo "saturn_ring_contract_test: PASS imported_texture=mipmap shader_filter=linear_mipmap visual_mode=saturn"
