class_name SaveGame
extends RefCounted

## Reading and writing the one save file. Nothing here knows what a die is — it takes a
## dictionary, puts it on disk, and hands it back.
##
## Hand port of scripts/SaveGame.cs (ROADMAP 9a), and the *easiest* file in the port by
## some distance: the C# version was already written in Godot's own `Json` over
## `Godot.Collections.Dictionary` rather than `System.Text.Json`, precisely so this half
## would survive the crossing untouched. It did — this is a transliteration.
##
## **`user://`, deliberately, because of the browser.** Godot maps that path to IndexedDB
## in a web export, so the same `FileAccess` calls that write a file on a desktop write to
## browser storage in a tab, with no branch and no JavaScript. Anything that reached for
## `localStorage` through `JavaScriptBridge` would work in the browser and nowhere else.

const PATH := "user://board.json"

## The schema this build writes. A file from the future is left alone rather than
## half-read: a save that loads wrong is worse than one that does not load.
const VERSION := 1


## Whether there is anything to restore. Whether the game *should* restore it is
## GameManager.persist_board's business, not this class's.
static func exists() -> bool:
	return FileAccess.file_exists(PATH)


## The saved state, or null when there is none, it cannot be read, or it was written by a
## newer build.
##
## Never throws. A corrupt save is a thing a player can end up with and it must cost them
## their board, not the game.
static func load_board():
	if not FileAccess.file_exists(PATH):
		return null

	var file := FileAccess.open(PATH, FileAccess.READ)
	if file == null:
		push_warning("save exists but will not open: %s" % FileAccess.get_open_error())
		return null

	var parsed = JSON.parse_string(file.get_as_text())
	file.close()
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("save is not a JSON object; ignoring it")
		return null

	var version: int = parsed.get("version", 0)
	if version > VERSION:
		push_warning("save is version %d, this build reads %d" % [version, VERSION])
		return null
	return parsed


static func store(data: Dictionary) -> void:
	data["version"] = VERSION

	var file := FileAccess.open(PATH, FileAccess.WRITE)
	if file == null:
		push_warning("cannot write the save: %s" % FileAccess.get_open_error())
		return
	file.store_string(JSON.stringify(data, "  "))
	# The close is what matters on the web: that is when Godot flushes the file into
	# IndexedDB. C# had a `using` block do this; here it is explicit.
	file.close()


static func delete() -> void:
	if FileAccess.file_exists(PATH):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(PATH))
