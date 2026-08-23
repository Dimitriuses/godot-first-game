using Godot;

/// <summary>
/// Goes full screen and comes back, from the top-right corner.
///
/// **It exists for the browser.** A desktop build can be maximised by its window
/// manager and a phone app is already full screen, but a game in a page is a canvas
/// among page furniture, and the Fullscreen API is the only way out of that. The button
/// is the *only* way to ask: browsers grant fullscreen only from inside a user gesture,
/// so a call made at startup, from a timer, or from anything but a real click is refused.
/// That is why this is a control and not a setting.
///
/// The icon is drawn rather than written, like the two beside it — see
/// <see cref="UiSkin.IconChild"/> for what typing a glyph costs in a web export.
/// </summary>
public partial class FullscreenButton : Control
{
	/// The key that does the same thing. F is the near-universal convention, and unlike
	/// the button it is reachable while the pointer is over the board.
	public const Key FullscreenKey = Key.F;

	private Button button;
	private Control icon;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;

		button = UiSkin.CornerButton(this, "Fullscreen", 2);
		button.Pressed += Toggle;

		icon = UiSkin.IconChild(button, "Icon",
			c => UiSkin.DrawFullscreen(c, IsFullscreen, new Color("e6e8f2")));

		Refresh();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && Shortcuts.Is(key, FullscreenKey) && key.Pressed
			&& !key.Echo)
		{
			Toggle();
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>
	/// Whether the window is in one of the two full-screen modes.
	///
	/// Asked of the display server every time rather than tracked, because the player can
	/// leave full screen without touching this button — Escape does it in every browser,
	/// and nothing tells the game when it happens.
	/// </summary>
	private static bool IsFullscreen
	{
		get
		{
			DisplayServer.WindowMode mode = DisplayServer.WindowGetMode();
			return mode == DisplayServer.WindowMode.Fullscreen
				|| mode == DisplayServer.WindowMode.ExclusiveFullscreen;
		}
	}

	private void Toggle()
	{
		DisplayServer.WindowSetMode(IsFullscreen
			? DisplayServer.WindowMode.Windowed
			: DisplayServer.WindowMode.Fullscreen);
		Sfx.Play("ui_click");
		Refresh();
	}

	/// <summary>
	/// Public because the state can change behind the game's back: Escape leaves full
	/// screen in every browser and reports nothing, so <see cref="_Process"/> polls.
	/// </summary>
	public void Refresh()
	{
		button.TooltipText = IsFullscreen
			? $"Leave full screen (press {(char)FullscreenKey})"
			: $"Go full screen (press {(char)FullscreenKey})";
		icon?.QueueRedraw();
	}

	private bool wasFullscreen;

	/// Cheap — one enum read a frame — and the only way to notice the player pressing
	/// Escape, which no signal reports.
	public override void _Process(double delta)
	{
		if (IsFullscreen == wasFullscreen)
			return;
		wasFullscreen = IsFullscreen;
		Refresh();
	}
}
