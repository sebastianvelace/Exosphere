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
GODOT_LOG_FILE=""
RUN_ID=""
RUN_TOKEN=""
MAX_RUNTIME_SEC="${PLAYTEST_MAX_RUNTIME_SEC:-}"
RESOLUTION="1920x1080"
EXTERNAL_DISPLAY="${EXO_VISUAL_DISPLAY:-}"
VERIFY_ONLY=0
MODE="full"
HARNESS_MODE=""
REENTRY_BELLY_FIRST=""
REENTRY_SLUG=""
SUN_ELEVATION_DEG=""
CAMERA_PRESET=""
VARIANT_FILE=""
VARIANT_SITE=""
VARIANT_PROFILE=""
SKIP_BUILD=0
PROJECT_BACKUP=""
APOLLO11_HARDWARE=0
OWNS_HARNESS=0
OWNS_LOCK=0
CLEANUP_DONE=0

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
  --orbit       Seed standalone Starship at orbit and capture the direct planetary view.
  --cockpit     Capture the first-person cockpit optics and interior.
  --saturn      Jump to Saturn and capture the imported ring texture.
  --atmosphere  Capture a deterministic day/twilight/night altitude matrix with image metrics.
  --atmosphere-ground
                 Capture only Earth ground day/sunrise/sunset/night cases for fast lighting A/B.
  --atmosphere-low  Capture only the deterministic Earth 10 km daylight case for shader A/B work.
  --atmosphere-bodies  Capture explicit Mars/Venus day/orbit/night atmosphere cases.
  --spectral    Run the offline 9-band RGB/LUT comparison for Earth, Mars and Venus.
  --edl         Seed a deterministic 70 km entry and verify physical flip/touchdown.
  --edl-yaw DEG Override the deterministic EDL exterior camera yaw in degrees (default 0).
  --sun-elevation DEG
                Presentation-only solar elevation for comparable captures (range -90..90;
                physical Sun position and forces remain unchanged).
  --camera-preset NAME
                Deterministic composition: pad_side|tower_side|tracking|orbit_beauty|edl_side.
  --orbital-reentry  Seed a Starbase Starship in circular orbit, arm the real map deorbit
                autopilot, and verify normal atmospheric entry through tower catch.
  --hotstage    Fly [G] full ascent (default Flight 7 Starship/Super Heavy) and capture the
                real hot-staging dual-thrust overlap window, gated on vessel state
                (IsHotStageOverlapping), not a frame count.
  --reentry-compare  Capture nominal belly-flop vs. forced bad-attitude (nose-first,
                tumbling) EDL, gated on PEAK_HEATING/destruction, for VFX/thermal comparison.
  --run-id ID    Isolate default artifacts as /tmp/exo_play-ID{,.log}; recommended for agents.
  --resolution WIDTHxHEIGHT
                 Capture framebuffer size (default: 1920x1080; limits: 640x360..7680x4320).
  --display DISPLAY
                 Use an already-running X display (for example localhost:101) instead of
                 starting xvfb-run. The display must already match --resolution.
  --max-runtime SEC  Wall-clock budget (default: 3600 full mission, 1200 ascent,
                      1800 orbital reentry/other modes).
  --verify-only  Re-run artifact/log gates without building or launching Godot.
  --out-dir DIR PNG output directory (default: /tmp/exo_play)
  --log FILE    Telemetry log path (default: /tmp/exo_play.log)
  --skip-build  Skip the simulation-library prebuild (the Godot project is still built)
  -h, --help    Show this help

Environment:
  GODOT_BIN     Path to Godot 4.6 mono binary
  PLAYTEST_MAX_RUNTIME_SEC  Same override as --max-runtime
  EXO_VISUAL_DISPLAY  Optional external X display, equivalent to --display

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
    --orbit) MODE="orbit"; shift ;;
    --cockpit) MODE="cockpit"; shift ;;
    --saturn) MODE="saturn"; shift ;;
    --atmosphere) MODE="atmosphere"; shift ;;
    --atmosphere-ground) MODE="atmosphere_ground"; shift ;;
    --atmosphere-low) MODE="atmosphere_low"; shift ;;
    --atmosphere-bodies) MODE="atmosphere_bodies"; shift ;;
    --spectral) MODE="spectral"; shift ;;
    --edl) MODE="edl"; shift ;;
    --edl-yaw) EDL_YAW_DEG="$2"; shift 2 ;;
    --sun-elevation) SUN_ELEVATION_DEG="$2"; shift 2 ;;
    --camera-preset) CAMERA_PRESET="$2"; shift 2 ;;
    --orbital-reentry)
      MODE="orbital_reentry"
      VARIANT_FILE="starship_flight7_block2_2025.json"
      VARIANT_SITE="starbase"
      VARIANT_PROFILE="starship-flight7-ascent"
      shift ;;
    --hotstage)
      MODE="hotstage"
      VARIANT_FILE="starship_flight7_block2_2025.json"
      VARIANT_SITE="starbase"
      VARIANT_PROFILE="starship-flight7-ascent"
      shift ;;
    --reentry-compare) MODE="reentry_compare"; shift ;;
    --run-id) RUN_ID="$2"; shift 2 ;;
    --resolution) RESOLUTION="$2"; shift 2 ;;
    --display) EXTERNAL_DISPLAY="$2"; shift 2 ;;
    --max-runtime) MAX_RUNTIME_SEC="$2"; shift 2 ;;
    --verify-only) VERIFY_ONLY=1; shift ;;
    --out-dir) OUT_DIR="$2"; OUT_DIR_SET=1; shift 2 ;;
    --log) LOG="$2"; LOG_SET=1; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ ! "${EDL_YAW_DEG:-0}" =~ ^-?[0-9]+([.][0-9]+)?$ ]] \
  || ! awk -v yaw="${EDL_YAW_DEG:-0}" 'BEGIN { exit !(yaw >= -180.0 && yaw <= 180.0) }'; then
  echo "ERROR: --edl-yaw must be a number between -180 and 180 degrees" >&2
  exit 2
fi
EDL_YAW_DEG="${EDL_YAW_DEG:-0}"

SUN_ELEVATION_SET=0
if [[ -n "$SUN_ELEVATION_DEG" ]]; then
  if [[ ! "$SUN_ELEVATION_DEG" =~ ^-?[0-9]+([.][0-9]+)?$ ]] \
    || ! awk -v elevation="$SUN_ELEVATION_DEG" \
      'BEGIN { exit !(elevation >= -90.0 && elevation <= 90.0) }'; then
    echo "ERROR: --sun-elevation must be a number between -90 and 90 degrees" >&2
    exit 2
  fi
  SUN_ELEVATION_SET=1
else
  SUN_ELEVATION_DEG="0"
fi

case "$CAMERA_PRESET" in
  ""|pad_side|tower_side|tracking|orbit_beauty|edl_side) ;;
  *)
    echo "ERROR: --camera-preset must be pad_side, tower_side, tracking, orbit_beauty or edl_side" >&2
    exit 2
    ;;
esac

if [[ ! "$RESOLUTION" =~ ^([0-9]{1,4})x([0-9]{1,4})$ ]]; then
  echo "ERROR: --resolution must use WIDTHxHEIGHT (for example 1280x720)" >&2
  exit 2
fi
RESOLUTION_WIDTH="${BASH_REMATCH[1]}"
RESOLUTION_HEIGHT="${BASH_REMATCH[2]}"
if [[ -n "$EXTERNAL_DISPLAY" && ! "$EXTERNAL_DISPLAY" =~ ^[A-Za-z0-9._-]+:[0-9]+$ ]]; then
  echo "ERROR: --display must use DISPLAY syntax such as localhost:101 or :101" >&2
  exit 2
fi
if (( RESOLUTION_WIDTH < 640 || RESOLUTION_WIDTH > 7680
    || RESOLUTION_HEIGHT < 360 || RESOLUTION_HEIGHT > 4320
    || RESOLUTION_WIDTH * RESOLUTION_HEIGHT > 33177600 )); then
  echo "ERROR: --resolution must be between 640x360 and 7680x4320, with at most 33177600 pixels" >&2
  exit 2
fi

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
  elif [[ "$MODE" == "orbital_reentry" ]]; then
    # This is deliberately bounded: it validates one prepared orbit and one normal
    # deorbit/EDL pass, never an open-ended campaign or a demo fallback. The CPU/Xvfb
    # renderer needs more wall time than the direct 70 km EDL demonstration because
    # this path also integrates the 1,200 km coast and the real deorbit burn.
    MAX_RUNTIME_SEC=1800
  else
    MAX_RUNTIME_SEC=1800
  fi
fi
if [[ ! "$MAX_RUNTIME_SEC" =~ ^[0-9]+$ ]] \
  || (( MAX_RUNTIME_SEC < 60 || MAX_RUNTIME_SEC > 7200 )); then
  echo "ERROR: --max-runtime must be an integer between 60 and 7200 seconds" >&2
  exit 2
fi

# The spectral matrix is a CPU validation harness, not a Godot scene. Keep it on the same
# entry point as the visual matrix so acceptance jobs can request either artifact family,
# while avoiding a temporary autoload and a framebuffer for an offline comparison.
if [[ "$MODE" == "spectral" ]]; then
  mkdir -p "$OUT_DIR"
  dotnet run --project tools/SpectralValidation/SpectralValidation.csproj \
    --no-restore -- "$OUT_DIR"
  echo "visual_playtest: spectral comparison PASS; artifacts=$OUT_DIR"
  exit 0
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
      grep -aE '^(SUMMARY|PARTIAL|FAIL|GAP|FALLBACK) ' "$LOG" | tail -20 || true
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
    grep -aE '^(CAPTURE|TRACE_ASCENT|TRANSITION_ASCENT|ASCENT_METRICS|TRACE_FULL|TRACE |FAIL|GAP|PARTIAL|FALLBACK|SUMMARY)' "$LOG" \
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

prepare_godot_log_file() {
  # Godot's default crash log lives under user://logs. In this environment the
  # crash handler can fail to create that directory and then abort the process
  # before the scene starts. --log-file is supported by the installed 4.6.3
  # binary; keep one explicit native log per launch beside the harness console
  # log so every run is reproducible and isolated by --run-id/--log.
  GODOT_LOG_FILE="${CONSOLE_LOG}.godot"
  mkdir -p "$(dirname "$GODOT_LOG_FILE")"
  : > "$GODOT_LOG_FILE"
}

cleanup() {
  local ec=$?
  if [[ "$CLEANUP_DONE" -eq 1 ]]; then
    return
  fi
  CLEANUP_DONE=1
  set +e
  local status="FAIL"
  if [[ $ec -eq 130 || $ec -eq 143 ]]; then
    status="PARTIAL"
    if [[ -f "$LOG" ]]; then
      echo "PARTIAL reason=INTERRUPTED exit=$ec mode=$MODE" >> "$LOG"
    fi
  fi
  if [[ "$status" == "PARTIAL" ]]; then
    write_run_summary "$status"
    print_failure_diagnostics
  elif [[ $ec -eq 0 ]]; then
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
  if [[ "$OWNS_LOCK" -eq 1 ]]; then
    rm -f "$PLAYTEST_LOCK/owner" 2>/dev/null || true
    rmdir "$PLAYTEST_LOCK" 2>/dev/null || true
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
if [[ -n "$EXTERNAL_DISPLAY" ]]; then
  if ! command -v xdpyinfo >/dev/null 2>&1; then
    echo "ERROR: xdpyinfo is required to validate --display" >&2
    exit 1
  fi
  if ! DISPLAY="$EXTERNAL_DISPLAY" xdpyinfo >/dev/null 2>&1; then
    echo "ERROR: external display is not reachable: $EXTERNAL_DISPLAY" >&2
    exit 1
  fi
elif ! command -v xvfb-run >/dev/null 2>&1; then
  echo "ERROR: xvfb-run not found (install xvfb), or pass --display DISPLAY" >&2
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
using Exosphere.Simulation.Presentation;
using Exosphere.Simulation.Propulsion;
using Exosphere.Simulation.Systems;

/// <summary>
/// Temporary visual playtest autoload — generated by tools/visual_playtest.sh; never commit.
/// </summary>
public partial class _PlaytestShot : Node
{
    const double FluxPeak = 6.0e5;
    const int SettleFrames = 4;
    const double MaxRuntimeSec = ${MAX_RUNTIME_SEC}.0;
    const double AscentFallbackSec = 720.0;
    // Keep these runtime fields rather than compile-time constants. A matrix mode that
    // does not request a visual override must still compile the shared branch without
    // CS0162 unreachable-code warnings in the temporary harness.
    static readonly bool HasVisualSunElevation = ${SUN_ELEVATION_SET} == 1;
    static readonly double VisualSunElevationDeg = ${SUN_ELEVATION_DEG};
    const string VisualCameraPreset = "${CAMERA_PRESET}";

    readonly string _mode;
    readonly string _outDir;

    StreamWriter _log = null!;
    double _t0;
    double _lastProcessWallSeconds;
    int _frame;
    int _readyFrames;
    int _settleLeft;
    string? _pendingSlug;

    bool _pad, _liftoff, _maxq, _separation, _orbit, _orbitBeauty;
    bool _entry, _peak, _retro, _landed, _caught;
    bool _ascentEngaged, _deorbitStarted, _deorbitDone, _ascentFallbackUsed;
    bool _beautyJumped;
    int _beautyWaitFrames;
    bool _edlSeeded, _flipComplete, _shipSeeded;
    double _edlScenarioStart, _retroStart = -1.0, _nextEdlTelemetry;
    double _nextFullTelemetry;
    double _lastApproachSpeed = double.NaN;
    Vector3d _originalMoonPosition;
    Vector3d _originalMoonVelocity;
    bool _moonSnapshot;
    bool _finished;
    bool _authorized;
    bool _visualConfigurationApplied;

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
    bool _hotstage, _hotstageSeparation;

    // ── reentry_variant mode (one attitude per process; --reentry-compare drives two
    // separate Godot launches — see tools/visual_playtest.sh orchestration below) ──────
    bool _reentrySeeded, _reentryQueued;
    double _reentryScenarioStart;

    // ── orbital_reentry mode ────────────────────────────────────────────────
    // This path deliberately starts from an explicit circular-orbit setup, then uses the
    // production map deorbit planner/autopilot and normal EDL activation. It never calls
    // BeginReentryDemonstration; the log names the setup teleport so the acceptance result
    // cannot be mistaken for a flown ascent or the deterministic demo.
    bool _orbitalReentrySeeded, _orbitalReentryEntry, _orbitalReentryPeak;
    bool _orbitalReentryRetro, _orbitalReentryCaught;
    double _orbitalReentryScenarioStart, _nextOrbitalReentryTelemetry;
    double _orbitalReentrySeededPe = double.NaN;

    // Atmosphere acceptance is deliberately state-seeded instead of flown.  This makes
    // every altitude/solar-elevation pair reproducible and keeps the matrix fast enough
    // to run while tuning the scattering shader.  Cockpit cases are last because the
    // public camera API intentionally only enters (rather than exits) IVA mode.
    readonly (string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit, string Eclipse)[] _atmosCases =
    {
        ("ground_day",       20.0,  45.0, false, "none"),
        ("ground_sunrise",   20.0,  -1.0, false, "none"),
        ("ground_sunset",    20.0,   1.0, false, "none"),
        ("ground_night",     20.0, -35.0, false, "none"),
        ("10km_day",     10_000.0,  35.0, false, "none"),
        ("30km_day",     30_000.0,  35.0, false, "none"),
        ("70km_day",     70_000.0,  35.0, false, "none"),
        ("120km_day",   120_000.0,  35.0, false, "none"),
        ("400km_day",   400_000.0,  35.0, false, "none"),
        ("10km_night",   10_000.0, -35.0, false, "none"),
        ("30km_night",   30_000.0, -35.0, false, "none"),
        ("70km_night",   70_000.0, -35.0, false, "none"),
        ("120km_night", 120_000.0, -35.0, false, "none"),
        ("400km_night", 400_000.0, -35.0, false, "none"),
        ("eclipse_clear",          120_000.0, 35.0, false, "clear"),
        ("eclipse_partial_central",120_000.0, 35.0, false, "partial_central"),
        ("eclipse_partial_limb",   120_000.0, 35.0, false, "partial_limb"),
        ("eclipse_total",           120_000.0, 35.0, false, "total"),
        ("cockpit_120km_day",   120_000.0,  35.0, true, "none"),
        ("cockpit_120km_night", 120_000.0, -35.0, true, "none"),
    };
    private bool IsSingleAtmosphereCase => _mode == "atmosphere_low";
    private bool IsGroundAtmosphereCase => _mode == "atmosphere_ground";
    private (string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit, string Eclipse)
        CurrentAtmosphereCase() => IsSingleAtmosphereCase ? _atmosCases[4] : _atmosCases[_atmosIndex];
    // Mars/Venus are deliberately a separate matrix.  The Earth set above is an existing
    // acceptance baseline and must remain byte-for-byte reproducible; this set exercises
    // the same renderer contract after a real body transition, including the lazy planet
    // presentation path.  The orbital cases are outside the dense lower atmosphere on
    // purpose: they validate the optically thin limb/space transition rather than hiding a
    // body-specific profile behind a copied Earth altitude.
    readonly (string BodyId, string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit)[] _atmosBodyCases =
    {
        ("mars",  "mars_10km_day",   10_000.0,  35.0, false),
        ("mars",  "mars_400km_day", 400_000.0,  35.0, false),
        ("mars",  "mars_10km_night", 10_000.0, -35.0, false),
        ("venus", "venus_10km_day",  10_000.0,  35.0, false),
        ("venus", "venus_400km_day",400_000.0,  35.0, false),
        ("venus", "venus_10km_night",10_000.0, -35.0, false),
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
    bool _atmosBodyCaseApplied;
    string? _atmosBodyTransitionRequested;
    Vector3d _atmosUp = Vector3d.Up, _atmosLook = Vector3d.Forward;
    double _atmosFrameSeconds, _atmosMaxFrameSeconds;
    int _atmosPerfFrames, _atmosSlowFrames;
    double _atmosPreviousExposure = -1.0;
    int _atmosExposureStableFrames;
    SpectralAtmosphereOracle? _spectralOracle;
    string? _spectralBodyId;

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
        _lastProcessWallSeconds = _t0;
        ProcessMode = ProcessModeEnum.Always;
        // Controllers run at priority 100. Diagnostics run later so their requested
        // bounded RK4 x2 acceleration is not immediately overwritten back to real time.
        ProcessPriority = 200;
    }

    public override void _Process(double delta)
    {
        if (!_authorized || _finished) return;
        _frame++;

        double nowWallSeconds = Time.GetTicksMsec() / 1000.0;
        double frameSeconds = nowWallSeconds - _lastProcessWallSeconds;
        _lastProcessWallSeconds = nowWallSeconds;
        double elapsed = nowWallSeconds - _t0;
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
        if (frameSeconds > 0.0 && double.IsFinite(frameSeconds))
        {
            var schedulerTelemetry = universe.LastSchedulerTelemetry;
            string catchUpRisk = schedulerTelemetry.CatchUpRisk ? "true" : "false";
            string schedulerInitialized = schedulerTelemetry.IsInitialized ? "true" : "false";
            _log.WriteLine($"PERF_FRAME frame={_frame} frame_ms={frameSeconds * 1000.0:F3} " +
                $"scheduler_ms={schedulerTelemetry.WallClockMilliseconds:F3} " +
                $"scheduler_branch={schedulerTelemetry.Branch} " +
                $"scheduler_substeps={schedulerTelemetry.OuterSubsteps} " +
                $"scheduler_cap={schedulerTelemetry.EffectiveStepCap:F6} " +
                $"scheduler_simulated={schedulerTelemetry.SimulatedSeconds:F6} " +
                $"catch_up_risk={catchUpRisk} " +
                "source=process_callback");
            _log.WriteLine($"PERF_SCHEDULER schema=2 frame={_frame} " +
                $"initialized={schedulerInitialized} " +
                $"skip_reason={schedulerTelemetry.SkipReason} " +
                $"branch={schedulerTelemetry.Branch} " +
                $"substeps={schedulerTelemetry.OuterSubsteps} " +
                $"full_physics={schedulerTelemetry.FullPhysicsDispatches} " +
                $"on_rails={schedulerTelemetry.OnRailsDispatches} " +
                $"surface_settled={schedulerTelemetry.SurfaceSettledDispatches} " +
                $"ground_held={schedulerTelemetry.GroundHeldDispatches} " +
                $"destroyed={schedulerTelemetry.DestroyedDispatches} " +
                $"docked_skips={schedulerTelemetry.DockedSecondarySkips} " +
                $"rails_slices={schedulerTelemetry.RailsSlices} " +
                $"docking_constraints={schedulerTelemetry.DockingConstraintApplications} " +
                $"deadline_eligible={schedulerTelemetry.DeadlineEligibleEvaluations} " +
                $"deadline_deferred={schedulerTelemetry.DeadlineDeferredSkips} " +
                $"deadline_catch_up={schedulerTelemetry.DeadlineCatchUpDispatches} " +
                $"deadline_projected={schedulerTelemetry.DeadlineProjectedDispatches} " +
                $"requested_simulated={schedulerTelemetry.RequestedSimulationSeconds:F6} " +
                $"processed_simulated={schedulerTelemetry.ProcessedSimulationSeconds:F6} " +
                $"pending_simulated={schedulerTelemetry.PendingSimulationSeconds:F6} " +
                $"budget_limited={(schedulerTelemetry.BudgetLimited ? "true" : "false")} " +
                $"budget_reason={schedulerTelemetry.BudgetReason} " +
                $"total_work={schedulerTelemetry.TotalWorkDispatches} " +
                "source=process_callback");
            _log.WriteLine($"PERF_SCHEDULER_CANDIDATE schema=1 frame={_frame} " +
                $"enabled={(universe.DeferredPhysicsCandidateEnabled ? "true" : "false")} " +
                $"deferred_skips={schedulerTelemetry.CandidateDeferredSkips} " +
                "source=process_callback");
            if ((_frame & 31) == 0) _log.Flush();
        }
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
        ApplyVisualCaptureConfiguration(bridge, vessel, universe, body);

        if (_mode == "atmosphere_bodies")
        {
            ProcessAtmosphereBodies(delta, bridge, vessel, universe, body);
            return;
        }

        if (_mode == "atmosphere" || _mode == "atmosphere_ground")
        {
            ProcessAtmosphereMatrix(delta, bridge, vessel, universe, body);
            return;
        }

        if (_mode == "atmosphere_low")
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

        if (_mode == "saturn")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                // Keep the production presentation path: stage the default stack, use
                // the public body jump, and let SimulationBridge queue Saturn lazily.
                bridge.TriggerStaging();
                bridge.JumpToBody("saturn");
                // The public jump positions the vessel at Saturn, while this frame places
                // the camera almost on the outward radial so the body/ring is in view.
                CameraController.Instance?.SetExternalChaseFrame(0f, 82f, 38f);
                _shipSeeded = true;
                _readyFrames = 0;
                return;
            }
            if (_shipSeeded && _pendingSlug == null && _readyFrames >= 120 && !_orbitBeauty)
            {
                QueueCapture("saturn_ring");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("SATURN_OK");
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
                // The ship-only row is an exterior beauty capture, not a continuation of
                // the pad preset. Explicitly select the production chase framing after the
                // teleport so stale pad yaw/distance cannot contaminate the orbital image.
                CameraController.Instance?.EnterShipChaseView();
                _shipSeeded = true;
                return;
            }
            if (_shipSeeded && _pendingSlug == null && _readyFrames >= 110 && !_orbitBeauty)
            {
                var padNode = GetTree().Root.FindChild("LaunchPadController", true, false) as Node3D;
                var rendererNode = GetTree().Root.FindChild("ActiveVesselRenderer", true, false) as Node3D;
                _log.WriteLine($"VISUAL_NODES ship padVisible={padNode?.Visible.ToString() ?? "missing"} " +
                    $"padChildren={padNode?.GetChildCount() ?? -1} " +
                    $"rendererPos={rendererNode?.Position.ToString() ?? "missing"} " +
                    $"rendererVisible={rendererNode?.Visible.ToString() ?? "missing"}");
                _log.Flush();
                QueueCapture("ship_vacuum");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("SHIP_OK");
            return;
        }

        if (_mode == "orbit")
        {
            if (!_shipSeeded && _readyFrames >= 45)
            {
                // This is a presentation-only seed for visual shader validation. It
                // uses the same public staging/jump path as --ship, but leaves the
                // engines off so the planetary backdrop can be inspected directly.
                bridge.TriggerStaging();
                bridge.JumpToOrbit(200_000.0);
                bridge.SetThrottle(0.0);
                // Pull the chase camera back so the direct orbital Earth test measures
                // the planetary presentation rather than a close-up of the ship mesh.
                CameraController.Instance?.SetExternalChaseFrame(0f, 45f, 400_000f);
                _shipSeeded = true;
                return;
            }
            if (_shipSeeded && _pendingSlug == null && _readyFrames >= 110 && !_orbitBeauty)
            {
                QueueCapture("orbit_direct");
                _orbitBeauty = true;
            }
            if (_orbitBeauty && _pendingSlug == null)
                Finish("ORBIT_DIRECT_OK");
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

        if (_mode == "orbital_reentry")
        {
            ProcessOrbitalReentry(bridge, vessel, universe, body, mission, alt, surfVel, phase);
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
        if (_mode is "hotstage" or "ascent" or "full"
            && _hotstage
            && !_hotstageSeparation
            && _pendingSlug == null
            && bridge.ActiveVessel?.IsHotStageOverlapping == false
            && mission?.Phase is MissionPhase.SEPARATION or MissionPhase.ASCENT_SHIP)
        {
            QueueCapture("hotstage_separation");
            _hotstageSeparation = true;
            _log.WriteLine($"ACTION hot-stage separation detected phase={phase} alt={alt:F0} spd={spd:F0}");
            _log.Flush();
        }
        if (_mode == "hotstage" && _hotstageSeparation && _pendingSlug == null)
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
            CameraController.Instance?.SetExternalChaseFrame(${EDL_YAW_DEG}f, 12.0f, 28.0f);
            _log.WriteLine("EDL_CAMERA yawDeg=${EDL_YAW_DEG} pitchDeg=12 distance=28");
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
            var cluster = vessel.Parts.ActiveEngines.FirstOrDefault();
            double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(up);
            Vector3d catchOffset = vessel.Position - vessel.CatchTargetPositionWorld;
            Vector3d catchHorizontalOffset = catchOffset - up * catchOffset.Dot(up);
            int contacts = vessel.LastSurfaceContact?.ContactCount ?? 0;
            double maxStroke = vessel.LastSurfaceContact?.Points.Max(p => p.PenetrationM) ?? 0.0;
            double peakLegLoad = vessel.LastSurfaceContact?.Points.Max(p => p.NormalLoadN) ?? 0.0;
            double minCatchGap = vessel.LastCatchContact?.Points.Min(p => p.SignedGapM) ?? double.NaN;
            double catchRange = (vessel.Position - vessel.CatchTargetPositionWorld).Magnitude;
            double maxCatchPinY = vessel.CatchContactPoints
                .Select(point => point.LocalPositionFromDatum.Y)
                .DefaultIfEmpty(double.NaN)
                .Max();
            _log.WriteLine($"TRACE t={simElapsed:F1} alt={alt:F1} vUp={vUp:F1} horiz={horizontal:F1} " +
                $"throttle={vessel.Throttle:F3} spool={cluster?.ThrottleLevel ?? 0.0:F3} " +
                $"engines={cluster?.SelectedEngineCount ?? 0} upright={upright:F4} phase={phase} " +
                $"catchArmed={vessel.IsAttemptingTowerCatch} catchPins={vessel.HasCatchPins} " +
                $"catchMiss={catchHorizontalOffset.Magnitude:F1} catchAlt={body.GetAltitude(vessel.CatchTargetPositionWorld):F1} " +
                $"catchGap={minCatchGap:F3} catchRange={catchRange:F1} " +
                $"evalRange={vessel.LastCatchEvaluationRangeM:F1} evalGate={vessel.LastCatchEvaluationPassedGate} pinY={maxCatchPinY:F1} " +
                $"rails={vessel.IsOnRails} contacts={contacts} maxStroke={maxStroke:F3} peakLegLoad={peakLegLoad:F0} " +
                $"settled={vessel.IsSurfaceSettled}");
            _log.Flush();
            _nextEdlTelemetry = simElapsed + 5.0;
        }

        if (!_entry && mission?.Phase is MissionPhase.ENTRY or MissionPhase.PEAK_HEATING
                or MissionPhase.AERO_DESCENT or MissionPhase.RETRO_BURN
                or MissionPhase.FINAL_DESCENT or MissionPhase.LANDED
                or MissionPhase.CAUGHT)
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
                // The flip is captured at real-time scale for readable evidence. Once the
                // attitude gate has passed, accelerate only the validation run's final
                // descent; the same EDL/engine/contact physics continues to execute.
                bridge.SetTimeScale(6.0);
                int engines = vessel.Parts.ActiveEngines.FirstOrDefault()?.SelectedEngineCount ?? 0;
                _log.WriteLine($"CHECK finite_flip duration={universe.CurrentTime - _retroStart:F2}s " +
                    $"alignment={alignment:F5} omega={vessel.AngularVelocity.Magnitude:F4} engines={engines}");
                _log.Flush();
            }
        }

        if (!_caught && mission?.Phase == MissionPhase.CAUGHT)
        {
            QueueCapture("caught");
            _caught = true;
            _log.WriteLine($"CHECK tower_catch caught=True pins={vessel.LastCatchContact?.ContactCount ?? 0} " +
                $"relativeSpeed={(vessel.Velocity - vessel.CatchTargetVelocityWorld).Magnitude:F3} " +
                $"angularSpeed={vessel.AngularVelocity.Magnitude:F4}");
            _log.Flush();
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

        if (_caught && _pendingSlug == null)
        {
            Finish("CAUGHT");
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

    private void ProcessOrbitalReentry(SimulationBridge bridge, Vessel vessel, Universe universe,
        CelestialBody body, MissionManager? mission, double alt, Vector3d surfVel, string phase)
    {
        // Earth keeps active vessels on bounded RK4 through the modeled 1,000 km
        // thermosphere. Start above that boundary so the coast can use analytic rails;
        // the real deorbit burn targets the player-safe 60 km periapsis and re-enters normally.
        const double OrbitAltitudeM = 1_200_000.0;
        const double DeorbitTargetPeM = 60_000.0;
        // A 1,200 km circular setup needs roughly half an orbital period to reach its
        // 60 km periapsis after the impulsive burn. Keep this as simulated time; the
        // wall-clock budget remains controlled by the shell harness.
        const double SimTimeoutSec = 6_000.0;

        if (!_orbitalReentrySeeded)
        {
            if (_readyFrames < 45) return;

            // Keep this acceptance mode narrow and explicit. A different site or vehicle
            // would exercise a different policy and must not silently pass as Starbase.
            if (!string.Equals("${VARIANT_SITE}", "starbase", StringComparison.OrdinalIgnoreCase))
            {
                _log.WriteLine("GAP normal orbital reentry requires launchSite=starbase " +
                    "(scenario was not armed)");
                _log.Flush();
                Finish("ORBITAL_REENTRY_UNAVAILABLE");
                return;
            }

            var earth = universe.GetBody("earth");
            if (earth == null)
            {
                _log.WriteLine("GAP normal orbital reentry requires Earth");
                _log.Flush();
                Finish("ORBITAL_REENTRY_UNAVAILABLE");
                return;
            }

            bool hasShip = vessel.Parts.Parts.Any(part =>
                part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("ship_engines"));
            bool hasBooster = vessel.Parts.Parts.Any(part =>
                part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("booster"));
            if (hasBooster)
            {
                // Setup only: normal reentry must be evaluated on the standalone Ship.
                bridge.TriggerStaging();
                vessel = bridge.ActiveVessel!;
                hasShip = vessel.Parts.Parts.Any(part =>
                    part.Definition.IsStarshipFamily
                    && part.Definition.HasVehicleRole("ship_engines"));
            }
            if (!hasShip || vessel.IsDestroyed)
            {
                _log.WriteLine("GAP normal orbital reentry requires a standalone Starship " +
                    "ship_engines vessel after staging");
                _log.Flush();
                Finish("ORBITAL_REENTRY_UNAVAILABLE");
                return;
            }

            // Explicit setup teleport: it establishes a reproducible safe orbit, but does
            // not enter EDL and is never reported as a normal reentry milestone.
            bridge.JumpToOrbit(OrbitAltitudeM);
            vessel = bridge.ActiveVessel!;
            earth = universe.GetBody("earth")!;
            var seededOrbit = OrbitalElements.FromStateVector(
                vessel.Position - earth.Position,
                vessel.Velocity - earth.Velocity,
                earth.GM,
                earth.Id,
                universe.CurrentTime);
            double seededPe = seededOrbit.Periapsis - earth.Radius;
            double seededAp = seededOrbit.Apoapsis - earth.Radius;
            double atmosphereTop = earth.Atmosphere?.MaxAltitude ?? double.NaN;
            bool finiteOrbit = double.IsFinite(seededPe)
                && double.IsFinite(seededAp)
                && double.IsFinite(atmosphereTop);
            bool safeOrbit = finiteOrbit
                && seededPe >= atmosphereTop
                && seededAp >= seededPe;
            if (!safeOrbit)
            {
                _log.WriteLine($"GAP normal orbital setup is not a safe closed orbit " +
                    $"pe={seededPe:F1} ap={seededAp:F1} atmoTop={atmosphereTop:F1}");
                _log.Flush();
                Finish("ORBITAL_REENTRY_SETUP_INVALID");
                return;
            }

            var map = MapViewController.Instance;
            if (map == null)
            {
                _log.WriteLine("GAP normal orbital reentry map planner/autopilot is unavailable");
                _log.Flush();
                Finish("ORBITAL_REENTRY_UNAVAILABLE");
                return;
            }

            // Invoke the same public input path as the player: B plans the deorbit and
            // Enter arms the local AutopilotController. No private controller state is
            // reached from this temporary harness.
            if (!map.Visible) map.ToggleVisible();
            map._UnhandledInput(new InputEventKey
            {
                Keycode = Key.B,
                Pressed = true,
            });
            if (!map.Planner.HasNode
                || map.Planner.DvPrograde >= -50.0
                || !double.IsFinite(map.Planner.DeltaVMagnitude))
            {
                _log.WriteLine($"GAP normal deorbit planner refused targetPe={DeorbitTargetPeM:F1} " +
                    $"dv={map.Planner.DeltaVMagnitude:F1} prograde={map.Planner.DvPrograde:F1}");
                _log.Flush();
                Finish("ORBITAL_REENTRY_DEORBIT_UNAVAILABLE");
                return;
            }
            double plannedDv = map.Planner.DeltaVMagnitude;
            map._UnhandledInput(new InputEventKey
            {
                Keycode = Key.Enter,
                Pressed = true,
            });
            if (mission?.Phase != MissionPhase.COAST)
            {
                _log.WriteLine($"GAP normal deorbit autopilot refused arm phase={mission?.Phase} " +
                    $"dv={plannedDv:F1}");
                _log.Flush();
                Finish("ORBITAL_REENTRY_DEORBIT_UNAVAILABLE");
                return;
            }
            if (map.Visible) map.ToggleVisible();
            CameraController.Instance?.EnterShipChaseView();

            _log.WriteLine($"NORMAL_REENTRY_SETUP source=JumpToOrbit altitude={OrbitAltitudeM:F0} " +
                $"pe={seededPe:F1} ap={seededAp:F1} atmoTop={atmosphereTop:F1} " +
                "launchSite=starbase demo=False flownAscent=False");
            _log.WriteLine($"NORMAL_REENTRY_ARMED source=map_deorbit_autopilot targetPe={DeorbitTargetPeM:F0} " +
                $"dv={plannedDv:F1} phase={mission?.Phase} launchSite=starbase demo=False");
            _log.Flush();
            QueueCapture("orbital_reentry_orbit");
            _orbitalReentrySeeded = true;
            _orbitalReentryScenarioStart = universe.CurrentTime;
            _nextOrbitalReentryTelemetry = universe.CurrentTime;
            _orbitalReentrySeededPe = seededPe;
            bridge.SetTimeScale(3.0);
            return;
        }

        double simElapsed = universe.CurrentTime - _orbitalReentryScenarioStart;
        var earthBody = universe.GetBody("earth");
        if (earthBody == null)
        {
            _log.WriteLine("FAIL normal orbital reentry lost Earth reference");
            _log.Flush();
            Finish("ORBITAL_REENTRY_INVALID");
            return;
        }

        if (universe.CurrentTime >= _nextOrbitalReentryTelemetry)
        {
            var trajectory = OrbitalElements.FromStateVector(
                vessel.Position - earthBody.Position,
                vessel.Velocity - earthBody.Velocity,
                earthBody.GM,
                earthBody.Id,
                universe.CurrentTime);
            Vector3d up = (vessel.Position - earthBody.Position).Normalized;
            double vUp = surfVel.Dot(up);
            double pe = trajectory.Periapsis - earthBody.Radius;
            double ap = trajectory.Apoapsis - earthBody.Radius;
            Vector3d thrustAxis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            Vector3d retroDirection = surfVel.Magnitude > 1.0
                ? -surfVel.Normalized : Vector3d.Zero;
            double retroAlignment = retroDirection.MagnitudeSquared > 0.0
                ? thrustAxis.Dot(retroDirection) : double.NaN;
            double thrustN = vessel.ComputeThrust(earthBody).Magnitude;
            var activeEnginePart = vessel.Parts.ActiveEngines.FirstOrDefault();
            var allEngineParts = vessel.Parts.Parts.Where(
                part => part.Definition.Category == PartCategory.Engine).ToArray();
            int activeEngines = vessel.Parts.ActiveEngines.Count();
            int failedEngines = allEngineParts.Sum(part => part.EngineStates
                .Count(state => !string.IsNullOrWhiteSpace(state.FailureCode)));
            string failureCodes = allEngineParts.Length == 0
                ? "-"
                : string.Join("|", allEngineParts.Select(part =>
                    $"{part.Definition.Id}:{(part.IsBroken ? "BROKEN" :
                        part.IsStagingActive ? "STAGE" : "INACTIVE")}:" +
                    string.Join(",", part.EngineStates.Select(state => state.FailureCode ?? "-"))));
            double propellant = vessel.Parts.Parts.Sum(part => part.LiquidFuel + part.Oxidizer);
            var autopilot = GetTree().Root.FindChild("AutopilotController", true, false)
                as AutopilotController;
            _log.WriteLine($"TRACE_ORBITAL_REENTRY t={simElapsed:F1} alt={alt:F1} " +
                $"vUp={vUp:F1} spd={surfVel.Magnitude:F1} pe={pe:F1} ap={ap:F1} " +
                $"phase={phase} throttle={vessel.Throttle:F3} " +
                $"activeEngines={activeEngines} thrustN={thrustN:F0} " +
                $"retroAlignment={retroAlignment:F4} pyr={vessel.PitchYawRoll} " +
                $"failedEngines={failedEngines} failureCodes={failureCodes} " +
                $"propellant={propellant:F0} " +
                $"autopilotArmed={autopilot?.IsArmed ?? false} " +
                $"autopilotBurning={autopilot?.IsBurning ?? false} " +
                $"catchArmed={vessel.IsAttemptingTowerCatch} " +
                $"catchPins={vessel.HasCatchPins} destroyed={vessel.IsDestroyed} " +
                "normalFlow=True demo=False");
            _log.Flush();
            _nextOrbitalReentryTelemetry = universe.CurrentTime + 10.0;

            // A deorbit burn must either lower the measured periapsis or leave the
            // RETRO_BURN state. The ship rotates on the modeled RCS floor while throttle
            // is inhibited, so allow that bounded alignment window before declaring a
            // controller stall.
            // Waiting beyond this bounded window would only turn a controller stall into a
            // misleading timeout. This is evidence of a limitation, never a success path.
            if (!_orbitalReentryEntry
                && simElapsed > 150.0
                && phase == nameof(MissionPhase.RETRO_BURN)
                && alt > 200_000.0
                && pe > _orbitalReentrySeededPe - 5_000.0)
            {
                _log.WriteLine($"GAP normal deorbit made no physical progress within 150s " +
                    $"phase={phase} alt={alt:F1} pe={pe:F1} seededPe={_orbitalReentrySeededPe:F1} " +
                    "possible_autopilot_or_power_abort=True");
                _log.Flush();
                Finish("ORBITAL_REENTRY_DEORBIT_STALLED");
                return;
            }
        }

        // RETRO_BURN is also the pre-entry deorbit phase. Do not classify that
        // phase as atmospheric entry or the stall watchdog above would be disabled
        // exactly when the map autopilot is stuck before lowering periapsis.
        if (!_orbitalReentryEntry && mission?.Phase is MissionPhase.ENTRY
                or MissionPhase.PEAK_HEATING or MissionPhase.AERO_DESCENT
                or MissionPhase.FINAL_DESCENT or MissionPhase.CAUGHT)
        {
            QueueCapture("orbital_reentry_entry");
            _orbitalReentryEntry = true;
            bridge.SetTimeScale(3.0);
        }
        if (!_orbitalReentryPeak && mission?.Phase == MissionPhase.PEAK_HEATING)
        {
            QueueCapture("orbital_reentry_peak_heating");
            _orbitalReentryPeak = true;
        }
        if (_orbitalReentryEntry && !_orbitalReentryRetro
            && mission?.Phase == MissionPhase.RETRO_BURN)
        {
            QueueCapture("orbital_reentry_retro_burn");
            _orbitalReentryRetro = true;
            bridge.SetTimeScale(1.0);
        }

        if (vessel.IsDestroyed)
        {
            _log.WriteLine($"FAIL normal orbital Starbase reentry destroyed phase={phase} " +
                $"cause={vessel.DestructionCause} alt={alt:F1} spd={surfVel.Magnitude:F1}");
            _log.Flush();
            Finish("ORBITAL_REENTRY_CRASHED");
            return;
        }

        if (mission?.Phase == MissionPhase.LANDED)
        {
            // A leg touchdown is not accepted for this Starbase catch scenario. Keeping it
            // as a GAP makes an abort-to-legs visible instead of silently passing the wrong
            // terminal behavior.
            _log.WriteLine($"GAP normal Starbase reentry reached LANDED without tower catch " +
                $"alt={alt:F1} spd={surfVel.Magnitude:F1} catchArmed={vessel.IsAttemptingTowerCatch}");
            _log.Flush();
            Finish("ORBITAL_REENTRY_NO_CATCH");
            return;
        }

        if (!_orbitalReentryCaught && mission?.Phase == MissionPhase.CAUGHT)
        {
            QueueCapture("orbital_reentry_caught");
            _orbitalReentryCaught = true;
            _log.WriteLine($"CHECK orbital_reentry caught=True pins={vessel.LastCatchContact?.ContactCount ?? 0} " +
                $"relativeSpeed={(vessel.Velocity - vessel.CatchTargetVelocityWorld).Magnitude:F3} " +
                $"angularSpeed={vessel.AngularVelocity.Magnitude:F4} normalFlow=True demo=False");
            _log.Flush();
        }

        if (_orbitalReentryCaught && _pendingSlug == null)
        {
            Finish("ORBITAL_REENTRY_OK");
            return;
        }

        // Once the real deorbit burn has completed, use the same bounded coast policy as
        // the full-mission harness: high warp is safe above the atmosphere, then reduce it
        // before the entry interface so EDL still receives physical RK4 steps.
        if (!_orbitalReentryEntry && mission?.Phase == MissionPhase.COAST)
        {
            bridge.SetTimeScale(alt > 120_000.0 ? 200.0 : 5.0);
        }

        if (simElapsed > SimTimeoutSec)
        {
            _log.WriteLine($"GAP normal orbital reentry did not reach Starbase catch within " +
                $"simTimeout={SimTimeoutSec:F0}s phase={phase} alt={alt:F1} " +
                $"entry={_orbitalReentryEntry} peak={_orbitalReentryPeak} " +
                $"retro={_orbitalReentryRetro}");
            _log.Flush();
            Finish("ORBITAL_REENTRY_TIMEOUT");
        }
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
                or MissionPhase.LANDED or MissionPhase.CAUGHT)
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
            // A 1,200 km -> 60 km deorbit spends more propellant than a nominal
            // 12% landing reserve.  That value was valid for the direct 70 km EDL
            // demonstration, but it starved the real orbital-return vehicle before
            // the flip and made the acceptance run look like an attitude failure.
            // Keep enough for the deorbit burn plus three-engine flip/catch margin.
            // This is an explicit scenario seed, not a claim about the player's live tank
            // state: JumpToOrbit may be reached from a partially flown launch profile.
            const double orbitalReturnReserve = 0.45;
            SetPropellantReserve(vessel, orbitalReturnReserve);
            bridge.SetTimeScale(1.0);
            _deorbitStarted = true;
            _log.WriteLine($"ACTION deorbit: capped propellant at {orbitalReturnReserve:P0} " +
                "deorbit+landing reserve, starting retro burn");
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

        // Inspect the current stage, not a hard-coded ship part: before staging the active
        // cluster is Super Heavy (33 engines), while after staging it is the Ship (6).
        Part? engineCluster = vessel.Parts.ActiveEngines.FirstOrDefault();
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
        Part? engineCluster = vessel.Parts.ActiveEngines.FirstOrDefault();
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

    private void ApplyVisualCaptureConfiguration(
        SimulationBridge bridge, Vessel vessel, Universe universe, CelestialBody body)
    {
        if (_visualConfigurationApplied
            || _mode is "atmosphere" or "atmosphere_ground" or "atmosphere_low"
                or "atmosphere_bodies" or "spectral")
            return;

        bool hasCameraPreset = VisualCameraPreset.Length > 0;
        if (!HasVisualSunElevation && !hasCameraPreset)
        {
            _visualConfigurationApplied = true;
            return;
        }
        if ((HasVisualSunElevation && SunController.Instance == null)
            || (hasCameraPreset && CameraController.Instance == null))
            return;

        if (HasVisualSunElevation)
            SunController.Instance!.SetVisualSunElevationOverride(VisualSunElevationDeg);

        string phase = HasVisualSunElevation
            ? SunController.ClassifySolarPhase(VisualSunElevationDeg)
            : SunController.SolarPhase;
        _log.WriteLine(
            $"VISUAL_SUN override={HasVisualSunElevation} "
            + $"elevationDeg={(HasVisualSunElevation ? VisualSunElevationDeg.ToString("F2") : "physical")} "
            + $"phase={phase} physicalSunPositionUnchanged=True");

        if (hasCameraPreset)
        {
            if (!CameraController.Instance!.TryApplyVisualPreset(VisualCameraPreset))
            {
                _log.WriteLine($"FAIL visual_camera_preset_invalid preset={VisualCameraPreset}");
                _log.Flush();
                Finish("VISUAL_CAMERA_PRESET_INVALID");
                return;
            }

            var camera = CameraController.Instance;
            _log.WriteLine(
                $"VISUAL_CAMERA preset={camera.VisualPreset} "
                + $"yawDeg={camera.PresentationYawDegrees:F2} "
                + $"pitchDeg={camera.PresentationPitchDegrees:F2} "
                + $"distance={camera.PresentationDistance:F2} "
                + $"fov={camera.PresentationFov:F2} mode={camera.Mode}");
        }
        _log.Flush();
        _visualConfigurationApplied = true;
    }

    private void ProcessAtmosphereMatrix(double delta, SimulationBridge bridge,
        Vessel vessel, Universe universe, CelestialBody body)
    {
        if (_spectralOracle == null || _spectralBodyId != body.Id)
        {
            _spectralOracle = SpectralAtmosphereOracle.Build(
                body, maxOrder: SpectralAtmosphereOracle.ExperimentalOrder, sampleCount: 12);
            _spectralBodyId = body.Id;
            _log.WriteLine($"SPECTRAL_ORACLE body={body.Id} bands={SpectralAtmosphereOracle.BandCount} "
                + $"provenance={_spectralOracle.DataProvenance} "
                + $"maxOrder={_spectralOracle.MaxScatteringOrder}");
            _log.Flush();
        }
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

        var shot = CurrentAtmosphereCase();
        CaptureNow(shot.Slug);
        LogAtmosphereState(bridge, vessel, universe, body, shot);

        _atmosIndex++;
        if (IsSingleAtmosphereCase
            || (IsGroundAtmosphereCase && _atmosIndex >= 4))
        {
            Finish(IsSingleAtmosphereCase ? "ATMOSPHERE_LOW_OK" : "ATMOSPHERE_GROUND_OK");
            return;
        }
        if (_atmosIndex >= _atmosCases.Length)
        {
            Finish("ATMOSPHERE_OK");
            return;
        }
        ApplyAtmosphereCase(bridge, vessel, universe, body);
    }

    private void ProcessAtmosphereBodies(double delta, SimulationBridge bridge,
        Vessel vessel, Universe universe, CelestialBody body)
    {
        if (_atmosIndex < 0)
        {
            bridge.SetTimeScale(0.0);
            _atmosIndex = 0;
            _atmosBodyCaseApplied = false;
            _atmosBodyTransitionRequested = null;
        }

        var shot = _atmosBodyCases[_atmosIndex];
        var targetBody = universe.GetBody(shot.BodyId);
        if (targetBody?.Atmosphere == null)
        {
            _log.WriteLine($"FAIL atmosphere_body_missing body={shot.BodyId} slug={shot.Slug}");
            _log.Flush();
            Finish("ATMOSPHERE_BODIES_INVALID");
            return;
        }

        // Do not label an Earth frame as Mars/Venus.  JumpToBody performs the same public
        // guidance cancellation and rigid-body reset used by the game; the next frame must
        // prove that the dominant body actually changed before the case is applied.
        if (!string.Equals(body.Id, shot.BodyId, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(_atmosBodyTransitionRequested, shot.BodyId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _log.WriteLine($"ATMOS_BODY_SWITCH from={body.Id} target={shot.BodyId} slug={shot.Slug}");
                bridge.JumpToBody(shot.BodyId, shot.AltitudeM);
                bridge.SetTimeScale(0.0);
                _atmosBodyTransitionRequested = shot.BodyId;
                _log.Flush();
            }
            return;
        }

        _atmosBodyTransitionRequested = null;
        if (_spectralOracle == null || _spectralBodyId != body.Id)
        {
            _spectralOracle = SpectralAtmosphereOracle.Build(
                body, maxOrder: SpectralAtmosphereOracle.ExperimentalOrder, sampleCount: 12);
            _spectralBodyId = body.Id;
            _log.WriteLine($"SPECTRAL_ORACLE body={body.Id} bands={SpectralAtmosphereOracle.BandCount} "
                + $"provenance={_spectralOracle.DataProvenance} "
                + $"maxOrder={_spectralOracle.MaxScatteringOrder}");
            _log.Flush();
        }

        if (!_atmosBodyCaseApplied)
        {
            ApplyAtmosphereBodyCase(bridge, vessel, universe, body, shot);
            _atmosBodyCaseApplied = true;
            return;
        }

        _atmosFrameSeconds += delta;
        _atmosMaxFrameSeconds = System.Math.Max(_atmosMaxFrameSeconds, delta);
        _atmosPerfFrames++;
        if (delta > 1.0 / 30.0) _atmosSlowFrames++;

        ApplyAtmosphereBodyCamera(shot.Cockpit);
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

        CaptureNow(shot.Slug);
        LogAtmosphereBodyState(vessel, universe, body, shot);
        _atmosIndex++;
        _atmosBodyCaseApplied = false;
        if (_atmosIndex >= _atmosBodyCases.Length)
        {
            Finish("ATMOSPHERE_BODIES_OK");
            return;
        }
    }

    private void ApplyAtmosphereCase(SimulationBridge bridge, Vessel vessel,
        Universe universe, CelestialBody earth)
    {
        var shot = CurrentAtmosphereCase();
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
        ConfigureEclipseCase(universe, vessel, earth, sun, shot.Eclipse);

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
            $"targetSunElevation={shot.SunElevationDeg:F1} cockpit={shot.Cockpit} " +
            $"eclipse={shot.Eclipse}");
        _log.Flush();
    }

    private void ApplyAtmosphereBodyCase(SimulationBridge bridge, Vessel vessel,
        Universe universe, CelestialBody body,
        (string BodyId, string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit) shot)
    {
        var sun = universe.GetBody("sun");
        Vector3d sunDir = sun == null
            ? new Vector3d(0.4, 0.5, 0.8).Normalized
            : (sun.Position - body.Position).Normalized;
        Vector3d seed = System.Math.Abs(sunDir.Dot(Vector3d.Up)) < 0.92
            ? Vector3d.Up : Vector3d.Right;
        Vector3d terminatorUp = (seed - sunDir * seed.Dot(sunDir)).Normalized;
        double elev = shot.SunElevationDeg * System.Math.PI / 180.0;
        _atmosUp = (terminatorUp * System.Math.Cos(elev)
            + sunDir * System.Math.Sin(elev)).Normalized;

        Vector3d projectedSun = sunDir - _atmosUp * sunDir.Dot(_atmosUp);
        if (projectedSun.MagnitudeSquared < 1e-10)
            projectedSun = body.GetEastDirection(body.Position + _atmosUp * body.Radius);
        _atmosLook = projectedSun.Normalized;

        vessel.IsGroundHeld = false;
        vessel.Position = body.Position + _atmosUp * (body.Radius + shot.AltitudeM);
        vessel.Velocity = body.Velocity + body.GetSurfaceVelocity(vessel.Position);
        vessel.Throttle = 0.0;

        Vector3d localXWorld = _atmosLook.Cross(-_atmosUp).Normalized;
        var basis = new Basis(ToGodot(localXWorld), ToGodot(_atmosLook), ToGodot(-_atmosUp));
        var q = basis.GetRotationQuaternion();
        vessel.Orientation = new Quaterniond(q.W, q.X, q.Y, q.Z);

        bridge.SetTimeScale(0.0);
        if (GetTree().Root.FindChild("HUDController", true, false) is CanvasItem hud)
            hud.Visible = false;
        if (CameraController.Instance != null)
            CameraController.Instance.ProcessMode = ProcessModeEnum.Disabled;

        _atmosFrameSeconds = 0.0;
        _atmosMaxFrameSeconds = 0.0;
        _atmosPerfFrames = 0;
        _atmosSlowFrames = 0;
        _atmosPreviousExposure = -1.0;
        _atmosExposureStableFrames = 0;
        _log.WriteLine($"ATMOS_APPLY body={body.Id} slug={shot.Slug} targetAlt={shot.AltitudeM:F1} "
            + $"targetSunElevation={shot.SunElevationDeg:F1} cockpit={shot.Cockpit} eclipse=none");
        _log.Flush();
    }

    private void ConfigureEclipseCase(
        Universe universe, Vessel vessel, CelestialBody earth, CelestialBody? sun,
        string eclipse)
    {
        var moon = universe.GetBody("moon");
        if (moon == null || sun == null) return;
        if (!_moonSnapshot)
        {
            _originalMoonPosition = moon.Position;
            _originalMoonVelocity = moon.Velocity;
            _moonSnapshot = true;
        }

        Vector3d observer = vessel.Position;
        Vector3d sunAxis = (sun.Position - observer).Normalized;
        Vector3d sideSeed = System.Math.Abs(sunAxis.Dot(Vector3d.Up)) < 0.92
            ? Vector3d.Up : Vector3d.Right;
        Vector3d side = (sideSeed - sunAxis * sideSeed.Dot(sunAxis)).Normalized;
        // A realistic Earth-Moon distance makes the lunar and solar discs almost equal.
        // The totality fixture moves the Moon closer so the umbral case is deterministic.
        double moonDistance = eclipse == "total" ? 300_000_000.0 : 384_400_000.0;
        double sunRadius = MissionGeometry.ApparentAngularRadius(sun.Radius,
            (sun.Position - observer).Magnitude);
        double targetSeparation = eclipse switch
        {
            "partial_central" => sunRadius * 0.55,
            "partial_limb" => sunRadius * 1.10,
            _ => 0.0,
        };

        if (eclipse == "none" || eclipse == "clear")
        {
            // Put the fixture at quadrature so the real Moon cannot accidentally occlude
            // the Sun while the altitude/day-night matrix is being captured.
            moon.Position = observer + side * moonDistance;
        }
        else
        {
            moon.Position = observer + sunAxis * moonDistance
                + side * (moonDistance * System.Math.Tan(targetSeparation));
        }
        moon.Velocity = Vector3d.Zero;
    }

    private void ApplyAtmosphereCamera()
    {
        var shot = CurrentAtmosphereCase();
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

    private void ApplyAtmosphereBodyCamera(bool cockpit)
    {
        if (cockpit) return;
        if (CameraController.Instance != null)
            CameraController.Instance.ProcessMode = ProcessModeEnum.Disabled;
        if (GetTree().Root.FindChild("StarshipRenderer", true, false) is Node3D renderer)
            renderer.Visible = false;
        if (GetTree().Root.FindChild("ActiveVesselRenderer", true, false) is Node3D activeRenderer)
            activeRenderer.Visible = false;
        if (GetTree().Root.FindChild("CockpitRenderer", true, false) is Node3D cockpitRenderer)
            cockpitRenderer.Visible = false;

        if (GetTree().Root.FindChild("Camera3D", true, false) is Camera3D camera)
        {
            camera.Position = Vector3.Zero;
            camera.Near = 0.1f;
            camera.Fov = 60.0f;
            // A level horizon leaves Mars/Venus as a thin lower strip and makes the
            // orbital cases look empty. Tilt the body dossier 28° toward the surface;
            // this keeps the limb and terminator in frame while exposing texture scale
            // and the low-altitude terrain patch.
            double bodyViewAngle = 28.0 * System.Math.PI / 180.0;
            Vector3d bodyLook = (_atmosLook * System.Math.Cos(bodyViewAngle)
                - _atmosUp * System.Math.Sin(bodyViewAngle)).Normalized;
            camera.LookAt(ToGodot(bodyLook) * 100.0f, ToGodot(_atmosUp));
        }
    }

    private void LogAtmosphereState(SimulationBridge bridge, Vessel vessel,
        Universe universe, CelestialBody earth,
        (string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit, string Eclipse) shot)
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
        var moon = universe.GetBody("moon");
        double eclipseVisibility = 1.0;
        double separation = 0.0;
        double sunRadius = 0.0;
        double occluderRadius = 0.0;
        if (sun != null && moon != null)
        {
            Vector3d toSun = sun.Position - vessel.Position;
            Vector3d toMoon = moon.Position - vessel.Position;
            sunRadius = MissionGeometry.ApparentAngularRadius(sun.Radius, toSun.Magnitude);
            occluderRadius = MissionGeometry.ApparentAngularRadius(moon.Radius, toMoon.Magnitude);
            separation = System.Math.Atan2(toSun.Normalized.Cross(toMoon.Normalized).Magnitude,
                System.Math.Clamp(toSun.Normalized.Dot(toMoon.Normalized), -1.0, 1.0));
            eclipseVisibility = MissionGeometry.LimbDarkenedSolarDiscVisibility(
                vessel.Position, moon.Position, moon.Radius, sun.Position, sun.Radius);
        }

        double solarElevationRadians = solarElevation * System.Math.PI / 180.0;
        double viewSunCosine = _atmosLook.Dot(sunDir);
        var spectral = _spectralOracle?.Evaluate(
            vessel.GetAltitude(earth), solarElevationRadians, 0.5, viewSunCosine,
            eclipseVisibility);
        Vector3d spectralRgb = spectral?.ToLinearRgb() ?? Vector3d.Zero;

        _log.WriteLine($"ATMOS_STATE slug={shot.Slug} actualAlt={vessel.GetAltitude(earth):F1} " +
            $"sunElevation={solarElevation:F2} solarVisibility={SunController.SolarVisibility:F3} " +
            $"eclipse={shot.Eclipse} eclipseVisibility={eclipseVisibility:F6} " +
            $"lutVersion={SkyController.MultipleScatteringLutVersion} " +
            $"lutOrder={SkyController.RuntimeMultipleScatteringOrder} " +
            $"spectralOrder={_spectralOracle?.MaxScatteringOrder ?? 0} " +
            $"spectralEnergy={spectral?.Energy ?? 0.0:E4} " +
            $"spectralRgb={spectralRgb.X:E4},{spectralRgb.Y:E4},{spectralRgb.Z:E4} " +
            $"separationRad={separation:E4} sunRadiusRad={sunRadius:E4} " +
            $"occluderRadiusRad={occluderRadius:E4} cockpit={shot.Cockpit} " +
            $"exposure={exposure:F3} fov={camera?.Fov ?? -1:F2} " +
            $"near={camera?.Near ?? -1:F3} " +
            $"exposureSettled={_atmosExposureStableFrames >= AtmosphereExposureStableFrames}");
        _log.WriteLine($"PERF slug={shot.Slug} meanFrameMs={meanMs:F2} " +
            $"maxFrameMs={_atmosMaxFrameSeconds * 1000.0:F2} slowFrames={_atmosSlowFrames} " +
            $"sampleFrames={_atmosPerfFrames} exposureStableFrames={_atmosExposureStableFrames} " +
            $"reportedFps={Engine.GetFramesPerSecond()}");
        _log.Flush();
    }

    private void LogAtmosphereBodyState(Vessel vessel, Universe universe, CelestialBody body,
        (string BodyId, string Slug, double AltitudeM, double SunElevationDeg, bool Cockpit) shot)
    {
        var sun = universe.GetBody("sun");
        Vector3d up = (vessel.Position - body.Position).Normalized;
        Vector3d sunDir = sun == null ? Vector3d.Up : (sun.Position - vessel.Position).Normalized;
        double solarElevation = System.Math.Asin(System.Math.Clamp(up.Dot(sunDir), -1.0, 1.0))
            * 180.0 / System.Math.PI;
        var camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
        var world = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        float exposure = world?.Environment?.TonemapExposure ?? -1.0f;
        double meanMs = _atmosPerfFrames > 0
            ? _atmosFrameSeconds * 1000.0 / _atmosPerfFrames : 0.0;
        double solarElevationRadians = solarElevation * System.Math.PI / 180.0;
        double viewSunCosine = _atmosLook.Dot(sunDir);
        var spectral = _spectralOracle?.Evaluate(
            vessel.GetAltitude(body), solarElevationRadians, 0.5, viewSunCosine, 1.0);
        Vector3d spectralRgb = spectral?.ToLinearRgb() ?? Vector3d.Zero;

        _log.WriteLine($"ATMOS_STATE body={body.Id} slug={shot.Slug} actualAlt={vessel.GetAltitude(body):F1} "
            + $"sunElevation={solarElevation:F2} solarVisibility={SunController.SolarVisibility:F3} "
            + $"eclipse=none eclipseVisibility=1.000000 "
            + $"lutVersion={SkyController.MultipleScatteringLutVersion} "
            + $"lutOrder={SkyController.RuntimeMultipleScatteringOrder} "
            + $"spectralOrder={_spectralOracle?.MaxScatteringOrder ?? 0} "
            + $"spectralEnergy={spectral?.Energy ?? 0.0:E4} "
            + $"spectralRgb={spectralRgb.X:E4},{spectralRgb.Y:E4},{spectralRgb.Z:E4} "
            + $"separationRad=0.0000E+00 sunRadiusRad=0.0000E+00 "
            + $"occluderRadiusRad=0.0000E+00 cockpit={shot.Cockpit} exposure={exposure:F3} "
            + $"fov={camera?.Fov ?? -1:F2} near={camera?.Near ?? -1:F3} "
            + $"exposureSettled={_atmosExposureStableFrames >= AtmosphereExposureStableFrames}");
        _log.WriteLine($"PERF body={body.Id} slug={shot.Slug} meanFrameMs={meanMs:F2} "
            + $"maxFrameMs={_atmosMaxFrameSeconds * 1000.0:F2} slowFrames={_atmosSlowFrames} "
            + $"sampleFrames={_atmosPerfFrames} exposureStableFrames={_atmosExposureStableFrames} "
            + $"reportedFps={Engine.GetFramesPerSecond()}");
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
        LogHotStageVisualTelemetry(slug);
        LogReentryVisualTelemetry(slug);
        LogLaunchComplexVisualTelemetry(slug);
        LogEngineVisualTelemetry(slug);
        // Headless runs are telemetry-only diagnostics: the dummy renderer has no
        // framebuffer texture, but scene framing/planet placement telemetry still
        // remains valid. Keep that evidence identical across real and dummy paths so
        // a failed PNG gate cannot be misread as an out-of-frame planet.
        if (DisplayServer.GetName() == "headless")
        {
            LogTelemetry(slug, $"headless://{slug}");
            _log.WriteLine($"CAPTURE {slug} headless=True");
            _log.Flush();
            return;
        }
        var img = GetTree().Root.GetViewport().GetTexture().GetImage();
        string path = Path.Combine(_outDir, $"exo_play_{slug}.png");
        img.SavePng(path);
        LogTelemetry(slug, path);
        LogImageMetrics(slug, img);
        GD.Print($"[Playtest] captured {slug} -> {path}");
    }

    private void LogHotStageVisualTelemetry(string slug)
    {
        if (slug is not ("hotstage" or "hotstage_separation")) return;

        var effect = GetTree().Root.FindChild(
            "HotStageFlashController", true, false) as HotStageFlashController;
        var plume = effect?.GetNodeOrNull<Node3D>("HotStagePlume");
        var renderer = GetTree().Root.FindChild(
            "ActiveVesselRenderer", true, false) as Node3D;
        bool overlap = SimulationBridge.Instance?.ActiveVessel?.IsHotStageOverlapping == true;
        _log.WriteLine(
            $"VISUAL_HOTSTAGE slug={slug} visible={effect?.Visible ?? false} " +
            $"frameSynced={effect?.IsVesselFrameSynchronized ?? false} " +
            $"overlap={overlap} " +
            $"interfaceY={HotStageFlashController.HotStageInterfaceRenderY:F2} " +
            $"plumeLocalY={(plume != null ? plume.Position.Y : float.NaN):F2} " +
            $"rendererY={(renderer != null ? renderer.Position.Y : float.NaN):F2} " +
            $"rootY={(effect != null ? effect.Position.Y : float.NaN):F2}");
        _log.Flush();
    }

    private void LogReentryVisualTelemetry(string slug)
    {
        if (!slug.Contains("entry", StringComparison.Ordinal)
            && slug is not ("peak_heating" or "retro_burn" or "flip_complete")) return;

        var plasma = GetTree().Root.FindChild(
            "ReentryPlasma", true, false) as ReentryPlasmaController;
        string phase = MissionManager.Instance?.Phase.ToString() ?? "UNKNOWN";
        _log.WriteLine(
            $"VISUAL_REENTRY slug={slug} coreVisible={plasma?.CoreEffectsVisible ?? false} " +
            $"phase={phase} " +
            $"flux={plasma?.LastHeatFluxWm2 ?? double.NaN:E3} " +
            $"fluxIntensity={plasma?.LastFluxIntensity01 ?? float.NaN:F3} " +
            $"visualFluxInput={plasma?.LastVisualFluxInput01 ?? float.NaN:F3} " +
            $"visualIntensity={plasma?.LastVisualIntensity01 ?? float.NaN:F3} " +
            $"shockHeat={plasma?.LastShockHeatLevel ?? float.NaN:F3}");
        _log.Flush();
    }

    private void LogLaunchComplexVisualTelemetry(string slug)
    {
        if (slug is not ("pad" or "liftoff")) return;

        var pad = GetTree().Root.FindChild(
            "LaunchPadController", true, false) as LaunchPadController;
        if (pad == null)
        {
            _log.WriteLine($"VISUAL_LAUNCH slug={slug} present=False");
            _log.Flush();
            return;
        }

        int delugeOutlets = 0;
        int tankBodies = 0;
        int chopsticks = 0;
        foreach (Node child in pad.GetChildren())
        {
            string childName = child.Name.ToString();
            if (childName.StartsWith("DelugeOutlet", StringComparison.Ordinal))
                delugeOutlets++;
            if (childName.EndsWith("Body", StringComparison.Ordinal)
                && childName.StartsWith("Tank", StringComparison.Ordinal))
                tankBodies++;
            if (childName == "ChopstickL" || childName == "ChopstickR")
                chopsticks++;
        }

        _log.WriteLine($"VISUAL_LAUNCH slug={slug} present=True "
            + $"visible={pad.Visible} children={pad.GetChildCount()} "
            + $"nightFloodlights={pad.NightFloodlightCount} "
            + $"floodlightsActive={pad.NightFloodlightsActive} "
            + $"delugeOutlets={delugeOutlets} tankBodies={tankBodies} "
            + $"chopsticks={chopsticks}");
        _log.Flush();
    }

    private void LogEngineVisualTelemetry(string slug)
    {
        if (slug is not ("pad" or "liftoff")) return;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;
        var body = universe.GetDominantBody(vessel.Position);
        if (body == null) return;

        var rows = new List<EngineReadout>(39);
        vessel.FillEngineReadouts(body, rows, out var summary);
        int delivered = EngineHudPresentation.CountDelivered(rows);
        int failed = EngineHudPresentation.CountFailures(rows);
        int starting = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (EngineHudPresentation.Classify(rows[i]) == EngineHudIndicatorState.Starting)
                starting++;
        }

        _log.WriteLine($"VISUAL_ENGINES slug={slug} commandThrottle={vessel.Throttle:F3} "
            + $"nominal={summary.NominalEngineCount} rows={summary.ReadoutEngineCount} "
            + $"delivered={delivered} starting={starting} failed={failed}");
        _log.Flush();
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

        var floating = GetTree().Root.FindChild("FloatingOrigin", true, false)
            as FloatingOrigin;
        if (floating != null
            && string.Equals(floating.LastPresentationBodyId, body.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            _log.WriteLine($"VISUAL_PLANET body={body.Id} slug={slug} " +
                $"visible={floating.LastPresentationVisible} " +
                $"cameraDistanceM={floating.LastPresentationCameraDistance:F1} " +
                $"angularDiameterDeg={floating.LastPresentationAngularDiameterDeg:F4} " +
                $"cameraForwardCos={floating.LastPresentationForwardCosine:F5} " +
                $"direction={floating.LastPresentationDirection.X:F4}," +
                $"{floating.LastPresentationDirection.Y:F4}," +
                $"{floating.LastPresentationDirection.Z:F4} " +
                $"backdrop={floating.LastPresentationBackdropPosition.X:F1}," +
                $"{floating.LastPresentationBackdropPosition.Y:F1}," +
                $"{floating.LastPresentationBackdropPosition.Z:F1}");
        }

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
            $"hotStageOverlap={vessel.IsHotStageOverlapping} " +
            $"landingParts={landingParts} approachSpeed={_lastApproachSpeed:F2} " +
            $"contacts={contacts} maxStroke={maxStroke:F3} peakLegLoad={peakLegLoad:F0} " +
            $"settled={vessel.IsSurfaceSettled}");
        _log.Flush();
    }

    private static void SetPropellantReserve(Vessel vessel, double reserveFrac)
    {
        foreach (var p in vessel.Parts.Parts)
        {
            double cap = p.Definition.FuelCapacityLF + p.Definition.FuelCapacityOx;
            if (cap <= 0.0) continue;
            double target = cap * reserveFrac;
            double fuelFraction = cap > 1e-9
                ? p.Definition.FuelCapacityLF / cap
                : 0.45;
            p.LiquidFuel = target * fuelFraction;
            p.Oxidizer = target * (1.0 - fuelFraction);
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
  elif [[ "$MODE" == "orbit" ]]; then
    if [[ ! -f "$OUT_DIR/exo_play_orbit_direct.png" ]]; then
      echo "ERROR: missing direct orbital visual milestone PNG" >&2
      return 1
    fi
    if ! grep -q 'SUMMARY reason=ORBIT_DIRECT_OK' "$LOG"; then
      echo "ERROR: direct orbital visual capture did not finish cleanly" >&2
      return 1
    fi
    if ! grep -Eq 'VISUAL_PLANET body=earth slug=orbit_direct visible=True .*angularDiameterDeg=1[1-9][0-9]\.[0-9]+ .*cameraForwardCos=0\.[5-9][0-9]*' "$LOG"; then
      echo "ERROR: direct orbital capture did not prove Earth is visible and framed" >&2
      return 1
    fi
    if ! awk '
      /^IMAGE slug=orbit_direct / {
        for (i = 1; i <= NF; i++) {
          if ($i ~ /^mean=/) { split($i, p, "="); mean = p[2] + 0 }
          if ($i ~ /^darkFrac=/) { split($i, p, "="); dark = p[2] + 0 }
          if ($i ~ /^surfaceWhiteClipFrac=/) { split($i, p, "="); white = p[2] + 0 }
        }
        found = 1
      }
      END { exit !(found && mean > 0.005 && dark < 0.90 && white < 0.20) }
    ' "$LOG"; then
      echo "ERROR: direct orbital Earth image is empty or broadly clipped" >&2
      return 1
    fi
  elif [[ "$MODE" == "edl" ]]; then
    local required=(entry retro_burn flip_complete)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing required EDL milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
    done
    if grep -q 'SUMMARY reason=CAUGHT' "$LOG"; then
      if [[ ! -f "$OUT_DIR/exo_play_caught.png" ]]; then
        echo "ERROR: verified tower catch has no caught milestone PNG" >&2
        return 1
      fi
      if ! grep -Eq 'CHECK tower_catch caught=True pins=[2-9][0-9]* relativeSpeed=[0-9.]+ angularSpeed=[0-9.]+' "$LOG"; then
        echo "ERROR: tower catch summary lacks two settled pin contacts" >&2
        return 1
      fi
    elif grep -q 'SUMMARY reason=LANDED' "$LOG"; then
      if [[ ! -f "$OUT_DIR/exo_play_touchdown.png" ]]; then
        echo "ERROR: verified leg landing has no touchdown milestone PNG" >&2
        return 1
      fi
      if ! grep -Eq 'CAPTURE touchdown .*contacts=[3-9][0-9]* .*settled=True' "$LOG"; then
        echo "ERROR: touchdown was not supported by at least three settled physical contacts" >&2
        return 1
      fi
    else
      echo "ERROR: deterministic EDL did not end in a verified catch or landing" >&2
      return 1
    fi
  elif [[ "$MODE" == "orbital_reentry" ]]; then
    local required=(orbital_reentry_orbit orbital_reentry_entry
      orbital_reentry_peak_heating orbital_reentry_retro_burn orbital_reentry_caught)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing normal orbital reentry milestone PNG: exo_play_${slug}.png" >&2
        return 1
      fi
    done
    if ! grep -q 'SUMMARY reason=ORBITAL_REENTRY_OK' "$LOG"; then
      echo "ERROR: normal orbital reentry did not finish with ORBITAL_REENTRY_OK" >&2
      return 1
    fi
    if ! grep -q 'NORMAL_REENTRY_SETUP .*source=JumpToOrbit .*demo=False .*flownAscent=False' "$LOG"; then
      echo "ERROR: missing explicit non-demo orbital setup evidence" >&2
      return 1
    fi
    if ! grep -q 'NORMAL_REENTRY_ARMED .*source=map_deorbit_autopilot .*demo=False' "$LOG"; then
      echo "ERROR: missing normal map deorbit/autopilot evidence" >&2
      return 1
    fi
    if ! grep -Eq 'CHECK orbital_reentry caught=True pins=[2-9][0-9]* relativeSpeed=[0-9.]+ angularSpeed=[0-9.]+ normalFlow=True demo=False' "$LOG"; then
      echo "ERROR: normal orbital reentry lacks a settled physical tower catch" >&2
      return 1
    fi
    if grep -Eq '^(FAIL|GAP) ' "$LOG"; then
      echo "ERROR: normal orbital reentry log contains FAIL/GAP evidence" >&2
      grep -E '^(FAIL|GAP) ' "$LOG" >&2
      return 1
    fi
    if ! grep -q 'TRACE_ORBITAL_REENTRY .*normalFlow=True demo=False' "$LOG"; then
      echo "ERROR: missing normal-flow telemetry (demo must not satisfy this gate)" >&2
      return 1
    fi
  elif [[ "$MODE" == "hotstage" ]]; then
    if [[ ! -f "$OUT_DIR/exo_play_hotstage.png" ]]; then
      echo "ERROR: missing required hot-stage milestone PNG: exo_play_hotstage.png" >&2
      return 1
    fi
    if [[ ! -f "$OUT_DIR/exo_play_hotstage_separation.png" ]]; then
      echo "ERROR: missing required post-separation hot-stage milestone PNG: exo_play_hotstage_separation.png" >&2
      return 1
    fi
    if ! grep -q 'SUMMARY reason=HOTSTAGE_OK' "$LOG"; then
      echo "ERROR: hot-stage capture did not confirm the dual-thrust overlap state" >&2
      return 1
    fi
    if ! grep -Eq 'VISUAL_HOTSTAGE slug=hotstage .*frameSynced=True .*interfaceY=25\.36' "$LOG"; then
      echo "ERROR: hot-stage overlap capture lacks synchronized interstage anchor telemetry" >&2
      return 1
    fi
    if ! grep -Eq 'VISUAL_HOTSTAGE slug=hotstage .*frameSynced=True .*overlap=True .*interfaceY=25\.36' "$LOG"; then
      echo "ERROR: hot-stage overlap capture did not prove live overlap or synchronized interstage anchor" >&2
      return 1
    fi
    if ! grep -Eq 'VISUAL_HOTSTAGE slug=hotstage_separation .*frameSynced=True .*overlap=False .*interfaceY=25\.36' "$LOG"; then
      echo "ERROR: hot-stage separation capture lacks synchronized interstage anchor telemetry" >&2
      return 1
    fi
  elif [[ "$MODE" == "saturn" ]]; then
    if [[ ! -f "$OUT_DIR/exo_play_saturn_ring.png" ]]; then
      echo "ERROR: missing required Saturn ring milestone PNG" >&2
      return 1
    fi
    if ! grep -q 'SUMMARY reason=SATURN_OK' "$LOG"; then
      echo "ERROR: Saturn capture did not finish its physical body-transition scenario" >&2
      return 1
    fi
    # A valid file alone can hide an out-of-frame body (the first version of this mode
    # captured only the starfield). Require measurable ring/body signal in the image
    # metrics so this remains a real visual acceptance gate.
    if ! awk '
      /^IMAGE slug=saturn_ring / {
        for (i = 1; i <= NF; i++) {
          if ($i ~ /^mean=/) { split($i, p, "="); mean = p[2] + 0 }
          if ($i ~ /^p95=/)  { split($i, p, "="); p95  = p[2] + 0 }
        }
        found = 1
      }
      END { exit !(found && mean > 0.02 && p95 > 0.20) }
    ' "$LOG"; then
      echo "ERROR: Saturn image metrics do not prove visible ring/body signal" >&2
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
  elif [[ "$MODE" == "atmosphere_bodies" ]]; then
    local required=(
      mars_10km_day mars_400km_day mars_10km_night
      venus_10km_day venus_400km_day venus_10km_night
    )
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing Mars/Venus atmosphere PNG: exo_play_${slug}.png" >&2
        return 1
      fi
      if ! grep -q "^IMAGE slug=${slug} " "$LOG"; then
        echo "ERROR: missing image metrics for Mars/Venus case: ${slug}" >&2
        return 1
      fi
      if ! grep -q "^ATMOS_STATE .*slug=${slug} " "$LOG"; then
        echo "ERROR: missing physical state evidence for Mars/Venus case: ${slug}" >&2
        return 1
      fi
    done
    if ! grep -q 'SUMMARY reason=ATMOSPHERE_BODIES_OK' "$LOG"; then
      echo "ERROR: Mars/Venus atmosphere matrix did not finish cleanly" >&2
      return 1
    fi
    if grep -Eq '^(FAIL|GAP) ' "$LOG"; then
      echo "ERROR: Mars/Venus atmosphere log contains FAIL/GAP evidence" >&2
      grep -E '^(FAIL|GAP) ' "$LOG" >&2
      return 1
    fi
    if grep '^ATMOS_STATE ' "$LOG" | grep -qv ' exposureSettled=True'; then
      echo "ERROR: Mars/Venus atmosphere capture reached its safety limit before exposure settled" >&2
      grep '^ATMOS_STATE ' "$LOG" | grep -v ' exposureSettled=True' >&2
      return 1
    fi

    # Fail closed on body identity, physical target matching, and finite optical telemetry.
    # This prevents a stale Earth frame or an out-of-range body jump from being accepted as
    # a valid Mars/Venus screenshot.  The orbital cases intentionally allow altitude above
    # the atmosphere top; they still require the correct body and finite state.
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
      function finite(value) { return value != "" && value == value && value !~ /^(nan|NaN|inf|Inf|-inf|-Inf)$/ }
      function reject(message) {
        print "ERROR: Mars/Venus atmosphere gate: " message > "/dev/stderr"
        bad = 1
      }
      BEGIN {
        expected["mars_10km_day"] = "mars"
        expected["mars_400km_day"] = "mars"
        expected["mars_10km_night"] = "mars"
        expected["venus_10km_day"] = "venus"
        expected["venus_400km_day"] = "venus"
        expected["venus_10km_night"] = "venus"
      }
      /^ATMOS_APPLY / {
        slug = value("slug")
        body = value("body")
        if (!(slug in expected) || body != expected[slug])
          reject("unexpected apply identity slug=" slug " body=" body)
        targetAlt[slug] = value("targetAlt") + 0
        targetSun[slug] = value("targetSunElevation") + 0
        requested[slug]++
      }
      /^ATMOS_STATE / {
        slug = value("slug")
        body = value("body")
        if (!(slug in expected) || body != expected[slug])
          reject("unexpected state identity slug=" slug " body=" body)
        if (!(slug in targetAlt))
          reject("state without preceding apply for " slug)
        actualAlt = value("actualAlt")
        actualSun = value("sunElevation")
        solarRaw = value("solarVisibility")
        energyRaw = value("spectralEnergy")
        solar = solarRaw + 0
        energy = energyRaw + 0
        if (!finite(actualAlt) || !finite(actualSun) || !finite(solarRaw) || !finite(energyRaw))
          reject("non-finite state for " slug)
        if (abs(actualAlt + 0 - targetAlt[slug]) > 2.0)
          reject(slug " altitude mismatch actual=" actualAlt " target=" targetAlt[slug])
        if (abs(actualSun + 0 - targetSun[slug]) > 0.25)
          reject(slug " solar-elevation mismatch actual=" actualSun " target=" targetSun[slug])
        if (solar < -1e-6 || solar > 1.000001)
          reject(slug " solarVisibility outside [0,1]: " solar)
        if (value("eclipse") != "none" || (value("eclipseVisibility") + 0) < 0.999)
          reject(slug " has unexpected eclipse state")
        if (value("exposureSettled") != "True")
          reject(slug " exposure did not settle")
        seen[slug]++
      }
      /^IMAGE / {
        slug = value("slug")
        meanRaw = value("mean")
        clippedRaw = value("clippedFrac")
        if (!(slug in expected) || !finite(meanRaw) || !finite(clippedRaw))
          reject("invalid image metrics for " slug)
        mean = meanRaw + 0
        clipped = clippedRaw + 0
        if (mean <= 0.00002 || mean >= 0.9995 || clipped < 0.0 || clipped > 0.95)
          reject("degenerate image metrics for " slug)
        image[slug]++
      }
      # Finish() appends run metrics (for example, "frames=709") after the
      # terminal reason. Accept those fields while still requiring exactly one
      # terminal summary record.
      /^SUMMARY reason=ATMOSPHERE_BODIES_OK([[:space:]]|$)/ { summary++ }
      END {
        for (slug in expected) {
          if (requested[slug] != 1) reject("expected exactly one apply for " slug)
          if (seen[slug] != 1) reject("expected exactly one state for " slug)
          if (image[slug] != 1) reject("expected exactly one image for " slug)
        }
        if (summary != 1) reject("missing or duplicated ATMOSPHERE_BODIES_OK")
        exit bad
      }
    ' "$LOG"; then
      return 1
    fi
  elif [[ "$MODE" == "atmosphere_low" ]]; then
    if [[ ! -f "$OUT_DIR/exo_play_10km_day.png" ]]; then
      echo "ERROR: missing low-atmosphere diagnostic PNG" >&2
      return 1
    fi
    if ! awk '
      function value(prefix,    i, pair) {
        for (i = 1; i <= NF; i++)
          if ($i ~ ("^" prefix "=")) { split($i, pair, "="); return pair[2] }
        return ""
      }
      function finite(value) { return value != "" && value == value && value !~ /^(nan|NaN|inf|Inf|-inf|-Inf)$/ }
      function reject(message) { print "ERROR: low-atmosphere gate: " message > "/dev/stderr"; bad = 1 }
      /^ATMOS_APPLY / {
        if (value("slug") != "10km_day") reject("unexpected apply slug=" value("slug"))
        targetAlt = value("targetAlt") + 0; targetSun = value("targetSunElevation") + 0; requested++
      }
      /^ATMOS_STATE / {
        if (value("slug") != "10km_day") reject("unexpected state slug=" value("slug"))
        if (!finite(value("actualAlt")) || !finite(value("sunElevation"))) reject("non-finite physical state")
        if (value("actualAlt") + 0 < targetAlt - 2.0 || value("actualAlt") + 0 > targetAlt + 2.0) reject("altitude mismatch")
        if (value("sunElevation") + 0 < targetSun - 0.25 || value("sunElevation") + 0 > targetSun + 0.25) reject("solar-elevation mismatch")
        if (value("exposureSettled") != "True") reject("exposure did not settle")
        state++
      }
      /^IMAGE / {
        if (value("slug") != "10km_day" || !finite(value("mean")) || !finite(value("clippedFrac"))) reject("invalid image metrics")
        image++
      }
      /^SUMMARY reason=ATMOSPHERE_LOW_OK([[:space:]]|$)/ { summary++ }
      END {
        if (requested != 1) reject("expected one apply, got " requested)
        if (state != 1) reject("expected one physical state, got " state)
        if (image != 1) reject("expected one image, got " image)
        if (summary != 1) reject("missing low-atmosphere summary")
        exit bad
      }
    ' "$LOG"; then
      return 1
    fi
  elif [[ "$MODE" == "atmosphere_ground" ]]; then
    local required=(ground_day ground_sunrise ground_sunset ground_night)
    for slug in "${required[@]}"; do
      if [[ ! -f "$OUT_DIR/exo_play_${slug}.png" ]]; then
        echo "ERROR: missing Earth-ground matrix PNG: exo_play_${slug}.png" >&2
        return 1
      fi
      if ! grep -q "^IMAGE slug=${slug} " "$LOG"; then
        echo "ERROR: missing image metrics for Earth-ground case: ${slug}" >&2
        return 1
      fi
      if ! grep -q "^ATMOS_STATE slug=${slug} " "$LOG"; then
        echo "ERROR: missing physical state evidence for Earth-ground case: ${slug}" >&2
        return 1
      fi
    done
    if ! grep -q 'SUMMARY reason=ATMOSPHERE_GROUND_OK' "$LOG"; then
      echo "ERROR: Earth-ground matrix did not finish cleanly" >&2
      return 1
    fi
    if grep '^ATMOS_STATE ' "$LOG" | grep -qv ' exposureSettled=True'; then
      echo "ERROR: Earth-ground capture reached its safety limit before exposure settled" >&2
      return 1
    fi
  elif [[ "$MODE" == "atmosphere" ]]; then
    local required=(
      ground_day ground_sunrise ground_sunset ground_night
      10km_day 30km_day 70km_day 120km_day 400km_day
      10km_night 30km_night 70km_night 120km_night 400km_night
      eclipse_clear eclipse_partial_central eclipse_partial_limb eclipse_total
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

    # Eclipse fixtures are synthetic but geometrically physical.  The CPU limb-darkened
    # oracle and the live SunController must agree, while the four cases preserve the
    # expected ordering: clear > partial limb > partial central > total.
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
        print "ERROR: eclipse gate: " message > "/dev/stderr"
        bad = 1
      }
      /^ATMOS_STATE / {
        slug = value("slug")
        eclipse = value("eclipse")
        if (eclipse == "none") next
        visibility[slug] = value("eclipseVisibility") + 0
        solar[slug] = value("solarVisibility") + 0
        separation[slug] = value("separationRad") + 0
        if (visibility[slug] < -1e-6 || visibility[slug] > 1.000001)
          reject(slug " visibility outside [0,1]: " visibility[slug])
        if (abs(visibility[slug] - solar[slug]) > 0.05)
          reject(slug " CPU/GPU solar visibility mismatch: cpu=" visibility[slug] \
            " runtime=" solar[slug])
      }
      END {
        required[1] = "eclipse_clear"
        required[2] = "eclipse_partial_central"
        required[3] = "eclipse_partial_limb"
        required[4] = "eclipse_total"
        for (i = 1; i <= 4; i++)
          if (!(required[i] in visibility)) reject("missing " required[i])
        if (visibility["eclipse_clear"] < 0.999)
          reject("clear fixture is not unobscured: " visibility["eclipse_clear"])
        if (!(visibility["eclipse_partial_central"] > 0.0 \
            && visibility["eclipse_partial_central"] < visibility["eclipse_clear"]))
          reject("central partial ordering invalid")
        if (!(visibility["eclipse_partial_limb"] > visibility["eclipse_partial_central"] \
            && visibility["eclipse_partial_limb"] < visibility["eclipse_clear"]))
          reject("limb partial ordering invalid")
        if (visibility["eclipse_total"] > 0.02)
          reject("totality still receives direct solar disc: " visibility["eclipse_total"])
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
if ! mkdir "$PLAYTEST_LOCK" 2>/dev/null; then
  lock_owner=""
  if [[ -f "$PLAYTEST_LOCK/owner" ]]; then
    lock_owner="$(<"$PLAYTEST_LOCK/owner")"
  fi
  if [[ "$lock_owner" =~ ^[0-9]+$ ]] && kill -0 "$lock_owner" 2>/dev/null; then
    echo "ERROR: another visual_playtest process is already using the temporary autoload ($PLAYTEST_LOCK)" >&2
    echo "  owner pid=$lock_owner" >&2
    exit 1
  fi
  # A killed shell cannot run the cleanup trap. Recover only an unowned lock
  # directory; a live owner always wins the race and remains protected.
  rm -f "$PLAYTEST_LOCK/owner" 2>/dev/null || true
  if ! rmdir "$PLAYTEST_LOCK" 2>/dev/null || ! mkdir "$PLAYTEST_LOCK" 2>/dev/null; then
    echo "ERROR: visual_playtest lock is busy or could not be recovered ($PLAYTEST_LOCK)" >&2
    exit 1
  fi
fi
printf '%s\n' "$$" > "$PLAYTEST_LOCK/owner"
OWNS_LOCK=1
OWNS_HARNESS=1

register_autoload

if [[ "$SKIP_BUILD" -eq 0 ]]; then
  dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
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
    prepare_godot_log_file
    write_harness
    dotnet build Exosphere.csproj --no-restore --nologo -v quiet
    if [[ -n "$EXTERNAL_DISPLAY" ]]; then
      DISPLAY="$EXTERNAL_DISPLAY" env \
        EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" "$GODOT" \
        --path . --rendering-driver opengl3 \
        --resolution "$RESOLUTION" \
        --log-file "$GODOT_LOG_FILE" \
        res://scenes/flight/Flight.tscn 2>&1 | tee -a "$CONSOLE_LOG"
    else
      xvfb-run -a -s "-screen 0 ${RESOLUTION}x24" env \
        EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" "$GODOT" \
        --path . --rendering-driver opengl3 \
        --resolution "$RESOLUTION" \
        --log-file "$GODOT_LOG_FILE" \
        res://scenes/flight/Flight.tscn 2>&1 | tee -a "$CONSOLE_LOG"
    fi
    cat "$LOG" >> "$COMBINED_LOG"
    cat "$CONSOLE_LOG" >> "$COMBINED_CONSOLE_LOG"
    rm -f "$LOG" "$CONSOLE_LOG"
  done
  LOG="$COMBINED_LOG"
  CONSOLE_LOG="$COMBINED_CONSOLE_LOG"
else
  HARNESS_MODE="$MODE"
  write_harness
  dotnet build Exosphere.csproj --no-restore --nologo -v quiet
  prepare_godot_log_file
  if [[ -n "$EXTERNAL_DISPLAY" ]]; then
    DISPLAY="$EXTERNAL_DISPLAY" env \
      EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" "$GODOT" \
      --path . --rendering-driver opengl3 \
      --resolution "$RESOLUTION" \
      --log-file "$GODOT_LOG_FILE" \
      res://scenes/flight/Flight.tscn 2>&1 | tee -a "$CONSOLE_LOG"
  else
    xvfb-run -a -s "-screen 0 ${RESOLUTION}x24" env \
      EXOSPHERE_PLAYTEST_TOKEN="$RUN_TOKEN" "$GODOT" \
      --path . --rendering-driver opengl3 \
      --resolution "$RESOLUTION" \
      --log-file "$GODOT_LOG_FILE" \
      res://scenes/flight/Flight.tscn 2>&1 | tee -a "$CONSOLE_LOG"
  fi
fi

verify_pngs

if (( SUN_ELEVATION_SET == 1 )) || [[ -n "$CAMERA_PRESET" ]]; then
  if ! grep -q '^VISUAL_SUN .*physicalSunPositionUnchanged=True' "$LOG"; then
    echo "ERROR: deterministic visual run is missing VISUAL_SUN telemetry" >&2
    exit 1
  fi
  if [[ -n "$CAMERA_PRESET" ]] \
    && ! grep -q "^VISUAL_CAMERA preset=${CAMERA_PRESET} " "$LOG"; then
    echo "ERROR: deterministic visual run is missing VISUAL_CAMERA preset telemetry" >&2
    exit 1
  fi
fi

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
elif [[ "$MODE" == "orbital_reentry" ]]; then
  echo "visual_playtest: normal orbital Starbase reentry verification OK — physical catch confirmed"
elif [[ "$MODE" == "hotstage" ]]; then
  echo "visual_playtest: hot-stage overlap capture OK"
elif [[ "$MODE" == "reentry_compare" ]]; then
  echo "visual_playtest: reentry attitude compare OK — see REENTRY_COMPARE rows in $LOG"
elif [[ "$MODE" == "atmosphere_bodies" ]]; then
  echo "visual_playtest: Mars/Venus atmosphere matrix OK — compare IMAGE/PERF rows in $LOG"
elif [[ "$MODE" == "atmosphere_low" ]]; then
  echo "visual_playtest: low-atmosphere shader diagnostic OK — compare IMAGE/PERF rows in $LOG"
elif [[ "$MODE" == "atmosphere_ground" ]]; then
  echo "visual_playtest: Earth-ground lighting matrix OK — compare IMAGE/PERF rows in $LOG"
elif [[ "$MODE" == "atmosphere" ]]; then
  echo "visual_playtest: atmosphere matrix OK — compare IMAGE/PERF rows in $LOG"
elif [[ "$MODE" == "lunar_map" ]]; then
  echo "visual_playtest: lunar Lambert map verification OK"
else
  echo "visual_playtest: full run complete — review $LOG for GAP lines"
fi
