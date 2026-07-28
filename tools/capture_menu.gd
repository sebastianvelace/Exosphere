extends SceneTree


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var scene := load("res://scenes/ui/MainMenu.tscn") as PackedScene
	root.add_child(scene.instantiate())
	for _frame in range(45):
		await process_frame
	var modal := OS.get_environment("CAPTURE_MENU_MODAL").to_lower()
	if modal == "campaign":
		var campaign_button := _find_button(root, "HISTORICAL CAMPAIGN")
		if campaign_button == null:
			campaign_button = _find_button(root, "CAMPAÑA HISTÓRICA")
		if campaign_button == null:
			push_error("Could not find campaign button for modal capture")
			quit(1)
			return
		campaign_button.emit_signal("pressed")
	for _frame in range(45):
		await process_frame
	var image := root.get_viewport().get_texture().get_image()
	var output := OS.get_environment("CAPTURE_MENU_OUTPUT")
	if output.is_empty():
		output = "/tmp/exosphere_menu_1920x1080.png"
	var error := image.save_png(output)
	if error != OK:
		push_error("Could not save menu capture: %s" % error)
		quit(1)
		return
	print("MENU_CAPTURE path=%s width=%d height=%d" % [
		output, image.get_width(), image.get_height()
	])
	quit()


func _find_button(node: Node, expected_text: String) -> Button:
	if node is Button and (node as Button).text.to_upper() == expected_text:
		return node as Button
	for child in node.get_children():
		var match := _find_button(child, expected_text)
		if match != null:
			return match
	return null
