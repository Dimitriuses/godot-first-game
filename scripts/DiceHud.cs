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
		public int Faces;
	}

	private readonly Dictionary<Dice, DieEntry> entries = new();
	private VBoxContainer rows;
	private PanelContainer drawer;
	private Button totalButton;
	private Tween drawerTween;
	private bool isOpen;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		BuildInterface();
		SetOpen(false, false);
		Refresh();
	}

	public void AddDie(Dice die, int id, int value)
	{
		entries[die] = new DieEntry
		{
			Die = die, Id = id, Value = value, Faces = die.FaceCount
		};
		Refresh();
	}

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
		if (entries.Remove(die))
			Refresh();
	}

	private void BuildInterface()
	{
		drawer = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		drawer.SetAnchorsPreset(LayoutPreset.BottomLeft);
		drawer.OffsetLeft = 16;
		drawer.OffsetTop = -330;
		drawer.OffsetRight = 306;
		drawer.OffsetBottom = -64;
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
		float top = open ? -330f : 0f;
		float bottom = open ? -64f : 266f;

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
			Text = $"d{entry.Faces} #{entry.Id}    {entry.Value}",
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
			TooltipText = $"Remove d{entry.Faces} #{entry.Id}"
		};
		remove.Pressed += () => DeleteRequested?.Invoke(entry.Die);
		row.AddChild(remove);
	}
}
