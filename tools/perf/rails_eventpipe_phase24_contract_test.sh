#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUNNER="$ROOT_DIR/tools/perf/rails_eventpipe_phase24.sh"

bash -n "$RUNNER" "$ROOT_DIR/tools/perf/rails_eventpipe_phase24_contract_test.sh"
rg -q --fixed-strings 'dotnet-trace' "$RUNNER"
rg -q --fixed-strings 'dotnet-counters' "$RUNNER"
rg -q --fixed-strings 'BLOCKED' "$RUNNER"
rg -q --fixed-strings 'timeout' "$RUNNER"
rg -q --fixed-strings 'allocations_tick_phase23_benchmark.sh' "$RUNNER"
rg -q --fixed-strings 'rails_fleet' "$RUNNER"
rg -q --fixed-strings 'mixed_fleet' "$RUNNER"
rg -q --fixed-strings 'sample_window_deadline_projections' "$RUNNER"

if rg -q 'ExosphereSimulation|scripts/|project\.godot' "$RUNNER"; then
  echo "rails_eventpipe_phase24_contract_test: FAIL runtime ownership leak" >&2
  exit 1
fi

echo "rails_eventpipe_phase24_contract_test: PASS (fail-closed EventPipe runner and rails/mixed fallback)"
