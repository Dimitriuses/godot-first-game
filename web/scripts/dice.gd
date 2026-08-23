class_name Dice
extends RigidBody2D

## A die, and the small state machine that drives its sprite.
##
## Hand port of scripts/Dice.cs (ROADMAP 9a). The C# tree is canonical: when the two
## disagree, the C# one is right and this one is stale. Structure, order of operations
## and comments are kept deliberately close to it so the two can be read side by side —
## this is not the place to improve on the original.
##
## Three visual states, and every transition sets the sprite explicitly, so nothing
## ever has to poll for or repair a stalled animation:
##
##   Resting  one static frame — the last frame of `_current_result`'s clip, which is
##            the die sitting still showing that face.
##   Held     idle0 / idle1 loop while the player agitates it. These loops play *only*
##            while the die is held, so a free die is never left looping.
##   Rolling  one of the numbered clips. It ends on its own last frame, which is the
##            resting pose, and emits `dice_rolled`.
##
## The number is not a bare random draw. It is taken from **where the die was in its
## tumble at the moment it was let go**, nudged by a random factor — see
## `_choose_result`. The roll clip also picks up from that same frame, so the spin the
## player was watching carries into the throw instead of cutting.

const IDLE0 := &"idle0"
const IDLE1 := &"idle1"

## How long an arrival and a departure take.
##
## Longer than they look like they should be. Back easing is heavily front-loaded — at
## a fifth of the way through, a Back-out is already two thirds of the way up — so 0.3s
## read as an instant pop with the overshoot invisible.
const APPEAR_SECONDS := 0.42
const VANISH_SECONDS := 0.3

## The scale an arrival starts from. Not zero: a zero-scaled sprite has no size for the
## tween to work back from.
const APPEAR_FROM := 0.02

signal dice_rolled(result: int)

@export var animated_sprite: AnimatedSprite2D
@export var collision_shape: CollisionShape2D

## Relative impact speed at which one die knocks another into a roll.
@export var collision_roll_speed := 140.0
## Speed a release must exceed to roll even if the die never span up.
@export var release_roll_speed := 180.0

## How far the random factor may move the face away from the one the release frame
## picked. 0 makes the throw fully aimable; 3 makes the frame irrelevant.
@export var result_jitter := 2

## How many frames of a landing clip cover one whole tumble — the same motion the idle
## loop shows. roll() resumes the landing at the point the idle had reached, so it has
## to know how the two line up.
##
## It used to need no telling: both clips were rendered at 30 fps from one motion, so
## roll frame i simply *was* idle frame i and the index carried straight over. Thinning
## the landing clips broke that identity, because the plan takes most of its saving out
## of the blur — the thirty frames of tumble now survive as sixteen. Left at the idle
## length it is the old 1:1, which is what an undecimated render still wants.
@export var tumble_frames := 30

## What one face is worth when the die reports its result. 10 on a percentile d10,
## which is printed in tens and whose "00" face is the tenth; 1 on everything else. The
## stored result stays 1..face_count either way, because that is what names the
## animation clips — this only changes the number the HUD is told.
@export var value_step := 1

## What the palette calls this die. Empty means "work it out from the face count",
## which is right for every die until two of them have the same one -- a pipped and a
## numbered d6 would both come out "d6".
@export var die_label := ""

## How hard a held die must be moved to spin it up to idle0, then to idle1.
@export var spin_on_speed := 120.0
@export var fast_spin_on_speed := 600.0
## The same, for a die swung hard enough to rotate about the mouse pin.
@export var spin_on_angular := 1.5
@export var fast_spin_on_angular := 8.0

## Impulse throw() gives a die, so the Space key scatters them across the board rather
## than animating them on the spot.
@export var throw_speed_min := 220.0
@export var throw_speed_max := 400.0
@export var throw_spin_max := 9.0

## The die this one is read together with, or null. A percentile d10 and a plain one
## make a d100: the first is the tens, the second the units, and the pair reads 1 to 100
## rather than either die's own number.
##
## Held here so anything showing a die can see it, but maintained by GameManager —
## pairing is a board operation, and both sides have to be set and cleared together.
var partner: Dice = null

var _theme_index := DiceTheme.BONE
var _hover_scale := 1.0
var _effect_scale := 1.0
var _scale_tween: Tween = null
var _current_result := 1
var _face_count := 0            # 0 until first read; see face_count
var _is_held := false
var _is_rolling := false
var _spin_level := 0            # 0 resting, 1 idle0, 2 idle1
var _move_speed := 0.0
var _last_velocity := Vector2.ZERO
var _last_position := Vector2.ZERO
var _teleport_to := Vector2.ZERO
var _has_teleport := false
var _last_collision_roll_ms := 0


## The colour scheme this die is wearing, as an index into DiceTheme.
##
## Lives on the sprite as a material rather than anywhere in this class's state machine,
## because it changes nothing about how the die behaves — the same clips play, the same
## face comes up, and the frame the result is read from is the frame it always was. A
## theme is only ever how the die is painted.
var theme: int:
	get:
		return _theme_index
	set(value):
		_theme_index = value
		_refresh_theme_material()

## How fast the die was travelling over the previous physics step, as a vector.
##
## Not the same thing as `linear_velocity` when a collision is being reported:
## `body_entered` is emitted after the step has already resolved the contact, so by then
## the die has been slowed by the very impact being described. A die arriving at a wall
## at 95 px/s reports 34. This is what it was doing on the way in.
var approach_velocity: Vector2:
	get:
		return _last_velocity

var is_held: bool:
	get:
		return _is_held

var is_rolling: bool:
	get:
		return _is_rolling

## What that face is worth — what a player reads off the die. The same number for every
## die but the percentile d10, which shows 10 through 90 and then 00 for 100.
var value: int:
	get:
		return _current_result * value_step

## What to call this die anywhere it is named — the palette, the die list, the
## right-click menu. Its face count, unless two dice share one and it carries its own
## label to tell them apart.
var display_name: String:
	get:
		return "d%d" % face_count if die_label.is_empty() else die_label

## How many numbered faces this die has, counted from its own clips rather than
## exported, so a d20 scene needs no extra wiring. Resolved on first use, which may be
## before `_ready` — the palette reads it off a scene it has only instantiated.
var face_count: int:
	get:
		if _face_count <= 0:
			_face_count = _count_faces()
		return _face_count

## Whether this die supplies the tens of a pair. The percentile d10 is the one whose
## faces are worth ten apiece, which is exactly what `value_step` records.
var is_tens_die: bool:
	get:
		return face_count == 10 and value_step == 10

## Radius of the die's collider, and where that collider sits relative to the body
## origin — it is deliberately offset to line up with the drawn die. GameManager reads
## both to work out how far a dragged die may go before it touches a wall.
var collision_radius: float:
	get:
		if collision_shape != null and collision_shape.shape is CircleShape2D:
			return (collision_shape.shape as CircleShape2D).radius
		return 32.0

var collision_offset: Vector2:
	get:
		return collision_shape.position if collision_shape != null else Vector2.ZERO


func _ready() -> void:
	animated_sprite.animation_finished.connect(_on_animation_finished)
	contact_monitor = true
	max_contacts_reported = 4
	body_entered.connect(_on_body_entered)
	_last_position = global_position


## Which face is up: 1..face_count, and the name of that face's clip.
func get_result() -> int:
	return _current_result


## The die sitting still on a face, as a texture.
##
## The *last* frame of that face's clip, which is the resting pose — frame zero is the
## die still high in the air and blurred past recognition, which is what a palette icon
## or a drag ghost must not be.
func resting_frame(face: int) -> Texture2D:
	if animated_sprite == null or animated_sprite.sprite_frames == null:
		return null
	var frames := animated_sprite.sprite_frames
	var clip := StringName(str(face))
	if not frames.has_animation(clip):
		return null
	return frames.get_frame_texture(clip, frames.get_frame_count(clip) - 1)


## Whether two dice can be read as a d100: ten faces each, one counting in tens and the
## other in ones.
static func can_pair(a: Dice, b: Dice) -> bool:
	return (a != null and b != null and a != b
		and a.face_count == 10 and b.face_count == 10
		and a.is_tens_die != b.is_tens_die
		and (a.value_step == 1 or b.value_step == 1))


## What a linked pair reads, from 1 to 100.
##
## Both dice carry a ten on the face printed as a zero, so each is taken modulo its face
## count first: the percentile's tenth face is 00 and contributes no tens, the plain
## one's is 0 and contributes no units. Two zeroes is the one combination that cannot
## mean nothing, and by long convention it is a hundred.
static func pair_percent(a: Dice, b: Dice) -> int:
	if not can_pair(a, b):
		return 0
	var tens: Dice = a if a.is_tens_die else b
	var units: Dice = b if a.is_tens_die else a
	var total := tens.get_result() % tens.face_count * 10 \
		+ units.get_result() % units.face_count
	return 100 if total == 0 else total


## Put the die somewhere, immediately, without going through the solver.
##
## The engine's own answer to "how do I move a rigid body": the transform is applied
## from inside `_integrate_forces`, which is the one moment the physics server is
## willing to be told where a body is. Assigning `global_position` fights the solver,
## and a settled body ignores it.
func teleport_to(position: Vector2) -> void:
	_teleport_to = position
	_has_teleport = true
	# A sleeping body is never integrated, so it would never see the request.
	sleeping = false


func _integrate_forces(state: PhysicsDirectBodyState2D) -> void:
	if not _has_teleport:
		return
	state.transform = Transform2D(0.0, _teleport_to)
	state.linear_velocity = Vector2.ZERO
	state.angular_velocity = 0.0
	_has_teleport = false


func _physics_process(delta: float) -> void:
	# Keep the face upright while the body itself spins.
	animated_sprite.rotation = -rotation

	# Measured off the node rather than linear_velocity, because the two drag styles
	# move the die differently: the single drag pulls it with a pin and physics carries
	# it, while the Shift group drag freezes the body and moves it by hand — and a
	# frozen body reports no velocity at all.
	var position := global_position
	_last_velocity = (position - _last_position) / delta if delta > 0 else Vector2.ZERO
	_move_speed = _last_velocity.length()
	_last_position = position

	if _is_held:
		_update_held_spin()

	# Unconditional, not folded into _update_held_spin: that only runs while the die is
	# in hand, and letting go is exactly when the rainbow clip stops playing.
	_refresh_theme_material()


## Keep the sprite on the right material for the clip it is playing.
##
## `idle1` is the one clip rendered through a rainbow rather than the grey palette, and
## a themed die has to correct for that; the shader is told which case it is in rather
## than guessing per pixel. Cheap enough to check every frame — it is a reference
## comparison against a material that is shared by every die wearing the same theme.
func _refresh_theme_material() -> void:
	if animated_sprite == null:
		return
	var wanted := DiceTheme.material_for(_theme_index,
		animated_sprite.animation == IDLE1)
	if animated_sprite.material != wanted:
		animated_sprite.material = wanted


# ------------------------------------------------------------------------ dragging

func start_dragging() -> void:
	# Grabbing a die mid-roll settles it now, so the value the HUD reports always
	# matches the face left on screen.
	if _is_rolling:
		_finish_roll()

	_is_held = true
	_spin_level = 0
	_last_position = global_position
	_show_resting()


## release_speed is the speed the die is being let go at, in px/s.
func release_from_drag(release_speed: float) -> void:
	if not _is_held:
		return

	var spinning := _spin_level > 0 or release_speed >= release_roll_speed
	_clear_held()

	if spinning:
		roll()
	else:
		_show_resting()     # released without spinning: it just sits there


## Drops the die without rolling it — used when a drag is interrupted rather than
## finished (deleted, respawned, knocked out of bounds).
func cancel_dragging() -> void:
	if not _is_held:
		return

	_clear_held()
	_show_resting()


func _clear_held() -> void:
	_is_held = false
	_spin_level = 0


func _update_held_spin() -> void:
	var wanted := 0
	var spin := absf(angular_velocity)
	if _move_speed >= fast_spin_on_speed or spin >= fast_spin_on_angular:
		wanted = 2
	elif _move_speed >= spin_on_speed or spin >= spin_on_angular:
		wanted = 1

	# The level only ever ratchets up. Once the player has set the die going it keeps
	# tumbling in their hand until they let go, even if they then hold it perfectly
	# still — letting it fall back would park a spinning die on a static frame
	# mid-hold, which reads as the animation having broken.
	if wanted > _spin_level:
		_spin_level = wanted

	if _spin_level == 0:
		return          # never agitated yet: still on the resting frame

	var wanted_animation := IDLE1 if _spin_level == 2 else IDLE0
	if animated_sprite.animation != wanted_animation or not animated_sprite.is_playing():
		animated_sprite.play(wanted_animation)


# ------------------------------------------------------------------------- rolling

## Throw the die across the board and roll it. This is what the Space key does: the dice
## scatter as well as animating, rather than tumbling on the spot.
##
## A die already in the air is thrown again rather than skipped. Sharing roll()'s guard
## meant a board of dice split into two groups that could never be brought back
## together: every press threw whichever group was resting and passed over whichever was
## mid-clip, so they alternated forever. The guard is still right for a collision, which
## should not restart a roll that is already running; it is wrong for someone asking for
## a throw.
func throw() -> void:
	var angle := randf() * TAU
	var speed := randf() * (throw_speed_max - throw_speed_min) + throw_speed_min

	freeze = false
	linear_velocity = Vector2(cos(angle), sin(angle)) * speed
	angular_velocity = (randf() * 2.0 - 1.0) * throw_spin_max
	roll(true)


## Play a roll. The face comes from where the die was in its tumble when this was
## called, plus a random nudge; the clip picks up from that same frame.
##
## `restart` rolls a die that is already rolling, from the top. Off by default, so a die
## knocked about mid-clip keeps the throw it is in the middle of; on for an explicit
## throw, which has to reach every die or the board drifts out of phase.
func roll(restart := false) -> void:
	if _is_rolling and not restart:
		return

	var release_pos := _current_idle_position()
	_current_result = _choose_result(release_pos)
	_clear_held()
	_is_rolling = true

	var clip := StringName(str(_current_result))
	var frames := animated_sprite.sprite_frames
	var start := 0
	if release_pos >= 0.0 and frames != null and frames.has_animation(clip):
		# release_pos is a position in the idle loop; scale it onto however many frames
		# of this clip cover the same tumble. Read the idle length off the clip rather
		# than assuming thirty, so a die rendered to another length still lands in its
		# own blur instead of part way through its settle.
		var idle_length := float(frames.get_frame_count(IDLE0)) \
			if frames.has_animation(IDLE0) else 30.0
		var tumble := mini(tumble_frames, frames.get_frame_count(clip))
		start = clampi(roundi(release_pos / idle_length * tumble),
			0, frames.get_frame_count(clip) - 1)

	# Play first, then place the frame: play() only rewinds when it actually changes
	# clip, so rolling the same number twice would otherwise resume wherever the
	# previous one left off.
	animated_sprite.speed_scale = 1.0
	animated_sprite.play(clip)
	animated_sprite.frame = start       # setting frame also clears frame_progress


## How far through the idle loop the tumble had got, or -1 if the die is not tumbling —
## a die at rest has no release moment to read.
##
## Continuous, not the bare frame index. With six faces over a thirty-frame loop each
## face owned exactly five frames, so the integer worked out even; twenty faces over
## thirty frames would give some faces two frames and some one, biasing the die 2:1.
## frame_progress makes the position continuous and the split even for any face count.
func _current_idle_position() -> float:
	var animation := animated_sprite.animation
	if animation != IDLE0 and animation != IDLE1:
		return -1.0
	return animated_sprite.frame + animated_sprite.frame_progress


## Pick the face. Where the tumble had got to when the die was let go chooses a base
## face — the idle loop covers every face across its length — and a random offset
## decides how close to that it actually lands, so the throw can be influenced but not
## aimed. A die that was not tumbling has no release moment to read and falls back to a
## plain draw.
func _choose_result(release_pos: float) -> int:
	var frames := animated_sprite.sprite_frames
	var idle_length := frames.get_frame_count(IDLE0) \
		if frames != null and frames.has_animation(IDLE0) else 0
	var faces := face_count

	var base_face := 0
	if release_pos >= 0.0 and idle_length > 0:
		base_face = clampi(int(release_pos * faces / idle_length), 0, faces - 1) + 1
	else:
		base_face = randi_range(1, faces)

	if result_jitter <= 0:
		return _wrap_face(base_face)

	return _wrap_face(base_face + randi_range(-result_jitter, result_jitter))


func _wrap_face(face: int) -> int:
	var n := face_count
	return ((face - 1) % n + n) % n + 1


func _on_animation_finished() -> void:
	# Only the numbered clips can reach this; idle0/idle1 loop instead.
	if _is_rolling:
		_finish_roll()


func _finish_roll() -> void:
	_is_rolling = false
	_show_resting()
	# Only here, not in place_on_face: that one also reports a result, but it is a
	# put-down rather than a landing and the screenshot tool leans on it.
	Sfx.play("die_land", 0.0, 0.06)
	dice_rolled.emit(_current_result)


# ---------------------------------------------------------------------- appearance

## Park the sprite on the resting pose: the roll clips end on the die sitting still, so
## that last frame *is* the idle picture for the current face.
func _show_resting() -> void:
	var clip := StringName(str(_current_result))
	var frames := animated_sprite.sprite_frames
	if frames == null or not frames.has_animation(clip):
		return

	# stop() rewinds to frame 0 and assigning animation does the same, so both have to
	# happen before the frame is placed.
	animated_sprite.stop()
	animated_sprite.animation = clip
	animated_sprite.frame = frames.get_frame_count(clip) - 1
	animated_sprite.frame_progress = 1.0


## Put the die down showing a chosen face, without rolling for it. This is how the
## screenshot tool gets a reproducible board; it is also what anything restoring a saved
## board would want.
func place_on_face(face: int) -> void:
	if face < 1 or face > face_count:
		return

	_current_result = face
	_is_rolling = false
	_clear_held()
	linear_velocity = Vector2.ZERO
	angular_velocity = 0.0
	_last_position = global_position
	animated_sprite.speed_scale = 1.0
	_show_resting()
	dice_rolled.emit(face)


func set_hovered(hovered: bool) -> void:
	_hover_scale = 1.12 if hovered else 1.0
	_apply_scale()
	animated_sprite.z_index = 20 if hovered else 0


## The scale an arriving or departing die is drawn at, on top of whatever the hover is
## doing.
##
## Two multipliers rather than one number, because both want the same property and
## neither knows about the other: pointing at a die mid-arrival used to snap it to full
## size, and letting go of it snapped it back to nothing.
##
## The sprite, never the body. Scaling a RigidBody2D scales its collision shape with it,
## so a die would arrive with a collider growing out of the floor.
var effect_scale: float:
	get:
		return _effect_scale
	set(value):
		_effect_scale = value
		_apply_scale()


func _apply_scale() -> void:
	animated_sprite.scale = Vector2.ONE * _hover_scale * _effect_scale


## A die arriving on the board: up from nothing, with a little overshoot at the end.
func appear(seconds := APPEAR_SECONDS) -> void:
	if _scale_tween != null:
		_scale_tween.kill()
	effect_scale = APPEAR_FROM
	modulate = Color(1.0, 1.0, 1.0, 0.0)
	_scale_tween = create_tween().set_parallel()
	# Back-out is the overshoot: it passes full size and comes back, which reads as the
	# die landing rather than as a window opening.
	_scale_tween.tween_method(_set_effect_scale, APPEAR_FROM, 1.0, seconds) \
		.set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	# The fade finishes first, well inside the overshoot, so what is seen bouncing is a
	# solid die and not a ghost of one.
	_scale_tween.tween_property(self, "modulate:a", 1.0, seconds * 0.45) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)


## A die leaving: down to nothing, and then gone.
##
## The die is already out of every list that matters by the time this is called — it is
## a picture from here on, so it is frozen and made deaf to the mouse and to contacts
## first. Turning contact_monitor off rather than clearing the collision layer is
## deliberate: the bounds Area2D finds bodies by layer, and clearing it fires
## body_exited and teleports the whole board back to the spawn point.
func vanish(seconds := VANISH_SECONDS) -> void:
	if _scale_tween != null:
		_scale_tween.kill()
	freeze = true
	input_pickable = false
	contact_monitor = false

	_scale_tween = create_tween().set_parallel()
	# Back-in is the mirror of the arrival: it dips outward first, so the die gathers
	# itself before it goes.
	_scale_tween.tween_method(_set_effect_scale, _effect_scale, APPEAR_FROM, seconds) \
		.set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_IN)
	_scale_tween.tween_property(self, "modulate:a", 0.0, seconds) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	_scale_tween.chain().tween_callback(queue_free)


## tween_method needs a Callable taking the value; a lambda would work but a named
## method keeps the two call sites above honest about what is being driven.
func _set_effect_scale(v: float) -> void:
	effect_scale = v


## Stop any arrival or departure and sit at full size.
##
## Called by whoever needs a die to be *finished*, which in practice is the screenshot
## tool: a die caught mid-bounce would make its output depend on how fast the machine
## ran. Deliberately not folded into place_on_face, which was where it lived at first —
## that method is about which face is up, and putting this in it meant a copy, which is
## placed on a face the moment it is made, never got to animate in at all.
func snap_scale() -> void:
	if _scale_tween != null:
		_scale_tween.kill()
	_scale_tween = null
	effect_scale = 1.0
	modulate = Color.WHITE


func _count_faces() -> int:
	if animated_sprite == null or animated_sprite.sprite_frames == null:
		return 6
	var frames := animated_sprite.sprite_frames

	# A Dictionary used as a set: GDScript has no HashSet.
	var numbered := {}
	for name in frames.get_animation_names():
		var text := String(name)
		if text.is_valid_int():
			numbered[int(text)] = true

	# They have to be 1..N with nothing missing, or the mapping from a release frame
	# onto a face would silently point at a clip that is not there.
	for i in range(1, numbered.size() + 1):
		if not numbered.has(i):
			push_warning("%s: numbered clips are not 1..%d" % [name, numbered.size()])
			return 6
	return numbered.size() if numbered.size() > 0 else 6


func _on_body_entered(body: Node) -> void:
	# Relative speed against another die, plain speed against a wall. Both are worth a
	# click; only a die is worth a re-roll, which is why the sound is settled first and
	# the early return kept below it.
	var other := body as Dice

	# Two different speeds on purpose. The sound wants how hard the die arrived, which
	# is the approach velocity; the re-roll threshold below has always been measured
	# against post-impact linear_velocity and is left reading exactly what it read
	# before, because changing it would change how the game plays.
	if not _is_held:
		if other != null:
			Sfx.die_hit((approach_velocity - other.approach_velocity).length())
		else:
			Sfx.die_hit(approach_velocity.length())

	if other == null:
		return

	var impact_speed := (linear_velocity - other.linear_velocity).length()
	var now := Time.get_ticks_msec()
	if (not _is_held and not _is_rolling and impact_speed >= collision_roll_speed
			and now - _last_collision_roll_ms >= 250):
		_last_collision_roll_ms = now
		roll()
