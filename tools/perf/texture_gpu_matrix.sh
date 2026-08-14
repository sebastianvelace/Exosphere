#!/usr/bin/env bash
set -euo pipefail

# Reproducible Earth/star texture matrix. The production worktree is never
# edited: every variant is imported and measured in a detached temporary
# worktree. A software renderer can validate the harness, but cannot satisfy
# the physical-GPU gate.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
FORMAT_VERSION="texture_gpu_matrix_v1"
VARIANT_IDS=(8k_nomip 8k_mip 4k_mip 2k_mip)
TEXTURE_IMPORTS=(
  assets/textures/earth_day.jpg.import
  assets/textures/earth_night.jpg.import
  assets/textures/earth_clouds.jpg.import
  assets/textures/starmap_milkyway_8k.jpg.import
)

RUN=0
DISPLAY_MODE="auto"
DRIVER="opengl3"
METHOD="auto"
RESOLUTION="1920x1080"
FRAMES=60
TIMEOUT_SEC=180
IMPORT_TIMEOUT_SEC=300
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"
OUT_DIR="/tmp/exo_texture_gpu_matrix_$$"
VISUAL_MODE="none"
RUN_ID="texture-matrix-$$"
ALLOW_SOFTWARE=0
SKIP_BUILD=0
OFFLINE=0

usage() {
  cat <<'EOF'
Usage:
  tools/perf/texture_gpu_matrix.sh [options]
  tools/perf/texture_gpu_matrix.sh --validate MATRIX_DIR

The default is a dry manifest. Use --run for the four-variant matrix:
  8k_nomip, 8k_mip, 4k_mip, 2k_mip.

Options:
  --run                  Create temporary worktrees and run the matrix.
  --display MODE         auto, native, xvfb, or headless (default: auto).
  --driver DRIVER        auto, opengl3, or vulkan (default: opengl3).
  --method METHOD        auto, gl_compatibility, mobile, or forward_plus.
  --resolution WxH       Render resolution (default: 1920x1080).
  --frames N             Probe iterations per variant (default: 60).
  --timeout SEC          Probe/visual timeout (default: 180).
  --import-timeout SEC   Godot import timeout (default: 300).
  --godot PATH           Godot 4.6.3 mono executable.
  --out-dir DIR          Matrix artifact directory.
  --run-id ID            Stable visual artifact id.
  --visual-mode MODE     none, smoke, cockpit, saturn, or atmosphere.
  --allow-software       Complete software-renderer diagnostics; never PASS hardware gate.
  --skip-build           Reuse existing candidate build (advanced).
  --offline              Ignore unavailable NuGet feeds and use the local package cache.
  --validate DIR         Validate matrix.meta and matrix.rows.tsv in DIR.
  -h, --help             Show this help.

By default --run requires a non-software adapter. Use --allow-software only to
exercise the harness on llvmpipe; that result is BLOCKED, never a physical-GPU PASS.
EOF
}

die() { echo "texture_gpu_matrix: FAIL $*" >&2; exit 2; }

metric_value() {
  local key="$1" file="$2"
  sed -n "s/^${key}=//p" "$file" | tail -n 1
}

safe_id() { [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]] || die "invalid id: $1"; }
require_integer() { [[ "$1" =~ ^[0-9]+$ ]]; }

validate_matrix() {
  local dir="$1" meta="$1/matrix.meta" rows="$1/matrix.rows.tsv"
  [[ -f "$meta" ]] || { echo "FAIL matrix.meta is missing" >&2; return 1; }
  [[ -f "$rows" ]] || { echo "FAIL matrix.rows.tsv is missing" >&2; return 1; }
  awk 'NF == 0 || $0 !~ /^[A-Za-z_][A-Za-z0-9_]*=[^[:space:]]+$/ { bad = 1 } END { exit bad }' "$meta" || {
    echo "FAIL matrix.meta contains malformed key=value data" >&2; return 1;
  }

  local key value
  for key in format_version status source_commit variant_count display_mode rendering_driver_request rendering_method_request resolution frames_requested visual_mode physical_gpu_gate software_renderer_observed; do
    grep -q "^${key}=" "$meta" || { echo "FAIL matrix.meta missing ${key}" >&2; return 1; }
  done
  [[ "$(metric_value format_version "$meta")" == "$FORMAT_VERSION" ]] || { echo "FAIL unsupported matrix format" >&2; return 1; }
  case "$(metric_value status "$meta")" in NOT_RUN|PASS|BLOCKED|FAIL) ;; *) echo "FAIL invalid matrix status" >&2; return 1 ;; esac
  case "$(metric_value physical_gpu_gate "$meta")" in NOT_RUN|PASS|BLOCKED|FAIL) ;; *) echo "FAIL invalid physical_gpu_gate" >&2; return 1 ;; esac
  case "$(metric_value software_renderer_observed "$meta")" in true|false|unknown) ;; *) echo "FAIL invalid software_renderer_observed" >&2; return 1 ;; esac
  value="$(metric_value variant_count "$meta")"; require_integer "$value" || { echo "FAIL variant_count is not an integer" >&2; return 1; }
  [[ "$value" == 4 ]] || { echo "FAIL variant_count must be 4" >&2; return 1; }
  value="$(metric_value frames_requested "$meta")"; require_integer "$value" || { echo "FAIL frames_requested is not an integer" >&2; return 1; }

  local header expected_header row_count variant status probe_status visual_status cache_bytes
  expected_header=$'variant_id\tmipmaps\tsize_limit\tstatus\timport_cache_bytes\tprobe_status\tprobe_report\tvisual_status\tvisual_summary\tnonsoftware_adapter'
  header="$(head -n 1 "$rows")"
  [[ "$header" == "$expected_header" ]] || { echo "FAIL invalid matrix row header" >&2; return 1; }
  row_count="$(tail -n +2 "$rows" | awk 'NF { count++ } END { print count + 0 }')"
  [[ "$row_count" == 4 ]] || { echo "FAIL matrix must contain four rows" >&2; return 1; }
  while IFS=$'\t' read -r variant _ _ status cache_bytes probe_status _ visual_status _ nonsoftware_adapter; do
    [[ -n "$variant" ]] || continue
    case "$variant" in 8k_nomip|8k_mip|4k_mip|2k_mip) ;; *) echo "FAIL unknown variant ${variant}" >&2; return 1 ;; esac
    case "$status" in NOT_RUN|PASS|BLOCKED|FAIL) ;; *) echo "FAIL invalid status for ${variant}" >&2; return 1 ;; esac
    case "$probe_status" in NOT_RUN|MEASURED|NOT_MEASURED|FAIL) ;; *) echo "FAIL invalid probe status for ${variant}" >&2; return 1 ;; esac
    case "$visual_status" in NOT_RUN|PASS|FAIL) ;; *) echo "FAIL invalid visual status for ${variant}" >&2; return 1 ;; esac
    case "$nonsoftware_adapter" in true|false|unknown) ;; *) echo "FAIL invalid adapter evidence for ${variant}" >&2; return 1 ;; esac
    [[ "$cache_bytes" == NOT_MEASURED || "$cache_bytes" =~ ^[0-9]+$ ]] || {
      echo "FAIL invalid import cache bytes for ${variant}" >&2; return 1;
    }
  done < <(tail -n +2 "$rows")
  echo "texture_gpu_matrix: matrix valid ($dir)"
}

if [[ "${1:-}" == "--validate" ]]; then
  [[ $# -eq 2 ]] || die "--validate requires one matrix directory"
  validate_matrix "$2"
  exit 0
fi

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run) RUN=1; shift ;;
    --display) [[ $# -ge 2 ]] || die "--display requires a value"; DISPLAY_MODE="$2"; shift 2 ;;
    --driver) [[ $# -ge 2 ]] || die "--driver requires a value"; DRIVER="$2"; shift 2 ;;
    --method) [[ $# -ge 2 ]] || die "--method requires a value"; METHOD="$2"; shift 2 ;;
    --resolution) [[ $# -ge 2 ]] || die "--resolution requires a value"; RESOLUTION="$2"; shift 2 ;;
    --frames) [[ $# -ge 2 ]] || die "--frames requires a value"; FRAMES="$2"; shift 2 ;;
    --timeout) [[ $# -ge 2 ]] || die "--timeout requires a value"; TIMEOUT_SEC="$2"; shift 2 ;;
    --import-timeout) [[ $# -ge 2 ]] || die "--import-timeout requires a value"; IMPORT_TIMEOUT_SEC="$2"; shift 2 ;;
    --godot) [[ $# -ge 2 ]] || die "--godot requires a value"; GODOT="$2"; shift 2 ;;
    --out-dir) [[ $# -ge 2 ]] || die "--out-dir requires a value"; OUT_DIR="$2"; shift 2 ;;
    --run-id) [[ $# -ge 2 ]] || die "--run-id requires a value"; RUN_ID="$2"; shift 2 ;;
    --visual-mode) [[ $# -ge 2 ]] || die "--visual-mode requires a value"; VISUAL_MODE="$2"; shift 2 ;;
    --allow-software) ALLOW_SOFTWARE=1; shift ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --offline) OFFLINE=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

case "$DISPLAY_MODE" in auto|native|xvfb|headless) ;; *) die "invalid --display" ;; esac
case "$DRIVER" in auto|opengl3|vulkan) ;; *) die "invalid --driver" ;; esac
case "$METHOD" in auto|gl_compatibility|mobile|forward_plus) ;; *) die "invalid --method" ;; esac
case "$VISUAL_MODE" in none|smoke|cockpit|saturn|atmosphere) ;; *) die "invalid --visual-mode" ;; esac
[[ "$RESOLUTION" =~ ^[1-9][0-9]*x[1-9][0-9]*$ ]] || die "invalid --resolution"
require_integer "$FRAMES" && (( FRAMES >= 1 && FRAMES <= 100000 )) || die "--frames must be 1..100000"
require_integer "$TIMEOUT_SEC" && (( TIMEOUT_SEC >= 5 && TIMEOUT_SEC <= 7200 )) || die "--timeout must be 5..7200"
require_integer "$IMPORT_TIMEOUT_SEC" && (( IMPORT_TIMEOUT_SEC >= 5 && IMPORT_TIMEOUT_SEC <= 7200 )) || die "--import-timeout must be 5..7200"
safe_id "$RUN_ID"
[[ -x "$GODOT" ]] || die "Godot executable not found: $GODOT"

mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
META="$OUT_DIR/matrix.meta"
ROWS="$OUT_DIR/matrix.rows.tsv"
REVISION="$(git -C "$ROOT" rev-parse HEAD)"

write_meta() {
  local status="$1" physical="$2" software="$3"
  {
    echo "format_version=$FORMAT_VERSION"
    echo "status=$status"
    echo "source_commit=$REVISION"
    echo "variant_count=4"
    echo "display_mode=$DISPLAY_MODE"
    echo "rendering_driver_request=$DRIVER"
    echo "rendering_method_request=$METHOD"
    echo "resolution=$RESOLUTION"
    echo "frames_requested=$FRAMES"
    echo "visual_mode=$VISUAL_MODE"
    if (( OFFLINE == 1 )); then echo "restore_mode=ignore_failed_sources"; else echo "restore_mode=strict"; fi
    echo "physical_gpu_gate=$physical"
    echo "software_renderer_observed=$software"
  } > "$META"
}

write_meta NOT_RUN NOT_RUN unknown
printf '%s\n' $'variant_id\tmipmaps\tsize_limit\tstatus\timport_cache_bytes\tprobe_status\tprobe_report\tvisual_status\tvisual_summary\tnonsoftware_adapter' > "$ROWS"
for variant in "${VARIANT_IDS[@]}"; do
  case "$variant" in
    8k_nomip) printf '%s\n' $'8k_nomip\tfalse\t0\tNOT_RUN\tNOT_MEASURED\tNOT_RUN\tNOT_RUN\tNOT_RUN\tNOT_RUN\tunknown' >> "$ROWS" ;;
    8k_mip) printf '%s\n' $'8k_mip\ttrue\t0\tNOT_RUN\tNOT_MEASURED\tNOT_RUN\tNOT_RUN\tNOT_RUN\tNOT_RUN\tunknown' >> "$ROWS" ;;
    4k_mip) printf '%s\n' $'4k_mip\ttrue\t4096\tNOT_RUN\tNOT_MEASURED\tNOT_RUN\tNOT_RUN\tNOT_RUN\tNOT_RUN\tunknown' >> "$ROWS" ;;
    2k_mip) printf '%s\n' $'2k_mip\ttrue\t2048\tNOT_RUN\tNOT_MEASURED\tNOT_RUN\tNOT_RUN\tNOT_RUN\tNOT_RUN\tunknown' >> "$ROWS" ;;
  esac
done

if (( RUN == 0 )); then
  validate_matrix "$OUT_DIR"
  echo "texture_gpu_matrix: dry manifest=$OUT_DIR"
  exit 0
fi

if [[ -n "$(git -C "$ROOT" status --porcelain)" ]]; then
  die "production worktree must be clean before creating candidates"
fi

TEMP_ROOT="$(mktemp -d /tmp/exo_texture_gpu_matrix.XXXXXX)"
WORKTREES=()
cleanup() {
  local candidate
  for candidate in "${WORKTREES[@]:-}"; do
    [[ -d "$candidate" ]] || continue
    git -C "$ROOT" worktree remove --force "$candidate" >/dev/null 2>&1 || true
  done
  rmdir "$TEMP_ROOT" >/dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM

replace_row() {
  local id="$1" new_row="$2" tmp="$OUT_DIR/.rows.tmp"
  awk -F '\t' -v id="$id" -v replacement="$new_row" 'BEGIN { OFS="\t" } NR == 1 || $1 != id { print } $1 == id { print replacement }' "$ROWS" > "$tmp"
  mv "$tmp" "$ROWS"
}

apply_variant_imports() {
  local candidate="$1" mipmaps="$2" size_limit="$3" import
  [[ "$mipmaps" == true ]] || return 0
  for import in "${TEXTURE_IMPORTS[@]}"; do
    sed -i -E "s/^mipmaps\/generate=.*/mipmaps\/generate=true/" "$candidate/$import"
    sed -i -E "s/^process\/size_limit=.*/process\/size_limit=$size_limit/" "$candidate/$import"
  done
}

all_cache_bytes() {
  find "$1/.godot/imported" -type f -printf '%s\n' 2>/dev/null \
    | awk '{ total += $1 } END { print total + 0 }'
}

visual_switch() {
  case "$VISUAL_MODE" in
    smoke) echo "--smoke" ;;
    cockpit) echo "--cockpit" ;;
    saturn) echo "--saturn" ;;
    atmosphere) echo "--atmosphere" ;;
    none) echo "" ;;
  esac
}

overall_failure=0
saw_nonsoftware=0
saw_software=0
saw_unknown=0

for variant in "${VARIANT_IDS[@]}"; do
  candidate="$TEMP_ROOT/$variant"
  git -C "$ROOT" worktree add --detach "$candidate" "$REVISION" >/dev/null
  WORKTREES+=("$candidate")

  mipmaps=false; size_limit=0
  case "$variant" in
    8k_mip) mipmaps=true; size_limit=0 ;;
    4k_mip) mipmaps=true; size_limit=4096 ;;
    2k_mip) mipmaps=true; size_limit=2048 ;;
  esac
  apply_variant_imports "$candidate" "$mipmaps" "$size_limit"
  variant_dir="$OUT_DIR/$variant"
  mkdir -p "$variant_dir"
  variant_status=PASS

  restore_args=()
  if (( OFFLINE == 1 )); then restore_args+=(--ignore-failed-sources); fi
  if ! dotnet restore "$candidate/Exosphere.csproj" --nologo "${restore_args[@]}" > "$variant_dir/dotnet_restore.log" 2>&1; then
    variant_status=FAIL
  fi
  if [[ "$variant_status" == PASS && "$SKIP_BUILD" -eq 0 ]] && ! dotnet build "$candidate/Exosphere.csproj" --no-restore --nologo -v quiet > "$variant_dir/dotnet_build.log" 2>&1; then
    variant_status=FAIL
  fi
  if [[ "$variant_status" == PASS ]] && ! timeout "$IMPORT_TIMEOUT_SEC" "$GODOT" --headless --path "$candidate" --import > "$variant_dir/import.log" 2>&1; then
    variant_status=FAIL
  fi

  cache_bytes=NOT_MEASURED
  if [[ -d "$candidate/.godot/imported" ]]; then cache_bytes="$(all_cache_bytes "$candidate")"; fi
  probe_status=NOT_RUN
  probe_report=NOT_RUN
  nonsoftware=unknown
  if [[ "$variant_status" == PASS ]]; then
    probe_dir="$variant_dir/probe"
    set +e
    bash "$candidate/tools/perf/phase4_gpu_probe.sh" --run \
      --display "$DISPLAY_MODE" --driver "$DRIVER" --method "$METHOD" \
      --resolution "$RESOLUTION" --frames "$FRAMES" --timeout "$TIMEOUT_SEC" \
      --godot "$GODOT" --out-dir "$probe_dir" > "$variant_dir/probe.stdout" 2>&1
    probe_exit=$?
    set -e
    probe_report="$variant/probe/gpu_probe.tsv"
    if [[ "$probe_exit" -ne 0 || ! -f "$probe_dir/gpu_probe.tsv" ]]; then
      probe_status=FAIL
      variant_status=FAIL
    else
      probe_status="$(metric_value status "$probe_dir/gpu_probe.tsv")"
      software="$(metric_value software_renderer_detected "$probe_dir/gpu_probe.tsv")"
      adapter="$(metric_value adapter_observed "$probe_dir/gpu_probe.tsv")"
      if [[ "$software" == false && "$adapter" != NOT_MEASURED ]]; then nonsoftware=true; saw_nonsoftware=1; fi
      if [[ "$software" == true ]]; then nonsoftware=false; saw_software=1; fi
      if [[ "$nonsoftware" == unknown ]]; then saw_unknown=1; fi
    fi
  fi

  visual_status=NOT_RUN
  visual_summary=NOT_RUN
  if [[ "$variant_status" == PASS && "$VISUAL_MODE" != none ]]; then
    visual_dir="$variant_dir/visual"
    visual_log="$variant_dir/visual.log"
    visual_arg="$(visual_switch)"
    set +e
    OUT_DIR="$visual_dir" LOG="$visual_log" bash "$candidate/tools/visual_playtest.sh" "$visual_arg" \
      --run-id "${RUN_ID}-${variant}" --skip-build > "$variant_dir/visual.stdout" 2>&1
    visual_exit=$?
    set -e
    visual_summary="$(sed -n 's/.*SUMMARY reason=\([^ ]*\).*/\1/p' "$visual_log" 2>/dev/null | tail -n 1)"
    visual_summary="${visual_summary:-NOT_MEASURED}"
    if [[ "$visual_exit" -eq 0 ]]; then visual_status=PASS; else visual_status=FAIL; variant_status=FAIL; fi
  fi

  if [[ "$variant_status" != PASS ]]; then overall_failure=1; fi
  printf -v new_row '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s' \
    "$variant" "$mipmaps" "$size_limit" "$variant_status" "$cache_bytes" \
    "$probe_status" "$probe_report" "$visual_status" "$visual_summary" "$nonsoftware"
  replace_row "$variant" "$new_row"

  if (( ALLOW_SOFTWARE == 0 )) && [[ "$nonsoftware" != true ]]; then
    echo "texture_gpu_matrix: BLOCKED non-physical/unknown renderer evidence in $variant; use --allow-software for diagnostics" >&2
    break
  fi
done

row_passes="$(tail -n +2 "$ROWS" | awk -F '\t' '$4 == "PASS" { count++ } END { print count + 0 }')"
if (( overall_failure != 0 )); then
  final_status=FAIL; physical_gate=FAIL
elif (( row_passes != 4 )); then
  final_status=BLOCKED; physical_gate=BLOCKED
elif (( ALLOW_SOFTWARE == 0 && saw_nonsoftware == 1 && saw_software == 0 && saw_unknown == 0 )); then
  final_status=PASS; physical_gate=PASS
else
  final_status=BLOCKED; physical_gate=BLOCKED
fi
if (( saw_software == 1 )); then software_observed=true
elif (( saw_nonsoftware == 1 )); then software_observed=false
else software_observed=unknown
fi
write_meta "$final_status" "$physical_gate" "$software_observed"
validate_matrix "$OUT_DIR"
echo "texture_gpu_matrix: status=$final_status physical_gpu_gate=$physical_gate artifacts=$OUT_DIR"
if [[ "$final_status" == FAIL ]]; then exit 1; fi
if [[ "$final_status" == BLOCKED ]]; then exit 3; fi
