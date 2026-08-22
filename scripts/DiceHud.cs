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
		public string Name;

		/// The order the die was added in. It fixes the row's place in the list for as
		/// long as the die is on the board: sorting by value instead meant every throw
		/// reshuffled the whole list, and a new die arrived somewhere unpredictable.
		public int Seq;

		/// The last *settled* value. Not read live from the die, because the result is
		/// decided the moment a throw starts and reading it would give the roll away.
		public int Value;

		/// Which one of its kind this is, and whether that is worth printing — a lone
		/// d20 is just "d20". Both recomputed on every refresh, so deleting a die never
		/// leaves a gap in the numbering.
		public int Number;
		public bool Numbered;

		// The row's nodes, kept alive across refreshes. Rebuilding them every time was
		// what made an added die blink into existence instead of arriving.
		public Control Wrapper;
		public PanelContainer Panel;
		public Control IconBox;
		public TextureRect Icon;
		public Label NameLabel;
		public Label ValueLabel;
		public Button Remove;

		/// Where the die sits inside its frame, and how much to magnify it. Measured once
		/// off the resting pose so a tumbling frame keeps the same scale as a still one.
		public float IconZoom = 1f;
		public Vector2 IconCentre;

		public string Shown = "";
		public bool WasRolling;
		public bool Hovered;
	}

	/// Where the hover tag sits relative to the cursor. Down and right, so the pointer
	/// is not standing on the number it is there to show.
	private static readonly Vector2 TagOffset = new(18, 18);

	/// Drawer geometry, as offsets from the bottom-left corner. The height is sized to
	/// hold five rows without scrolling — the list scrolls perfectly well, but it should
	/// not open onto a half-cut row, and it has to leave the palette above it room.
	private const float DrawerTop = -384f;
	private const float DrawerBottom = -64f;

	private const float RowHeight = 38f;
	private const float IconSize = 30f;

	/// How long a row takes to unfold in or collapse out.
	private const double RowFade = 0.2;

	private readonly Dictionary<Dice, DieEntry> entries = new();
	private readonly List<DieEntry> visible = new();
	private VBoxContainer rows;
	private PanelContainer drawer;
	private Button totalButton;
	private PanelContainer valueTag;
	private Label valueTagLabel;
	private StyleBoxFlat rowRest;
	private StyleBoxFlat rowHover;
	private StyleBoxFlat rowRolling;
	private Dice hoveredDie;
	private Tween drawerTween;
	private bool isOpen;

	/// On a touchscreen there is no hovering, so anything that only appears under the
	/// pointer never appears at all. The delete cross stays out on those.
	private bool touchUi;
	private int nextSeq = 1;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		touchUi = DisplayServer.IsTouchscreenAvailable();
		BuildInterface();
		SetOpen(false, false);
		Refresh();
	}

	/// Runs whenever there is anything in the list. The rows show live sprite frames and
	/// have to notice a roll starting, which nothing signals — a die can be set rolling
	/// by a collision, by the space bar or from its own menu.
	public override void _Process(double delta)
	{
		Tick();
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

		DieEntry shown = RendersRow(entry) ? entry : PartnerEntry(entry) ?? entry;
		valueTagLabel.Text = $"{LabelOf(shown)}  {ValueText(shown)}";
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

	public void AddDie(Dice die, int value)
	{
		entries[die] = new DieEntry
		{
			Die = die, Seq = nextSeq++, Value = value, Name = die.DisplayName
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
		if (!entries.TryGetValue(die, out DieEntry entry))
			return;
		die.SetHovered(false);
		SetDieHovered(die, false);
		entries.Remove(die);
		Dismiss(entry);
		Refresh();
	}

	/// <summary>
	/// How a die is named in the list and in its own menu: "d6" on its own, "d6 #2" once
	/// there are two of them, "d100" for a linked pair.
	///
	/// Public because the right-click menu wants the same name — a die called "#2" in
	/// one place and "d6" in the other is two dice as far as the player is concerned.
	/// </summary>
	public string LabelFor(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return "";
		if (!entries.TryGetValue(die, out DieEntry entry))
			return die.DisplayName;
		return LabelOf(RendersRow(entry) ? entry : PartnerEntry(entry) ?? entry);
	}

	/// The name a die reads under: its own, or "d100" once it is half of one. A pair is
	/// one entry, not two — showing the same hundred against both would double it in the
	/// total, and showing each die's own number would contradict the pair it is part of.
	private string GroupOf(DieEntry entry) =>
		PartnerEntry(entry) != null ? "d100" : entry.Name;

	private string LabelOf(DieEntry entry) =>
		entry.Numbered ? $"{GroupOf(entry)} #{entry.Number}" : GroupOf(entry);

	private DieEntry PartnerEntry(DieEntry entry)
	{
		Dice partner = entry.Die.Partner;
		return partner != null && IsInstanceValid(partner)
			&& entries.TryGetValue(partner, out DieEntry other) ? other : null;
	}

	/// Which of a linked pair owns the row. The tens die, so the choice does not depend
	/// on which one happened to be linked first.
	private bool RendersRow(DieEntry entry) =>
		PartnerEntry(entry) == null || entry.Die.IsTensDie;

	private int ValueOf(DieEntry entry)
	{
		DieEntry other = PartnerEntry(entry);
		return other == null ? entry.Value : Dice.PairPercent(entry.Die, other.Die);
	}

	private string ValueText(DieEntry entry) =>
		PartnerEntry(entry) == null ? entry.Value.ToString() : $"{ValueOf(entry)}%";

	/// Whether the die behind a row is mid-throw — either half of a pair counts, because
	/// the percentage is not settled until both are.
	private bool IsRolling(DieEntry entry)
	{
		if (!IsInstanceValid(entry.Die))
			return false;
		DieEntry other = PartnerEntry(entry);
		return entry.Die.IsRolling || (other != null && other.Die.IsRolling);
	}

	private void BuildInterface()
	{
		drawer = new PanelContainer { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
		drawer.SetAnchorsPreset(LayoutPreset.BottomLeft);
		// Flush with the palette above it: the two panels are one column, and a column
		// whose halves do not line up reads as a mistake rather than as a choice.
		drawer.OffsetLeft = DicePalette.DrawerLeft;
		drawer.OffsetTop = DrawerTop;
		drawer.OffsetRight = DicePalette.DrawerLeft + DicePalette.DrawerWidth;
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

		rowRest = RowStyle("2b3149a0", "2b3149a0");
		rowHover = RowStyle("3b4468e0", "8c97b8");
		rowRolling = RowStyle("4a3f5ce0", "c2a2e8");

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
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
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
			Name = "Total",
			Text = "Total: 0",
			FocusMode = FocusModeEnum.None,
			TooltipText = "Show or hide dice on the board"
		};
		totalButton.SetAnchorsPreset(LayoutPreset.BottomLeft);
		totalButton.OffsetLeft = DicePalette.DrawerLeft;
		totalButton.OffsetTop = -56;
		totalButton.OffsetRight = DicePalette.DrawerLeft + DicePalette.DrawerWidth;
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

	private static StyleBoxFlat RowStyle(string bg, string border)
	{
		var style = new StyleBoxFlat
		{
			BgColor = new Color(bg),
			BorderColor = new Color(border)
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(7);
		return style;
	}

	/// <summary>
	/// Bring the rows into line with the dice: add what is new, retire what is gone,
	/// renumber, and reorder if anything has actually moved.
	///
	/// Structure only. What each row *says* is settled every frame in <see cref="Tick"/>,
	/// because a rolling die changes what its row shows without anything calling in.
	/// </summary>
	private void Refresh()
	{
		if (rows == null)
			return;

		visible.Clear();
		foreach (DieEntry entry in entries.Values)
			if (RendersRow(entry))
				visible.Add(entry);
		visible.Sort((a, b) => a.Seq.CompareTo(b.Seq));

		// Number each kind separately and from one, every time. A single global counter
		// that only ever went up left the list claiming dice that had been deleted.
		var counted = new Dictionary<string, int>();
		foreach (DieEntry entry in visible)
		{
			string group = GroupOf(entry);
			counted[group] = counted.GetValueOrDefault(group) + 1;
		}
		var seen = new Dictionary<string, int>();
		foreach (DieEntry entry in visible)
		{
			string group = GroupOf(entry);
			seen[group] = seen.GetValueOrDefault(group) + 1;
			entry.Number = seen[group];
			entry.Numbered = counted[group] > 1;
		}

		// A die that has just been linked stops drawing its own row; unlinking gives it
		// back. Both go through the same fold in and out as arriving and leaving.
		foreach (DieEntry entry in entries.Values)
			if (!RendersRow(entry) && entry.Wrapper != null)
				Dismiss(entry);
		foreach (DieEntry entry in visible)
			if (entry.Wrapper == null)
				BuildRow(entry);

		// Only shuffle when the live rows are genuinely out of order. Moving them on
		// every change would yank the survivors of a delete upward before the deleted
		// row had finished collapsing.
		int last = -1;
		bool ordered = true;
		foreach (DieEntry entry in visible)
		{
			int index = entry.Wrapper.GetIndex();
			if (index < last)
			{
				ordered = false;
				break;
			}
			last = index;
		}
		if (!ordered)
		{
			int target = 0;
			foreach (DieEntry entry in visible)
				rows.MoveChild(entry.Wrapper, target++);
		}

		Tick();
		SetProcess(entries.Count > 0);
	}

	/// Everything about a row that can change without anyone calling in: the die's
	/// current sprite frame, whether it is mid-throw, and the total underneath.
	private void Tick()
	{
		int total = 0;
		bool anyRolling = false;

		// Ask the viewport what the pointer is on, rather than letting each row report
		// its own mouse_entered/mouse_exited. Those fire on "is this the hovered
		// control", not "is the pointer inside this rect", so moving onto the delete
		// cross — a child that takes the mouse — exited the row, which hid the cross
		// before it could be clicked. Anything inside the row counts as the row.
		//
		// Null-guarded because this also runs on the way out: a die reports TreeExiting
		// during teardown, which refreshes a HUD that has already left the tree and has
		// no viewport to ask.
		Control under = GetViewport()?.GuiGetHoveredControl();

		foreach (DieEntry entry in visible)
		{
			if (!IsInstanceValid(entry.Die) || entry.Wrapper == null)
				continue;

			bool hovered = under != null
				&& (under == entry.Panel || entry.Panel.IsAncestorOf(under));
			if (hovered != entry.Hovered)
				SetRowHovered(entry, hovered);

			bool rolling = IsRolling(entry);
			anyRolling |= rolling;
			if (!rolling)
				total += ValueOf(entry);

			// The die's own frame, live. A die that is tumbling tumbles in the list too,
			// which is the whole of "which one is going" — there is no separate indicator
			// to keep in step with the animation, because it *is* the animation.
			AnimatedSprite2D sprite = entry.Die.AnimatedSprite;
			SpriteFrames frames = sprite?.SpriteFrames;
			if (frames != null && frames.HasAnimation(sprite.Animation))
			{
				Texture2D frame = frames.GetFrameTexture(sprite.Animation,
					Mathf.Min(sprite.Frame, frames.GetFrameCount(sprite.Animation) - 1));
				if (frame != null)
				{
					entry.Icon.Texture = frame;
					// The row wears whatever the die is wearing — taken from the sprite
					// rather than looked up, because the die swaps material as it spins
					// and the row has to follow it. A reference assignment either way.
					entry.Icon.Material = sprite.Material;
					entry.Icon.Size = frame.GetSize() * entry.IconZoom;
					entry.Icon.Position = new Vector2(IconSize, IconSize) / 2f
						- entry.IconCentre * entry.IconZoom;
				}
			}

			entry.NameLabel.Text = LabelOf(entry);

			// An ellipsis rather than the old number: the value on screen during a throw
			// is the previous one, and leaving it up is a small lie the hover tag already
			// refuses to tell.
			string text = rolling ? "…" : ValueText(entry);
			if (text != entry.Shown)
			{
				entry.ValueLabel.Text = text;
				// Flash the new number, but never the ellipsis — the point is to catch
				// the eye when a result lands, and a die still going has no result.
				if (!rolling && entry.WasRolling)
					Flash(entry.ValueLabel);
				entry.Shown = text;
			}
			entry.ValueLabel.SelfModulate = rolling
				? new Color(0.72f, 0.76f, 0.88f) : Colors.White;
			entry.WasRolling = rolling;

			entry.Panel.AddThemeStyleboxOverride("panel",
				rolling ? rowRolling : entry.Hovered ? rowHover : rowRest);
			entry.Remove.Modulate =
				new Color(1, 1, 1, entry.Hovered || touchUi ? 1f : 0f);
		}

		// Rolling dice are left out rather than counted at their old value, so the total
		// never states a number the board is not showing. The ellipsis says as much.
		totalButton.Text = anyRolling ? $"Total: {total} …" : $"Total: {total}";
	}

	private static void Flash(Control label)
	{
		label.CreateTween()
			.TweenProperty(label, "modulate", Colors.White, 0.45)
			.From(new Color("ffe08a"));
	}

	private void BuildRow(DieEntry entry)
	{
		// A plain Control the row is clipped by, so it can unfold from nothing on the way
		// in and collapse to nothing on the way out. The panel keeps its full height
		// throughout — squashing it instead would compress the text rather than reveal it.
		entry.Wrapper = new Control
		{
			// Named so the row can be picked out of the tree — by the remote inspector,
			// and by a harness that wants to read what the list is actually saying. The
			// sequence number is part of it because a name that collides with a sibling
			// is not renamed but thrown away: Godot substitutes "@Control@42".
			Name = $"DieRow{entry.Seq}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			ClipContents = true,
			MouseFilter = MouseFilterEnum.Ignore,
			Modulate = new Color(1, 1, 1, 0)
		};
		rows.AddChild(entry.Wrapper);

		entry.Panel = new PanelContainer { Name = "Panel", MouseFilter = MouseFilterEnum.Stop };
		entry.Panel.AnchorRight = 1f;
		entry.Panel.OffsetBottom = RowHeight;
		entry.Panel.AddThemeStyleboxOverride("panel", rowRest);
		entry.Wrapper.AddChild(entry.Panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 5);
		margin.AddThemeConstantOverride("margin_right", 5);
		entry.Panel.AddChild(margin);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 7);
		margin.AddChild(row);

		entry.IconBox = new Control
		{
			Name = "IconBox",
			CustomMinimumSize = new Vector2(IconSize, IconSize),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			ClipContents = true,
			MouseFilter = MouseFilterEnum.Ignore
		};
		row.AddChild(entry.IconBox);

		entry.Icon = new TextureRect
		{
			Name = "Icon",
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore
		};
		entry.IconBox.AddChild(entry.Icon);
		MeasureIcon(entry);

		entry.NameLabel = new Label
		{
			Name = "DieName",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore
		};
		entry.NameLabel.AddThemeFontSizeOverride("font_size", 15);
		row.AddChild(entry.NameLabel);

		entry.ValueLabel = new Label
		{
			Name = "DieValue",
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = MouseFilterEnum.Ignore
		};
		entry.ValueLabel.AddThemeFontSizeOverride("font_size", 18);
		row.AddChild(entry.ValueLabel);

		// Out of sight until the row is pointed at. Eight always-on crosses down the
		// side of the list were louder than the numbers they sat beside.
		entry.Remove = new Button
		{
			Name = "DieRemove",
			Text = "×",
			CustomMinimumSize = new Vector2(24, 24),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			FocusMode = FocusModeEnum.None,
			Modulate = new Color(1, 1, 1, 0)
		};
		entry.Remove.Pressed += () =>
		{
			// One row, one delete: on a pair the row stands for both dice, so the cross
			// has to take both. Removing half a d100 and leaving the other half behind
			// would be a stranger thing for it to do.
			DieEntry other = PartnerEntry(entry);
			if (other != null)
				DeleteRequested?.Invoke(other.Die);
			DeleteRequested?.Invoke(entry.Die);
		};
		row.AddChild(entry.Remove);

		Tween tween = entry.Wrapper.CreateTween().SetParallel()
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(entry.Wrapper, "custom_minimum_size",
			new Vector2(0, RowHeight), RowFade).From(Vector2.Zero);
		tween.TweenProperty(entry.Wrapper, "modulate:a", 1f, RowFade);
	}

	/// Fold a row away and let it go. The entry keeps no reference afterwards, so the
	/// same die being unlinked builds a fresh one.
	private void Dismiss(DieEntry entry)
	{
		Control wrapper = entry.Wrapper;
		entry.Wrapper = null;
		if (wrapper == null || !IsInstanceValid(wrapper))
			return;

		SetRowHovered(entry, false);
		// Renamed on the way out so a row still folding away is not mistaken for one of
		// the live ones — it stays in the tree for the length of the fade.
		wrapper.Name = $"GoneRow{entry.Seq}";
		if (entry.Panel != null && IsInstanceValid(entry.Panel))
			entry.Panel.MouseFilter = MouseFilterEnum.Ignore;

		Tween tween = wrapper.CreateTween().SetParallel()
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
		tween.TweenProperty(wrapper, "custom_minimum_size", Vector2.Zero, RowFade);
		tween.TweenProperty(wrapper, "modulate:a", 0f, RowFade);
		tween.Chain().TweenCallback(Callable.From(wrapper.QueueFree));
	}

	private void SetRowHovered(DieEntry entry, bool hovered)
	{
		entry.Hovered = hovered;
		if (!IsInstanceValid(entry.Die))
			return;
		// Both halves of a pair light up: the row stands for the two of them.
		entry.Die.SetHovered(hovered);
		DieEntry other = PartnerEntry(entry);
		if (other != null && IsInstanceValid(other.Die))
			other.Die.SetHovered(hovered);
	}

	/// <summary>
	/// How far to magnify the die in its row, and where it sits inside its frame.
	///
	/// Taken from the resting pose and then applied to every frame, so a die that starts
	/// tumbling does not jump in size — the blur simply spills past the edges of the
	/// little window and is clipped, which is what a die going past a window looks like.
	/// </summary>
	private static void MeasureIcon(DieEntry entry)
	{
		if (entry.Die.RestingFrame(1) is not AtlasTexture full)
			return;
		var cropped = DicePalette.CropToDie(full) as AtlasTexture ?? full;
		Vector2 crop = cropped.Region.Size;
		if (crop.X <= 0f || crop.Y <= 0f)
			return;
		entry.IconZoom = IconSize / Mathf.Max(crop.X, crop.Y);
		entry.IconCentre = cropped.Region.Position - full.Region.Position + crop / 2f;
	}

	public bool IsOpen => isOpen;

	public void SetOpen(bool open, bool animate)
	{
		// Only when the player did it. The silent form is how the screenshot tool and the
		// harnesses arrange the panel, and neither wants a noise for it.
		if (animate && open != isOpen)
			Sfx.Play(open ? "ui_open" : "ui_close");
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
}
