class_name Sfx
extends Node

## Every sound the game makes, and the small amount of policy that keeps them bearable.
##
## Hand port of scripts/Sfx.cs (ROADMAP 9a). The C# tree is canonical.
##
## The clips are generated, not recorded — see tools/audio-render/make_sounds.py, which
## synthesises all seventeen from nothing and is the only place their character is
## decided. Nothing here knows what a die sounds like; it knows how loud, how often and
## how many at once. Both trees load the same WAVs, so a difference in how the board
## sounds can only ever be this file.
##
## Reached through a static instance rather than passed down through the UI. That is not
## the pattern the rest of this project uses — panels raise signals and the board decides
## — but sound is the one thing every layer wants and no layer owns, and threading a
## reference through five classes to play a click would be worse than this. The instance
## is created and cleared by GameManager, and every entry point no-ops when it is
## missing, so nothing here can break a scene that has no audio in it.

## Its own bus, made at runtime so the project needs no bus layout resource. One place to
## put a volume slider when there is one.
const BUS_NAME := "Sfx"

## Enough for a board of dice rattling without cutting each other off. Voices are reused
## oldest-first once they are all busy.
const VOICES := 14

const AUDIO_DIR := "res://assets/audio/"

const QUIETEST_HIT := 40.0
const LOUDEST_HIT := 330.0
const HIT_VARIANTS := 4

## The shortest gap between two plays of the same sound. Without it a die skittering
## along a wall fires a clack a frame and it turns into a buzz.
const MIN_GAP_MS := {
	"die_hit": 40,
	"ui_click": 45,
	"die_land": 60,
	# R is a key someone will hold down. roll() restarts the clip each time, and without
	# this the rattle restarts with it, forty times a second.
	"die_roll": 220,
	# Mass operations go through the same call once per die. The gap is what makes
	# "Delete all" one sound rather than eight landing on the same frame.
	"delete": 130,
	"spawn": 90,
}

static var instance: Sfx = null

## Everything is mixed here rather than at each call site, so the relative levels baked
## into the WAVs are what actually reaches the speakers.
@export var volume_db := -7.0

var _voices: Array[AudioStreamPlayer] = []
var _streams := {}
var _last_played := {}
var _started_ms := {}
var _die_hits_muted_until := 0
var _hit_variant := 0
var _muted := false


func _ready() -> void:
	instance = self

	if AudioServer.get_bus_index(BUS_NAME) < 0:
		var index := AudioServer.bus_count
		AudioServer.add_bus(index)
		AudioServer.set_bus_name(index, BUS_NAME)
		AudioServer.set_bus_send(index, "Master")
	AudioServer.set_bus_volume_db(AudioServer.get_bus_index(BUS_NAME), volume_db)

	for i in VOICES:
		var player := AudioStreamPlayer.new()
		player.name = "Voice%d" % i
		player.bus = BUS_NAME
		add_child(player)
		_voices.append(player)
		_started_ms[player] = 0


func _exit_tree() -> void:
	if instance == self:
		instance = null


## Whether sound is off. False when there is no Sfx at all, because a scene with no audio
## in it is not muted — it is silent, which is a different thing to report.
static func muted() -> bool:
	return instance != null and instance._muted


## Turn everything off or on.
##
## Muting the bus rather than skipping the calls: voices carry on being allocated and
## recycled exactly as before, so unmuting cannot land in a state the mixer has not been
## keeping up to date, and a sound already sounding stops rather than finishing under a
## mute the player has just asked for.
static func set_muted(value: bool) -> void:
	if instance == null:
		return
	instance._muted = value
	AudioServer.set_bus_mute(AudioServer.get_bus_index(BUS_NAME), value)


## Play a clip by name, in dB relative to how it was mixed, with optional pitch scatter
## so repeats do not sound mechanical.
static func play(name: String, volume_db := 0.0, pitch_spread := 0.0) -> void:
	if instance != null:
		instance._play_now(name, volume_db, pitch_spread, name)


## A die struck something, at `speed` px/s.
##
## Loud in proportion, and quiet enough below a threshold to be dropped entirely — dice
## jostling as they settle would otherwise chatter for as long as the physics takes to go
## to sleep.
static func die_hit(speed: float) -> void:
	if instance == null or speed < QUIETEST_HIT:
		return
	if Time.get_ticks_msec() < instance._die_hits_muted_until:
		return

	var loudness := clampf((speed - QUIETEST_HIT) / (LOUDEST_HIT - QUIETEST_HIT),
		0.0, 1.0)
	# Compressive, not expansive. Measured rather than guessed: a die thrown at the
	# board's own throw_speed_max arrives at a wall doing 90 to 350 px/s, so the useful
	# range is narrow, and anything that curves the other way leaves every ordinary tap
	# inaudible.
	var db := lerpf(-18.0, 0.0, pow(loudness, 0.8))

	instance._hit_variant = (instance._hit_variant + 1) % HIT_VARIANTS
	# One shared gap key for all four variants: they are the same event, and letting each
	# keep its own let four clacks land on the same frame.
	instance._play_now("die_hit_%d" % instance._hit_variant, db, 0.12, "die_hit")


## The whole board going at once. It has its own clatter baked in, so the individual
## impacts are held off for as long as that lasts — and a die teleported by throw()
## registers a contact it did not really have (see CLAUDE.md), which would otherwise fire
## a clack for every die on the board in the same frame.
static func throw_all() -> void:
	if instance == null:
		return
	instance._die_hits_muted_until = Time.get_ticks_msec() + 260
	play("throw_all")


func _play_now(name: String, volume: float, pitch_spread: float, gap_key: String) -> void:
	var now := Time.get_ticks_msec()
	if MIN_GAP_MS.has(gap_key) and _last_played.has(gap_key) \
			and now - _last_played[gap_key] < MIN_GAP_MS[gap_key]:
		return

	var stream := _stream_for(name)
	if stream == null:
		return

	var voice := _free_voice()
	voice.stream = stream
	voice.volume_db = volume
	voice.pitch_scale = 1.0 + randf_range(-pitch_spread, pitch_spread) \
		if pitch_spread > 0.0 else 1.0
	voice.play()

	_last_played[gap_key] = now
	_started_ms[voice] = now


## A voice that is idle, or the one that has been going longest. Stealing is better than
## dropping: the sound that gets cut is the one already fading out.
func _free_voice() -> AudioStreamPlayer:
	var oldest := _voices[0]
	for voice in _voices:
		if not voice.playing:
			return voice
		if _started_ms[voice] < _started_ms[oldest]:
			oldest = voice
	return oldest


func _stream_for(name: String) -> AudioStream:
	if _streams.has(name):
		return _streams[name]
	var loaded: AudioStream = load("%s%s.wav" % [AUDIO_DIR, name])
	if loaded == null:
		push_warning("no sound called %s" % name)
	_streams[name] = loaded
	return loaded
