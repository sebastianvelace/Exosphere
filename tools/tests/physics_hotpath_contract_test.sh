#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNIVERSE="$ROOT_DIR/ExosphereSimulation/Universe.cs"
ENGINE_HUD="$ROOT_DIR/scripts/EngineGridHUD.cs"

fail() {
  echo "physics_hotpath_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$UNIVERSE" ]] || fail "missing Universe.cs"
[[ -f "$ENGINE_HUD" ]] || fail "missing EngineGridHUD.cs"

# Tick loops must preserve the old snapshot semantics for structural breakup while
# avoiding a fresh List allocation for every physics substep.
rg -q --fixed-strings 'for (int i = 0, count = _vessels.Count; i < count; i++)' "$UNIVERSE" \
  || fail "indexed vessel tick loop missing"
if rg -q --fixed-strings '_vessels.ToList()' "$UNIVERSE"; then
  fail "vessel scheduler still snapshots the fleet with ToList"
fi
rg -q --fixed-strings 'structural breakup may append debris mid-loop' "$UNIVERSE" \
  || fail "structural-breakup snapshot invariant is undocumented"

# Engine telemetry is presentation-only: keep it bounded and reuse its readout
# buffer so the HUD cannot turn render cadence into simulation-thread garbage.
rg -q --fixed-strings 'TelemetryUpdatePeriodSeconds = 0.10' "$ENGINE_HUD" \
  || fail "engine telemetry cadence missing"
rg -q --fixed-strings '_telemetryAccumulator' "$ENGINE_HUD" \
  || fail "engine telemetry accumulator missing"
rg -q --fixed-strings '_readoutScratch' "$ENGINE_HUD" \
  || fail "engine readout scratch buffer missing"
if rg -q --fixed-strings 'using System.Linq;' "$ENGINE_HUD" || rg -q --fixed-strings '.ToList()' "$ENGINE_HUD"; then
  fail "engine HUD retains per-update LINQ list materialization"
fi

echo "physics_hotpath_contract_test: PASS (allocation-free vessel snapshots, bounded engine HUD telemetry)"
