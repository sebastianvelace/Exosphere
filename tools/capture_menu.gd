extends SceneTree

var menu: Control
var initiating_action: Button

func _initialize() -> void:
	call_deferred("_capture")

func _capture() -> void:
	menu = (load("res://scenes/ui/MainMenu.tscn") as PackedScene).instantiate()
	root.add_child(menu)
	current_scene = menu
	for _frame in range(45):
		await process_frame
	var mode := OS.get_environment("CAPTURE_MENU_MODAL").to_lower()
	var labels := {
		"campaign": ["HISTORICAL CAMPAIGN", "CAMPAÑA HISTÓRICA"],
		"vehicles": ["FREE FLIGHT", "VUELO LIBRE"],
		"vehicle": ["FREE FLIGHT", "VUELO LIBRE"],
		"launch": ["FREE FLIGHT", "VUELO LIBRE"],
		"mission": ["HISTORICAL CAMPAIGN", "CAMPAÑA HISTÓRICA"],
		"missionlaunch": ["HISTORICAL CAMPAIGN", "CAMPAÑA HISTÓRICA"],
		"partial": ["HISTORICAL CAMPAIGN", "CAMPAÑA HISTÓRICA"],
		"vab": ["VEHICLE ASSEMBLY", "ENSAMBLAJE DE VEHÍCULOS"],
		"flight14": ["EXPLORE FLIGHT 14", "EXPLORAR VUELO 14"],
		"settings": ["SETTINGS", "AJUSTES"],
		"continue": ["CONTINUE", "CONTINUAR"],
	}
	if labels.has(mode):
		if not _press(labels[mode]): return
		await process_frame
		if mode in ["vehicle", "launch"]:
			var variant := OS.get_environment("CAPTURE_MENU_VEHICLE")
			if variant.is_empty(): variant = "starship-flight-12-v3-2026-05-22"
			var selected := menu.find_child(variant, true, false) as Button
			if selected == null:
				_fail("Vehicle missing: " + variant)
				return
			selected.emit_signal("pressed")
			await process_frame
		if mode in ["mission", "missionlaunch", "partial"]:
			if not _press(["05  APOLLO 11"] if mode == "partial" else ["01  FREEDOM"]): return
			await process_frame
		if mode in ["launch", "missionlaunch"]:
			var start_labels := ["START MISSION", "INICIAR MISIÓN"] if mode == "missionlaunch" else ["LAUNCH VEHICLE", "LANZAR VEHÍCULO"]
			if not _press(start_labels): return
			for _frame in range(90): await process_frame
			if not root.has_node("Flight"):
				_fail("Selected vehicle did not open Flight")
				return
			print("MENU_LAUNCH_OK selected_vehicle=" + OS.get_environment("CAPTURE_MENU_VEHICLE"))
	for _frame in range(45): await process_frame
	if mode not in ["launch", "missionlaunch", "vab"]:
		if not _validate_layout(mode): return
		if mode == "flight14":
			var launch := _find_button(menu, ["LAUNCH FLIGHT 14", "INICIAR VUELO 14"])
			if launch == null or not launch.disabled:
				_fail("Unimplemented Flight 14 exposed as playable")
				return
		if mode == "continue":
			if _find_button(menu.get_node("MenuModal"), ["ALPHA"]) == null or _find_button(menu.get_node("MenuModal"), ["ZULU"]) == null:
				_fail("Continue did not offer both save slots")
				return
			print("MENU_CONTINUE_CHOICE_OK")
		if mode == "vehicles":
			var count := 0
			for button in _buttons(menu):
				if button.name.to_lower().contains("202") or button.name.to_lower().contains("196"):
					count += 1
			if count != 10:
				_fail("Expected 10 launch vehicles; got %d" % count)
				return
			print("MENU_CATALOG_OK launchers=10")
	if mode == "vab":
		if current_scene == null or current_scene.scene_file_path != "res://scenes/construction/Construction.tscn":
			_fail("Assembly action did not open Construction")
			return
		print("MENU_VAB_OK")
	var output := OS.get_environment("CAPTURE_MENU_OUTPUT")
	if output.is_empty(): output = "/tmp/exosphere_menu.png"
	var error := root.get_viewport().get_texture().get_image().save_png(output)
	if error != OK:
		_fail("Could not save menu capture: " + output)
		return
	print("MENU_CAPTURE path=%s width=%d height=%d mode=%s" % [output, root.size.x, root.size.y, mode])
	if mode.is_empty():
		for button in _buttons(menu):
			if button.disabled: continue
			var ancestor := button.get_parent()
			var home_scroll: ScrollContainer
			while ancestor != null:
				if ancestor is ScrollContainer: home_scroll = ancestor
				ancestor = ancestor.get_parent()
			if home_scroll == null: continue
			button.grab_focus()
			for _frame in range(4): await process_frame
			if root.gui_get_focus_owner() != button or not home_scroll.get_global_rect().grow(1).encloses(button.get_global_rect()):
				_fail("Keyboard cannot reveal action: " + button.text)
				return
		print("MENU_HOME_FOCUS_OK")
	if mode not in ["launch", "missionlaunch", "vab"] and menu.has_node("MenuModal"):
		var escape := InputEventKey.new()
		escape.keycode = KEY_ESCAPE
		escape.pressed = true
		Input.parse_input_event(escape)
		for _frame in range(3): await process_frame
		escape.pressed = false
		Input.parse_input_event(escape)
		if menu.has_node("MenuModal") or root.gui_get_focus_owner() != initiating_action:
			_fail("Escape did not close dialog and restore initiating focus")
			return
		print("MENU_BACK_OK")
	quit()

func _validate_layout(mode: String) -> bool:
	var viewport_rect := Rect2(Vector2.ZERO, root.get_visible_rect().size)
	var modal := menu.get_node_or_null("MenuModal") as Control
	if not mode.is_empty() and modal == null:
		_fail("Menu route failed: " + mode)
		return false
	for button in _buttons(menu):
		if not button.is_visible_in_tree(): continue
		if modal != null and not modal.is_ancestor_of(button):
			if button.focus_mode != Control.FOCUS_NONE:
				_fail("Background retained keyboard focus under modal")
				return false
			continue
		# Scrolled content can extend below the viewport; its enclosing scroll must fit.
		var ancestor := button.get_parent()
		var scroll: ScrollContainer
		while ancestor != null:
			if ancestor is ScrollContainer: scroll = ancestor
			ancestor = ancestor.get_parent()
		var bounds := scroll.get_global_rect() if scroll != null else button.get_global_rect()
		if not viewport_rect.grow(1).encloses(bounds):
			_fail("Control exceeds viewport: " + str(button.get_path()) + " " + str(bounds))
			return false
	if modal != null:
		# Closing must return focus to the initiating action, and reopen cleanly.
		var close := _find_button(modal, ["CLOSE", "CERRAR"])
		if close == null:
			_fail("Missing accessible close action")
			return false
	print("MENU_LAYOUT_OK logical_viewport=%s mode=%s" % [viewport_rect.size, mode])
	return true

func _press(labels: Array) -> bool:
	var button := _find_button(menu, labels)
	if button == null or button.disabled:
		_fail("Missing usable menu action: " + str(labels))
		return false
	button.grab_focus()
	if not menu.has_node("MenuModal"): initiating_action = button
	button.emit_signal("pressed")
	return true

func _buttons(node: Node) -> Array[Button]:
	var result: Array[Button] = []
	if node is Button: result.append(node as Button)
	for child in node.get_children(): result.append_array(_buttons(child))
	return result

func _find_button(node: Node, labels: Array) -> Button:
	for button in _buttons(node):
		for label in labels:
			if button.text.to_upper().begins_with(label): return button
	return null

func _fail(message: String) -> void:
	push_error(message)
	quit(1)
