#!/usr/bin/env bash
# Fast Flight 14 segment iteration: runs the checkpoint equivalence gate plus the descent
# and terminal-burn segments, which resume from one cached pad-to-entry flight.
# The cache lives in $TMPDIR/exosphere-flight14-checkpoints, keyed by a SHA-256 of the
# simulation assembly and data/; any physics or data change rebuilds it (one ~7 min flight).
#   bash tools/flight14_segments.sh            # gate + all segments
#   bash tools/flight14_segments.sh Landing    # gate + tests whose name contains "Landing"
set -euo pipefail
cd "$(dirname "$0")/.."
extra="${1:-}"
filter="FullyQualifiedName~Flight14EntryCheckpointTests|FullyQualifiedName~Flight14DescentTests.LoadedMission|FullyQualifiedName~Flight14LandingBurnTests"
if [[ -n "$extra" ]]; then filter="FullyQualifiedName~Flight14EntryCheckpointTests|FullyQualifiedName~${extra}"; fi
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --filter "${filter}"
