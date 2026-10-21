#!/usr/bin/env bash
# Static contract checks (bash/python source and fixture gates). They need no .NET build
# and no Godot, so local tools/ci_check.sh and the GitHub workflow both run this same list:
# a contract that only ran locally once shipped green remotely while broken.
set -euo pipefail
cd "$(dirname "$0")/.."

bash -n tools/visual_playtest.sh
python3 tools/tests/atmosphere_render_state_test.py
python3 -m unittest discover -s tools/tests -p test_flight14_telemetry.py
python3 -m unittest discover -s tools/tests -p test_flight14_booster_comparison.py
bash tools/tests/visual_playtest_contract_test.sh
bash tools/tests/godot_smoke_log_contract_test.sh
bash tools/tests/visual_shader_failure_contract_test.sh
bash tools/tests/gameplay_regression_contract_test.sh
bash tools/tests/flight_startup_contract_test.sh
bash tools/tests/sky_runtime_performance_contract_test.sh
bash tools/tests/physics_hotpath_contract_test.sh
bash tools/tests/starship_hotpath_contract_test.sh
bash tools/tests/visual_telemetry_contract_test.sh
bash tools/tests/visual_material_fill_contract_test.sh
bash tools/tests/main_menu_responsive_contract_test.sh
bash tools/tests/vab_preview_lighting_contract_test.sh
bash tools/tests/launch_visual_lighting_contract_test.sh
bash tools/tests/vab_picking_alignment_contract_test.sh
bash tools/tests/hud_alert_layout_contract_test.sh
bash tools/tests/edl_overlay_layout_contract_test.sh
bash tools/tests/edl_visual_presentation_contract_test.sh
bash tools/tests/cockpit_visual_material_contract_test.sh
bash tools/tests/atmosphere_low_altitude_prefilter_contract_test.sh
bash tools/tests/space_sky_banding_contract_test.sh
bash tools/tests/earth_ground_lighting_contract_test.sh
bash tools/tests/starbase_scaled_earth_lighting_contract_test.sh
bash tools/tests/starbase_3dep_relief_contract_test.sh
bash tools/tests/earth_relief_pipeline_contract_test.sh
bash tools/tests/solar_cycle_contract_test.sh
bash tools/tests/mars_terrain_lighting_contract_test.sh
bash tools/tests/edl_catch_guidance_contract_test.sh
bash tools/tests/planet_body_lighting_contract_test.sh
bash tools/tests/visual_camera_planet_framing_contract_test.sh
bash tools/tests/visual_camera_transition_contract_test.sh
bash tools/tests/visual_daylight_capture_contract_test.sh
bash tools/tests/hotstage_visual_anchor_contract_test.sh
bash tools/tests/orbital_plume_visual_contract_test.sh
bash tools/tests/engine_hud_semantics_contract_test.sh
bash tools/tests/engine_hud_visual_semantics_contract_test.sh
bash tools/tests/engine_failure_disabled_contract_test.sh
bash tools/tests/cockpit_subviewport_contract_test.sh
bash tools/tests/render_performance_probe_contract_test.sh
bash tools/tests/saturn_ring_contract_test.sh
bash tools/perf/phase4_gpu_probe_contract_test.sh
bash tools/perf/texture_gpu_matrix_contract_test.sh
bash tools/perf/scheduler_phase6_benchmark_contract_test.sh
bash tools/perf/allocations_tick_phase23_contract_test.sh
bash tools/perf/rails_eventpipe_phase24_contract_test.sh
bash tools/tests/atmosphere_phase23_contract_test.sh
bash tools/tests/render_cadence_phase23_contract_test.sh
bash tools/tests/launch_pad_performance_contract_test.sh
bash tools/tests/maxq_ring_performance_contract_test.sh
bash tools/tests/reentry_plasma_performance_contract_test.sh
bash tools/tests/camera_shake_performance_contract_test.sh
bash tools/tests/audio_manager_performance_contract_test.sh
bash tools/tests/engine_startup_performance_contract_test.sh
bash tools/tests/starfield_performance_contract_test.sh
bash tools/tests/launch_effects_performance_contract_test.sh
bash tools/tests/visual_exposure_performance_contract_test.sh
bash tools/tests/optimization_phase23_contract_test.sh

echo "contract_checks: all contracts passed"
