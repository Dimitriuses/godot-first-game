class_name DiceHud
extends Control

## The list of dice on the board, the running total, and the hover tag.
##
## Hand port of scripts/DiceHud.cs (ROADMAP 9a). The C# tree is canonical.

signal delete_requested(die: Dice)
signal delete_all_requested()


class DieEntry extends RefCounted:
	var die: Dice
	var die_name := ""

	## The order the die was added in. It fixes the row's place in the list for as long
	## as the die is on the board: sorting by value instead meant every throw reshuffled
	## the whole list, and a new die arrived somewhere unpredictable.
	var seq := 0

	## The last *settled* value. Not read live from the die, because the result is decided
	## the moment a throw starts and reading it would give the roll away.
	var value := 0

	## Which one of its kind this is, and whether that is worth printing — a lone d20 is
	## just "d20". Both recomputed on every refresh, so deleting a die never leaves a gap
	## in the numbering.
	var number := 0
	var numbered := false

	# The row's nodes, kept alive across refreshes. Rebuilding them every time was what
	# made an added die blink into existence instead of arriving.
	var wrapper: Control = null
	var panel: PanelContainer = null
	var icon_box: Control = null
	var icon: TextureRect = null
	var name_label: Label = null
	var value_label: Label = null
	var remove: Button = null

	## Where the die sits inside its frame, and how much to magnify it. Measured once off
	## the resting pose so a tumbling frame keeps the same scale as a still one.
	var icon_zoom := 1.0
	var icon_centre := Vector2.ZERO

	var shown := ""
	var was_rolling := false
	var hovered := false


## Where the hover tag sits relative to the cursor. Down and right, so the pointer is not
## standing on the number it is there to show.
const TAG_OFFSET := Vector2(18, 18)

## Drawer geometry, as offsets from the bottom-left corner. The height is sized to hold
## five rows without scrolling — the list scrolls perfectly well, but it should not open
## onto a half-cut row, and it has to leave the palette above it room.
const DRAWER_TOP := -384.0
const DRAWER_BOTTOM := -64.0

const ROW_HEIGHT := 38.0
const ICON_SIZE := 30.0

## How long a row takes to unfold in or collapse out.
const ROW_FADE := 0.2

var _entries := {}
# Not `visible`: Control already has one, and shadowing it hides the panel.
var _visible_entries: Array = []
var _rows: VBoxContainer
var _drawer: PanelContainer
var _total_button: Button
var _value_tag: PanelContainer
var _value_tag_label: Label
var _row_rest: StyleBoxFlat
var _row_hover: StyleBoxFlat
var _row_rolling: StyleBoxFlat
var _hovered_die: Dice = null
var _drawer_tween: Tween = null
var _is_open := false

## On a touchscreen there is no hovering, so anything that only appears under the pointer
## never appears at all. The delete cross stays out on those.
var _touch_ui := false
var _next_seq := 1


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_touch_ui = DisplayServer.is_touchscreen_available()
	_build_interface()
	set_open(false, false)
	refresh()


## Runs whenever there is anything in the list. The rows show live sprite frames and have
## to notice a roll starting, which nothing signals — a die can be set rolling by a
## collision, by the space bar or from its own menu.
func _process(_delta: float) -> void:
	tick()
	_update_value_tag()


## A die was entered or left by the cursor. Both dice report, so the exit of one arriving
## after the entry of the next must not clear the newer one.
func set_die_hovered(die: Dice, hovered: bool) -> void:
	if hovered:
		_hovered_die = die
	elif _hovered_die == die:
		_hovered_die = null
	else:
		return

	_update_value_tag()


## The number the die is showing, next to the cursor.
##
## Re-evaluated every frame rather than only on enter and exit, because everything it
## depends on can change while the cursor sits still: the die gets picked up, a roll
## finishes and the value changes, another die knocks into it.
func _update_value_tag() -> void:
	# Hidden while held, and while rolling: the result is decided the moment a throw
	# starts, and showing it before the clip lands would give the throw away.
	if _hovered_die == null or not is_instance_valid(_hovered_die) \
			or _hovered_die.is_held or _hovered_die.is_rolling \
			or not _entries.has(_hovered_die):
		_value_tag.visible = false
		return

	var entry: DieEntry = _entries[_hovered_die]
	var shown := entry
	if not _renders_row(entry):
		var other := _partner_entry(entry)
		shown = other if other != null else entry
	_value_tag_label.text = "%s  %s" % [_label_of(shown), _value_text(shown)]
	_value_tag.reset_size()     # a PanelContainer outside a container keeps its old size
	_value_tag.visible = true

	# Flip to the other side of the cursor near an edge rather than clamping to it. A
	# clamped tag stops following the pointer while the pointer keeps moving, which reads
	# as a label that has got stuck.
	var mouse := get_viewport().get_mouse_position()
	var view := get_viewport_rect().size
	var size := _value_tag.size
	_value_tag.position = Vector2(
		maxf(8.0, mouse.x - TAG_OFFSET.x - size.x \
			if mouse.x + TAG_OFFSET.x + size.x > view.x - 8.0 \
			else mouse.x + TAG_OFFSET.x),
		maxf(8.0, mouse.y - TAG_OFFSET.y - size.y \
			if mouse.y + TAG_OFFSET.y + size.y > view.y - 8.0 \
			else mouse.y + TAG_OFFSET.y))


func add_die(die: Dice, value: int) -> void:
	var entry := DieEntry.new()
	entry.die = die
	entry.seq = _next_seq
	_next_seq += 1
	entry.value = value
	entry.die_name = die.display_name
	_entries[die] = entry
	refresh()


func update_value(die: Dice, value: int) -> void:
	if not _entries.has(die):
		return
	_entries[die].value = value
	refresh()


func remove_die(die: Dice) -> void:
	if not _entries.has(die):
		return
	var entry: DieEntry = _entries[die]
	die.set_hovered(false)
	set_die_hovered(die, false)
	_entries.erase(die)
	_dismiss(entry)
	refresh()


## How a die is named in the list and in its own menu: "d6" on its own, "d6 #2" once there
## are two of them, "d100" for a linked pair.
##
## Public because the right-click menu wants the same name — a die called "#2" in one
## place and "d6" in the other is two dice as far as the player is concerned.
func label_for(die: Dice) -> String:
	if die == null or not is_instance_valid(die):
		return ""
	if not _entries.has(die):
		return die.display_name
	var entry: DieEntry = _entries[die]
	if _renders_row(entry):
		return _label_of(entry)
	var other := _partner_entry(entry)
	return _label_of(other if other != null else entry)


## The name a die reads under: its own, or "d100" once it is half of one. A pair is one
## entry, not two — showing the same hundred against both would double it in the total,
## and showing each die's own number would contradict the pair it is part of.
func _group_of(entry: DieEntry) -> String:
	return "d100" if _partner_entry(entry) != null else entry.die_name


func _label_of(entry: DieEntry) -> String:
	return "%s #%d" % [_group_of(entry), entry.number] if entry.numbered \
		else _group_of(entry)


func _partner_entry(entry: DieEntry) -> DieEntry:
	var partner := entry.die.partner
	if partner != null and is_instance_valid(partner) and _entries.has(partner):
		return _entries[partner]
	return null


## Which of a linked pair owns the row. The tens die, so the choice does not depend on
## which one happened to be linked first.
func _renders_row(entry: DieEntry) -> bool:
	return _partner_entry(entry) == null or entry.die.is_tens_die


func _value_of(entry: DieEntry) -> int:
	var other := _partner_entry(entry)
	return entry.value if other == null else Dice.pair_percent(entry.die, other.die)


func _value_text(entry: DieEntry) -> String:
	return str(entry.value) if _partner_entry(entry) == null \
		else "%d%%" % _value_of(entry)


## Whether the die behind a row is mid-throw — either half of a pair counts, because the
## percentage is not settled until both are.
func _is_rolling(entry: DieEntry) -> bool:
	if not is_instance_valid(entry.die):
		return false
	var other := _partner_entry(entry)
	return entry.die.is_rolling or (other != null and other.die.is_rolling)


func _build_interface() -> void:
	_drawer = PanelContainer.new()
	_drawer.name = "Drawer"
	_drawer.mouse_filter = Control.MOUSE_FILTER_STOP
	_drawer.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	# Flush with the palette above it: the two panels are one column, and a column whose
	# halves do not line up reads as a mistake rather than as a choice.
	_drawer.offset_left = DicePalette.DRAWER_LEFT
	_drawer.offset_top = DRAWER_TOP
	_drawer.offset_right = DicePalette.DRAWER_LEFT + DicePalette.DRAWER_WIDTH
	_drawer.offset_bottom = DRAWER_BOTTOM
	add_child(_drawer)

	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color = Color("202536e8")
	panel_style.border_color = Color("77819b")
	panel_style.set_border_width_all(2)
	panel_style.set_corner_radius_all(10)
	_drawer.add_theme_stylebox_override("panel", panel_style)

	_row_rest = _row_style("2b3149a0", "2b3149a0")
	_row_hover = _row_style("3b4468e0", "8c97b8")
	_row_rolling = _row_style("4a3f5ce0", "c2a2e8")

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_top", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_bottom", 12)
	_drawer.add_child(margin)

	var content := VBoxContainer.new()
	content.add_theme_constant_override("separation", 8)
	margin.add_child(content)

	var title := Label.new()
	title.text = "DICE ON BOARD"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 17)
	content.add_child(title)

	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	content.add_child(scroll)

	_rows = VBoxContainer.new()
	_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_rows.add_theme_constant_override("separation", 4)
	scroll.add_child(_rows)

	var delete_all := Button.new()
	delete_all.text = "Delete All"
	delete_all.focus_mode = Control.FOCUS_NONE
	delete_all.tooltip_text = "Remove every die from the board"
	delete_all.pressed.connect(delete_all_requested.emit)
	content.add_child(delete_all)

	_total_button = Button.new()
	_total_button.name = "Total"
	_total_button.text = "Total: 0"
	_total_button.focus_mode = Control.FOCUS_NONE
	_total_button.tooltip_text = "Show or hide dice on the board"
	_total_button.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	_total_button.offset_left = DicePalette.DRAWER_LEFT
	_total_button.offset_top = -56
	_total_button.offset_right = DicePalette.DRAWER_LEFT + DicePalette.DRAWER_WIDTH
	_total_button.offset_bottom = -16
	_total_button.pressed.connect(_on_total_pressed)
	add_child(_total_button)

	# Added last so it draws over the drawer, and ignores the mouse so it cannot put
	# itself between the cursor and the die it is describing.
	_value_tag = PanelContainer.new()
	_value_tag.name = "ValueTag"
	_value_tag.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_value_tag.visible = false
	var tag_style := StyleBoxFlat.new()
	tag_style.bg_color = Color("202536f0")
	tag_style.border_color = Color("77819b")
	tag_style.content_margin_left = 10
	tag_style.content_margin_right = 10
	tag_style.content_margin_top = 5
	tag_style.content_margin_bottom = 5
	tag_style.set_border_width_all(2)
	tag_style.set_corner_radius_all(8)
	_value_tag.add_theme_stylebox_override("panel", tag_style)
	add_child(_value_tag)

	_value_tag_label = Label.new()
	_value_tag_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_value_tag_label.add_theme_font_size_override("font_size", 16)
	_value_tag.add_child(_value_tag_label)


func _on_total_pressed() -> void:
	set_open(not _is_open, true)


static func _row_style(bg: String, border: String) -> StyleBoxFlat:
	var style := StyleBoxFlat.new()
	style.bg_color = Color(bg)
	style.border_color = Color(border)
	style.set_border_width_all(1)
	style.set_corner_radius_all(7)
	return style


## Bring the rows into line with the dice: add what is new, retire what is gone,
## renumber, and reorder if anything has actually moved.
##
## Structure only. What each row *says* is settled every frame in tick(), because a
## rolling die changes what its row shows without anything calling in.
func refresh() -> void:
	if _rows == null:
		return

	_visible_entries.clear()
	for entry in _entries.values():
		if _renders_row(entry):
			_visible_entries.append(entry)
	_visible_entries.sort_custom(func(a, b): return a.seq < b.seq)

	# Number each kind separately and from one, every time. A single global counter that
	# only ever went up left the list claiming dice that had been deleted.
	var counted := {}
	for entry in _visible_entries:
		var group := _group_of(entry)
		counted[group] = counted.get(group, 0) + 1
	var seen := {}
	for entry in _visible_entries:
		var group := _group_of(entry)
		seen[group] = seen.get(group, 0) + 1
		entry.number = seen[group]
		entry.numbered = counted[group] > 1

	# A die that has just been linked stops drawing its own row; unlinking gives it back.
	# Both go through the same fold in and out as arriving and leaving.
	for entry in _entries.values():
		if not _renders_row(entry) and entry.wrapper != null:
			_dismiss(entry)
	for entry in _visible_entries:
		if entry.wrapper == null:
			_build_row(entry)

	# Only shuffle when the live rows are genuinely out of order. Moving them on every
	# change would yank the survivors of a delete upward before the deleted row had
	# finished collapsing.
	var last := -1
	var ordered := true
	for entry in _visible_entries:
		var index: int = entry.wrapper.get_index()
		if index < last:
			ordered = false
			break
		last = index
	if not ordered:
		var target := 0
		for entry in _visible_entries:
			_rows.move_child(entry.wrapper, target)
			target += 1

	tick()
	set_process(_entries.size() > 0)


## Everything about a row that can change without anyone calling in: the die's current
## sprite frame, whether it is mid-throw, and the total underneath.
func tick() -> void:
	var total := 0
	var any_rolling := false

	# Ask the viewport what the pointer is on, rather than letting each row report its own
	# mouse_entered/mouse_exited. Those fire on "is this the hovered control", not "is the
	# pointer inside this rect", so moving onto the delete cross — a child that takes the
	# mouse — exited the row, which hid the cross before it could be clicked. Anything
	# inside the row counts as the row.
	#
	# Null-guarded because this also runs on the way out: a die reports tree_exiting during
	# teardown, which refreshes a HUD that has already left the tree and has no viewport to
	# ask.
	var viewport := get_viewport()
	var under: Control = viewport.gui_get_hovered_control() if viewport != null else null

	for entry in _visible_entries:
		if not is_instance_valid(entry.die) or entry.wrapper == null:
			continue

		# Annotated rather than inferred: `entry` comes out of an untyped Array, so
		# `entry.panel` is a Variant and the expression has no static type to infer.
		var hovered: bool = under != null \
			and (under == entry.panel or entry.panel.is_ancestor_of(under))
		if hovered != entry.hovered:
			_set_row_hovered(entry, hovered)

		var rolling := _is_rolling(entry)
		any_rolling = any_rolling or rolling
		if not rolling:
			total += _value_of(entry)

		# The die's own frame, live. A die that is tumbling tumbles in the list too, which
		# is the whole of "which one is going" — there is no separate indicator to keep in
		# step with the animation, because it *is* the animation.
		var sprite: AnimatedSprite2D = entry.die.animated_sprite
		var frames: SpriteFrames = sprite.sprite_frames if sprite != null else null
		if frames != null and frames.has_animation(sprite.animation):
			var frame := frames.get_frame_texture(sprite.animation,
				mini(sprite.frame, frames.get_frame_count(sprite.animation) - 1))
			if frame != null:
				entry.icon.texture = frame
				# The row wears whatever the die is wearing — taken from the sprite rather
				# than looked up, because the die swaps material as it spins and the row
				# has to follow it. A reference assignment either way.
				entry.icon.material = sprite.material
				entry.icon.size = frame.get_size() * entry.icon_zoom
				entry.icon.position = Vector2(ICON_SIZE, ICON_SIZE) / 2.0 \
					- entry.icon_centre * entry.icon_zoom

		entry.name_label.text = _label_of(entry)

		# An ellipsis rather than the old number: the value on screen during a throw is
		# the previous one, and leaving it up is a small lie the hover tag already refuses
		# to tell.
		var text := "…" if rolling else _value_text(entry)
		if text != entry.shown:
			entry.value_label.text = text
			# Flash the new number, but never the ellipsis — the point is to catch the eye
			# when a result lands, and a die still going has no result.
			if not rolling and entry.was_rolling:
				_flash(entry.value_label)
			entry.shown = text
		entry.value_label.self_modulate = Color(0.72, 0.76, 0.88) if rolling \
			else Color.WHITE
		entry.was_rolling = rolling

		var style := _row_rolling if rolling else (_row_hover if entry.hovered \
			else _row_rest)
		entry.panel.add_theme_stylebox_override("panel", style)
		entry.remove.modulate = Color(1, 1, 1,
			1.0 if entry.hovered or _touch_ui else 0.0)

	# Rolling dice are left out rather than counted at their old value, so the total never
	# states a number the board is not showing. The ellipsis says as much.
	_total_button.text = "Total: %d …" % total if any_rolling else "Total: %d" % total


static func _flash(label: Control) -> void:
	label.create_tween() \
		.tween_property(label, "modulate", Color.WHITE, 0.45) \
		.from(Color("ffe08a"))


func _build_row(entry: DieEntry) -> void:
	# A plain Control the row is clipped by, so it can unfold from nothing on the way in
	# and collapse to nothing on the way out. The panel keeps its full height throughout —
	# squashing it instead would compress the text rather than reveal it.
	entry.wrapper = Control.new()
	# Named so the row can be picked out of the tree — by the remote inspector, and by a
	# harness that wants to read what the list is actually saying. The sequence number is
	# part of it because a name that collides with a sibling is not renamed but thrown
	# away: Godot substitutes "@Control@42".
	entry.wrapper.name = "DieRow%d" % entry.seq
	entry.wrapper.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry.wrapper.clip_contents = true
	entry.wrapper.mouse_filter = Control.MOUSE_FILTER_IGNORE
	entry.wrapper.modulate = Color(1, 1, 1, 0)
	_rows.add_child(entry.wrapper)

	entry.panel = PanelContainer.new()
	entry.panel.name = "Panel"
	entry.panel.mouse_filter = Control.MOUSE_FILTER_STOP
	entry.panel.anchor_right = 1.0
	entry.panel.offset_bottom = ROW_HEIGHT
	entry.panel.add_theme_stylebox_override("panel", _row_rest)
	entry.wrapper.add_child(entry.panel)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 5)
	margin.add_theme_constant_override("margin_right", 5)
	entry.panel.add_child(margin)

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 7)
	margin.add_child(row)

	entry.icon_box = Control.new()
	entry.icon_box.name = "IconBox"
	entry.icon_box.custom_minimum_size = Vector2(ICON_SIZE, ICON_SIZE)
	entry.icon_box.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	entry.icon_box.clip_contents = true
	entry.icon_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(entry.icon_box)

	entry.icon = TextureRect.new()
	entry.icon.name = "Icon"
	entry.icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	entry.icon.stretch_mode = TextureRect.STRETCH_SCALE
	entry.icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	entry.icon_box.add_child(entry.icon)
	_measure_icon(entry)

	entry.name_label = Label.new()
	entry.name_label.name = "DieName"
	entry.name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	entry.name_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	entry.name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	entry.name_label.add_theme_font_size_override("font_size", 15)
	row.add_child(entry.name_label)

	entry.value_label = Label.new()
	entry.value_label.name = "DieValue"
	entry.value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	entry.value_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	entry.value_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	entry.value_label.add_theme_font_size_override("font_size", 18)
	row.add_child(entry.value_label)

	# Out of sight until the row is pointed at. Eight always-on crosses down the side of
	# the list were louder than the numbers they sat beside.
	entry.remove = Button.new()
	entry.remove.name = "DieRemove"
	entry.remove.custom_minimum_size = Vector2(24, 24)
	entry.remove.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	entry.remove.focus_mode = Control.FOCUS_NONE
	entry.remove.modulate = Color(1, 1, 1, 0)
	# Drawn rather than typed, for the same reason the palette's arrow is: "×" came out
	# as a blank box in the browser.
	UiSkin.icon_child(entry.remove, "Icon", _draw_remove_icon)
	entry.remove.pressed.connect(_on_remove_pressed.bind(entry))
	row.add_child(entry.remove)

	var tween := entry.wrapper.create_tween().set_parallel() \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(entry.wrapper, "custom_minimum_size",
		Vector2(0, ROW_HEIGHT), ROW_FADE).from(Vector2.ZERO)
	tween.tween_property(entry.wrapper, "modulate:a", 1.0, ROW_FADE)


func _draw_remove_icon(icon: Control) -> void:
	UiSkin.draw_cross(icon, Color("e6e8f2"))


func _on_remove_pressed(entry: DieEntry) -> void:
	# One row, one delete: on a pair the row stands for both dice, so the cross has to
	# take both. Removing half a d100 and leaving the other half behind would be a
	# stranger thing for it to do.
	var other := _partner_entry(entry)
	if other != null:
		delete_requested.emit(other.die)
	delete_requested.emit(entry.die)


## Fold a row away and let it go. The entry keeps no reference afterwards, so the same die
## being unlinked builds a fresh one.
func _dismiss(entry: DieEntry) -> void:
	var wrapper := entry.wrapper
	entry.wrapper = null
	if wrapper == null or not is_instance_valid(wrapper):
		return

	_set_row_hovered(entry, false)
	# Renamed on the way out so a row still folding away is not mistaken for one of the
	# live ones — it stays in the tree for the length of the fade.
	wrapper.name = "GoneRow%d" % entry.seq
	if entry.panel != null and is_instance_valid(entry.panel):
		entry.panel.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var tween := wrapper.create_tween().set_parallel() \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	tween.tween_property(wrapper, "custom_minimum_size", Vector2.ZERO, ROW_FADE)
	tween.tween_property(wrapper, "modulate:a", 0.0, ROW_FADE)
	tween.chain().tween_callback(wrapper.queue_free)


func _set_row_hovered(entry: DieEntry, hovered: bool) -> void:
	entry.hovered = hovered
	if not is_instance_valid(entry.die):
		return
	# Both halves of a pair light up: the row stands for the two of them.
	entry.die.set_hovered(hovered)
	var other := _partner_entry(entry)
	if other != null and is_instance_valid(other.die):
		other.die.set_hovered(hovered)


## How far to magnify the die in its row, and where it sits inside its frame.
##
## Taken from the resting pose and then applied to every frame, so a die that starts
## tumbling does not jump in size — the blur simply spills past the edges of the little
## window and is clipped, which is what a die going past a window looks like.
static func _measure_icon(entry: DieEntry) -> void:
	var resting := entry.die.resting_frame(1)
	if not (resting is AtlasTexture):
		return
	var full := resting as AtlasTexture
	var cropped := DicePalette.crop_to_die(full) as AtlasTexture
	if cropped == null:
		cropped = full
	var crop := cropped.region.size
	if crop.x <= 0.0 or crop.y <= 0.0:
		return
	entry.icon_zoom = ICON_SIZE / maxf(crop.x, crop.y)
	entry.icon_centre = cropped.region.position - full.region.position + crop / 2.0


func is_open() -> bool:
	return _is_open


func set_open(open: bool, animate: bool) -> void:
	# Only when the player did it. The silent form is how the screenshot tool and the
	# harnesses arrange the panel, and neither wants a noise for it.
	if animate and open != _is_open:
		Sfx.play("ui_open" if open else "ui_close")
	_is_open = open
	var top := DRAWER_TOP if open else 0.0
	var bottom := DRAWER_BOTTOM if open else DRAWER_TOP - DRAWER_BOTTOM

	if _drawer_tween != null:
		_drawer_tween.kill()
	if animate:
		_drawer_tween = create_tween().set_parallel() \
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_drawer_tween.tween_property(_drawer, "offset_top", top, 0.24)
		_drawer_tween.tween_property(_drawer, "offset_bottom", bottom, 0.24)
	else:
		_drawer.offset_top = top
		_drawer.offset_bottom = bottom
