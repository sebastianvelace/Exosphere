#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fail() { echo "main_menu_responsive_contract_test: FAIL: $*" >&2; exit 1; }

menu="$ROOT/scripts/MainMenu.cs"
text="$ROOT/scripts/UI/UserInterfaceSettings.cs"

rg -q 'float effectiveHeight = Size\.Y' "$menu" \
  || fail "menu has no height-aware responsive branch"
rg -q 'private GridContainer _navigation' "$menu" \
  || fail "menu navigation is not using the mission module grid"
rg -q 'Columns = 2' "$menu" \
  || fail "mission module grid does not declare two columns"
rg -q 'button\.CustomMinimumSize = new Vector2\(198, compact \? 42 : 52\)' "$menu" \
  || fail "compact navigation does not preserve a bounded hit target"
rg -q '_dossier\.Visible = !narrow' "$menu" \
  || fail "narrow layout does not remove the secondary dossier"
rg -q '\["flight_operations"\]' "$text" \
  || fail "menu classification is not localized"
rg -q '\["footer_controls"\]' "$text" \
  || fail "menu footer controls are not localized"
rg -q '\["physics_ready"\]' "$text" \
  || fail "menu status is not localized"
rg -q 'UiText\.Get\("dossier_note"\)' "$menu" \
  || fail "dossier note still bypasses localization"

echo "main_menu_responsive_contract_test: PASS (height-aware 1280 layout, localized chrome)"
