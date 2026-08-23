extends Node

## Does the whole board come up, in the GDScript tree?
##
## `dice_check.gd` proves one die; this proves the thing that builds everything else.
## `game.tscn` is the scene the web build boots into, so if this fails there is no demo —
## and it fails *silently* in the ways this port is most likely to go wrong. A signal
## connected to a renamed method, an export the rewriter missed, a `Sfx.play` that is now
## a parse-time error: none of them stop the scene loading, and several of them leave a
## board that looks right until it is touched.

var failures := 0


func _check(what: String, ok: bool, detail: String) -> void:
	print("  %s  %-44s%s" % ["PASS" if ok else "FAIL", what, detail])
	if not ok:
		failures += 1


func _frames(n: int) -> void:
	for i in n:
		await get_tree().physics_frame


func _ready() -> void:
	await _frames(4)

	var game: GameManager = load("res://scenes/game.tscn").instantiate()
	# Never let a harness read or overwrite the player's board.
	game.persist_board = false
	add_child(game)
	await _frames(30)

	_check("game.tscn instantiates and _ready runs", game != null, "GameManager")
	_check("the exports bound from the rewritten scene",
		game.mouse_pin != null and game.fake_body != null and game.dice_area != null,
		"pin %s, body %s, area %s" % [game.mouse_pin != null,
			game.fake_body != null, game.dice_area != null])

	_check("the pack manifest loaded", game.pack.size() == 8,
		"%d dice: %s" % [game.pack.size(),
			", ".join(game.pack.map(func(e): return e["name"]))])

	var bounds: Rect2 = game.board_bounds()
	_check("board bounds derived from the walls",
		bounds.size.x > 400 and bounds.size.y > 300,
		"%.0fx%.0f at %.0f,%.0f" % [bounds.size.x, bounds.size.y,
			bounds.position.x, bounds.position.y])

	# The panels are built in code, so a rename that broke one would show up here and
	# nowhere else until someone clicked it.
	var layer := game.get_node_or_null("GameUiLayer")
	_check("the UI layer and its panels were built", layer != null
		and layer.get_node_or_null("DicePalette") != null
		and layer.get_node_or_null("DiceHud") != null
		and layer.get_node_or_null("DiceMenu") != null
		and layer.get_node_or_null("MuteButton") != null
		and layer.get_node_or_null("GroupDragButton") != null,
		"%d children" % (layer.get_child_count() if layer != null else 0))
	_check("Sfx is up and reachable statically", Sfx.instance != null,
		"muted=%s" % Sfx.muted())

	# Synchronous, like the screenshot tool: the threaded path's timing would decide what
	# this sees.
	var before: int = game._dice.size()
	game.spawn_die_scene(load("res://scenes/dice.tscn"), Vector2(500, 300))
	await _frames(10)
	_check("a die spawns and registers", game._dice.size() == before + 1,
		"%d die/dice on the board" % game._dice.size())

	var die: Dice = game._dice[game._dice.size() - 1]
	_check("the spawned die sits where it was asked for",
		absf(die.global_position.x + die.collision_offset.x - 500.0) < 1.0,
		"x %.1f + offset %.1f" % [die.global_position.x, die.collision_offset.x])

	# The list is what most of the UI wiring runs through. game.tscn already holds one
	# die, so this is the second d6 and both should now carry a number — the rule is that
	# "#n" appears only once it means something, and a lone die is just "d6".
	var hud: DiceHud = layer.get_node("DiceHud")
	var labels: Array = game._dice.map(func(d): return hud.label_for(d))
	_check("the die list numbers dice per type, from one",
		labels == ["d6 #1", "d6 #2"], str(labels))

	# Throw all: the space bar's path, the shudder, and the sound gate.
	game.throw_all_dice()
	await _frames(2)
	_check("throw_all_dice rolls the board", die.is_rolling, "rolling=%s" % die.is_rolling)
	await _frames(4)
	_check("the shudder moves the view, not the board",
		get_viewport().canvas_transform != Transform2D.IDENTITY,
		str(get_viewport().canvas_transform.origin.round()))

	# ...and puts it back. shake_duration is 0.3s = 18 physics frames.
	await _frames(30)
	_check("the shudder resets the canvas transform",
		get_viewport().canvas_transform == Transform2D.IDENTITY,
		str(get_viewport().canvas_transform.origin))

	# Deleting goes through unlink, the HUD, and the vanish animation.
	game.delete_die(die)
	await _frames(4)
	_check("a deleted die leaves the board list", not game._dice.has(die),
		"%d left" % game._dice.size())

	# The save round-trip, in memory: this is the half that becomes IndexedDB on the web.
	game.spawn_die_scene(load("res://scenes/dice.tscn"), Vector2(400, 300))
	await _frames(6)
	var data := game._collect_save()
	_check("the board serialises", data.has("dice") and data.has("settings")
		and data["dice"].size() == game._dice.size(),
		"%d die entries, %d settings" % [data["dice"].size(), data["settings"].size()])
	_check("positions are rounded for the autosave compare",
		data["dice"][0]["x"] == roundf(data["dice"][0]["x"]),
		"x=%s" % data["dice"][0]["x"])

	print("\n%s" % ("all checks passed" if failures == 0 else "%d FAILED" % failures))
	get_tree().quit(0 if failures == 0 else 1)
