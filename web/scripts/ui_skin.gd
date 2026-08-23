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
