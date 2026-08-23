class_name DicePalette
extends Control

## The drawer of die types down the left edge, and the drag that puts one on the board.
##
## Hand port of scripts/DicePalette.cs (ROADMAP 9a). The C# tree is canonical.

## `theme` is the slot's own colour scheme, set by right-clicking it. It travels with the
## spawn rather than being looked up afterwards, because by the time the die exists the
## drag that asked for it is over.
signal spawn_requested(scene_path: String, screen_position: Vector2, theme: int)

## The pack, as paths and icon regions — never as loaded scenes. One Dictionary per die,
## with "scene", "name" and "icon"; C# used a tuple, which GDScript has not got.
##
## The palette used to build each button by instantiating the die's scene and taking a
## frame off it. That loads the die's entire sheet set: 727 MB of texture memory for the
## eight of them, before anything had been thrown. It now draws from one 50 KB icon sheet
## and touches no die scene at all.
var pack: Array = []

const ICON_SHEET := "res://assets/dice/icons.png"

## Four across and two down holds the whole pack without scrolling, which is what sets the
## width: four buttons, three gaps between them and a margin either side.
const COLUMNS := 4
const BUTTON_SIZE := 50.0
const BUTTON_GAP := 5.0
const DRAWER_MARGIN := 12.0
## Public because the die list matches it: the two panels stack into one column down the
## left edge, and a column whose halves are different widths reads as a mistake.
const DRAWER_WIDTH := COLUMNS * BUTTON_SIZE + (COLUMNS - 1) * BUTTON_GAP \
	+ DRAWER_MARGIN * 2

## How far in from the left edge the column sits. Shared with the die list below it.
const DRAWER_LEFT := 16.0

## Blank pixels left around the die inside its button.
const ICON_PADDING := 3

## How far below the top edge the drawer floats.
const DRAWER_TOP := 8.0

## How big a die riding the cursor is drawn. Smaller than the 96 it used to be: it is a
## label saying what is coming, not a preview of its final size.
const GHOST_SIZE := 56.0

const GHOST_GAP := 12.0

var _drawer: PanelContainer
var _toggle_button: Button
var _drag_preview: TextureRect
var _name_tag: PanelContainer
var _name_tag_label: Label
# One entry per die in the pack, in the order they sit in the grid.
var _slot_buttons: Array[Button] = []
var _slot_icons: Array[TextureRect] = []
var _slot_themes: Array[int] = []
var _slot_names: Array[String] = []

var _dragging_scene := ""
var _dragging_icon: Texture2D = null
var _dragging_slot := -1
var _is_open := false

var _is_dragging_icon := false
var _drawer_tween: Tween = null


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build_interface()
	set_drawer_open(false, false)


func _process(_delta: float) -> void:
	# The one honest use of the live cursor: a ghost that follows the pointer has to read
	# the pointer. The *drop* below uses the event's own position instead.
	if _is_dragging_icon and _drag_preview != null:
		_drag_preview.position = get_viewport().get_mouse_position() \
			- _drag_preview.size / 2.0


func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.keycode == KEY_TAB and event.pressed \
			and not event.echo:
		set_drawer_open(not _is_open, true)
		get_viewport().set_input_as_handled()
		return

	if not _is_dragging_icon or not (event is InputEventMouseButton):
		return
	var mouse_button := event as InputEventMouseButton
	if mouse_button.button_index != MOUSE_BUTTON_LEFT or mouse_button.pressed:
		return

	# The release's own position, not the current cursor. They are the same thing in the
	# game and not in a harness — Input.warp_mouse does nothing headless — and the drop
	# should mean where the button came up, which is what the event carries.
	var mouse := mouse_button.position
	_is_dragging_icon = false
	if _drag_preview != null:
		_drag_preview.queue_free()
	_drag_preview = null

	# Ask what is under the pointer, rather than assuming the drawer's width is a no-go
	# strip. It was: every drop left of x=255 was refused at any height, which is a fifth
	# of the board's width, whether or not a panel was actually there and whether or not
	# the die list below it was even open. This covers the drawer, the list and the corner
	# buttons at once, and only where they really are.
	if get_viewport().gui_get_hovered_control() == null and not _dragging_scene.is_empty():
		spawn_requested.emit(_dragging_scene, mouse,
			_slot_themes[_dragging_slot] if _dragging_slot >= 0 else DiceTheme.BONE)
	_dragging_scene = ""
	_dragging_slot = -1
	get_viewport().set_input_as_handled()


func _build_interface() -> void:
	# Only as tall as it needs to be. Anchored full height it was nine tenths empty once
	# the dice fitted on two rows, which reads as something failing to load.
	_drawer = PanelContainer.new()
	_drawer.name = "Drawer"
	_drawer.mouse_filter = Control.MOUSE_FILTER_STOP
	_drawer.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_drawer.offset_top = DRAWER_TOP
	add_child(_drawer)

	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = Color("202536e8")
	panel_style.border_color = Color("77819b")
	panel_style.set_border_width_all(2)
	panel_style.set_corner_radius_all(10)
	_drawer.add_theme_stylebox_override("panel", panel_style)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", int(DRAWER_MARGIN))
	margin.add_theme_constant_override("margin_top", 14)
	margin.add_theme_constant_override("margin_right", int(DRAWER_MARGIN))
	margin.add_theme_constant_override("margin_bottom", 14)
	_drawer.add_child(margin)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 10)
	margin.add_child(content)

	var title := Label.new()
	title.text = "DICE"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 20)
	content.add_child(title)

	# A grid rather than a scrolling column. Eight dice down one side left half the pack
	# behind a scrollbar nobody thinks to drag.
	var grid := GridContainer.new()
	grid.columns = COLUMNS
	grid.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	grid.add_theme_constant_override("h_separation", int(BUTTON_GAP))
	grid.add_theme_constant_override("v_separation", int(BUTTON_GAP))
	content.add_child(grid)

	var sheet: Texture2D = load(ICON_SHEET)
	if sheet == null:
		push_error("no icon sheet at %s; run tools/dice-render/make_icons.py" % ICON_SHEET)
	for entry in pack:
		# Already cropped and centred by the generator, so no crop_to_die here.
		var icon: Texture2D = null
		if sheet != null:
			var atlas := AtlasTexture.new()
			atlas.atlas = sheet
			atlas.region = entry["icon"]
			icon = atlas
		_add_die_option(grid, entry["name"], icon, entry["scene"])

	# Both gestures in one label at one size. As two labels they cost a third line, and
	# the drawer grew until it was all but touching the die list below it.
	var hint := Label.new()
	hint.text = "Drag a die onto the board\nRight-click for a theme"
	hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	hint.modulate = Color(0.74, 0.78, 0.88)
	hint.add_theme_font_size_override("font_size", 12)
	content.add_child(hint)

	_toggle_button = Button.new()
	_toggle_button.name = "Toggle"
	_toggle_button.text = "▶"
	_toggle_button.tooltip_text = "Open dice menu"
	_toggle_button.mouse_filter = Control.MOUSE_FILTER_STOP
	_toggle_button.focus_mode = Control.FOCUS_NONE
	_toggle_button.set_anchors_preset(Control.PRESET_TOP_LEFT)
	_toggle_button.pressed.connect(_on_toggle_pressed)
	add_child(_toggle_button)

	# Added last so it draws over the drawer, and ignores the mouse so pointing at a
	# button cannot put the tag between the pointer and the button it describes.
	_name_tag = PanelContainer.new()
	_name_tag.name = "NameTag"
	_name_tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_name_tag.visible = false
	var tag_style := StyleBoxFlat.new()
	tag_style.bg_color = Color("202536f0")
	tag_style.border_color = Color("77819b")
	tag_style.content_margin_left = 9
	tag_style.content_margin_right = 9
	tag_style.content_margin_top = 4
	tag_style.content_margin_bottom = 4
	tag_style.set_border_width_all(2)
	tag_style.set_corner_radius_all(8)
	_name_tag.add_theme_stylebox_override("panel", tag_style)
	add_child(_name_tag)

	_name_tag_label = Label.new()
	_name_tag_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_name_tag_label.add_theme_font_size_override("font_size", 16)
	_name_tag.add_child(_name_tag_label)

	_drawer.offset_bottom = DRAWER_TOP + _drawer.get_combined_minimum_size().y


func _on_toggle_pressed() -> void:
	set_drawer_open(not _is_open, true)


func _add_die_option(list: Container, label: String, icon: Texture2D,
		scene: String) -> void:
	var slot := _slot_buttons.size()

	# No caption on the button. Eight of them will not fit beside the icons at this size,
	# and the die itself says which it is — except for the two pairs that share a shape,
	# which is what the hover name is for.
	var button := Button.new()
	button.name = "DieSlot%d" % slot
	button.custom_minimum_size = Vector2(BUTTON_SIZE, BUTTON_SIZE)
	button.focus_mode = Control.FOCUS_NONE
	# bind() appends, so the handler takes the event first and then these.
	button.gui_input.connect(_on_die_button_input.bind(scene, icon, slot))
	button.mouse_entered.connect(_show_name_tag.bind(label, button))
	button.mouse_exited.connect(_hide_name_tag)
	list.add_child(button)

	# The die goes in a child rather than in the button's own icon, because a themed slot
	# needs a material and a material on the Button would recolour its background along
	# with the die.
	var art := TextureRect.new()
	art.name = "Art"
	art.texture = icon
	art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	art.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	art.mouse_filter = Control.MOUSE_FILTER_IGNORE
	art.set_anchors_preset(Control.PRESET_FULL_RECT)
	art.offset_left = ICON_PADDING
	art.offset_top = ICON_PADDING
	art.offset_right = -ICON_PADDING
	art.offset_bottom = -ICON_PADDING
	button.add_child(art)

	_slot_buttons.append(button)
	_slot_icons.append(art)
	_slot_themes.append(DiceTheme.BONE)
	_slot_names.append(label)


## Which slot a Control belongs to, or -1. Asked with whatever gui_get_hovered_control()
## returned, which may be the button or something inside it, so the walk goes upward.
func slot_of(control: Node) -> int:
	var node := control
	while node != null:
		if node is Button:
			var index := _slot_buttons.find(node)
			if index >= 0:
				return index
		node = node.get_parent()
	return -1


## How many dice the pack holds, so the save can walk them.
func slot_count() -> int:
	return _slot_buttons.size()


func is_open() -> bool:
	return _is_open


func slot_theme(slot: int) -> int:
	return _slot_themes[slot] if slot >= 0 and slot < _slot_themes.size() \
		else DiceTheme.BONE


func slot_name(slot: int) -> String:
	return _slot_names[slot] if slot >= 0 and slot < _slot_names.size() else ""


## Paint a slot, and remember it. Only the dice made from here afterwards are affected —
## what is already on the board keeps whatever it was given.
func set_slot_theme(slot: int, theme: int) -> void:
	if slot < 0 or slot >= _slot_themes.size():
		return
	_slot_themes[slot] = theme
	_slot_icons[slot].material = DiceTheme.material_for(theme)


func _hide_name_tag() -> void:
	if _name_tag != null:
		_name_tag.visible = false


## Name the die being pointed at, in a tag beside its button.
##
## Anchored rather than following the cursor: the buttons sit on a grid, so a tag pinned
## to one lands somewhere predictable. Level with the button it names, but clear of the
## *drawer* rather than of the button — hung off the button it covered whichever ones were
## beside it, which is three quarters of them.
func _show_name_tag(label: String, button: Control) -> void:
	_name_tag_label.text = label
	_name_tag.reset_size()    # a PanelContainer outside a container keeps its old size
	_name_tag.visible = true
	var box := button.get_global_rect()
	_name_tag.position = Vector2(
		minf(_drawer.get_global_rect().end.x + 10.0,
			get_viewport_rect().size.x - _name_tag.size.x - 8.0),
		box.position.y + (box.size.y - _name_tag.size.y) / 2.0)


## Where a ghost would sit if it were offset from the pointer rather than centred on it:
## up and to the right, folding to the other side near an edge the way the hover tag does.
##
## **Currently unused, and kept on purpose.** The ghosts are centred on the cursor, which
## is where they read best; this is here for the day that changes. It is not dead code
## left behind by accident — do not delete it as such. Kept in the port for the same
## reason it is kept in the C# tree, so the two do not quietly diverge.
static func ghost_position(cursor: Vector2, size: Vector2, view: Vector2) -> Vector2:
	var x := cursor.x + GHOST_GAP
	if x + size.x > view.x - 4.0:
		x = cursor.x - GHOST_GAP - size.x
	var y := cursor.y - GHOST_GAP - size.y
	if y < 4.0:
		y = cursor.y + GHOST_GAP
	return Vector2(clampf(x, 4.0, maxf(4.0, view.x - size.x - 4.0)),
		clampf(y, 4.0, maxf(4.0, view.y - size.y - 4.0)))


## The die's own corner of its 128px cell, so a button can be the size of the die rather
## than the size of the frame it was rendered in.
##
## Measured off the icon's alpha rather than fixed, because the dice are not all the same
## size in frame — the d4 is 70px across and the numbered d6 48 — and a fixed window would
## leave the small ones swimming. Cropping each to itself makes every button equally full.
static func crop_to_die(icon: Texture2D) -> Texture2D:
	if not (icon is AtlasTexture):
		return icon
	var cell := icon as AtlasTexture
	if cell.atlas == null:
		return icon
	var image := cell.get_image()
	if image == null:
		return icon

	# get_image() on an AtlasTexture yields the region; on anything else the lot.
	var whole := image.get_width() > int(cell.region.size.x)
	var origin := Vector2i(cell.region.position) if whole else Vector2i.ZERO
	var size := Vector2i(cell.region.size)

	var x0 := size.x
	var y0 := size.y
	var x1 := -1
	var y1 := -1
	for y in size.y:
		for x in size.x:
			if image.get_pixel(origin.x + x, origin.y + y).a > 0.1:
				if x < x0: x0 = x
				if x > x1: x1 = x
				if y < y0: y0 = y
				if y > y1: y1 = y
	if x1 < x0 or y1 < y0:
		return icon             # nothing drawn; leave it alone

	x0 = maxi(0, x0 - ICON_PADDING)
	y0 = maxi(0, y0 - ICON_PADDING)
	x1 = mini(size.x - 1, x1 + ICON_PADDING)
	y1 = mini(size.y - 1, y1 + ICON_PADDING)
	var cropped := AtlasTexture.new()
	cropped.atlas = cell.atlas
	cropped.region = Rect2(cell.region.position + Vector2(x0, y0),
		Vector2(x1 - x0 + 1, y1 - y0 + 1))
	return cropped


func _on_die_button_input(event: InputEvent, scene: String, icon: Texture2D,
		slot: int) -> void:
	if not (event is InputEventMouseButton):
		return
	var mouse_button := event as InputEventMouseButton
	if mouse_button.button_index != MOUSE_BUTTON_LEFT or not mouse_button.pressed:
		return

	Sfx.play("ui_click")
	_is_dragging_icon = true
	_dragging_scene = scene
	_dragging_icon = icon
	_dragging_slot = slot
	_drag_preview = TextureRect.new()
	_drag_preview.texture = _dragging_icon
	_drag_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_drag_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_drag_preview.size = Vector2(GHOST_SIZE, GHOST_SIZE)
	_drag_preview.modulate = Color(1, 1, 1, 0.8)
	_drag_preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_drag_preview.z_index = 100
	# The ghost wears the slot's colour, so what you drop is what you dragged.
	_drag_preview.material = DiceTheme.material_for(_slot_themes[slot])
	add_child(_drag_preview)
	_drag_preview.position = get_viewport().get_mouse_position() \
		- _drag_preview.size / 2.0
	get_viewport().set_input_as_handled()


func set_drawer_open(open: bool, animate: bool) -> void:
	if animate and open != _is_open:
		Sfx.play("ui_open" if open else "ui_close")
	_is_open = open
	var panel_left := DRAWER_LEFT if open else -DRAWER_WIDTH
	var panel_right := DRAWER_LEFT + DRAWER_WIDTH if open else 0.0
	var toggle_left := DRAWER_LEFT + DRAWER_WIDTH + 8.0 if open else 8.0
	var toggle_right := DRAWER_LEFT + DRAWER_WIDTH + 48.0 if open else 48.0

	if _drawer_tween != null:
		_drawer_tween.kill()
	if animate:
		_drawer_tween = create_tween().set_parallel() \
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_drawer_tween.tween_property(_drawer, "offset_left", panel_left, 0.22)
		_drawer_tween.tween_property(_drawer, "offset_right", panel_right, 0.22)
		_drawer_tween.tween_property(_toggle_button, "offset_left", toggle_left, 0.22)
		_drawer_tween.tween_property(_toggle_button, "offset_right", toggle_right, 0.22)
	else:
		_drawer.offset_left = panel_left
		_drawer.offset_right = panel_right
		_toggle_button.offset_left = toggle_left
		_toggle_button.offset_right = toggle_right

	_toggle_button.text = "◀" if open else "▶"
	_toggle_button.tooltip_text = "Close dice menu" if open else "Open dice menu"
