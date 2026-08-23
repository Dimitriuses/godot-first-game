class_name MuteButton
extends Control

## Turns the sound off and on, from the top-right corner.
##
## Hand port of scripts/MuteButton.cs (ROADMAP 9a). The C# tree is canonical.
##
## The icon is drawn rather than written. A speaker is an emoji, and Godot's default font
## has no emoji in it — a glyph it cannot render comes out as a blank box, which is a
## worse button than no button. Everything here is two polygons and a few strokes, so it
## renders the same wherever it runs and scales with the button rather than with a font.
## That matters more in this tree than in the C# one: a browser build runs on whatever
## fonts the machine has.

## The key that does the same thing. Handled here rather than in GameManager for the same
## reason the palette handles Tab: it belongs to this control and nothing else needs to
## know about it.
const MUTE_KEY := KEY_M

var _button: Button
var _icon: Control


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	_button = UiSkin.corner_button(self, "Mute", 0)
	_button.pressed.connect(_toggle)

	_icon = Control.new()
	_icon.name = "Icon"
	_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_icon.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon.draw.connect(_draw_icon)
	_button.add_child(_icon)

	refresh()


func _input(event: InputEvent) -> void:
	if Shortcuts.pressed(event, MUTE_KEY):
		_toggle()
		get_viewport().set_input_as_handled()


func _toggle() -> void:
	Sfx.set_muted(not Sfx.muted())
	# After the change, so unmuting is audible and muting is not — which is the only
	# confirmation this button can give.
	Sfx.play("ui_click")
	refresh()


## Public because the button is built before the save is read: a muted save would
## otherwise leave the speaker drawn as though the sound were on.
func refresh() -> void:
	var key := OS.get_keycode_string(MUTE_KEY)
	_button.tooltip_text = "Sound off — click or press %s to turn it on" % key \
		if Sfx.muted() else "Sound on — click or press %s to turn it off" % key
	_icon.queue_redraw()


## A speaker, and either two waves coming out of it or a cross where they were.
##
## Laid out in a 24-unit square and scaled to whatever the button is, so the drawing never
## has to know the button's size.
func _draw_icon() -> void:
	var is_muted := Sfx.muted()
	var tint := Color("8b93a8") if is_muted else Color("e6e8f2")
	var frame := UiSkin.icon_frame(_icon)
	var unit: float = frame[0]
	var origin: Vector2 = frame[1]

	# The block, and the cone opening to the right of it.
	_icon.draw_rect(Rect2(_p(origin, unit, 5.0, 9.5),
		Vector2(4.0 * unit, 5.0 * unit)), tint)
	_icon.draw_colored_polygon(PackedVector2Array([
		_p(origin, unit, 9.0, 9.5), _p(origin, unit, 14.0, 5.0),
		_p(origin, unit, 14.0, 19.0), _p(origin, unit, 9.0, 14.5)]), tint)

	if is_muted:
		var w := 2.0 * unit
		_icon.draw_line(_p(origin, unit, 16.5, 8.0), _p(origin, unit, 21.5, 16.0), tint, w)
		_icon.draw_line(_p(origin, unit, 21.5, 8.0), _p(origin, unit, 16.5, 16.0), tint, w)
		return

	# Two arcs, opening rightward from the mouth of the cone.
	for i in range(1, 3):
		_icon.draw_arc(_p(origin, unit, 14.0, 12.0), i * 3.4 * unit,
			deg_to_rad(-52.0), deg_to_rad(52.0), 20, tint, 1.8 * unit, true)


## The C# version had a local function for this; GDScript has no nested funcs, so the
## frame is passed in rather than captured.
func _p(origin: Vector2, unit: float, x: float, y: float) -> Vector2:
	return origin + Vector2(x, y) * unit
