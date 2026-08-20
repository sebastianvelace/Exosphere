#!/usr/bin/env bash
set -euo pipefail

# Guard anti-harness: los harnesses de captura visual temporales, escenas y autoloads
# temporales en project.godot NUNCA deben commitearse.
# Anti-harness guard: temporary visual-capture harness scripts/scenes and temporary
# autoloads in project.godot must NEVER be committed.
TRACKED_HARNESS="$(git ls-files 'scripts/_*Shot.cs' 'scripts/_*Shot.cs.uid' 'scripts/*VerifyShot.cs' 'scripts/*VerifyShot.cs.uid' 'scenes/*VerifyShot.tscn' 'scenes/*VerifyShot.tscn.uid')"
if [[ -n "$TRACKED_HARNESS" ]]; then
  echo "ERROR: temporary capture harness is tracked in git:"
  echo "$TRACKED_HARNESS"
  echo "Remove it before committing (see skill visual-testing / .gitignore)."
  exit 1
fi
if grep -Eq '(_[A-Za-z0-9]*Shot|[A-Za-z0-9]*VerifyShot)' project.godot; then
  echo "ERROR: a temporary capture autoload is present in project.godot."
  echo "Restore it with: git checkout project.godot"
  exit 1
fi

bash -n tools/visual_playtest.sh
bash tools/tests/visual_playtest_contract_test.sh
bash tools/tests/godot_smoke_log_contract_test.sh
bash tools/tests/gameplay_regression_contract_test.sh
bash tools/tests/flight_startup_contract_test.sh
bash tools/tests/sky_runtime_performance_contract_test.sh
bash tools/tests/physics_hotpath_contract_test.sh
bash tools/tests/starship_hotpath_contract_test.sh
bash tools/tests/visual_telemetry_contract_test.sh
bash tools/tests/visual_material_fill_contract_test.sh
bash tools/tests/main_menu_responsive_contract_test.sh
bash tools/tests/vab_preview_lighting_contract_test.sh
bash tools/tests/vab_picking_alignment_contract_test.sh
bash tools/tests/hud_alert_layout_contract_test.sh
bash tools/tests/edl_overlay_layout_contract_test.sh
bash tools/tests/edl_visual_presentation_contract_test.sh
bash tools/tests/cockpit_visual_material_contract_test.sh
bash tools/tests/atmosphere_low_altitude_prefilter_contract_test.sh
bash tools/tests/space_sky_banding_contract_test.sh
bash tools/tests/earth_ground_lighting_contract_test.sh
bash tools/tests/solar_cycle_contract_test.sh
bash tools/tests/mars_terrain_lighting_contract_test.sh
bash tools/tests/edl_catch_guidance_contract_test.sh
bash tools/tests/planet_body_lighting_contract_test.sh
bash tools/tests/visual_camera_planet_framing_contract_test.sh
bash tools/tests/visual_camera_transition_contract_test.sh
bash tools/tests/visual_daylight_capture_contract_test.sh
bash tools/tests/engine_hud_semantics_contract_test.sh
bash tools/tests/engine_hud_visual_semantics_contract_test.sh
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

dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
bash tools/flight_startup_quick_check.sh

DEFAULT_GODOT="/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"
GODOT="${GODOT_BIN:-$DEFAULT_GODOT}"

if [[ -x "$GODOT" ]]; then
  # Godot 4.6.3 can abort while copying its crash log when user://logs is absent
  # in a clean CI/Xvfb profile. Keep smoke output explicit and outside user://.
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3 \
    --log-file /tmp/exo_ci_main.godot.log
  "$GODOT" --headless --path . --quit-after 3 --rendering-driver opengl3 \
    --log-file /tmp/exo_ci_construction.godot.log \
    res://scenes/construction/Construction.tscn

  # Captura de viewport con framebuffer real: --headless usa el renderer dummy y no
  # produce píxeles. Para aceptación visual completa usar:
  #   bash tools/visual_playtest.sh          # local full matrix
  #   bash tools/visual_playtest.sh --smoke  # pad-only (~60s, same as CI)
  #
  # Real-framebuffer viewport capture: --headless uses the dummy renderer and produces
  # no pixels. Full visual acceptance:
  #   bash tools/visual_playtest.sh          # local full matrix
  #   bash tools/visual_playtest.sh --smoke  # pad-only (~60s, CI parity)
else
  echo "Skipping Godot smoke: set GODOT_BIN or install Godot at $DEFAULT_GODOT"
fi
