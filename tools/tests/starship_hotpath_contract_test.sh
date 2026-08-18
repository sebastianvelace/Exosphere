#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PART="$ROOT_DIR/ExosphereSimulation/Parts/Part.cs"
GRAPH="$ROOT_DIR/ExosphereSimulation/Parts/PartGraph.cs"
TEST="$ROOT_DIR/ExosphereSimulation.Tests/StarshipPerformanceRegressionTests.cs"

fail() {
  echo "starship_hotpath_contract_test: FAIL: $*" >&2
  exit 1
}

[[ -f "$PART" ]] || fail "missing Part.cs"
[[ -f "$GRAPH" ]] || fail "missing PartGraph.cs"
[[ -f "$TEST" ]] || fail "missing StarshipPerformanceRegressionTests.cs"

# Runtime engine reductions must stay allocation-free in the paths exercised by Vessel.Tick.
if rg -q --fixed-strings '.ToLowerInvariant()' "$PART" "$GRAPH"; then
  fail "hot-path fuel classification still materializes lowercase strings"
fi
rg -q --fixed-strings 'for (int i = 0; i < count; i++)' "$PART" \
  || fail "selected engine reduction loop missing"
rg -q --fixed-strings 'double allocatedBytesPerTick' "$TEST" \
  || fail "Starship allocation budget missing"
rg -q --fixed-strings 'Assert.InRange(allocatedBytesPerTick, 0.0, 1_000.0)' "$TEST" \
  || fail "Starship allocation budget is not enforced"
rg -q --fixed-strings 'internal List<Part> ActiveEngineList' "$GRAPH" \
  || fail "concrete active-engine buffer missing"
rg -q --fixed-strings 'public bool HasActiveEngineParts => ActiveEngineList.Count > 0;' "$GRAPH" \
  || fail "allocation-free active-engine presence query missing"
rg -q --fixed-strings 'public IEnumerable<Part> ActiveEngines => ActiveEngineList;' "$GRAPH" \
  || fail "public active-engine compatibility enumerable missing"
rg -q --fixed-strings 'internal List<Part> PartList => _parts;' "$GRAPH" \
  || fail "concrete part topology buffer missing"
rg -q --fixed-strings 'public IReadOnlyList<Part>  Parts  => _partsView;' "$GRAPH" \
  || fail "public part topology compatibility facade missing"
rg -q --fixed-strings 'GetEngineInstanceThrustGeometrySnapshot' "$PART" "$GRAPH" \
  || fail "thrust geometry snapshot buffer missing"
rg -q --fixed-strings 'GetEngineInstanceGimbalAuthoritySnapshot' "$PART" "$GRAPH" \
  || fail "gimbal authority snapshot buffer missing"
if rg -q 'foreach \(var .*GetEngineInstance(ThrustGeometry|GimbalAuthority)' "$GRAPH"; then
  fail "PartGraph still consumes engine geometry through iterator foreach"
fi

for presentation_file in \
  "$ROOT_DIR/scripts/LaunchEffectsController.cs" \
  "$ROOT_DIR/scripts/EngineStartupController.cs" \
  "$ROOT_DIR/scripts/MissionManager.cs" \
  "$ROOT_DIR/scripts/AudioManager.cs" \
  "$ROOT_DIR/scripts/CameraShake.cs"; do
  if rg -q 'ActiveEngines\.Any\(|ActiveEngines\.GetEnumerator\(\)\.MoveNext\(\)' "$presentation_file"; then
    fail "presence-only engine query still enumerates compatibility view in ${presentation_file##*/}"
  fi
done
rg -q --fixed-strings 'public bool HasActiveEngineParts => Parts.HasActiveEngineParts;' "$ROOT_DIR/ExosphereSimulation/Vessel.cs" \
  || fail "Vessel presence-query wrapper missing"

echo "starship_hotpath_contract_test: PASS (allocation budget, reductions and geometry snapshots)"
