using Godot;
using System.Threading.Tasks;

/// <summary>
/// Renders every mode of dice_theme.gdshader against every theme, so they can be
/// compared by eye rather than argued about. See the README beside this file.
///
///     godot --path . res://tools/theme-lab/theme_lab.tscn --quit-after 1500
///
/// Writes tools/theme-lab/out/*.png. Needs a real window: --headless has no renderer.
/// </summary>
public partial class ThemeLab : Node
{
	private const int Cell = 190;
	private const int LabelCol = 96;
	private const int HeadRow = 34;

	/// The mode that ships. Everything else is here to show why.
	private const int PreferredMode = 2;

	private static readonly string[] Modes =
		{ "0 original", "1 multiply", "2 body ramp", "3 body+glyph", "4 full ramp" };

	/// The shipping list, read from the game rather than copied: a lab that disagrees
	/// with what the player sees is worse than no lab. Tint and glyph exist only for
	/// modes 1 and 3, neither of which ships, so they are derived rather than authored.
	private static (string Name, string[] Stops) Theme(int i) => DiceTheme.All[i];

	private static Color Tint(int i) => DiceTheme.Swatch(i);

	/// Whatever contrasts with the body: mode 3 is about light glyphs on dark dice and
	/// dark ones on light dice, and picking by luminance says which this is.
	private static Color Glyph(int i)
	{
		Color mid = DiceTheme.Swatch(i);
		float l = 0.2126f * mid.R + 0.7152f * mid.G + 0.0722f * mid.B;
		return l < 0.45f ? new Color("eef1f8") : new Color("121212");
	}

	private Shader shader;
	private readonly string outDir = "res://tools/theme-lab/out";
	private (string Name, Texture2D Tex)[] subjects;

	public override async void _Ready()
	{
		shader = GD.Load<Shader>("res://shaders/dice_theme.gdshader");
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(outDir));
		subjects = Subjects();

		int wide = LabelCol + Mathf.Max(Modes.Length, subjects.Length) * Cell;
		int tall = HeadRow + Mathf.Max(DiceTheme.Count, subjects.Length) * Cell;
		DisplayServer.WindowSetSize(new Vector2I(wide, tall));
		await Frames(4);

		for (int t = 0; t < DiceTheme.Count; t++)
			await Comparison(t);

		await Gallery();
		await Rainbow();
		await Sweep("glyph_cut", new[] { 0.30f, 0.40f, 0.50f, 0.60f, 0.70f });
		await Sweep("outline_reach", new[] { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f });

		GD.Print("theme lab -> ", outDir);
		GetTree().Quit(0);
	}

	/// One theme, every mode.
	private async Task Comparison(int theme)
	{
		ColorRect page = Page();
		GradientTexture1D ramp = Ramp(Theme(theme).Stops);
		Text(page, Theme(theme).Name, new Vector2(6, 6), 17, new Color("ffe6a8"));
		for (int m = 0; m < Modes.Length; m++)
			Text(page, Modes[m], new Vector2(LabelCol + m * Cell + 6, 8), 15,
				new Color("e6e8f2"));

		for (int m = 0; m < Modes.Length; m++)
			for (int s = 0; s < subjects.Length; s++)
				Tile(page, m, s, subjects[s], ramp, Tint(theme), Glyph(theme), m);

		await Done(page, $"{outDir}/mode_{Theme(theme).Name.ToLower()}.png",
			LabelCol + Modes.Length * Cell, HeadRow + subjects.Length * Cell);
	}

	/// Every theme in the mode that ships — what the feature would actually look like.
	private async Task Gallery()
	{
		ColorRect page = Page();
		Text(page, $"mode {PreferredMode}", new Vector2(6, 6), 17, new Color("ffe6a8"));
		for (int s = 0; s < subjects.Length; s++)
			Text(page, subjects[s].Name, new Vector2(LabelCol + s * Cell + 6, 8), 15,
				new Color("e6e8f2"));

		for (int t = 0; t < DiceTheme.Count; t++)
		{
			GradientTexture1D ramp = Ramp(Theme(t).Stops);
			Text(page, Theme(t).Name, new Vector2(6, HeadRow + t * Cell + Cell / 2 - 8),
				14, new Color("c8cbd8"));
			for (int s = 0; s < subjects.Length; s++)
				Tile(page, s, t, subjects[s], ramp, Tint(t), Glyph(t), PreferredMode,
					false);
		}

		await Done(page, $"{outDir}/gallery.png",
			LabelCol + subjects.Length * Cell, HeadRow + DiceTheme.Count * Cell);
	}

	/// <summary>
	/// The one clip that is not greyscale, with and without the correction for it.
	///
	/// `idle1` is rendered through a sweeping rainbow rather than the grey palette, so a
	/// themed die playing it goes wrong twice: the body pulses as the hue sweeps, because
	/// luminance follows the tint, and the sub-cut pixels the body ramp deliberately keeps
	/// are dark *rainbow* rather than near-black, which leaves a coloured rim. The shader
	/// is told when it is on that clip; this page is what the telling is worth.
	/// </summary>
	private async Task Rainbow()
	{
		var d6 = GD.Load<PackedScene>("res://scenes/dice.tscn").Instantiate<Dice>();
		SpriteFrames frames = d6.AnimatedSprite.SpriteFrames;
		var shots = new (string Name, Texture2D Tex)[]
		{
			("idle1 f4", frames.GetFrameTexture("idle1", 4)),
			("idle1 f17", frames.GetFrameTexture("idle1", 17)),
		};
		d6.Free();

		string[] heads = { "f4  as-is", "f4  corrected", "f17  as-is", "f17  corrected" };
		ColorRect page = Page();
		Text(page, "idle1", new Vector2(6, 6), 17, new Color("ffe6a8"));
		for (int c = 0; c < heads.Length; c++)
			Text(page, heads[c], new Vector2(LabelCol + c * Cell + 6, 8), 15,
				new Color("e6e8f2"));

		for (int t = 0; t < DiceTheme.Count; t++)
		{
			GradientTexture1D ramp = Ramp(Theme(t).Stops);
			Text(page, Theme(t).Name, new Vector2(6, HeadRow + t * Cell + Cell / 2 - 8),
				14, new Color("c8cbd8"));
			for (int c = 0; c < heads.Length; c++)
			{
				Tile(page, c, t, shots[c / 2], ramp, Tint(t), Glyph(t), PreferredMode,
					false);
				var rect = (TextureRect)page.GetChild(page.GetChildCount() - 1);
				((ShaderMaterial)rect.Material)
					.SetShaderParameter("rainbow", c % 2 == 1 ? 1f : 0f);
			}
		}

		await Done(page, $"{outDir}/idle1.png",
			LabelCol + heads.Length * Cell, HeadRow + DiceTheme.Count * Cell);
	}

	/// <summary>
	/// One mode-3 parameter, swept, against the obsidian ramp — the theme that needs
	/// mode 3 at all. Both of its numbers were settled this way rather than by taste:
	/// outline_reach by how wide the composited outline actually is, glyph_cut by where
	/// glyphs stop filling in and body shading starts flipping.
	/// </summary>
	private async Task Sweep(string parameter, float[] values)
	{
		ColorRect page = Page();
		const int Obsidian = 5;
		GradientTexture1D ramp = Ramp(Theme(Obsidian).Stops);
		Text(page, parameter, new Vector2(6, 6), 16, new Color("ffe6a8"));
		for (int v = 0; v < values.Length; v++)
			Text(page, $"{values[v]:0.00}", new Vector2(LabelCol + v * Cell + 6, 8), 15,
				new Color("e6e8f2"));

		for (int v = 0; v < values.Length; v++)
			for (int s = 0; s < subjects.Length; s++)
			{
				Tile(page, v, s, subjects[s], ramp, Tint(Obsidian), Glyph(Obsidian), 3);
				var rect = (TextureRect)page.GetChild(page.GetChildCount() - 1);
				((ShaderMaterial)rect.Material).SetShaderParameter(parameter, values[v]);
			}

		await Done(page, $"{outDir}/sweep_{parameter}.png",
			LabelCol + values.Length * Cell, HeadRow + subjects.Length * Cell);
	}

	private ColorRect Page()
	{
		var page = new ColorRect
		{
			Color = new Color("454049"),
			Size = DisplayServer.WindowGetSize()
		};
		AddChild(page);
		return page;
	}

	private async Task Done(ColorRect page, string path, int w, int h)
	{
		await Frames(6);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image image = GetViewport().GetTexture().GetImage();
		image.GetRegion(new Rect2I(0, 0, Mathf.Min(w, image.GetWidth()),
			Mathf.Min(h, image.GetHeight()))).SavePng(path);
		GD.Print("  ", path);
		page.QueueFree();
		await Frames(2);
	}

	private static (string Name, Texture2D Tex)[] Subjects()
	{
		var d6 = GD.Load<PackedScene>("res://scenes/dice.tscn").Instantiate<Dice>();
		var d6n = GD.Load<PackedScene>("res://scenes/d6n.tscn").Instantiate<Dice>();
		var d20 = GD.Load<PackedScene>("res://scenes/d20.tscn").Instantiate<Dice>();
		SpriteFrames d6f = d6.AnimatedSprite.SpriteFrames;
		var found = new (string, Texture2D)[]
		{
			("d6 rest", d6.RestingFrame(6)),
			// Mid-clip, where the motion blur is worst. Any mode that only works on a
			// resting die is no use: a die spends its loudest three seconds like this.
			("d6 roll blur", d6f.GetFrameTexture("6", 34)),
			// The two spin loops a held die plays. idle1 is the fast one, and it is the
			// hardest thing in the pack for a recolour to survive.
			("d6 idle0", d6f.GetFrameTexture("idle0", 8)),
			("d6 idle1 f4", d6f.GetFrameTexture("idle1", 4)),
			("d6 idle1 f17", d6f.GetFrameTexture("idle1", 17)),
			("d20 rest", d20.RestingFrame(20)),
		};
		d6.Free();
		d6n.Free();
		d20.Free();
		return found;
	}

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

	private void Tile(Control page, int col, int row, (string Name, Texture2D Tex) subject,
		GradientTexture1D ramp, Color tint, Color glyph, int mode, bool label = true)
	{
		if (label && col == 0)
			Text(page, subject.Name,
				new Vector2(6, HeadRow + row * Cell + Cell / 2 - 8), 13,
				new Color("c8cbd8"));

		var material = new ShaderMaterial { Shader = shader };
		material.SetShaderParameter("mode", mode);
		material.SetShaderParameter("ramp", ramp);
		material.SetShaderParameter("tint", tint);
		material.SetShaderParameter("glyph_color", glyph);

		page.AddChild(new TextureRect
		{
			Texture = subject.Tex,
			Material = material,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Position = new Vector2(LabelCol + col * Cell, HeadRow + row * Cell),
			Size = new Vector2(Cell, Cell)
		});
	}

	private static void Text(Control page, string text, Vector2 at, int size, Color colour)
	{
		var label = new Label { Text = text, Position = at, Modulate = colour };
		label.AddThemeFontSizeOverride("font_size", size);
		page.AddChild(label);
	}

	private async Task Frames(int n)
	{
		for (int i = 0; i < n; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
}
