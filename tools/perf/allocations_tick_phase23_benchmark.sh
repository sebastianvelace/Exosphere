#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="${OUT_DIR:-/tmp/exo_allocations_tick_phase23}"
SAMPLES="${SAMPLES:-256}"
WARMUP="${WARMUP:-32}"
REPORT="$OUT_DIR/allocations_tick_metrics.tsv"

mkdir -p "$OUT_DIR"
dotnet build "$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj" \
  --no-restore --nologo -v quiet
dotnet run --project "$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj" \
  --no-build --no-restore -- \
  --samples "$SAMPLES" --warmup "$WARMUP" --out "$REPORT"

for scenario in full_single full_fleet rails_fleet mixed_fleet wake_catchup; do
  rg -q "^${scenario}\.tick_ms_p50=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.tick_ms_p95=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.tick_ms_p99=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.managed_alloc_bytes_per_tick=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.scheduler_telemetry_snapshot\.managed_alloc_bytes_per_operation=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.scheduler_empty\.managed_alloc_bytes_per_operation=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.scheduler_telemetry_snapshot\.valid=true$" "$REPORT"
  rg -q "^${scenario}\.scheduler_empty\.valid=true$" "$REPORT"
  rg -q "^${scenario}\.allocation_valid=true$" "$REPORT"
done

rg -q '^full_single\.flight7_vessel_tick\.managed_alloc_bytes_per_operation=[0-9]+\.[0-9]+$' "$REPORT"
rg -q '^full_single\.engine_readout_snapshot\.managed_alloc_bytes_per_operation=[0-9]+\.[0-9]+$' "$REPORT"
rg -q '^full_single\.hud_telemetry_capture\.managed_alloc_bytes_per_operation=[0-9]+\.[0-9]+$' "$REPORT"
rg -q '^summary_finite=true$' "$REPORT"
rg -q '^summary_valid=true$' "$REPORT"

echo "allocations_tick_phase23_benchmark: PASS report=$REPORT"
