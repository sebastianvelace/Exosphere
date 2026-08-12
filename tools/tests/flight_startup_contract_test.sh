#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CHECK="$ROOT/tools/flight_startup_quick_check.sh"
TEST_DIR="$(mktemp -d /tmp/exo_flight_startup_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

FAKE_GODOT="$TEST_DIR/fake-godot.sh"
cat > "$FAKE_GODOT" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
log_file=""
while (($#)); do
  if [[ "$1" == "--log-file" ]]; then
    log_file="$2"
    shift 2
  else
    shift
  fi
done
printf '%s\n' \
  'PERF_STARTUP phase=simulation_loaded ms=100.0' \
  'PERF_ATMOS body=earth stage=queued worker=true'
if [[ -n "$log_file" ]]; then
  cp /dev/null "$log_file"
fi
EOF
chmod +x "$FAKE_GODOT"

GODOT_BIN="$FAKE_GODOT" "$CHECK" >/dev/null
echo "PASS asynchronous startup fixture accepted"

cat > "$FAKE_GODOT" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
log_file=""
while (($#)); do
  if [[ "$1" == "--log-file" ]]; then
    log_file="$2"
    shift 2
  else
    shift
  fi
done
printf '%s\n' \
  'PERF_STARTUP phase=simulation_loaded ms=100.0' \
  'PERF_ATMOS body=earth stage=transmittance_lut ms=12625.2'
if [[ -n "$log_file" ]]; then
  cp /dev/null "$log_file"
fi
EOF

if GODOT_BIN="$FAKE_GODOT" "$CHECK" >/dev/null 2>&1; then
  echo "FAIL synchronous startup fixture was accepted" >&2
  exit 1
fi
echo "PASS synchronous startup fixture rejected"
