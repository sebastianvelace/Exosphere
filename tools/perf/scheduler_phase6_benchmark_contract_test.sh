#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BENCHMARK="$ROOT_DIR/tools/perf/scheduler_phase6_benchmark.sh"
PROJECT="$ROOT_DIR/tools/SchedulerBenchmark/SchedulerBenchmark.csproj"

rg -q --fixed-strings -- "--samples" "$BENCHMARK"
rg -q --fixed-strings "tick_ms_p95" "$BENCHMARK"
rg -q --fixed-strings "GC.GetAllocatedBytesForCurrentThread" "$PROJECT" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "PhysicsSchedulerTelemetry" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "BuildMixedFleet" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "BuildRailsFleet" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "scheduler_phase23_v1" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings '"wake_catchup"' "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "sample_window_dispatches" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"
rg -q --fixed-strings "sample_window_deadline_catchup" "$ROOT_DIR/tools/SchedulerBenchmark/Program.cs"

echo "scheduler_phase6_benchmark_contract_test: PASS (phase23 window + wake_catchup covered)"
