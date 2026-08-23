class_name GameManager
extends Node2D

## The board: dragging, menus, spawning, linking, saving, and the shudder.
##
## Hand port of scripts/GameManager.cs (ROADMAP 9a) and the largest file in the port at
## 1,531 C# lines. The C# tree is canonical.
##
## Two things GDScript has not got show up here more than anywhere else, and both are
## handled the same way throughout:
##
##   * **no method overloads.** C#'s three `SpawnDie` become `spawn_die` (by path, the
##     one everything outside this class should use), `spawn_die_at_slot` and
##     `spawn_die_scene`.
##   * **no tuples and no HashSet.** The pack is an Array of Dictionaries, and
##     `_deleting_dice` is a Dictionary used as a set.


## A die that has been asked for but whose scene is still loading.
##
## The first d20 of a session takes about half a second to load — measured at 441ms
## blocking — and a die that takes half a second to turn up after the player drops it
## feels broken. So the palette's icon stands in: it is already in memory, it is the same
## artwork, and it plays the same arrival animation. The real die replaces it once both
## the load and the animation have finished, which makes the swap invisible — by then the
## placeholder is sitting still at full size, showing exactly what the die will show.
class PendingSpawn extends RefCounted:
	var path := ""
	var at := Vector2.ZERO
	var theme := 0
	var ghost: Sprite2D = null
	var elapsed := 0.0


@export var mouse_pin: PinJoint2D
@export var fake_body: StaticBody2D
@export var dice_area: Area2D

## The pack, read from a manifest rather than held as an exported array of PackedScene.
##
## That is not a style choice. Loading a die's PackedScene loads its whole sheet set —
## measured at 110 MB of texture memory for the d20 — and an exported array loads every
## entry the moment game.tscn does, whether or not a die is ever thrown. Holding paths and
## loading on demand is what keeps a board of two d6 from paying for a d20 nobody touched.
@export var pack_path := "res://assets/dice/pack.json"

## Fastest a dragged die is steered or released at, px/s. Without a bound, a flick hands
## the solver an impulse it has to fight, which is how dice used to come out through the
## walls.
@export var max_drag_speed := 4000.0

## Radius of the clump a Shift-drag gathers dice into, per die. The clump grows as the
## square root of the count, because that is how discs pack into a disc.
@export var gather_spread := 38.0

## How hard and how long the board shudders when every die is thrown at once. Small on
## purpose: it is a table being knocked, not an earthquake.
@export var shake_strength := 5.0
@export var shake_duration := 0.3
@export var shake_frequency := 55.0

## Whether this board reads and writes the save file.
##
## Off for the screenshot tool and for harnesses. Both would otherwise inherit whatever
## board the machine happened to have saved — and, worse, overwrite it on the way out.
@export var persist_board := true

## One entry per die in the pack, in palette order: {"scene", "name", "icon"}.
var pack: Array = []

## Where each die's art sits relative to its origin, and how much its icon was resized to
## fit the sheet. Parallel to `pack`; used only by the loading placeholder.
var _pack_offsets: Array[Vector2] = []
var _pack_scales: Array[float] = []

## Scenes already loaded, kept so a second d6 costs nothing. Godot caches resources
## itself; this only saves the repeated path lookup.
var _loaded_scenes := {}

var _pending_spawns: Array = []
var _icon_sheet: Texture2D = null

var _dice: Array[Dice] = []
var _selected_dice: Array[Dice] = []
var _deleting_dice := {}
var _dragged_die: Dice = null
var _active_die: Dice = null
var _hovered_die: Dice = null
var _dice_hud: DiceHud
var _dice_menu: DiceMenu
var _palette: DicePalette
var _mute_button: MuteButton
var _group_drag_button: GroupDragButton
var _fullscreen_button: FullscreenButton

## Whether a drag takes every die, as holding Shift does. A toggle rather than a modifier
## because a touchscreen has no modifiers to hold.
var _group_drag := false

## Shaking the device throws the board, where there is a device to shake. Zero on a
## desktop and in a browser, so feed() is never reached and this costs nothing there.
## ROADMAP 9b-touch is what would feed it from the DOM in a browser build.
var _shake_gesture := ShakeGesture.new()

## The browser-shaped holes in Godot, filled from the DOM: the accelerometer, which the
## web platform does not implement at all, and the audio context, which a browser will
## not start outside a user gesture. Inert off the web. This node has no counterpart in
## the C# tree and cannot have one — see web_platform.gd.
var _web: WebPlatform
var _ui_layer: CanvasLayer

## A copy waiting to be put down: the die type taken, the face it was showing, and the
## ghost that follows the cursor until a click places it.
var _pending_copy_scene := ""
var _pending_copy_face := 0
var _pending_copy_theme := 0
var _copy_preview: TextureRect = null

## The die waiting to be paired, while its possible partners stand highlighted.
var _pending_link: Dice = null
var _swallow_next_die_click := false

## Set when a double tap has just opened a menu, so the emulated mouse press that Godot
## generates from the same tap does not also act on it.
var _swallow_next_press := false

## When and where the last touch went down, for spotting a double tap on a platform that
## does not report one. The window is generous — a thumb is not a mouse — and the distance
## is what stops a quick tap-and-drag reading as a double.
const DOUBLE_TAP_MS := 400
const DOUBLE_TAP_SLOP := 48.0
var _last_tap_ms := 0
var _last_tap_at := Vector2.ZERO

## Seconds of shudder left, and the direction it runs along.
var _shake_left := 0.0
var _shake_axis := Vector2.RIGHT
var _is_dragging := false
var _is_group_dragging := false
var _last_mouse_position := Vector2.ZERO
var _drag_velocity := Vector2.ZERO
var _spawn_position := Vector2.ZERO

## The last state written, as JSON. The autosave compares against it rather than tracking
## a dirty flag through a dozen call sites: it catches the mute button and the palette's
## colours as readily as it catches a die being deleted, and a settled board serialises
## identically and so writes nothing.
var _last_saved := ""
var _next_save_check_ms := 0
## How often the board is compared against what is on disk. This is the primary way a save
## happens, not a backstop: a browser tab can close without anything being notified, so
## the last second of play is what a web build stands to lose — and this *is* the web
## build, so it matters here rather than in theory.
const SAVE_CHECK_MS := 1000
var _single_grab_offset := Vector2.ZERO
var _board_bounds := Rect2()

## How fast a release has to be before it counts as a throw rather than a put-down.
const THROW_SOUND_SPEED := 220.0


func _ready() -> void:
	mouse_pin.node_a = mouse_pin.get_path_to(fake_body)
	dice_area.body_exited.connect(_on_body_exited)
	_board_bounds = _compute_board_bounds()

	_load_pack()

	# Before Sfx, so the audio context is being unlocked by the time anything asks to
	# make a noise.
	_web = WebPlatform.new()
	_web.name = "WebPlatform"
	add_child(_web)

	# Before the panels, so anything that makes a noise while building has somewhere to
	# send it.
	var sfx := Sfx.new()
	sfx.name = "Sfx"
	add_child(sfx)

	_ui_layer = CanvasLayer.new()
	_ui_layer.name = "GameUiLayer"
	add_child(_ui_layer)

	_palette = DicePalette.new()
	_palette.name = "DicePalette"
	_palette.pack = pack
	_ui_layer.add_child(_palette)
	_palette.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_palette.spawn_requested.connect(_on_palette_spawn_requested)

	_dice_hud = DiceHud.new()
	_dice_hud.name = "DiceHud"
	_ui_layer.add_child(_dice_hud)
	_dice_hud.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_dice_hud.delete_requested.connect(delete_die)
	_dice_hud.delete_all_requested.connect(delete_all_dice)

	_mute_button = MuteButton.new()
	_mute_button.name = "MuteButton"
	_ui_layer.add_child(_mute_button)
	_mute_button.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	_group_drag_button = GroupDragButton.new()
	_group_drag_button.name = "GroupDragButton"
	_ui_layer.add_child(_group_drag_button)
	_group_drag_button.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_group_drag_button.group_toggled.connect(_on_group_drag_toggled)

	# Slot 2. This is the tree where it matters: the game is a canvas among page
	# furniture, and the Fullscreen API is the only way out of that.
	_fullscreen_button = FullscreenButton.new()
	_fullscreen_button.name = "FullscreenButton"
	_ui_layer.add_child(_fullscreen_button)
	_fullscreen_button.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	_dice_menu = DiceMenu.new()
	_dice_menu.name = "DiceMenu"
	_ui_layer.add_child(_dice_menu)
	_dice_menu.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_dice_menu.roll_requested.connect(_roll_one)
	_dice_menu.copy_requested.connect(_begin_copy)
	_dice_menu.delete_requested.connect(delete_die)
	_dice_menu.link_requested.connect(_begin_link)
	_dice_menu.unlink_requested.connect(_unlink)
	_dice_menu.throw_all_requested.connect(throw_all_dice)
	_dice_menu.respawn_requested.connect(on_spawn_button)
	_dice_menu.delete_all_requested.connect(delete_all_dice)
	_dice_menu.theme_requested.connect(_on_theme_chosen)

	for child in get_children():
		if child is Dice:
			_register_die(child)

	if _dice.size() > 0:
		_spawn_position = _dice[0].position
		_select_only(_dice[0])

	# After the spawn point is taken from the scene's own die, because that die is about
	# to be cleared away and the respawn anchor should not go with it.
	if persist_board and SaveGame.exists():
		_apply_save(SaveGame.load_board())

	# One line to the browser console, so a build that misbehaves in a way no harness can
	# reach can still be diagnosed from a phone without a rebuild.
	print(_web.describe())

	_update_processing()        # idle until a copy or a shake needs the frame


func _on_group_drag_toggled(value: bool) -> void:
	_group_drag = value


func _on_palette_spawn_requested(path: String, at: Vector2, theme: int) -> void:
	spawn_die(path, at, theme)


## A real quit, while the tree is still standing.
##
## **Not `_exit_tree`**, which was the first attempt and is too late: children leave the
## tree before their parent, so every die has already run its tree_exiting handler and
## taken itself out of the list, and Sfx.instance has already been cleared. The board
## saved there is an empty one that is not muted.
func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST and persist_board:
		_save_if_changed()


func _physics_process(delta: float) -> void:
	if persist_board and Time.get_ticks_msec() >= _next_save_check_ms:
		_next_save_check_ms = Time.get_ticks_msec() + SAVE_CHECK_MS
		_save_if_changed()

	if _pending_spawns.size() > 0:
		_step_pending_spawns(delta)

	# A phone being shaken is the space bar. Godot returns zero from the accelerometer on
	# any platform without one — including the browser, which implements no sensor API at
	# all — so on the web the samples come from the DOM instead. WebPlatform answers for
	# both, and this line does not have to know which it got.
	var acceleration := _web.read_accelerometer()
	if acceleration != Vector3.ZERO and _shake_gesture.feed(acceleration, delta) \
			and _dice.size() > 0:
		throw_all_dice()

	var mouse := get_global_mouse_position()

	# Let the cursor leave the board, but never let the pin follow it out. Dragged
	# unclamped, the joint hauls the die into a wall and the solver fights back hard
	# enough to squeeze it through — worst in the corners, where two walls push at once.
	var target := mouse
	if _is_dragging and not _is_group_dragging and _dragged_die != null \
			and is_instance_valid(_dragged_die):
		target = _clamp_into(_origin_bounds_for(_dragged_die),
			mouse + _single_grab_offset) - _single_grab_offset
	mouse_pin.global_position = target

	if not _is_dragging or delta <= 0:
		return

	# Measured from the clamped point, not the raw cursor: once the die is up against a
	# wall it is not moving, so letting go there should not fling it.
	_drag_velocity = (target - _last_mouse_position) / delta
	_last_mouse_position = target

	if _is_group_dragging:
		# Steer, rather than teleport: a frozen body moved by assigning global_position
		# ignores walls and other dice completely, while a body given a velocity gets
		# stopped by the solver.
		#
		# Everything is drawn to the cursor and nothing keeps the arrangement it was
		# picked up in. Holding the original formation meant each die fought to reach a
		# slot that was inside a wall, which is what made them strain against it.
		var gather := gather_spread * sqrt(maxf(1.0, _selected_dice.size()))
		for die in _selected_dice:
			if not is_instance_valid(die):
				continue
			var to_target := _clamp_into(_origin_bounds_for(die), target) \
				- die.global_position
			var far := to_target.length() - gather
			if far > 0.0:
				die.linear_velocity = (to_target.normalized() * far / delta) \
					.limit_length(max_drag_speed)
			else:
				# Already in the clump: stop pushing and let them settle against each
				# other, so a pile against a wall sits still instead of shoving.
				die.linear_velocity *= 0.8

	# Last line of defence. Steering a die by setting its velocity overrides the contact
	# response, so a die caught between another die and a wall gets extruded through it —
	# measured at 22px past the wall before this was added. Nudging it back is a few
	# pixels and only ever happens under that squeeze.
	for die in _selected_dice:
		if not is_instance_valid(die):
			continue
		var inside := _clamp_into(_origin_bounds_for(die), die.global_position)
		if not inside.is_equal_approx(die.global_position):
			die.global_position = inside


## The playable rectangle, inside the walls. Worked out once at _ready.
func board_bounds() -> Rect2:
	return _board_bounds


## The playable rectangle, derived from the wall colliders rather than hardcoded, so
## nudging a wall in the editor moves the drag limit with it.
func _compute_board_bounds() -> Rect2:
	var slabs: Array[Rect2] = []
	for child in get_children():
		if not (child is StaticBody2D):
			continue
		for part in child.get_children():
			if part is CollisionShape2D and part.shape is RectangleShape2D:
				var size: Vector2 = (part.shape as RectangleShape2D).size \
					* part.global_scale.abs()
				slabs.append(Rect2(part.global_position - size / 2, size))
	if slabs.is_empty():
		return Rect2(Vector2.ZERO, get_viewport_rect().size)

	var outer := slabs[0]
	for s in slabs:
		outer = outer.merge(s)

	# Each slab is one side of the frame; take its inner face.
	var mid := outer.position + outer.size / 2
	var left := outer.position.x
	var right := outer.end.x
	var top := outer.position.y
	var bottom := outer.end.y
	for s in slabs:
		var c := s.position + s.size / 2
		if s.size.x >= s.size.y:
			if c.y < mid.y:
				top = maxf(top, s.end.y)
			else:
				bottom = minf(bottom, s.position.y)
		else:
			if c.x < mid.x:
				left = maxf(left, s.end.x)
			else:
				right = minf(right, s.position.x)
	return Rect2(left, top, maxf(0, right - left), maxf(0, bottom - top))


## Where a die's origin may sit so that its collider stays inside the walls. The collider
## is offset from the origin, so that offset has to come back out.
func _origin_bounds_for(die: Dice) -> Rect2:
	var r := die.collision_radius
	var size := _board_bounds.size - Vector2(r, r) * 2.0
	return Rect2(_board_bounds.position + Vector2(r, r) - die.collision_offset,
		Vector2(maxf(0, size.x), maxf(0, size.y)))


static func _clamp_into(box: Rect2, p: Vector2) -> Vector2:
	return Vector2(clampf(p.x, box.position.x, box.end.x),
		clampf(p.y, box.position.y, box.end.y))


func _input(event: InputEvent) -> void:
	# A double tap is what a screen has instead of a second mouse button. Godot reports
	# the touch itself and, with emulate_mouse_from_touch on, a mouse event alongside it —
	# this reads the touch, and the flag stops the mouse copy from acting on the same tap.
	if event is InputEventScreenTouch:
		var tap := event as InputEventScreenTouch
		if tap.pressed:
			# `double_tap` is filled in by the platform, and not every platform fills it
			# in: the first web build never reported one, so a double tap did nothing in
			# a browser at all. Detecting it here as well costs two fields and works
			# wherever touches are reported — and the platform's own flag is still
			# honoured first, so nothing changes where it already worked.
			var doubled := tap.double_tap or _is_second_tap(tap.position)
			_last_tap_ms = Time.get_ticks_msec()
			_last_tap_at = tap.position
			if doubled:
				_last_tap_ms = 0    # three taps are not two double taps
				_open_context_menu(tap.position)
				_swallow_next_press = true
				get_viewport().set_input_as_handled()
			return

	if event is InputEventKey:
		var key := event as InputEventKey
		if Shortcuts.is_key(key, KEY_SHIFT) and key.pressed and not key.echo:
			if not _is_dragging:    # changing the selection mid-drag would be a surprise
				_select_all()
			return

		if key.pressed and not key.echo:
			if Shortcuts.is_key(key, KEY_SPACE):
				throw_all_dice()
				get_viewport().set_input_as_handled()
				return

			if Shortcuts.is_key(key, KEY_ESCAPE) and (not _pending_copy_scene.is_empty() \
					or _pending_link != null or _dice_menu.target != null):
				_cancel_copy()
				_cancel_link()
				_dice_menu.close()
				get_viewport().set_input_as_handled()
				return

			# The menu's actions, on whichever die is under the cursor — or on the one the
			# open menu belongs to, since pointing at an item means not pointing at the die
			# any more.
			var subject := _dice_menu.target
			if subject == null:
				subject = _hovered_die
			if subject != null and is_instance_valid(subject) and not _is_dragging:
				if Shortcuts.is_key(key, DiceMenu.ROLL_KEY):
					_roll_one(subject)
					_dice_menu.close()
					get_viewport().set_input_as_handled()
					return
				if Shortcuts.is_key(key, DiceMenu.COPY_KEY):
					_begin_copy(subject)
					_dice_menu.close()
					get_viewport().set_input_as_handled()
					return

	if not (event is InputEventMouseButton) or not (event as InputEventMouseButton).pressed:
		if _is_dragging and event is InputEventMouseButton:
			var release := event as InputEventMouseButton
			if release.button_index == MOUSE_BUTTON_LEFT and not release.pressed:
				_release_dragged_dice()
				get_viewport().set_input_as_handled()
		return

	var mouse_button := event as InputEventMouseButton

	# The emulated press that follows a double tap. Godot turns touches into mouse events
	# as well as reporting them, so without this the tap that opened the menu goes on to
	# grab whatever it was over.
	if _swallow_next_press:
		_swallow_next_press = false
		get_viewport().set_input_as_handled()
		return
	_swallow_next_press = false

	# The event's own position, not the current cursor: they are the same thing in the
	# game and not in a harness, and the click should mean where it was made.
	var point := mouse_button.position
	# Cleared at the top of every press so a flag set for a click the die never heard
	# about cannot go on to swallow a later one.
	_swallow_next_die_click = false

	# A press on the menu is the menu's. Closing it here would take the panel away before
	# the button saw the release, and no item would ever fire.
	if _dice_menu.covers(point):
		return

	if mouse_button.button_index == MOUSE_BUTTON_RIGHT:
		_open_context_menu(point)
		get_viewport().set_input_as_handled()
		return

	if mouse_button.button_index != MOUSE_BUTTON_LEFT:
		return

	_dice_menu.close()

	# _input runs before the GUI gets a look, so a click on the palette or the die list
	# arrives here too. Acting on either at that point would drop a die behind the panel
	# that was clicked.
	if get_viewport().gui_get_hovered_control() != null:
		return

	# Finishing a pairing has to happen here rather than in the die's own click handler:
	# this runs first, and whatever it decides is already done by the time the die hears
	# about the same click.
	if _pending_link != null:
		var source := _pending_link
		_cancel_link()
		var picked := _die_at(get_viewport().get_canvas_transform().affine_inverse() * point)
		if picked != null and Dice.can_pair(source, picked) and picked.partner == null:
			_link(source, picked)
		# Consumed whether or not it landed on a partner: the click was the choice, and it
		# should not also grab whatever it happened to be over.
		_swallow_next_die_click = true
		get_viewport().set_input_as_handled()
		return

	if _place_copy(point, mouse_button.shift_pressed):
		get_viewport().set_input_as_handled()


## The menu a right-click asks for — or a double-tap, which is the same request made with
## one finger on a screen that has no second button.
##
## Which of the three menus it is depends on what is under the point, and that has to be
## settled here: _input runs before the GUI and before physics picking, so waiting for
## either to report is waiting for a decision this has already made.
func _open_context_menu(point: Vector2) -> void:
	# Asked for while something is pending means "not that", and nothing more.
	var was_pending := not _pending_copy_scene.is_empty() or _pending_link != null
	_cancel_copy()
	_cancel_link()
	_dice_menu.close()
	if was_pending:
		return

	# The palette is about the kind of die, not about any die on the board, so it has to
	# be asked of the GUI before the physics world: the pointer is over a Control, and
	# _die_at would find nothing and open the board menu behind the panel.
	var slot := _palette.slot_of(get_viewport().gui_get_hovered_control())
	if slot >= 0:
		_dice_menu.open_palette(slot, _palette.slot_name(slot), point,
			_palette.slot_theme(slot))
		return

	var under := _die_at(get_viewport().get_canvas_transform().affine_inverse() * point)
	if under != null:
		_dice_menu.open(under, point, _linkage_of(under), _dice_hud.label_for(under))
	else:
		_dice_menu.open_board(point, _dice.size())
	_swallow_next_die_click = true


## Whether this touch lands soon enough after the last one, and close enough to it, to be
## the second half of a double tap.
func _is_second_tap(at: Vector2) -> bool:
	return _last_tap_ms > 0 		and Time.get_ticks_msec() - _last_tap_ms <= DOUBLE_TAP_MS 		and at.distance_to(_last_tap_at) <= DOUBLE_TAP_SLOP


## The die under a point on the board, or null. Asked of the physics world rather than
## waited for from the die itself, because picking reports after _input has already run
## and decided.
func _die_at(world_point: Vector2) -> Dice:
	var query := PhysicsPointQueryParameters2D.new()
	query.position = world_point
	query.collide_with_bodies = true
	query.collide_with_areas = false
	for hit in get_world_2d().direct_space_state.intersect_point(query, 8):
		if hit["collider"] is Dice:
			return hit["collider"]
	return null


func _register_die(die: Dice) -> void:
	if _dice.has(die):
		return

	_dice.append(die)
	_dice_hud.add_die(die, die.value)
	die.input_pickable = true
	die.dice_rolled.connect(_on_dice_rolled.bind(die))
	die.input_event.connect(_on_dice_input_event.bind(die))
	# Hovering a die names the number it is showing. A d20's up-face is small and steeply
	# foreshortened in this camera, so reading it off the die is a squint.
	die.mouse_entered.connect(_on_die_mouse_entered.bind(die))
	die.mouse_exited.connect(_on_die_mouse_exited.bind(die))
	die.tree_exiting.connect(_on_die_tree_exiting.bind(die))


func _on_die_mouse_entered(die: Dice) -> void:
	_hovered_die = die
	_dice_hud.set_die_hovered(die, true)


func _on_die_mouse_exited(die: Dice) -> void:
	if _hovered_die == die:
		_hovered_die = null
	_dice_hud.set_die_hovered(die, false)


func _on_die_tree_exiting(die: Dice) -> void:
	_dice.erase(die)
	_selected_dice.erase(die)
	_dice_hud.remove_die(die)
	_deleting_dice.erase(die)
	if _active_die == die:
		_active_die = _dice[0] if _dice.size() > 0 else null


# input_event passes (viewport, event, shape_idx); bind() appends the die after them.
func _on_dice_input_event(_viewport: Node, event: InputEvent, _shape_idx: int,
		clicked_die: Dice) -> void:
	if _is_dragging or not (event is InputEventMouseButton):
		return
	var mouse_button := event as InputEventMouseButton
	if not mouse_button.pressed or mouse_button.button_index != MOUSE_BUTTON_LEFT:
		return

	# _input already dealt with this click — as a menu, or as the second half of a
	# pairing. Either way the die has nothing left to do with it.
	if _swallow_next_die_click:
		_swallow_next_die_click = false
		return
	_dice_menu.close()

	# A copy waiting to be placed wins over grabbing the die that was clicked; the
	# alternative is a click that looks dead because the copy is still on the cursor.
	if _place_copy(mouse_button.position, mouse_button.shift_pressed):
		get_viewport().set_input_as_handled()
		return

	# The event's own modifier, not the keyboard's current state — the rule the copy above
	# already follows, and the only one a synthesised click can satisfy. The toggle sits
	# beside it because it means exactly the same thing.
	if mouse_button.shift_pressed or _group_drag:
		_select_all()
		_active_die = clicked_die
	else:
		_select_only(clicked_die)

	_begin_drag(clicked_die)
	get_viewport().set_input_as_handled()


func _begin_drag(clicked_die: Dice) -> void:
	_is_dragging = true
	_dragged_die = clicked_die
	var mouse := get_global_mouse_position()
	_last_mouse_position = mouse
	_drag_velocity = Vector2.ZERO

	if _selected_dice.size() == 1:
		# Remember where on the die the cursor grabbed it, so the clamp can keep the die
		# itself inside rather than just the pin.
		_single_grab_offset = clicked_die.global_position - mouse
		mouse_pin.node_b = mouse_pin.get_path_to(clicked_die)
		clicked_die.start_dragging()
		clicked_die.angular_damp = 10
		return

	_is_group_dragging = true
	for die in _selected_dice:
		die.start_dragging()
		# Deliberately not frozen. A frozen body moved by hand is noclip: it walks
		# straight through the walls and through the other dice.


func _release_dragged_dice() -> void:
	if _is_group_dragging:
		# The fastest of them decides, and the sound plays once for the throw rather than
		# once per die: a handful of them together is one gesture, and eight copies of the
		# same clip landing on one frame is just a louder clip.
		var fastest := 0.0
		for die in _selected_dice:
			# It is already moving at the steer velocity; do not overwrite that with the
			# raw cursor speed, which ignores whatever the die was pressed against.
			die.freeze = false
			die.linear_velocity = die.linear_velocity.limit_length(max_drag_speed)
			var speed := die.linear_velocity.length()
			fastest = maxf(fastest, speed)
			die.release_from_drag(speed)
		if fastest > THROW_SOUND_SPEED:
			Sfx.play("die_throw", 0.0, 0.08)
	elif _dragged_die != null:
		_dragged_die.linear_velocity = \
			_dragged_die.linear_velocity.limit_length(max_drag_speed)
		var release_speed := _dragged_die.linear_velocity.length()
		var die := _dragged_die
		_end_single_drag()
		die.release_from_drag(release_speed)
		# A fling makes a noise; setting a die down does not.
		if release_speed > THROW_SOUND_SPEED:
			Sfx.play("die_throw", 0.0, 0.08)

	_clear_drag_state()


## Put a die from the pack on the board, loading its scene if this is the first one.
##
## This is what everything outside this class should use — it is the only one of the three
## that keeps the pack lazy.
func spawn_die(scene_path: String, screen_position: Vector2,
		theme := DiceTheme.BONE) -> void:
	# Already in memory: nothing to wait for, so put it down the usual way.
	if _loaded_scenes.has(scene_path):
		spawn_die_scene(_scene_at_path(scene_path), screen_position, theme)
		return
	if _pack_index_of(scene_path) < 0:
		return
	_begin_pending_spawn(scene_path, screen_position, theme)


func _pack_index_of(scene_path: String) -> int:
	for i in pack.size():
		if pack[i]["scene"] == scene_path:
			return i
	return -1


## Start the die's scene loading and stand its icon in the meantime.
##
## The icon is positioned by the manifest's offset and drawn at the reciprocal of its
## scale, because the icons were resized to a common cell and the dice are not the same
## size in frame. Get either wrong and the die jumps when it arrives.
func _begin_pending_spawn(scene_path: String, at: Vector2, theme: int) -> void:
	ResourceLoader.load_threaded_request(scene_path)

	var wanted := _clamp_spawn(at)
	var pending := PendingSpawn.new()
	pending.path = scene_path
	pending.at = wanted
	pending.theme = theme

	var index := _pack_index_of(scene_path)
	var icon: Rect2 = pack[index]["icon"] if index >= 0 else Rect2()
	var offset := _pack_offsets[index] if index >= 0 else Vector2.ZERO
	var scale := _pack_scales[index] if index >= 0 else 1.0

	if _icon_sheet == null:
		_icon_sheet = load("res://assets/dice/icons.png")
	pending.ghost = Sprite2D.new()
	pending.ghost.name = "Arriving"
	if _icon_sheet != null:
		var atlas := AtlasTexture.new()
		atlas.atlas = _icon_sheet
		atlas.region = icon
		pending.ghost.texture = atlas
	# The icon cell is 64px of transparent border around cropped art, so the offset lands
	# the art where the die's own art will be.
	pending.ghost.global_position = wanted + offset
	pending.ghost.scale = Vector2.ONE * (Dice.APPEAR_FROM / maxf(0.001, scale))
	pending.ghost.modulate = Color(1, 1, 1, 0)
	pending.ghost.material = DiceTheme.material_for(theme)
	add_child(pending.ghost)

	# The same curve and duration Dice.appear uses, so the handover is a continuation
	# rather than a second animation.
	var full := 1.0 / maxf(0.001, scale)
	var tween := pending.ghost.create_tween().set_parallel()
	tween.tween_property(pending.ghost, "scale", Vector2.ONE * full,
		Dice.APPEAR_SECONDS).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	tween.tween_property(pending.ghost, "modulate:a", 1.0,
		Dice.APPEAR_SECONDS * 0.45).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

	Sfx.play("spawn", 0.0, 0.05)
	_pending_spawns.append(pending)


## Poll the loads in flight. A die appears once its scene is ready *and* the arrival
## animation has run its course — waiting for both is what makes the swap invisible,
## because the placeholder is then sitting still at full size.
func _step_pending_spawns(delta: float) -> void:
	for i in range(_pending_spawns.size() - 1, -1, -1):
		var pending: PendingSpawn = _pending_spawns[i]
		pending.elapsed += delta

		var status := ResourceLoader.load_threaded_get_status(pending.path)
		if status == ResourceLoader.THREAD_LOAD_IN_PROGRESS \
				or pending.elapsed < Dice.APPEAR_SECONDS:
			continue

		_pending_spawns.remove_at(i)
		if is_instance_valid(pending.ghost):
			pending.ghost.queue_free()
		if status != ResourceLoader.THREAD_LOAD_LOADED:
			push_warning("could not load %s" % pending.path)
			continue

		var scene := ResourceLoader.load_threaded_get(pending.path) as PackedScene
		if scene == null:
			continue
		_loaded_scenes[pending.path] = scene
		# Silent and unanimated: the placeholder already made the noise and played the
		# arrival.
		spawn_die_scene(scene, pending.at, pending.theme, false, true)


## Where a die dropped at this point should be seen.
##
## Clamped in terms of the *drawn* die, not its origin: a die is drawn about twelve pixels
## below its own origin, so clamping the origin left every spawn sitting low. The caller
## subtracts the collider offset to get the origin.
func _clamp_spawn(screen_position: Vector2) -> Vector2:
	var view := get_viewport_rect().size
	return Vector2(clampf(screen_position.x, 80.0, view.x - 240.0),
		clampf(screen_position.y, 90.0, view.y - 80.0))


## The die at this place in the pack, by palette order.
func spawn_die_at_slot(slot: int, screen_position: Vector2,
		theme := DiceTheme.BONE) -> void:
	if slot >= 0 and slot < pack.size():
		spawn_die(pack[slot]["scene"], screen_position, theme)


func spawn_die_scene(scene: PackedScene, screen_position: Vector2,
		theme := DiceTheme.BONE, animate := true, quiet := false) -> void:
	if scene == null:
		return

	var die: Dice = scene.instantiate()
	add_child(die)
	die.theme = theme
	if animate:
		die.appear()
	if not quiet:
		Sfx.play("spawn", 0.0, 0.05)
	die.global_position = _clamp_spawn(screen_position) - die.collision_offset
	_register_die(die)
	_select_only(die)


## Select one die — and its partner with it, if it has one.
##
## A linked pair reads as a single d100, so it picks up, moves and is thrown as one thing.
## Two dice selected is already the group drag, which steers both to the cursor and keeps
## them apart, so this needs no separate handling downstream.
func _select_only(die: Dice) -> void:
	_selected_dice.clear()
	_selected_dice.append(die)
	if die.partner != null and is_instance_valid(die.partner) \
			and _dice.has(die.partner):
		_selected_dice.append(die.partner)
	_active_die = die


func _select_all() -> void:
	_selected_dice.clear()
	for die in _dice:
		_selected_dice.append(die)


## Gather every die back to the middle.
##
## The block is sized to the board rather than being four dice wide however many there
## are. That was the old rule, and with a lot of dice it built a column that ran off the
## bottom of the board — 40 dice made ten rows, 540px of them, from a spawn point 283px
## above the floor.
##
## Roughly square, spaced no wider than fits, and centred on the spawn point unless that
## would hang the block over an edge. _reset_die clamps each die individually afterwards,
## so the arithmetic here only has to be close.
func on_spawn_button() -> void:
	_cancel_drag()
	if _dice.size() == 0:
		return
	Sfx.play("respawn")

	# Inset by a die, so a die *centred* on the edge of this box is still inside the board
	# rather than half through the wall.
	var inner := _board_bounds.grow(-40.0)
	var columns := maxi(1, ceili(sqrt(float(_dice.size()))))
	var rows := ceili(_dice.size() / float(columns))

	# Shrink the spacing rather than the block: dice that end up touching is what a gather
	# looks like, dice off the board is not.
	var spacing := minf(54.0, minf(inner.size.x / maxi(1, columns - 1),
		inner.size.y / maxi(1, rows - 1)))

	var block := Vector2((columns - 1) * spacing, (rows - 1) * spacing)
	var top_left := _spawn_position - block / 2.0
	top_left = Vector2(
		clampf(top_left.x, inner.position.x,
			maxf(inner.position.x, inner.end.x - block.x)),
		clampf(top_left.y, inner.position.y,
			maxf(inner.position.y, inner.end.y - block.y)))

	for i in _dice.size():
		_reset_die(_dice[i], top_left
			+ Vector2((i % columns) * spacing, (i / columns) * spacing))


## Put a die back where it started, however it got to where it is.
##
## Dice.teleport_to applies the move from inside _integrate_forces, which is the engine's
## own answer to moving a rigid body and clears the velocities with it.
func _reset_die(die: Dice, position: Vector2) -> void:
	die.cancel_dragging()
	# Whatever state it was left in: a respawn is meant to un-stick things.
	die.freeze = false
	# Clamped per die, because each shape sits at its own offset from its origin.
	die.teleport_to(_clamp_into(_origin_bounds_for(die), position))


## Detach the mouse pin and undo the extra damping the single drag applies.
func _end_single_drag() -> void:
	mouse_pin.node_b = NodePath()
	if _dragged_die != null and is_instance_valid(_dragged_die):
		_dragged_die.angular_damp = 0


func _cancel_drag() -> void:
	if not _is_dragging:
		return
	_end_single_drag()
	for die in _selected_dice:
		die.freeze = false
		die.cancel_dragging()
	_clear_drag_state()


func _clear_drag_state() -> void:
	_is_dragging = false
	_is_group_dragging = false
	_dragged_die = null


## What the menu may offer for this die: pair up, come apart, or neither.
func _linkage_of(die: Dice) -> DiceMenu.Linkage:
	if die.partner != null and is_instance_valid(die.partner):
		return DiceMenu.Linkage.LINKED
	for other in _dice:
		if Dice.can_pair(die, other) and other.partner == null:
			return DiceMenu.Linkage.AVAILABLE
	return DiceMenu.Linkage.IMPOSSIBLE


## Start picking a partner. The dice that could take the other half light up, and the next
## click on one of them makes the pair.
func _begin_link(die: Dice) -> void:
	if die == null or not is_instance_valid(die):
		return

	_cancel_link()
	_pending_link = die
	for other in _dice:
		if Dice.can_pair(die, other) and other.partner == null:
			other.set_hovered(true)


func _cancel_link() -> void:
	if _pending_link == null:
		return
	for other in _dice:
		if is_instance_valid(other):
			other.set_hovered(false)
	_pending_link = null


## Read two dice as one d100. Either may already be in a pair; the old one comes apart
## first, because a die cannot be half of two hundreds at once.
func _link(a: Dice, b: Dice) -> void:
	if not Dice.can_pair(a, b):
		return
	_unlink(a)
	_unlink(b)
	a.partner = b
	b.partner = a
	Sfx.play("link")
	_dice_hud.update_value(a, a.value)      # redraw the list as one row


func _unlink(die: Dice) -> void:
	if die == null or not is_instance_valid(die):
		return
	var partner := die.partner
	die.partner = null
	if partner != null:
		Sfx.play("unlink")
	if partner != null and is_instance_valid(partner):
		partner.partner = null
		_dice_hud.update_value(partner, partner.value)
	_dice_hud.update_value(die, die.value)


## Take a copy of a die and wait for a click to put it down.
##
## Deliberately not the palette's press-drag-release: this starts from a menu the pointer
## has already been pressed and released on, so there is no drag left to carry. Click once
## more to place, Escape or right-click to drop the idea.
func _begin_copy(die: Dice) -> void:
	if die == null or not is_instance_valid(die):
		return

	var scene := _scene_of(die)
	if scene.is_empty():
		push_warning("%s: cannot tell which scene it came from, not copying" % die.name)
		return

	_cancel_copy()
	_pending_copy_scene = scene
	_pending_copy_face = die.get_result()    # a copy shows what it was copied from
	_pending_copy_theme = die.theme          # and wears what it was copied from

	_copy_preview = TextureRect.new()
	_copy_preview.name = "CopyPreview"
	# The face it was copied from, at rest and cropped to the die — the same picture the
	# palette puts on its buttons, so a copy on the cursor looks like the thing being
	# copied.
	_copy_preview.texture = DicePalette.crop_to_die(
		die.resting_frame(_pending_copy_face))
	_copy_preview.material = DiceTheme.material_for(_pending_copy_theme)
	_copy_preview.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_copy_preview.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_copy_preview.size = Vector2(DicePalette.GHOST_SIZE, DicePalette.GHOST_SIZE)
	_copy_preview.modulate = Color(1, 1, 1, 0.65)
	_copy_preview.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_copy_preview.z_index = 100
	_ui_layer.add_child(_copy_preview)
	_update_processing()


func _cancel_copy() -> void:
	_pending_copy_scene = ""
	if _copy_preview != null:
		_copy_preview.queue_free()
	_copy_preview = null
	_update_processing()


## Put the waiting copy down.
##
## `keep_ghost` means Shift was held for this click: leave the copy on the cursor so the
## next click stamps another, rather than going back to the menu for each one. Taken from
## the click itself rather than from the live keyboard, so it is the modifier state of the
## press that decides — and so a harness can drive it.
func _place_copy(screen_point: Vector2, keep_ghost: bool) -> bool:
	if _pending_copy_scene.is_empty():
		return false

	var scene := _pending_copy_scene
	var face := _pending_copy_face
	var theme := _pending_copy_theme
	if not keep_ghost:
		_cancel_copy()

	spawn_die(scene, screen_point, theme)
	# spawn_die selects what it made, so this is the copy and not the original.
	if _active_die != null:
		_active_die.place_on_face(face)
	return true


## Where a die came from, as a path. Never loads anything: a die that exists has already
## had its scene loaded, and asking for it again by name would defeat the point of loading
## on demand.
func _scene_of(die: Dice) -> String:
	return die.scene_file_path


func _process(delta: float) -> void:
	if _copy_preview != null:
		_copy_preview.position = get_viewport().get_mouse_position() \
			- _copy_preview.size / 2.0
	if _shake_left > 0.0:
		_step_shake(delta)


func delete_die(die: Dice) -> void:
	if not is_instance_valid(die) or _deleting_dice.has(die):
		return

	if _is_dragging and (_dragged_die == die or _selected_dice.has(die)):
		_cancel_drag()

	_unlink(die)        # never leave the other half of a pair pointing at a corpse
	if _pending_link == die:
		_cancel_link()

	Sfx.play("delete", 0.0, 0.05)
	_deleting_dice[die] = true
	_dice.erase(die)
	_selected_dice.erase(die)
	_dice_hud.remove_die(die)
	if _active_die == die:
		_active_die = _dice[0] if _dice.size() > 0 else null
	die.set_hovered(false)
	# Shrinks away and frees itself. Everything above has already taken it out of the
	# lists, so what is left on the board is a picture finishing its exit.
	die.vanish()


# ---------------------------------------------------------------------- persistence

## Everything worth remembering: the settings, and every die on the board.
##
## Dice are recorded by the path of the scene they came from rather than by an index into
## the pack, so reordering the pack cannot turn somebody's d20 into a d4. A die whose
## scene is no longer configured is dropped on load rather than guessed at.
func _collect_save() -> Dictionary:
	var themes := []
	for i in _palette.slot_count():
		themes.append(_palette.slot_theme(i))

	# The savable dice, gathered first, because a pairing is stored as an index into this
	# list and it must not be an index into one that skipped an entry.
	var live: Array[Dice] = []
	for die in _dice:
		if is_instance_valid(die) and not die.scene_file_path.is_empty():
			live.append(die)

	var saved := []
	for die in live:
		saved.append({
			"scene": die.scene_file_path,
			# Rounded, and not only for tidiness: the autosave decides whether to write by
			# comparing this against the last one, and unrounded positions change in the
			# sixth decimal for as long as the physics is awake.
			"x": roundf(die.global_position.x),
			"y": roundf(die.global_position.y),
			"rot": snappedf(die.rotation, 0.001),
			"face": die.get_result(),
			"theme": die.theme,
			"partner": live.find(die.partner) \
				if die.partner != null and is_instance_valid(die.partner) else -1,
		})

	return {
		"settings": {
			"muted": Sfx.muted(),
			"group_drag": _group_drag,
			"palette_open": _palette.is_open(),
			"list_open": _dice_hud.is_open(),
			"palette_themes": themes,
		},
		"dice": saved,
	}


## Write, but only when something has actually changed. Called on a timer rather than from
## every place that could change something, so nothing can be forgotten.
func _save_if_changed() -> void:
	if _palette == null or _dice_hud == null:
		return
	var data := _collect_save()
	var text := JSON.stringify(data)
	if text == _last_saved:
		return
	_last_saved = text
	SaveGame.store(data)


## Put a saved board back.
##
## Every field is optional and every one is checked. A save is a file on someone's
## machine: it can be half-written, hand-edited, or left over from a build that knew
## different dice, and none of those may throw.
func _apply_save(data) -> void:
	if data == null or typeof(data) != TYPE_DICTIONARY:
		return

	var settings = data.get("settings")
	if typeof(settings) == TYPE_DICTIONARY:
		if settings.has("muted"):
			Sfx.set_muted(bool(settings["muted"]))
			# The button was built before this ran, so it is still drawn for whatever the
			# sound was before the save was read.
			if _mute_button != null:
				_mute_button.refresh()
		if settings.has("group_drag"):
			_group_drag = bool(settings["group_drag"])
			if _group_drag_button != null:
				_group_drag_button.set_on(_group_drag)
		if settings.has("palette_themes") \
				and typeof(settings["palette_themes"]) == TYPE_ARRAY:
			var list: Array = settings["palette_themes"]
			for i in mini(list.size(), _palette.slot_count()):
				_palette.set_slot_theme(i,
					clampi(int(list[i]), 0, DiceTheme.count() - 1))
		# Silent: the drawers are being put back where they were, not opened by a player,
		# and a game that chimes twice on startup is a game with a bug.
		if settings.has("palette_open"):
			_palette.set_drawer_open(bool(settings["palette_open"]), false)
		if settings.has("list_open"):
			_dice_hud.set_open(bool(settings["list_open"]), false)

	var entries = data.get("dice")
	if typeof(entries) != TYPE_ARRAY:
		return

	_clear_board_silently()

	# Nulls are kept in place so the partner indices still line up.
	var restored: Array = []
	for item in entries:
		restored.append(_restore_die(item) if typeof(item) == TYPE_DICTIONARY else null)

	for i in entries.size():
		if typeof(entries[i]) != TYPE_DICTIONARY or restored[i] == null:
			continue
		var entry: Dictionary = entries[i]
		if not entry.has("partner"):
			continue
		var other := int(entry["partner"])
		if other < 0 or other >= restored.size() or restored[other] == null:
			continue
		# Both sides together, and only if the pair is still a legal one — the save may
		# predate a change to what can be linked.
		if Dice.can_pair(restored[i], restored[other]):
			restored[i].partner = restored[other]
			restored[other].partner = restored[i]
	for die in _dice:
		_dice_hud.update_value(die, die.value)

	if _dice.size() > 0:
		_select_only(_dice[0])


## One die from its saved entry, put down rather than played in: no arrival animation and
## no spawn sound, because eight dice popping and chiming at startup is a mess.
func _restore_die(entry: Dictionary) -> Dice:
	var scene := _scene_at_path(str(entry.get("scene", "")))
	if scene == null:
		return null

	var die: Dice = scene.instantiate()
	add_child(die)
	die.theme = clampi(int(entry.get("theme", DiceTheme.BONE)), 0,
		DiceTheme.count() - 1)
	die.global_position = _clamp_into(_origin_bounds_for(die), Vector2(
		float(entry.get("x", _spawn_position.x)),
		float(entry.get("y", _spawn_position.y))))
	die.rotation = float(entry.get("rot", 0.0))
	_register_die(die)

	die.place_on_face(clampi(int(entry.get("face", 1)), 1, die.face_count))
	return die


## The scene at this path, loaded now if it has not been already, or null if the path is
## not one of the pack's.
##
## The check is what stops a hand-edited save naming an arbitrary resource, and it is also
## what makes loading here safe: only eight paths can ever reach load().
func _scene_at_path(path: String) -> PackedScene:
	if path.is_empty():
		return null
	if _loaded_scenes.has(path):
		return _loaded_scenes[path]
	if _pack_index_of(path) < 0:
		return null
	var loaded: PackedScene = load(path)
	if loaded != null:
		_loaded_scenes[path] = loaded
	return loaded


## Read the pack manifest: which dice there are, what they are called, and where their
## icons sit in the sheet. Generated by tools/dice-render/make_icons.py.
func _load_pack() -> void:
	pack.clear()
	_pack_offsets.clear()
	_pack_scales.clear()
	var file := FileAccess.open(pack_path, FileAccess.READ)
	if file == null:
		push_error("no die pack at %s; the palette will be empty" % pack_path)
		return
	var parsed = JSON.parse_string(file.get_as_text())
	file.close()
	if typeof(parsed) != TYPE_ARRAY:
		push_error("%s is not a JSON array" % pack_path)
		return
	for item in parsed:
		if typeof(item) != TYPE_DICTIONARY:
			continue
		if not item.has("scene") or not item.has("icon") \
				or typeof(item["icon"]) != TYPE_ARRAY:
			continue
		var box: Array = item["icon"]
		if box.size() < 4:
			continue
		pack.append({
			"scene": str(item["scene"]),
			"name": str(item.get("name", "die")),
			"icon": Rect2(float(box[0]), float(box[1]), float(box[2]), float(box[3])),
		})

		var offset := Vector2.ZERO
		if item.has("offset") and typeof(item["offset"]) == TYPE_ARRAY:
			var pair: Array = item["offset"]
			if pair.size() >= 2:
				offset = Vector2(float(pair[0]), float(pair[1]))
		_pack_offsets.append(offset)
		_pack_scales.append(float(item.get("scale", 1.0)))


## Empty the board without the sounds or the animations — this is a board being replaced
## before anyone has seen it, not dice being deleted.
func _clear_board_silently() -> void:
	_cancel_drag()
	for die in _dice.duplicate():
		if die.partner != null and is_instance_valid(die.partner):
			die.partner.partner = null
		die.partner = null
		_dice.erase(die)
		_selected_dice.erase(die)
		_dice_hud.remove_die(die)
		die.set_hovered(false)
		die.queue_free()
	_active_die = null


## Roll one die where it stands — the menu's first item, and the R key.
##
## The only two ways a roll is asked for rather than caused. A collision re-roll and a
## throw both start one too, and neither goes through here: they already make a noise of
## their own, and a rattle on top of a clack is just a louder clack.
func _roll_one(die: Dice) -> void:
	if die == null or not is_instance_valid(die):
		return
	die.roll(true)
	Sfx.play("die_roll", 0.0, 0.06)


## A colour scheme was picked. What it lands on depends on where the menu was opened: a
## slot in the palette repaints that slot and everything made from it afterwards, a die on
## the board repaints only itself.
##
## The menu does not decide this — it reports the choice and the board applies it, which
## is the same split every other item in that panel already uses.
func _on_theme_chosen(theme: int) -> void:
	if _dice_menu.palette_slot >= 0:
		_palette.set_slot_theme(_dice_menu.palette_slot, theme)
		return

	var die := _dice_menu.target
	if die == null or not is_instance_valid(die):
		return
	die.theme = theme
	Sfx.play("theme")
	# Both halves of a d100 together: they are read as one die, so a pair wearing two
	# colours would be a stranger thing than the pair not matching the board.
	if die.partner != null and is_instance_valid(die.partner):
		die.partner.theme = theme


## Scatter every die across the board and roll it — the space bar, and the board menu's
## first item.
func throw_all_dice() -> void:
	_cancel_drag()
	for die in _dice:
		die.throw()
	if _dice.size() > 0:
		Sfx.throw_all()
		_start_shake()


## Knock the board, as though the table had been thumped.
##
## The *view* moves, not the board: the dice are children of this node, so shifting it
## would teleport eight rigid bodies every frame — which reads as a hard contact, starts
## collision re-rolls and can push a die through a wall. Setting the viewport's canvas
## transform moves what is drawn and leaves the physics world exactly where it was. The
## HUD, palette and menus sit on a CanvasLayer, which has its own transform and does not
## follow, so only the board shudders.
func _start_shake() -> void:
	_shake_left = shake_duration
	var angle := randf() * TAU
	_shake_axis = Vector2(cos(angle), sin(angle))
	_update_processing()


## One decaying oscillation along a fixed direction, rather than a fresh random offset
## each frame: a thump that rings down, where per-frame noise reads as the picture being
## broken.
func _step_shake(delta: float) -> void:
	_shake_left -= delta
	if _shake_left <= 0.0:
		_shake_left = 0.0
		get_viewport().canvas_transform = Transform2D.IDENTITY
		_update_processing()
		return

	var remaining := _shake_left / shake_duration      # 1 at the thump, 0 at rest
	var amplitude := shake_strength * remaining * remaining
	var phase := (shake_duration - _shake_left) * shake_frequency
	get_viewport().canvas_transform = \
		Transform2D(0.0, _shake_axis * amplitude * sin(phase))


## _process runs only when something needs it: a copy riding the cursor, or a shake
## running down.
func _update_processing() -> void:
	set_process(_copy_preview != null or _shake_left > 0.0)


func delete_all_dice() -> void:
	_cancel_drag()
	for die in _dice.duplicate():
		delete_die(die)


# dice_rolled passes (result); bind() appends the die.
func _on_dice_rolled(result: int, die: Dice) -> void:
	# `result` is the face; what the HUD wants is what that face is worth, which differs
	# only on the percentile d10.
	_dice_hud.update_value(die, die.value)
	print("Rolled: %d" % result)


func _on_body_exited(body: Node2D) -> void:
	if body is Dice and is_instance_valid(body) and not _deleting_dice.has(body):
		_reset_exited_die.call_deferred(body)


func _reset_exited_die(die: Dice) -> void:
	if is_instance_valid(die):
		_reset_die(die, _spawn_position)
