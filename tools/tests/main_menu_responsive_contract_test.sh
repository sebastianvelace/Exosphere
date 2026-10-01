#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "main_menu_responsive_contract_test: FAIL: $*" >&2; exit 1; }
menu="$ROOT/scripts/MainMenu.cs"
text="$ROOT/scripts/UI/UserInterfaceSettings.cs"

# Static integration guard. Actual sizing, focus and routes are exercised by
# tools/capture_menu.gd with a real framebuffer, including 150% interface scale.
rg -q 'BuildBody\(\);' "$menu" || fail "operations menu is not reachable at boot"
rg -q 'float effectiveHeight = Size.Y;' "$menu" || fail "layout is not height aware"
rg -q 'Name = "HomeScroll"' "$menu" || fail "short windows cannot scroll navigation"
rg -q 'Name = "ModalScroll"' "$menu" || fail "mission lists cannot scroll"
rg -q '_dossier.Visible = !narrow' "$menu" || fail "secondary content cannot collapse"
rg -q 'SuspendBackgroundFocus\(\);' "$menu" || fail "modal keyboard isolation missing"
rg -q 'flight14_launch_pending.*disabled: true' "$menu" || fail "Flight 14 unavailable launch must remain disabled"
rg -q 'vehicle.SiteId, "manual", "sandbox"' "$menu" || fail "free flight bypasses vehicle selection"
for key in flight_operations footer_controls physics_ready flight14_pending free_flight vehicle_selection; do
  rg -q "\[\"$key\"\]" "$text" || fail "missing localized menu key: $key"
done

echo "main_menu_responsive_contract_test: PASS (reachable modes, scroll, focus and availability guards)"
