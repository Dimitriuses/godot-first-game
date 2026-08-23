using Godot;

/// <summary>
/// Which key was that, whatever the keyboard is set to.
///
/// **`Keycode` is the letter the layout produces; `PhysicalKeycode` is the key that was
/// pressed.** On a Latin layout they agree, which is why every shortcut in this project
/// worked for a year by reading `Keycode` alone. On a Cyrillic layout they do not: the
/// key engraved R produces К, so `Keycode` is that and the R shortcut never fires. The
/// web build is where this surfaced — the browser reports the layout faithfully — but it
/// was never a web bug, and the desktop build had it too.
///
/// Matching either is Godot's own advice for gameplay keys, and it costs nothing: no
/// layout produces a Latin letter from a *different* physical key, so the two cannot
/// disagree in a way that fires the wrong action.
///
/// Everything that reads a key goes through here, so a shortcut added later cannot
/// quietly go back to being layout-dependent.
/// </summary>
public static class Shortcuts
{
	public static bool Is(InputEventKey key, Key code) =>
		key != null && (key.Keycode == code || key.PhysicalKeycode == code);

	/// A press, not a repeat — the shape every shortcut here wants.
	public static bool Pressed(InputEvent @event, Key code) =>
		@event is InputEventKey key && key.Pressed && !key.Echo && Is(key, code);
}
