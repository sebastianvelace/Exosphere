#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
EDL="$ROOT/scripts/EDLController.cs"

fail() {
  echo "edl_overlay_layout_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$EDL" ]] || fail "EDLController.cs missing"

# The entry rail must cover the physical entry envelope.  Once the vehicle is in the
# landing-burn track, the tighter radar scale is intentional and easier to read.
rg -q 'private static float AltimeterMaxAltitude\(Edl phase\)' "$EDL" \
  || fail "altimeter range helper missing"
rg -q 'Edl\.Entry or Edl\.Peak or Edl\.Aero \? 70_000f : 5_000f' "$EDL" \
  || fail "altimeter does not switch from 70 km to 5 km at RETRO_BURN"
rg -q 'float tickAltitude = maxAlt \* \(1f - i / 5f\)' "$EDL" \
  || fail "altimeter ticks are not derived from the active range"
rg -q 'FormatAltitudeReadout\(_alt\)' "$EDL" \
  || fail "altimeter marker does not use the active unit format"
if rg -q 'const float maxAlt = 5000f|float x = 70f, top = vp\.Y \* 0\.25f' "$EDL"; then
  fail "legacy fixed 5 km/fixed-position altimeter remains"
fi

# All three visual blocks must be driven by one reservation object.  This prevents a
# later text change from moving HIGH G back into the thermal panel.
rg -q 'private readonly struct EdlOverlayLayout' "$EDL" \
  || fail "shared EDL overlay layout missing"
rg -q 'Rect2 AltimeterRect' "$EDL" \
  || fail "altimeter reservation missing"
rg -q 'Rect2 TelemetryRect' "$EDL" \
  || fail "telemetry reservation missing"
rg -q 'Rect2 ThermalRect' "$EDL" \
  || fail "thermal reservation missing"
rg -q 'Vector2 HighGOrigin' "$EDL" \
  || fail "HIGH G reservation missing"
rg -q 'var layout = EdlOverlayLayout\.Build\(vp\)' "$EDL" \
  || fail "EDL draw path does not build the shared layout"
rg -q 'DrawAltimeter\(layout\)' "$EDL" \
  || fail "altimeter does not consume the shared layout"
rg -q 'DrawTelemetry\(layout\)' "$EDL" \
  || fail "telemetry does not consume the shared layout"
rg -q 'DrawThermal\(layout\)' "$EDL" \
  || fail "thermal does not consume the shared layout"
rg -q 'DrawRect\(layout\.TelemetryRect' "$EDL" \
  || fail "telemetry panel is not bounded by its reservation"
rg -q 'DrawRect\(panel,' "$EDL" \
  || fail "thermal panel is not bounded by its reservation"
rg -q 'DrawGLoad\(layout\.GLoadOrigin, layout\)' "$EDL" \
  || fail "G-load readout does not consume the shared layout"
rg -q 'Text\("HIGH G", layout\.HighGOrigin' "$EDL" \
  || fail "HIGH G is not anchored to its reserved position"
if rg -q 'float x = 120f|float px = 260f|DrawGLoad\(new Vector2' "$EDL"; then
  fail "legacy fixed telemetry/thermal coordinates remain"
fi

# Keep the x-separation explicit and proportional at every supported viewport scale:
# telemetry ends at margin+350*scale and thermal starts at margin+400*scale.
rg -q 'margin \+ 120f \* scale' "$EDL" \
  || fail "telemetry column anchor changed unexpectedly"
rg -q 'new Vector2\(230f \* scale, 250f \* scale\)' "$EDL" \
  || fail "telemetry reservation size changed unexpectedly"
rg -q 'margin \+ 400f \* scale' "$EDL" \
  || fail "thermal column is not separated from telemetry"
rg -q 'new Vector2\(250f \* scale, 250f \* scale\)' "$EDL" \
  || fail "thermal reservation size changed unexpectedly"
rg -q 'Vector2 highGOrigin = gLoadOrigin \+ new Vector2\(138f, 20f\) \* scale' "$EDL" \
  || fail "HIGH G is not reserved inside the telemetry column"

echo "edl_overlay_layout_contract_test: PASS (70 km entry rail, 5 km landing rail, disjoint telemetry/thermal/HIGH G layout)"
