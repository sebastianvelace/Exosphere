#!/usr/bin/env bash
# Private test-build exporter for Exosphere (Godot 4.6.3 .NET).
#
# Produces copyable Linux + Windows folders under dist/, each with:
#   - the Godot export binary + .pck
#   - the Godot-generated data_Exosphere_* .NET folder (left in place)
#   - a loose copy of repo data/ beside the executable (required: System.IO
#     loaders cannot read simulation JSON from the PCK alone; see GameDataPath)
#
# Mission saves use Godot user://saves (cross-platform). The export does not
# ship saves; players keep them in the per-user Godot data dir.
#
# Usage:
#   export GODOT_BIN=/path/to/Godot_v4.6.3-stable_mono_linux.x86_64
#   bash tools/export_game.sh
#   bash tools/export_game.sh --linux-only
#   bash tools/export_game.sh --windows-only
#   bash tools/export_game.sh --skip-build
#
# Linux smoke of an exported folder (optional):
#   xvfb-run -a dist/linux/Exosphere.x86_64 --rendering-driver opengl3 \
#     --quit-after 25 -- --exo-smoke-flight
#   Look for PERF_STARTUP phase=universe_loaded in the log.
#
# Requires official 4.6.3.stable.mono export templates in
#   ~/.local/share/godot/export_templates/4.6.3.stable.mono/

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

GODOT_BIN="${GODOT_BIN:-/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64}"
TEMPLATE_DIR="${HOME}/.local/share/godot/export_templates/4.6.3.stable.mono"
PRESETS_FILE="export_presets.cfg"
DIST_ROOT="dist"
LINUX_DIR="${DIST_ROOT}/linux"
WINDOWS_DIR="${DIST_ROOT}/windows"
LINUX_PRESET="Linux x86_64"
WINDOWS_PRESET="Windows x86_64"

DO_LINUX=1
DO_WINDOWS=1
SKIP_BUILD=0

for arg in "$@"; do
  case "$arg" in
    --linux-only) DO_WINDOWS=0 ;;
    --windows-only) DO_LINUX=0 ;;
    --skip-build) SKIP_BUILD=1 ;;
    -h|--help)
      sed -n '2,25p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 2
      ;;
  esac
done

die() { echo "ERROR: $*" >&2; exit 1; }

[[ -x "$GODOT_BIN" ]] || die "Godot .NET binary not found or not executable: $GODOT_BIN
Set GODOT_BIN to Godot_v4.6.3-stable_mono_linux.x86_64"
[[ -f "$PRESETS_FILE" ]] || die "Missing $PRESETS_FILE (tracked private-test presets)."
[[ -d "$TEMPLATE_DIR" ]] || die "Missing mono export templates at $TEMPLATE_DIR
Download Godot_v4.6.3-stable_mono_export_templates.tpz and install into that folder."
[[ -f "$TEMPLATE_DIR/version.txt" ]] || die "Templates incomplete (no version.txt)."
TEMPLATE_VERSION="$(tr -d '\r\n' < "$TEMPLATE_DIR/version.txt")"
[[ "$TEMPLATE_VERSION" == "4.6.3.stable.mono" ]] || die "Expected templates 4.6.3.stable.mono, found '$TEMPLATE_VERSION'"

GODOT_VERSION="$("$GODOT_BIN" --version 2>/dev/null | head -n1 || true)"
echo "Godot: $GODOT_VERSION"
echo "Templates: $TEMPLATE_VERSION ($TEMPLATE_DIR)"

# Strip a temporary visual-playtest autoload if a concurrent harness left it in
# project.godot. Never leave PlaytestShot in a private test zip.
PROJECT_BACKUP=""
restore_project_godot() {
  if [[ -n "$PROJECT_BACKUP" && -f "$PROJECT_BACKUP" ]]; then
    mv -f "$PROJECT_BACKUP" project.godot
    PROJECT_BACKUP=""
  fi
}
trap restore_project_godot EXIT

if grep -q 'PlaytestShot=' project.godot 2>/dev/null; then
  PROJECT_BACKUP="$(mktemp)"
  cp -a project.godot "$PROJECT_BACKUP"
  # Delete only the temporary harness line; restore from backup on EXIT.
  sed -i '/PlaytestShot=/d' project.godot
  echo "Temporarily removed PlaytestShot autoload from project.godot for export."
fi

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  echo "Building simulation + game assemblies..."
  dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
  dotnet build Exosphere.csproj --nologo -v quiet
fi

copy_loose_data() {
  local dest="$1"
  mkdir -p "$dest"
  rm -rf "${dest}/data"
  # Preserve tree; exclude editor/cache junk if any sneaks under data/.
  rsync -a --delete \
    --exclude '.git/' \
    --exclude '__pycache__/' \
    --exclude '*.uid' \
    data/ "${dest}/data/"
}

export_preset() {
  local preset_name="$1"
  local out_path="$2"
  mkdir -p "$(dirname "$out_path")"
  echo "Exporting preset '$preset_name' -> $out_path"
  # Headless export; --quit so the editor process exits after export.
  "$GODOT_BIN" --headless --path "$ROOT" --export-release "$preset_name" "$out_path"
}

print_tree_summary() {
  local dir="$1"
  echo "--- $dir ---"
  du -sh "$dir" 2>/dev/null || true
  # Avoid `head` under `set -o pipefail` (SIGPIPE → exit 141).
  find "$dir" -maxdepth 2 -mindepth 1 | sort | awk 'NR<=40'
}

LINUX_ZIP=""
WINDOWS_ZIP=""

if [[ "$DO_LINUX" -eq 1 ]]; then
  rm -rf "$LINUX_DIR"
  mkdir -p "$LINUX_DIR"
  export_preset "$LINUX_PRESET" "${LINUX_DIR}/Exosphere.x86_64"
  copy_loose_data "$LINUX_DIR"
  LINUX_ZIP="${DIST_ROOT}/Exosphere-linux-x86_64.zip"
  # zip from inside dist so the archive root is "linux/"
  rm -f "$LINUX_ZIP"
  (cd "$DIST_ROOT" && zip -qr "$(basename "$LINUX_ZIP")" linux)
  print_tree_summary "$LINUX_DIR"
  echo "Linux zip: $LINUX_ZIP ($(du -h "$LINUX_ZIP" | awk '{print $1}'))"
fi

if [[ "$DO_WINDOWS" -eq 1 ]]; then
  rm -rf "$WINDOWS_DIR"
  mkdir -p "$WINDOWS_DIR"
  export_preset "$WINDOWS_PRESET" "${WINDOWS_DIR}/Exosphere.exe"
  copy_loose_data "$WINDOWS_DIR"
  WINDOWS_ZIP="${DIST_ROOT}/Exosphere-windows-x86_64.zip"
  rm -f "$WINDOWS_ZIP"
  (cd "$DIST_ROOT" && zip -qr "$(basename "$WINDOWS_ZIP")" windows)
  print_tree_summary "$WINDOWS_DIR"
  echo "Windows zip: $WINDOWS_ZIP ($(du -h "$WINDOWS_ZIP" | awk '{print $1}'))"
fi

restore_project_godot
trap - EXIT

echo
echo "Export complete (private test build — not a public release)."
echo "Copy the folder or zip to another machine; keep data/ next to the executable."
[[ -n "$LINUX_ZIP" ]] && echo "  Linux:   $ROOT/$LINUX_ZIP"
[[ -n "$WINDOWS_ZIP" ]] && echo "  Windows: $ROOT/$WINDOWS_ZIP"
echo "  Rebuild later: GODOT_BIN=... bash tools/export_game.sh"
