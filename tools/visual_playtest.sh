#!/usr/bin/env bash
# State-gated pad→orbit→EDL visual play harness for Exosphere.
# Generates a temporary scripts/_PlaytestShot.cs autoload, runs Godot under xvfb,
# writes PNG milestones + telemetry, and always cleans up on exit.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
source "$ROOT/tools/lib/playtest_contracts.sh"

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
HARNESS="scripts/_PlaytestShot.cs"
OUT_DIR_SET=0
LOG_SET=0
if [[ -n "${OUT_DIR:-}" ]]; then OUT_DIR_SET=1; fi
if [[ -n "${LOG:-}" ]]; then LOG_SET=1; fi
OUT_DIR="${OUT_DIR:-/tmp/exo_play}"
LOG="${LOG:-/tmp/exo_play.log}"
CONSOLE_LOG=""
RUN_ID=""
RUN_TOKEN=""
MAX_RUNTIME_SEC="${PLAYTEST_MAX_RUNTIME_SEC:-}"
VERIFY_ONLY=0
MODE="full"
HARNESS_MODE=""
REENTRY_BELLY_FIRST=""
REENTRY_SLUG=""
VARIANT_FILE=""
VARIANT_SITE=""
VARIANT_PROFILE=""
SKIP_BUILD=0
PROJECT_BACKUP=""
APOLLO11_HARDWARE=0
OWNS_HARNESS=0

usage() {
  cat <<'EOF'
Usage: tools/visual_playtest.sh [options]

Runs the Exosphere visual playtest harness (temporary autoload, never committed).

Options:
  --smoke       Pad-only capture (~30s). Used by CI for pipeline validation.
  --falcon      Seed the Falcon 9 Block 5 / Kennedy scenario before capture.
  --new-glenn   Seed the New Glenn 7x2 / LC-36 scenario before capture.
  --mercury     Seed Freedom 7 / Mercury-Redstone 3 at LC-5.
  --friendship  Seed Friendship 7 / Mercury-Atlas 6 at LC-14.
  --gemini      Seed Gemini 8 / Titan II GLV-8 at LC-19.
  --gemini-docking  Seed and capture Gemini 8 docked to Agena 5003.
  --apollo8     Seed Apollo 8 / Saturn V AS-503 at LC-39A.
  --apollo8-lunar  Seed CSM-103 in its historical low lunar orbit.
  --apollo11    Validate Apollo 11 / AS-506 hardware at LC-39A (launch mode by default).
  --lunar-map   Seed Earth orbit and capture the Lambert TLI/LOI map dossier.
  --flight7     Seed the historical Starship Flight 7 / Starbase scenario.
  --flight12    Seed the historical Starship Flight 12 V3 / Starbase scenario.
  --ascent      Fly only pad→stable orbit with dense guidance/physics diagnostics, then exit.
  --launch      Capture ignition and early vertical liftoff, then exit.
  --ship        Stage immediately and capture powered standalone Starship in vacuum.
  --cockpit     Capture the first-person cockpit optics and interior.
  --atmosphere  Capture a deterministic day/twilight/night altitude matrix with image metrics.
  --edl         Seed a deterministic 70 km entry and verify physical flip/touchdown.
  --hotstage    Fly [G] full ascent (default Flight 7 Starship/Super Heavy) and capture the
                real hot-staging dual-thrust overlap window, gated on vessel state
                (IsHotStageOverlapping), not a frame count.
  --reentry-compare  Capture nominal belly-flop vs. forced bad-attitude (nose-first,
                tumbling) EDL, gated on PEAK_HEATING/destruction, for VFX/thermal comparison.
  --run-id ID    Isolate default artifacts as /tmp/exo_play-ID{,.log}; recommended for agents.
  --max-runtime SEC  Wall-clock budget (default: 3600 full mission, 1800 other modes).
  --verify-only  Re-run artifact/log gates without building or launching Godot.
  --out-dir DIR PNG output directory (default: /tmp/exo_play)
  --log FILE    Telemetry log path (default: /tmp/exo_play.log)
  --skip-build  Skip the simulation-library prebuild (the Godot project is still built)
  -h, --help    Show this help

Environment:
  GODOT_BIN     Path to Godot 4.6 mono binary
  PLAYTEST_MAX_RUNTIME_SEC  Same override as --max-runtime

Outputs:
  ${OUT_DIR}/exo_play_<milestone>.png
  ${OUT_DIR}/run-summary.txt
  ${LOG}         One telemetry line per captured milestone + summary

Cleanup on exit (success or failure):
  removes scripts/_PlaytestShot.cs (+ .uid), restores project.godot
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --smoke) MODE="smoke"; shift ;;
    --falcon)
      VARIANT_FILE="falcon9_block5_standard_2025.json"
      VARIANT_SITE="kennedy"
      VARIANT_PROFILE="falcon9-block5-ascent"
      shift ;;
    --new-glenn)
      VARIANT_FILE="newglenn_7x2_public_2026.json"
      VARIANT_SITE="cape_canaveral_lc36"
      VARIANT_PROFILE="newglenn-7x2-ascent"
      shift ;;
    --mercury)
      VARIANT_FILE="mercury_redstone3_freedom7_1961.json"
      VARIANT_SITE="cape_canaveral_lc5"
      VARIANT_PROFILE="mercury-redstone3-suborbital"
      shift ;;
    --friendship)
      VARIANT_FILE="mercury_atlas6_friendship7_1962.json"
      VARIANT_SITE="cape_canaveral_lc14"
      VARIANT_PROFILE="mercury-atlas6-three-orbit"
      shift ;;
    --gemini)
      VARIANT_FILE="gemini8_titan2_1966.json"
      VARIANT_SITE="cape_canaveral_lc19"
      VARIANT_PROFILE="gemini8-rendezvous-emergency-return"
      shift ;;
    --gemini-docking)
      MODE="gemini_docking"
      VARIANT_FILE="gemini8_titan2_1966.json"
      VARIANT_SITE="cape_canaveral_lc19"
      VARIANT_PROFILE="gemini8-rendezvous-emergency-return"
      shift ;;
    --apollo8)
      VARIANT_FILE="apollo8_saturn5_as503_1968.json"
      VARIANT_SITE="kennedy"
      VARIANT_PROFILE="apollo8-lunar-orbit-return"
      shift ;;
    --apollo8-lunar)
      MODE="apollo8_lunar"
      VARIANT_FILE="apollo8_saturn5_as503_1968.json"
      VARIANT_SITE="kennedy"
      VARIANT_PROFILE="apollo8-lunar-orbit-return"
      shift ;;
    --apollo11)
      VARIANT_FILE="apollo11_saturn5_as506_1969.json"
      VARIANT_SITE="kennedy"
      VARIANT_PROFILE="apollo11-lunar-landing-return"
      APOLLO11_HARDWARE=1
      shift ;;
    --lunar-map) MODE="lunar_map"; shift ;;
    --flight7)
      VARIANT_FILE="starship_flight7_block2_2025.json"
      VARIANT_SITE="starbase"
      VARIANT_PROFILE="starship-flight7-ascent"
      shift ;;
    --flight12)
      VARIANT_FILE="starship_flight12_v3_2026.json"
      VARIANT_SITE="starbase_pad2"
      VARIANT_PROFILE="starship-flight12-ascent"
      shift ;;
    --ascent) MODE="ascent"; shift ;;
    --launch) MODE="launch"; shift ;;
    --ship) MODE="ship"; shift ;;
    --cockpit) MODE="cockpit"; shift ;;
    --atmosphere) MODE="atmosphere"; shift ;;
    --edl) MODE="edl"; shift ;;
    --hotstage)
      MODE="hotstage"
      VARIANT_FILE="starship_flight7_block2_2025.json"
      VARIANT_SITE="starbase"
      VARIANT_PROFILE="starship-flight7-ascent"
      shift ;;
    --reentry-compare) MODE="reentry_compare"; shift ;;
    --run-id) RUN_ID="$2"; shift 2 ;;
    --max-runtime) MAX_RUNTIME_SEC="$2"; shift 2 ;;
    --verify-only) VERIFY_ONLY=1; shift ;;
    --out-dir) OUT_DIR="$2"; OUT_DIR_SET=1; shift 2 ;;
    --log) LOG="$2"; LOG_SET=1; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ "$MODE" == "ascent" && -z "$VARIANT_FILE" ]]; then
  VARIANT_FILE="starship_flight7_block2_2025.json"
  VARIANT_SITE="starbase"
  VARIANT_PROFILE="starship-flight7-ascent"
fi

if [[ -n "$RUN_ID" ]]; then
  if [[ ! "$RUN_ID" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "ERROR: --run-id accepts only letters, digits, dot, underscore and dash" >&2
    exit 2
  fi
  if [[ "$OUT_DIR_SET" -eq 0 ]]; then OUT_DIR="/tmp/exo_play-${RUN_ID}"; fi
  if [[ "$LOG_SET" -eq 0 ]]; then LOG="/tmp/exo_play-${RUN_ID}.log"; fi
fi
CONSOLE_LOG="${LOG}.console"
RUN_TOKEN="exo-${RUN_ID:-default}-$$"

# Apollo 11 currently ships as a dated hardware preset, not yet as the full
# historical lunar-landing profile. Keep its default visual acceptance honest:
# pad-to-liftoff only, while still allowing an explicitly requested mode.
if [[ $APOLLO11_HARDWARE -eq 1 && "$MODE" == "full" ]]; then
  MODE="launch"
fi

if [[ -z "$MAX_RUNTIME_SEC" ]]; then
  if [[ "$MODE" == "full" ]]; then
    MAX_RUNTIME_SEC=3600
  elif [[ "$MODE" == "ascent" ]]; then
    MAX_RUNTIME_SEC=1200
  else
    MAX_RUNTIME_SEC=1800
  fi
fi
if [[ ! "$MAX_RUNTIME_SEC" =~ ^[0-9]+$ ]] \
  || (( MAX_RUNTIME_SEC < 60 || MAX_RUNTIME_SEC > 7200 )); then
  echo "ERROR: --max-runtime must be an integer between 60 and 7200 seconds" >&2
  exit 2
fi

write_run_summary() {
  local status="$1"
  [[ -d "$OUT_DIR" ]] || return 0
  {
    echo "status=$status"
    echo "mode=$MODE"
    echo "run_id=${RUN_ID:-default}"
    echo "log=$LOG"
    echo "console_log=$CONSOLE_LOG"
    echo "artifacts=$OUT_DIR"
    if [[ -f "$LOG" ]]; then
      echo "milestones=$(awk '/^CAPTURE / { printf "%s%s", separator, $2; separator="," } END { print "" }' "$LOG")"
      grep -aE '^(SUMMARY|FAIL|GAP|FALLBACK) ' "$LOG" | tail -20 || true
      grep -aE '^(ASCENT_METRICS|TRANSITION_ASCENT) ' "$LOG" | tail -8 || true
      grep -a '^TRACE_ASCENT ' "$LOG" | tail -1 || true
      grep -aE 'failures=[^ ]*[A-Z][A-Z_]+:[1-9]' "$LOG" | tail -1 || true
    fi
  } > "$OUT_DIR/run-summary.txt"
}

print_failure_diagnostics() {
  echo "visual_playtest: diagnostics mode=$MODE out=$OUT_DIR log=$LOG" >&2
  if [[ -f "$LOG" ]]; then
    echo "visual_playtest: last state evidence:" >&2
    grep -aE '^(CAPTURE|TRACE_ASCENT|TRANSITION_ASCENT|ASCENT_METRICS|TRACE_FULL|TRACE |FAIL|GAP|FALLBACK|SUMMARY)' "$LOG" \
      | tail -25 >&2 || true
  else
    echo "visual_playtest: telemetry log was not created" >&2
  fi
  if [[ -s "$CONSOLE_LOG" ]]; then
    echo "visual_playtest: last Godot console lines:" >&2
    tail -12 "$CONSOLE_LOG" >&2 || true
  fi
  if [[ -d "$OUT_DIR" ]]; then
    local captures
    captures="$(find "$OUT_DIR" -maxdepth 1 -type f -name 'exo_play_*.png' \
      -printf '%f\n' 2>/dev/null | sort | paste -sd, -)"
    echo "visual_playtest: captures=${captures:-none}" >&2
  fi
}

cleanup() {
  local ec=$?
  set +e
  if [[ $ec -eq 0 ]]; then
    write_run_summary "PASS"
  else
    write_run_summary "FAIL"
    print_failure_diagnostics
  fi
  if [[ "$OWNS_HARNESS" -eq 1 ]]; then
    rm -f "$HARNESS" "${HARNESS}.uid" 2>/dev/null || true
    if [[ -n "$PROJECT_BACKUP" && -f "$PROJECT_BACKUP" ]]; then
      cp "$PROJECT_BACKUP" project.godot
      rm -f "$PROJECT_BACKUP"
    fi
  fi
  if [[ $ec -ne 0 ]]; then
    if [[ "$OWNS_HARNESS" -eq 1 ]]; then
      echo "visual_playtest: failed (exit $ec). Owner resources cleaned; project.godot restored; see $OUT_DIR/run-summary.txt." >&2
    else
      echo "visual_playtest: failed (exit $ec) before acquiring harness ownership; see $OUT_DIR/run-summary.txt." >&2
    fi
  fi
  exit "$ec"
}
trap cleanup EXIT INT TERM

if [[ ! -x "$GODOT" ]]; then
  echo "ERROR: Godot not found at $GODOT (set GODOT_BIN)" >&2
  exit 1
fi
if ! command -v xvfb-run >/dev/null 2>&1; then
  echo "ERROR: xvfb-run not found (install xvfb)" >&2
  exit 1
fi

register_autoload() {
  if grep -q 'PlaytestShot=' project.godot 2>/dev/null; then
    return
  fi
  PROJECT_BACKUP="$(mktemp /tmp/exo_project_godot.XXXXXX)"
  cp project.godot "$PROJECT_BACKUP"
  if grep -q '^\[autoload\]' project.godot; then
    sed -i '/^\[autoload\]/a PlaytestShot="*res://scripts/_PlaytestShot.cs"' project.godot
  else
    cat >> project.godot <<'EOF'

[autoload]

PlaytestShot="*res://scripts/_PlaytestShot.cs"
EOF
  fi
}

write_harness() {
  mkdir -p "$(dirname "$HARNESS")"
  cat > "$HARNESS" <<CS
namespace Exosphere.Game;

using Godot;
using System;
using System.IO;
using System.Linq;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Propulsion;

/// <summary>
/// Temporary visual playtest autoload — generated by tools/visual_playtest.sh; never commit.
/// </summary>
public partial class _PlaytestShot : Node
{
    const double FluxPeak = 6.0e5;
    const int SettleFrames = 4;
    const double MaxRuntimeSec = ${MAX_RUNTIME_SEC}.0;
    const double AscentFallbackSec = 720.0;

    readonly string _mode;
    readonly string _outDir;

    StreamWriter _log = null!;
    double _t0;
    int _frame;
    int _readyFrames;
    int _settleLeft;
    string? _pendingSlug;

    bool _pad, _liftoff, _maxq, _separation, _orbit, _orbitBeauty;
    bool _entry, _peak, _retro, _landed;
    bool _ascentEngaged, _deorbitStarted, _deorbitDone, _ascentFallbackUsed;
    bool _beautyJumped;
    int _beautyWaitFrames;
    bool _edlSeeded, _flipComplete, _shipSeeded;
    double _edlScenarioStart, _retroStart = -1.0, _nextEdlTelemetry;
    double _nextFullTelemetry;
    double _lastApproachSpeed = double.NaN;
    bool _finished;
    bool _authorized;

    // ── ascent diagnostics ─────────────────────────────────────────────────
    double _nextAscentTelemetry;
    double _insertStartedAt = double.NaN;
    double _minimumInsertionVSpeed = double.PositiveInfinity;
    double _maximumInsertionDescent = 0.0;
    string _lastGuidancePhase = "Unavailable";
    bool _insertObserved;
    int _ascentTraceCount;
    double _lastProgressAt = double.NaN;
    double _lastProgressAltitude = double.NaN;
    double _lastProgressSpeed = double.NaN;
    double _lastProgressPeriapsis = double.NaN;

    // ── hotstage mode ────────────────────────────────────────────────────
    bool _hotstage;

    // ── reentry_variant mode (one attitude per process; --reentry-compare drives two
    // separate Godot launches — see tools/visual_playtest.sh orchestration below) ──────
    bool _reentrySeeded, _reentryQueued;
    double _reentryScenarioStart;

    // Atmosphere acceptance is deliberately state-seeded instead of flown.  This makes
    // every altitude/solar-elevation pair reproducible and keeps the matrix fast enough
    // to run while tuning the scattering shader.  Cockpit cases are last because the
    // public camera API intentionally only enters (rather than exits) IVA mode.
    readonly (string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit)[] _atmosCases =
    {
        ("ground_day",       20.0,  45.0, false),
        ("ground_sunrise",   20.0,  -1.0, false),
        ("ground_sunset",    20.0,   1.0, false),
        ("ground_night",     20.0, -35.0, false),
        ("10km_day",     10_000.0,  35.0, false),
        ("30km_day",     30_000.0,  35.0, false),
        ("70km_day",     70_000.0,  35.0, false),
        ("120km_day",   120_000.0,  35.0, false),
        ("400km_day",   400_000.0,  35.0, false),
        ("10km_night",   10_000.0, -35.0, false),
        ("30km_night",   30_000.0, -35.0, false),
        ("70km_night",   70_000.0, -35.0, false),
        ("120km_night", 120_000.0, -35.0, false),
        ("400km_night", 400_000.0, -35.0, false),
        ("cockpit_120km_day",   120_000.0,  35.0, true),
        ("cockpit_120km_night", 120_000.0, -35.0, true),
    };
    const int AtmosphereMinimumSettleFrames = 8;
    // Incremental Sky updates one cubemap face per frame.  The atmospheric shader is
    // intentionally expensive on llvmpipe, so a 1.2 s fixed delay could capture a
    // half-updated (black or white) face after a solar-elevation change.  Wait long
    // enough for a complete six-face refresh before judging the optics.
    const double AtmosphereSettleSeconds = 6.0;
    const double AtmosphereMaximumSettleSeconds = 45.0;
    const int AtmosphereExposureStableFrames = 4;
    const double AtmosphereExposureRateTolerance = 0.015;
    int _atmosIndex = -1;
    Vector3d _atmosUp = Vector3d.Up, _atmosLook = Vector3d.Forward;
    double _atmosFrameSeconds, _atmosMaxFrameSeconds;
    int _atmosPerfFrames, _atmosSlowFrames;
    double _atmosPreviousExposure = -1.0;
    int _atmosExposureStableFrames;

    public _PlaytestShot()
    {
        _mode = "${HARNESS_MODE:-$MODE}";
        _outDir = "${OUT_DIR}";
    }

    public override void _Ready()
    {
        // project.godot temporarily exposes this autoload to every process using the
        // checkout. Only the Godot child launched by this script receives the token;
        // editors or manually running games must never touch this run's artifacts.
        if (System.Environment.GetEnvironmentVariable("EXOSPHERE_PLAYTEST_TOKEN")
            != "${RUN_TOKEN}")
        {
            ProcessMode = ProcessModeEnum.Disabled;
            QueueFree();
            return;
        }
        _authorized = true;

        if ("${VARIANT_FILE}".Length > 0)
        {
            string data = ProjectSettings.GlobalizePath("res://data");
            var catalog = PartCatalog.LoadFromDirectory(
                Path.Combine(data, "parts"));
            var variant = VehicleVariantDefinition.LoadFromJson(
                Path.Combine(data, "vehicles", "${VARIANT_FILE}"));
            var craft = variant.Build(catalog).ToCraftDocument(variant.Name);
            craft.VehicleVariantId = variant.Id;
            CraftLaunchRequest.Set(new LaunchIntent
            {
                Mode = "scenario",
                VehicleVariantId = variant.Id,
                LaunchSiteId = "${VARIANT_SITE}",
                FlightProfileId = "${VARIANT_PROFILE}",
                Craft = craft,
            });
        }
        Directory.CreateDirectory(_outDir);
        _log = new StreamWriter("${LOG}", false);
        _log.WriteLine($"=== Exosphere visual playtest {DateTime.UtcNow:O} mode={_mode} ===");
        _log.Flush();
        _t0 = Time.GetTicksMsec() / 1000.0;
        ProcessMode = ProcessModeEnum.Always;
        // Controllers run at priority 100. Diagnostics run later so their requested
        // bounded RK4 x2 acceleration is not immediately overwritten back to real time.
        ProcessPriority = 200;
    }

    public override void _Process(double delta)
    {
        if (!_authorized || _finished) return;
        _frame++;

        double elapsed = Time.GetTicksMsec() / 1000.0 - _t0;
        if (elapsed > MaxRuntimeSec)
        {
            _log.WriteLine(
                $"FAIL wall-clock timeout elapsed={elapsed:F0}s budget={MaxRuntimeSec:F0}s " +
                $"mode={_mode}; rerun with --max-runtime or PLAYTEST_MAX_RUNTIME_SEC only " +
                "after confirming TRACE evidence still shows physical progress");
            _log.Flush();
            Finish("TIMEOUT");
            return;
        }

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || vessel == null || universe == null)
        {
            if (_frame > 6000) Finish("NO_BRIDGE");
            return;
        }

        _readyFrames++;
        var body = universe.GetDominantBody(vessel.Position);
        if (body == null) return;

        double alt = vessel.GetAltitude(body);
        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        Vector3d up = (vessel.Position - body.Position).Normalized;
        double spd = surfVel.Magnitude;
        double vSpeed = surfVel.Dot(up);
        double q = vessel.GetDynamicPressure(body);
        if (!vessel.IsSurfaceSettled && alt < 100.0)
            _lastApproachSpeed = spd;
        double mass = System.Math.Max(vessel.TotalMass, 1.0);
        Vector3d nonGrav = (vessel.ComputeThrust(body) + vessel.ComputeDrag(body)) / mass;
        double g = nonGrav.Magnitude / 9.80665;
        var mission = MissionManager.Instance;
        string phase = mission?.Phase.ToString() ?? "UNKNOWN";
        double maxT = vessel.Parts.Parts.Count > 0 ? vessel.Parts.Parts.Max(p => p.Temperature) : 0.0;
        double heatRatio = vessel.Parts.Parts.Count > 0 ? vessel.Parts.Parts.Max(p => p.ThermalRatio) : 0.0;
        double density = body.GetAtmosphericDensity(vessel.Position);
        double flux = ThermalModel.ComputeHeatFlux(density, spd);
        double fluxRatio = flux / FluxPeak;

        TryCapturePending();

        if (_mode == "atmosphere")
        {
            ProcessAtmosphereMatrix(delta, bridge, vessel, universe, body);
            return;
        }

        if (_mode == "cockpit")
        {
            CameraController.Instance?.EnterCockpitView();
            if (!_shipSeeded && _readyFrames >= 45)
            {
                bridge.TriggerStaging();
                bridge.JumpToOrbit(118_000.0);
                bridge.SetThrottle(0.35);
                _shipSeeded = true;
                return;
            }
            if (_shipSeeded && _pendingSlug == null && _readyFrames >= 110 && !_orbitBeauty)
            {
                QueueCapture("cockpit");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("COCKPIT_OK");
            return;
        }

        if (_mode == "lunar_map")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                bridge.JumpToOrbit(200_000.0);
                var earth = universe.GetBody("earth")!;
                var moon = universe.GetBody("moon")!;
                var moonOrbit = moon.OrbitalElements!;
                var parking = new OrbitalElements
                {
                    SemiMajorAxis = earth.Radius + 200_000.0,
                    Eccentricity = 0.0,
                    Inclination = moonOrbit.Inclination,
                    LongitudeOfAscendingNode = moonOrbit.LongitudeOfAscendingNode,
                    ArgumentOfPeriapsis = 0.0,
                    MeanAnomalyAtEpoch = 0.0,
                    Epoch = universe.CurrentTime,
                    ReferenceBodyId = earth.Id,
                };
                var (parkingPosition, parkingVelocity) =
                    parking.GetStateAtTime(universe.CurrentTime, earth.GM);
                vessel.Position = earth.Position + parkingPosition;
                vessel.Velocity = earth.Velocity + parkingVelocity;
                vessel.ReferenceBodyId = earth.Id;
                vessel.IsOnRails = false;
                vessel.OrbitalState = null;
                var map = MapViewController.Instance;
                if (map == null)
                {
                    Finish("LUNAR_MAP_MISSING");
                    return;
                }
                if (!map.Visible) map.ToggleVisible();
                var node = map.SelectTransferTarget("moon");
                var encounter = TransferPlanner.Instance?.Encounter;
                if (node == null || encounter == null)
                {
                    _log.WriteLine(
                        $"LUNAR_MAP_FAIL error={TransferPlanner.Instance?.LastPlanningError ?? "unknown"}");
                    _log.Flush();
                    Finish("LUNAR_PLAN_FAILED");
                    return;
                }
                _log.WriteLine(
                    $"LUNAR_MAP model={node.TransferKind} encounter={encounter.Value.HasEncounter} " +
                    $"tli={node.DvMagnitude:F1} loi={node.SecondBurnDv:F1} " +
                    $"pe={node.PredictedPeriapsisAltitude:F1} " +
                    $"tBurn={node.BurnTime - universe.CurrentTime:F1}");
                _log.Flush();
                _shipSeeded = true;
                _readyFrames = 0;
                return;
            }
            if (_shipSeeded && _readyFrames >= 90
                && !_orbitBeauty && _pendingSlug == null)
            {
                QueueCapture("lunar_transfer_map");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("LUNAR_MAP_OK");
            return;
        }

        if (_mode == "ship")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                bridge.TriggerStaging();
                bridge.JumpToOrbit(118_000.0);
                bridge.SetThrottle(1.0);
                _shipSeeded = true;
                return;
            }
            if (_shipSeeded && _pendingSlug == null && _readyFrames >= 110 && !_orbitBeauty)
            {
                QueueCapture("ship_vacuum");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("SHIP_OK");
            return;
        }

        if (_mode == "smoke")
        {
            if (!_pad && _readyFrames >= 45)
            {
                QueueCapture("pad");
                _pad = true;
            }
            if (_pad && _pendingSlug == null)
                Finish("SMOKE_OK");
            return;
        }

        if (_mode == "edl")
        {
            ProcessEdlVerification(bridge, vessel, universe, body, mission, alt, surfVel, phase);
            return;
        }

        if (_mode == "reentry_variant")
        {
            ProcessReentryVariant(bridge, universe, mission);
            return;
        }

        if (_mode == "gemini_docking")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                bridge.TriggerStaging();
                bridge.TriggerStaging();
                vessel = bridge.ActiveVessel!;
                var target = bridge.EnsureGemini8AgenaTarget()!;
                var earth = universe.GetBody("earth")!;
                var sun = universe.GetBody("sun");
                var dockingUp = sun != null
                    ? (sun.Position - earth.Position).Normalized
                    : (vessel.Position - earth.Position).Normalized;
                var tangent =
                    earth.RotationAxis.Cross(dockingUp).Normalized;
                double radius = earth.Radius + 298_730.0;
                var position = earth.Position + dockingUp * radius;
                var velocity = earth.Velocity
                    + tangent * System.Math.Sqrt(earth.GM / radius);
                var orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
                var axis = orientation.Rotate(Vector3d.Up);
                target.Position = position;
                target.Velocity = velocity;
                target.Orientation = orientation;
                target.ReferenceBodyId = earth.Id;
                target.IsOnRails = false;
                target.OrbitalState = null;
                vessel.Position = position - axis * 1.075;
                vessel.Velocity = velocity + axis * 0.10;
                vessel.Orientation = orientation;
                vessel.ReleaseGroundHold();
                vessel.ReferenceBodyId = earth.Id;
                vessel.IsOnRails = false;
                vessel.OrbitalState = null;
                string geminiPort = vessel.Parts.Parts.Single(part =>
                    part.Definition.HasVehicleRole(
                        "gemini_docking_port")).InstanceId;
                string targetPort = target.Parts.Parts.Single(part =>
                    part.Definition.HasVehicleRole(
                        "agena_target_docking_port")).InstanceId;
                var result = universe.TryDock(
                    vessel.Id, geminiPort,
                    target.Id, targetPort,
                    "gemini8-agena-docking");
                if (!result.Succeeded)
                    throw new InvalidOperationException(
                        $"Gemini visual docking failed: {result.Failure}");
                vessel.SASEnabled = false;
                target.SASEnabled = false;
                vessel.AngularVelocity =
                    axis * (20.0 * System.Math.PI / 180.0);
                MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
                CameraController.Instance?.EnterShipChaseView();
                _shipSeeded = true;
                _readyFrames = 0;
                return;
            }
            if (_shipSeeded && _readyFrames >= 75
                && !_orbitBeauty && _pendingSlug == null)
            {
                QueueCapture("gemini_docked_anomaly");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("GEMINI_DOCKING_OK");
            return;
        }

        if (_mode == "apollo8_lunar")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                string? lesId = vessel.Parts.Parts.FirstOrDefault(part =>
                    part.Definition.HasVehicleRole(
                        "launch_escape_system"))?.InstanceId;
                if (lesId != null)
                    bridge.DeployPartAsVessel(
                        lesId,
                        "Apollo 8 Launch Escape System",
                        new Vector3d(0.0, 1.0, 0.0));
                bridge.TriggerStaging();
                bridge.TriggerStaging();
                bridge.TriggerStaging();
                vessel = bridge.ActiveVessel!;
                var moon = universe.GetBody("moon")!;
                var sun = universe.GetBody("sun");
                Vector3d lunarUp = sun != null
                    ? (sun.Position - moon.Position).Normalized
                    : Vector3d.Right;
                Vector3d tangent =
                    moon.RotationAxis.Cross(lunarUp).Normalized;
                double radius = moon.Radius
                    + Apollo8FlightProfile.CircularLunarOrbitAltitudeM;
                vessel.Position = moon.Position + lunarUp * radius;
                vessel.Velocity = moon.Velocity
                    + tangent * System.Math.Sqrt(moon.GM / radius);
                vessel.Orientation =
                    Quaterniond.FromTo(Vector3d.Up, tangent);
                vessel.AngularVelocity = Vector3d.Zero;
                vessel.ReleaseGroundHold();
                vessel.ReferenceBodyId = moon.Id;
                vessel.IsOnRails = false;
                vessel.OrbitalState = null;
                MissionManager.Instance?.EnterPhase(
                    MissionPhase.LUNAR_ORBIT);
                CameraController.Instance?.EnterShipChaseView();
                _shipSeeded = true;
                _readyFrames = 0;
                return;
            }
            if (_shipSeeded && _readyFrames >= 75
                && !_orbitBeauty && _pendingSlug == null)
            {
                QueueCapture("apollo8_lunar_orbit");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("APOLLO8_LUNAR_OK");
            return;
        }

        // ── Pad ───────────────────────────────────────────────────────────────
        if (!_pad && _readyFrames >= 45 && vessel.IsGroundHeld && alt < 40.0)
        {
            QueueCapture("pad");
            _pad = true;
        }

        // ── Ascent autopilot ──────────────────────────────────────────────────
        if (_pad && !_ascentEngaged
            && bridge.ActiveFlightProfileId is
                "mercury-redstone3-suborbital"
                or "mercury-atlas6-three-orbit"
                or "gemini8-rendezvous-emergency-return"
                or "apollo8-lunar-orbit-return"
            && MissionManager.Instance != null)
        {
            MissionManager.Instance.StartCountdown();
            _ascentEngaged = true;
            _log.WriteLine(
                $"ACTION start {bridge.ActiveFlightProfileId} historical countdown");
            _log.Flush();
        }
        else if (_pad && !_ascentEngaged && AscentController.Instance != null)
        {
            AscentController.Instance.Engage();
            _ascentEngaged = true;
            _log.WriteLine("ACTION engage AscentController [G] autopilot");
            _log.Flush();
        }

        if (_mode is ("full" or "ascent")
            && _separation && _pendingSlug == null && !_orbit)
        {
            // Bounded RK4 acceleration for the long upper-stage insertion. This harness
            // runs after guidance, so the request survives into the next physics tick.
            bridge.SetTimeScale(2.0);
        }

        if (_ascentEngaged && !_orbit
            && _mode is ("full" or "ascent" or "hotstage"))
        {
            MonitorAscent(vessel, body, universe, phase, alt, spd, vSpeed, q, g);
            if (_finished) return;
        }

        // ── Hot-staging: gate on the real dual-thrust overlap state (booster still
        // attached, Ship engines already lit), not on the SEPARATION phase — that phase
        // only fires once mechanical staging completes, i.e. *after* the overlap window.
        if (_mode is "full" or "ascent" or "hotstage"
            && !_hotstage
            && _pendingSlug == null
            && bridge.ActiveVessel?.IsHotStageOverlapping == true)
        {
            QueueCapture("hotstage");
            _hotstage = true;
            _log.WriteLine($"ACTION hot-stage overlap detected phase={phase} alt={alt:F0} spd={spd:F0}");
            _log.Flush();
        }
        if (_mode == "hotstage" && _hotstage && _pendingSlug == null)
        {
            Finish("HOTSTAGE_OK");
            return;
        }

        if (!_liftoff && alt is >= 80.0 and <= 350.0 &&
            mission?.Phase is MissionPhase.LIFTOFF or MissionPhase.ASCENT_SH or MissionPhase.IGNITION)
        {
            QueueCapture("liftoff");
            _liftoff = true;
        }

        if (_mode == "launch" && _liftoff && _pendingSlug == null)
        {
            Finish("LAUNCH_OK");
            return;
        }

        if (!_maxq && mission?.Phase == MissionPhase.MAX_Q)
        {
            QueueCapture("maxq");
            _maxq = true;
        }

        if (!_separation
            && _pendingSlug == null
            && mission?.Phase is MissionPhase.SEPARATION or MissionPhase.MECO or MissionPhase.ASCENT_SHIP)
        {
            QueueCapture("separation");
            _separation = true;
        }

        if (!_orbit && mission?.Phase == MissionPhase.ORBIT)
        {
            QueueCapture("orbit");
            _orbit = true;
        }

        if (_mode == "ascent" && _orbit && _pendingSlug == null)
        {
            Finish("ASCENT_ORBIT_OK");
            return;
        }

        // Fallback: if ascent takes too long, teleport to orbit for downstream milestones.
        if (_mode == "full"
            && !_orbit && !_ascentFallbackUsed && elapsed > AscentFallbackSec &&
            mission?.Phase is MissionPhase.PRE_LAUNCH or MissionPhase.COUNTDOWN or MissionPhase.IGNITION
                or MissionPhase.LIFTOFF or MissionPhase.ASCENT_SH or MissionPhase.MAX_Q or MissionPhase.MECO
                or MissionPhase.SEPARATION or MissionPhase.ASCENT_SHIP)
        {
            bridge.JumpToOrbit(200_000.0);
            _ascentFallbackUsed = true;
            _orbit = true;
            QueueCapture("orbit");
            _log.WriteLine("FALLBACK JumpToOrbit(200km) — ascent did not reach ORBIT in time");
            _log.Flush();
        }
        // ── Orbit beauty shot ─────────────────────────────────────────────────
        if (_orbit && !_beautyJumped && _pendingSlug == null)
        {
            bridge.JumpToOrbit(250_000.0);
            bridge.SetTimeScale(1.0);
            _beautyJumped = true;
            _beautyWaitFrames = 60;
            _deorbitStarted = false;
            _deorbitDone = false;
        }

        if (_beautyJumped && !_orbitBeauty && _beautyWaitFrames > 0)
            _beautyWaitFrames--;

        if (_beautyJumped && !_orbitBeauty && _beautyWaitFrames == 0 && _pendingSlug == null)
        {
            QueueCapture("orbit_beauty");
            _orbitBeauty = true;
        }

        // ── Deorbit → EDL (best effort) ───────────────────────────────────────
        if (_orbitBeauty && _pendingSlug == null && !_landed && !vessel.IsDestroyed)
        {
            ProcessDeorbit(bridge, vessel, universe, body, mission, alt);
            LogFullMissionProgress(vessel, body, universe, phase);
        }

        if (!_entry && mission?.Phase == MissionPhase.ENTRY)
        {
            QueueCapture("entry");
            _entry = true;
        }

        if (!_peak && mission?.Phase == MissionPhase.PEAK_HEATING)
        {
            QueueCapture("peak_heating");
            _peak = true;
        }

        if (!_retro && mission?.Phase == MissionPhase.RETRO_BURN)
        {
            QueueCapture("retro_burn");
            _retro = true;
        }

        if (!_landed && mission?.Phase == MissionPhase.LANDED)
        {
            QueueCapture("touchdown");
            _landed = true;
        }

        if (vessel.IsDestroyed)
        {
            _log.WriteLine($"FAIL vessel destroyed phase={phase} alt={alt:F0} spd={spd:F0} heatRatio={heatRatio:F3} maxT={maxT:F0}");
            _log.Flush();
            Finish("CRASHED");
            return;
        }

        if (_landed && _pendingSlug == null)
        {
            Finish("LANDED");
            return;
        }

        // End when robust milestones done and EDL did not activate in time.
        if (_orbitBeauty && _pendingSlug == null && !_landed && elapsed > 720.0 && !_entry)
        {
            _log.WriteLine("GAP deorbit→EDL: no ENTRY phase reached within 720s (see PLAN_PLAYTEST.md milestone 7)");
            _log.Flush();
            Finish("EDL_GAP");
        }
    }

    private void ProcessEdlVerification(SimulationBridge bridge, Vessel vessel, Universe universe,
        CelestialBody body, MissionManager? mission, double alt, Vector3d surfVel, string phase)
    {
        if (!_edlSeeded)
        {
            if (_readyFrames < 30 || EDLController.Instance == null) return;

            // Exercise the exact public entry point used by the HUD button/[R], rather than
            // maintaining a second private seed that could silently drift from gameplay.
            if (!bridge.BeginReentryDemonstration())
            {
                _log.WriteLine("GAP HUD reentry demonstration entry point refused the scenario");
                Finish("EDL_DEMO_START_FAILED");
                return;
            }
            vessel = bridge.ActiveVessel!;
            body = universe.Bodies.First(b => b.Name == "Earth");
            bridge.SetTimeScale(3.0); // verification speed only; the HUD button starts at x1

            _edlSeeded = true;
            _edlScenarioStart = universe.CurrentTime;
            _log.WriteLine("ACTION seeded deterministic EDL alt=70000m airspeed=1804m/s reserve=12%");
            _log.Flush();
            return;
        }

        double simElapsed = universe.CurrentTime - _edlScenarioStart;
        if (simElapsed >= _nextEdlTelemetry)
        {
            Vector3d up = (vessel.Position - body.Position).Normalized;
            double vUp = surfVel.Dot(up);
            double horizontal = (surfVel - up * vUp).Magnitude;
            var cluster = vessel.Parts.Parts.FirstOrDefault(
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("ship_engines"));
            double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up);
            int contacts = vessel.LastSurfaceContact?.ContactCount ?? 0;
            double maxStroke = vessel.LastSurfaceContact?.Points.Max(p => p.PenetrationM) ?? 0.0;
            double peakLegLoad = vessel.LastSurfaceContact?.Points.Max(p => p.NormalLoadN) ?? 0.0;
            _log.WriteLine($"TRACE t={simElapsed:F1} alt={alt:F1} vUp={vUp:F1} horiz={horizontal:F1} " +
                $"throttle={vessel.Throttle:F3} spool={cluster?.ThrottleLevel ?? 0.0:F3} " +
                $"engines={cluster?.SelectedEngineCount ?? 0} upright={upright:F4} phase={phase} " +
                $"contacts={contacts} maxStroke={maxStroke:F3} peakLegLoad={peakLegLoad:F0} " +
                $"settled={vessel.IsSurfaceSettled}");
            _log.Flush();
            _nextEdlTelemetry = simElapsed + 5.0;
        }

        if (!_entry && mission?.Phase is MissionPhase.ENTRY or MissionPhase.PEAK_HEATING
                or MissionPhase.AERO_DESCENT or MissionPhase.RETRO_BURN
                or MissionPhase.FINAL_DESCENT or MissionPhase.LANDED)
        {
            QueueCapture("entry");
            _entry = true;
        }

        if (!_peak && mission?.Phase == MissionPhase.PEAK_HEATING)
        {
            QueueCapture("peak_heating");
            _peak = true;
        }

        if (!_retro && mission?.Phase is MissionPhase.RETRO_BURN or MissionPhase.FINAL_DESCENT)
        {
            bridge.SetTimeScale(1.0);
            QueueCapture("retro_burn");
            _retro = true;
            _retroStart = universe.CurrentTime;
        }

        if (_retro && !_flipComplete && _pendingSlug == null && surfVel.Magnitude > 1.0)
        {
            Vector3d nose = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            double alignment = nose.Dot(-surfVel.Normalized);
            if (alignment > System.Math.Cos(5.0 * System.Math.PI / 180.0) &&
                universe.CurrentTime - _retroStart > 0.5)
            {
                QueueCapture("flip_complete");
                _flipComplete = true;
                int engines = vessel.Parts.Parts
                    .FirstOrDefault(p => p.Definition.IsStarshipFamily
                        && p.Definition.HasVehicleRole("ship_engines"))
                    ?.SelectedEngineCount ?? 0;
                _log.WriteLine($"CHECK finite_flip duration={universe.CurrentTime - _retroStart:F2}s " +
                    $"alignment={alignment:F5} omega={vessel.AngularVelocity.Magnitude:F4} engines={engines}");
                _log.Flush();
            }
        }

        if (!_landed && mission?.Phase == MissionPhase.LANDED)
        {
            QueueCapture("touchdown");
            _landed = true;
        }

        if (vessel.IsDestroyed)
        {
            var contact = vessel.LastSurfaceContact;
            int contacts = contact?.ContactCount ?? 0;
            double maxStroke = contact?.Points.Max(p => p.PenetrationM) ?? 0.0;
            double peakLegLoad = contact?.Points.Max(p => p.NormalLoadN) ?? 0.0;
            _log.WriteLine($"FAIL vessel destroyed phase={phase} alt={alt:F1} " +
                $"spd={surfVel.Magnitude:F2} cause={vessel.DestructionCause} " +
                $"impact={vessel.CrashImpactSpeed:F2} gear={vessel.HasDeployedLandingGear} " +
                $"points={vessel.LandingContactPoints.Count} contacts={contacts} " +
                $"maxStroke={maxStroke:F3} peakLegLoad={peakLegLoad:F0} " +
                $"travelExcess={contact?.MaxTravelExcessM ?? 0.0:F3} " +
                $"overTravel={contact?.HasOverTravel ?? false} overload={contact?.HasOverload ?? false}");
            _log.Flush();
            Finish("CRASHED");
            return;
        }

        if (_landed && _pendingSlug == null)
        {
            Finish(_flipComplete ? "LANDED" : "LANDED_WITHOUT_FLIP");
            return;
        }

        if (simElapsed > 900.0)
            Finish("EDL_TIMEOUT");
    }

    // Reuses the exact same deterministic-70km-entry seeding as ProcessEdlVerification
    // (SimulationBridge.BeginReentryDemonstration) for exactly one attitude. --reentry-compare
    // runs this mode twice as two separate Godot launches (see the bash orchestration at the
    // bottom of this file) rather than seeding twice inside one process: EDLController keeps
    // its own private phase state and BeginReentryDemonstration intentionally does not reset
    // it (that reset belongs to EDL/Ascent guidance, which this capture harness must not
    // touch), so a second in-process seed would leave EDL stuck past Inactive and never re-arm.
    private void ProcessReentryVariant(SimulationBridge bridge, Universe universe, MissionManager? mission)
    {
        const double PeakHeatingTimeoutSec = 240.0;
        bool bellyFirst = "${REENTRY_BELLY_FIRST}" == "true";
        string slug = "${REENTRY_SLUG}";

        if (!_reentrySeeded)
        {
            if (_readyFrames < 30 || EDLController.Instance == null) return;
            if (!bridge.BeginReentryDemonstration(bellyFirst: bellyFirst))
            {
                _log.WriteLine($"GAP HUD reentry demonstration entry point refused the {slug} scenario");
                Finish("REENTRY_COMPARE_START_FAILED");
                return;
            }
            bridge.SetTimeScale(3.0); // verification speed only; the HUD button starts at x1
            _reentrySeeded = true;
            _reentryScenarioStart = universe.CurrentTime;
            _log.WriteLine($"ACTION seeded {(bellyFirst ? "nominal belly-flop" : "forced broadside bad-attitude")} " +
                $"entry for compare (slug={slug})");
            _log.Flush();
            return;
        }

        var vessel = bridge.ActiveVessel;
        var body = universe.Bodies.First(b => b.Name == "Earth");
        if (vessel == null)
        {
            Finish("REENTRY_COMPARE_NO_VESSEL");
            return;
        }
        double simElapsed = universe.CurrentTime - _reentryScenarioStart;

        // Never gates on a frame count. The primary gate is MissionPhase.PEAK_HEATING (the
        // moment thermal/VFX divergence between attitudes is most visible). A bad, low-drag
        // attitude can have a materially different aero/heating profile than the protected
        // belly-flop and can drive the mission FSM straight past PEAK_HEATING into
        // RETRO_BURN/FINAL_DESCENT/LANDED without ever reporting it as the current phase — in
        // that case capture the next EDL phase reached instead of waiting on one that will not
        // recur. Vessel destruction is captured directly. A generous simulated-time bound
        // (mirroring ProcessEdlVerification's own EDL_TIMEOUT pattern) exists only to catch
        // genuine no-progress stalls, never to gate a normal capture.
        if (!_reentryQueued)
        {
            if (mission?.Phase == MissionPhase.PEAK_HEATING)
            {
                LogReentryCompareState(slug, vessel, body, mission, simElapsed, "PEAK_HEATING");
                QueueCapture(slug);
                _reentryQueued = true;
            }
            else if (vessel.IsDestroyed)
            {
                LogReentryCompareState(slug, vessel, body, mission, simElapsed,
                    $"DESTROYED cause={vessel.DestructionCause}");
                QueueCapture(slug);
                _reentryQueued = true;
            }
            else if (mission?.Phase is MissionPhase.RETRO_BURN or MissionPhase.FINAL_DESCENT
                or MissionPhase.LANDED)
            {
                LogReentryCompareState(slug, vessel, body, mission, simElapsed,
                    $"POST_HEATING_FALLBACK phase={mission?.Phase}");
                QueueCapture(slug);
                _reentryQueued = true;
            }
            else if (simElapsed > PeakHeatingTimeoutSec)
            {
                if (mission?.InDescent == true)
                {
                    // Made real descent progress (e.g. stuck in ENTRY/AERO_DESCENT) but never
                    // reached one of the sharper states above — capture what we have rather
                    // than fail a run that is clearly not stalled.
                    LogReentryCompareState(slug, vessel, body, mission, simElapsed,
                        $"TIMEOUT_FALLBACK phase={mission?.Phase}");
                    QueueCapture(slug);
                    _reentryQueued = true;
                }
                else
                {
                    _log.WriteLine($"GAP {slug}: entry state machine made no descent progress " +
                        $"within timeout (phase={mission?.Phase})");
                    _log.Flush();
                    Finish("REENTRY_COMPARE_TIMEOUT");
                    return;
                }
            }
        }

        if (_reentryQueued && _pendingSlug == null)
            Finish("REENTRY_VARIANT_OK");
    }

    private void LogReentryCompareState(string slug, Vessel vessel, CelestialBody body,
        MissionManager? mission, double simElapsed, string trigger)
    {
        Vector3d up = (vessel.Position - body.Position).Normalized;
        double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up);
        double maxT = vessel.Parts.Parts.Count > 0 ? vessel.Parts.Parts.Max(p => p.Temperature) : 0.0;
        double heatRatio = vessel.Parts.Parts.Count > 0 ? vessel.Parts.Parts.Max(p => p.ThermalRatio) : 0.0;
        _log.WriteLine($"REENTRY_COMPARE slug={slug} trigger={trigger} t={simElapsed:F1} " +
            $"phase={mission?.Phase} upright={upright:F4} maxT={maxT:F0} heatRatio={heatRatio:F3} " +
            $"omega={vessel.AngularVelocity.Magnitude:F4}");
        _log.Flush();
    }

    private void ProcessDeorbit(SimulationBridge bridge, Vessel vessel, Universe universe,
        CelestialBody body, MissionManager? mission, double alt)
    {
        if (!_deorbitStarted)
        {
            DrainToReserve(vessel, 0.12);
            bridge.SetTimeScale(1.0);
            _deorbitStarted = true;
            _log.WriteLine("ACTION deorbit: capped propellant at 12% landing reserve, starting retro burn");
            _log.Flush();
        }

        Vector3d rel = vessel.Position - body.Position;
        Vector3d vel = vessel.Velocity - body.Velocity;
        var oe = OrbitalElements.FromStateVector(rel, vel, body.GM, body.Id, universe.CurrentTime);
        double periAlt = oe.Periapsis - body.Radius;

        if (!_deorbitDone)
        {
            Vector3d surfVel = vessel.GetSurfaceVelocity(body);
            Vector3d retro = surfVel.Magnitude > 10.0 ? -surfVel.Normalized : -rel.Normalized;
            vessel.Orientation = ShortestArc(Vector3d.Up, retro);
            vessel.Throttle = 1.0;
            vessel.SASEnabled = false;
            // Commit the entry: a shallow periapsis (~64 km) grazes and can skip back out of
            // the atmosphere, re-coasting a full orbit and blowing the wall budget. Target
            // ~55 km so the belly-flop enters on a single committed pass (survivable corridor
            // for a high-drag broadside attitude) and reaches touchdown in a few sim-minutes.
            if (periAlt < 55_000.0)
            {
                vessel.Throttle = 0.0;
                _deorbitDone = true;
                _log.WriteLine($"ACTION deorbit burn complete periAlt={periAlt / 1000.0:F0} km");
                _log.Flush();
            }
        }
        else if (alt > 120_000.0 && vessel.Throttle < 0.01 && !_entry)
        {
            // Coast quickly until EDL declares ENTRY.  On the next frame the harness drops
            // to x1 and queues the state-gated entry capture before accelerating again.
            bridge.SetTimeScale(200.0);
        }
        else if (alt > 90_000.0 && vessel.Throttle < 0.01 && _entry)
        {
            // The controller is already armed, but meaningful aero/heating has not started.
            // Warp 5 remains on the RK4 path (unlike on-rails warp >= 10) and closes the long
            // 140→90 km coast without skipping the physical entry solution.
            bridge.SetTimeScale(5.0);
        }
        else
        {
            // Dense entry uses the same x3 RK4 path as the independently verified --edl
            // acceptance mode.  Return to x1 before powered descent: the orbital-entry
            // trajectory reaches the flip faster than the deterministic demo, and landing
            // guidance/contact resolution need full temporal fidelity there.
            bool poweredDescent = mission?.Phase is MissionPhase.RETRO_BURN
                or MissionPhase.FINAL_DESCENT
                or MissionPhase.LANDED;
            bridge.SetTimeScale(_entry && !poweredDescent ? 3.0 : 1.0);
        }
    }

    private void MonitorAscent(
        Vessel vessel,
        CelestialBody body,
        Universe universe,
        string missionPhase,
        double altitude,
        double surfaceSpeed,
        double verticalSpeed,
        double dynamicPressure,
        double properAccelerationG)
    {
        var controller = AscentController.Instance;
        string guidance = controller?.GuidancePhase ?? "Unavailable";
        var trajectory = OrbitalElements.FromStateVector(
            vessel.Position - body.Position,
            vessel.Velocity - body.Velocity,
            body.GM,
            body.Id,
            universe.CurrentTime);
        double apo = trajectory.Apoapsis - body.Radius;
        double pe = trajectory.Periapsis - body.Radius;
        double atmosphereTop = body.Atmosphere?.MaxAltitude ?? 0.0;
        bool finite = double.IsFinite(altitude)
            && double.IsFinite(surfaceSpeed)
            && double.IsFinite(verticalSpeed)
            && double.IsFinite(vessel.Position.X)
            && double.IsFinite(vessel.Position.Y)
            && double.IsFinite(vessel.Position.Z)
            && double.IsFinite(vessel.Velocity.X)
            && double.IsFinite(vessel.Velocity.Y)
            && double.IsFinite(vessel.Velocity.Z);

        Part? engineCluster = vessel.Parts.Parts.FirstOrDefault(
            p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"));
        int runningEngines = engineCluster?.EngineStates.Count(state =>
            state.State is EngineLifecycleState.Ignition
                or EngineLifecycleState.Running) ?? 0;
        int failedEngines = engineCluster?.EngineStates.Count(state =>
            !string.IsNullOrWhiteSpace(state.FailureCode)) ?? 0;
        double propellant = vessel.Parts.Parts.Sum(
            part => part.LiquidFuel + part.Oxidizer);

        if (guidance != _lastGuidancePhase)
        {
            _log.WriteLine(
                $"TRANSITION_ASCENT t={universe.CurrentTime:F1} " +
                $"from={_lastGuidancePhase} guidance={guidance} mission={missionPhase} " +
                $"alt={altitude:F1} spd={surfaceSpeed:F1} vSpeed={verticalSpeed:F1} " +
                $"apo={apo:F1} pe={pe:F1} throttle={vessel.Throttle:F3} " +
                $"runningEngines={runningEngines}");
            if (_insertObserved && guidance == "Coast")
            {
                FailAscentInvariant(
                    "insert_to_coast",
                    $"t={universe.CurrentTime:F1} from={_lastGuidancePhase} guidance={guidance}");
                return;
            }
            _lastGuidancePhase = guidance;
        }

        if (guidance == "Insert")
        {
            if (!_insertObserved)
            {
                _insertObserved = true;
                _insertStartedAt = universe.CurrentTime;
            }
            _minimumInsertionVSpeed = System.Math.Min(
                _minimumInsertionVSpeed,
                verticalSpeed);
            _maximumInsertionDescent = System.Math.Max(
                _maximumInsertionDescent,
                -verticalSpeed);
        }

        if (!finite)
        {
            FailAscentInvariant(
                "non_finite_state",
                $"t={universe.CurrentTime:F1} alt={altitude} spd={surfaceSpeed} vSpeed={verticalSpeed}");
            return;
        }
        if (vessel.IsDestroyed)
        {
            FailAscentInvariant(
                "vehicle_destroyed",
                $"t={universe.CurrentTime:F1} cause={vessel.DestructionCause}");
            return;
        }
        if (vessel.StructuralControlLost)
        {
            FailAscentInvariant(
                "structural_control_lost",
                $"t={universe.CurrentTime:F1} alt={altitude:F1} q={dynamicPressure:F1}");
            return;
        }
        if (failedEngines > 0)
        {
            FailAscentInvariant(
                "engine_failure",
                $"t={universe.CurrentTime:F1} failedEngines={failedEngines} guidance={guidance}");
            return;
        }
        if (missionPhase == nameof(MissionPhase.ORBIT)
            && !OrbitQualificationPolicy.HasSafePeriapsis(
                trajectory,
                body.Radius,
                atmosphereTop))
        {
            FailAscentInvariant(
                "unsafe_orbit_phase",
                $"t={universe.CurrentTime:F1} pe={pe:F1} atmoTop={atmosphereTop:F1}");
            return;
        }
        if (guidance == "Insert"
            && missionPhase != nameof(MissionPhase.ORBIT)
            && universe.CurrentTime - _insertStartedAt > 3.0
            && (vessel.Throttle < 0.05 || runningEngines == 0))
        {
            FailAscentInvariant(
                "insertion_thrust_lost",
                $"t={universe.CurrentTime:F1} throttle={vessel.Throttle:F3} " +
                $"runningEngines={runningEngines} propellant={propellant:F1}");
            return;
        }
        if (guidance == "Insert"
            && universe.CurrentTime - _insertStartedAt > 20.0
            && verticalSpeed < -100.0
            && pe < atmosphereTop)
        {
            FailAscentInvariant(
                "insertion_descent_unrecovered",
                $"t={universe.CurrentTime:F1} vSpeed={verticalSpeed:F1} " +
                $"apo={apo:F1} pe={pe:F1} atmoTop={atmosphereTop:F1}");
            return;
        }

        bool meaningfulProgress = !double.IsFinite(_lastProgressAt)
            || System.Math.Abs(altitude - _lastProgressAltitude) >= 100.0
            || System.Math.Abs(surfaceSpeed - _lastProgressSpeed) >= 10.0
            || System.Math.Abs(pe - _lastProgressPeriapsis) >= 1_000.0;
        if (meaningfulProgress)
        {
            _lastProgressAt = universe.CurrentTime;
            _lastProgressAltitude = altitude;
            _lastProgressSpeed = surfaceSpeed;
            _lastProgressPeriapsis = pe;
        }
        else if (universe.CurrentTime - _lastProgressAt > 60.0)
        {
            FailAscentInvariant(
                "physics_stalled",
                $"t={universe.CurrentTime:F1} noProgressFor={universe.CurrentTime - _lastProgressAt:F1} " +
                $"guidance={guidance} alt={altitude:F1} spd={surfaceSpeed:F1} pe={pe:F1}");
            return;
        }

        if (universe.CurrentTime + 1e-9 < _nextAscentTelemetry)
            return;

        _ascentTraceCount++;
        _log.WriteLine(
            $"TRACE_ASCENT t={universe.CurrentTime:F1} mission={missionPhase} " +
            $"guidance={guidance} active={controller?.IsEngaged ?? false} " +
            $"alt={altitude:F1} spd={surfaceSpeed:F1} vSpeed={verticalSpeed:F1} " +
            $"apo={apo:F1} pe={pe:F1} atmoTop={atmosphereTop:F1} " +
            $"q={dynamicPressure:F1} g={properAccelerationG:F2} " +
            $"throttle={vessel.Throttle:F3} spool={engineCluster?.ThrottleLevel ?? 0.0:F3} " +
            $"runningEngines={runningEngines} failedEngines={failedEngines} " +
            $"propellant={propellant:F1} warp={universe.TimeScale:F1} " +
            $"finite={finite} destroyed={vessel.IsDestroyed} " +
            $"structuralLost={vessel.StructuralControlLost}");
        _log.Flush();
        _nextAscentTelemetry = universe.CurrentTime + 10.0;
    }

    private void FailAscentInvariant(string code, string evidence)
    {
        _log.WriteLine($"FAIL invariant={code} {evidence}");
        _log.Flush();
        Finish("ASCENT_INVARIANT_FAILED");
    }

    private void LogFullMissionProgress(Vessel vessel, CelestialBody body,
        Universe universe, string phase)
    {
        if (!_deorbitStarted || universe.CurrentTime < _nextFullTelemetry) return;

        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        Vector3d up = (vessel.Position - body.Position).Normalized;
        double alt = vessel.GetAltitude(body);
        double vSpeed = surfVel.Dot(up);
        double maxT = vessel.Parts.Parts.Count > 0
            ? vessel.Parts.Parts.Max(p => p.Temperature)
            : 0.0;
        double heatRatio = vessel.Parts.Parts.Count > 0
            ? vessel.Parts.Parts.Max(p => p.ThermalRatio)
            : 0.0;
        Part? engineCluster = vessel.Parts.Parts.FirstOrDefault(
            p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"));
        string engineRuntime = engineCluster == null
            ? "none"
            : string.Join(",", engineCluster.EngineStates
                .GroupBy(state => state.State)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));
        string engineFailures = engineCluster == null
            ? "none"
            : string.Join(",", engineCluster.EngineStates
                .Where(state => !string.IsNullOrWhiteSpace(state.FailureCode))
                .GroupBy(state => state.FailureCode)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}:{group.Count()}"));
        int completedStarts = engineCluster?.EngineStates
            .Take(engineCluster.SelectedEngineCount)
            .Select(state => state.StartsCompleted)
            .DefaultIfEmpty(0)
            .Min() ?? 0;
        double liquidFuel = vessel.Parts.Parts.Sum(p => p.LiquidFuel);
        double oxidizer = vessel.Parts.Parts.Sum(p => p.Oxidizer);
        double propellant = liquidFuel + oxidizer;
        double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up);
        _log.WriteLine(
            $"TRACE_FULL t={universe.CurrentTime:F1} alt={alt:F1} " +
            $"spd={surfVel.Magnitude:F1} vSpeed={vSpeed:F1} phase={phase} " +
            $"warp={universe.TimeScale:F1} heatRatio={heatRatio:F3} maxT={maxT:F0} " +
            $"propellant={propellant:F0} throttle={vessel.Throttle:F3} " +
            $"spool={engineCluster?.ThrottleLevel ?? 0.0:F3} " +
            $"engines={engineCluster?.SelectedEngineCount ?? 0} " +
            $"runtime={engineRuntime} failures={engineFailures} starts={completedStarts} " +
            $"lf={liquidFuel:F0} ox={oxidizer:F0} " +
            $"upright={upright:F4} authority={vessel.ControlAuthorityFactor:F2}");
        _log.Flush();
        _nextFullTelemetry = universe.CurrentTime + 20.0;
    }

    private void ProcessAtmosphereMatrix(double delta, SimulationBridge bridge,
        Vessel vessel, Universe universe, CelestialBody body)
    {
        if (_atmosIndex < 0)
        {
            // Freeze the physical clock before the first case is applied. Previously the
            // default launch craft was allowed to run for 60 frames, often crashing on the
            // pad before the matrix could move it to its requested altitude. That made every
            // capture report the same ~0 m state while the PNG gate still claimed success.
            bridge.SetTimeScale(0.0);
            _atmosIndex = 0;
            ApplyAtmosphereCase(bridge, vessel, universe, body);
        }

        _atmosFrameSeconds += delta;
        _atmosMaxFrameSeconds = System.Math.Max(_atmosMaxFrameSeconds, delta);
        _atmosPerfFrames++;
        if (delta > 1.0 / 30.0) _atmosSlowFrames++;

        ApplyAtmosphereCamera();
        // Exposure adaptation is stateful and asymmetric.  Fixed-delay captures previously
        // inherited up to 6x night exposure in the following daylight case, making a valid
        // shader look white-clipped.  Require the measured exposure rate to settle as well
        // as allowing several incremental sky-cubemap updates.  A wall-time cap, rather
        // than a frame cap, avoids truncating the 9 s dark-adaptation model on a fast GPU.
        var world = GetTree().Root.FindChild("WorldEnvironment", true, false)
            as WorldEnvironment;
        double exposure = world?.Environment?.TonemapExposure ?? -1.0;
        if (exposure > 0.0 && _atmosPreviousExposure > 0.0 && delta > 1e-6)
        {
            double rate = System.Math.Abs(exposure - _atmosPreviousExposure) / delta;
            _atmosExposureStableFrames = rate <= AtmosphereExposureRateTolerance
                ? _atmosExposureStableFrames + 1 : 0;
        }
        _atmosPreviousExposure = exposure;

        bool enoughFrames = _atmosPerfFrames >= AtmosphereMinimumSettleFrames;
        bool enoughTime = _atmosFrameSeconds >= AtmosphereSettleSeconds;
        bool stableExposure = _atmosExposureStableFrames >= AtmosphereExposureStableFrames;
        bool safetyLimit = _atmosFrameSeconds >= AtmosphereMaximumSettleSeconds;
        if (!safetyLimit && !(enoughFrames && enoughTime && stableExposure)) return;

        var shot = _atmosCases[_atmosIndex];
        CaptureNow(shot.Slug);
        LogAtmosphereState(bridge, vessel, universe, body, shot);

        _atmosIndex++;
        if (_atmosIndex >= _atmosCases.Length)
        {
            Finish("ATMOSPHERE_OK");
            return;
        }
        ApplyAtmosphereCase(bridge, vessel, universe, body);
    }

    private void ApplyAtmosphereCase(SimulationBridge bridge, Vessel vessel,
        Universe universe, CelestialBody earth)
    {
        var shot = _atmosCases[_atmosIndex];
        var sun = universe.GetBody("sun");
        Vector3d sunDir = sun == null
            ? new Vector3d(0.4, 0.5, 0.8).Normalized
            : (sun.Position - earth.Position).Normalized;

        // Choose a stable vector on the terminator, then tilt it by the requested solar
        // elevation.  The resulting dot(up,sunDir) is exactly sin(elevation).
        Vector3d seed = System.Math.Abs(sunDir.Dot(Vector3d.Up)) < 0.92
            ? Vector3d.Up : Vector3d.Right;
        Vector3d terminatorUp = (seed - sunDir * seed.Dot(sunDir)).Normalized;
        double elev = shot.SunElevationDeg * System.Math.PI / 180.0;
        _atmosUp = (terminatorUp * System.Math.Cos(elev)
            + sunDir * System.Math.Sin(elev)).Normalized;

        Vector3d projectedSun = sunDir - _atmosUp * sunDir.Dot(_atmosUp);
        if (projectedSun.MagnitudeSquared < 1e-10)
            projectedSun = earth.GetEastDirection(earth.Position + _atmosUp * earth.Radius);
        _atmosLook = projectedSun.Normalized;

        vessel.IsGroundHeld = false;
        vessel.Position = earth.Position + _atmosUp * (earth.Radius + shot.AltitudeM);
        // Co-rotate with the sampled surface so q/heat telemetry remains zero.  The matrix
        // validates optics, not an accidental 350–460 m/s wind caused by inertial rest.
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        vessel.Throttle = 0.0;

        // Local +Y looks along the horizon; local -Z is radial-up.  This also gives the
        // cockpit cases a deterministic, level attitude rather than a random roll.
        Vector3d localXWorld = _atmosLook.Cross(-_atmosUp).Normalized;
        var basis = new Basis(ToGodot(localXWorld), ToGodot(_atmosLook), ToGodot(-_atmosUp));
        var q = basis.GetRotationQuaternion();
        vessel.Orientation = new Quaterniond(q.W, q.X, q.Y, q.Z);

        bridge.SetTimeScale(0.0);
        if (GetTree().Root.FindChild("HUDController", true, false) is CanvasItem hud)
            hud.Visible = false;
        if (shot.Cockpit)
        {
            if (CameraController.Instance != null)
                CameraController.Instance.ProcessMode = ProcessModeEnum.Inherit;
            CameraController.Instance?.EnterCockpitView();
        }

        _atmosFrameSeconds = 0.0;
        _atmosMaxFrameSeconds = 0.0;
        _atmosPerfFrames = 0;
        _atmosSlowFrames = 0;
        _atmosPreviousExposure = -1.0;
        _atmosExposureStableFrames = 0;
        _log.WriteLine($"ATMOS_APPLY slug={shot.Slug} targetAlt={shot.AltitudeM:F1} " +
            $"targetSunElevation={shot.SunElevationDeg:F1} cockpit={shot.Cockpit}");
        _log.Flush();
    }

    private void ApplyAtmosphereCamera()
    {
        var shot = _atmosCases[_atmosIndex];
        if (shot.Cockpit) return;

        if (CameraController.Instance != null)
            CameraController.Instance.ProcessMode = ProcessModeEnum.Disabled;
        if (GetTree().Root.FindChild("StarshipRenderer", true, false) is Node3D renderer)
            renderer.Visible = false;
        if (GetTree().Root.FindChild("ActiveVesselRenderer", true, false) is Node3D activeRenderer)
            activeRenderer.Visible = false;
        if (GetTree().Root.FindChild("CockpitRenderer", true, false) is Node3D cockpit)
            cockpit.Visible = false;

        if (GetTree().Root.FindChild("Camera3D", true, false) is Camera3D camera)
        {
            camera.Position = Vector3.Zero;
            camera.Near = 0.1f;
            camera.Fov = 60.0f;
            camera.LookAt(ToGodot(_atmosLook) * 100.0f, ToGodot(_atmosUp));
        }
    }

    private void LogAtmosphereState(SimulationBridge bridge, Vessel vessel,
        Universe universe, CelestialBody earth,
        (string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit) shot)
    {
        var sun = universe.GetBody("sun");
        Vector3d up = (vessel.Position - earth.Position).Normalized;
        Vector3d sunDir = sun == null ? Vector3d.Up : (sun.Position - vessel.Position).Normalized;
        double solarElevation = System.Math.Asin(System.Math.Clamp(up.Dot(sunDir), -1.0, 1.0))
            * 180.0 / System.Math.PI;
        var camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
        var world = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        float exposure = world?.Environment?.TonemapExposure ?? -1.0f;
        double meanMs = _atmosPerfFrames > 0
            ? _atmosFrameSeconds * 1000.0 / _atmosPerfFrames : 0.0;

        _log.WriteLine($"ATMOS_STATE slug={shot.Slug} actualAlt={vessel.GetAltitude(earth):F1} " +
            $"sunElevation={solarElevation:F2} solarVisibility={SunController.SolarVisibility:F3} " +
            $"cockpit={shot.Cockpit} exposure={exposure:F3} fov={camera?.Fov ?? -1:F2} " +
            $"near={camera?.Near ?? -1:F3} " +
            $"exposureSettled={_atmosExposureStableFrames >= AtmosphereExposureStableFrames}");
        _log.WriteLine($"PERF slug={shot.Slug} meanFrameMs={meanMs:F2} " +
            $"maxFrameMs={_atmosMaxFrameSeconds * 1000.0:F2} slowFrames={_atmosSlowFrames} " +
            $"sampleFrames={_atmosPerfFrames} exposureStableFrames={_atmosExposureStableFrames} " +
            $"reportedFps={Engine.GetFramesPerSecond()}");
        _log.Flush();
    }

    private static Vector3 ToGodot(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    private void QueueCapture(string slug)
    {
        if (_pendingSlug != null) return;
        _pendingSlug = slug;
        _settleLeft = SettleFrames;
    }

    private void TryCapturePending()
    {
        if (_pendingSlug == null) return;
        if (_settleLeft > 0) { _settleLeft--; return; }
        string slug = _pendingSlug;
        _pendingSlug = null;
        CaptureNow(slug);
    }

    private void CaptureNow(string slug)
    {
        var img = GetTree().Root.GetViewport().GetTexture().GetImage();
        string path = Path.Combine(_outDir, $"exo_play_{slug}.png");
        img.SavePng(path);
        LogTelemetry(slug, path);
        LogImageMetrics(slug, img);
        GD.Print($"[Playtest] captured {slug} -> {path}");
    }

    private void LogImageMetrics(string slug, Image image)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();
        const int stride = 4;
        long samples = 0, clipped = 0, dark = 0;
        long starCandidates = 0, starSamples = 0;
        long skySamples = 0, skyClipped = 0, skyWhiteClipped = 0, skyBright = 0;
        long surfaceSamples = 0, surfaceClipped = 0, surfaceWhiteClipped = 0;
        long horizonSamples = 0, limbSamples = 0, neonGreen = 0, twilightUpperSamples = 0;
        double sum = 0.0, upperSum = 0.0, lowerSum = 0.0;
        double horizonSum = 0.0, horizonRed = 0.0, horizonBlue = 0.0;
        double horizonGreenExcess = 0.0, twilightUpperSum = 0.0;
        long upperN = 0, lowerN = 0;
        int[] histogram = new int[256];
        int[] skyHistogram = new int[256];

        for (int y = 0; y < height; y += stride)
        {
            for (int x = 0; x < width; x += stride)
            {
                Color c = image.GetPixel(x, y);
                double luma = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                double maxChannel = System.Math.Max(c.R, System.Math.Max(c.G, c.B));
                double minChannel = System.Math.Min(c.R, System.Math.Min(c.G, c.B));
                sum += luma;
                samples++;
                histogram[System.Math.Clamp((int)System.Math.Round(luma * 255.0), 0, 255)]++;
                if (maxChannel >= 0.995f) clipped++;
                if (luma <= 0.02) dark++;

                if (y < height * 0.42)
                {
                    upperSum += luma;
                    upperN++;
                }

                // Upper-sky ROI.  Keeping it clear of the expected y=0.5 horizon makes a
                // blown daytime sky distinguishable from a legitimately bright planet limb.
                if (x >= width * 0.15 && x < width * 0.85
                    && y >= height * 0.08 && y < height * 0.38)
                {
                    skySamples++;
                    skyHistogram[System.Math.Clamp((int)System.Math.Round(luma * 255.0), 0, 255)]++;
                    if (maxChannel >= 0.995f) skyClipped++;
                    if (minChannel >= 0.985f) skyWhiteClipped++;
                    if (luma >= 0.90) skyBright++;
                }

                // The lower central ROI is terrain/planet in exterior horizon views.  This
                // catches exposure regressions that a whole-frame average hides.
                if (x >= width * 0.15 && x < width * 0.85
                    && y >= height * 0.58 && y < height * 0.88)
                {
                    surfaceSamples++;
                    if (maxChannel >= 0.995f) surfaceClipped++;
                    if (minChannel >= 0.985f) surfaceWhiteClipped++;
                }

                // Side sectors deliberately omit the solar disc in the centre.  At ±1°
                // solar elevation, a physical twilight must brighten and warm toward the
                // horizon even when the disc itself is clipped.
                bool twilightSide = (x >= width * 0.15 && x < width * 0.42)
                    || (x >= width * 0.58 && x < width * 0.85);
                if (twilightSide && y >= height * 0.40 && y < height * 0.49)
                {
                    horizonSamples++;
                    horizonSum += luma;
                    horizonRed += c.R;
                    horizonBlue += c.B;
                }
                if (twilightSide && y >= height * 0.20 && y < height * 0.32)
                {
                    twilightUpperSamples++;
                    twilightUpperSum += luma;
                }

                // A real 557.7 nm airglow layer is dim and narrow.  Count only bright,
                // saturated green pixels across the limb as "neon"; subtle green emission
                // remains valid and is reported separately as mean green excess.
                if (x >= width * 0.10 && x < width * 0.90
                    && y >= height * 0.36 && y < height * 0.62)
                {
                    limbSamples++;
                    double chroma = maxChannel - minChannel;
                    double greenExcess = System.Math.Max(0.0,
                        c.G - System.Math.Max(c.R, c.B));
                    horizonGreenExcess += greenExcess;
                    if (luma >= 0.12 && c.G >= c.R + 0.04
                        && c.G >= c.B + 0.025
                        && maxChannel > 0.0 && chroma / maxChannel >= 0.20)
                        neonGreen++;
                }

                // Sharp-star proxy: require a true local maximum against an eight-point
                // ring, not just one bright pixel on a smooth atmospheric gradient.
                if (x >= width * 0.20 && x < width * 0.80
                    && y >= height * 0.08 && y < height * 0.36)
                {
                    double left = Luma(image.GetPixel(System.Math.Max(0, x - stride), y));
                    double right = Luma(image.GetPixel(System.Math.Min(width - 1, x + stride), y));
                    double above = Luma(image.GetPixel(x, System.Math.Max(0, y - stride)));
                    double below = Luma(image.GetPixel(x, System.Math.Min(height - 1, y + stride)));
                    double nw = Luma(image.GetPixel(System.Math.Max(0, x - stride),
                        System.Math.Max(0, y - stride)));
                    double ne = Luma(image.GetPixel(System.Math.Min(width - 1, x + stride),
                        System.Math.Max(0, y - stride)));
                    double sw = Luma(image.GetPixel(System.Math.Max(0, x - stride),
                        System.Math.Min(height - 1, y + stride)));
                    double se = Luma(image.GetPixel(System.Math.Min(width - 1, x + stride),
                        System.Math.Min(height - 1, y + stride)));
                    double ringMean = (left + right + above + below + nw + ne + sw + se) / 8.0;
                    double ringMax = System.Math.Max(
                        System.Math.Max(System.Math.Max(left, right), System.Math.Max(above, below)),
                        System.Math.Max(System.Math.Max(nw, ne), System.Math.Max(sw, se)));
                    starSamples++;
                    if (luma >= 0.12 && luma - ringMean >= 0.055
                        && luma >= ringMax) starCandidates++;
                }
                if (y > height * 0.58)
                {
                    lowerSum += luma;
                    lowerN++;
                }
            }
        }

        double mean = samples > 0 ? sum / samples : 0.0;
        double upper = upperN > 0 ? upperSum / upperN : 0.0;
        double lower = lowerN > 0 ? lowerSum / lowerN : 0.0;
        double horizonMean = horizonSamples > 0 ? horizonSum / horizonSamples : 0.0;
        double twilightUpperMean = twilightUpperSamples > 0
            ? twilightUpperSum / twilightUpperSamples : 0.0;
        _log.WriteLine($"IMAGE slug={slug} width={width} height={height} samples={samples} " +
            $"mean={mean:F5} p50={HistogramPercentile(histogram, samples, 0.50):F5} " +
            $"p95={HistogramPercentile(histogram, samples, 0.95):F5} " +
            $"clippedFrac={(samples > 0 ? (double)clipped / samples : 0.0):F5} " +
            $"darkFrac={(samples > 0 ? (double)dark / samples : 0.0):F5} " +
            $"upperMean={upper:F5} lowerMean={lower:F5} " +
            $"horizonContrast={System.Math.Abs(upper - lower):F5} " +
            $"skyP95={HistogramPercentile(skyHistogram, skySamples, 0.95):F5} " +
            $"skyClippedFrac={(skySamples > 0 ? (double)skyClipped / skySamples : 0.0):F5} " +
            $"skyWhiteClipFrac={(skySamples > 0 ? (double)skyWhiteClipped / skySamples : 0.0):F5} " +
            $"skyBrightFrac={(skySamples > 0 ? (double)skyBright / skySamples : 0.0):F5} " +
            $"surfaceClippedFrac={(surfaceSamples > 0 ? (double)surfaceClipped / surfaceSamples : 0.0):F5} " +
            $"surfaceWhiteClipFrac={(surfaceSamples > 0 ? (double)surfaceWhiteClipped / surfaceSamples : 0.0):F5} " +
            $"twilightUpperMean={twilightUpperMean:F5} twilightHorizonMean={horizonMean:F5} " +
            $"twilightGradient={(horizonMean - twilightUpperMean):F5} " +
            $"twilightWarmth={(horizonSamples > 0 ? (horizonRed - horizonBlue) / horizonSamples : 0.0):F5} " +
            $"neonGreenFrac={(limbSamples > 0 ? (double)neonGreen / limbSamples : 0.0):F6} " +
            $"limbGreenExcess={(limbSamples > 0 ? horizonGreenExcess / limbSamples : 0.0):F6} " +
            $"starCandidateFrac={(starSamples > 0 ? (double)starCandidates / starSamples : 0.0):F6} " +
            $"sharpStarCount={starCandidates} " +
            $"sharpStarFrac={(starSamples > 0 ? (double)starCandidates / starSamples : 0.0):F6}");
        _log.Flush();
    }

    private static double Luma(Color c) =>
        0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    private static double HistogramPercentile(int[] histogram, long samples, double fraction)
    {
        if (samples <= 0) return 0.0;
        long target = (long)System.Math.Ceiling(samples * fraction);
        long count = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            count += histogram[i];
            if (count >= target) return i / 255.0;
        }
        return 1.0;
    }

    private void LogTelemetry(string slug, string path)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;
        var body = universe.GetDominantBody(vessel.Position);
        if (body == null) return;

        double alt = vessel.GetAltitude(body);
        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        Vector3d up = (vessel.Position - body.Position).Normalized;
        double spd = surfVel.Magnitude;
        double vSpeed = surfVel.Dot(up);
        double q = vessel.GetDynamicPressure(body);
        double mass = System.Math.Max(vessel.TotalMass, 1.0);
        Vector3d nonGrav = (vessel.ComputeThrust(body) + vessel.ComputeDrag(body)) / mass;
        double g = nonGrav.Magnitude / 9.80665;
        string phase = MissionManager.Instance?.Phase.ToString() ?? "?";
        double maxT = vessel.Parts.Parts.Max(p => p.Temperature);
        double heatRatio = vessel.Parts.Parts.Max(p => p.ThermalRatio);
        double density = body.GetAtmosphericDensity(vessel.Position);
        double flux = ThermalModel.ComputeHeatFlux(density, spd);
        var trajectory = OrbitalElements.FromStateVector(
            vessel.Position - body.Position,
            vessel.Velocity - body.Velocity,
            body.GM,
            body.Id,
            universe.CurrentTime);
        double apo = trajectory.Apoapsis - body.Radius;
        double pe = trajectory.Periapsis - body.Radius;
        double atmosphereTop = body.Atmosphere?.MaxAltitude ?? 0.0;
        int selectedEngines = vessel.Parts.Parts
            .FirstOrDefault(p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"))
            ?.SelectedEngineCount ?? 0;
        int landingParts = vessel.Parts.Parts.Count(
            p => p.Definition.Category == PartCategory.Landing);
        int contacts = vessel.LastSurfaceContact?.ContactCount ?? 0;
        double maxStroke = vessel.LastSurfaceContact?.Points.Max(p => p.PenetrationM) ?? 0.0;
        double peakLegLoad = vessel.LastSurfaceContact?.Points.Max(p => p.NormalLoadN) ?? 0.0;

        _log.WriteLine(
            $"CAPTURE {slug} path={path} alt={alt:F1} spd={spd:F1} vSpeed={vSpeed:F1} " +
            $"apo={apo:F1} pe={pe:F1} atmoTop={atmosphereTop:F1} q={q:F0} g={g:F2} " +
            $"phase={phase} heatRatio={heatRatio:F3} maxT={maxT:F0} flux={flux:E2} " +
            $"omega={vessel.AngularVelocity.Magnitude:F4} selectedEngines={selectedEngines} " +
            $"landingParts={landingParts} approachSpeed={_lastApproachSpeed:F2} " +
            $"contacts={contacts} maxStroke={maxStroke:F3} peakLegLoad={peakLegLoad:F0} " +
            $"settled={vessel.IsSurfaceSettled}");
        _log.Flush();
    }

    private static void DrainToReserve(Vessel vessel, double reserveFrac)
    {
        foreach (var p in vessel.Parts.Parts)
        {
            double cap = p.Definition.FuelCapacityLF + p.Definition.FuelCapacityOx;
            if (cap <= 0.0) continue;
            double target = cap * reserveFrac;
            double total = p.LiquidFuel + p.Oxidizer;
            if (total <= target) continue;
            double remove = total - target;
            double lfFrac = total > 1e-9 ? p.LiquidFuel / total : 0.45;
            p.LiquidFuel -= remove * lfFrac;
            p.Oxidizer -= remove * (1.0 - lfFrac);
        }
    }

    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized;
        var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot > 0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d ax = System.Math.Abs(f.X) < 0.9 ? f.Cross(Vector3d.Right) : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(ax.Normalized, System.Math.PI);
        }
        Vector3d axis = f.Cross(t).Normalized;
        return Quaterniond.FromAxisAngle(axis, System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0)));
    }

    private void Finish(string reason)
    {
        if (_finished) return;
        _finished = true;
        if (_ascentTraceCount > 0)
        {
            string minimumVSpeed = double.IsFinite(_minimumInsertionVSpeed)
                ? $"{_minimumInsertionVSpeed:F1}"
                : "n/a";
            _log.WriteLine(
                $"ASCENT_METRICS samples={_ascentTraceCount} insertObserved={_insertObserved} " +
                $"minInsertionVSpeed={minimumVSpeed} " +
                $"maxInsertionDescent={_maximumInsertionDescent:F1}");
        }
        _log.WriteLine($"SUMMARY reason={reason} frames={_frame}");
        _log.Flush();
        _log.Dispose();
        GD.Print($"[Playtest] finish: {reason}");
        GetTree().CallDeferred("quit");
    }

    public override void _ExitTree()
    {
        if (!_authorized) return;
        if (!_finished)
        {
            _log?.WriteLine("SUMMARY reason=ABORT");
            _log?.Flush();
            _log?.Dispose();
        }
    }
}
CS
}

verify_pngs() {
  local min_bytes="${1:-8000}"
  local found=0
  shopt -s nullglob
  for png in "$OUT_DIR"/exo_play_*.png; do
    found=1
    local size
    size="$(wc -c < "$png" | tr -d ' ')"
    if [[ "$size" -lt "$min_bytes" ]]; then
      echo "ERROR: PNG too small ($size bytes): $png" >&2
      return 1
    fi
  done
  shopt -u nullglob
  if [[ "$found" -eq 0 ]]; then
    echo "ERROR: no PNG captures in $OUT_DIR" >&2
    return 1
  fi

  if [[ "$MODE" == "full" ]]; then
    local required=(
      pad liftoff maxq hotstage separation orbit orbit_beauty
      entry peak_heating retro_burn touchdown
    )
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing required milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
    done
    if ! verify_ascent_log_contract "$LOG" "LANDED"; then
      echo "ERROR: full mission failed its pad-to-stable-orbit contract" >&2
      return 1
    fi
    # Historical Flight 7/12 intentionally has no fictional landing gear. Those variants
    # use Universe.HandleSurfaceImpact's physical soft-landing path (<=3 m/s) and therefore
    # have no multi-foot ContactWrench. Other full-mission craft still require >=3 contacts.
    local gearless_historical=0
    if [[ "$VARIANT_FILE" == "starship_flight7_block2_2025.json" \
      || "$VARIANT_FILE" == "starship_flight12_v3_2026.json" ]]; then
      gearless_historical=1
    fi
    if ! awk -v gearless_historical="$gearless_historical" '
      function value(prefix,    i, pair) {
        for (i = 1; i <= NF; i++)
          if ($i ~ ("^" prefix "=")) {
            split($i, pair, "=")
            return pair[2]
          }
        return ""
      }
      /^TRACE_FULL / {
        altitude = value("alt") + 0
        if (altitude < 100.0)
          tracedApproachSpeed = value("spd") + 0
      }
      /^CAPTURE touchdown / {
        found = 1
        contacts = value("contacts") + 0
        settled = value("settled")
        landingParts = value("landingParts")
        capturedApproachSpeed = value("approachSpeed")
      }
      END {
        if (!found || settled != "True")
          exit 1
        if (contacts >= 3)
          exit 0
        if (!gearless_historical)
          exit 1
        # New logs carry both fields. For artifacts produced immediately before this
        # instrumentation landed, fall back to the last sub-100 m TRACE_FULL speed.
        if (landingParts != "" && landingParts + 0 != 0)
          exit 1
        if (capturedApproachSpeed != "")
          approachSpeed = capturedApproachSpeed + 0
        else
          approachSpeed = tracedApproachSpeed
        exit !(contacts == 0 && approachSpeed <= 3.0)
      }
    ' "$LOG"; then
      echo "ERROR: full-mission touchdown is neither a 3-contact gear landing nor a <=3 m/s settled historical gearless landing" >&2
      return 1
    fi
  elif [[ "$MODE" == "ascent" ]]; then
    local required=(pad liftoff maxq hotstage separation orbit)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing required ascent milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
    done
    if ! verify_ascent_log_contract "$LOG" "ASCENT_ORBIT_OK"; then
      echo "ERROR: focused ascent did not reach a verified stable orbit" >&2
      return 1
    fi
  elif [[ "$MODE" == "edl" ]]; then
    local required=(entry retro_burn flip_complete touchdown)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing required EDL milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
    done
    if ! grep -q 'SUMMARY reason=LANDED' "$LOG"; then
      echo "ERROR: deterministic EDL did not end in a verified landing" >&2
      return 1
    fi
    if ! grep -Eq 'CAPTURE touchdown .*contacts=[3-9][0-9]* .*settled=True' "$LOG"; then
      echo "ERROR: touchdown was not supported by at least three settled physical contacts" >&2
      return 1
    fi
  elif [[ "$MODE" == "hotstage" ]]; then
    if [[ ! -f "$OUT_DIR/exo_play_hotstage.png" ]]; then
      echo "ERROR: missing required hot-stage milestone PNG: exo_play_hotstage.png" >&2
      return 1
    fi
    if ! grep -q 'SUMMARY reason=HOTSTAGE_OK' "$LOG"; then
      echo "ERROR: hot-stage capture did not confirm the dual-thrust overlap state" >&2
      return 1
    fi
  elif [[ "$MODE" == "reentry_compare" ]]; then
    local required=(reentry_nominal reentry_bad_attitude)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing required reentry-compare milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
      if ! grep -q "^REENTRY_COMPARE slug=${slug} " "$LOG"; then
        echo "ERROR: missing REENTRY_COMPARE state evidence for ${slug}" >&2
        return 1
      fi
    done
    # Two independent Godot launches (nominal, bad-attitude) share this one $LOG file, so a
    # successful compare shows exactly one REENTRY_VARIANT_OK finish per launch.
    local ok_count
    ok_count="$(grep -c 'SUMMARY reason=REENTRY_VARIANT_OK' "$LOG" || true)"
    if [[ "${ok_count:-0}" -ne 2 ]]; then
      echo "ERROR: reentry compare did not finish cleanly in both launches (found ${ok_count:-0}/2 REENTRY_VARIANT_OK, see GAP/timeout lines)" >&2
      return 1
    fi
  elif [[ "$MODE" == "atmosphere" ]]; then
    local required=(
      ground_day ground_sunrise ground_sunset ground_night
      10km_day 30km_day 70km_day 120km_day 400km_day
      10km_night 30km_night 70km_night 120km_night 400km_night
      cockpit_120km_day cockpit_120km_night
    )
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing atmosphere matrix PNG: exo_play_${slug}.png" >&2
        return 1
      fi
      if ! grep -q "^IMAGE slug=${slug} " "$LOG"; then
        echo "ERROR: missing image metrics for atmosphere case: ${slug}" >&2
        return 1
      fi
      if ! grep -q "^ATMOS_STATE slug=${slug} " "$LOG"; then
        echo "ERROR: missing physical state evidence for atmosphere case: ${slug}" >&2
        return 1
      fi
    done
    if ! grep -q 'SUMMARY reason=ATMOSPHERE_OK' "$LOG"; then
      echo "ERROR: atmosphere matrix did not finish cleanly" >&2
      return 1
    fi
    if grep '^ATMOS_STATE ' "$LOG" | grep -qv ' exposureSettled=True'; then
      echo "ERROR: atmosphere capture reached its safety limit before exposure settled" >&2
      grep '^ATMOS_STATE ' "$LOG" | grep -v ' exposureSettled=True' >&2
      return 1
    fi

    # Presence alone is not enough: a stale vessel/camera state can still produce a
    # perfectly valid PNG while labeling it as another altitude or solar elevation.
    # Compare every state against the immediately preceding ATMOS_APPLY request so the
    # matrix remains a physical, reproducible experiment rather than just a screenshot
    # inventory. The tolerances cover only telemetry rounding and floating-point body
    # geometry (not an accidental launch, drift, or wrong Sun direction).
    if ! awk '
      function value(prefix,    i, pair) {
        for (i = 1; i <= NF; i++)
          if ($i ~ ("^" prefix "=")) {
            split($i, pair, "=")
            return pair[2]
          }
        return ""
      }
      function abs(value) { return value < 0 ? -value : value }
      function reject(message) {
        print "ERROR: atmosphere state gate: " message > "/dev/stderr"
        bad = 1
      }
      /^ATMOS_APPLY / {
        slug = value("slug")
        if (slug == "") { reject("request without slug: " $0); next }
        requestedAlt[slug] = value("targetAlt") + 0
        requestedSun[slug] = value("targetSunElevation") + 0
        requested[slug] = 1
      }
      /^ATMOS_STATE / {
        slug = value("slug")
        if (!(slug in requested)) {
          reject("state has no matching request: " slug)
          next
        }
        actualAlt = value("actualAlt")
        actualSun = value("sunElevation")
        if (actualAlt == "" || actualSun == "") {
          reject("missing actualAlt/sunElevation for " slug)
          next
        }
        if (abs(actualAlt + 0 - requestedAlt[slug]) > 2.0)
          reject(slug " altitude mismatch: actual=" (actualAlt + 0) \
            " target=" requestedAlt[slug] " (tolerance 2 m)")
        if (abs(actualSun + 0 - requestedSun[slug]) > 0.25)
          reject(slug " solar-elevation mismatch: actual=" (actualSun + 0) \
            " target=" requestedSun[slug] " (tolerance 0.25 deg)")
        seen[slug]++
      }
      END {
        for (slug in requested)
          if (!(slug in seen)) reject("missing state for " slug)
        exit bad
      }
    ' "$LOG"; then
      return 1
    fi

    # These are instrumentation/physics guardrails, not a subjective image-quality score.
    # Each threshold targets a failure mode visible in prior regressions:
    #   * regional clipping catches a white surface sky hidden by a normal whole-frame mean;
    #   * true local maxima quantify stars that leak through daylight exposure;
    #   * bright saturated green distinguishes a neon band from dim 557.7 nm airglow;
    #   * off-disc horizon/upper-sky ROIs require an actual twilight gradient.
    if ! awk '
      function remember(slug, key, value) {
        metric[slug SUBSEP key] = value + 0
      }
      function has(slug, key) {
        return (slug SUBSEP key) in metric
      }
      function get(slug, key) {
        return metric[slug SUBSEP key] + 0
      }
      function reject(message) {
        print "ERROR: atmosphere optics gate: " message > "/dev/stderr"
        bad = 1
      }
      function require_metric(slug, key) {
        if (!has(slug, key))
          reject("missing " key " for " slug)
      }
      function maximum(slug, key, limit, label, value) {
        require_metric(slug, key)
        value = get(slug, key)
        if (value > limit)
          reject(label " in " slug ": " key "=" value " > " limit)
      }
      function minimum(slug, key, limit, label, value) {
        require_metric(slug, key)
        value = get(slug, key)
        if (value < limit)
          reject(label " in " slug ": " key "=" value " < " limit)
      }
      /^IMAGE / {
        slug = ""
        for (i = 1; i <= NF; i++) {
          if ($i ~ /^slug=/) {
            split($i, a, "=")
            slug = a[2]
          }
        }
        if (slug == "") {
          reject("IMAGE row without slug: " $0)
          next
        }
        for (i = 1; i <= NF; i++) {
          if (index($i, "=") > 0) {
            split($i, a, "=")
            remember(slug, a[1], a[2])
          }
        }
        mean = get(slug, "mean")
        clip = get(slug, "clippedFrac")
        # A real night-side frame can have a mean below 5e-4 after exposure adapts;
        # reject only an effectively black framebuffer, not a dim star/airglow field.
        if (mean < 0.00002 || mean > 0.9995 || clip > 0.95) {
          reject("degenerate capture " slug ": mean=" mean " clippedFrac=" clip)
        }
      }
      END {
        # A small solar disc/bloom may clip; broad white sky or terrain may not.
        maximum("ground_day", "clippedFrac", 0.20,
          "surface-level daytime frame is broadly clipped")
        maximum("ground_day", "skyWhiteClipFrac", 0.10,
          "daytime sky is white-clipped")
        maximum("ground_day", "skyBrightFrac", 0.55,
          "daytime sky has lost too much luminance structure")
        maximum("ground_day", "surfaceWhiteClipFrac", 0.12,
          "daytime terrain is white-clipped")
        maximum("ground_sunrise", "skyWhiteClipFrac", 0.20,
          "sunrise sky is broadly white-clipped")
        maximum("ground_sunset", "skyWhiteClipFrac", 0.20,
          "sunset sky is broadly white-clipped")

        # With exposure adapted to a lit atmosphere/Earth, point stars should be below
        # framebuffer visibility.  Limits are densities of true local maxima, not raw
        # bright pixels, and still permit a handful of sensor-scale outliers.
        maximum("ground_day", "sharpStarFrac", 0.00010,
          "stars remain visible through daylight")
        maximum("10km_day", "sharpStarFrac", 0.00015,
          "stars remain visible through daylight")
        maximum("30km_day", "sharpStarFrac", 0.00025,
          "stars remain too prominent at daytime exposure")
        maximum("70km_day", "sharpStarFrac", 0.00035,
          "stars remain too prominent beside the lit limb")
        maximum("120km_day", "sharpStarFrac", 0.00035,
          "stars remain too prominent beside the lit Earth")
        maximum("400km_day", "sharpStarFrac", 0.00035,
          "stars remain too prominent beside the lit Earth")

        # Day/night pairs also guard against a renderer that simply erases the starfield
        # in both states to satisfy the absolute daytime limits.
        split("ground 10km 30km 70km 120km 400km", pairNames, " ")
        for (p = 1; p <= 6; p++) {
          daySlug = pairNames[p] "_day"
          nightSlug = pairNames[p] "_night"
          require_metric(daySlug, "sharpStarFrac")
          require_metric(nightSlug, "sharpStarFrac")
          dayStars = get(daySlug, "sharpStarFrac")
          nightStars = get(nightSlug, "sharpStarFrac")
          if (nightStars < 0.00008)
            reject("night starfield is missing for " pairNames[p] \
              ": sharpStarFrac=" nightStars)
          if (nightStars >= 0.00010) {
            ratioLimit = p <= 3 ? 0.45 : 0.65
            if (dayStars > nightStars * ratioLimit)
              reject("insufficient day/night star suppression for " pairNames[p] \
                ": day=" dayStars " night=" nightStars)
          }
        }

        # Real night-side airglow may be green, but it is optically thin: a wide,
        # high-luminance saturated band is a renderer artefact.
        split("70km_night 120km_night 400km_night", nightLimb, " ")
        for (p = 1; p <= 3; p++) {
          maximum(nightLimb[p], "neonGreenFrac", 0.010,
            "night limb is neon green")
          maximum(nightLimb[p], "limbGreenExcess", 0.018,
            "night limb green emission is too intense")
        }

        # Both ±1° cases look toward the projected Sun.  Side ROIs exclude the solar
        # disc, so the signal must come from atmospheric path length and spectral
        # extinction rather than bloom.
        split("ground_sunrise ground_sunset", twilight, " ")
        for (p = 1; p <= 2; p++) {
          minimum(twilight[p], "twilightGradient", 0.010,
            "twilight lacks a horizon-to-upper-sky luminance gradient")
          minimum(twilight[p], "twilightWarmth", 0.006,
            "twilight horizon lacks red/blue spectral separation")
          require_metric(twilight[p], "twilightHorizonMean")
          require_metric("ground_night", "twilightHorizonMean")
          if (get(twilight[p], "twilightHorizonMean") < get("ground_night", "twilightHorizonMean") + 0.012)
            reject(twilight[p] " horizon is not measurably brighter than full night")
        }

        exit bad
      }
    ' "$LOG"; then
      return 1
    fi
  fi

  echo "visual_playtest: verified PNG(s) in $OUT_DIR (min ${min_bytes} bytes)"
}

if [[ "$VERIFY_ONLY" -eq 1 ]]; then
  echo "visual_playtest: verify-only mode=$MODE out=$OUT_DIR log=$LOG"
  verify_pngs
  echo "visual_playtest: verify-only OK"
  exit 0
fi

PLAYTEST_LOCK="/tmp/exosphere-visual-playtest.lock"
exec {PLAYTEST_LOCK_FD}>"$PLAYTEST_LOCK"
if ! flock -n "$PLAYTEST_LOCK_FD"; then
  echo "ERROR: another visual_playtest process is already using the temporary autoload ($PLAYTEST_LOCK)" >&2
  echo "  Check who holds it: lsof \"$PLAYTEST_LOCK\" (or fuser \"$PLAYTEST_LOCK\")" >&2
  echo "  If every listed PID is gone/stuck (e.g. an orphaned dotnet build-server), the lock" >&2
  echo "  releases itself once those processes are killed — it is not a stale pidfile to rm -f." >&2
  exit 1
fi
OWNS_HARNESS=1

register_autoload

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  # Close the lock fd for this child: dotnet build can spawn a long-lived, detached
  # VBCSCompiler/MSBuild worker ("build server") that outlives this script. If that
  # daemon inherited fd $PLAYTEST_LOCK_FD, a later hang in it would keep the flock
  # held forever, orphaning the lock even after this script's own process exits.
  dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet \
    {PLAYTEST_LOCK_FD}>&-
fi

mkdir -p "$OUT_DIR"
rm -f "$OUT_DIR"/exo_play_*.png 2>/dev/null || true
: > "$LOG"
: > "$CONSOLE_LOG"

echo "visual_playtest: mode=$MODE max_runtime=${MAX_RUNTIME_SEC}s out=$OUT_DIR log=$LOG"

if [[ "$MODE" == "reentry_compare" ]]; then
  # Two independent Godot launches — one per attitude — appending into the same combined
  # $LOG/$OUT_DIR. EDLController keeps its own private phase state and (correctly) is never
  # reset by a second in-process BeginReentryDemonstration seed, so a single-process two-seed
  # attempt left EDL stuck past Inactive on the second seed. A fresh process per attitude is
  # exactly --edl's own single-shot pattern, just run twice with a different seeded orientation.
  #
  # Each launch's harness opens its log with StreamWriter(path, append: false) — correct for
  # every other (single-launch) mode, but it means the second launch would truncate away the
  # first launch's telemetry if both wrote directly to $LOG. Give each launch its own temp
  # log instead, then append that into the combined $LOG after the launch exits.
  COMBINED_LOG="$LOG"
  COMBINED_CONSOLE_LOG="$CONSOLE_LOG"
  for pair in "true:reentry_nominal" "false:reentry_bad_attitude"; do
    REENTRY_BELLY_FIRST="${pair%%:*}"
    REENTRY_SLUG="${pair##*:}"
    HARNESS_MODE="reentry_variant"
    LOG="${COMBINED_LOG}.${REENTRY_SLUG}"
    CONSOLE_LOG="${LOG}.console"
    : > "$LOG"
    : > "$CONSOLE_LOG"
    write_harness
    # See the SKIP_BUILD block above: {PLAYTEST_LOCK_FD}>&- keeps the lock fd out of every
    # build/launch child so a hung build-server or stray Godot subprocess can never hold the
    # flock past this script's own lifetime.
    dotnet build Exosphere.csproj --no-restore --nologo -v quiet {PLAYTEST_LOCK_FD}>&-
    EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" \
    xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" \
      --path . --rendering-driver opengl3 \
      res://scenes/flight/Flight.tscn {PLAYTEST_LOCK_FD}>&- 2>&1 | tee -a "$CONSOLE_LOG"
    cat "$LOG" >> "$COMBINED_LOG"
    cat "$CONSOLE_LOG" >> "$COMBINED_CONSOLE_LOG"
    rm -f "$LOG" "$CONSOLE_LOG"
  done
  LOG="$COMBINED_LOG"
  CONSOLE_LOG="$COMBINED_CONSOLE_LOG"
else
  HARNESS_MODE="$MODE"
  write_harness
  dotnet build Exosphere.csproj --no-restore --nologo -v quiet {PLAYTEST_LOCK_FD}>&-
  EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" \
  xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" \
    --path . --rendering-driver opengl3 \
    res://scenes/flight/Flight.tscn {PLAYTEST_LOCK_FD}>&- 2>&1 | tee -a "$CONSOLE_LOG"
fi

verify_pngs

if [[ "$MODE" == "gemini_docking" ]]; then
  omega="$(awk '
    /CAPTURE gemini_docked_anomaly/ {
      for (i = 1; i <= NF; i++)
        if ($i ~ /^omega=/) {
          split($i, pair, "=")
          print pair[2]
          exit
        }
    }' "$LOG")"
  if [[ -z "$omega" ]] || ! awk -v value="$omega" '
      BEGIN { exit !(value >= 0.30 && value <= 0.36) }'; then
    echo "ERROR: Gemini anomaly capture requires 20 deg/s (omega=${omega:-missing} rad/s)." >&2
    exit 1
  fi
fi

if [[ "$MODE" == "lunar_map" ]]; then
  if ! grep -Eq \
      'LUNAR_MAP model=LunarLambert encounter=True tli=[0-9.]+ loi=[0-9.]+ pe=[1-9][0-9.]* tBurn=[1-9][0-9.]*' \
      "$LOG"; then
    echo "ERROR: lunar map capture did not validate Lambert encounter telemetry." >&2
    exit 1
  fi
fi

if [[ "$MODE" == "smoke" ]]; then
  echo "visual_playtest: smoke OK"
elif [[ "$MODE" == "ascent" ]]; then
  echo "visual_playtest: focused ascent diagnostics OK — stable orbit verified"
elif [[ "$MODE" == "edl" ]]; then
  echo "visual_playtest: deterministic EDL verification OK"
elif [[ "$MODE" == "hotstage" ]]; then
  echo "visual_playtest: hot-stage overlap capture OK"
elif [[ "$MODE" == "reentry_compare" ]]; then
  echo "visual_playtest: reentry attitude compare OK — see REENTRY_COMPARE rows in $LOG"
elif [[ "$MODE" == "atmosphere" ]]; then
  echo "visual_playtest: atmosphere matrix OK — compare IMAGE/PERF rows in $LOG"
elif [[ "$MODE" == "lunar_map" ]]; then
  echo "visual_playtest: lunar Lambert map verification OK"
else
  echo "visual_playtest: full run complete — review $LOG for GAP lines"
fi
