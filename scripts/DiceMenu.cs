using Godot;
using System;

/// <summary>
/// The panel that opens on a right-click, on a die or on the board behind it.
///
/// One panel with two sets of items rather than two menus: the fiddly parts — folding
/// back at the screen edge, not closing on the press that is about to choose an item,
/// telling the board's clicks from its own — are the same either way, and having them
/// once means they cannot drift apart.
///
/// It owns no behaviour: choosing an item raises an event and closes, and
/// <see cref="GameManager"/> does the work. That keeps the rules in one place rather
/// than splitting them between the board and a menu, and it is why the same actions can
/// be driven from the keyboard without going through here at all.
/// </summary>
public partial class DiceMenu : Control
{
	public event Action<Dice> RollRequested;
	public event Action<Dice> CopyRequested;
	public event Action<Dice> DeleteRequested;
	public event Action<Dice> LinkRequested;
	public event Action<Dice> UnlinkRequested;

	public event Action ThrowAllRequested;
	public event Action RespawnRequested;
	public event Action DeleteAllRequested;

	/// What the Link item can offer for the die the menu is opening on. The board knows
	/// which dice are out and what they are; the menu only draws the answer.
	public enum Linkage
	{
		/// Nothing on the board could pair with it — a d6, or the only d10 out.
		Impossible,
		/// A partner exists to pick from.
		Available,
		/// Already half of a d100.
		Linked
	}

	/// Keys that do the same thing to the die under the cursor. Listed on the items so
	/// they are discoverable; <see cref="GameManager"/> is what actually watches for them.
	public const Key RollKey = Key.R;
	public const Key CopyKey = Key.C;

	private PanelContainer panel;
	private Label nameLabel;
	private Label valueLabel;
	private Button linkItem;
	private VBoxContainer dieItems;
	private VBoxContainer boardItems;
	private Button throwAllItem;
	private Button respawnItem;
	private Button deleteAllItem;
	private Dice target;
	private bool linkedNow;     // whether linkItem currently means "Unlink"

	/// The die the open menu belongs to, or null when it is closed.
	public Dice Target => panel != null && panel.Visible && IsInstanceValid(target)
		? target : null;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Build();
		panel.Visible = false;
		SetProcess(false);
	}

	/// Only while open, and only to keep the value honest: a die rolled from the menu
	/// keeps the menu up for the three seconds the clip runs, and a stale number there
	/// would be worse than none.
	public override void _Process(double delta)
	{
		// Only ever running for the die menu; the board one has no value to keep up to
		// date and switches processing off.
		Dice die = Target;
		if (die == null)
			Close();
		else
			valueLabel.Text = die.Value.ToString();
	}

	/// `label` is the name the die list is calling this die — "d6 #2" rather than "d6"
	/// once there are two of them. Passed in rather than worked out here, so the menu and
	/// the list can never disagree about which die is which.
	public void Open(Dice die, Vector2 at, Linkage linkage = Linkage.Impossible,
		string label = null)
	{
		if (die == null || !IsInstanceValid(die))
			return;

		target = die;
		nameLabel.Text = string.IsNullOrEmpty(label) ? die.DisplayName : label;
		valueLabel.Text = die.Value.ToString();
		SetLinkage(linkage);
		dieItems.Visible = true;
		boardItems.Visible = false;
		Show(at);
		SetProcess(true);       // the value can change while the menu is up
	}

	/// <summary>
	/// The other menu: a right-click on the board rather than on a die.
	///
	/// The same three things the buttons and the space bar already do, gathered where
	/// the pointer is. `count` is only for the header — everything is greyed out when
	/// there is nothing to do it to.
	/// </summary>
	public void OpenBoard(Vector2 at, int count)
	{
		target = null;
		nameLabel.Text = "BOARD";
		valueLabel.Text = count.ToString();
		dieItems.Visible = false;
		boardItems.Visible = true;
		throwAllItem.Disabled = count == 0;
		respawnItem.Disabled = count == 0;
		deleteAllItem.Disabled = count == 0;
		Show(at);
		SetProcess(false);      // nothing here changes on its own
	}

	/// Put the panel at the pointer, folding back rather than hanging off the edge — a
	/// menu that opens half outside the window cannot be finished.
	private void Show(Vector2 at)
	{
		panel.Visible = true;
		panel.ResetSize();          // a PanelContainer outside a container keeps its size
		Vector2 view = GetViewportRect().Size;
		Vector2 size = panel.Size;
		panel.Position = new Vector2(
			Mathf.Max(4f, at.X + size.X > view.X - 4f ? at.X - size.X : at.X),
			Mathf.Max(4f, at.Y + size.Y > view.Y - 4f ? at.Y - size.Y : at.Y));
	}

	/// Whether either menu is up. `Target` is the die one only.
	public bool IsOpen => panel != null && panel.Visible;

	public void Close()
	{
		target = null;
		if (panel != null)
			panel.Visible = false;
		SetProcess(false);
	}

	/// Whether a point is over the open menu, so a click there is the menu's and not
	/// the board's.
	public bool Covers(Vector2 point) =>
		panel != null && panel.Visible && panel.GetGlobalRect().HasPoint(point);

	private void Build()
	{
		panel = new PanelContainer { Name = "Panel", MouseFilter = MouseFilterEnum.Stop };
		panel.CustomMinimumSize = new Vector2(168, 0);
		var style = new StyleBoxFlat
		{
			BgColor = new Color("202536f2"),
			BorderColor = new Color("77819b")
		};
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(10);
		panel.AddThemeStyleboxOverride("panel", style);
		AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		panel.AddChild(margin);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 4);
		margin.AddChild(content);

		// Header: which die this is, and what it is showing.
		var head = new HBoxContainer();
		nameLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		nameLabel.AddThemeFontSizeOverride("font_size", 16);
		head.AddChild(nameLabel);
		valueLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right };
		valueLabel.AddThemeFontSizeOverride("font_size", 18);
		head.AddChild(valueLabel);
		content.AddChild(head);
		content.AddChild(new HSeparator());

		// Two sets of items, one shown at a time. Separate containers rather than one
		// list with things hidden, so neither has to know the other exists.
		dieItems = new VBoxContainer { Name = "DieItems" };
		dieItems.AddThemeConstantOverride("separation", 4);
		content.AddChild(dieItems);

		AddItem(dieItems, "Roll", $"({(char)RollKey})", "Roll this die where it stands",
			() => RollRequested?.Invoke(target));
		AddItem(dieItems, "Theme", "", "Colour schemes — not built yet", null);
		AddItem(dieItems, "Copy", $"({(char)CopyKey})",
			"Take a copy, then click where to put it — hold Shift to keep stamping",
			() => CopyRequested?.Invoke(target));
		linkItem = AddItem(dieItems, "Link", "", "", () =>
		{
			if (linkedNow)
				UnlinkRequested?.Invoke(target);
			else
				LinkRequested?.Invoke(target);
		});
		dieItems.AddChild(new HSeparator());
		AddItem(dieItems, "Delete", "", "Remove this die from the board",
			() => DeleteRequested?.Invoke(target));

		boardItems = new VBoxContainer { Name = "BoardItems", Visible = false };
		boardItems.AddThemeConstantOverride("separation", 4);
		content.AddChild(boardItems);

		throwAllItem = AddItem(boardItems, "Throw all", "(Space)",
			"Scatter every die across the board and roll it",
			() => ThrowAllRequested?.Invoke());
		respawnItem = AddItem(boardItems, "Respawn", "",
			"Gather every die back to the middle and stop it",
			() => RespawnRequested?.Invoke());
		boardItems.AddChild(new HSeparator());
		deleteAllItem = AddItem(boardItems, "Delete all", "",
			"Clear the board", () => DeleteAllRequested?.Invoke());
	}

	/// <summary>
	/// Set the Link item to whatever the die can do: pair up, come apart, or nothing.
	///
	/// One item rather than two, because linking and unlinking are never both offers —
	/// a die is in a pair or it is not.
	/// </summary>
	private void SetLinkage(Linkage linkage)
	{
		linkItem.Disabled = linkage == Linkage.Impossible;
		switch (linkage)
		{
			case Linkage.Linked:
				linkItem.Text = "Unlink";
				linkItem.TooltipText = "Read this die on its own again";
				break;
			case Linkage.Available:
				linkItem.Text = "Link";
				linkItem.TooltipText = "Pick the other d10 to read as one d100";
				break;
			default:
				linkItem.Text = "Link";
				linkItem.TooltipText =
					"Needs a plain d10 and a percentile d10 on the board";
				break;
		}
		linkedNow = linkage == Linkage.Linked;
	}

	private Button AddItem(Container into, string text, string hint, string tooltip,
		Action pressed)
	{
		var button = new Button
		{
			Text = hint.Length > 0 ? $"{text}   {hint}" : text,
			Alignment = HorizontalAlignment.Left,
			TooltipText = tooltip,
			FocusMode = FocusModeEnum.None,
			// No handler means it is not built yet, and a button that looks live but
			// does nothing is worse than one that says so.
			Disabled = pressed == null
		};
		button.AddThemeConstantOverride("h_separation", 0);
		if (pressed != null)
			button.Pressed += () =>
			{
				pressed();
				Close();
			};
		into.AddChild(button);
		return button;
	}
}
