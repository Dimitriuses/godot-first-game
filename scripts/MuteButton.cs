using Godot;

/// <summary>
/// Turns the sound off and on, from the top-right corner.
///
/// The icon is drawn rather than written. A speaker is an emoji, and Godot's default font
/// has no emoji in it — a glyph it cannot render comes out as a blank box, which is a
/// worse button than no button. Everything here is two polygons and a few strokes, so it
/// renders the same wherever it runs and scales with the button rather than with a font.
/// </summary>
public partial class MuteButton : Control
{
	/// The key that does the same thing. Handled here rather than in
	/// <see cref="GameManager"/> for the same reason the palette handles Tab: it belongs
	/// to this control and nothing else needs to know about it.
	public const Key MuteKey = Key.M;

	private Button button;
	private Control icon;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;

		button = UiSkin.CornerButton(this, "Mute", 0);
		button.Pressed += Toggle;

		icon = new Control { Name = "Icon", MouseFilter = MouseFilterEnum.Ignore };
		icon.SetAnchorsPreset(LayoutPreset.FullRect);
		icon.Draw += DrawIcon;
		button.AddChild(icon);

		Refresh();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Keycode == MuteKey && key.Pressed
			&& !key.Echo)
		{
			Toggle();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Toggle()
	{
		Sfx.SetMuted(!Sfx.Muted);
		// After the change, so unmuting is audible and muting is not — which is the only
		// confirmation this button can give.
		Sfx.Play("ui_click");
		Refresh();
	}

	/// Public because the button is built before the save is read: a muted save would
	/// otherwise leave the speaker drawn as though the sound were on.
	public void Refresh()
	{
		button.TooltipText = Sfx.Muted
			? $"Sound off — click or press {(char)MuteKey} to turn it on"
			: $"Sound on — click or press {(char)MuteKey} to turn it off";
		icon.QueueRedraw();
	}

	/// <summary>
	/// A speaker, and either two waves coming out of it or a cross where they were.
	///
	/// Laid out in a 24-unit square and scaled to whatever the button is, so the drawing
	/// never has to know the button's size.
	/// </summary>
	private void DrawIcon()
	{
		bool muted = Sfx.Muted;
		Color tint = muted ? new Color("8b93a8") : new Color("e6e8f2");
		(float unit, Vector2 origin) = UiSkin.IconFrame(icon);
		Vector2 P(float x, float y) => origin + new Vector2(x, y) * unit;

		// The block, and the cone opening to the right of it.
		icon.DrawRect(new Rect2(P(5f, 9.5f), new Vector2(4f * unit, 5f * unit)), tint);
		icon.DrawColoredPolygon(
			new[] { P(9f, 9.5f), P(14f, 5f), P(14f, 19f), P(9f, 14.5f) }, tint);

		if (muted)
		{
			float w = 2f * unit;
			icon.DrawLine(P(16.5f, 8f), P(21.5f, 16f), tint, w);
			icon.DrawLine(P(21.5f, 8f), P(16.5f, 16f), tint, w);
			return;
		}

		// Two arcs, opening rightward from the mouth of the cone.
		for (int i = 1; i <= 2; i++)
			icon.DrawArc(P(14f, 12f), i * 3.4f * unit, Mathf.DegToRad(-52f),
				Mathf.DegToRad(52f), 20, tint, 1.8f * unit, true);
	}
}
