#!/usr/bin/env bash
set -euo pipefail

# Phase 4 GPU probe contract.
#
# This is deliberately a read-only, opt-in harness. Godot's command-line
# --benchmark-file records startup/shutdown phases, not per-frame GPU
# timestamps. The real in-process sources are RenderingServer viewport render
# measurements and RenderingServer rendering-info counters. No C# instrumentation
# is added here, so this probe must never turn process FPS, wall time, or an
# adapter name into a GPU measurement.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FORMAT_VERSION="phase4_gpu_probe_v1"
DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"

usage() {
  cat <<'EOF'
Usage:
  tools/perf/phase4_gpu_probe.sh [options]
  tools/perf/phase4_gpu_probe.sh --validate REPORT

The default is an audit-only report. Use --run to launch Godot explicitly.
The probe never claims GPU FPS, GPU frame time, or VRAM from process timing.

Options:
  --run                  Launch the selected scene for the requested frames.
  --scene PATH           Scene to launch (default: res://scenes/flight/Flight.tscn).
  --frames N             Iterations before Godot exits (default: 120).
  --display MODE         auto, native, xvfb, or headless (default: auto).
  --driver DRIVER        auto, opengl3, or vulkan (default: opengl3).
  --method METHOD        auto, gl_compatibility, mobile, or forward_plus.
  --resolution WxH       Window resolution for a rendered run (default: 1920x1080).
  --gpu-index N          Optional Godot GPU index.
  --timeout SEC          Run timeout (default: 180).
  --godot PATH           Godot 4.6.3 mono executable.
  --out-dir DIR          Artifact directory (default: /tmp/exo_phase4_gpu_probe_<pid>).
  --validate REPORT      Validate a phase4_gpu_probe_v1 key=value report and exit.
  -h, --help             Show this help.

Important:
  --display xvfb is a framebuffer probe and commonly uses llvmpipe/software
  rendering. It is useful for command and scene smoke only, not for GPU claims.
EOF
}

die() {
  echo "phase4_gpu_probe: FAIL $*" >&2
  exit 2
}

metric_value() {
  local key="$1" report="$2"
  sed -n "s/^${key}=//p" "$report" | tail -n 1
}

is_nonnegative_integer() {
  [[ "$1" =~ ^[0-9]+$ ]]
}

validate_report() {
  local report="$1"
  [[ -f "$report" ]] || { echo "FAIL report does not exist: $report" >&2; return 1; }

  if awk 'NF == 0 || $0 !~ /^[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]+$/ { bad = 1 } END { exit bad }' "$report"; then
    :
  else
    echo "FAIL report contains malformed key=value data" >&2
    return 1
  fi

  local required key value
  required=(
    format_version status probe_mode godot_version scene frames_requested
    display_mode display_backend rendering_method_request rendering_driver_request
    rendering_method_observed rendering_driver_observed adapter_observed adapter_source
    real_gpu_observed software_renderer_detected godot_cli_benchmark
    godot_cli_benchmark_file godot_cli_gpu_profile godot_cli_profiling
    godot_cli_extra_gpu_memory_tracking gpu_profile_output
    godot_process_exit_code benchmark_file benchmark_phase_count benchmark_scope
    rendering_server_api gpu_viewport_time_api gpu_rendering_info_api
    gpu_memory_report_api gpu_frame_time_source gpu_frame_time_p50_ms
    gpu_frame_time_p95_ms gpu_frame_time_p99_ms gpu_vram_source gpu_vram_bytes
    fps_source fps_p50 fps_p95 fps_p99
  )
  for key in "${required[@]}"; do
    grep -q "^${key}=" "$report" || {
      echo "FAIL report is missing ${key}" >&2
      return 1
    }
  done

  [[ "$(metric_value format_version "$report")" == "$FORMAT_VERSION" ]] || {
    echo "FAIL unsupported format version" >&2
    return 1
  }
  [[ "$(metric_value status "$report")" == "NOT_MEASURED" ]] || {
    echo "FAIL status must remain NOT_MEASURED until an in-process GPU source is added" >&2
    return 1
  }
  case "$(metric_value probe_mode "$report")" in audit|run) ;; *)
    echo "FAIL unsupported probe mode" >&2
    return 1
  esac
  case "$(metric_value display_mode "$report")" in auto|native|xvfb|headless) ;; *)
    echo "FAIL unsupported display mode" >&2
    return 1
  esac
  value="$(metric_value frames_requested "$report")"
  is_nonnegative_integer "$value" || { echo "FAIL frames_requested is not an integer" >&2; return 1; }
  value="$(metric_value godot_process_exit_code "$report")"
  [[ "$value" == "NA" || "$value" =~ ^[0-9]+$ ]] || {
    echo "FAIL godot_process_exit_code is not NA or an integer" >&2
    return 1
  }
  value="$(metric_value benchmark_phase_count "$report")"
  [[ "$value" == "NOT_MEASURED" || "$value" =~ ^[0-9]+$ ]] || {
    echo "FAIL benchmark_phase_count is not NOT_MEASURED or an integer" >&2
    return 1
  }
  case "$(metric_value real_gpu_observed "$report")" in false|unknown) ;; *)
    echo "FAIL real_gpu_observed may not claim true from this shell probe" >&2
    return 1
  esac
  case "$(metric_value software_renderer_detected "$report")" in true|false|unknown) ;; *)
    echo "FAIL software_renderer_detected is not boolean/unknown" >&2
    return 1
  esac

  # This is the central fail-closed gate. The shell can discover a renderer
  # header and an adapter label, but it cannot produce a GPU timestamp or
  # driver-resident VRAM measurement. Numeric values are rejected until the
  # report contract is deliberately extended with an in-process source.
  for key in \
    gpu_frame_time_source gpu_frame_time_p50_ms gpu_frame_time_p95_ms gpu_frame_time_p99_ms \
    gpu_vram_source gpu_vram_bytes fps_source fps_p50 fps_p95 fps_p99; do
    [[ "$(metric_value "$key" "$report")" == "NOT_MEASURED" ]] || {
      echo "FAIL ${key} must be NOT_MEASURED" >&2
      return 1
    }
  done
  [[ "$(metric_value rendering_server_api "$report")" == "IN_PROCESS_ONLY" ]] || {
    echo "FAIL rendering_server_api must identify the in-process boundary" >&2
    return 1
  }
  [[ "$(metric_value gpu_viewport_time_api "$report")" == "RenderingServer_ViewportGetMeasuredRenderTimeGpu" ]] || {
    echo "FAIL viewport GPU API marker is incorrect" >&2
    return 1
  }
  [[ "$(metric_value gpu_rendering_info_api "$report")" == "RenderingServer_GetRenderingInfo" ]] || {
    echo "FAIL RenderingServer info API marker is incorrect" >&2
    return 1
  }
  [[ "$(metric_value gpu_memory_report_api "$report")" == "RenderingDevice_GetDriverAndDeviceMemoryReport_VulkanOnly" ]] || {
    echo "FAIL Vulkan memory API marker is incorrect" >&2
    return 1
  }
  echo "phase4_gpu_probe: report valid ($report)"
}

strip_ansi() {
  sed $'s/\033\[[0-9;]*[[:alpha:]]//g'
}

safe_value() {
  # Reports are intentionally strict key=value records. Paths and startup
  # headers are encoded rather than emitted with whitespace.
  printf '%s' "$1" | tr '\n\t ' '___' | sed 's/=/~/g'
}

has_help_flag() {
  local help="$1" flag="$2"
  grep -Eq "(^|[[:space:]])${flag}([[:space:]]|$)" <<<"$help"
}

display_is_live() {
  [[ -n "${DISPLAY:-}" ]] || return 1
  command -v xdpyinfo >/dev/null 2>&1 || return 1
  xdpyinfo >/dev/null 2>&1
}

parse_benchmark_phase_count() {
  local benchmark="$1"
  if [[ -f "$benchmark" ]] && command -v jq >/dev/null 2>&1 && jq -e 'type == "object"' "$benchmark" >/dev/null 2>&1; then
    jq 'length' "$benchmark"
  else
    echo "NOT_MEASURED"
  fi
}

GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
RUN=0
SCENE="res://scenes/flight/Flight.tscn"
FRAMES=120
DISPLAY_MODE="auto"
DRIVER="opengl3"
METHOD="auto"
RESOLUTION="1920x1080"
GPU_INDEX=""
TIMEOUT_SEC=180
OUT_DIR="/tmp/exo_phase4_gpu_probe_$$"

if [[ "${1:-}" == "--validate" ]]; then
  [[ $# -eq 2 ]] || die "--validate requires one report path"
  validate_report "$2"
  exit 0
fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run) RUN=1; shift ;;
    --scene) [[ $# -ge 2 ]] || die "--scene requires a value"; SCENE="$2"; shift 2 ;;
    --frames) [[ $# -ge 2 ]] || die "--frames requires a value"; FRAMES="$2"; shift 2 ;;
    --display) [[ $# -ge 2 ]] || die "--display requires a value"; DISPLAY_MODE="$2"; shift 2 ;;
    --driver) [[ $# -ge 2 ]] || die "--driver requires a value"; DRIVER="$2"; shift 2 ;;
    --method) [[ $# -ge 2 ]] || die "--method requires a value"; METHOD="$2"; shift 2 ;;
    --resolution) [[ $# -ge 2 ]] || die "--resolution requires a value"; RESOLUTION="$2"; shift 2 ;;
    --gpu-index) [[ $# -ge 2 ]] || die "--gpu-index requires a value"; GPU_INDEX="$2"; shift 2 ;;
    --timeout) [[ $# -ge 2 ]] || die "--timeout requires a value"; TIMEOUT_SEC="$2"; shift 2 ;;
    --godot) [[ $# -ge 2 ]] || die "--godot requires a value"; GODOT="$2"; shift 2 ;;
    --out-dir) [[ $# -ge 2 ]] || die "--out-dir requires a value"; OUT_DIR="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

case "$DISPLAY_MODE" in auto|native|xvfb|headless) ;; *) die "--display must be auto, native, xvfb, or headless" ;; esac
case "$DRIVER" in auto|opengl3|vulkan) ;; *) die "--driver must be auto, opengl3, or vulkan" ;; esac
case "$METHOD" in auto|gl_compatibility|mobile|forward_plus) ;; *) die "unsupported --method: $METHOD" ;; esac
[[ "$FRAMES" =~ ^[0-9]+$ ]] && (( FRAMES >= 1 && FRAMES <= 100000 )) || die "--frames must be 1..100000"
[[ "$TIMEOUT_SEC" =~ ^[0-9]+$ ]] && (( TIMEOUT_SEC >= 5 && TIMEOUT_SEC <= 7200 )) || die "--timeout must be 5..7200"
[[ "$RESOLUTION" =~ ^[1-9][0-9]*x[1-9][0-9]*$ ]] || die "--resolution must be WxH"
[[ -x "$GODOT" ]] || die "Godot executable not found: $GODOT"

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
REPORT="$OUT_DIR/gpu_probe.tsv"
STDOUT_LOG="$OUT_DIR/godot.stdout"
GODOT_LOG="$OUT_DIR/godot.log"
BENCHMARK_FILE="$OUT_DIR/godot_benchmark.json"

version="$("$GODOT" --version 2>/dev/null | head -n 1 | tr -d '\r')"
[[ -n "$version" ]] || die "could not read Godot version"
help_text="$("$GODOT" --help 2>&1 | strip_ansi)"
cli_benchmark="unavailable"; has_help_flag "$help_text" --benchmark && cli_benchmark="available"
cli_benchmark_file="unavailable"; has_help_flag "$help_text" --benchmark-file && cli_benchmark_file="available"
cli_gpu_profile="unavailable"; has_help_flag "$help_text" --gpu-profile && cli_gpu_profile="available"
  cli_profiling="unavailable"; has_help_flag "$help_text" --profiling && cli_profiling="available"
  cli_extra_gpu_memory_tracking="unavailable"; has_help_flag "$help_text" --extra-gpu-memory-tracking && cli_extra_gpu_memory_tracking="available"

probe_mode="audit"
display_backend="not_started"
godot_exit="NA"
benchmark_phase_count="NOT_MEASURED"
benchmark_scope="not_run"
rendering_method_observed="NOT_MEASURED"
rendering_driver_observed="NOT_MEASURED"
adapter_observed="NOT_MEASURED"
adapter_source="NOT_MEASURED"
software_renderer="unknown"
real_gpu="unknown"

if (( RUN == 1 )); then
  probe_mode="run"
  case "$DISPLAY_MODE" in
    auto)
      if display_is_live; then display_backend="native"; else
        command -v xvfb-run >/dev/null 2>&1 && display_backend="xvfb" || display_backend="headless"
      fi
      ;;
    native) display_backend="native" ;;
    xvfb) display_backend="xvfb" ;;
    headless) display_backend="headless" ;;
  esac

  if [[ "$display_backend" == headless ]]; then
    rendering_driver_observed="dummy"
    software_renderer="true"
    real_gpu="false"
  fi

  engine_args=(
    "$GODOT" --path "$ROOT" --scene "$SCENE" --quit-after "$FRAMES"
    --log-file "$GODOT_LOG" --verbose
  )
  if [[ "$cli_benchmark" == available ]]; then engine_args+=(--benchmark); fi
  if [[ "$cli_benchmark_file" == available ]]; then engine_args+=(--benchmark-file "$BENCHMARK_FILE"); fi
  if [[ "$cli_gpu_profile" == available ]]; then engine_args+=(--gpu-profile); fi
  if [[ "$cli_profiling" == available ]]; then engine_args+=(--profiling); fi
  if [[ "$DRIVER" != auto ]]; then engine_args+=(--rendering-driver "$DRIVER"); fi
  if [[ "$METHOD" != auto ]]; then engine_args+=(--rendering-method "$METHOD"); fi
  if [[ -n "$GPU_INDEX" ]]; then engine_args+=(--gpu-index "$GPU_INDEX"); fi
  if [[ "$display_backend" != headless ]]; then engine_args+=(--resolution "$RESOLUTION"); fi

  set +e
  case "$display_backend" in
    native)
      timeout "$TIMEOUT_SEC" "${engine_args[@]}" >"$STDOUT_LOG" 2>&1
      godot_exit=$?
      ;;
    xvfb)
      timeout "$TIMEOUT_SEC" xvfb-run -a -s "-screen 0 ${RESOLUTION}x24" "${engine_args[@]}" >"$STDOUT_LOG" 2>&1
      godot_exit=$?
      ;;
    headless)
      timeout "$TIMEOUT_SEC" "${engine_args[@]}" --headless >"$STDOUT_LOG" 2>&1
      godot_exit=$?
      ;;
  esac
  set -e

  benchmark_phase_count="$(parse_benchmark_phase_count "$BENCHMARK_FILE")"
  [[ -f "$BENCHMARK_FILE" ]] && benchmark_scope="startup_shutdown_only"
  observed_line="$(rg -i 'OpenGL API|Vulkan API|Using Device:' "$STDOUT_LOG" "$GODOT_LOG" 2>/dev/null | tail -n 1 || true)"
  if [[ "$observed_line" == *"OpenGL API"* ]]; then rendering_driver_observed="opengl3"; fi
  if [[ "$observed_line" == *"Vulkan API"* ]]; then rendering_driver_observed="vulkan"; fi
  if [[ "$observed_line" == *"Compatibility"* ]]; then rendering_method_observed="gl_compatibility"; fi
  if [[ "$observed_line" == *"Forward+"* ]]; then rendering_method_observed="forward_plus"; fi
  if [[ "$observed_line" == *"Mobile"* ]]; then rendering_method_observed="mobile"; fi
  if [[ "$observed_line" == *"Using Device:"* ]]; then
    adapter_observed="$(safe_value "$(sed -E 's/.*Using Device:[[:space:]]*//' <<<"$observed_line")")"
    adapter_source="godot_startup_header"
  fi
  if rg -qi 'llvmpipe|softpipe|lavapipe|swiftshader|software rasterizer' "$STDOUT_LOG" "$GODOT_LOG" 2>/dev/null; then
    software_renderer="true"
    real_gpu="false"
  elif [[ "$adapter_observed" != NOT_MEASURED ]]; then
    software_renderer="false"
    real_gpu="unknown"
  fi
fi

status="NOT_MEASURED"
if (( RUN == 1 )) && [[ "$godot_exit" != 0 ]]; then status="FAIL"; fi
{
  echo "format_version=$FORMAT_VERSION"
  echo "status=$status"
  echo "probe_mode=$probe_mode"
  echo "godot_version=$(safe_value "$version")"
  echo "scene=$(safe_value "$SCENE")"
  echo "frames_requested=$FRAMES"
  echo "display_mode=$DISPLAY_MODE"
  echo "display_backend=$display_backend"
  echo "rendering_method_request=$METHOD"
  echo "rendering_driver_request=$DRIVER"
  echo "rendering_method_observed=$rendering_method_observed"
  echo "rendering_driver_observed=$rendering_driver_observed"
  echo "adapter_observed=$adapter_observed"
  echo "adapter_source=$adapter_source"
  echo "real_gpu_observed=$real_gpu"
  echo "software_renderer_detected=$software_renderer"
  echo "godot_cli_benchmark=$cli_benchmark"
  echo "godot_cli_benchmark_file=$cli_benchmark_file"
  echo "godot_cli_gpu_profile=$cli_gpu_profile"
  echo "godot_cli_profiling=$cli_profiling"
  echo "godot_cli_extra_gpu_memory_tracking=$cli_extra_gpu_memory_tracking"
  echo "gpu_profile_output=NOT_MACHINE_READABLE"
  echo "godot_process_exit_code=$godot_exit"
  echo "benchmark_file=$([[ -f "$BENCHMARK_FILE" ]] && safe_value "$BENCHMARK_FILE" || echo not_created)"
  echo "benchmark_phase_count=$benchmark_phase_count"
  echo "benchmark_scope=$benchmark_scope"
  echo "rendering_server_api=IN_PROCESS_ONLY"
  echo "gpu_viewport_time_api=RenderingServer_ViewportGetMeasuredRenderTimeGpu"
  echo "gpu_rendering_info_api=RenderingServer_GetRenderingInfo"
  echo "gpu_memory_report_api=RenderingDevice_GetDriverAndDeviceMemoryReport_VulkanOnly"
  echo "gpu_frame_time_source=NOT_MEASURED"
  echo "gpu_frame_time_p50_ms=NOT_MEASURED"
  echo "gpu_frame_time_p95_ms=NOT_MEASURED"
  echo "gpu_frame_time_p99_ms=NOT_MEASURED"
  echo "gpu_vram_source=NOT_MEASURED"
  echo "gpu_vram_bytes=NOT_MEASURED"
  echo "fps_source=NOT_MEASURED"
  echo "fps_p50=NOT_MEASURED"
  echo "fps_p95=NOT_MEASURED"
  echo "fps_p99=NOT_MEASURED"
} > "$REPORT"

if [[ "$status" == FAIL ]]; then
  echo "phase4_gpu_probe: FAIL Godot exit=$godot_exit artifacts=$OUT_DIR report=$REPORT" >&2
  exit "$godot_exit"
fi
validate_report "$REPORT"
echo "phase4_gpu_probe: status=$status artifacts=$OUT_DIR report=$REPORT"
echo "phase4_gpu_probe: GPU frame time, VRAM, and FPS remain NOT_MEASURED (in-process API required)"
