#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "engine_failure_disabled_contract_test: FAIL: $*" >&2; exit 1; }
has() { rg -qF "$1" "$2" || fail "$3"; }

part="$ROOT/ExosphereSimulation/Parts/Part.cs"
graph="$ROOT/ExosphereSimulation/Parts/PartGraph.cs"
vessel="$ROOT/ExosphereSimulation/Vessel.cs"
bridge="$ROOT/scripts/SimulationBridge.cs"
hud="$ROOT/scripts/HUDController.cs"

has 'public bool FuelDepleted' "$part" "fuel depletion state is missing"
has 'public void MarkFuelDepleted()' "$part" "fuel depletion does not stop the engine command"
has 'double effectiveCommandedThrottle = FuelDepleted ? 0.0 : commandedThrottle;' "$part" \
  "fuel depletion does not gate the next engine command"
has 'demand.EnginePart.MarkFuelDepleted();' "$graph" \
  "liquid fuel exhaustion does not use the resource boundary"
has 'engine.MarkFuelDepleted();' "$graph" \
  "non-liquid fuel exhaustion does not use the resource boundary"

for file in "$graph" "$vessel" "$bridge"; do
  if rg -q 'FailEngine\(' "$file"; then
    fail "runtime propulsion path still creates an engine failure in $file"
  fi
done
if rg -q 'InjectActiveEngineFailure|ScheduleActiveEngineFailure|INJECT ENGINE FAILURE|case Key\.F8' "$bridge" "$hud"; then
  fail "the player-facing engine failure controls are still present"
fi
if rg -q 'ENGINE_OVERTEMPERATURE|PROPELLANT_STARVATION|FEED_BRANCH_FLOW_LIMIT|RESTART_LIMIT_EXCEEDED' "$part" "$graph"; then
  fail "automatic engine failure codes remain in runtime propulsion paths"
fi

echo "engine_failure_disabled_contract_test: PASS (fuel depletion retained, failure transitions removed)"
