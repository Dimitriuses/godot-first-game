extends Node

## Does the port make a noise? Headless, with the dummy driver, which runs
## AudioStreamPlayer properly: `playing` goes true on play() and back to false when the
## clip ends, and stream.resource_path says which clip a voice holds.
##
## The point is to tell a *port* fault from a *browser* fault. If this passes and the
## deployed build is silent, the port is right and the platform is the problem.

var failures := 0


func _check(what: String, ok: bool, detail: String) -> void:
	print("  %s  %-44s%s" % ["PASS" if ok else "FAIL", what, detail])
	if not ok:
		failures += 1


func _ready() -> void:
	for i in 4:
		await get_tree().physics_frame

	var sfx := Sfx.new()
	sfx.name = "Sfx"
	add_child(sfx)
	for i in 4:
		await get_tree().physics_frame

	_check("the Sfx bus exists", AudioServer.get_bus_index(Sfx.BUS_NAME) >= 0,
		"index %d of %d" % [AudioServer.get_bus_index(Sfx.BUS_NAME),
			AudioServer.bus_count])
	_check("it is mixed at the exported level",
		is_equal_approx(AudioServer.get_bus_volume_db(
			AudioServer.get_bus_index(Sfx.BUS_NAME)), sfx.volume_db),
		"%.1f dB" % AudioServer.get_bus_volume_db(
			AudioServer.get_bus_index(Sfx.BUS_NAME)))
	_check("the voice pool was built", sfx._voices.size() == Sfx.VOICES,
		"%d voices" % sfx._voices.size())

	# Every clip the game asks for by name, loaded the way Sfx loads them.
	var names := ["ui_click", "ui_open", "ui_close", "spawn", "delete", "respawn",
		"link", "unlink", "theme", "die_roll", "die_land", "die_throw", "throw_all",
		"die_hit_0", "die_hit_1", "die_hit_2", "die_hit_3"]
	var missing := []
	for n in names:
		if sfx._stream_for(n) == null:
			missing.append(n)
	_check("every named clip loads", missing.is_empty(),
		"%d clips, missing: %s" % [names.size(), str(missing)])

	Sfx.play("ui_click")
	await get_tree().physics_frame
	var playing := []
	for v in sfx._voices:
		if v.playing:
			playing.append(v.stream.resource_path.get_file())
	_check("play() puts a clip on a voice", playing.size() == 1,
		str(playing))

	# die_hit is the one with a threshold and a rate limit. Assert which clip is on a
	# voice rather than how many voices are busy — whether the ui_click above is still
	# sounding depends on how fast the run got here, and counting it cost two runs.
	Sfx.die_hit(5.0)
	await get_tree().physics_frame
	var soft := ""
	for v in sfx._voices:
		if v.playing and v.stream.resource_path.get_file().begins_with("die_hit"):
			soft = v.stream.resource_path.get_file()
	_check("a gentle touch is dropped below the threshold", soft.is_empty(),
		"nothing" if soft.is_empty() else soft)

	# Which clip is on a voice, not how many voices are busy: the ui_click above is a
	# short sample and whether it is still sounding depends on how fast the run got here.
	Sfx.die_hit(300.0)
	await get_tree().physics_frame
	var hit := ""
	for v in sfx._voices:
		if v.playing and v.stream.resource_path.get_file().begins_with("die_hit"):
			hit = v.stream.resource_path.get_file()
	_check("a hard hit sounds", not hit.is_empty(), hit if hit else "nothing playing")

	Sfx.set_muted(true)
	_check("muting mutes the bus, not the calls",
		AudioServer.is_bus_mute(AudioServer.get_bus_index(Sfx.BUS_NAME))
			and Sfx.muted(), "bus muted")
	Sfx.set_muted(false)

	print("\n%s" % ("all checks passed" if failures == 0 else "%d FAILED" % failures))
	get_tree().quit(0 if failures == 0 else 1)
