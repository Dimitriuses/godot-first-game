extends Node

var failures := 0

func _check(what: String, ok: bool, detail: String) -> void:
	print("  %s  %-46s%s" % ["PASS" if ok else "FAIL", what, detail])
	if not ok:
		failures += 1

func _frames(n: int) -> void:
	for i in n:
		await get_tree().physics_frame

func _ready() -> void:
	await _frames(4)
	var die: Dice = load("res://scenes/dice.tscn").instantiate()
	add_child(die)
	die.freeze = true
	await _frames(4)
	var frames := die.animated_sprite.sprite_frames

	print("tumble_frames = %d, roll clip = %d, idle = %d, face_count = %d\n" % [
		die.tumble_frames, frames.get_frame_count("1"),
		frames.get_frame_count("idle0"), die.face_count])

	_check("exports bound from the rewritten scene",
		die.animated_sprite != null and die.collision_shape != null,
		"sprite %s, collider %s" % [die.animated_sprite != null,
			die.collision_shape != null])
	_check("collision_offset read from the scene",
		die.collision_offset != Vector2.ZERO, str(die.collision_offset))
	_check("display_name", die.display_name == "d6", die.display_name)

	for at in [0, 7, 14, 21, 29]:
		die.animated_sprite.play("idle0")
		die.animated_sprite.pause()
		die.animated_sprite.frame = at
		die.roll(true)
		var start := die.animated_sprite.frame
		_check("release at idle %d starts in the tumble" % at,
			start <= die.tumble_frames,
			"frame %d of %d, %.2fs of roll left" % [start, die.tumble_frames,
				(frames.get_frame_count("1") - start) / 30.0])

	die.animated_sprite.play("1")
	die.animated_sprite.frame = frames.get_frame_count("1") - 1
	await _frames(1)
	die.roll(true)
	_check("a die at rest starts at frame 0", die.animated_sprite.frame == 0,
		"frame %d" % die.animated_sprite.frame)

	var rolled := [0]
	die.dice_rolled.connect(func(_r): rolled[0] += 1)
	die.animated_sprite.play("idle0")
	die.animated_sprite.pause()
	die.animated_sprite.frame = 20
	var began := Time.get_ticks_msec()
	die.roll(true)
	for i in 1200:
		if rolled[0] > 0:
			break
		await get_tree().process_frame
	_check("the roll finishes and reports once", rolled[0] == 1,
		"%d report(s) after %.2fs" % [rolled[0], (Time.get_ticks_msec() - began) / 1000.0])
	_check("it ends on the clip's last frame",
		die.animated_sprite.frame
			== frames.get_frame_count(die.animated_sprite.animation) - 1,
		"frame %d of %d" % [die.animated_sprite.frame,
			frames.get_frame_count(die.animated_sprite.animation)])

	var all_faces := true
	for f in range(1, die.face_count + 1):
		die.place_on_face(f)
		await _frames(1)
		if die.animated_sprite.animation != str(f) or die.animated_sprite.frame \
				!= frames.get_frame_count(str(f)) - 1:
			all_faces = false
	_check("place_on_face lands on every face's rest", all_faces,
		"%d faces" % die.face_count)

	_check("theme null for Bone", DiceTheme.material_for(DiceTheme.BONE) == null, "null")
	var m := DiceTheme.material_for(1)
	_check("theme 1 makes a shared ShaderMaterial",
		m != null and m == DiceTheme.material_for(1), DiceTheme.name_of(1))

	print("\n%s" % ("all checks passed" if failures == 0 else "%d FAILED" % failures))
	get_tree().quit(0 if failures == 0 else 1)
