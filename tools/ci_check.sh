#!/usr/bin/env bash
set -euo pipefail

dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"

if [[ -x "$GODOT" ]]; then
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3 res://scenes/construction/Construction.tscn
else
  echo "Skipping Godot smoke: set GODOT_BIN or install Godot at $DEFAULT_GODOT"
fi
