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
require_text "$COCKPIT" 'SubViewport.UpdateMode.Always' \
  "Flight cockpit must restore Always mode when active"
require_text "$COCKPIT" 'if (_cockpitRenderingActive)' \
  "Flight cockpit panel redraw must remain gated by active cockpit state"
require_text "$COCKPIT" 'for (int i = 0; i < 3; i++) _pan[i].QueueRedraw();' \
  "Flight cockpit must redraw all three instruments when active"

# Construction is intentionally audit-only in this phase.  This contract ensures its
# viewport remains untouched and documents the measured baseline for the phase report.
require_text "$CONSTRUCTION" 'Size = new Vector2I(1024, 1024),' \
  "Construction preview baseline is missing"
require_text "$CONSTRUCTION" 'RenderTargetUpdateMode = SubViewport.UpdateMode.Always,' \
  "Construction preview update mode changed outside this phase ownership"

echo "cockpit_subviewport_contract_test: PASS (Flight=3x512 paused outside cockpit; Construction=1x1024 audit-only)"
