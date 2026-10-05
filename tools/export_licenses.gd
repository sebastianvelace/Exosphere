extends SceneTree

# Capture notices from the actual engine used for the export, not another release.
func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 1:
		push_error("Expected the exported folder path")
		quit(1)
		return
	var folder := args[0]
	var license_file := FileAccess.open(folder.path_join("GODOT-LICENSE.txt"), FileAccess.WRITE)
	var notices_file := FileAccess.open(folder.path_join("GODOT-THIRD-PARTY-NOTICES.json"), FileAccess.WRITE)
	if license_file == null or notices_file == null:
		push_error("Cannot write export notices")
		quit(1)
		return
	license_file.store_string(Engine.get_license_text())
	notices_file.store_string(JSON.stringify({
		"components": Engine.get_copyright_info(),
		"licenses": Engine.get_license_info(),
	}, "  "))
	license_file.close()
	notices_file.close()
	quit(0)
