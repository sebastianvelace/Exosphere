extends SceneTree

# Reproducible real-framebuffer VAB evidence. This is intentionally kept under tools/
# rather than the gameplay scene: it never becomes an autoload or a shipped runtime node.
# CAPTURE_VAB_SCENARIO: empty, starter, starship, selection.

func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var scene := load("res://scenes/construction/Construction.tscn") as PackedScene
	root.add_child(scene.instantiate())
	await _frames(90)

	var scenario := OS.get_environment("CAPTURE_VAB_SCENARIO").to_lower()
	if scenario == "starter" or scenario == "starship":
		var button_text := "STARTER" if scenario == "starter" else "STARSHIP"
		var button := _find_button(root, button_text)
		if button == null:
			push_error("Could not find VAB quick-build button: %s" % button_text)
			quit(1)
			return
		button.emit_signal("pressed")
		await _frames(120)
	elif scenario == "selection":
		var starship := _find_button(root, "STARSHIP")
		if starship == null:
			push_error("Could not find Starship quick-build button")
			quit(1)
			return
		starship.emit_signal("pressed")
		await _frames(120)
		var stack := root.find_child("StackList", true, false) as ItemList
		if stack == null or stack.item_count == 0:
			push_error("VAB stack did not populate for selection capture")
			quit(1)
			return
		stack.select(0)
		stack.emit_signal("item_selected", 0)
		await _frames(30)

	var image := root.get_viewport().get_texture().get_image()
	var output := OS.get_environment("CAPTURE_VAB_OUTPUT")
	if output.is_empty():
		output = "/tmp/exosphere_vab_%s.png" % scenario
	var error := image.save_png(output)
	if error != OK:
		push_error("Could not save VAB capture: %s" % error)
		quit(1)
		return
	print("VAB_CAPTURE scenario=%s path=%s width=%d height=%d" % [
		scenario, output, image.get_width(), image.get_height()
	])
	quit()


func _frames(count: int) -> void:
	for _frame in range(count):
		await process_frame


func _find_button(node: Node, expected_text: String) -> Button:
	if node is Button and (node as Button).text.to_upper() == expected_text:
		return node as Button
	for child in node.get_children():
		var match := _find_button(child, expected_text)
		if match != null:
			return match
	return null
