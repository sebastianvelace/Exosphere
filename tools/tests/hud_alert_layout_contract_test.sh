#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HUD="$ROOT_DIR/scripts/HUDController.cs"

fail() {
  echo "hud_alert_layout_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$HUD" ]] || fail "missing HUDController.cs"

# Phase title and alert lane must have independent layout ownership.
rg -q 'Name = "PhaseTitle"' "$HUD" \
  || fail "phase title container missing"
rg -q 'Name = "PhaseTitleLabel"' "$HUD" \
  || fail "phase title label missing"
rg -q 'private PanelContainer _alertRoot' "$HUD" \
  || fail "dedicated alert lane missing"
rg -q 'Name = "AlertLane"' "$HUD" \
  || fail "alert lane name missing"
rg -q 'Name = "AlertRows"' "$HUD" \
  || fail "alert rows container missing"

# The lane is exactly two one-line rows: summary and detail/action.
[[ "$(rg -c 'MaxLinesVisible = 1' "$HUD")" -eq 2 ]] \
  || fail "alert rows must each cap visible lines at one"
[[ "$(rg -c 'AutowrapMode = TextServer\.AutowrapMode\.WordSmart' "$HUD")" -ge 5 ]] \
  || fail "word wrapping must remain configured for banner and alert text"
[[ "$(rg -c 'TextOverrunBehavior = TextServer\.OverrunBehavior\.TrimWordEllipsis' "$HUD")" -eq 2 ]] \
  || fail "alert rows must trim by word with ellipsis"
rg -q 'Name = "AlertSummary"' "$HUD" \
  || fail "alert summary row missing"
rg -q 'Name = "AlertDetail"' "$HUD" \
  || fail "alert detail row missing"

# Preserve every flight-data field from FlightAlertSnapshot in the alert lane.
rg -q 'alert\.Severity\.ToString\(\)\.ToUpperInvariant\(\)' "$HUD" \
  || fail "alert severity missing"
rg -q 'alert\.Title' "$HUD" || fail "alert title missing"
rg -q 'alert\.Value' "$HUD" || fail "alert value missing"
rg -q 'alert\.Limit' "$HUD" || fail "alert limit missing"
rg -q 'alert\.RecommendedAction' "$HUD" || fail "recommended action missing"
rg -q 'alert\.Acknowledged' "$HUD" || fail "acknowledgement state missing"
rg -q 'ACTION:' "$HUD" || fail "action cue missing"

# Critical alerts may be shown in Clean mode, but the phase title is hidden and the
# dedicated lane remains the only banner content. No simulation code is touched here.
rg -q 'bool criticalOnly = clean && \(exterior \|\| cockpit\) && HasCriticalAlert\(\);' "$HUD" \
  || fail "critical-only visibility mode missing"
rg -q '_phaseLabel\.Visible = banner;' "$HUD" \
  || fail "phase title is not isolated from critical-only alerts"
rg -q '_alertRoot\.Visible = _hasRenderedAlert && \(banner \|\| criticalOnly\);' "$HUD" \
  || fail "alert lane visibility is not mode-gated"
awk '
  /_alertRoot\.Visible = _hasRenderedAlert && \(banner \|\| criticalOnly\);/ { seen=1 }
  /if \(_lastAppliedViewMode == viewMode && _lastAppliedHudDensity == density\)/ {
    if (!seen) exit 1
  }
  END { exit seen ? 0 : 1 }
' "$HUD" || fail "alert visibility must be evaluated before the view-mode cache return"
rg -q '_countdownRequestedVisible' "$HUD" \
  || fail "countdown mission visibility must be kept separate from view-mode filtering"
rg -q '_countdownRoot\.Visible = _countdownRequestedVisible' "$HUD" \
  || fail "countdown visibility filter is not reversible after cockpit/clean mode"
rg -q '_hasRenderedAlert = false;' "$HUD" \
  || fail "stale alert lane state is not cleared"

# Guard against regressing to the old one-label concatenation that mixed details into
# the phase banner row.
if rg -q '\$"\{alert\.Severity\.ToString\(\)\.ToUpperInvariant\(\)\}  \{alert\.Title\}  "' "$HUD"; then
  fail "old single-line alert concatenation remains"
fi

echo "hud_alert_layout_contract_test: PASS"
