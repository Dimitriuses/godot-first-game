using Godot;

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
}
