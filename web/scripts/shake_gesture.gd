class_name ShakeGesture
extends RefCounted

## Decides whether the device has just been shaken, from a stream of accelerometer
## samples. Nothing to do with the board's visual shudder — that is
## GameManager._start_shake, and this is a way of asking for it.
##
## Hand port of scripts/ShakeGesture.cs (ROADMAP 9a). The C# tree is canonical.
##
## A plain object rather than a node, and fed rather than polling, for two reasons. It
## can be tested by handing it a made-up sequence of samples, which a node reading
## `Input.get_accelerometer()` could not be. And the source of those samples differs per
## platform: an Android or iOS export has the accelerometer through Godot, while a web
## build has to be handed values from the browser's `devicemotion` event, because Godot's
## web platform does not implement the sensor APIs.
##
## **This tree is the web one, so that second case is not hypothetical here.** See
## ROADMAP 9b-touch: the browser feed has to be requested from inside a user gesture,
## because iOS 13+ refuses `DeviceMotionEvent.requestPermission()` anywhere else. None of
## that belongs in this class — it decides what a shake is, and it is fed by whoever can.

## How much sharper than gravity a jolt has to be, in m/s². About 2g: enough that putting
## a phone down on a table does not throw the dice.
var trigger := 19.0

## The quietest a shake may end before another one counts, so one wobble of a wrist is
## one throw rather than five.
var cooldown_seconds := 1.2

## Seconds for the running estimate of "which way is down" to catch up. Long enough that
## a shake does not become the baseline it is measured against.
const GRAVITY_CATCH_UP := 0.7

var _gravity := Vector3.ZERO
var _seeded := false
var _since_fired := INF


## Take one sample. True exactly once per shake.
##
## The first sample only seeds the baseline. Without that, a detector starting from zero
## sees the whole of gravity as a jolt and fires the moment it is switched on.
func feed(acceleration: Vector3, delta: float) -> bool:
	if delta <= 0:
		return false

	if not _seeded:
		_gravity = acceleration
		_seeded = true
		return false

	# A low pass on the raw reading is "down"; whatever is left is the shaking.
	var catch_up := minf(1.0, delta / GRAVITY_CATCH_UP)
	_gravity = _gravity.lerp(acceleration, catch_up)
	var jolt := (acceleration - _gravity).length()

	_since_fired += delta
	if jolt < trigger or _since_fired < cooldown_seconds:
		return false

	_since_fired = 0.0
	return true


## Forget everything. For a test, or for a device that has stopped reporting.
func reset() -> void:
	_seeded = false
	_since_fired = INF
