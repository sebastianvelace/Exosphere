#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HARNESS="$ROOT/tools/perf/phase4_gpu_probe.sh"
TEST_DIR="$(mktemp -d /tmp/exo_phase4_gpu_contract.XXXXXX)"
trap 'rm -rf "$TEST_DIR"' EXIT

bash -n "$HARNESS" "$ROOT/tools/perf/phase4_gpu_probe_contract_test.sh"

good="$TEST_DIR/good.tsv"
cat > "$good" <<'EOF'
format_version=phase4_gpu_probe_v1
status=NOT_MEASURED
probe_mode=audit
godot_version=4.6.3.stable.mono.official
scene=res://scenes/flight/Flight.tscn
frames_requested=120
display_mode=auto
display_backend=not_started
rendering_method_request=auto
rendering_driver_request=opengl3
rendering_method_observed=NOT_MEASURED
rendering_driver_observed=NOT_MEASURED
adapter_observed=NOT_MEASURED
adapter_source=NOT_MEASURED
real_gpu_observed=unknown
software_renderer_detected=unknown
godot_cli_benchmark=available
godot_cli_benchmark_file=available
godot_cli_gpu_profile=available
godot_cli_profiling=available
godot_cli_extra_gpu_memory_tracking=available
gpu_profile_output=NOT_MACHINE_READABLE
godot_process_exit_code=NA
benchmark_file=not_created
benchmark_phase_count=NOT_MEASURED
benchmark_scope=not_run
rendering_server_api=IN_PROCESS_ONLY
gpu_viewport_time_api=RenderingServer_ViewportGetMeasuredRenderTimeGpu
gpu_rendering_info_api=RenderingServer_GetRenderingInfo
gpu_memory_report_api=RenderingDevice_GetDriverAndDeviceMemoryReport_VulkanOnly
gpu_frame_time_source=NOT_MEASURED
gpu_frame_time_p50_ms=NOT_MEASURED
gpu_frame_time_p95_ms=NOT_MEASURED
gpu_frame_time_p99_ms=NOT_MEASURED
gpu_vram_source=NOT_MEASURED
gpu_vram_bytes=NOT_MEASURED
fps_source=NOT_MEASURED
fps_p50=NOT_MEASURED
fps_p95=NOT_MEASURED
fps_p99=NOT_MEASURED
EOF

bash "$HARNESS" --validate "$good"
echo "PASS audit-only NOT_MEASURED report accepted"

expect_failure() {
  local name="$1" fixture="$2"
  if bash "$HARNESS" --validate "$fixture" >/dev/null 2>&1; then
    echo "FAIL invalid GPU probe fixture accepted: $name" >&2
    exit 1
  fi
  echo "PASS invalid GPU probe fixture rejected: $name"
}

fake_gpu="$TEST_DIR/fake-gpu.tsv"
sed 's/gpu_frame_time_p95_ms=NOT_MEASURED/gpu_frame_time_p95_ms=4.2/' "$good" > "$fake_gpu"
expect_failure "numeric GPU time without source" "$fake_gpu"

fake_fps="$TEST_DIR/fake-fps.tsv"
sed 's/fps_p95=NOT_MEASURED/fps_p95=60.0/' "$good" > "$fake_fps"
expect_failure "numeric FPS without source" "$fake_fps"

fake_status="$TEST_DIR/fake-status.tsv"
sed 's/status=NOT_MEASURED/status=PASS/' "$good" > "$fake_status"
expect_failure "PASS status without measured source" "$fake_status"

malformed="$TEST_DIR/malformed.tsv"
sed 's/adapter_source=NOT_MEASURED/adapter source=NOT_MEASURED/' "$good" > "$malformed"
expect_failure "malformed key=value line" "$malformed"

missing="$TEST_DIR/missing.tsv"
sed '/gpu_vram_bytes=/d' "$good" > "$missing"
expect_failure "missing required GPU field" "$missing"

echo "phase4_gpu_probe_contract_test: 1 valid and 5 invalid fixtures passed"
