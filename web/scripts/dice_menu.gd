class_name DiceMenu
extends Control

## The panel that opens on a right-click, on a die or on the board behind it.
##
## Hand port of scripts/DiceMenu.cs (ROADMAP 9a). The C# tree is canonical.
##
## One panel with two sets of items rather than two menus: the fiddly parts — folding back
## at the screen edge, not closing on the press that is about to choose an item, telling
## the board's clicks from its own — are the same either way, and having them once means
## they cannot drift apart.
##
## It owns no behaviour: choosing an item raises a signal and closes, and GameManager does
## the work. That keeps the rules in one place rather than splitting them between the
## board and a menu, and it is why the same actions can be driven from the keyboard
## without going through here at all.

signal roll_requested(die: Dice)
signal copy_requested(die: Dice)
signal delete_requested(die: Dice)
signal link_requested(die: Dice)
signal unlink_requested(die: Dice)

## A colour scheme was chosen, for whatever the menu is open on — a die, or a slot in the
## palette. Which of those it is, the board decides; the menu only reports the choice, the
## same way every other item here does.
signal theme_requested(theme: int)

signal throw_all_requested()
signal respawn_requested()
signal delete_all_requested()

## What the Link item can offer for the die the menu is opening on. The board knows which
## dice are out and what they are; the menu only draws the answer.
enum Linkage {
	## Nothing on the board could pair with it — a d6, or the only d10 out.
	IMPOSSIBLE,
	## A partner exists to pick from.
	AVAILABLE,
	## Already half of a d100.
	LINKED,
}

## Keys that do the same thing to the die under the cursor. Listed on the items so they
## are discoverable; GameManager is what actually watches for them.
const ROLL_KEY := KEY_R
const COPY_KEY := KEY_C

var _panel: PanelContainer
var _name_label: Label
var _value_label: Label
var _link_item: Button
var _die_items: VBoxContainer
var _board_items: VBoxContainer
var _theme_items: VBoxContainer
var _swatches: Array[Button] = []
var _throw_all_item: Button
var _respawn_item: Button
var _delete_all_item: Button
var _target: Dice = null
var _linked_now := false     # whether _link_item currently means "Unlink"

## Which palette slot the menu was opened on, or -1 when it was opened on a die or on the
## board. The board reads it to know what a chosen theme applies to.
var palette_slot := -1


## The die the open menu belongs to, or null when it is closed.
var target: Dice:
	get:
		if _panel != null and _panel.visible and is_instance_valid(_target):
			return _target
		return null


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build()
	_panel.visible = false
	set_process(false)


## Only while open, and only to keep the value honest: a die rolled from the menu keeps
## the menu up for the two seconds the clip runs, and a stale number there would be worse
## than none.
func _process(_delta: float) -> void:
	# Only ever running for the die menu; the board one has no value to keep up to date
	# and switches processing off.
	var die := target
	if die == null:
		close()
	else:
		_value_label.text = str(die.value)


## `label` is the name the die list is calling this die — "d6 #2" rather than "d6" once
## there are two of them. Passed in rather than worked out here, so the menu and the list
## can never disagree about which die is which.
func open(die: Dice, at: Vector2, linkage := Linkage.IMPOSSIBLE, label := "") -> void:
	if die == null or not is_instance_valid(die):
		return

	_target = die
	palette_slot = -1
	_name_label.text = die.display_name if label.is_empty() else label
	_mark_theme(die.theme)
	_theme_items.visible = true
	_value_label.text = str(die.value)
	_set_linkage(linkage)
	_die_items.visible = true
	_board_items.visible = false
	_show_at(at)
	set_process(true)       # the value can change while the menu is up


## The other menu: a right-click on the board rather than on a die.
##
## The same three things the buttons and the space bar already do, gathered where the
## pointer is. `count` is only for the header — everything is greyed out when there is
## nothing to do it to.
func open_board(at: Vector2, count: int) -> void:
	_target = null
	palette_slot = -1
	_theme_items.visible = false
	_name_label.text = "BOARD"
	_value_label.text = str(count)
	_die_items.visible = false
	_board_items.visible = true
	_throw_all_item.disabled = count == 0
	_respawn_item.disabled = count == 0
	_delete_all_item.disabled = count == 0
	_show_at(at)
	set_process(false)      # nothing here changes on its own


## The third way in: a right-click on a die in the palette, which sets what colour that
## kind of die comes out in from now on.
##
## Only the swatches — there is nothing to roll or delete, because there is no die yet.
## Choosing here changes what the *next* one looks like, never what is already on the
## board, which is why it is the palette that remembers it and not this menu.
func open_palette(slot: int, label: String, at: Vector2, theme: int) -> void:
	_target = null
	palette_slot = slot
	_name_label.text = label
	_value_label.text = ""
	_die_items.visible = false
	_board_items.visible = false
	_theme_items.visible = true
	_mark_theme(theme)
	_show_at(at)
	set_process(false)


## Put the panel at the pointer, folding back rather than hanging off the edge — a menu
## that opens half outside the window cannot be finished.
func _show_at(at: Vector2) -> void:
	# On opening only. A sound on closing as well would fire on every click that happens
	# to dismiss the panel, which is most of them.
	if not _panel.visible:
		Sfx.play("ui_open")
	_panel.visible = true
	_panel.reset_size()         # a PanelContainer outside a container keeps its size
	var view := get_viewport_rect().size
	var size := _panel.size
	_panel.position = Vector2(
		maxf(4.0, at.x - size.x if at.x + size.x > view.x - 4.0 else at.x),
		maxf(4.0, at.y - size.y if at.y + size.y > view.y - 4.0 else at.y))


## Whether either menu is up. `target` is the die one only.
func is_open() -> bool:
	return _panel != null and _panel.visible


func close() -> void:
	_target = null
	palette_slot = -1
	if _panel != null:
		_panel.visible = false
	set_process(false)


## Whether a point is over the open menu, so a click there is the menu's and not the
## board's.
func covers(point: Vector2) -> bool:
	return _panel != null and _panel.visible \
		and _panel.get_global_rect().has_point(point)


func _build() -> void:
	_panel = PanelContainer.new()
	_panel.name = "Panel"
	_panel.mouse_filter = Control.MOUSE_FILTER_STOP
	# Wide enough for the swatch strip: seven of them plus their gaps and the panel's own
	# margins. The items were happy at 168 and do not mind the extra.
	_panel.custom_minimum_size = Vector2(196, 0)
	var style := StyleBoxFlat.new()
	style.bg_color = Color("202536f2")
	style.border_color = Color("77819b")
	style.set_border_width_all(2)
	style.set_corner_radius_all(10)
	_panel.add_theme_stylebox_override("panel", style)
	add_child(_panel)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 10)
	margin.add_theme_constant_override("margin_top", 8)
	margin.add_theme_constant_override("margin_right", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	_panel.add_child(margin)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 4)
	margin.add_child(content)

	# Header: which die this is, and what it is showing.
	var head := HBoxContainer.new()
	_name_label = Label.new()
	_name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_name_label.add_theme_font_size_override("font_size", 16)
	head.add_child(_name_label)
	_value_label = Label.new()
	_value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_value_label.add_theme_font_size_override("font_size", 18)
	head.add_child(_value_label)
	content.add_child(head)
	content.add_child(HSeparator.new())

	# Two sets of items, one shown at a time. Separate containers rather than one list
	# with things hidden, so neither has to know the other exists.
	_die_items = VBoxContainer.new()
	_die_items.name = "DieItems"
	_die_items.add_theme_constant_override("separation", 4)
	content.add_child(_die_items)

	_add_item(_die_items, "Roll", "(%s)" % OS.get_keycode_string(ROLL_KEY),
		"Roll this die where it stands", _emit_roll)
	_add_item(_die_items, "Copy", "(%s)" % OS.get_keycode_string(COPY_KEY),
		"Take a copy, then click where to put it — hold Shift to keep stamping",
		_emit_copy)
	_link_item = _add_item(_die_items, "Link", "", "", _emit_link_or_unlink)
	_die_items.add_child(HSeparator.new())
	_add_item(_die_items, "Delete", "", "Remove this die from the board", _emit_delete)

	# Between the die's items and its Delete, and shared with the palette menu, which
	# shows this and nothing else.
	_theme_items = VBoxContainer.new()
	_theme_items.name = "ThemeItems"
	_theme_items.add_theme_constant_override("separation", 4)
	content.add_child(_theme_items)
	_theme_items.add_child(HSeparator.new())
	var theme_caption := Label.new()
	theme_caption.text = "Theme"
	theme_caption.add_theme_font_size_override("font_size", 13)
	theme_caption.modulate = Color(0.78, 0.82, 0.92)
	_theme_items.add_child(theme_caption)
	_build_swatches(_theme_items)

	_board_items = VBoxContainer.new()
	_board_items.name = "BoardItems"
	_board_items.visible = false
	_board_items.add_theme_constant_override("separation", 4)
	content.add_child(_board_items)

	_throw_all_item = _add_item(_board_items, "Throw all", "(Space)",
		"Scatter every die across the board and roll it", throw_all_requested.emit)
	_respawn_item = _add_item(_board_items, "Respawn", "",
		"Gather every die back to the middle and stop it", respawn_requested.emit)
	_board_items.add_child(HSeparator.new())
	_delete_all_item = _add_item(_board_items, "Delete all", "",
		"Clear the board", delete_all_requested.emit)


# The item callbacks. C# used lambdas closing over `target`; these are named because a
# GDScript lambda captures by value at connect time, which would freeze the die the menu
# was built with rather than the one it is currently open on.
func _emit_roll() -> void:
	roll_requested.emit(_target)


func _emit_copy() -> void:
	copy_requested.emit(_target)


func _emit_delete() -> void:
	delete_requested.emit(_target)


func _emit_link_or_unlink() -> void:
	if _linked_now:
		unlink_requested.emit(_target)
	else:
		link_requested.emit(_target)


## One small square per colour scheme, showing the scheme rather than naming it.
##
## Seven names would not fit and would not help — a colour is quicker to recognise than to
## read. The name is on the tooltip for the two that are hard to tell apart at this size.
func _build_swatches(into: Container) -> void:
	var row := HBoxContainer.new()
	row.name = "Swatches"
	row.add_theme_constant_override("separation", 3)
	into.add_child(row)

	for i in DiceTheme.count():
		var button := Button.new()
		button.name = "Swatch%d" % i
		button.custom_minimum_size = Vector2(21, 21)
		button.tooltip_text = DiceTheme.name_of(i)
		button.focus_mode = Control.FOCUS_NONE
		button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		button.pressed.connect(_on_swatch_pressed.bind(i))
		row.add_child(button)
		_swatches.append(button)
	_mark_theme(DiceTheme.BONE)


func _on_swatch_pressed(theme: int) -> void:
	# Reported before closing, so the board can still ask what the menu was open on.
	# close() clears that.
	theme_requested.emit(theme)
	close()


## Paint the swatches, and ring the one that is already on. Every state of a Button has to
## be overridden or it repaints itself grey the moment it is hovered.
func _mark_theme(theme: int) -> void:
	for i in _swatches.size():
		var active := i == theme
		var style := StyleBoxFlat.new()
		style.bg_color = DiceTheme.swatch(i)
		style.border_color = Color("ffe6a8") if active else Color("00000060")
		style.set_border_width_all(2 if active else 1)
		style.set_corner_radius_all(5)
		for state in ["normal", "pressed", "disabled"]:
			_swatches[i].add_theme_stylebox_override(state, style)

		var lit: StyleBoxFlat = style.duplicate()
		lit.border_color = Color("ffffff")
		lit.set_border_width_all(2)
		_swatches[i].add_theme_stylebox_override("hover", lit)


## Set the Link item to whatever the die can do: pair up, come apart, or nothing.
##
## One item rather than two, because linking and unlinking are never both offers — a die
## is in a pair or it is not.
func _set_linkage(linkage: Linkage) -> void:
	_link_item.disabled = linkage == Linkage.IMPOSSIBLE
	match linkage:
		Linkage.LINKED:
			_link_item.text = "Unlink"
			_link_item.tooltip_text = "Read this die on its own again"
		Linkage.AVAILABLE:
			_link_item.text = "Link"
			_link_item.tooltip_text = "Pick the other d10 to read as one d100"
		_:
			_link_item.text = "Link"
			_link_item.tooltip_text = \
				"Needs a plain d10 and a percentile d10 on the board"
	_linked_now = linkage == Linkage.LINKED


func _add_item(into: Container, text: String, hint: String, tooltip: String,
		pressed: Callable) -> Button:
	var button := Button.new()
	# Named after the item so it can be picked out of the tree — by the remote inspector,
	# and by a harness driving the menu the way a player does.
	button.name = text.replace(" ", "")
	button.text = "%s   %s" % [text, hint] if hint.length() > 0 else text
	button.alignment = HORIZONTAL_ALIGNMENT_LEFT
	button.tooltip_text = tooltip
	button.focus_mode = Control.FOCUS_NONE
	# No handler means it is not built yet, and a button that looks live but does nothing
	# is worse than one that says so.
	button.disabled = not pressed.is_valid()
	button.add_theme_constant_override("h_separation", 0)
	if pressed.is_valid():
		button.pressed.connect(_on_item_pressed.bind(pressed))
	into.add_child(button)
	return button


func _on_item_pressed(pressed: Callable) -> void:
	pressed.call()
	close()
