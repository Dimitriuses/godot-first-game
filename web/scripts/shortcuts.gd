class_name Shortcuts
extends RefCounted

## Which key was that, whatever the keyboard is set to.
##
## Hand port of scripts/Shortcuts.cs (ROADMAP 9a). The C# tree is canonical.
##
## **`keycode` is the letter the layout produces; `physical_keycode` is the key that was
## pressed.** On a Latin layout they agree, which is why every shortcut in this project
## worked for a year by reading `keycode` alone. On a Cyrillic layout they do not: the key
## engraved R produces К, so `keycode` is that and the R shortcut never fires. This tree is
## where it surfaced — a browser reports the layout faithfully — but it was never a web
## bug, and the desktop build had it too.
##
## Matching either is Godot's own advice for gameplay keys, and it costs nothing: no
## layout produces a Latin letter from a *different* physical key, so the two cannot
## disagree in a way that fires the wrong action.


static func is_key(key: InputEventKey, code: Key) -> bool:
	return key != null and (key.keycode == code or key.physical_keycode == code)


## A press, not a repeat — the shape every shortcut here wants.
static func pressed(event: InputEvent, code: Key) -> bool:
	if not (event is InputEventKey):
		return false
	var key := event as InputEventKey
	return key.pressed and not key.echo and is_key(key, code)
