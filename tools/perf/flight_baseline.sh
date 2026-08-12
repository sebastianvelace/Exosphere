#!/usr/bin/env bash
set -euo pipefail

# Agent 0 baseline runner. This deliberately uses a headless Flight scene and does not
# install an autoload, edit project.godot, or change game sources. It is separate from
# tools/flight_startup_quick_check.sh because it records longer-run CPU/RSS/FPS evidence.

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
FRAMES="${FRAMES:-300}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-60}"
FIXED_FPS="${FIXED_FPS:-60}"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT_DIR="${OUT_DIR:-/tmp/exo_perf_baseline_${STAMP}}"

if [[ ! -x "$GODOT" ]]; then
  echo "flight_baseline: SKIP (set GODOT_BIN to a Godot 4.6.3 mono binary)"
  exit 0
fi

mkdir -p "$OUT_DIR"
LOG="$OUT_DIR/flight.log"
STDOUT="$OUT_DIR/flight.stdout"
TIMEFILE="$OUT_DIR/flight.time"
COMBINED="$OUT_DIR/flight.combined.log"
SUMMARY="$OUT_DIR/summary.tsv"

cat > "$OUT_DIR/command.txt" <<EOF
timeout ${TIMEOUT_SECONDS}s /usr/bin/time -v "$GODOT" --headless --path . \\
  --scene res://scenes/flight/Flight.tscn --quit-after ${FRAMES} \\
  --fixed-fps ${FIXED_FPS} --print-fps --log-file "$LOG"
EOF

set +e
/usr/bin/time -v timeout "${TIMEOUT_SECONDS}s" "$GODOT" \
  --headless --path . \
  --scene res://scenes/flight/Flight.tscn \
  --quit-after "$FRAMES" \
  --fixed-fps "$FIXED_FPS" \
  --print-fps \
  --log-file "$LOG" \
  > "$STDOUT" 2> "$TIMEFILE"
status=$?
set -e

# Godot mirrors the same PERF lines to stdout and --log-file. De-duplicate by complete
# line so startup and worker events represent one observation in the report.
awk '!seen[$0]++' "$STDOUT" "$LOG" > "$COMBINED"

{
  echo -e "metric\tvalue"
  echo -e "exit_code\t${status}"
  echo -e "frames_requested\t${FRAMES}"
  echo -e "fixed_fps\t${FIXED_FPS}"
  awk -F'ms=' '/PERF_STARTUP phase=/{split($1, a, "phase="); gsub(/[[:space:]]+$/, "", a[2]); split($2, b, " "); print "startup_" a[2] "_ms\t" b[1]}' "$COMBINED"
  awk '/PERF_ATMOS/{print "atmosphere_event\t" $0}' "$COMBINED"
  awk -F': ' '/Project FPS:/{print "project_fps\t" $2}' "$COMBINED" | tail -1
  awk -F': ' '/Elapsed \(wall clock\) time/{print "wall_time\t" $2}' "$TIMEFILE"
  awk -F': ' '/Maximum resident set size/{print "max_rss_kib\t" $2}' "$TIMEFILE"
  awk -F': ' '/User time \(seconds\)/{print "user_cpu_seconds\t" $2}' "$TIMEFILE"
  awk -F': ' '/System time \(seconds\)/{print "system_cpu_seconds\t" $2}' "$TIMEFILE"
  awk -F': ' '/Minor \(reclaiming I\/O\) page faults/{print "minor_page_faults\t" $2}' "$TIMEFILE"
} > "$SUMMARY"

cat "$SUMMARY"
echo "artifacts_dir=${OUT_DIR}"

if [[ "$status" -ne 0 ]]; then
  echo "flight_baseline: FAIL (Godot exit code ${status})" >&2
  tail -80 "$COMBINED" >&2 || true
  exit "$status"
fi

if rg -q 'SCRIPT ERROR|ERROR: /root: The caller thread' "$COMBINED"; then
  echo "flight_baseline: FAIL (runtime error detected)" >&2
  rg -n 'SCRIPT ERROR|ERROR: /root: The caller thread' "$COMBINED" >&2
  exit 1
fi

echo "flight_baseline: PASS (Flight completed ${FRAMES} iterations)"
