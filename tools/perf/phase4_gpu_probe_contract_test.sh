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

measured="$TEST_DIR/measured.tsv"
sed -e 's/format_version=phase4_gpu_probe_v1/format_version=phase4_gpu_probe_v2/' \
    -e 's/status=NOT_MEASURED/status=MEASURED/' "$good" > "$measured"
cat >> "$measured" <<'EOF'
render_probe_enabled=true
render_probe_samples=4
render_cpu_time_source=in_process_rendering_server
render_cpu_time_p50_ms=8.000
render_cpu_time_p95_ms=12.000
render_cpu_time_p99_ms=12.000
render_draw_calls_source=in_process_rendering_server
render_draw_calls_p50=100.000
render_draw_calls_p95=110.000
render_draw_calls_p99=110.000
render_primitives_source=in_process_rendering_server
render_primitives_p50=2000.000
render_primitives_p95=2200.000
render_primitives_p99=2200.000
render_objects_source=in_process_rendering_server
render_objects_p50=50.000
render_objects_p95=55.000
render_objects_p99=55.000
render_video_mem_source=in_process_rendering_server
render_video_mem_p50_bytes=4096.000
render_video_mem_p95_bytes=8192.000
render_video_mem_p99_bytes=8192.000
EOF
sed -i \
  -e 's/gpu_frame_time_source=NOT_MEASURED/gpu_frame_time_source=in_process_rendering_server/' \
  -e 's/gpu_frame_time_p50_ms=NOT_MEASURED/gpu_frame_time_p50_ms=4.000/' \
  -e 's/gpu_frame_time_p95_ms=NOT_MEASURED/gpu_frame_time_p95_ms=5.000/' \
  -e 's/gpu_frame_time_p99_ms=NOT_MEASURED/gpu_frame_time_p99_ms=5.000/' \
  "$measured"
bash "$HARNESS" --validate "$measured"
echo "PASS in-process measured report accepted"

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

bad_measured_samples="$TEST_DIR/bad-measured-samples.tsv"
sed 's/render_probe_samples=4/render_probe_samples=0/' "$measured" > "$bad_measured_samples"
expect_failure "MEASURED report without samples" "$bad_measured_samples"

malformed="$TEST_DIR/malformed.tsv"
sed 's/adapter_source=NOT_MEASURED/adapter source=NOT_MEASURED/' "$good" > "$malformed"
expect_failure "malformed key=value line" "$malformed"

missing="$TEST_DIR/missing.tsv"
sed '/gpu_vram_bytes=/d' "$good" > "$missing"
expect_failure "missing required GPU field" "$missing"

echo "phase4_gpu_probe_contract_test: 1 valid and 5 invalid fixtures passed"
