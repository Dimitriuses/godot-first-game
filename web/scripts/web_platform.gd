class_name WebPlatform
extends Node

## The browser-shaped holes in Godot, filled from the DOM.
##
## This file has **no counterpart in the C# tree**, and cannot have one: it exists
## precisely because the web platform does not implement things every other export does.
## ROADMAP 9b-touch describes the shake half; the audio half turned up on the first
## deploy. Everything here is guarded on `OS.has_feature("web")` and is inert anywhere
## else, so the GDScript tree still runs on a desktop unchanged.
##
## Two rules that shape all of it:
##
##   * **A browser only grants things from inside a user gesture.** Audio may not start
##     and motion permission may not be asked for except during a real click or touch.
##     So nothing here is done at startup; it is all armed by the first interaction.
##   * **`JavaScriptBridge` is single-threaded glue.** Reading a value per frame is fine;
##     `eval` per frame is not, so the shared state lives in a JS object read through
##     `get_interface`.

## Accelerometer samples arrive here from the DOM. Zero until the browser starts
## reporting, which is also what `Input.get_accelerometer()` returns everywhere else —
## so a caller cannot tell the difference and does not have to.
var acceleration := Vector3.ZERO

## Whether the browser has actually delivered a motion sample. Distinct from "the vector
## is zero", which is also what a phone lying perfectly still on a table reads.
var motion_live := false

var _shake_state: JavaScriptObject = null
var _armed := false


static func is_web() -> bool:
	return OS.has_feature("web")


func _ready() -> void:
	if not is_web():
		set_process(false)
		return
	_install()


## Everything that needs a user gesture, armed by the browser itself.
##
## The JS registers one-shot listeners rather than being called from GDScript on the first
## input, because the gesture has to be *the browser's own event* — by the time Godot has
## turned a touch into an InputEvent and run a frame, the user-activation window that
## `DeviceMotionEvent.requestPermission()` and `AudioContext.resume()` require may already
## have closed.
func _install() -> void:
	JavaScriptBridge.eval("""
		window.__diceWeb = window.__diceWeb || {x:0, y:0, z:0, live:0, audio:0};
		(function () {
			var w = window.__diceWeb;
			if (w.installed) return;
			w.installed = 1;

			function bindMotion() {
				window.addEventListener('devicemotion', function (e) {
					var a = e.accelerationIncludingGravity || e.acceleration;
					if (!a) return;
					w.x = a.x || 0; w.y = a.y || 0; w.z = a.z || 0;
					w.live = 1;
				});
			}

			function unlock() {
				// iOS 13+ refuses this anywhere but a user gesture, and refuses it
				// permanently if asked from the wrong place -- hence the listener.
				try {
					if (typeof DeviceMotionEvent !== 'undefined' &&
						typeof DeviceMotionEvent.requestPermission === 'function') {
						DeviceMotionEvent.requestPermission().then(function (state) {
							if (state === 'granted') bindMotion();
						}).catch(function () {});
					} else {
						bindMotion();
					}
				} catch (err) {}

				// Every AudioContext the page owns. Godot resumes its own on a gesture,
				// but a context created before the first interaction can be left
				// suspended, which is silence with nothing in the log.
				try {
					var ctxs = [];
					if (window.AudioContext || window.webkitAudioContext) {
						var C = window.AudioContext || window.webkitAudioContext;
						if (C.__diceCtxs) ctxs = C.__diceCtxs;
					}
					for (var i = 0; i < ctxs.length; i++) {
						if (ctxs[i].state === 'suspended') ctxs[i].resume();
					}
					w.audio = 1;
				} catch (err) {}
			}

			// Patch the constructor so every context Godot makes is reachable above.
			try {
				var C = window.AudioContext || window.webkitAudioContext;
				if (C && !C.__diceCtxs) {
					C.__diceCtxs = [];
					var Orig = C;
					var Patched = function () {
						var ctx = new (Function.prototype.bind.apply(
							Orig, [null].concat([].slice.call(arguments))))();
						Orig.__diceCtxs.push(ctx);
						return ctx;
					};
					Patched.prototype = Orig.prototype;
					Patched.__diceCtxs = Orig.__diceCtxs;
					window.AudioContext = Patched;
					if (window.webkitAudioContext) window.webkitAudioContext = Patched;
				}
			} catch (err) {}

			['click', 'touchend', 'keydown'].forEach(function (name) {
				window.addEventListener(name, unlock, {once: false, passive: true});
			});
		})();
	""", true)
	_shake_state = JavaScriptBridge.get_interface("__diceWeb")
	_armed = _shake_state != null


func _process(_delta: float) -> void:
	if not _armed:
		return
	# Reading three properties off a JS object, not an eval: cheap enough per frame.
	motion_live = int(_shake_state.live) == 1
	if motion_live:
		acceleration = Vector3(float(_shake_state.x), float(_shake_state.y),
			float(_shake_state.z))


## What the board should feed its ShakeGesture: the browser's samples where there are
## any, and Godot's own everywhere else. One call site, so GameManager does not have to
## know which platform it is on.
func read_accelerometer() -> Vector3:
	if motion_live:
		return acceleration
	return Input.get_accelerometer()


## A line for the browser console, so a silent build can be diagnosed without a rebuild.
## Printed once, from GameManager, and harmless anywhere else.
func describe() -> String:
	if not is_web():
		return "web platform glue: not a web build, nothing installed"
	return ("web platform glue: installed=%s motion=%s audio_unlocked=%s buses=%d"
		% [_armed, motion_live,
			"yes" if _armed and int(_shake_state.audio) == 1 else "not yet",
			AudioServer.bus_count])
