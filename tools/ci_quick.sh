#!/usr/bin/env bash
# Fast inner-loop check: contracts, the three builds, and every test except the classes in
# tools/slow_tests.txt (full pad-to-entry missions and other multi-minute runs). It is not a
# substitute for tools/ci_check.sh: run the full check once before pushing a batch of
# commits; GitHub Actions also runs the full suite on every push.
#
# Usage: bash tools/ci_quick.sh [extra dotnet test filter, ANDed]
#   e.g. bash tools/ci_quick.sh "FullyQualifiedName~Apollo11"
set -euo pipefail
cd "$(dirname "$0")/.."

bash tools/contract_checks.sh >/dev/null
echo "contracts: pass"

dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet

filter=""
while IFS= read -r line; do
  [[ -z "$line" || "$line" == \#* ]] && continue
  filter+="${filter:+&}FullyQualifiedName!~${line}."
done < tools/slow_tests.txt
if [[ $# -gt 0 && -n "$1" ]]; then
  filter="${filter:+$filter&}($1)"
fi

dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo ${filter:+--filter "$filter"}
echo "ci_quick: pass (slow classes in tools/slow_tests.txt were skipped; run tools/ci_check.sh before pushing)"
