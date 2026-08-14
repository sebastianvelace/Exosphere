#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BENCHMARK="$ROOT_DIR/tools/perf/allocations_tick_phase23_benchmark.sh"
PROJECT="$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"

bash -n "$BENCHMARK" "$ROOT_DIR/tools/perf/allocations_tick_phase23_contract_test.sh"

for scenario in full_single full_fleet rails_fleet mixed_fleet wake_catchup; do
  rg -q --fixed-strings "for scenario in full_single full_fleet rails_fleet mixed_fleet wake_catchup" "$BENCHMARK"
done

rg -q --fixed-strings -- "--samples" "$BENCHMARK"
rg -q --fixed-strings "GC.GetAllocatedBytesForCurrentThread" "$PROJECT"
rg -q --fixed-strings "MeasureDirectVesselTick" "$PROJECT"
rg -q --fixed-strings "MeasureFlight7VesselTick" "$PROJECT"
rg -q --fixed-strings "MeasureTelemetrySnapshot" "$PROJECT"
rg -q --fixed-strings "MeasureEmptyScheduler" "$PROJECT"
rg -q --fixed-strings "MeasureHudTelemetryCapture" "$PROJECT"
rg -q --fixed-strings '"wake_catchup"' "$PROJECT"
rg -q --fixed-strings "allocation_valid=true" "$BENCHMARK"

echo "allocations_tick_phase23_contract_test: PASS (tick, scheduler, telemetry and wake-up coverage)"
