#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
CI="$ROOT/tools/ci_check.sh"
fail() { echo "godot_smoke_log_contract_test: FAIL: $*" >&2; exit 1; }

[[ -f "$CI" ]] || fail "ci_check.sh missing"

log_count="$(grep -c -- '--log-file /tmp/exo_ci_' "$CI" || true)"
[[ "$log_count" -eq 2 ]] || fail "both Godot smoke launches must use explicit /tmp logs"
grep -q -- '--log-file /tmp/exo_ci_main.godot.log' "$CI" \
  || fail "main smoke log path is not explicit"
grep -q -- '--log-file /tmp/exo_ci_construction.godot.log' "$CI" \
  || fail "Construction smoke log path is not explicit"

echo "godot_smoke_log_contract_test: PASS (headless smoke logs are explicit)"
