#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COCKPIT="$ROOT/scripts/CockpitInstruments.cs"
RENDERER="$ROOT/scripts/VesselRenderer.cs"
CONSTRUCTION="$ROOT/scripts/ConstructionController.cs"
CAMERA="$ROOT/scripts/CameraController.cs"
PHASE_LIGHTING="$ROOT/scripts/PhaseLightingController.cs"
SYSTEMS_HUD="$ROOT/scripts/SystemsHUD.cs"
ATTITUDE_STRIP="$ROOT/scripts/AttitudeDataStrip.cs"
NAVBALL="$ROOT/scripts/AttitudeNavball.cs"
HUD_CONTROLLER="$ROOT/scripts/HUDController.cs"

fail() {
  echo "render_cadence_phase23_contract_test: FAIL: $*" >&2
  exit 1
}

require_text() {
  local file="$1" pattern="$2" description="$3"
  rg -q --fixed-strings "$pattern" "$file" || fail "$description"
}

for file in "$COCKPIT" "$RENDERER" "$CONSTRUCTION" "$CAMERA" "$PHASE_LIGHTING" \
  "$SYSTEMS_HUD" "$ATTITUDE_STRIP" "$NAVBALL" "$HUD_CONTROLLER"; do
  [[ -f "$file" ]] || fail "missing $file"
done

# Cockpit presentation is paused outside IVA and refreshed by bounded one-shot work.
require_text "$COCKPIT" 'RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,' \
  "cockpit viewports must start disabled"
require_text "$COCKPIT" 'SubViewport.UpdateMode.Once' \
  "cockpit refresh must remain one-shot"
require_text "$COCKPIT" 'CockpitRefreshHz = 30.0' \
  "cockpit refresh rate must remain explicit"
require_text "$COCKPIT" 'if (_cockpitRenderingActive)' \
  "cockpit redraw must remain gated by cockpit visibility"

# Exterior presentation must not process while hidden and must skip thermal physics
# queries for renderer families with no thermal presentation materials.
require_text "$RENDERER" 'if (!Visible || TargetVessel == null) return;' \
  "hidden exterior renderer gate missing"
require_text "$RENDERER" 'EngineVisualPeriodSeconds = 1.0 / 30.0' \
  "engine visual cadence guard missing"
require_text "$RENDERER" 'ThermalVisualPeriodSeconds = 1.0 / 15.0' \
  "thermal visual cadence guard missing"
require_text "$RENDERER" 'PresentationSamplePeriodSeconds = 1.0 / 20.0' \
  "renderer physics sample cadence missing"
require_text "$RENDERER" 'RefreshPresentationSample();' \
  "renderer presentation sample refresh missing"
require_text "$RENDERER" '_cachedPresentationPressureRatio' \
  "renderer cached atmospheric pressure missing"
require_text "$RENDERER" 'private void RefreshPresentationSample()' \
  "renderer presentation sample helper missing"
require_text "$RENDERER" 'if (_shipSteelMats.Count == 0 && _tileZoneMats.Count == 0) return;' \
  "non-Starship thermal query skip missing"

# VAB policy is audited here without changing its controller in this scoped task.
require_text "$CONSTRUCTION" 'RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,' \
  "VAB empty state must start disabled"
require_text "$CONSTRUCTION" 'SetPreviewRenderingActive(active: false);' \
  "VAB empty state gate missing"
require_text "$CONSTRUCTION" 'SubViewport.UpdateMode.Always' \
  "VAB populated state must restore rendering"
require_text "$CONSTRUCTION" 'ProcessModeEnum.Disabled' \
  "VAB renderer must stop processing when empty"

# The runtime renderer is created by SimulationBridge as ActiveVesselRenderer. The
# camera must hide that node in IVA, while retaining the old name only for temporary
# visual harness compatibility; repeated tree walks must not remain in the visibility
# setter on every frame.
require_text "$CAMERA" 'root.FindChild("ActiveVesselRenderer", true, false)' \
  "cockpit visibility must resolve the production exterior renderer name"
require_text "$CAMERA" 'root.FindChild("StarshipRenderer", true, false)' \
  "cockpit visibility must retain the visual harness fallback name"
require_text "$CAMERA" 'private Node3D? _exteriorRenderer;' \
  "exterior renderer reference must be cached"
require_text "$CAMERA" 'ResolvePresentationNodes();' \
  "camera presentation nodes must use the lazy cache resolver"
require_text "$CAMERA" 'PresentationLookupRetrySeconds = 0.25' \
  "camera node lookup retry must be bounded"
require_text "$CAMERA" '_presentationLookupCooldown' \
  "camera node lookup must retain retry state"
require_text "$CAMERA" 'ResolvePresentationNodes(double delta = 0.0)' \
  "camera resolver must receive frame delta for bounded retries"
require_text "$CAMERA" 'if (!needsLookup) return;' \
  "camera resolver must return without a tree walk when nodes are cached"

# Direct solar transmittance is an optical presentation sample, not a physics tick. It
# must remain bounded and must invalidate on body/horizon/geometry changes so eclipse and
# SOI transitions cannot display a stale planet's light.
require_text "$PHASE_LIGHTING" 'DirectTransmittanceCadenceSeconds = 0.10' \
  "phase lighting direct-transmittance cadence missing"
require_text "$PHASE_LIGHTING" '_cachedDirectTransmittance' \
  "phase lighting transmittance cache missing"
require_text "$PHASE_LIGHTING" 'GetCachedDirectTransmittance(' \
  "phase lighting cache helper missing"
require_text "$PHASE_LIGHTING" 'horizonChanged' \
  "phase lighting horizon invalidation missing"
require_text "$PHASE_LIGHTING" 'DirectAltitudeRefreshMeters' \
  "phase lighting altitude invalidation missing"

# Secondary HUD panels and the attitude cluster are presentation-only. They retain
# immediate redraw on first visibility, then run at bounded rates instead of forcing
# a CanvasItem redraw on every render frame.
require_text "$SYSTEMS_HUD" 'RefreshPeriodSeconds = 0.10' \
  "systems HUD refresh cadence missing"
require_text "$SYSTEMS_HUD" '_refreshAccumulator' \
  "systems HUD cadence accumulator missing"
require_text "$SYSTEMS_HUD" 'if (_refreshAccumulator < RefreshPeriodSeconds) return;' \
  "systems HUD redraw is not cadence-gated"
require_text "$ATTITUDE_STRIP" 'RefreshPeriodSeconds = 1.0 / 30.0' \
  "attitude strip refresh cadence missing"
require_text "$ATTITUDE_STRIP" '_hasPendingSnapshot' \
  "attitude strip pending snapshot gate missing"
require_text "$NAVBALL" 'RefreshPeriodSeconds = 1.0 / 30.0' \
  "navball refresh cadence missing"
require_text "$NAVBALL" 'double refreshDelta = _refreshAccumulator;' \
  "navball smoothing must consume accumulated presentation time"
require_text "$HUD_CONTROLLER" 'PresentationRefreshPeriodSeconds = 1.0 / 30.0' \
  "main HUD presentation cadence missing"
require_text "$HUD_CONTROLLER" 'presentationBoundaryChanged' \
  "main HUD boundary invalidation missing"
require_text "$HUD_CONTROLLER" 'snapshot.TimeToPeriapsisS' \
  "main HUD must consume presenter orbital timing"
if rg -q --fixed-strings 'OrbitalElements.FromStateVector(' "$HUD_CONTROLLER"; then
  fail "main HUD still recomputes orbital elements outside the presenter"
fi
require_text "$HUD_CONTROLLER" '_lastRenderedNavigationMode' \
  "navigation label cache missing"
require_text "$HUD_CONTROLLER" '_lastAppliedViewMode' \
  "view mode presentation cache missing"
require_text "$HUD_CONTROLLER" '_lastPhaseTrackPhase' \
  "phase track cache missing"
require_text "$HUD_CONTROLLER" 'UpdateDensityToast(delta);' \
  "main HUD toast timer must remain wall-clock driven"
require_text "$HUD_CONTROLLER" 'if (!presentationBoundaryChanged' \
  "main HUD capture path is not cadence-gated"
input_line="$(rg -n --fixed-strings 'if (Input.IsKeyPressed(Key.W))' "$HUD_CONTROLLER" | head -1 | cut -d: -f1)"
toast_line="$(rg -n --fixed-strings 'UpdateDensityToast(delta);' "$HUD_CONTROLLER" | head -1 | cut -d: -f1)"
gate_line="$(rg -n --fixed-strings 'if (!presentationBoundaryChanged' "$HUD_CONTROLLER" | head -1 | cut -d: -f1)"
body_line="$(rg -n --fixed-strings 'var refBody = universe.GetDominantBody(vessel.Position);' "$HUD_CONTROLLER" | head -1 | cut -d: -f1)"
(( input_line < gate_line )) || fail "HUD input is behind presentation cadence gate"
(( toast_line < gate_line )) || fail "HUD toast timer is behind presentation cadence gate"
(( body_line > gate_line )) || fail "HUD dominant-body lookup is outside presentation cadence gate"

echo "render_cadence_phase23_contract_test: PASS (cockpit=30Hz, exterior sample=20Hz, thermal gated, HUD cluster bounded, VAB=demand-driven)"
