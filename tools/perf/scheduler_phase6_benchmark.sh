#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT_DIR="${OUT_DIR:-/tmp/exo_scheduler_phase6}"
SAMPLES="${SAMPLES:-80}"
WARMUP="${WARMUP:-10}"
REPORT="$OUT_DIR/scheduler_metrics.tsv"

mkdir -p "$OUT_DIR"
dotnet build "$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj" \
  --nologo -v quiet
dotnet run --project "$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj" \
  --no-build --no-restore -- \
  --samples "$SAMPLES" --warmup "$WARMUP" --out "$REPORT"

for scenario in full_single full_fleet rails_fleet mixed_fleet; do
  rg -q "^scenario=$scenario$" "$REPORT"
  rg -q "^${scenario}\.tick_ms_p50=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.tick_ms_p95=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.tick_ms_p99=[0-9]+\.[0-9]+$" "$REPORT"
  rg -q "^${scenario}\.finite=true$" "$REPORT"
done

echo "scheduler_phase6_benchmark: PASS report=$REPORT"
