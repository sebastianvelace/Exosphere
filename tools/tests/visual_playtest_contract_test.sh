#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
source "$ROOT/tools/lib/playtest_contracts.sh"

HARNESS_SCRIPT="$ROOT/tools/visual_playtest.sh"
bash -n "$HARNESS_SCRIPT"

# Resolution must be explicit, bounded, and shared by both Xvfb/Godot launch paths.
if ! grep -q '^RESOLUTION="1920x1080"$' "$HARNESS_SCRIPT"; then
  echo "FAIL default capture resolution is not 1920x1080" >&2
  exit 1
fi
if ! grep -q -- '--resolution) RESOLUTION="\$2"' "$HARNESS_SCRIPT"; then
  echo "FAIL --resolution parser is missing" >&2
  exit 1
fi
if ! grep -q -- '--display) EXTERNAL_DISPLAY="\$2"' "$HARNESS_SCRIPT" \
  || ! grep -q 'DISPLAY="\$EXTERNAL_DISPLAY" xdpyinfo' "$HARNESS_SCRIPT"; then
  echo "FAIL external display path is missing" >&2
  exit 1
fi
if ! grep -q 'RESOLUTION_WIDTH < 640' "$HARNESS_SCRIPT" \
  || ! grep -q 'RESOLUTION_HEIGHT < 360' "$HARNESS_SCRIPT" \
  || ! grep -q 'RESOLUTION_WIDTH \* RESOLUTION_HEIGHT > 33177600' "$HARNESS_SCRIPT"; then
  echo "FAIL --resolution bounds are missing or too permissive" >&2
  exit 1
fi
screen_count="$(grep -c -- '-screen 0 \${RESOLUTION}x24' "$HARNESS_SCRIPT")"
if [[ "$screen_count" -ne 2 ]]; then
  echo "FAIL expected both xvfb-run paths to use the validated resolution, got $screen_count" >&2
  exit 1
fi
godot_resolution_count="$(grep -c -- '--resolution "\$RESOLUTION"' "$HARNESS_SCRIPT")"
if [[ "$godot_resolution_count" -ne 4 ]]; then
  echo "FAIL expected both display paths in both Godot launches to receive --resolution, got $godot_resolution_count" >&2
  exit 1
fi
if grep -q -- '-screen 0 1920x1080x24' "$HARNESS_SCRIPT"; then
  echo "FAIL hard-coded 1920x1080 Xvfb screen remains" >&2
  exit 1
fi
echo "PASS bounded --resolution is wired to both Xvfb/Godot launches"

# Both Godot launch paths must override the default user://logs destination.
# That default can fail to create its parent directory in the Xvfb environment
# and caused Godot 4.6.3 to abort before the scene loaded.
launch_count="$(grep -c -- '--log-file "\$GODOT_LOG_FILE"' "$HARNESS_SCRIPT")"
if [[ "$launch_count" -ne 4 ]]; then
  echo "FAIL expected 4 explicit Godot --log-file arguments across display paths, got $launch_count" >&2
  exit 1
fi
if ! grep -q 'GODOT_LOG_FILE="\${CONSOLE_LOG}.godot"' "$HARNESS_SCRIPT"; then
  echo "FAIL Godot native log is not isolated beside the per-run console log" >&2
  exit 1
fi
if ! grep -q 'mkdir -p "\$(dirname "\$GODOT_LOG_FILE")"' "$HARNESS_SCRIPT"; then
  echo "FAIL Godot native log parent directory is not created explicitly" >&2
  exit 1
fi
echo "PASS explicit per-launch Godot log contract"

if ! grep -q -- '--orbital-reentry' "$HARNESS_SCRIPT" \
  || ! grep -q 'MODE="orbital_reentry"' "$HARNESS_SCRIPT" \
  || ! grep -q 'ProcessOrbitalReentry' "$HARNESS_SCRIPT"; then
  echo "FAIL normal orbital reentry mode is not wired into the harness" >&2
  exit 1
fi
if ! grep -q -- '--orbit' "$HARNESS_SCRIPT" \
  || ! grep -q 'MODE="orbit"' "$HARNESS_SCRIPT" \
  || ! grep -q 'ORBIT_DIRECT_OK' "$HARNESS_SCRIPT"; then
  echo "FAIL direct orbital visual mode is not wired into the harness" >&2
  exit 1
fi
if ! grep -q -- '--atmosphere-ground' "$HARNESS_SCRIPT" \
  || ! grep -q 'MODE="atmosphere_ground"' "$HARNESS_SCRIPT" \
  || ! grep -q 'ATMOSPHERE_GROUND_OK' "$HARNESS_SCRIPT"; then
  echo "FAIL fast Earth-ground atmosphere matrix is not wired into the harness" >&2
  exit 1
fi
if awk '
  /private void ProcessOrbitalReentry\(/ { inside = 1 }
  inside && /BeginReentryDemonstration/ { found = 1 }
  inside && /^    \/\/ Reuses the exact same deterministic-70km-entry/ { inside = 0 }
  END { exit found ? 0 : 1 }
' "$HARNESS_SCRIPT"; then
  echo "FAIL orbital reentry harness calls the deterministic demo entry point" >&2
  exit 1
fi
if ! grep -q 'source=map_deorbit_autopilot' "$HARNESS_SCRIPT" \
  || ! grep -q 'normalFlow=True demo=False' "$HARNESS_SCRIPT" \
  || ! grep -q 'ORBITAL_REENTRY_OK' "$HARNESS_SCRIPT" \
  || ! grep -q 'ORBITAL_REENTRY_DEORBIT_STALLED' "$HARNESS_SCRIPT"; then
  echo "FAIL normal orbital reentry fail-closed evidence is incomplete" >&2
  exit 1
fi
if ! grep -q 'status="PARTIAL"' "$HARNESS_SCRIPT" \
  || ! grep -q 'PARTIAL reason=INTERRUPTED' "$HARNESS_SCRIPT"; then
  echo "FAIL interrupted visual runs are not marked PARTIAL" >&2
  exit 1
fi
echo "PASS normal orbital reentry is opt-in, non-demo, and fail-closed"

TEST_DIR="$(mktemp -d /tmp/exo_contract_test.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

write_good_log() {
  local target="$1"
  {
    echo "=== Exosphere visual playtest fixture mode=ascent ==="
    echo "TRACE_ASCENT t=1 guidance=Ascent finite=True destroyed=False structuralLost=False"
    echo "TRACE_ASCENT t=2 guidance=Ascent finite=True destroyed=False structuralLost=False"
    echo "TRANSITION_ASCENT t=3 from=Ascent guidance=Coast"
    echo "TRACE_ASCENT t=3 guidance=Coast finite=True destroyed=False structuralLost=False"
    echo "TRACE_ASCENT t=4 guidance=Coast finite=True destroyed=False structuralLost=False"
    echo "TRANSITION_ASCENT t=5 from=Coast guidance=Insert"
    echo "TRACE_ASCENT t=5 guidance=Insert finite=True destroyed=False structuralLost=False"
    echo "CAPTURE orbit pe=146729.1 atmoTop=140000.0"
    echo "SUMMARY reason=ASCENT_ORBIT_OK"
  } > "$target"
}

expect_failure() {
  local name="$1"
  local log="$2"
  if verify_ascent_log_contract "$log" >/dev/null 2>&1; then
    echo "FAIL: contract accepted invalid fixture: $name" >&2
    return 1
  fi
  echo "PASS invalid fixture rejected: $name"
}

good="$TEST_DIR/good.log"
write_good_log "$good"
verify_ascent_log_contract "$good"
echo "PASS valid ascent fixture accepted"

bad_periapsis="$TEST_DIR/bad-periapsis.log"
write_good_log "$bad_periapsis"
sed -i 's/pe=146729.1/pe=-1200.0/' "$bad_periapsis"
expect_failure "suborbital orbit label" "$bad_periapsis"

missing_insert="$TEST_DIR/missing-insert.log"
write_good_log "$missing_insert"
sed -i '/guidance=Insert/d' "$missing_insert"
expect_failure "missing insertion phase" "$missing_insert"

non_finite="$TEST_DIR/non-finite.log"
write_good_log "$non_finite"
sed -i 's/finite=True/finite=False/' "$non_finite"
expect_failure "non-finite state" "$non_finite"

fallback="$TEST_DIR/fallback.log"
write_good_log "$fallback"
echo "FALLBACK JumpToOrbit(200km)" >> "$fallback"
expect_failure "teleport fallback" "$fallback"

regression="$TEST_DIR/insert-to-coast.log"
write_good_log "$regression"
echo "FAIL invariant=insert_to_coast from=Insert guidance=Coast" >> "$regression"
expect_failure "insert-to-coast regression" "$regression"

short_trace="$TEST_DIR/short-trace.log"
write_good_log "$short_trace"
sed -i '1,3d' "$short_trace"
expect_failure "insufficient diagnostic sampling" "$short_trace"

destroyed="$TEST_DIR/destroyed.log"
write_good_log "$destroyed"
sed -i 's/destroyed=False/destroyed=True/' "$destroyed"
expect_failure "destroyed vehicle state" "$destroyed"

malformed_orbit="$TEST_DIR/malformed-orbit.log"
write_good_log "$malformed_orbit"
sed -i 's/pe=146729.1/pe=NaN/' "$malformed_orbit"
expect_failure "non-numeric orbital evidence" "$malformed_orbit"

stalled="$TEST_DIR/stalled.log"
write_good_log "$stalled"
echo "FAIL invariant=physics_stalled noProgressFor=60.1" >> "$stalled"
expect_failure "stalled physics invariant" "$stalled"

nul_bytes="$TEST_DIR/nul-bytes.log"
write_good_log "$nul_bytes"
printf '\0corrupt-tail\n' >> "$nul_bytes"
expect_failure "concurrent-writer NUL corruption" "$nul_bytes"

duplicate_run="$TEST_DIR/duplicate-run.log"
write_good_log "$duplicate_run"
echo "SUMMARY reason=ABORT" >> "$duplicate_run"
expect_failure "duplicate run boundary" "$duplicate_run"

echo "visual_playtest_contract_test: 1 valid and 11 invalid fixtures passed"

orbital_out="$TEST_DIR/orbital-good-out"
mkdir -p "$orbital_out"
for slug in orbital_reentry_orbit orbital_reentry_entry orbital_reentry_peak_heating \
  orbital_reentry_retro_burn orbital_reentry_caught; do
  dd if=/dev/zero of="$orbital_out/exo_play_${slug}.png" bs=9000 count=1 status=none
done
orbital_good="$TEST_DIR/orbital-good.log"
cat > "$orbital_good" <<'EOF'
=== Exosphere visual playtest fixture mode=orbital_reentry ===
NORMAL_REENTRY_SETUP source=JumpToOrbit altitude=250000 pe=250000 ap=250000 atmoTop=140000 launchSite=starbase demo=False flownAscent=False
NORMAL_REENTRY_ARMED source=map_deorbit_autopilot targetPe=60000 dv=95.0 phase=COAST launchSite=starbase demo=False
TRACE_ORBITAL_REENTRY t=1 alt=250000 vUp=0 spd=7700 pe=250000 ap=250000 phase=COAST throttle=0 failedEngines=0 catchArmed=False catchPins=True destroyed=False normalFlow=True demo=False
TRACE_ORBITAL_REENTRY t=2 alt=70000 vUp=-1200 spd=1800 pe=50000 ap=250000 phase=ENTRY throttle=0 failedEngines=0 catchArmed=True catchPins=True destroyed=False normalFlow=True demo=False
CHECK orbital_reentry caught=True pins=2 relativeSpeed=0.030 angularSpeed=0.0000 normalFlow=True demo=False
SUMMARY reason=ORBITAL_REENTRY_OK frames=120
EOF
if ! bash "$HARNESS_SCRIPT" --orbital-reentry --verify-only \
    --out-dir "$orbital_out" --log "$orbital_good" >/dev/null; then
  echo "FAIL valid normal orbital reentry fixture rejected" >&2
  exit 1
fi
echo "PASS valid normal orbital reentry fixture accepted"

orbital_demo="$TEST_DIR/orbital-demo.log"
sed 's/demo=False/demo=True/g' "$orbital_good" > "$orbital_demo"
if bash "$HARNESS_SCRIPT" --orbital-reentry --verify-only \
    --out-dir "$orbital_out" --log "$orbital_demo" >/dev/null 2>&1; then
  echo "FAIL demo-only orbital fixture was accepted" >&2
  exit 1
fi
echo "PASS demo-only orbital fixture rejected"

orbital_no_catch="$TEST_DIR/orbital-no-catch.log"
sed '/CHECK orbital_reentry/d' "$orbital_good" > "$orbital_no_catch"
if bash "$HARNESS_SCRIPT" --orbital-reentry --verify-only \
    --out-dir "$orbital_out" --log "$orbital_no_catch" >/dev/null 2>&1; then
  echo "FAIL no-catch orbital fixture was accepted" >&2
  exit 1
fi
echo "PASS no-catch orbital fixture rejected"

echo "visual_playtest_contract_test: normal orbital reentry fixtures passed"
