extends Node

## Proves the GDScript tree runs at all, under the standard (non-.NET) engine.
##
## It is the first thing ROADMAP 9's suggested order asks for and the first thing CI
## should export: if this cannot be built and served, nothing else about the port
## matters. Deliberately depends on nothing -- no assets, no scenes, no other script --
## so a failure here is the engine or the project file and never the port.

func _ready() -> void:
	print("web tree: GDScript running under ", Engine.get_version_info().string)
	print("web tree: renderer ", RenderingServer.get_video_adapter_name())
	print("web tree: viewport ", get_viewport().get_visible_rect().size)
	get_tree().quit(0)
