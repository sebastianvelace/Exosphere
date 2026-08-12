#!/usr/bin/env bash
set -euo pipefail

# Reproducible Flight 7 ascent benchmark. This wrapper delegates scene setup,
# physics, rendering and cleanup to visual_playtest.sh; it does not modify the
# visual harness or any C# source.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VISUAL_HARNESS="$ROOT/tools/visual_playtest.sh"
PLAYTEST_CONTRACTS="$ROOT/tools/lib/playtest_contracts.sh"

usage() {
  cat <<'EOF'
Usage:
  tools/perf/flight7_phase4_benchmark.sh [options]
  tools/perf/flight7_phase4_benchmark.sh --validate REPORT

Options:
  --run-id ID              Stable artifact id; defaults to flight7-phase4-<pid>.
  --out-dir DIR            Artifact directory; defaults to /tmp/exo_flight7_phase4_<stamp>.
  --max-runtime SEC        Passed to visual_playtest.sh (60..7200, default 1200).
  --max-trace-gap SEC      Maximum allowed TRACE_ASCENT interval (default 60).
  --skip-build             Pass --skip-build to visual_playtest.sh.
  --replay-dir DIR         Rebuild/validate metrics from a completed artifact directory.
  --validate REPORT        Validate an existing Phase 4 key=value report only.
  -h, --help               Show this help.
EOF
}

die() {
  echo "flight7_phase4_benchmark: FAIL $*" >&2
  exit 2
}

require_safe_id() {
  [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]] || die "invalid --run-id: $1"
}

metric_value() {
  local key="$1" file="$2"
  sed -n "s/^${key}=//p" "$file" | tail -n 1
}

field_value() {
  local line="$1" key="$2" token
  for token in $line; do
    if [[ "$token" == "$key="* ]]; then
      printf '%s\n' "${token#*=}"
      return 0
    fi
  done
  printf 'NA\n'
}

parse_elapsed_seconds() {
  local value="$1"
  if [[ "$value" =~ ^([0-9]+):([0-9]{2}):([0-9]{2})(\.[0-9]+)?$ ]]; then
    awk -v h="${BASH_REMATCH[1]}" -v m="${BASH_REMATCH[2]}" \
      -v s="${BASH_REMATCH[3]}${BASH_REMATCH[4]:-}" \
      'BEGIN { printf "%.6f", h * 3600 + m * 60 + s }'
  elif [[ "$value" =~ ^([0-9]+):([0-9]{2})(\.[0-9]+)?$ ]]; then
    awk -v m="${BASH_REMATCH[1]}" \
      -v s="${BASH_REMATCH[2]}${BASH_REMATCH[3]:-}" \
      'BEGIN { printf "%.6f", m * 60 + s }'
  else
    printf 'NA\n'
  fi
}

validate_report() {
  local report="$1"
  [[ -f "$report" ]] || { echo "FAIL report does not exist: $report" >&2; return 1; }

  if grep -Eqi '(^|[[:space:]=])(FAIL|NAN|GAP|FALLBACK)([[:space:]]|$)' "$report"; then
    echo "FAIL report contains a failure, NaN, GAP or FALLBACK token" >&2
    return 1
  fi
  if awk '
    NF == 0 || $0 !~ /^[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]+$/ { bad = 1 }
    {
      key = $0
      sub(/=.*/, "", key)
      if (++seen[key] > 1) bad = 1
    }
    END { exit bad }
  ' "$report"; then
    :
  else
    echo "FAIL report is not strict unique key=value data" >&2
    return 1
  fi

  local required key value
  required=(
    format_version benchmark status mode visual_exit_code
    header_count summary_count summary_reason summary_frames
    trace_count transition_count transition_sequence
    trace_file transitions_file
    nan_detected gap_detected fallback_detected
    trace_numeric_valid trace_time_monotonic trace_progress_detected
    trace_stall_detected trace_stall_pairs trace_max_gap_sec trace_time_span_sec
    trace_first_t_sec trace_last_t_sec trace_first_alt_m trace_last_alt_m
    trace_first_apo_m trace_last_apo_m trace_first_propellant trace_last_propellant
    capture_count capture_valid wall_seconds rss_max_kib failure_reasons
  )
  for key in "${required[@]}"; do
    grep -q "^${key}=" "$report" || {
      echo "FAIL report is missing ${key}" >&2
      return 1
    }
  done

  [[ "$(metric_value format_version "$report")" == "flight7_phase4_v1" ]] ||
    { echo "FAIL unsupported Phase 4 format" >&2; return 1; }
  [[ "$(metric_value benchmark "$report")" == "flight7_phase4" ]] ||
    { echo "FAIL unsupported benchmark name" >&2; return 1; }
  [[ "$(metric_value status "$report")" == "PASS" ]] ||
    { echo "FAIL report status is not PASS" >&2; return 1; }
  [[ "$(metric_value mode "$report")" == "ascent_flight7" ]] ||
    { echo "FAIL report mode is not ascent_flight7" >&2; return 1; }
  [[ "$(metric_value visual_exit_code "$report")" == "0" ]] ||
    { echo "FAIL visual exit code is not zero" >&2; return 1; }
  [[ "$(metric_value summary_reason "$report")" == "ASCENT_ORBIT_OK" ]] ||
    { echo "FAIL report does not require ASCENT_ORBIT_OK" >&2; return 1; }

  for key in header_count summary_count trace_count transition_count trace_stall_pairs capture_count; do
    value="$(metric_value "$key" "$report")"
    [[ "$value" =~ ^[0-9]+$ ]] || { echo "FAIL ${key} is not an integer" >&2; return 1; }
  done
  (( $(metric_value header_count "$report") == 1 )) || { echo "FAIL expected one run header" >&2; return 1; }
  (( $(metric_value summary_count "$report") == 1 )) || { echo "FAIL expected one run summary" >&2; return 1; }
  (( $(metric_value trace_count "$report") >= 5 )) || { echo "FAIL insufficient TRACE_ASCENT samples" >&2; return 1; }
  (( $(metric_value transition_count "$report") >= 2 )) || { echo "FAIL insufficient ascent transitions" >&2; return 1; }
  (( $(metric_value capture_count "$report") >= 1 )) || { echo "FAIL no ascent capture was recorded" >&2; return 1; }

  for key in nan_detected gap_detected fallback_detected trace_numeric_valid \
    trace_time_monotonic trace_progress_detected trace_stall_detected capture_valid; do
    value="$(metric_value "$key" "$report")"
    [[ "$value" == false || "$value" == true ]] || { echo "FAIL ${key} is not boolean" >&2; return 1; }
  done
  for key in nan_detected gap_detected fallback_detected trace_stall_detected; do
    [[ "$(metric_value "$key" "$report")" == false ]] || { echo "FAIL ${key} is asserted" >&2; return 1; }
  done
  for key in trace_numeric_valid trace_time_monotonic trace_progress_detected capture_valid; do
    [[ "$(metric_value "$key" "$report")" == true ]] || { echo "FAIL ${key} is not satisfied" >&2; return 1; }
  done

  local sequence
  sequence="$(metric_value transition_sequence "$report")"
  [[ "$sequence" == *Coast* && "$sequence" == *Insert* ]] ||
    { echo "FAIL transition sequence lacks Coast/Insert" >&2; return 1; }
  [[ "$(metric_value transitions_file "$report")" != "none" ]] ||
    { echo "FAIL transition extraction is missing" >&2; return 1; }
  [[ "$(metric_value trace_file "$report")" != "none" ]] ||
    { echo "FAIL TRACE_ASCENT extraction is missing" >&2; return 1; }
  [[ "$(metric_value failure_reasons "$report")" == "none" ]] ||
    { echo "FAIL report has failure reasons" >&2; return 1; }

  for key in trace_max_gap_sec trace_time_span_sec wall_seconds; do
    value="$(metric_value "$key" "$report")"
    [[ "$value" =~ ^[0-9]+(\.[0-9]+)?$ ]] &&
      awk -v v="$value" 'BEGIN { exit !(v > 0) }' ||
      { echo "FAIL ${key} is not positive finite telemetry" >&2; return 1; }
  done
  value="$(metric_value rss_max_kib "$report")"
  [[ "$value" =~ ^[0-9]+$ ]] && (( value > 0 )) ||
    { echo "FAIL rss_max_kib is not positive integer telemetry" >&2; return 1; }

  echo "flight7_phase4_benchmark: report valid ($report)"
}

if [[ "${1:-}" == "--validate" ]]; then
  [[ $# -eq 2 ]] || die "--validate requires one report path"
  validate_report "$2"
  exit 0
fi

RUN_ID="flight7-phase4-$$"
OUT_DIR=""
MAX_RUNTIME="1200"
MAX_TRACE_GAP="60"
SKIP_BUILD=0
REPLAY_DIR=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run-id) [[ $# -ge 2 ]] || die "--run-id requires a value"; RUN_ID="$2"; shift 2 ;;
    --out-dir) [[ $# -ge 2 ]] || die "--out-dir requires a value"; OUT_DIR="$2"; shift 2 ;;
    --max-runtime) [[ $# -ge 2 ]] || die "--max-runtime requires a value"; MAX_RUNTIME="$2"; shift 2 ;;
    --max-trace-gap) [[ $# -ge 2 ]] || die "--max-trace-gap requires a value"; MAX_TRACE_GAP="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --replay-dir) [[ $# -ge 2 ]] || die "--replay-dir requires a value"; REPLAY_DIR="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

require_safe_id "$RUN_ID"
[[ "$MAX_RUNTIME" =~ ^[0-9]+$ ]] && (( MAX_RUNTIME >= 60 && MAX_RUNTIME <= 7200 )) ||
  die "invalid --max-runtime: $MAX_RUNTIME"
[[ "$MAX_TRACE_GAP" =~ ^[0-9]+(\.[0-9]+)?$ ]] &&
  awk -v v="$MAX_TRACE_GAP" 'BEGIN { exit !(v > 0) }' ||
  die "invalid --max-trace-gap: $MAX_TRACE_GAP"
[[ -x "$VISUAL_HARNESS" ]] || die "visual harness is not executable: $VISUAL_HARNESS"
[[ -f "$PLAYTEST_CONTRACTS" ]] || die "shared playtest contract is missing: $PLAYTEST_CONTRACTS"
source "$PLAYTEST_CONTRACTS"

if [[ -n "$REPLAY_DIR" ]]; then
  [[ -d "$REPLAY_DIR" ]] || die "replay directory does not exist: $REPLAY_DIR"
  REPLAY_DIR="$(cd "$REPLAY_DIR" && pwd)"
  OUT_DIR="$REPLAY_DIR"
elif [[ -z "$OUT_DIR" ]]; then
  OUT_DIR="/tmp/exo_flight7_phase4_$(date +%Y%m%d-%H%M%S)-$$"
fi
mkdir -p "$OUT_DIR"

CAPTURE_DIR="$OUT_DIR/captures"
TELEMETRY="$OUT_DIR/telemetry.log"
STDOUT="$OUT_DIR/visual.stdout"
TIMEFILE="$OUT_DIR/visual.time"
COMBINED="$OUT_DIR/combined.log"
TRACE_FILE="$OUT_DIR/trace_ascent.log"
TRANSITIONS_FILE="$OUT_DIR/transitions_ascent.log"
NORMALIZED_TRACE="$OUT_DIR/trace_ascent.tsv"
TRACE_STATS="$OUT_DIR/trace_stats.tsv"
REPORT="$OUT_DIR/flight7_metrics.tsv"
COMMAND_FILE="$OUT_DIR/command.txt"
mkdir -p "$CAPTURE_DIR"

visual_args=(
  --ascent --flight7
  --run-id "$RUN_ID"
  --out-dir "$CAPTURE_DIR"
  --log "$TELEMETRY"
  --max-runtime "$MAX_RUNTIME"
)
if [[ "$SKIP_BUILD" -eq 1 ]]; then visual_args+=(--skip-build); fi

printf 'GODOT_BIN=%s bash tools/visual_playtest.sh' "${GODOT_BIN:-default}" > "$COMMAND_FILE"
printf ' --ascent --flight7 --run-id %s --out-dir %s --log %s --max-runtime %s' \
  "$RUN_ID" "$CAPTURE_DIR" "$TELEMETRY" "$MAX_RUNTIME" >> "$COMMAND_FILE"
if [[ "$SKIP_BUILD" -eq 1 ]]; then printf ' --skip-build' >> "$COMMAND_FILE"; fi
printf '\n' >> "$COMMAND_FILE"

visual_status=0
if [[ -z "$REPLAY_DIR" ]]; then
  set +e
  /usr/bin/time -v bash "$VISUAL_HARNESS" "${visual_args[@]}" > "$STDOUT" 2> "$TIMEFILE"
  visual_status=$?
  set -e
else
  [[ -s "$TELEMETRY" && -s "$STDOUT" && -s "$TIMEFILE" ]] ||
    die "replay directory lacks telemetry/stdout/time artifacts: $REPLAY_DIR"
fi

: > "$COMBINED"
for input in "$TELEMETRY" "$STDOUT"; do
  if [[ -f "$input" ]]; then
    awk '!seen[$0]++' "$input" >> "$COMBINED"
  fi
done

if [[ -f "$TELEMETRY" ]]; then
  grep '^TRACE_ASCENT ' "$TELEMETRY" > "$TRACE_FILE" || :
  grep '^TRANSITION_ASCENT ' "$TELEMETRY" > "$TRANSITIONS_FILE" || :
else
  : > "$TRACE_FILE"
  : > "$TRANSITIONS_FILE"
fi

trace_line_count="$(wc -l < "$TRACE_FILE" | tr -d ' ')"
trace_parse_errors="$(awk '
  function get(key, i, token) {
    for (i = 1; i <= NF; i++) {
      token = $i
      if (index(token, key "=") == 1) return substr(token, length(key) + 2)
    }
    return ""
  }
  function finite(value) {
    return value ~ /^-?[0-9]+([.][0-9]+)?([eE][+-]?[0-9]+)?$/
  }
  /^TRACE_ASCENT / {
    fields[1] = get("t")
    fields[2] = get("alt")
    fields[3] = get("spd")
    fields[4] = get("vSpeed")
    fields[5] = get("apo")
    fields[6] = get("pe")
    fields[7] = get("propellant")
    for (i = 1; i <= 7; i++) if (!finite(fields[i])) bad++
  }
  END { print bad + 0 }
' "$TRACE_FILE")"

if [[ "$trace_parse_errors" -eq 0 && "$trace_line_count" -gt 0 ]]; then
  awk '
    function get(key, i, token) {
      for (i = 1; i <= NF; i++) {
        token = $i
        if (index(token, key "=") == 1) return substr(token, length(key) + 2)
      }
      return ""
    }
    /^TRACE_ASCENT / {
      printf "%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n", \
        NR, get("t"), get("alt"), get("spd"), get("vSpeed"),
        get("apo"), get("pe"), get("propellant")
    }
  ' "$TRACE_FILE" > "$NORMALIZED_TRACE"
else
  : > "$NORMALIZED_TRACE"
fi

awk -v max_gap="$MAX_TRACE_GAP" -F '\t' '
  BEGIN {
    monotonic = "true"
    progress = 0
    stall_pairs = 0
    max_seen_gap = 0
  }
  NR == 1 {
    first_t = $2; first_alt = $3; first_apo = $6; first_propellant = $8
    last_t = $2; last_alt = $3; last_apo = $6; last_propellant = $8
    for (i = 3; i <= 8; i++) previous[i] = $i
    next
  }
  {
    delta_t = $2 - last_t
    if (!(delta_t > 0)) monotonic = "false"
    if (delta_t > max_seen_gap) max_seen_gap = delta_t
    same = "true"
    for (i = 3; i <= 8; i++) if ($i != previous[i]) same = "false"
    if (same == "true") stall_pairs++
    else progress++
    last_t = $2; last_alt = $3; last_apo = $6; last_propellant = $8
    for (i = 3; i <= 8; i++) previous[i] = $i
  }
  END {
    n = NR
    stalled = (n < 2 || monotonic == "false" || progress == 0 ||
      stall_pairs > 0 || max_seen_gap > max_gap) ? "true" : "false"
    progress_seen = (progress > 0) ? "true" : "false"
    printf "trace_time_monotonic=%s\n", monotonic
    printf "trace_progress_detected=%s\n", progress_seen
    printf "trace_stall_detected=%s\n", stalled
    printf "trace_stall_pairs=%d\n", stall_pairs
    if (n > 0) {
      printf "trace_first_t_sec=%.3f\n", first_t
      printf "trace_last_t_sec=%.3f\n", last_t
      printf "trace_time_span_sec=%.3f\n", last_t - first_t
      printf "trace_first_alt_m=%.3f\n", first_alt
      printf "trace_last_alt_m=%.3f\n", last_alt
      printf "trace_first_apo_m=%.3f\n", first_apo
      printf "trace_last_apo_m=%.3f\n", last_apo
      printf "trace_first_propellant=%.3f\n", first_propellant
      printf "trace_last_propellant=%.3f\n", last_propellant
      printf "trace_max_gap_sec=%.3f\n", max_seen_gap
    } else {
      print "trace_first_t_sec=NA"
      print "trace_last_t_sec=NA"
      print "trace_time_span_sec=NA"
      print "trace_first_alt_m=NA"
      print "trace_last_alt_m=NA"
      print "trace_first_apo_m=NA"
      print "trace_last_apo_m=NA"
      print "trace_first_propellant=NA"
      print "trace_last_propellant=NA"
      print "trace_max_gap_sec=NA"
    }
  }
' "$NORMALIZED_TRACE" > "$TRACE_STATS"

stat_value() {
  local key="$1"
  metric_value "$key" "$TRACE_STATS"
}

header_count="$(grep -c '^=== Exosphere visual playtest ' "$TELEMETRY" 2>/dev/null || true)"
summary_count="$(grep -c '^SUMMARY reason=' "$TELEMETRY" 2>/dev/null || true)"
header_count="${header_count:-0}"
summary_count="${summary_count:-0}"
summary_line="$(grep '^SUMMARY reason=' "$TELEMETRY" 2>/dev/null | tail -n 1 || true)"
summary_reason="$(field_value "$summary_line" reason)"
summary_frames="$(field_value "$summary_line" frames)"
[[ "$summary_reason" == "NA" ]] && summary_reason="missing"
[[ "$summary_frames" == "NA" ]] && summary_frames="NA"

transition_count="$(wc -l < "$TRANSITIONS_FILE" | tr -d ' ')"
transition_sequence="$(awk '
  function get(key, i, token) {
    for (i = 1; i <= NF; i++) {
      token = $i
      if (index(token, key "=") == 1) return substr(token, length(key) + 2)
    }
    return ""
  }
  {
    guidance = get("guidance")
    if (guidance != "") {
      if (sequence == "") sequence = guidance
      else sequence = sequence ">" guidance
    }
  }
  END { print (sequence == "" ? "none" : sequence) }
' "$TRANSITIONS_FILE")"

nan_detected=false
if grep -Eiq '(^|[^[:alnum:]_])(nan|inf|infinity)([^[:alnum:]_]|$)' "$COMBINED"; then nan_detected=true; fi
gap_detected=false
if grep -Eiq '(^|[[:space:]])GAP([[:space:]]|$)' "$COMBINED"; then gap_detected=true; fi
fallback_detected=false
if grep -Eiq '(^|[[:space:]])FALLBACK([[:space:]]|$)' "$COMBINED"; then fallback_detected=true; fi

capture_count=0
capture_files=none
capture_valid=true
if [[ -d "$CAPTURE_DIR" ]]; then
  mapfile -t captures < <(find "$CAPTURE_DIR" -maxdepth 1 -type f -name 'exo_play_*.png' -printf '%f\n' 2>/dev/null | sort)
  capture_count="${#captures[@]}"
  if (( capture_count == 0 )); then
    capture_valid=false
  else
    capture_files="$(IFS=,; echo "${captures[*]}")"
    for capture in "${captures[@]}"; do
      [[ "$(file -b --mime-type "$CAPTURE_DIR/$capture" 2>/dev/null || true)" == "image/png" ]] || capture_valid=false
    done
  fi
else
  capture_valid=false
fi

elapsed_raw="$(sed -n 's/^[[:space:]]*Elapsed (wall clock) time.*: //p' "$TIMEFILE" 2>/dev/null | tail -n 1)"
wall_seconds="$(parse_elapsed_seconds "$elapsed_raw")"
rss_max_kib="$(sed -n 's/^[[:space:]]*Maximum resident set size (kbytes): //p' "$TIMEFILE" 2>/dev/null | tail -n 1)"
rss_max_kib="${rss_max_kib:-0}"

FAILURES=()
add_failure() { FAILURES+=("$1"); }

if (( visual_status != 0 )); then add_failure visual_exit_nonzero; fi
[[ -s "$TELEMETRY" ]] || add_failure telemetry_missing
(( header_count == 1 )) || add_failure run_header_count
(( summary_count == 1 )) || add_failure summary_count
[[ "$summary_reason" == "ASCENT_ORBIT_OK" ]] || add_failure summary_not_ASCENT_ORBIT_OK
(( trace_line_count >= 5 )) || add_failure insufficient_TRACE_ASCENT
(( trace_parse_errors == 0 )) || add_failure trace_numeric_parse_error
(( transition_count >= 2 )) || add_failure insufficient_TRANSITION_ASCENT
grep -Eq '^TRANSITION_ASCENT .*guidance=Coast([[:space:]]|$)' "$TELEMETRY" 2>/dev/null ||
  add_failure missing_Coast_transition
grep -Eq '^TRANSITION_ASCENT .*guidance=Insert([[:space:]]|$)' "$TELEMETRY" 2>/dev/null ||
  add_failure missing_Insert_transition
[[ "$nan_detected" == false ]] || add_failure NaN_or_infinite_telemetry
[[ "$gap_detected" == false ]] || add_failure GAP_telemetry
[[ "$fallback_detected" == false ]] || add_failure FALLBACK_telemetry
[[ "$(stat_value trace_time_monotonic)" == true ]] || add_failure non_monotonic_trace_time
[[ "$(stat_value trace_progress_detected)" == true ]] || add_failure no_physical_trace_progress
[[ "$(stat_value trace_stall_detected)" == false ]] || add_failure stalled_telemetry
[[ "$capture_valid" == true ]] || add_failure missing_or_invalid_capture

if [[ -s "$TELEMETRY" ]] &&
  ! verify_ascent_log_contract "$TELEMETRY" ASCENT_ORBIT_OK >/dev/null 2>&1; then
  add_failure shared_ascent_contract
fi

status=PASS
failure_reasons=none
if (( ${#FAILURES[@]} > 0 )); then
  status=FAIL
  failure_reasons="$(IFS=,; echo "${FAILURES[*]}")"
fi

{
  echo "format_version=flight7_phase4_v1"
  echo "benchmark=flight7_phase4"
  echo "status=$status"
  echo "mode=ascent_flight7"
  echo "visual_exit_code=$visual_status"
  echo "header_count=$header_count"
  echo "summary_count=$summary_count"
  echo "summary_reason=$summary_reason"
  echo "summary_frames=$summary_frames"
  echo "trace_count=$trace_line_count"
  echo "transition_count=$transition_count"
  echo "transition_sequence=$transition_sequence"
  echo "trace_file=$(basename "$TRACE_FILE")"
  echo "transitions_file=$(basename "$TRANSITIONS_FILE")"
  echo "nan_detected=$nan_detected"
  echo "gap_detected=$gap_detected"
  echo "fallback_detected=$fallback_detected"
  echo "trace_numeric_valid=$([[ "$trace_parse_errors" -eq 0 ]] && echo true || echo false)"
  echo "trace_time_monotonic=$(stat_value trace_time_monotonic)"
  echo "trace_progress_detected=$(stat_value trace_progress_detected)"
  echo "trace_stall_detected=$(stat_value trace_stall_detected)"
  echo "trace_stall_pairs=$(stat_value trace_stall_pairs)"
  echo "trace_max_gap_sec=$(stat_value trace_max_gap_sec)"
  echo "trace_time_span_sec=$(stat_value trace_time_span_sec)"
  echo "trace_first_t_sec=$(stat_value trace_first_t_sec)"
  echo "trace_last_t_sec=$(stat_value trace_last_t_sec)"
  echo "trace_first_alt_m=$(stat_value trace_first_alt_m)"
  echo "trace_last_alt_m=$(stat_value trace_last_alt_m)"
  echo "trace_first_apo_m=$(stat_value trace_first_apo_m)"
  echo "trace_last_apo_m=$(stat_value trace_last_apo_m)"
  echo "trace_first_propellant=$(stat_value trace_first_propellant)"
  echo "trace_last_propellant=$(stat_value trace_last_propellant)"
  echo "capture_count=$capture_count"
  echo "capture_valid=$capture_valid"
  echo "capture_files=$capture_files"
  echo "wall_seconds=$wall_seconds"
  echo "rss_max_kib=$rss_max_kib"
  echo "failure_reasons=$failure_reasons"
} > "$REPORT"

if [[ "$status" != PASS ]]; then
  echo "flight7_phase4_benchmark: FAIL artifacts=$OUT_DIR report=$REPORT reasons=$failure_reasons" >&2
  tail -40 "$COMBINED" >&2 || true
  exit 1
fi

validate_report "$REPORT"
echo "flight7_phase4_benchmark: PASS artifacts=$OUT_DIR report=$REPORT"
