using Godot;
using System;

/// <summary>
/// Turns "drag them all at once" on and off — what holding Shift does, made permanent.
///
/// A touchscreen has no modifier keys, so the Shift group drag was unreachable there: the
/// gesture exists but there is no way to ask for it. This is that way. It is offered on
/// the desktop too rather than hidden behind a touch check, because a laptop with a
/// touchscreen is both, and a control that appears on some machines and not others is
/// harder to explain than one that is always there.
/// </summary>
public partial class GroupDragButton : Control
{
	/// Raised when the player toggles it. The board owns the setting; this only asks.
	public event Action<bool> Toggled;

	private Button button;
	private Control icon;
	private bool on;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		button = UiSkin.CornerButton(this, "GroupDrag", 1);
		button.Pressed += () =>
		{
			on = !on;
			Sfx.Play("ui_click");
			Toggled?.Invoke(on);
			Refresh();
		};

		icon = new Control { Name = "Icon", MouseFilter = MouseFilterEnum.Ignore };
		icon.SetAnchorsPreset(LayoutPreset.FullRect);
		icon.Draw += DrawIcon;
		button.AddChild(icon);
		Refresh();
	}

	/// <summary>
	/// Set from outside — the saved state is read after this button has been built, the
	/// same way the mute button is.
	/// </summary>
	public void SetOn(bool value)
	{
		on = value;
		Refresh();
	}

	public bool IsOn => on;

	private void Refresh()
	{
		button.TooltipText = on
			? "Dragging moves every die — click to move one at a time"
			: "Dragging moves one die — click to move them all together";
		icon?.QueueRedraw();
	}

	/// <summary>
	/// Four small dice in a square. All four are lit when the drag takes everything, and
	/// only one when it takes one — so the icon says which mode it is *in*, not which
	/// mode the button would switch to.
	/// </summary>
	private void DrawIcon()
	{
		(float unit, Vector2 origin) = UiSkin.IconFrame(icon);
		Color lit = new("e6e8f2");
		Color dim = new("6b7387");

		for (int i = 0; i < 4; i++)
		{
			float x = 5f + (i % 2) * 9f;
			float y = 5f + (i / 2) * 9f;
			// Bottom-left is the single die: the one that moves when the mode is off.
			bool active = on || i == 2;
			icon.DrawRect(
				new Rect2(origin + new Vector2(x, y) * unit, new Vector2(7f, 7f) * unit),
				active ? lit : dim, true);
		}
	}
}
