class_name FullscreenButton
extends Control

## Goes full screen and comes back, from the top-right corner.
##
## Hand port of scripts/FullscreenButton.cs (ROADMAP 9a). The C# tree is canonical.
##
## **It exists for this tree.** A desktop build can be maximised by its window manager and
## a phone app is already full screen, but a game in a page is a canvas among page
## furniture, and the Fullscreen API is the only way out of that. The button is the *only*
## way to ask: browsers grant fullscreen only from inside a user gesture, so a call made
## at startup, from a timer, or from anything but a real click is refused. That is why
## this is a control and not a setting.

## The key that does the same thing. F is the near-universal convention, and unlike the
## button it is reachable while the pointer is over the board.
const FULLSCREEN_KEY := KEY_F

var _button: Button
var _icon: Control
var _was_fullscreen := false


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	_button = UiSkin.corner_button(self, "Fullscreen", 2)
	_button.pressed.connect(_toggle)

	_icon = UiSkin.icon_child(_button, "Icon", _draw_icon)

	refresh()


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == FULLSCREEN_KEY and event.pressed \
			and not event.echo:
		_toggle()
		get_viewport().set_input_as_handled()


func _draw_icon(icon: Control) -> void:
	UiSkin.draw_fullscreen(icon, _is_fullscreen(), Color("e6e8f2"))


## Whether the window is in one of the two full-screen modes.
##
## Asked of the display server every time rather than tracked, because the player can
## leave full screen without touching this button — Escape does it in every browser, and
## nothing tells the game when it happens.
static func _is_fullscreen() -> bool:
	var mode := DisplayServer.window_get_mode()
	return mode == DisplayServer.WINDOW_MODE_FULLSCREEN \
		or mode == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN


func _toggle() -> void:
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED \
		if _is_fullscreen() else DisplayServer.WINDOW_MODE_FULLSCREEN)
	Sfx.play("ui_click")
	refresh()


## Public because the state can change behind the game's back: Escape leaves full screen
## in every browser and reports nothing, so _process polls.
func refresh() -> void:
	var key := OS.get_keycode_string(FULLSCREEN_KEY)
	_button.tooltip_text = "Leave full screen (press %s)" % key if _is_fullscreen() \
		else "Go full screen (press %s)" % key
	if _icon != null:
		_icon.queue_redraw()


## Cheap — one enum read a frame — and the only way to notice the player pressing Escape,
## which no signal reports.
func _process(_delta: float) -> void:
	if _is_fullscreen() == _was_fullscreen:
		return
	_was_fullscreen = _is_fullscreen()
	refresh()
