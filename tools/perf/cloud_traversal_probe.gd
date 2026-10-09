extends "res://tools/capture_menu.gd"

# Launch through the real menu, fly into the cloud shell, then compare transport
# paths at the SAME paused physical epoch. Timing includes no PNG readback.
func _explore_flight14() -> void:
	var bridge := root.find_child("SimulationBridge", true, false)
	if bridge == null: _fail("Flight 14 bridge missing"); return
	var viewport_rid := root.get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(viewport_rid, true)
	print("CLOUD_GPU adapter=" + RenderingServer.get_video_adapter_name() + " method=" + RenderingServer.get_current_rendering_method())
	bridge.call("SetWarpIndex", 3)
	var rows: Array = []
	var begin := Time.get_ticks_usec()
	while true:
		await process_frame
		var state: Dictionary = bridge.call("GetFlight14ExplorationSnapshot")
		rows.append(_cloud_sample("ascent", state, viewport_rid, begin))
		begin = Time.get_ticks_usec()
		if state["blocked"] != "": _fail(str(state)); return
		if state["altitude"] >= 1750:
			_find_button(root.get_node("Flight"), ["PAUSE"]).emit_signal("pressed")
			break
	var cache := root.find_child("CloudDensityCache", true, false)
	var occlusion := root.find_child("VesselCloudOcclusion", true, false)
	if cache == null or occlusion == null: _fail("Cloud transport nodes missing"); return
	var output := OS.get_environment("CLOUD_PROFILE_OUT")
	if output.is_empty(): output = "/tmp/exosphere-cloud-profile"
	var paused: Dictionary = bridge.call("GetFlight14ExplorationSnapshot")
	for mode in ["cached", "procedural", "cached-repeat", "hull-off"]:
		cache.set("PresentationEnabled", mode != "procedural")
		occlusion.set("PresentationEnabled", mode != "hull-off")
		for warmup in range(40): await process_frame
		begin = Time.get_ticks_usec()
		for frame in range(40):
			await process_frame
			var state: Dictionary = bridge.call("GetFlight14ExplorationSnapshot")
			if state["met"] != paused["met"] or state["mass"] != paused["mass"]:
				_fail("A/B advanced physics"); return
			rows.append(_cloud_sample(mode, state, viewport_rid, begin))
			begin = Time.get_ticks_usec()
		var material: ShaderMaterial = occlusion.get("CloudMaterial")
		if bool(material.get_shader_parameter("cloud_density_cache_enabled")) != (mode != "procedural"):
			_fail("A/B did not switch actual density transport"); return
		if root.get_texture().get_image().save_png(output + "-" + mode + ".png") != OK:
			_fail("Cloud capture failed"); return
		print("CLOUD_PROFILE_CASE mode=" + mode + " state=" + JSON.stringify(paused))
	var file := FileAccess.open(output + ".json", FileAccess.WRITE)
	if file == null: _fail("Cloud probe output unavailable"); return
	file.store_string(JSON.stringify(rows))
	print("CLOUD_PROFILE_OK frames=" + str(rows.size()) + " physics_unchanged=true")
	quit(0)

func _cloud_sample(mode: String, state: Dictionary, viewport_rid: RID, begin: int) -> Array:
	var occ := root.find_child("VesselCloudOcclusion", true, false)
	return [mode, state["altitude"], (Time.get_ticks_usec() - begin) / 1000.0,
		Performance.get_monitor(Performance.TIME_PROCESS) * 1000,
		Performance.get_monitor(Performance.TIME_PHYSICS_PROCESS) * 1000,
		RenderingServer.viewport_get_measured_render_time_cpu(viewport_rid),
		RenderingServer.viewport_get_measured_render_time_gpu(viewport_rid),
		Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
		occ.get("IsAttached") if occ != null else false]
