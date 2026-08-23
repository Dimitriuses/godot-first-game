class_name UiSkin
extends RefCounted

## The look and the placement of the small square buttons in the top-right corner, in one
## place so a second one cannot drift away from the first.
##
## Hand port of scripts/UiSkin.cs (ROADMAP 9a). The C# tree is canonical.
##
## Their icons are drawn rather than written, because Godot's default font has no emoji
## and a glyph it cannot render comes out as a blank box. Each button lays its drawing out
## in a 24-unit square and scales it, so nothing has to know the button's size.

const CORNER_BUTTON_SIZE := 40.0
const CORNER_MARGIN := 8.0
const CORNER_GAP := 6.0


## Slot 0 is the rightmost. Offsets are from the right edge, so they are negative.
static func corner_left(slot: int) -> float:
	return -(CORNER_MARGIN + (slot + 1) * CORNER_BUTTON_SIZE + slot * CORNER_GAP)


static func corner_right(slot: int) -> float:
	return -(CORNER_MARGIN + slot * (CORNER_BUTTON_SIZE + CORNER_GAP))


static func corner_top() -> float:
	return CORNER_MARGIN


static func corner_bottom() -> float:
	return CORNER_MARGIN + CORNER_BUTTON_SIZE


static func panel(bg: String, border: String) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(bg)
	style.border_color = Color(border)
	style.set_border_width_all(2)
	style.set_corner_radius_all(9)
	return style


## A corner button, styled and placed. The caller adds whatever it draws inside.
static func corner_button(parent: Control, name: String, slot: int) -> Button:
	var button := Button.new()
	button.name = name
	button.focus_mode = Control.FOCUS_NONE
	button.mouse_filter = Control.MOUSE_FILTER_STOP
	button.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	button.offset_left = corner_left(slot)
	button.offset_right = corner_right(slot)
	button.offset_top = corner_top()
	button.offset_bottom = corner_bottom()
	button.add_theme_stylebox_override("normal", panel("202536e8", "77819b"))
	button.add_theme_stylebox_override("hover", panel("2d3450f0", "aab3c8"))
	button.add_theme_stylebox_override("pressed", panel("171b28f0", "77819b"))
	parent.add_child(button)
	return button


## The transform every corner icon draws through: a 24-unit square, centred. Returned as
## [unit, origin] because GDScript has no tuples.
static func icon_frame(icon: Control) -> Array:
	var unit := icon.size.x / 24.0
	return [unit, Vector2(0.0, (icon.size.y - 24.0 * unit) / 2.0)]


## A drawing child filling its parent, for a button whose picture is drawn rather than
## typed.
##
## **Every glyph is a font dependency, and this is the tree where that bites.** The note
## elsewhere that `◀ ▶ ×` were safe was true of the desktop font and wrong of the browser:
## the first GitHub Pages deploy drew the palette's toggle as a blank box. Godot's
## fallback font in a web export carries a much smaller set than the desktop one, so
## anything past ASCII is a gamble that is not worth taking twice.
##
## `draw` is called with the icon Control; it is bound rather than closed over because a
## GDScript lambda captures by value at connect time.
static func icon_child(parent: Control, name: String, draw: Callable) -> Control:
	var icon := Control.new()
	icon.name = name
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon.set_anchors_preset(Control.PRESET_FULL_RECT)
	icon.draw.connect(draw.bind(icon))
	parent.add_child(icon)
	return icon


static func _p(origin: Vector2, unit: float, x: float, y: float) -> Vector2:
	return origin + Vector2(x, y) * unit


## A solid triangle pointing left or right — the palette's open/close arrow.
static func draw_chevron(icon: Control, points_right: bool, tint: Color) -> void:
	var frame := icon_frame(icon)
	var unit: float = frame[0]
	var o: Vector2 = frame[1]
	var points := PackedVector2Array([_p(o, unit, 9, 5), _p(o, unit, 16, 12),
		_p(o, unit, 9, 19)]) if points_right \
		else PackedVector2Array([_p(o, unit, 15, 5), _p(o, unit, 8, 12),
			_p(o, unit, 15, 19)])
	icon.draw_colored_polygon(points, tint)


## A cross — the die list's delete button.
static func draw_cross(icon: Control, tint: Color) -> void:
	var frame := icon_frame(icon)
	var unit: float = frame[0]
	var o: Vector2 = frame[1]
	var w := 2.2 * unit
	icon.draw_line(_p(o, unit, 7, 7), _p(o, unit, 17, 17), tint, w, true)
	icon.draw_line(_p(o, unit, 17, 7), _p(o, unit, 7, 17), tint, w, true)


## Four corner brackets, opening outward to go full screen and inward to come back.
static func draw_fullscreen(icon: Control, exit: bool, tint: Color) -> void:
	var frame := icon_frame(icon)
	var unit: float = frame[0]
	var o: Vector2 = frame[1]
	var w := 2.0 * unit
	# One bracket, mirrored into all four corners. `exit` flips which way the arms point,
	# so the button says what it will do rather than what state it is in.
	for c in [[5.0, 5.0, 1.0, 1.0], [19.0, 5.0, -1.0, 1.0],
			[5.0, 19.0, 1.0, -1.0], [19.0, 19.0, -1.0, -1.0]]:
		var arm := -4.5 if exit else 4.5
		var corner := _p(o, unit,
			c[0] - (c[2] * 4.5 if exit else 0.0),
			c[1] - (c[3] * 4.5 if exit else 0.0))
		icon.draw_line(corner, corner + Vector2(c[2] * arm, 0.0) * unit, tint, w)
		icon.draw_line(corner, corner + Vector2(0.0, c[3] * arm) * unit, tint, w)
