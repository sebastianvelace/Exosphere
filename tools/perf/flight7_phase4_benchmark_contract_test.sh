#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HARNESS="$ROOT/tools/perf/flight7_phase4_benchmark.sh"
TEST_DIR="$(mktemp -d /tmp/exo_flight7_phase4_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

bash -n "$HARNESS" "$ROOT/tools/perf/flight7_phase4_benchmark_contract_test.sh"

for key in trace_time_monotonic trace_progress_detected trace_stall_detected trace_stall_pairs \
  trace_max_gap_sec trace_time_span_sec trace_first_t_sec trace_last_t_sec \
  trace_first_alt_m trace_last_alt_m trace_first_apo_m trace_last_apo_m \
  trace_first_propellant trace_last_propellant; do
  grep -q "echo \"${key}=\$(stat_value ${key})\"" "$HARNESS" || {
    echo "FAIL generated report does not prefix ${key}" >&2
    exit 1
  }
done
if grep -Eq '^[[:space:]]*echo "\$\(stat_value ' "$HARNESS"; then
  echo "FAIL generated report contains a bare stat_value line" >&2
  exit 1
fi
echo "PASS generated trace statistics retain key=value names"

write_good_report() {
  local target="$1"
  cat > "$target" <<'EOF'
format_version=flight7_phase4_v1
benchmark=flight7_phase4
status=PASS
mode=ascent_flight7
visual_exit_code=0
header_count=1
summary_count=1
summary_reason=ASCENT_ORBIT_OK
summary_frames=1992
trace_count=48
transition_count=4
transition_sequence=Ignition>Ascent>Coast>Insert
trace_file=trace_ascent.log
transitions_file=transitions_ascent.log
nan_detected=false
gap_detected=false
fallback_detected=false
trace_numeric_valid=true
trace_time_monotonic=true
trace_progress_detected=true
trace_stall_detected=false
trace_stall_pairs=0
trace_max_gap_sec=10.300
trace_time_span_sec=479.000
trace_first_t_sec=7.200
trace_last_t_sec=486.200
trace_first_alt_m=19.800
trace_last_alt_m=150128.800
trace_first_apo_m=19.800
trace_last_apo_m=150654.500
trace_first_propellant=4500000.000
trace_last_propellant=149888.400
capture_count=5
capture_valid=true
capture_files=exo_play_pad.png,exo_play_liftoff.png,exo_play_maxq.png,exo_play_separation.png,exo_play_orbit.png
wall_seconds=512.400000
rss_max_kib=1263648
failure_reasons=none
EOF
}

expect_valid() {
  local name="$1" fixture="$2"
  bash "$HARNESS" --validate "$fixture"
  echo "PASS valid fixture accepted: $name"
}

expect_failure() {
  local name="$1" fixture="$2"
  if bash "$HARNESS" --validate "$fixture" >/dev/null 2>&1; then
    echo "FAIL invalid fixture accepted: $name" >&2
    exit 1
  fi
  echo "PASS invalid fixture rejected: $name"
}

good="$TEST_DIR/good.tsv"
write_good_report "$good"
expect_valid "nominal Flight 7 ascent" "$good"

good_second="$TEST_DIR/good-second.tsv"
sed -e 's/trace_count=48/trace_count=64/' \
  -e 's/trace_max_gap_sec=10.300/trace_max_gap_sec=11.100/' \
  -e 's/summary_frames=1992/summary_frames=2401/' \
  "$good" > "$good_second"
expect_valid "longer monotonic ascent trace" "$good_second"

gap="$TEST_DIR/gap.tsv"
sed 's/gap_detected=false/gap_detected=true/' "$good" > "$gap"
expect_failure "GAP detection" "$gap"

fallback="$TEST_DIR/fallback.tsv"
sed 's/fallback_detected=false/fallback_detected=true/' "$good" > "$fallback"
expect_failure "FALLBACK detection" "$fallback"

nan="$TEST_DIR/nan.tsv"
sed 's/nan_detected=false/nan_detected=true/' "$good" > "$nan"
expect_failure "NaN detection" "$nan"

stalled="$TEST_DIR/stalled.tsv"
sed -e 's/trace_stall_detected=false/trace_stall_detected=true/' \
  -e 's/trace_stall_pairs=0/trace_stall_pairs=3/' \
  "$good" > "$stalled"
expect_failure "stalled telemetry detection" "$stalled"

bad_summary="$TEST_DIR/bad-summary.tsv"
sed 's/summary_reason=ASCENT_ORBIT_OK/summary_reason=TIMEOUT/' "$good" > "$bad_summary"
expect_failure "missing ASCENT_ORBIT_OK" "$bad_summary"

malformed="$TEST_DIR/malformed.tsv"
sed 's/capture_files=.*/capture_files=exo play orbit.png/' "$good" > "$malformed"
expect_failure "non-strict key=value line" "$malformed"

missing="$TEST_DIR/missing.tsv"
sed '/trace_file=/d' "$good" > "$missing"
expect_failure "missing TRACE_ASCENT extraction field" "$missing"

echo "flight7_phase4_benchmark_contract_test: 2 valid and 7 invalid fixtures passed"
