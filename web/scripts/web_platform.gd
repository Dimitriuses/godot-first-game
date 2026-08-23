class_name WebPlatform
extends Node

## The browser-shaped holes in Godot, read from the DOM.
##
## This file has **no counterpart in the C# tree**, and cannot have one: it exists
## precisely because the web platform does not implement things every other export does.
##
## **The work is not done here.** It is done by `web/head_include.html`, which the
## exporter folds into the page's `<head>` — and that placement is the entire point. The
## first version of this file installed the JavaScript itself, from `_ready`, and that is
## too late: Godot creates its `AudioContext` while the WebAssembly module starts up, so
## a constructor patch installed from GDScript never sees it. The build stayed silent and
## said so, in one line of console:
##
##     web platform glue: installed=true motion=false audio_unlocked=not yet
##
## What is left here is the reading half — a few properties a frame off a plain JS
## object, which is cheap, where `JavaScriptBridge.eval` per frame would not be.

## Accelerometer samples arrive here from the DOM. Zero until the browser starts
## reporting, which is also what `Input.get_accelerometer()` returns everywhere else — so
## a caller cannot tell the difference and does not have to.
var acceleration := Vector3.ZERO

## Whether the browser has actually delivered a motion sample. Distinct from "the vector
## is zero", which is also what a phone lying perfectly still on a table reads.
var motion_live := false

var _state: JavaScriptObject = null


static func is_web() -> bool:
	return OS.has_feature("web")


func _ready() -> void:
	if not is_web():
		set_process(false)
		return
	# Created by the head include, long before this runs. Null would mean the include
	# did not make it into the export, which is worth saying rather than working around.
	_state = JavaScriptBridge.get_interface("__diceWeb")
	if _state == null:
		push_warning("web/head_include.html did not run: no audio unlock, no motion")
		set_process(false)


func _process(_delta: float) -> void:
	if _state == null:
		return
	motion_live = int(_state.live) == 1
	if motion_live:
		acceleration = Vector3(float(_state.x), float(_state.y), float(_state.z))


## What the board should feed its ShakeGesture: the browser's samples where there are
## any, and Godot's own everywhere else. One call site, so GameManager does not have to
## know which platform it is on.
func read_accelerometer() -> Vector3:
	if motion_live:
		return acceleration
	return Input.get_accelerometer()


## A line for the browser console, so a build that misbehaves can be diagnosed without a
## rebuild. Printed once, from GameManager, and harmless anywhere else.
##
## `audio` turns 1 on the first gesture that finds a context to resume, so "not yet" at
## startup is expected and only means something once the page has been clicked.
func describe() -> String:
	if not is_web():
		return "web platform glue: not a web build, nothing installed"
	if _state == null:
		return "web platform glue: MISSING — head_include.html is not in the export"
	return ("web platform glue: contexts=%d audio_unlocked=%s motion=%s buses=%d"
		% [int(_state.ctxs.length), "yes" if int(_state.audio) == 1 else "not yet",
			motion_live, AudioServer.bus_count])
