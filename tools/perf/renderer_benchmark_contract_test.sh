#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HARNESS="$ROOT/tools/perf/renderer_benchmark.sh"
TEST_DIR="$(mktemp -d /tmp/exo_renderer_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

bash -n "$HARNESS" "$ROOT/tools/perf/renderer_benchmark_contract_test.sh"

good="$TEST_DIR/good.tsv"
cat > "$good" <<'EOF'
format_version=renderer_phase3_v1
status=PASS
mode=pad
renderer=opengl3_xvfb
resolution=1920x1080x24
visual_exit_code=0
frame_count=50
frame_samples=0
frame_time_source=not_emitted_by_current_visual_harness
frame_time_p50_ms=NA
frame_time_p95_ms=NA
frame_time_p99_ms=NA
fps_source=not_emitted_by_current_visual_harness
fps_p50=NA
fps_p95=NA
fps_p99=NA
wall_seconds=1.250000
wall_frames_per_sec=40.000
rss_max_kib=747404
rss_source=gnu_time_process_tree_max_resident_set
capture_count=1
capture_bytes=193391
capture_valid=true
capture_files=exo_play_pad.png
capture_source=tools_visual_playtest
gpu_frame_time_source=NOT_MEASURED
gpu_frame_time_p50_ms=NOT_MEASURED
gpu_frame_time_p95_ms=NOT_MEASURED
gpu_frame_time_p99_ms=NOT_MEASURED
gpu_vram_source=NOT_MEASURED
gpu_vram_bytes=NOT_MEASURED
EOF
bash "$HARNESS" --validate "$good"
echo "PASS valid renderer report accepted"

sampled_fixture="$TEST_DIR/sampled.tsv"
sed \
  -e 's/frame_count=50/frame_count=4/' \
  -e 's/frame_samples=0/frame_samples=4/' \
  -e 's/frame_time_source=not_emitted_by_current_visual_harness/frame_time_source=PERF_FRAME_or_PERF_RENDER_telemetry/' \
  -e 's/frame_time_p50_ms=NA/frame_time_p50_ms=16.000/' \
  -e 's/frame_time_p95_ms=NA/frame_time_p95_ms=24.000/' \
  -e 's/frame_time_p99_ms=NA/frame_time_p99_ms=32.000/' \
  -e 's/fps_source=not_emitted_by_current_visual_harness/fps_source=derived_from_frame_time_percentiles/' \
  -e 's/fps_p50=NA/fps_p50=62.500/' \
  -e 's/fps_p95=NA/fps_p95=41.667/' \
  -e 's/fps_p99=NA/fps_p99=31.250/' \
  "$good" > "$sampled_fixture"
bash "$HARNESS" --validate "$sampled_fixture"
echo "PASS sampled renderer report accepted"

expect_failure() {
  local name="$1" fixture="$2"
  if bash "$HARNESS" --validate "$fixture" >/dev/null 2>&1; then
    echo "FAIL invalid renderer fixture accepted: $name" >&2
    exit 1
  fi
  echo "PASS invalid renderer fixture rejected: $name"
}

fail_fixture="$TEST_DIR/fail.tsv"
sed 's/status=PASS/status=FAIL/' "$good" > "$fail_fixture"
expect_failure "FAIL status" "$fail_fixture"

nan_fixture="$TEST_DIR/nan.tsv"
sed 's/wall_seconds=1.250000/wall_seconds=NAN/' "$good" > "$nan_fixture"
expect_failure "NAN token" "$nan_fixture"

malformed_fixture="$TEST_DIR/malformed.tsv"
sed 's/capture_files=exo_play_pad.png/capture_files=exo play pad.png/' "$good" > "$malformed_fixture"
expect_failure "malformed key=value line" "$malformed_fixture"

missing_fixture="$TEST_DIR/missing.tsv"
sed '/rss_max_kib=/d' "$good" > "$missing_fixture"
expect_failure "missing required metric" "$missing_fixture"

time_fixture="$TEST_DIR/time-output"
printf '    Elapsed (wall clock) time: 1:05.72\n    Maximum resident set size (kbytes): 1249036\n' > "$time_fixture"
elapsed="$(sed -n 's/^[[:space:]]*Elapsed (wall clock) time: //p' "$time_fixture")"
rss="$(sed -n 's/^[[:space:]]*Maximum resident set size (kbytes): //p' "$time_fixture")"
[[ "$elapsed" == "1:05.72" ]] || { echo "FAIL indented GNU time elapsed field was not parseable" >&2; exit 1; }
[[ "$rss" == "1249036" ]] || { echo "FAIL indented GNU time RSS field was not parseable" >&2; exit 1; }
echo "PASS indented GNU time fields accepted"

echo "renderer_benchmark_contract_test: 2 valid and 4 invalid fixtures passed"
