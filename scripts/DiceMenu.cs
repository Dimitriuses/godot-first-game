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

	/// A colour scheme was chosen, for whatever the menu is open on — a die, or a slot in
	/// the palette. Which of those it is, the board decides; the menu only reports the
	/// choice, the same way every other item here does.
	public event Action<int> ThemeRequested;

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
	private VBoxContainer themeItems;
	private readonly System.Collections.Generic.List<Button> swatches = new();
	private Button throwAllItem;
	private Button respawnItem;
	private Button deleteAllItem;
	private Dice target;
	private bool linkedNow;     // whether linkItem currently means "Unlink"

	/// Which palette slot the menu was opened on, or -1 when it was opened on a die or
	/// on the board. The board reads it to know what a chosen theme applies to.
	public int PaletteSlot { get; private set; } = -1;

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
		PaletteSlot = -1;
		nameLabel.Text = string.IsNullOrEmpty(label) ? die.DisplayName : label;
		MarkTheme(die.Theme);
		themeItems.Visible = true;
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
		PaletteSlot = -1;
		themeItems.Visible = false;
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

	/// <summary>
	/// The third way in: a right-click on a die in the palette, which sets what colour
	/// that kind of die comes out in from now on.
	///
	/// Only the swatches — there is nothing to roll or delete, because there is no die
	/// yet. Choosing here changes what the *next* one looks like, never what is already
	/// on the board, which is why it is the palette that remembers it and not this menu.
	/// </summary>
	public void OpenPalette(int slot, string label, Vector2 at, int theme)
	{
		target = null;
		PaletteSlot = slot;
		nameLabel.Text = label;
		valueLabel.Text = "";
		dieItems.Visible = false;
		boardItems.Visible = false;
		themeItems.Visible = true;
		MarkTheme(theme);
		Show(at);
		SetProcess(false);
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
		PaletteSlot = -1;
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
		// Wide enough for the swatch strip: seven of them plus their gaps and the panel's
		// own margins. The items were happy at 168 and do not mind the extra.
		panel.CustomMinimumSize = new Vector2(196, 0);
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

		// Between the die's items and its Delete, and shared with the palette menu, which
		// shows this and nothing else.
		themeItems = new VBoxContainer { Name = "ThemeItems" };
		themeItems.AddThemeConstantOverride("separation", 4);
		content.AddChild(themeItems);
		themeItems.AddChild(new HSeparator());
		var themeCaption = new Label { Text = "Theme" };
		themeCaption.AddThemeFontSizeOverride("font_size", 13);
		themeCaption.Modulate = new Color(0.78f, 0.82f, 0.92f);
		themeItems.AddChild(themeCaption);
		BuildSwatches(themeItems);

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
	/// One small square per colour scheme, showing the scheme rather than naming it.
	///
	/// Seven names would not fit and would not help — a colour is quicker to recognise
	/// than to read. The name is on the tooltip for the two that are hard to tell apart
	/// at this size.
	/// </summary>
	private void BuildSwatches(Container into)
	{
		var row = new HBoxContainer { Name = "Swatches" };
		row.AddThemeConstantOverride("separation", 3);
		into.AddChild(row);

		for (int i = 0; i < DiceTheme.Count; i++)
		{
			int theme = i;
			var button = new Button
			{
				Name = $"Swatch{i}",
				CustomMinimumSize = new Vector2(21, 21),
				TooltipText = DiceTheme.NameOf(i),
				FocusMode = FocusModeEnum.None,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			button.Pressed += () =>
			{
				// Reported before closing, so the board can still ask what the menu was
				// open on. Close clears that.
				ThemeRequested?.Invoke(theme);
				Close();
			};
			row.AddChild(button);
			swatches.Add(button);
		}
		MarkTheme(DiceTheme.Bone);
	}

	/// Paint the swatches, and ring the one that is already on. Every state of a Button
	/// has to be overridden or it repaints itself grey the moment it is hovered.
	private void MarkTheme(int theme)
	{
		for (int i = 0; i < swatches.Count; i++)
		{
			bool active = i == theme;
			var style = new StyleBoxFlat
			{
				BgColor = DiceTheme.Swatch(i),
				BorderColor = active ? new Color("ffe6a8") : new Color("00000060")
			};
			style.SetBorderWidthAll(active ? 2 : 1);
			style.SetCornerRadiusAll(5);
			foreach (string state in new[] { "normal", "pressed", "disabled" })
				swatches[i].AddThemeStyleboxOverride(state, style);

			var lit = (StyleBoxFlat)style.Duplicate();
			lit.BorderColor = new Color("ffffff");
			lit.SetBorderWidthAll(2);
			swatches[i].AddThemeStyleboxOverride("hover", lit);
		}
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
			// Named after the item so it can be picked out of the tree — by the remote
			// inspector, and by a harness driving the menu the way a player does.
			Name = text.Replace(" ", ""),
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
