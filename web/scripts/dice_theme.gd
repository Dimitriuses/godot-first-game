class_name DiceTheme
extends RefCounted

## The colour schemes a die can wear, and the materials that put them on.
##
## Hand port of scripts/DiceTheme.cs (ROADMAP 9a). The C# tree is canonical.
##
## A theme is four colours — a gradient from the deepest shade to the highlight,
## matching the four bands the artwork was toon-rendered with. Nothing is re-rendered
## and no extra images exist: `shaders/dice_theme.gdshader` reads each pixel's luminance
## and looks the colour up. That shader is shared verbatim between the two trees;
## `.gdshader` is language-agnostic and was the one part of this that cost nothing.
## See tools/theme-lab/README.md for why it has to work that way and what else was tried.
##
## One material per theme, shared by every die wearing it, because the uniforms are the
## same for all of them. That matters more than it looks *here in particular*: this is
## the tree that becomes the single-threaded web build, which has no background shader
## compilation, and sharing keeps it to one program however many dice are on the board.

## Bone. Not a colour scheme so much as the absence of one.
const BONE := 0

## The mode that ships. 2 is "body ramp": the glyphs and the outline are left exactly as
## rendered, so a die stays readable whatever its body becomes.
const BODY_RAMP_MODE := 2

## Stops run deepest shade -> highlight. Index 0 is the artwork as rendered, and is
## deliberately first: it is what a die is until someone chooses otherwise.
const NAMES: Array[String] = [
	"Bone", "Crimson", "Emerald", "Sapphire", "Amber", "Obsidian", "Ivory",
]

const STOPS: Array = [
	["8f92a4", "bdbecf", "d6dcea", "f1f0f7"],
	["5e1418", "9c2228", "cc3f42", "ef8f86"],
	["0d3a2b", "17694a", "2f9a6e", "8fd9b4"],
	["16244f", "27407f", "4568c4", "a8c2f2"],
	["5c3a08", "9c6b12", "d29a26", "f7dc94"],
	["07070b", "16161f", "2b2d3a", "555a6d"],
	["8a8172", "c0b6a2", "e3dccb", "fbf7ee"],
]

## Keyed by theme and by whether the die is playing the rainbow clip, flattened to one
## int because GDScript cannot key a Dictionary on a tuple. Two materials per theme
## rather than a uniform set per die, so they stay shared: the shader program is the
## same either way, only the value differs.
static var _materials := {}


static func count() -> int:
	return NAMES.size()


## The material for a theme, or null for Bone.
##
## Null rather than a material that happens to reproduce the artwork: a die with no
## material is the untouched original by construction, which is worth more than a close
## match and costs one less shader.
static func material_for(theme: int, rainbow := false) -> ShaderMaterial:
	if theme <= BONE or theme >= NAMES.size():
		return null

	var key := theme * 2 + (1 if rainbow else 0)
	if _materials.has(key):
		return _materials[key]

	var material := ShaderMaterial.new()
	material.shader = load("res://shaders/dice_theme.gdshader")
	material.set_shader_parameter("mode", BODY_RAMP_MODE)
	material.set_shader_parameter("ramp", _ramp(STOPS[theme]))
	material.set_shader_parameter("rainbow", 1.0 if rainbow else 0.0)
	_materials[key] = material
	return material


static func name_of(theme: int) -> String:
	return NAMES[theme] if theme >= 0 and theme < NAMES.size() else NAMES[BONE]


## One colour to stand for the theme in a menu. The third stop: the lightest one is
## nearly white on half the themes, which would make them hard to tell apart.
static func swatch(theme: int) -> Color:
	var i := theme if theme >= 0 and theme < NAMES.size() else BONE
	return Color(STOPS[i][2])


static func _ramp(stops: Array) -> GradientTexture1D:
	var gradient := Gradient.new()
	gradient.offsets = PackedFloat32Array()
	gradient.colors = PackedColorArray()
	for i in stops.size():
		gradient.add_point(float(i) / (stops.size() - 1), Color(stops[i]))

	var texture := GradientTexture1D.new()
	texture.gradient = gradient
	texture.width = 256
	return texture
