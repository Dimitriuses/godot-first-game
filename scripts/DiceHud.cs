using Godot;
using System;
using System.Collections.Generic;

public partial class DiceHud : Control
{
	public event Action<Dice> DeleteRequested;
	public event Action DeleteAllRequested;

	private sealed class DieEntry
	{
		public Dice Die;
		public int Id;
		public int Value;
		public string Name;
	}

	/// Where the hover tag sits relative to the cursor. Down and right, so the pointer
	/// is not standing on the number it is there to show.
	private static readonly Vector2 TagOffset = new(18, 18);

	/// Drawer geometry, as offsets from the bottom-left corner. The height is sized so
	/// one row per die type fits without scrolling — the list scrolls perfectly well,
	/// but a board holding one of each die should not open onto a half-cut row.
	private const float DrawerTop = -368f;
	private const float DrawerBottom = -64f;

	private readonly Dictionary<Dice, DieEntry> entries = new();
	private VBoxContainer rows;
	private PanelContainer drawer;
	private Button totalButton;
	private PanelContainer valueTag;
	private Label valueTagLabel;
	private Dice hoveredDie;
	private Tween drawerTween;
	private bool isOpen;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		BuildInterface();
		SetOpen(false, false);
		Refresh();
		SetProcess(false);          // only while a die is under the cursor
	}

	public override void _Process(double delta)
	{
		UpdateValueTag();
	}

	/// <summary>
	/// A die was entered or left by the cursor. Both dice report, so the exit of one
	/// arriving after the entry of the next must not clear the newer one.
	/// </summary>
	public void SetDieHovered(Dice die, bool hovered)
	{
		if (hovered)
			hoveredDie = die;
		else if (hoveredDie == die)
			hoveredDie = null;
		else
			return;

		SetProcess(hoveredDie != null);
		UpdateValueTag();
	}

	/// The number the die is showing, next to the cursor.
	///
	/// Re-evaluated every frame rather than only on enter and exit, because everything
	/// it depends on can change while the cursor sits still: the die gets picked up, a
	/// roll finishes and the value changes, another die knocks into it.
	private void UpdateValueTag()
	{
		// Hidden while held, and while rolling: the result is decided the moment a
		// throw starts, and showing it before the clip lands would give the throw away.
		if (hoveredDie == null || !IsInstanceValid(hoveredDie)
			|| hoveredDie.IsHeld || hoveredDie.IsRolling
			|| !entries.TryGetValue(hoveredDie, out DieEntry entry))
		{
			valueTag.Visible = false;
			return;
		}

		valueTagLabel.Text = $"{entry.Name} #{entry.Id}   {entry.Value}";
		valueTag.ResetSize();       // a PanelContainer outside a container keeps its old size
		valueTag.Visible = true;

		// Flip to the other side of the cursor near an edge rather than clamping to it.
		// A clamped tag stops following the pointer while the pointer keeps moving,
		// which reads as a label that has got stuck.
		Vector2 mouse = GetViewport().GetMousePosition();
		Vector2 view = GetViewportRect().Size;
		Vector2 size = valueTag.Size;
		valueTag.Position = new Vector2(
			Mathf.Max(8f, mouse.X + TagOffset.X + size.X > view.X - 8f
				? mouse.X - TagOffset.X - size.X : mouse.X + TagOffset.X),
			Mathf.Max(8f, mouse.Y + TagOffset.Y + size.Y > view.Y - 8f
				? mouse.Y - TagOffset.Y - size.Y : mouse.Y + TagOffset.Y));
	}

	public void AddDie(Dice die, int id, int value)
	{
		entries[die] = new DieEntry
		{
			Die = die, Id = id, Value = value, Name = NameOf(die)
		};
		Refresh();
	}

	/// What to call a die in the list. The same name the palette offers it under, so a
	/// row and a drawer entry can be matched up -- and so the pipped and numbered d6,
	/// which have the same face count, do not both come out "d6".
	private static string NameOf(Dice die) =>
		string.IsNullOrEmpty(die.DieLabel) ? $"d{die.FaceCount}" : die.DieLabel;

	public void UpdateValue(Dice die, int value)
	{
		if (!entries.TryGetValue(die, out DieEntry entry))
			return;
		entry.Value = value;
		Refresh();
	}

	public void RemoveDie(Dice die)
	{
		die.SetHovered(false);
		SetDieHovered(die, false);
		if (entries.Remove(die))
			Refresh();
	}

	private void BuildInterface()
	{
		drawer = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		drawer.SetAnchorsPreset(LayoutPreset.BottomLeft);
		drawer.OffsetLeft = 16;
		drawer.OffsetTop = DrawerTop;
		drawer.OffsetRight = 306;
		drawer.OffsetBottom = DrawerBottom;
		AddChild(drawer);

		var panelStyle = new StyleBoxFlat
		{
			BgColor = new Color("202536e8"),
			BorderColor = new Color("77819b")
		};
		panelStyle.SetBorderWidthAll(2);
		panelStyle.SetCornerRadiusAll(10);
		drawer.AddThemeStyleboxOverride("panel", panelStyle);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		drawer.AddChild(margin);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 8);
		margin.AddChild(content);

		var title = new Label { Text = "DICE ON BOARD" };
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 17);
		content.AddChild(title);

		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		content.AddChild(scroll);

		rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rows.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(rows);

		var deleteAll = new Button
		{
			Text = "Delete All",
			FocusMode = FocusModeEnum.None,
			TooltipText = "Remove every die from the board"
		};
		deleteAll.Pressed += () => DeleteAllRequested?.Invoke();
		content.AddChild(deleteAll);

		totalButton = new Button
		{
			Text = "Total: 0",
			FocusMode = FocusModeEnum.None,
			TooltipText = "Show or hide dice on the board"
		};
		totalButton.SetAnchorsPreset(LayoutPreset.BottomLeft);
		totalButton.OffsetLeft = 16;
		totalButton.OffsetTop = -56;
		totalButton.OffsetRight = 306;
		totalButton.OffsetBottom = -16;
		totalButton.Pressed += () => SetOpen(!isOpen, true);
		AddChild(totalButton);

		// Added last so it draws over the drawer, and ignores the mouse so it cannot
		// put itself between the cursor and the die it is describing.
		valueTag = new PanelContainer
		{
			Name = "ValueTag",
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false
		};
		var tagStyle = new StyleBoxFlat
		{
			BgColor = new Color("202536f0"),
			BorderColor = new Color("77819b"),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 5,
			ContentMarginBottom = 5
		};
		tagStyle.SetBorderWidthAll(2);
		tagStyle.SetCornerRadiusAll(8);
		valueTag.AddThemeStyleboxOverride("panel", tagStyle);
		AddChild(valueTag);

		valueTagLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
		valueTagLabel.AddThemeFontSizeOverride("font_size", 16);
		valueTag.AddChild(valueTagLabel);
	}

	private void Refresh()
	{
		if (rows == null)
			return;

		foreach (DieEntry entry in entries.Values)
			entry.Die.SetHovered(false);
		foreach (Node child in rows.GetChildren())
		{
			rows.RemoveChild(child);
			child.QueueFree();
		}

		var sorted = new List<DieEntry>(entries.Values);
		sorted.Sort((a, b) =>
		{
			int valueOrder = a.Value.CompareTo(b.Value);
			return valueOrder != 0 ? valueOrder : a.Id.CompareTo(b.Id);
		});

		int total = 0;
		foreach (DieEntry entry in sorted)
		{
			total += entry.Value;
			AddRow(entry);
		}
		totalButton.Text = $"Total: {total}";
	}

	public void SetOpen(bool open, bool animate)
	{
		isOpen = open;
		float top = open ? DrawerTop : 0f;
		float bottom = open ? DrawerBottom : DrawerTop - DrawerBottom;

		drawerTween?.Kill();
		if (animate)
		{
			drawerTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			drawerTween.TweenProperty(drawer, "offset_top", top, 0.24);
			drawerTween.TweenProperty(drawer, "offset_bottom", bottom, 0.24);
		}
		else
		{
			drawer.OffsetTop = top;
			drawer.OffsetBottom = bottom;
		}
	}

	private void AddRow(DieEntry entry)
	{
		var row = new HBoxContainer
		{
			CustomMinimumSize = new Vector2(0, 34),
			MouseFilter = MouseFilterEnum.Stop
		};
		row.AddThemeConstantOverride("separation", 8);
		row.MouseEntered += () => entry.Die.SetHovered(true);
		row.MouseExited += () => entry.Die.SetHovered(false);
		rows.AddChild(row);

		var value = new Label
		{
			// "d6 #3", not "D3": with more than one die type on the board, D<n> reads
			// as the die's kind and collides with the d6/d20 the label is next to.
			Text = $"{entry.Name} #{entry.Id}    {entry.Value}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore
		};
		row.AddChild(value);

		var remove = new Button
		{
			Text = "×",
			CustomMinimumSize = new Vector2(38, 30),
			FocusMode = FocusModeEnum.None,
			TooltipText = $"Remove {entry.Name} #{entry.Id}"
		};
		remove.Pressed += () => DeleteRequested?.Invoke(entry.Die);
		row.AddChild(remove);
	}
}
