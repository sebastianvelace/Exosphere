#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEST_FILE="$ROOT_DIR/ExosphereSimulation.Tests/PerformanceAcceptanceTests.cs"
PROJECT_FILE="$ROOT_DIR/project.godot"
SIMULATION_BRIDGE="$ROOT_DIR/scripts/SimulationBridge.cs"
SYSTEMS_CONTROLLER="$ROOT_DIR/scripts/SystemsController.cs"
AUTOPILOT_CONTROLLER="$ROOT_DIR/scripts/AutopilotController.cs"
MANEUVER_EXECUTOR="$ROOT_DIR/scripts/ManeuverExecutor.cs"
EDL_CONTROLLER="$ROOT_DIR/scripts/EDLController.cs"
VISUAL_PLAYTEST="$ROOT_DIR/tools/visual_playtest.sh"
SKY_CONTROLLER="$ROOT_DIR/scripts/SkyController.cs"
MARS_TERRAIN="$ROOT_DIR/scripts/MarsTerrainController.cs"
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
require_text "$MARS_TERRAIN" "EnsureMesh" \
    "Mars terrain mesh is lazy-built on approach"
require_text "$MARS_TERRAIN" "PERF_RENDER stage=mars_terrain_build" \
    "Mars terrain build exposes its one-time render cost"
require_text "$UNIVERSE" "GetMixedPhysicsStepCap" \
    "mixed scheduler derives its cap from every eligible vessel"
require_text "$UNIVERSE" "LastMixedPhysicsStepCap" \
    "mixed scheduler exposes effective cap telemetry"
require_text "$SCHEDULER_TELEMETRY" "PhysicsSchedulerTelemetry" \
    "scheduler workload snapshot type is present"
require_text "$SCHEDULER_TELEMETRY" "DeadlineProjectedDispatches" \
    "scheduler telemetry exposes current-epoch deadline projections"
require_text "$SCHEDULER_TELEMETRY" "CatchUpRisk" \
    "scheduler telemetry exposes catch-up risk"
require_text "$UNIVERSE" "CatchUpWarningSubsteps" \
    "scheduler defines a catch-up warning threshold"
require_text "$SCHEDULER_TEST" "SchedulerTelemetryFlagsLargeCatchUpWithoutChangingSimulatedTime" \
    "large catch-up telemetry regression test is present"
require_text "$SCHEDULER_TEST" "SchedulerRejectsInvalidDeltaWithoutCorruptingClock" \
    "invalid scheduler delta regression test is present"
require_text "$SCHEDULER_TEST" "MixedSchedulerBoundsSecondaryForceSensitiveVesselAndMatchesFineTick" \
    "multi-vessel mixed-cap regression test is present"
require_text "$SCHEDULER_TEST" "SchedulerTelemetryCountsMixedWorkloadWithoutSkippingVessels" \
    "mixed workload telemetry regression test is present"
require_text "$SCHEDULER_TEST" "DeferredRailsProjectsCurrentEpochAndMatchesAlwaysCheckedReference" \
    "deferred rails equivalence regression test is present"
require_text "$SCHEDULER_TEST" "DeferredRailsCatchesUpBeforeForceSensitiveWake" \
    "deferred rails wake-up regression test is present"
require_text "$SCHEDULER_TEST" "BudgetedSchedulerConservesExactTemporalDebtAcrossTicksAndPause" \
    "budgeted scheduler debt conservation test is present"
require_text "$SCHEDULER_TEST" "ExistingTemporalDebtIsNotRescaledWhenTimeScaleChanges" \
    "time-scale debt invariance test is present"
require_text "$SCHEDULER_TEST" "AbsoluteTimeSeekClearsTemporalDebtAndRailDeadlines" \
    "absolute seek debt reset test is present"
require_text "$SCHEDULER_TEST" "UndockClearsStaleRailStateEvenWithoutSeparationImpulse" \
    "undock wake-up invalidation test is present"
require_text "$SCHEDULER_TEST" "CorruptedRailStateIsNeverClassifiedAsAnalyticWork" \
    "invalid rail state fail-safe test is present"
require_text "$SIMULATION_BRIDGE" "GetWarpPhysicsRequirements" \
    "warp-limit bridge uses one combined physics-requirements query"
require_text "$SIMULATION_BRIDGE" "LastProcessedSimulationSeconds" \
    "bridge exposes committed simulation time to gameplay systems"
require_text "$SIMULATION_BRIDGE" "AdvanceProcessedSimulation" \
    "bridge advances gameplay systems after the physics tick"
require_text "$SYSTEMS_CONTROLLER" "ProcessedSimulationSeconds" \
    "systems controller consumes committed simulation time"
require_text "$AUTOPILOT_CONTROLLER" "LastProcessedSimulationSeconds" \
    "autopilot delta-v accounting consumes committed simulation time"
require_text "$MANEUVER_EXECUTOR" "LastProcessedSimulationSeconds" \
    "maneuver delta-v accounting consumes committed simulation time"
require_text "$EDL_CONTROLLER" "LastProcessedSimulationSeconds" \
    "EDL physical timers consume committed simulation time"
require_text "$VISUAL_PLAYTEST" "scheduler_ms=" \
    "visual playtest records scheduler wall-clock telemetry"
require_text "$VISUAL_PLAYTEST" "scheduler_branch=" \
    "visual playtest records scheduler branch telemetry"
require_text "$VISUAL_PLAYTEST" "scheduler_substeps=" \
    "visual playtest records scheduler substep telemetry"
require_text "$VISUAL_PLAYTEST" "scheduler_cap=" \
    "visual playtest records scheduler cap telemetry"
require_text "$VISUAL_PLAYTEST" "scheduler_simulated=" \
    "visual playtest records simulated-seconds telemetry"
require_text "$VISUAL_PLAYTEST" "catch_up_risk=" \
    "visual playtest records scheduler catch-up telemetry"
require_text "$VISUAL_PLAYTEST" "LastSchedulerTelemetry" \
    "visual playtest reads the authoritative scheduler snapshot"
require_text "$VISUAL_PLAYTEST" "PERF_SCHEDULER schema=2" \
    "visual playtest records extended scheduler telemetry"
require_text "$VISUAL_PLAYTEST" "PERF_SCHEDULER_CANDIDATE schema=1" \
    "visual playtest records deferred-candidate telemetry"
require_text "$VISUAL_PLAYTEST" "CandidateDeferredSkips" \
    "visual playtest exposes candidate skip count"
require_text "$PROJECT_FILE" "deferred_physics_candidate_enabled=false" \
    "official runtime keeps deferred-physics candidate disabled"
require_text "$VISUAL_PLAYTEST" "SkipReason" \
    "visual playtest records scheduler skip reason"
require_text "$VISUAL_PLAYTEST" "TotalWorkDispatches" \
    "visual playtest records scheduler work totals"
require_text "$SCHEDULER_TELEMETRY" "PendingSimulationSeconds" \
    "scheduler telemetry exposes exact pending simulation debt"
require_text "$VISUAL_PLAYTEST" "pending_simulated=" \
    "visual playtest records pending simulation debt"

log_file="${PERF_ACCEPTANCE_LOG:-}"
if [[ -z "$log_file" ]]; then
    skip_gate "dynamic telemetry (set PERF_ACCEPTANCE_LOG=/path/to/playtest.log)"
elif [[ ! -f "$log_file" ]]; then
    fail_gate "dynamic telemetry log exists: $log_file"
else
    pass_gate "dynamic telemetry log exists: $log_file"

    # visual_playtest.sh intentionally keeps harness telemetry and Godot stdout in
    # separate files.  Accept either path and join the companion automatically so the
    # dynamic gate validates startup/LUT markers and PERF_FRAME records together.
    startup_log="$log_file"
    frame_log="$log_file"
    if [[ "$log_file" == *.console ]]; then
        companion_log="${log_file%.console}"
        if [[ -f "$companion_log" ]]; then
            frame_log="$companion_log"
        fi
    elif [[ -f "${log_file}.console" ]]; then
        startup_log="${log_file}.console"
    fi

    if rg -qi '\b(nan|inf|-inf|infinity|-infinity)\b' "$log_file" "$startup_log" "$frame_log"; then
        fail_gate "telemetry contains NaN/inf"
    else
        pass_gate "telemetry contains no NaN/inf tokens"
    fi

    if rg -q 'PERF_STARTUP phase=simulation_loaded ms=[0-9]+(\.[0-9]+)?' "$startup_log"; then
        pass_gate "simulation-loaded startup marker is present and finite"
    else
        fail_gate "simulation-loaded startup marker is present and finite"
    fi

    startup_budget_ms="${PERF_STARTUP_BUDGET_MS:-5000}"
    startup_ms="$(sed -n 's/.*PERF_STARTUP phase=simulation_loaded ms=\([0-9.]*\).*/\1/p' "$startup_log" | tail -n 1)"
    if [[ -n "$startup_ms" ]] && awk -v actual="$startup_ms" -v budget="$startup_budget_ms" 'BEGIN { exit !(actual <= budget) }'; then
        pass_gate "simulation-loaded startup <= ${startup_budget_ms} ms (${startup_ms} ms)"
    else
        fail_gate "simulation-loaded startup <= ${startup_budget_ms} ms (${startup_ms:-missing} ms)"
    fi

    if rg -q 'PERF_ATMOS .*stage=queued worker=true' "$startup_log"; then
        pass_gate "atmosphere LUT build is queued asynchronously"
    else
        fail_gate "atmosphere LUT build is queued asynchronously"
    fi

    frame_lines="$(rg 'PERF_FRAME .*frame_ms=[0-9]+(\.[0-9]+)?' "$frame_log" || true)"
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

        perf_frame_lines="$(rg '^PERF_FRAME ' "$frame_log" || true)"
        perf_frame_count="$(printf '%s\n' "$perf_frame_lines" | sed '/^$/d' | wc -l | tr -d ' ')"
        scheduler_pattern='^PERF_FRAME frame=[0-9]+ frame_ms=[0-9]+(\.[0-9]+)? scheduler_ms=[0-9]+(\.[0-9]+)? scheduler_branch=(None|FullPhysics|Mixed|Rails) scheduler_substeps=[0-9]+ scheduler_cap=[0-9]+(\.[0-9]+)? scheduler_simulated=[0-9]+(\.[0-9]+)? catch_up_risk=(true|false) source=process_callback$'
        valid_scheduler_lines="$(printf '%s\n' "$perf_frame_lines" | rg "$scheduler_pattern" || true)"
        valid_scheduler_frame_count="$(printf '%s\n' "$valid_scheduler_lines" | sed '/^$/d' | wc -l | tr -d ' ')"
        if [[ "$perf_frame_count" == "0" ]]; then
            skip_gate "scheduler frame telemetry (no PERF_FRAME lines in supplied log)"
        elif [[ "$valid_scheduler_frame_count" == "$perf_frame_count" ]]; then
            pass_gate "all PERF_FRAME scheduler fields are finite and schema-valid (${valid_scheduler_frame_count}/${perf_frame_count})"
            scheduler_invariant_failures="$(printf '%s\n' "$perf_frame_lines" | awk '
                {
                    branch = ""; substeps = -1; cap = -1; simulated = -1; risk = "";
                    for (i = 1; i <= NF; i++) {
                        split($i, pair, "=");
                        if (pair[1] == "scheduler_branch") branch = pair[2];
                        if (pair[1] == "scheduler_substeps") substeps = pair[2] + 0;
                        if (pair[1] == "scheduler_cap") cap = pair[2] + 0;
                        if (pair[1] == "scheduler_simulated") simulated = pair[2] + 0;
                        if (pair[1] == "catch_up_risk") risk = pair[2];
                    }
                    if (branch == "None" && (substeps != 0 || cap != 0 || simulated != 0)) bad++;
                    if (branch != "None" && (substeps <= 0 || cap <= 0 || simulated <= 0)) bad++;
                    if (risk == "true" && substeps < 128) bad++;
                }
                END { print bad + 0 }
            ' )"
            if [[ "$scheduler_invariant_failures" == "0" ]]; then
                pass_gate "PERF_FRAME scheduler branch/substep invariants hold"
            else
                fail_gate "PERF_FRAME scheduler branch/substep invariants hold (${scheduler_invariant_failures} violations)"
            fi
        else
            fail_gate "all PERF_FRAME scheduler fields are finite and schema-valid (${valid_scheduler_frame_count}/${perf_frame_count})"
        fi

        extended_scheduler_lines="$(rg '^PERF_SCHEDULER ' "$frame_log" || true)"
        extended_scheduler_count="$(printf '%s\n' "$extended_scheduler_lines" | sed '/^$/d' | wc -l | tr -d ' ')"
        extended_scheduler_pattern_v1='^PERF_SCHEDULER schema=1 frame=[0-9]+ initialized=(true|false) skip_reason=(NotInitialized|None|Paused|InvalidDelta|InvalidTimeScale) branch=(None|FullPhysics|Mixed|Rails) substeps=[0-9]+ full_physics=[0-9]+ on_rails=[0-9]+ surface_settled=[0-9]+ ground_held=[0-9]+ destroyed=[0-9]+ docked_skips=[0-9]+ rails_slices=[0-9]+ docking_constraints=[0-9]+ deadline_eligible=[0-9]+ deadline_deferred=[0-9]+ deadline_catch_up=[0-9]+ deadline_projected=[0-9]+ total_work=[0-9]+ source=process_callback$'
        extended_scheduler_pattern_v2='^PERF_SCHEDULER schema=2 frame=[0-9]+ initialized=(true|false) skip_reason=(NotInitialized|None|Paused|InvalidDelta|InvalidTimeScale) branch=(None|FullPhysics|Mixed|Rails) substeps=[0-9]+ full_physics=[0-9]+ on_rails=[0-9]+ surface_settled=[0-9]+ ground_held=[0-9]+ destroyed=[0-9]+ docked_skips=[0-9]+ rails_slices=[0-9]+ docking_constraints=[0-9]+ deadline_eligible=[0-9]+ deadline_deferred=[0-9]+ deadline_catch_up=[0-9]+ deadline_projected=[0-9]+ requested_simulated=[0-9]+(\.[0-9]+)? processed_simulated=[0-9]+(\.[0-9]+)? pending_simulated=[0-9]+(\.[0-9]+)? budget_limited=(true|false) budget_reason=(None|Disabled|SubstepLimit) total_work=[0-9]+ source=process_callback$'
        valid_extended_scheduler_lines_v1="$(printf '%s\n' "$extended_scheduler_lines" | rg "$extended_scheduler_pattern_v1" || true)"
        valid_extended_scheduler_lines_v2="$(printf '%s\n' "$extended_scheduler_lines" | rg "$extended_scheduler_pattern_v2" || true)"
        valid_extended_scheduler_count_v1="$(printf '%s\n' "$valid_extended_scheduler_lines_v1" | sed '/^$/d' | wc -l | tr -d ' ')"
        valid_extended_scheduler_count_v2="$(printf '%s\n' "$valid_extended_scheduler_lines_v2" | sed '/^$/d' | wc -l | tr -d ' ')"
        valid_extended_scheduler_count=$((valid_extended_scheduler_count_v1 + valid_extended_scheduler_count_v2))
        if [[ "$extended_scheduler_count" == "0" ]]; then
            skip_gate "extended scheduler telemetry (no PERF_SCHEDULER lines in supplied log)"
        elif [[ "$valid_extended_scheduler_count" == "$extended_scheduler_count" ]]; then
            pass_gate "all PERF_SCHEDULER fields are schema-valid (${valid_extended_scheduler_count}/${extended_scheduler_count})"
            extended_scheduler_invariant_failures="$(printf '%s\n' "$extended_scheduler_lines" | awk '
                {
                    initialized = ""; reason = ""; branch = ""; schema = "";
                    full = -1; rails = -1; settled = -1; ground = -1; destroyed = -1; total = -1;
                    requested = -1; processed = -1; pending = -1; budget_limited = ""; budget_reason = "";
                    for (i = 1; i <= NF; i++) {
                        split($i, pair, "=");
                        if (pair[1] == "schema") schema = pair[2];
                        if (pair[1] == "initialized") initialized = pair[2];
                        if (pair[1] == "skip_reason") reason = pair[2];
                        if (pair[1] == "branch") branch = pair[2];
                        if (pair[1] == "full_physics") full = pair[2] + 0;
                        if (pair[1] == "on_rails") rails = pair[2] + 0;
                        if (pair[1] == "surface_settled") settled = pair[2] + 0;
                        if (pair[1] == "ground_held") ground = pair[2] + 0;
                        if (pair[1] == "destroyed") destroyed = pair[2] + 0;
                        if (pair[1] == "requested_simulated") requested = pair[2] + 0;
                        if (pair[1] == "processed_simulated") processed = pair[2] + 0;
                        if (pair[1] == "pending_simulated") pending = pair[2] + 0;
                        if (pair[1] == "budget_limited") budget_limited = pair[2];
                        if (pair[1] == "budget_reason") budget_reason = pair[2];
                        if (pair[1] == "total_work") total = pair[2] + 0;
                    }
                    if (initialized == "false" && reason != "NotInitialized") bad++;
                    if (branch != "None" && reason != "None") bad++;
                    if (full + rails + settled + ground + destroyed != total) bad++;
                    if (schema == "2") {
                        if (requested < 0 || processed < 0 || pending < 0) bad++;
                        if (budget_limited == "true" && (budget_reason != "SubstepLimit" || pending <= 0)) bad++;
                        if (budget_limited == "false" && budget_reason == "SubstepLimit") bad++;
                    }
                }
                END { print bad + 0 }
            ' )"
            if [[ "$extended_scheduler_invariant_failures" == "0" ]]; then
                pass_gate "PERF_SCHEDULER work/skip invariants hold"
            else
                fail_gate "PERF_SCHEDULER work/skip invariants hold (${extended_scheduler_invariant_failures} violations)"
            fi
        else
            fail_gate "all PERF_SCHEDULER fields are schema-valid (${valid_extended_scheduler_count}/${extended_scheduler_count})"
        fi

        candidate_lines="$(rg '^PERF_SCHEDULER_CANDIDATE ' "$frame_log" || true)"
        candidate_count="$(printf '%s\n' "$candidate_lines" | sed '/^$/d' | wc -l | tr -d ' ')"
        candidate_pattern='^PERF_SCHEDULER_CANDIDATE schema=1 frame=[0-9]+ enabled=(true|false) deferred_skips=[0-9]+ source=process_callback$'
        valid_candidate_count="$(printf '%s\n' "$candidate_lines" | rg "$candidate_pattern" | sed '/^$/d' | wc -l | tr -d ' ')"
        if [[ "$candidate_count" == "0" ]]; then
            skip_gate "deferred-candidate telemetry (no PERF_SCHEDULER_CANDIDATE lines in supplied log)"
        elif [[ "$valid_candidate_count" == "$candidate_count" ]]; then
            pass_gate "all PERF_SCHEDULER_CANDIDATE fields are schema-valid (${valid_candidate_count}/${candidate_count})"
            candidate_invariant_failures="$(printf '%s\n' "$candidate_lines" | awk '
                {
                    enabled = ""; skips = -1;
                    for (i = 1; i <= NF; i++) {
                        split($i, pair, "=");
                        if (pair[1] == "enabled") enabled = pair[2];
                        if (pair[1] == "deferred_skips") skips = pair[2] + 0;
                    }
                    if (skips < 0 || (enabled == "false" && skips != 0)) bad++;
                }
                END { print bad + 0 }
            ' )"
            if [[ "$candidate_invariant_failures" == "0" ]]; then
                pass_gate "deferred-candidate enabled/skip invariants hold"
            else
                fail_gate "deferred-candidate enabled/skip invariants hold (${candidate_invariant_failures} violations)"
            fi
        else
            fail_gate "all PERF_SCHEDULER_CANDIDATE fields are schema-valid (${valid_candidate_count}/${candidate_count})"
        fi
    fi
fi

printf 'summary pass=%d fail=%d skip=%d\n' "$pass_count" "$fail_count" "$skip_count"
if (( fail_count > 0 )); then
    exit 1
fi
