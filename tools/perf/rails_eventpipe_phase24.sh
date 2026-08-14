#!/usr/bin/env bash
set -euo pipefail

# Phase 24 diagnostic only. This file deliberately does not change simulation/runtime code.
# EventPipe collection is optional; the deterministic Phase 23 benchmark is the fail-closed
# fallback and remains the only source of aggregate CPU/allocation numbers when profiling is
# unavailable.

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="${OUT_DIR:-/tmp/exo_rails_eventpipe_phase24}"
SAMPLES="${SAMPLES:-256}"
WARMUP="${WARMUP:-32}"
TIMEOUT_SEC="${TIMEOUT_SEC:-120}"
BASELINE_OUT="$OUT_DIR/baseline"
BASELINE_REPORT="$BASELINE_OUT/allocations_tick_metrics.tsv"
METRICS="$OUT_DIR/rails_mixed_metrics.tsv"
TRACE_OUT="$OUT_DIR/rails_mixed.speedscope.json"
META="$OUT_DIR/matrix.meta"

mkdir -p "$OUT_DIR"

command -v timeout >/dev/null 2>&1 || {
  echo "rails_eventpipe_phase24: BLOCKED timeout_command_missing" >&2
  exit 2
}

trace_bin="$(command -v dotnet-trace || true)"
counters_bin="$(command -v dotnet-counters || true)"
trace_status="BLOCKED_NOT_INSTALLED"
counters_status="BLOCKED_NOT_INSTALLED"
baseline_status="NOT_RUN"

{
  printf 'format_version=rails_eventpipe_phase24_v1\n'
  printf 'timestamp_utc=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'dotnet_version=%s\n' "$(dotnet --version)"
  printf 'runtime=%s\n' "$(dotnet --list-runtimes | awk '/Microsoft.NETCore.App/{print $2; exit}')"
  printf 'os=%s\n' "$(uname -srmo)"
  printf 'samples=%s\n' "$SAMPLES"
  printf 'warmup=%s\n' "$WARMUP"
  printf 'timeout_sec=%s\n' "$TIMEOUT_SEC"
  printf 'dotnet_trace=%s\n' "${trace_bin:-NOT_FOUND}"
  printf 'dotnet_counters=%s\n' "${counters_bin:-NOT_FOUND}"
} > "$META"

if timeout "${TIMEOUT_SEC}s" env OUT_DIR="$BASELINE_OUT" SAMPLES="$SAMPLES" WARMUP="$WARMUP" \
    bash "$ROOT_DIR/tools/perf/allocations_tick_phase23_benchmark.sh" \
    > "$OUT_DIR/baseline.console.log" 2>&1; then
  baseline_status="PASS"
else
  baseline_status="FAILED"
fi

if [[ "$baseline_status" != PASS || ! -s "$BASELINE_REPORT" ]]; then
  printf 'baseline_status=%s\n' "$baseline_status" >> "$META"
  printf 'eventpipe_status=BLOCKED_BASELINE_UNAVAILABLE\n' >> "$META"
  echo "rails_eventpipe_phase24: BLOCKED baseline_status=$baseline_status" >&2
  exit 3
fi

for scenario in rails_fleet mixed_fleet; do
  for key in tick_ms_p50 tick_ms_p95 tick_ms_p99 managed_alloc_bytes_per_tick \
      sample_window_dispatches sample_window_rails_slices sample_window_deadline_projections \
      sample_window_deadline_catchup; do
    rg -q "^${scenario}\\.${key}=[0-9]+\\.?[0-9]*$" "$BASELINE_REPORT" || {
      echo "rails_eventpipe_phase24: BLOCKED missing_metric=${scenario}.${key}" >&2
      exit 4
    }
  done
done

{
  printf 'scenario\tp50_ms\tp95_ms\tp99_ms\talloc_bytes_per_tick\tdispatches_per_tick\trails_slices_per_tick\tprojections_per_tick\tcatchup_per_tick\n'
  for scenario in rails_fleet mixed_fleet; do
    value() { rg "^${scenario}\\.$2=" "$BASELINE_REPORT" | cut -d= -f2; }
    dispatches="$(value "$scenario" sample_window_dispatches)"
    slices="$(value "$scenario" sample_window_rails_slices)"
    projections="$(value "$scenario" sample_window_deadline_projections)"
    catchup="$(value "$scenario" sample_window_deadline_catchup)"
    divisor="$SAMPLES"
    printf '%s\t%s\t%s\t%s\t%s\t%.6f\t%.6f\t%.6f\t%.6f\n' \
      "$scenario" \
      "$(value "$scenario" tick_ms_p50)" \
      "$(value "$scenario" tick_ms_p95)" \
      "$(value "$scenario" tick_ms_p99)" \
      "$(value "$scenario" managed_alloc_bytes_per_tick)" \
      "$(awk -v value="$dispatches" -v divisor="$divisor" 'BEGIN { printf "%.6f", value / divisor }')" \
      "$(awk -v value="$slices" -v divisor="$divisor" 'BEGIN { printf "%.6f", value / divisor }')" \
      "$(awk -v value="$projections" -v divisor="$divisor" 'BEGIN { printf "%.6f", value / divisor }')" \
      "$(awk -v value="$catchup" -v divisor="$divisor" 'BEGIN { printf "%.6f", value / divisor }')"
  done
} > "$METRICS"

if [[ -n "$trace_bin" ]]; then
  trace_status="NOT_RUN"
  if timeout "${TIMEOUT_SEC}s" "$trace_bin" collect \
      --format speedscope \
      --output "$TRACE_OUT" \
      -- dotnet run --project "$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj" \
        --no-build --no-restore -- \
        --samples "$SAMPLES" --warmup "$WARMUP" --out "$OUT_DIR/trace_benchmark.tsv" \
      > "$OUT_DIR/dotnet-trace.console.log" 2>&1; then
    if [[ -s "$TRACE_OUT" ]]; then
      trace_status="PASS_ARTIFACT_ONLY"
    else
      trace_status="BLOCKED_EMPTY_ARTIFACT"
    fi
  else
    trace_status="BLOCKED_COLLECTION_FAILED"
  fi
fi

if [[ -n "$counters_bin" ]]; then
  # dotnet-counters cannot observe a process that has already exited. It is intentionally not
  # attached to a guessed PID; a future long-lived benchmark mode must provide that target.
  counters_status="BLOCKED_NO_LONG_LIVED_TARGET"
fi

{
  printf 'baseline_status=%s\n' "$baseline_status"
  printf 'eventpipe_status=%s\n' "$trace_status"
  printf 'counters_status=%s\n' "$counters_status"
  printf 'aggregate_metrics=%s\n' "$METRICS"
} >> "$META"

if [[ "$trace_status" == PASS_ARTIFACT_ONLY ]]; then
  echo "rails_eventpipe_phase24: PASS_BASELINE_TRACE_ARTIFACT_ONLY baseline=$BASELINE_REPORT trace=$TRACE_OUT"
else
  echo "rails_eventpipe_phase24: BLOCKED_EVENTPIPE baseline=PASS reason=$trace_status"
fi
