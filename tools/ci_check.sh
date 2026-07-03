#!/usr/bin/env bash
set -euo pipefail

# Guard anti-harness: los harnesses de captura visual temporales, escenas y autoloads
# temporales en project.godot NUNCA deben commitearse.
# Anti-harness guard: temporary visual-capture harness scripts/scenes and temporary
# autoloads in project.godot must NEVER be committed.
TRACKED_HARNESS="$(git ls-files 'scripts/_*Shot.cs' 'scripts/_*Shot.cs.uid' 'scripts/*VerifyShot.cs' 'scripts/*VerifyShot.cs.uid' 'scenes/*VerifyShot.tscn' 'scenes/*VerifyShot.tscn.uid')"
if [[ -n "$TRACKED_HARNESS" ]]; then
  echo "ERROR: temporary capture harness is tracked in git:"
  echo "$TRACKED_HARNESS"
  echo "Remove it before committing (see skill visual-testing / .gitignore)."
  exit 1
fi
if grep -Eq '(_[A-Za-z0-9]*Shot|[A-Za-z0-9]*VerifyShot)' project.godot; then
  echo "ERROR: a temporary capture autoload is present in project.godot."
  echo "Restore it with: git checkout project.godot"
  exit 1
fi

dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"

if [[ -x "$GODOT" ]]; then
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3 res://scenes/construction/Construction.tscn

  # Captura de viewport con framebuffer real: --headless usa el renderer dummy y no
  # produce píxeles. Para aceptación visual completa usar:
  #   bash tools/visual_playtest.sh          # local full matrix
  #   bash tools/visual_playtest.sh --smoke  # pad-only (~60s, same as CI)
  #
  # Real-framebuffer viewport capture: --headless uses the dummy renderer and produces
  # no pixels. Full visual acceptance:
  #   bash tools/visual_playtest.sh          # local full matrix
  #   bash tools/visual_playtest.sh --smoke  # pad-only (~60s, CI parity)
else
  echo "Skipping Godot smoke: set GODOT_BIN or install Godot at $DEFAULT_GODOT"
fi
