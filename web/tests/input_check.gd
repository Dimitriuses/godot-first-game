extends Node

## The input paths that the deployed build reported broken: hover, the R and C keys, and
## the double tap.
##
## All three are synthesisable headless, which makes them worth pinning down here before
## blaming the browser. Hovering a *die* needs real physics picking and cannot be driven
## headless (see CLAUDE.md), but the signal it raises can — and what the keys read is the
## result of that signal, not the picking, so the key path is fully testable either way.

var failures := 0


func _check(what: String, ok: bool, detail: String) -> void:
	print("  %s  %-46s%s" % ["PASS" if ok else "FAIL", what, detail])
	if not ok:
		failures += 1


func _frames(n: int) -> void:
	for i in n:
		await get_tree().physics_frame


func _key(code: Key, pressed := true) -> void:
	var event := InputEventKey.new()
	event.keycode = code
	event.physical_keycode = code
	event.pressed = pressed
	get_viewport().push_input(event, true)


func _ready() -> void:
	await _frames(4)

	var game: GameManager = load("res://scenes/game.tscn").instantiate()
	game.persist_board = false
	add_child(game)
	await _frames(30)

	var layer := game.get_node("GameUiLayer")
	var menu: DiceMenu = layer.get_node("DiceMenu")
	var die: Dice = game._dice[0]

	# --- hover ------------------------------------------------------------------
	# The picking cannot be driven headless, but everything downstream of it can: the
	# board listens to the die's own mouse_entered, so raising it is exactly what picking
	# would do.
	die.mouse_entered.emit()
	await _frames(1)
	_check("mouse_entered sets the board's hovered die",
		game._hovered_die == die, str(game._hovered_die))

	die.mouse_exited.emit()
	await _frames(1)
	_check("mouse_exited clears it", game._hovered_die == null,
		str(game._hovered_die))

	# --- the R key --------------------------------------------------------------
	die.mouse_entered.emit()
	await _frames(1)
	var was_rolling := die.is_rolling
	_key(KEY_R)
	await _frames(2)
	_check("R rolls the hovered die", die.is_rolling and not was_rolling,
		"rolling %s -> %s" % [was_rolling, die.is_rolling])

	# Let the roll finish so the copy test starts from a settled die.
	for i in 400:
		if not die.is_rolling:
			break
		await get_tree().process_frame

	# --- the C key --------------------------------------------------------------
	die.mouse_entered.emit()
	await _frames(1)
	_key(KEY_C)
	await _frames(2)
	_check("C starts a copy on the hovered die",
		not game._pending_copy_scene.is_empty(), game._pending_copy_scene)
	_key(KEY_ESCAPE)
	await _frames(2)
	_check("Escape cancels the pending copy",
		game._pending_copy_scene.is_empty(), "cleared")

	# --- the double tap ---------------------------------------------------------
	# What a touchscreen has instead of a right button. Godot reports the touch itself and
	# also emulates a mouse from it; the board reads the touch and swallows the emulated
	# press.
	menu.close()
	await _frames(1)
	var tap := InputEventScreenTouch.new()
	tap.pressed = true
	tap.double_tap = true
	tap.position = Vector2(600, 400)      # empty board, so the board menu
	get_viewport().push_input(tap, true)
	await _frames(2)
	_check("a double tap on the board opens the board menu",
		menu.is_open() and menu.target == null,
		"open=%s target=%s" % [menu.is_open(), menu.target])
	_check("...and arms the swallow for the emulated press",
		game._swallow_next_press, str(game._swallow_next_press))

	menu.close()
	await _frames(1)
	# On the die this time. The die is at a known place, so the tap is aimed at it.
	var at: Vector2 = die.global_position + die.collision_offset
	var tap2 := InputEventScreenTouch.new()
	tap2.pressed = true
	tap2.double_tap = true
	tap2.position = at
	get_viewport().push_input(tap2, true)
	await _frames(2)
	_check("a double tap on a die opens the die menu",
		menu.is_open() and menu.target == die,
		"open=%s target=%s at %s" % [menu.is_open(), menu.target, at])

	# --- the double tap, without the platform's flag -----------------------------
	# This is the case the browser actually presents: web reports the touch but never
	# fills in double_tap, so the first deploy had no context menu on a phone at all.
	menu.close()
	game._last_tap_ms = 0
	await _frames(1)
	for i in 2:
		var plain := InputEventScreenTouch.new()
		plain.pressed = true
		plain.double_tap = false
		plain.position = Vector2(620, 420)
		get_viewport().push_input(plain, true)
		await _frames(1)
	_check("two quick taps open the menu without double_tap",
		menu.is_open(), "open=%s" % menu.is_open())

	# ...and two taps far apart do not.
	menu.close()
	game._last_tap_ms = 0
	await _frames(1)
	for spot in [Vector2(300, 200), Vector2(800, 500)]:
		var far := InputEventScreenTouch.new()
		far.pressed = true
		far.double_tap = false
		far.position = spot
		get_viewport().push_input(far, true)
		await _frames(1)
	_check("two taps far apart are not a double tap",
		not menu.is_open(), "open=%s" % menu.is_open())

	print("\n%s" % ("all checks passed" if failures == 0 else "%d FAILED" % failures))
	get_tree().quit(0 if failures == 0 else 1)
