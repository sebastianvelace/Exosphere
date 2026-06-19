#!/usr/bin/env bash
# Stop hook — refuerza la regla "build 0/0" de Exosphere.
# Compila las dos soluciones; si hay errores O warnings, bloquea el fin del turno y
# devuelve el detalle a Claude para que lo corrija. Sale 0 (sin bloqueo) si todo está limpio.
set -uo pipefail

DIR="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
cd "$DIR" 2>/dev/null || exit 0

# Sin .NET disponible: no bloquear (no es el trabajo del hook diagnosticar el entorno).
command -v dotnet >/dev/null 2>&1 || exit 0

out_sim=$(dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet 2>&1); rc_sim=$?
out_game=$(dotnet build Exosphere.csproj --nologo -v quiet 2>&1);                         rc_game=$?

all_out=$(printf '%s\n%s\n' "$out_sim" "$out_game")

# Limpio = ambos exit 0 Y ninguna línea reporta un número de warnings distinto de cero.
has_warnings=$(printf '%s' "$all_out" | grep -Ec '[1-9][0-9]* Warning\(s\)')

if [ "$rc_sim" -eq 0 ] && [ "$rc_game" -eq 0 ] && [ "$has_warnings" -eq 0 ]; then
  exit 0
fi

detail=$(printf '%s' "$all_out" | grep -Ei 'error|warning' | head -40)
reason=$(printf 'El build no está 0/0 (regla obligatoria de Exosphere). Corrige esto antes de terminar:\n\n%s' "$detail")

# Bloquea el Stop y reinyecta el motivo a Claude.
if command -v jq >/dev/null 2>&1; then
  jq -n --arg r "$reason" '{decision:"block", reason:$r}'
else
  esc=${reason//\\/\\\\}; esc=${esc//\"/\\\"}; esc=${esc//$'\n'/\\n}
  printf '{"decision":"block","reason":"%s"}\n' "$esc"
fi
exit 0
