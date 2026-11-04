#!/usr/bin/env bash
# Runs the analytic and historical gates of one fidelity phase (docs/physics/fidelity_gates.md).
#   bash tools/fidelity_gate.sh F1            # every gate of F1
#   bash tools/fidelity_gate.sh F1 analytic   # only its analytic gates
set -euo pipefail
cd "$(dirname "$0")/.."
phase="${1:?usage: tools/fidelity_gate.sh PHASE [analytic|historical]}"
filter="FidelityGate=${phase}"
if [[ $# -ge 2 ]]; then filter="${filter}&GateKind=$2"; fi
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --filter "${filter}"
