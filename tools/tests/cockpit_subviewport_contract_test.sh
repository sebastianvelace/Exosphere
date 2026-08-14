#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COCKPIT="$ROOT/scripts/CockpitInstruments.cs"
CONSTRUCTION="$ROOT/scripts/ConstructionController.cs"

fail() {
  echo "cockpit_subviewport_contract_test: FAIL: $1" >&2
  exit 1
}

require_text() {
  local file="$1" pattern="$2" description="$3"
  rg -q --fixed-strings "$pattern" "$file" || fail "$description"
}

require_text "$COCKPIT" 'private readonly SubViewport[] _vp   = new SubViewport[3];' \
  "Flight must retain exactly three cockpit viewports"
require_text "$COCKPIT" 'Size = new Vector2I(512, 512),' \
  "Flight cockpit viewport resolution changed unexpectedly"
require_text "$COCKPIT" 'SetViewportUpdateMode(active: false);' \
  "Flight cockpit viewports must start paused"
require_text "$COCKPIT" 'SubViewport.UpdateMode.Disabled' \
  "Flight cockpit must have an explicit disabled update mode"
require_text "$COCKPIT" 'SubViewport.UpdateMode.Once' \
  "Flight cockpit must use bounded one-shot refreshes when active"
require_text "$COCKPIT" 'if (_cockpitRenderingActive)' \
  "Flight cockpit panel redraw must remain gated by active cockpit state"
require_text "$COCKPIT" '_pan[i].QueueRedraw();' \
  "Flight cockpit must retain the three-instrument refresh loop"
require_text "$COCKPIT" 'CockpitRefreshHz = 30.0' \
  "Flight cockpit refresh rate must remain explicitly bounded"

# Construction preview is demand-driven: its empty state must not keep a 1024² target
# and a hidden VesselRenderer processing every frame.
require_text "$CONSTRUCTION" 'Size = new Vector2I(1024, 1024),' \
  "Construction preview baseline is missing"
require_text "$CONSTRUCTION" 'RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,' \
  "Construction preview must start with rendering disabled"
require_text "$CONSTRUCTION" 'SetPreviewRenderingActive(active: false);' \
  "Construction preview empty state must disable rendering"
require_text "$CONSTRUCTION" 'SetPreviewRenderingActive(active: true);' \
  "Construction preview populated state must re-enable rendering"
require_text "$CONSTRUCTION" 'SubViewport.UpdateMode.Always' \
  "Construction preview must restore Always mode when populated"
require_text "$CONSTRUCTION" 'ProcessModeEnum.Disabled' \
  "Construction preview renderer must stop processing when empty"
require_text "$CONSTRUCTION" 'ProcessModeEnum.Inherit' \
  "Construction preview renderer must resume processing when populated"

echo "cockpit_subviewport_contract_test: PASS (Flight=3x512 paused outside cockpit, 30Hz once-refresh; Construction=1x1024 demand-driven)"
