#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_FILE="$ROOT_DIR/ExosphereSimulation.Tests/PerformanceAcceptanceTests.cs"
SIMULATION_BRIDGE="$ROOT_DIR/scripts/SimulationBridge.cs"
SKY_CONTROLLER="$ROOT_DIR/scripts/SkyController.cs"
EXPOSURE_CONTROLLER="$ROOT_DIR/scripts/VisualExposureController.cs"
UNIVERSE="$ROOT_DIR/ExosphereSimulation/Universe.cs"
SCHEDULER_TEST="$ROOT_DIR/ExosphereSimulation.Tests/PhysicsSchedulerPerformanceTests.cs"
SCHEDULER_TELEMETRY="$ROOT_DIR/ExosphereSimulation/PhysicsSchedulerTelemetry.cs"

pass_count=0
fail_count=0
skip_count=0

pass_gate() {
    printf 'PASS %s\n' "$1"
    pass_count=$((pass_count + 1))
}

fail_gate() {
    printf 'FAIL %s\n' "$1"
    fail_count=$((fail_count + 1))
}

skip_gate() {
    printf 'SKIP %s\n' "$1"
    skip_count=$((skip_count + 1))
}

require_text() {
    local file="$1"
    local pattern="$2"
    local description="$3"
    if rg -q --fixed-strings "$pattern" "$file"; then
        pass_gate "$description"
    else
        fail_gate "$description (missing '$pattern' in ${file#$ROOT_DIR/})"
    fi
}

printf 'Performance acceptance contract\n'
printf 'root=%s\n' "$ROOT_DIR"

if [[ -f "$TEST_FILE" ]]; then
    pass_gate "QA xUnit acceptance test file exists"
else
    fail_gate "QA xUnit acceptance test file exists"
fi

for marker in \
    "SimulationStartupLoadsFiniteWorldWithinWatchdog" \
    "ActiveNearbyAndOnRailsTiersAreMutuallyExclusive" \
    "ProgressWatchdogDetectsNoStallAndStateRemainsFinite" \
    "StarshipSizedPhysicsBurstStaysFiniteAndWithinFrameWatchdog" \
    "ShortOffRailsFlightPreservesPhysicalDirectionAndFiniteThermalState" \
    "WarpPolicyLeavesAtmosphericVesselOffRailsAndVacuumVesselOnRails"; do
    require_text "$TEST_FILE" "$marker" "xUnit gate '$marker' is present"
done

# These source-level checks make the QA commit fail closed if it is integrated without
# the runtime instrumentation/async pipeline that it is meant to gate.  They intentionally
# do not modify or depend on generated Godot artifacts.
require_text "$SIMULATION_BRIDGE" "PERF_STARTUP phase=simulation_loaded" \
    "startup telemetry exposes the simulation-loaded boundary"
require_text "$SIMULATION_BRIDGE" "PERF_STARTUP phase=starship_spawned" \
    "startup telemetry exposes Starship spawn completion"
require_text "$SKY_CONTROLLER" "Task.Run(() => BuildAtmosphereLutsCpu" \
    "atmosphere LUT CPU build is off the Godot main thread"
require_text "$SKY_CONTROLLER" "PollAtmosphereLutBuild" \
    "atmosphere LUT completion is polled on the main thread"
require_text "$EXPOSURE_CONTROLLER" "DirectTransmittanceCadenceSeconds" \
    "direct-transmittance work has an explicit cadence gate"
require_text "$UNIVERSE" "GetMixedPhysicsStepCap" \
    "mixed scheduler derives its cap from every eligible vessel"
require_text "$UNIVERSE" "LastMixedPhysicsStepCap" \
    "mixed scheduler exposes effective cap telemetry"
require_text "$SCHEDULER_TELEMETRY" "PhysicsSchedulerTelemetry" \
    "scheduler workload snapshot type is present"
require_text "$SCHEDULER_TELEMETRY" "DeadlineProjectedDispatches" \
    "scheduler telemetry exposes current-epoch deadline projections"
require_text "$SCHEDULER_TEST" "MixedSchedulerBoundsSecondaryForceSensitiveVesselAndMatchesFineTick" \
    "multi-vessel mixed-cap regression test is present"
require_text "$SCHEDULER_TEST" "SchedulerTelemetryCountsMixedWorkloadWithoutSkippingVessels" \
    "mixed workload telemetry regression test is present"
require_text "$SCHEDULER_TEST" "DeferredRailsProjectsCurrentEpochAndMatchesAlwaysCheckedReference" \
    "deferred rails equivalence regression test is present"
require_text "$SCHEDULER_TEST" "DeferredRailsCatchesUpBeforeForceSensitiveWake" \
    "deferred rails wake-up regression test is present"

log_file="${PERF_ACCEPTANCE_LOG:-}"
if [[ -z "$log_file" ]]; then
    skip_gate "dynamic telemetry (set PERF_ACCEPTANCE_LOG=/path/to/godot.log)"
elif [[ ! -f "$log_file" ]]; then
    fail_gate "dynamic telemetry log exists: $log_file"
else
    pass_gate "dynamic telemetry log exists: $log_file"

    if rg -qi '\b(nan|inf|-inf)\b' "$log_file"; then
        fail_gate "telemetry contains NaN/inf"
    else
        pass_gate "telemetry contains no NaN/inf tokens"
    fi

    if rg -q 'PERF_STARTUP phase=simulation_loaded ms=[0-9]+(\.[0-9]+)?' "$log_file"; then
        pass_gate "simulation-loaded startup marker is present and finite"
    else
        fail_gate "simulation-loaded startup marker is present and finite"
    fi

    startup_budget_ms="${PERF_STARTUP_BUDGET_MS:-5000}"
    startup_ms="$(sed -n 's/.*PERF_STARTUP phase=simulation_loaded ms=\([0-9.]*\).*/\1/p' "$log_file" | tail -n 1)"
    if [[ -n "$startup_ms" ]] && awk -v actual="$startup_ms" -v budget="$startup_budget_ms" 'BEGIN { exit !(actual <= budget) }'; then
        pass_gate "simulation-loaded startup <= ${startup_budget_ms} ms (${startup_ms} ms)"
    else
        fail_gate "simulation-loaded startup <= ${startup_budget_ms} ms (${startup_ms:-missing} ms)"
    fi

    if rg -q 'PERF_ATMOS .*stage=queued worker=true' "$log_file"; then
        pass_gate "atmosphere LUT build is queued asynchronously"
    else
        fail_gate "atmosphere LUT build is queued asynchronously"
    fi

    frame_lines="$(rg 'PERF_FRAME .*frame_ms=[0-9]+(\.[0-9]+)?' "$log_file" || true)"
    if [[ -z "$frame_lines" ]]; then
        skip_gate "frame budget (no PERF_FRAME frame_ms telemetry in supplied log)"
    else
        frame_budget_ms="${PERF_FRAME_BUDGET_MS:-50}"
        max_frame_ms="$(printf '%s\n' "$frame_lines" | sed -n 's/.*frame_ms=\([0-9.]*\).*/\1/p' | sort -nr | head -n 1)"
        if [[ -n "$max_frame_ms" ]] && awk -v actual="$max_frame_ms" -v budget="$frame_budget_ms" 'BEGIN { exit !(actual <= budget) }'; then
            pass_gate "max PERF_FRAME frame_ms <= ${frame_budget_ms} ms (${max_frame_ms} ms)"
        else
            fail_gate "max PERF_FRAME frame_ms <= ${frame_budget_ms} ms (${max_frame_ms:-missing} ms)"
        fi
    fi
fi

printf 'summary pass=%d fail=%d skip=%d\n' "$pass_count" "$fail_count" "$skip_count"
if (( fail_count > 0 )); then
    exit 1
fi
