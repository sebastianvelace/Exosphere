#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
if [[ ! -x "$GODOT" ]]; then
  echo "flight_startup_quick_check: SKIP (set GODOT_BIN to a Godot 4.6.3 mono binary)"
  exit 0
fi

CHECK_DIR="$(mktemp -d /tmp/exo_flight_startup_check.XXXXXX)"
trap 'rm -rf "$CHECK_DIR"' EXIT
LOG="$CHECK_DIR/flight.log"
STDOUT="$CHECK_DIR/flight.stdout"

if ! timeout 20s "$GODOT" --headless --path . \
  --scene res://scenes/flight/Flight.tscn \
  --quit-after 60 --log-file "$LOG" > "$STDOUT" 2>&1; then
  echo "flight_startup_quick_check: FAIL (Flight did not reach 60 frames in 20 s)" >&2
  tail -80 "$STDOUT" >&2 || true
  exit 1
fi

combined="$CHECK_DIR/combined.log"
cat "$STDOUT" "$LOG" > "$combined"
rg -q 'PERF_STARTUP phase=simulation_loaded' "$combined"
rg -q 'PERF_ATMOS body=earth stage=queued worker=true' "$combined"
if rg -q 'PERF_ATMOS body=earth stage=transmittance_lut' "$combined"; then
  echo "flight_startup_quick_check: FAIL (synchronous transmittance LUT build detected)" >&2
  exit 1
fi
if rg -q 'SCRIPT ERROR|ERROR: /root: The caller thread' "$combined"; then
  echo "flight_startup_quick_check: FAIL (runtime error detected)" >&2
  rg -n 'SCRIPT ERROR|ERROR: /root: The caller thread' "$combined" >&2
  exit 1
fi

echo "flight_startup_quick_check: PASS (Flight reached 60 frames with asynchronous atmosphere build)"
