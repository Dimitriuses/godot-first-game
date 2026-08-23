using Godot;
using System;

/// <summary>
/// The look and the placement of the small square buttons in the top-right corner, in one
/// place so a second one cannot drift away from the first.
///
/// Their icons are drawn rather than written, because Godot's default font has no emoji
/// and a glyph it cannot render comes out as a blank box. Each button lays its drawing out
/// in a 24-unit square and scales it, so nothing has to know the button's size.
/// </summary>
public static class UiSkin
{
	public const float CornerButtonSize = 40f;
	private const float CornerMargin = 8f;
	private const float CornerGap = 6f;

	/// Slot 0 is the rightmost. Offsets are from the right edge, so they are negative.
	public static float CornerLeft(int slot) =>
		-(CornerMargin + (slot + 1) * CornerButtonSize + slot * CornerGap);

	public static float CornerRight(int slot) =>
		-(CornerMargin + slot * (CornerButtonSize + CornerGap));

	public static float CornerTop => CornerMargin;

	public static float CornerBottom => CornerMargin + CornerButtonSize;

	public static StyleBoxFlat Panel(string bg, string border)
	{
		var style = new StyleBoxFlat
		{
			BgColor = new Color(bg),
			BorderColor = new Color(border)
		};
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(9);
		return style;
	}

	/// A corner button, styled and placed. The caller adds whatever it draws inside.
	public static Button CornerButton(Control parent, string name, int slot)
	{
		var button = new Button
		{
			Name = name,
			FocusMode = Control.FocusModeEnum.None,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		button.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		button.OffsetLeft = CornerLeft(slot);
		button.OffsetRight = CornerRight(slot);
		button.OffsetTop = CornerTop;
		button.OffsetBottom = CornerBottom;
		button.AddThemeStyleboxOverride("normal", Panel("202536e8", "77819b"));
		button.AddThemeStyleboxOverride("hover", Panel("2d3450f0", "aab3c8"));
		button.AddThemeStyleboxOverride("pressed", Panel("171b28f0", "77819b"));
		parent.AddChild(button);
		return button;
	}

	/// The transform every corner icon draws through: a 24-unit square, centred.
	public static (float Unit, Vector2 Origin) IconFrame(Control icon)
	{
		float unit = icon.Size.X / 24f;
		return (unit, new Vector2(0f, (icon.Size.Y - 24f * unit) / 2f));
	}

	/// <summary>
	/// A drawing child filling its parent, for a button whose picture is drawn rather
	/// than typed.
	///
	/// **Every glyph is a font dependency, and the web build is where that bites.** The
	/// note elsewhere that `◀ ▶ ×` were safe was true of the desktop font and wrong of
	/// the browser: the first GitHub Pages deploy drew the palette's toggle as a blank
	/// box. Godot's fallback font in a web export carries a much smaller set than the
	/// desktop one, so anything past ASCII is a gamble that is not worth taking twice.
	/// </summary>
	public static Control IconChild(Control parent, string name, Action<Control> draw)
	{
		var icon = new Control
		{
			Name = name,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		icon.Draw += () => draw(icon);
		parent.AddChild(icon);
		return icon;
	}

	/// A solid triangle pointing left or right — the palette's open/close arrow.
	public static void DrawChevron(Control icon, bool pointsRight, Color tint)
	{
		(float unit, Vector2 origin) = IconFrame(icon);
		Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;
		icon.DrawColoredPolygon(pointsRight
			? new[] { P(9f, 5f), P(16f, 12f), P(9f, 19f) }
			: new[] { P(15f, 5f), P(8f, 12f), P(15f, 19f) }, tint);
	}

	/// A cross — the die list's delete button.
	public static void DrawCross(Control icon, Color tint)
	{
		(float unit, Vector2 origin) = IconFrame(icon);
		Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;
		float w = 2.2f * unit;
		icon.DrawLine(P(7f, 7f), P(17f, 17f), tint, w, true);
		icon.DrawLine(P(17f, 7f), P(7f, 17f), tint, w, true);
	}

	/// Four corner brackets, opening outward to go full screen and inward to come back.
	public static void DrawFullscreen(Control icon, bool exit, Color tint)
	{
		(float unit, Vector2 origin) = IconFrame(icon);
		Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;
		float w = 2f * unit;
		// One bracket, mirrored into all four corners. `exit` flips which way the arms
		// point, so the button says what it will do rather than what state it is in.
		foreach ((float cx, float cy, float sx, float sy) in new[]
			{ (5f, 5f, 1f, 1f), (19f, 5f, -1f, 1f),
			  (5f, 19f, 1f, -1f), (19f, 19f, -1f, -1f) })
		{
			float arm = exit ? -4.5f : 4.5f;
			Vector2 corner = P(cx - (exit ? sx * 4.5f : 0f), cy - (exit ? sy * 4.5f : 0f));
			icon.DrawLine(corner, corner + new Vector2(sx * arm, 0f) * unit, tint, w);
			icon.DrawLine(corner, corner + new Vector2(0f, sy * arm) * unit, tint, w);
		}
	}
}
