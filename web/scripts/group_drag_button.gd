class_name GroupDragButton
extends Control

## Turns "drag them all at once" on and off — what holding Shift does, made permanent.
##
## Hand port of scripts/GroupDragButton.cs (ROADMAP 9a). The C# tree is canonical.
##
## A touchscreen has no modifier keys, so the Shift group drag was unreachable there: the
## gesture exists but there is no way to ask for it. This is that way. It is offered on
## the desktop too rather than hidden behind a touch check, because a laptop with a
## touchscreen is both, and a control that appears on some machines and not others is
## harder to explain than one that is always there.

## Raised when the player toggles it. The board owns the setting; this only asks.
signal group_toggled(value: bool)

var _button: Button
var _icon: Control
var _on := false


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_button = UiSkin.corner_button(self, "GroupDrag", 1)
	_button.pressed.connect(_on_pressed)

	_icon = Control.new()
	_icon.name = "Icon"
	_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_icon.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon.draw.connect(_draw_icon)
	_button.add_child(_icon)
	_refresh()


func _on_pressed() -> void:
	_on = not _on
	Sfx.play("ui_click")
	group_toggled.emit(_on)
	_refresh()


## Set from outside — the saved state is read after this button has been built, the same
## way the mute button is.
func set_on(value: bool) -> void:
	_on = value
	_refresh()


func is_on() -> bool:
	return _on


func _refresh() -> void:
	_button.tooltip_text = "Dragging moves every die — click to move one at a time" \
		if _on else "Dragging moves one die — click to move them all together"
	if _icon != null:
		_icon.queue_redraw()


## Four small dice in a square. All four are lit when the drag takes everything, and only
## one when it takes one — so the icon says which mode it is *in*, not which mode the
## button would switch to.
func _draw_icon() -> void:
	var frame := UiSkin.icon_frame(_icon)
	var unit: float = frame[0]
	var origin: Vector2 = frame[1]
	var lit := Color("e6e8f2")
	var dim := Color("6b7387")

	for i in 4:
		var x := 5.0 + (i % 2) * 9.0
		var y := 5.0 + (i / 2) * 9.0
		# Bottom-left is the single die: the one that moves when the mode is off.
		var active := _on or i == 2
		_icon.draw_rect(
			Rect2(origin + Vector2(x, y) * unit, Vector2(7.0, 7.0) * unit),
			lit if active else dim, true)
