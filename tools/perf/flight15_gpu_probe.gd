extends SceneTree
## Native Vulkan witness for Flight 15. Uses an actual GPU and pauses the
## playable mission so every isolation arm renders the same flight state.

func _initialize() -> void:
	call_deferred("run")

func choose(node: Node, prefix: String) -> bool:
	if node is Button and node.text.begins_with(prefix):
		node.pressed.emit()
		return true
	for child in node.get_children():
		if choose(child, prefix): return true
	return false

func run() -> void:
	print("F15_GPU adapter=%s type=%s driver=%s method=%s" % [
		RenderingServer.get_video_adapter_name(), RenderingServer.get_video_adapter_type(),
		RenderingServer.get_current_rendering_driver_name(), RenderingServer.get_current_rendering_method()])
	if RenderingServer.get_video_adapter_type() not in [1, 2]:
		quit(3)
		return
	var menu := (load("res://scenes/ui/MainMenu.tscn") as PackedScene).instantiate()
	root.add_child(menu)
	current_scene = menu
	await process_frame
	if not choose(menu, "EXPLORE FLIGHT 15"):
		quit(2)
		return
	await process_frame
	if not choose(menu, "START ASCENT"):
		quit(2)
		return
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	var viewport := root.get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(viewport, true)
	var bridge: Node
	var reached_witness := false
	for i in range(1200):
		await process_frame
		if bridge == null:
			bridge = root.find_child("SimulationBridge", true, false)
		if bridge == null: continue
		var state: Dictionary = bridge.call("GetFlight14ExplorationSnapshot")
		if state.is_empty(): continue
		if state["profile_id"] != "starship-flight15-ascent" or state["blocked"] != "":
			quit(3)
			return
		if float(state["met"]) >= 10.0:
			reached_witness = true
			break
	if not reached_witness:
		print("F15_GPU_NO_ASCENT_WITNESS")
		quit(3)
		return
	if not choose(root, "PAUSE"):
		quit(5)
		return
	var state: Dictionary = bridge.call("GetFlight14ExplorationSnapshot")
	if not state["paused"]:
		print("F15_GPU_WITNESS_NOT_PAUSED")
		quit(5)
		return
	var world := root.find_child("WorldEnvironment", true, false) as WorldEnvironment
	var light := root.find_child("DirectionalLight3D", true, false) as DirectionalLight3D
	var ground := root.find_child("EarthGroundController", true, false) as Node3D
	var vessels := root.find_child("Vessels", true, false) as Node3D
	var deluge := root.find_child("LaunchEffectsController", true, false) as Node3D
	if world == null or light == null or ground == null or vessels == null or deluge == null:
		print("F15_GPU_MISSING_ISOLATION_NODES")
		quit(5)
		return
	var sky := world.environment.sky
	var fog := world.environment.volumetric_fog_enabled
	var shadow := light.shadow_enabled
	var visible := ground.visible
	var vessels_visible := vessels.visible
	var deluge_visible := deluge.visible
	var scale := root.scaling_3d_scale
	print("F15_AB_CONFIG met=%.3f size=%s sky=%s fog=%s shadow=%s ground=%s" % [
		state["met"], root.size, sky != null, fog, shadow, visible])
	for mode in ["baseline", "no_ground", "no_shadows", "no_sky_fog",
			"no_vehicle", "no_deluge", "scale_060", "baseline_repeat"]:
		ground.visible = visible and mode != "no_ground"
		vessels.visible = vessels_visible and mode != "no_vehicle"
		deluge.visible = deluge_visible and mode != "no_deluge"
		light.shadow_enabled = shadow and mode != "no_shadows"
		world.environment.sky = null if mode == "no_sky_fog" else sky
		world.environment.volumetric_fog_enabled = fog and mode != "no_sky_fog"
		root.scaling_3d_scale = 0.60 if mode == "scale_060" else scale
		for i in range(24): await RenderingServer.frame_post_draw
		for i in range(72):
			await RenderingServer.frame_post_draw
			print("F15_AB mode=%s gpu_ms=%.3f" % [
				mode, RenderingServer.viewport_get_measured_render_time_gpu(viewport)])
		var output_dir := OS.get_environment("EXOSPHERE_GPU_CAPTURE_DIR")
		if not output_dir.is_empty():
			var output := output_dir.path_join("flight15-%s.png" % mode)
			if root.get_texture().get_image().save_png(output) != OK:
				quit(4)
				return
	print("F15_GPU_AB_OK")
	quit()
