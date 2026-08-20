#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
HARNESS="$ROOT/tools/visual_playtest.sh"
SUN="$ROOT/scripts/SunController.cs"
CAMERA="$ROOT/scripts/CameraController.cs"

bash -n "$HARNESS"

grep -q -- '--sun-elevation DEG' "$HARNESS" \
  || { echo "FAIL daylight elevation option is not documented" >&2; exit 1; }
grep -q -- '--camera-preset NAME' "$HARNESS" \
  || { echo "FAIL camera preset option is not documented" >&2; exit 1; }
grep -q -- '--sun-elevation) SUN_ELEVATION_DEG="\$2"' "$HARNESS" \
  || { echo "FAIL daylight elevation parser is missing" >&2; exit 1; }
grep -q -- '--camera-preset) CAMERA_PRESET="\$2"' "$HARNESS" \
  || { echo "FAIL camera preset parser is missing" >&2; exit 1; }
grep -q 'elevation >= -90.0 && elevation <= 90.0' "$HARNESS" \
  || { echo "FAIL daylight elevation bounds are missing" >&2; exit 1; }
grep -q 'pad_side|tower_side|tracking|orbit_beauty|edl_side' "$HARNESS" \
  || { echo "FAIL camera preset allow-list is missing" >&2; exit 1; }

if bash "$HARNESS" --sun-elevation 91 --run-id visual-contract-invalid-elevation \
    --skip-build >/dev/null 2>&1; then
  echo "FAIL invalid daylight elevation was accepted" >&2
  exit 1
fi
if bash "$HARNESS" --camera-preset invalid --run-id visual-contract-invalid-preset \
    --skip-build >/dev/null 2>&1; then
  echo "FAIL invalid camera preset was accepted" >&2
  exit 1
fi

grep -q 'SetVisualSunElevationOverride' "$SUN" \
  || { echo "FAIL presentation solar override is not exposed" >&2; exit 1; }
grep -q 'GetVisualSunDirection' "$SUN" \
  || { echo "FAIL visual solar direction helper is missing" >&2; exit 1; }
grep -q 'physicalSunPositionUnchanged=True' "$HARNESS" \
  || { echo "FAIL visual telemetry does not prove physical Sun is unchanged" >&2; exit 1; }
grep -q 'TryApplyVisualPreset' "$CAMERA" \
  || { echo "FAIL deterministic camera preset API is missing" >&2; exit 1; }
for preset in pad_side tower_side tracking orbit_beauty edl_side; do
  grep -q "case \"$preset\"" "$CAMERA" \
    || { echo "FAIL camera preset is not implemented: $preset" >&2; exit 1; }
done

echo "visual_daylight_capture_contract_test: PASS (bounded sun override and five camera presets)"
