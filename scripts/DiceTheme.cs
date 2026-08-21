using Godot;
using System.Collections.Generic;

/// <summary>
/// The colour schemes a die can wear, and the materials that put them on.
///
/// A theme is four colours — a gradient from the deepest shade to the highlight, matching
/// the four bands the artwork was toon-rendered with. Nothing is re-rendered and no extra
/// images exist: <c>shaders/dice_theme.gdshader</c> reads each pixel's luminance and looks
/// the colour up. See <c>tools/theme-lab/README.md</c> for why it has to work that way and
/// what else was tried.
///
/// One material per theme, shared by every die wearing it, because the uniforms are the
/// same for all of them. That matters more than it looks: a single-threaded web build has
/// no background shader compilation, and sharing keeps it to one program however many dice
/// are on the board.
/// </summary>
public static class DiceTheme
{
	/// Stops run deepest shade -> highlight. Index 0 is the artwork as rendered, and is
	/// deliberately first: it is what a die is until someone chooses otherwise.
	public static readonly (string Name, string[] Stops)[] All =
	{
		("Bone",     new[] { "8f92a4", "bdbecf", "d6dcea", "f1f0f7" }),
		("Crimson",  new[] { "5e1418", "9c2228", "cc3f42", "ef8f86" }),
		("Emerald",  new[] { "0d3a2b", "17694a", "2f9a6e", "8fd9b4" }),
		("Sapphire", new[] { "16244f", "27407f", "4568c4", "a8c2f2" }),
		("Amber",    new[] { "5c3a08", "9c6b12", "d29a26", "f7dc94" }),
		("Obsidian", new[] { "07070b", "16161f", "2b2d3a", "555a6d" }),
		("Ivory",    new[] { "8a8172", "c0b6a2", "e3dccb", "fbf7ee" }),
	};

	/// Bone. Not a colour scheme so much as the absence of one.
	public const int Bone = 0;

	public static int Count => All.Length;

	/// The mode that ships. 2 is "body ramp": the glyphs and the outline are left exactly
	/// as rendered, so a die stays readable whatever its body becomes.
	private const int BodyRampMode = 2;

	/// Keyed by theme, and again by whether the die is playing the rainbow clip. Two
	/// materials per theme rather than a uniform set per die, so they stay shared: the
	/// shader program is the same either way, only the value differs.
	private static readonly Dictionary<(int, bool), ShaderMaterial> materials = new();

	/// <summary>
	/// The material for a theme, or null for Bone.
	///
	/// Null rather than a material that happens to reproduce the artwork: a die with no
	/// material is the untouched original by construction, which is worth more than a
	/// close match and costs one less shader.
	/// </summary>
	public static ShaderMaterial MaterialFor(int theme, bool rainbow = false)
	{
		if (theme <= Bone || theme >= All.Length)
			return null;
		if (materials.TryGetValue((theme, rainbow), out ShaderMaterial cached))
			return cached;

		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/dice_theme.gdshader")
		};
		material.SetShaderParameter("mode", BodyRampMode);
		material.SetShaderParameter("ramp", Ramp(All[theme].Stops));
		material.SetShaderParameter("rainbow", rainbow ? 1f : 0f);
		materials[(theme, rainbow)] = material;
		return material;
	}

	public static string NameOf(int theme) =>
		theme >= 0 && theme < All.Length ? All[theme].Name : All[Bone].Name;

	/// One colour to stand for the theme in a menu. The third stop: the lightest one is
	/// nearly white on half the themes, which would make them hard to tell apart.
	public static Color Swatch(int theme) =>
		new(All[theme >= 0 && theme < All.Length ? theme : Bone].Stops[2]);

	private static GradientTexture1D Ramp(string[] stops)
	{
		var gradient = new Gradient();
		gradient.Offsets = new float[stops.Length];
		gradient.Colors = new Color[stops.Length];
		for (int i = 0; i < stops.Length; i++)
		{
			gradient.SetOffset(i, (float)i / (stops.Length - 1));
			gradient.SetColor(i, new Color(stops[i]));
		}
		return new GradientTexture1D { Gradient = gradient, Width = 256 };
	}
}
