extends SceneTree


func _initialize() -> void:
	call_deferred("_capture")


func _capture() -> void:
	var scene := load("res://scenes/ui/MainMenu.tscn") as PackedScene
	root.add_child(scene.instantiate())
	for _frame in range(90):
		await process_frame
	var image := root.get_viewport().get_texture().get_image()
	var output := "/tmp/exosphere_menu_1920x1080.png"
	var error := image.save_png(output)
	if error != OK:
		push_error("Could not save menu capture: %s" % error)
		quit(1)
		return
	print("MENU_CAPTURE path=%s width=%d height=%d" % [
		output, image.get_width(), image.get_height()
	])
	quit()
