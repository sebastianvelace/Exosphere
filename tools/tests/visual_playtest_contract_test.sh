#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
source "$ROOT/tools/lib/playtest_contracts.sh"

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
