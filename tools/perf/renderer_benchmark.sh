#!/usr/bin/env bash
set -euo pipefail

# Renderer-backed Phase 3 harness. It delegates scene setup and cleanup to the
# existing visual harness and reports only measurements it can actually observe.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VISUAL_HARNESS="$ROOT/tools/visual_playtest.sh"

usage() {
  cat <<'EOF'
Usage:
  tools/perf/renderer_benchmark.sh --mode pad|cockpit|ascent [options]
  tools/perf/renderer_benchmark.sh --validate REPORT

Options:
  --mode MODE       Renderer-backed visual mode: pad, cockpit, or ascent.
  --run-id ID       Stable artifact id; defaults to renderer-<pid>.
  --out-dir DIR     Artifact directory; defaults to /tmp/exo_renderer_<mode>_<stamp>.
  --max-runtime SEC Passed to visual_playtest.sh (60..7200).
  --skip-build      Skip the visual harness C# build.
  --validate FILE   Validate an existing Phase 3 key=value report and exit.
  -h, --help        Show this help.
EOF
}

die() { echo "renderer_benchmark: FAIL $*" >&2; exit 2; }

require_safe_id() {
  [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]] || die "invalid --run-id: $1"
}

metric_value() {
  local key="$1" file="$2"
  sed -n "s/^${key}=//p" "$file" | tail -n 1
}

parse_elapsed_seconds() {
  local value="$1"
  if [[ "$value" =~ ^([0-9]+):([0-9]{2}):([0-9]{2})(\.[0-9]+)?$ ]]; then
    awk -v h="${BASH_REMATCH[1]}" -v m="${BASH_REMATCH[2]}" \
      -v s="${BASH_REMATCH[3]}${BASH_REMATCH[4]:-}" \
      'BEGIN { printf "%.6f", h * 3600 + m * 60 + s }'
  elif [[ "$value" =~ ^([0-9]+):([0-9]{2})(\.[0-9]+)?$ ]]; then
    awk -v m="${BASH_REMATCH[1]}" -v s="${BASH_REMATCH[2]}${BASH_REMATCH[3]:-}" \
      'BEGIN { printf "%.6f", m * 60 + s }'
  else
    echo "NA"
  fi
}

validate_report() {
  local report="$1"
  [[ -f "$report" ]] || { echo "FAIL report does not exist: $report" >&2; return 1; }
  if grep -Eqi 'FAIL|NAN' "$report"; then
    echo "FAIL report contains FAIL/NAN token" >&2
    return 1
  fi
  if awk 'NF == 0 || $0 !~ /^[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]+$/ { bad = 1 } END { exit bad }' "$report"; then
    :
  else
    echo "FAIL report contains malformed key=value data" >&2
    return 1
  fi

  local required key value
  required=(
    format_version status mode renderer resolution visual_exit_code
    frame_count frame_samples frame_time_source frame_time_p50_ms
    frame_time_p95_ms frame_time_p99_ms fps_source fps_p50 fps_p95 fps_p99
    wall_seconds wall_frames_per_sec rss_max_kib rss_source capture_count
    capture_bytes capture_valid capture_files capture_source gpu_frame_time_source
    gpu_frame_time_p50_ms gpu_frame_time_p95_ms gpu_frame_time_p99_ms
    gpu_vram_source gpu_vram_bytes
  )
  for key in "${required[@]}"; do
    grep -q "^${key}=" "$report" || {
      echo "FAIL report is missing ${key}" >&2
      return 1
    }
  done

  [[ "$(metric_value format_version "$report")" == "renderer_phase3_v1" ]] || { echo "FAIL unsupported format" >&2; return 1; }
  [[ "$(metric_value status "$report")" == "PASS" ]] || { echo "FAIL report status is not PASS" >&2; return 1; }
  [[ "$(metric_value visual_exit_code "$report")" == "0" ]] || { echo "FAIL visual exit code is not zero" >&2; return 1; }
  case "$(metric_value mode "$report")" in pad|cockpit|ascent) ;; *) echo "FAIL unsupported mode" >&2; return 1 ;; esac

  for key in frame_count frame_samples rss_max_kib capture_count capture_bytes; do
    value="$(metric_value "$key" "$report")"
    [[ "$value" =~ ^[0-9]+$ ]] || { echo "FAIL ${key} is not an integer" >&2; return 1; }
  done
  for key in wall_seconds wall_frames_per_sec; do
    value="$(metric_value "$key" "$report")"
    [[ "$value" =~ ^[0-9]+(\.[0-9]+)?$ ]] || { echo "FAIL ${key} is not finite decimal telemetry" >&2; return 1; }
  done

  local samples
  samples="$(metric_value frame_samples "$report")"
  if [[ "$samples" -eq 0 ]]; then
    for key in frame_time_p50_ms frame_time_p95_ms frame_time_p99_ms fps_p50 fps_p95 fps_p99; do
      [[ "$(metric_value "$key" "$report")" == "NA" ]] || { echo "FAIL ${key} must be NA without samples" >&2; return 1; }
    done
  else
    for key in frame_time_p50_ms frame_time_p95_ms frame_time_p99_ms fps_p50 fps_p95 fps_p99; do
      value="$(metric_value "$key" "$report")"
      [[ "$value" =~ ^[0-9]+(\.[0-9]+)?$ ]] && awk -v v="$value" 'BEGIN { exit !(v > 0) }' || {
        echo "FAIL ${key} is not positive finite telemetry" >&2
        return 1
      }
    done
  fi

  for key in gpu_frame_time_p50_ms gpu_frame_time_p95_ms gpu_frame_time_p99_ms gpu_vram_bytes; do
    [[ "$(metric_value "$key" "$report")" == "NOT_MEASURED" ]] || { echo "FAIL ${key} must be NOT_MEASURED" >&2; return 1; }
  done
  [[ "$(metric_value capture_valid "$report")" == "true" ]] || { echo "FAIL capture_valid is not true" >&2; return 1; }
  echo "renderer_benchmark: report valid ($report)"
}

percentile() {
  local sorted="$1" percent="$2"
  awk -v p="$percent" '
    { values[NR] = $1 }
    END {
      n = NR
      if (n == 0) { print "NA"; exit }
      rank = int((p * n + 99) / 100)
      if (rank < 1) rank = 1
      if (rank > n) rank = n
      printf "%.3f\n", values[rank]
    }' "$sorted"
}

extract_samples() {
  local telemetry="$1" stdout="$2" destination="$3"
  awk '!seen[$0]++' "$telemetry" "$stdout" 2>/dev/null \
    | awk '/PERF_(FRAME|RENDER)([[:space:]]|$)/ {
        for (i = 1; i <= NF; i++) if ($i ~ /^frame_ms=/) {
          split($i, pair, "=")
          if (pair[2] ~ /^[0-9]+(\.[0-9]+)?$/ && pair[2] > 0) print pair[2]
        }
      }' | sort -n > "$destination"
}

if [[ "${1:-}" == "--validate" ]]; then
  [[ $# -eq 2 ]] || die "--validate requires one report path"
  validate_report "$2"
  exit 0
fi

MODE="pad"
RUN_ID="renderer-$$"
OUT_DIR=""
MAX_RUNTIME="1800"
SKIP_BUILD=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode) [[ $# -ge 2 ]] || die "--mode requires a value"; MODE="$2"; shift 2 ;;
    --run-id) [[ $# -ge 2 ]] || die "--run-id requires a value"; RUN_ID="$2"; shift 2 ;;
    --out-dir) [[ $# -ge 2 ]] || die "--out-dir requires a value"; OUT_DIR="$2"; shift 2 ;;
    --max-runtime) [[ $# -ge 2 ]] || die "--max-runtime requires a value"; MAX_RUNTIME="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

case "$MODE" in
  pad) VISUAL_MODE="--smoke" ;;
  cockpit) VISUAL_MODE="--cockpit" ;;
  ascent) VISUAL_MODE="--ascent" ;;
  *) die "--mode must be pad, cockpit, or ascent" ;;
esac
require_safe_id "$RUN_ID"
[[ "$MAX_RUNTIME" =~ ^[0-9]+$ ]] && (( MAX_RUNTIME >= 60 && MAX_RUNTIME <= 7200 )) || die "invalid --max-runtime"

if [[ -z "$OUT_DIR" ]]; then OUT_DIR="/tmp/exo_renderer_${MODE}_$(date +%Y%m%d-%H%M%S)-$$"; fi
mkdir -p "$OUT_DIR"
CAPTURE_DIR="$OUT_DIR/captures"
TELEMETRY="$OUT_DIR/telemetry.log"
STDOUT="$OUT_DIR/visual.stdout"
TIMEFILE="$OUT_DIR/visual.time"
SAMPLES="$OUT_DIR/frame_samples.ms"
REPORT="$OUT_DIR/renderer_metrics.tsv"
mkdir -p "$CAPTURE_DIR"

visual_args=("$VISUAL_MODE" --run-id "$RUN_ID" --out-dir "$CAPTURE_DIR" --log "$TELEMETRY" --max-runtime "$MAX_RUNTIME")
if [[ "$SKIP_BUILD" -eq 1 ]]; then visual_args+=(--skip-build); fi

visual_status=0
set +e
/usr/bin/time -v bash "$VISUAL_HARNESS" "${visual_args[@]}" > "$STDOUT" 2> "$TIMEFILE"
visual_status=$?
set -e

if [[ -f "$TELEMETRY" || -f "$STDOUT" ]]; then
  : > "$SAMPLES"
  extract_samples "$TELEMETRY" "$STDOUT" "$SAMPLES"
else
  : > "$SAMPLES"
fi
sample_count="$(wc -l < "$SAMPLES" | tr -d ' ')"
if [[ "$sample_count" -gt 0 ]]; then
  frame_p50="$(percentile "$SAMPLES" 50)"; frame_p95="$(percentile "$SAMPLES" 95)"; frame_p99="$(percentile "$SAMPLES" 99)"
  fps_p50="$(awk -v v="$frame_p50" 'BEGIN { printf "%.3f", 1000 / v }')"
  fps_p95="$(awk -v v="$frame_p95" 'BEGIN { printf "%.3f", 1000 / v }')"
  fps_p99="$(awk -v v="$frame_p99" 'BEGIN { printf "%.3f", 1000 / v }')"
  frame_source="PERF_FRAME_or_PERF_RENDER_telemetry"; fps_source="derived_from_frame_time_percentiles"
else
  frame_p50="NA"; frame_p95="NA"; frame_p99="NA"; fps_p50="NA"; fps_p95="NA"; fps_p99="NA"
  frame_source="not_emitted_by_current_visual_harness"; fps_source="not_emitted_by_current_visual_harness"
fi

elapsed_raw="$(sed -n 's/^[[:space:]]*Elapsed (wall clock) time.*: //p' "$TIMEFILE" | tail -n 1)"
wall_seconds="$(parse_elapsed_seconds "$elapsed_raw")"
frame_count="$(sed -n 's/.*SUMMARY reason=[^ ]* frames=\([0-9][0-9]*\).*/\1/p' "$TELEMETRY" 2>/dev/null | tail -n 1)"; frame_count="${frame_count:-0}"
if [[ "$wall_seconds" != "NA" ]] && [[ "$wall_seconds" != "0.000000" ]]; then
  wall_frames_per_sec="$(awk -v f="$frame_count" -v s="$wall_seconds" 'BEGIN { printf "%.3f", f / s }')"
else
  wall_frames_per_sec="0.000"
fi
rss_max_kib="$(sed -n 's/^[[:space:]]*Maximum resident set size (kbytes): //p' "$TIMEFILE" | tail -n 1)"; rss_max_kib="${rss_max_kib:-0}"

mapfile -t captures < <(find "$CAPTURE_DIR" -maxdepth 1 -type f -name 'exo_play_*.png' -printf '%f\n' 2>/dev/null | sort)
capture_count="${#captures[@]}"; capture_files=""; capture_bytes=0; capture_valid=true
: > "$OUT_DIR/capture_manifest.tsv"
for capture in "${captures[@]}"; do
  bytes="$(stat -c '%s' "$CAPTURE_DIR/$capture")"
  mime="$(file -b --mime-type "$CAPTURE_DIR/$capture" 2>/dev/null || true)"
  [[ "$mime" == "image/png" ]] || capture_valid=false
  capture_bytes=$((capture_bytes + bytes))
  printf '%s\t%s\t%s\n' "$capture" "$bytes" "$mime" >> "$OUT_DIR/capture_manifest.tsv"
  if [[ -z "$capture_files" ]]; then capture_files="$capture"; else capture_files+=",$capture"; fi
done
if [[ "$capture_count" -eq 0 || "$visual_status" -ne 0 ]]; then capture_valid=false; fi

status="FAIL"
if [[ "$visual_status" -eq 0 && "$capture_valid" == true && "$wall_seconds" != "NA" ]]; then status="PASS"; fi
{
  echo "format_version=renderer_phase3_v1"
  echo "status=$status"
  echo "mode=$MODE"
  echo "renderer=opengl3_xvfb"
  echo "resolution=1920x1080x24"
  echo "visual_exit_code=$visual_status"
  echo "frame_count=$frame_count"
  echo "frame_samples=$sample_count"
  echo "frame_time_source=$frame_source"
  echo "frame_time_p50_ms=$frame_p50"
  echo "frame_time_p95_ms=$frame_p95"
  echo "frame_time_p99_ms=$frame_p99"
  echo "fps_source=$fps_source"
  echo "fps_p50=$fps_p50"
  echo "fps_p95=$fps_p95"
  echo "fps_p99=$fps_p99"
  echo "wall_seconds=$wall_seconds"
  echo "wall_frames_per_sec=$wall_frames_per_sec"
  echo "rss_max_kib=$rss_max_kib"
  echo "rss_source=gnu_time_process_tree_max_resident_set"
  echo "capture_count=$capture_count"
  echo "capture_bytes=$capture_bytes"
  echo "capture_valid=$capture_valid"
  echo "capture_files=${capture_files:-none}"
  echo "capture_source=tools_visual_playtest"
  echo "gpu_frame_time_source=NOT_MEASURED"
  echo "gpu_frame_time_p50_ms=NOT_MEASURED"
  echo "gpu_frame_time_p95_ms=NOT_MEASURED"
  echo "gpu_frame_time_p99_ms=NOT_MEASURED"
  echo "gpu_vram_source=NOT_MEASURED"
  echo "gpu_vram_bytes=NOT_MEASURED"
} > "$REPORT"

if [[ "$status" != "PASS" ]]; then
  echo "renderer_benchmark: FAIL mode=$MODE artifacts=$OUT_DIR report=$REPORT" >&2
  tail -30 "$STDOUT" >&2 || true
  if [[ "$visual_status" -ne 0 ]]; then exit "$visual_status"; fi
  exit 1
fi
validate_report "$REPORT"
echo "renderer_benchmark: PASS mode=$MODE artifacts=$OUT_DIR report=$REPORT"
