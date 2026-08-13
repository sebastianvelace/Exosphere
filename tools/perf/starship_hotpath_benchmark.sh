#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT_DIR/ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj"
OUT_DIR="${OUT_DIR:-/tmp/exo_starship_hotpath}"
REPORT="$OUT_DIR/starship_hotpath.log"
mkdir -p "$OUT_DIR"

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  dotnet build "$PROJECT" --no-restore --nologo -v minimal
fi

dotnet test "$PROJECT" \
  --no-build --no-restore --nologo \
  --filter 'FullyQualifiedName~StarshipPerformanceRegressionTests' \
  --logger 'console;verbosity=detailed' > "$REPORT"
cat "$REPORT"

rg -q --fixed-strings 'Flight7Tick:' "$REPORT"
rg -q --fixed-strings 'Test Run Successful.' "$REPORT"
echo "starship_hotpath_benchmark: PASS report=$REPORT"
