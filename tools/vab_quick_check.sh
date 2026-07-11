#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"

start_ns="$(date +%s%N)"

dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo -v quiet
dotnet build Exosphere.csproj --no-restore --nologo -v quiet
run_construction_tests() {
  dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
    --no-restore --no-build --nologo \
    --filter 'FullyQualifiedName~ConstructionRegressionTests'
}

# vstest uses a short-lived localhost socket. A heavily sandboxed runner can deny the
# first bind transiently; retry once without rebuilding instead of making the developer
# restart the whole check.
run_construction_tests || run_construction_tests

if [[ -x "$GODOT" ]]; then
  "$GODOT" --headless --path . --quit-after 2 --rendering-driver opengl3 \
    res://scenes/construction/Construction.tscn
else
  echo "VAB scene smoke skipped: set GODOT_BIN or install Godot at $DEFAULT_GODOT"
fi

elapsed_ms="$(( ($(date +%s%N) - start_ns) / 1000000 ))"
echo "vab_quick_check: PASS in ${elapsed_ms} ms"
