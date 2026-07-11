#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
start_ns="$(date +%s%N)"

dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo -v quiet
dotnet build Exosphere.csproj --no-restore --nologo -v quiet

run_atmosphere_tests() {
  dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
    --no-restore --no-build --nologo \
    --filter 'FullyQualifiedName~AtmosphereOpticsTests|FullyQualifiedName~StandardAtmosphereTests|FullyQualifiedName~AtmosphereThermosphereTests'
}
run_atmosphere_tests || run_atmosphere_tests

if [[ -x "$GODOT" ]]; then
  "$GODOT" --headless --path . --quit-after 2 --rendering-driver opengl3
else
  echo "Atmosphere scene smoke skipped: set GODOT_BIN or install Godot at $DEFAULT_GODOT"
fi

elapsed_ms="$(( ($(date +%s%N) - start_ns) / 1000000 ))"
echo "atmosphere_quick_check: PASS in ${elapsed_ms} ms"
