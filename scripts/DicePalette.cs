using Godot;
using System.Collections.Generic;

public partial class DicePalette : Control
{
	/// `theme` is the slot's own colour scheme, set by right-clicking it. It travels with
	/// the spawn rather than being looked up afterwards, because by the time the die
	/// exists the drag that asked for it is over.
	[Signal]
	public delegate void SpawnRequestedEventHandler(PackedScene scene,
		Vector2 screenPosition, int theme);

	public Godot.Collections.Array<PackedScene> DiceScenes { get; set; } = new();

	/// Four across and two down holds the whole pack without scrolling, which is what
	/// sets the width: four buttons, three gaps between them and a margin either side.
	private const int Columns = 4;
	private const float ButtonSize = 50f;
	private const float ButtonGap = 5f;
	private const float DrawerMargin = 12f;
	/// Public because the die list matches it: the two panels stack into one column down
	/// the left edge, and a column whose halves are different widths reads as a mistake.
	public const float DrawerWidth =
		Columns * ButtonSize + (Columns - 1) * ButtonGap + DrawerMargin * 2;

	/// How far in from the left edge the column sits. Shared with the die list below it.
	public const float DrawerLeft = 16f;

	/// Blank pixels left around the die inside its button.
	private const int IconPadding = 3;

	/// How far below the top edge the drawer floats.
	private const float DrawerTop = 8f;

	private PanelContainer drawer;
	private Button toggleButton;
	private TextureRect dragPreview;
	private PanelContainer nameTag;
	private Label nameTagLabel;
	// One entry per die in the pack, in the order they sit in the grid.
	private readonly List<Button> slotButtons = new();
	private readonly List<TextureRect> slotIcons = new();
	private readonly List<int> slotThemes = new();
	private readonly List<string> slotNames = new();

	private PackedScene draggingScene;
	private Texture2D draggingIcon;
	private int draggingSlot = -1;
	private bool isOpen;
	private bool isDraggingIcon;
	private Tween drawerTween;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		BuildInterface();
		SetDrawerOpen(false, false);
	}

	public override void _Process(double delta)
	{
		if (isDraggingIcon && dragPreview != null)
			dragPreview.Position = GetViewport().GetMousePosition() - dragPreview.Size / 2f;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Keycode == Key.Tab && key.Pressed && !key.Echo)
		{
			SetDrawerOpen(!isOpen, true);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!isDraggingIcon || @event is not InputEventMouseButton mouseButton
			|| mouseButton.ButtonIndex != MouseButton.Left || mouseButton.Pressed)
			return;

		// The release's own position, not the current cursor. They are the same thing in
		// the game and not in a harness — Input.WarpMouse does nothing headless — and the
		// drop should mean where the button came up, which is what the event carries.
		Vector2 mouse = mouseButton.Position;
		isDraggingIcon = false;
		dragPreview?.QueueFree();
		dragPreview = null;

		if (mouse.X > DrawerLeft + DrawerWidth && draggingScene != null)
			EmitSignal(SignalName.SpawnRequested, draggingScene, mouse,
				draggingSlot >= 0 ? slotThemes[draggingSlot] : DiceTheme.Bone);
		draggingScene = null;
		draggingSlot = -1;
		GetViewport().SetInputAsHandled();
	}

	private void BuildInterface()
	{
		// Only as tall as it needs to be. Anchored full height it was nine tenths empty
		// once the dice fitted on two rows, which reads as something failing to load.
		drawer = new PanelContainer { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
		drawer.SetAnchorsPreset(LayoutPreset.TopLeft);
		drawer.OffsetTop = DrawerTop;
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
		margin.AddThemeConstantOverride("margin_left", (int)DrawerMargin);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_right", (int)DrawerMargin);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		drawer.AddChild(margin);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 10);
		margin.AddChild(content);

		var title = new Label { Text = "DICE" };
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 20);
		content.AddChild(title);

		// A grid rather than a scrolling column. Eight dice down one side left half the
		// pack behind a scrollbar nobody thinks to drag.
		var grid = new GridContainer
		{
			Columns = Columns,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter
		};
		grid.AddThemeConstantOverride("h_separation", (int)ButtonGap);
		grid.AddThemeConstantOverride("v_separation", (int)ButtonGap);
		content.AddChild(grid);

		foreach (PackedScene scene in DiceScenes)
		{
			if (scene == null)
				continue;
			(string label, Texture2D icon) = DescribeDie(scene);
			AddDieOption(grid, label, CropToDie(icon), scene);
		}

		// Both gestures in one label at one size. As two labels they cost a third line,
		// and the drawer grew until it was all but touching the die list below it.
		var hint = new Label
		{
			Text = "Drag a die onto the board\nRight-click for a theme",
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(0.74f, 0.78f, 0.88f)
		};
		hint.AddThemeFontSizeOverride("font_size", 12);
		content.AddChild(hint);

		toggleButton = new Button
		{
			Name = "Toggle",
			Text = "▶",
			TooltipText = "Open dice menu",
			MouseFilter = MouseFilterEnum.Stop,
			FocusMode = FocusModeEnum.None
		};
		toggleButton.SetAnchorsPreset(LayoutPreset.TopLeft);
		toggleButton.Pressed += () => SetDrawerOpen(!isOpen, true);
		AddChild(toggleButton);

		// Added last so it draws over the drawer, and ignores the mouse so pointing at a
		// button cannot put the tag between the pointer and the button it describes.
		nameTag = new PanelContainer
		{
			Name = "NameTag",
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false
		};
		var tagStyle = new StyleBoxFlat
		{
			BgColor = new Color("202536f0"),
			BorderColor = new Color("77819b"),
			ContentMarginLeft = 9,
			ContentMarginRight = 9,
			ContentMarginTop = 4,
			ContentMarginBottom = 4
		};
		tagStyle.SetBorderWidthAll(2);
		tagStyle.SetCornerRadiusAll(8);
		nameTag.AddThemeStyleboxOverride("panel", tagStyle);
		AddChild(nameTag);

		nameTagLabel = new Label { MouseFilter = MouseFilterEnum.Ignore };
		nameTagLabel.AddThemeFontSizeOverride("font_size", 16);
		nameTag.AddChild(nameTagLabel);

		drawer.OffsetBottom = DrawerTop + drawer.GetCombinedMinimumSize().Y;
	}

	private void AddDieOption(Container list, string label, Texture2D icon,
		PackedScene scene)
	{
		int slot = slotButtons.Count;

		// No caption on the button. Eight of them will not fit beside the icons at this
		// size, and the die itself says which it is — except for the two pairs that
		// share a shape, which is what the hover name is for.
		var button = new Button
		{
			Name = $"DieSlot{slot}",
			CustomMinimumSize = new Vector2(ButtonSize, ButtonSize),
			FocusMode = FocusModeEnum.None
		};
		button.GuiInput += e => OnDieButtonInput(e, scene, icon, slot);
		button.MouseEntered += () => ShowNameTag(label, button);
		button.MouseExited += () => { if (nameTag != null) nameTag.Visible = false; };
		list.AddChild(button);

		// The die goes in a child rather than in the button's own Icon, because a themed
		// slot needs a material and a material on the Button would recolour its
		// background along with the die.
		var art = new TextureRect
		{
			Name = "Art",
			Texture = icon,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore
		};
		art.SetAnchorsPreset(LayoutPreset.FullRect);
		art.OffsetLeft = IconPadding;
		art.OffsetTop = IconPadding;
		art.OffsetRight = -IconPadding;
		art.OffsetBottom = -IconPadding;
		button.AddChild(art);

		slotButtons.Add(button);
		slotIcons.Add(art);
		slotThemes.Add(DiceTheme.Bone);
		slotNames.Add(label);
	}

	/// <summary>
	/// Which slot a Control belongs to, or -1. Asked with whatever
	/// <c>GuiGetHoveredControl</c> returned, which may be the button or something inside
	/// it, so the walk goes upward.
	/// </summary>
	public int SlotOf(Node control)
	{
		for (Node node = control; node != null; node = node.GetParent())
		{
			int index = slotButtons.IndexOf(node as Button);
			if (index >= 0)
				return index;
		}
		return -1;
	}

	public int SlotTheme(int slot) =>
		slot >= 0 && slot < slotThemes.Count ? slotThemes[slot] : DiceTheme.Bone;

	public string SlotName(int slot) =>
		slot >= 0 && slot < slotNames.Count ? slotNames[slot] : "";

	/// Paint a slot, and remember it. Only the dice made from here afterwards are
	/// affected — what is already on the board keeps whatever it was given.
	public void SetSlotTheme(int slot, int theme)
	{
		if (slot < 0 || slot >= slotThemes.Count)
			return;
		slotThemes[slot] = theme;
		slotIcons[slot].Material = DiceTheme.MaterialFor(theme);
	}

	/// <summary>
	/// Name the die being pointed at, in a tag beside its button.
	///
	/// Anchored rather than following the cursor: the buttons sit on a grid, so a tag
	/// pinned to one lands somewhere predictable. Level with the button it names, but
	/// clear of the *drawer* rather than of the button — hung off the button it covered
	/// whichever ones were beside it, which is three quarters of them.
	/// </summary>
	private void ShowNameTag(string label, Control button)
	{
		nameTagLabel.Text = label;
		nameTag.ResetSize();      // a PanelContainer outside a container keeps its old size
		nameTag.Visible = true;
		Rect2 box = button.GetGlobalRect();
		nameTag.Position = new Vector2(
			Mathf.Min(drawer.GetGlobalRect().End.X + 10f,
				GetViewportRect().Size.X - nameTag.Size.X - 8f),
			box.Position.Y + (box.Size.Y - nameTag.Size.Y) / 2f);
	}

	/// <summary>
	/// The die's own corner of its 128px cell, so a button can be the size of the die
	/// rather than the size of the frame it was rendered in.
	///
	/// Measured off the icon's alpha rather than fixed, because the dice are not all the
	/// same size in frame — the d4 is 70px across and the numbered d6 48 — and a fixed
	/// window would leave the small ones swimming. Cropping each to itself makes every
	/// button equally full.
	/// </summary>
	public static Texture2D CropToDie(Texture2D icon)
	{
		if (icon is not AtlasTexture cell || cell.Atlas == null)
			return icon;
		Image image = cell.GetImage();
		if (image == null)
			return icon;

		// GetImage() on an AtlasTexture yields the region; on anything else the lot.
		bool whole = image.GetWidth() > (int)cell.Region.Size.X;
		var origin = whole ? (Vector2I)cell.Region.Position : Vector2I.Zero;
		var size = (Vector2I)cell.Region.Size;

		int x0 = size.X, y0 = size.Y, x1 = -1, y1 = -1;
		for (int y = 0; y < size.Y; y++)
			for (int x = 0; x < size.X; x++)
				if (image.GetPixel(origin.X + x, origin.Y + y).A > 0.1f)
				{
					if (x < x0) x0 = x;
					if (x > x1) x1 = x;
					if (y < y0) y0 = y;
					if (y > y1) y1 = y;
				}
		if (x1 < x0 || y1 < y0)
			return icon;            // nothing drawn; leave it alone

		x0 = Mathf.Max(0, x0 - IconPadding);
		y0 = Mathf.Max(0, y0 - IconPadding);
		x1 = Mathf.Min(size.X - 1, x1 + IconPadding);
		y1 = Mathf.Min(size.Y - 1, y1 + IconPadding);
		return new AtlasTexture
		{
			Atlas = cell.Atlas,
			Region = new Rect2(cell.Region.Position + new Vector2(x0, y0),
				new Vector2(x1 - x0 + 1, y1 - y0 + 1))
		};
	}

	/// <summary>
	/// What to call a die type and what to show for it, both read off the scene rather
	/// than hardcoded: the number of numbered clips is the number of faces, and the last
	/// frame of clip "1" is that die sitting at rest.
	/// </summary>
	private static (string, Texture2D) DescribeDie(PackedScene scene)
	{
		var probe = scene.Instantiate<Dice>();
		string label = probe.DisplayName;
		Texture2D icon = probe.RestingFrame(1);
		probe.Free();               // never entered the tree, so Free not QueueFree
		return (label, icon);
	}

	private void OnDieButtonInput(InputEvent @event, PackedScene scene, Texture2D icon,
		int slot)
	{
		if (@event is not InputEventMouseButton mouseButton
			|| mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
			return;

		Sfx.Play("ui_click");
		isDraggingIcon = true;
		draggingScene = scene;
		draggingIcon = icon;
		draggingSlot = slot;
		dragPreview = new TextureRect
		{
			Texture = draggingIcon,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Size = new Vector2(96, 96),
			Modulate = new Color(1, 1, 1, 0.8f),
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 100,
			// The ghost wears the slot's colour, so what you drop is what you dragged.
			Material = DiceTheme.MaterialFor(slotThemes[slot])
		};
		AddChild(dragPreview);
		dragPreview.Position = GetViewport().GetMousePosition() - dragPreview.Size / 2f;
		GetViewport().SetInputAsHandled();
	}

	public void SetDrawerOpen(bool open, bool animate)
	{
		if (animate && open != isOpen)
			Sfx.Play(open ? "ui_open" : "ui_close");
		isOpen = open;
		float panelLeft = open ? DrawerLeft : -DrawerWidth;
		float panelRight = open ? DrawerLeft + DrawerWidth : 0f;
		float toggleLeft = open ? DrawerLeft + DrawerWidth + 8f : 8f;
		float toggleRight = open ? DrawerLeft + DrawerWidth + 48f : 48f;

		drawerTween?.Kill();
		if (animate)
		{
			drawerTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic)
				.SetEase(Tween.EaseType.Out);
			drawerTween.TweenProperty(drawer, "offset_left", panelLeft, 0.22);
			drawerTween.TweenProperty(drawer, "offset_right", panelRight, 0.22);
			drawerTween.TweenProperty(toggleButton, "offset_left", toggleLeft, 0.22);
			drawerTween.TweenProperty(toggleButton, "offset_right", toggleRight, 0.22);
		}
		else
		{
			drawer.OffsetLeft = panelLeft;
			drawer.OffsetRight = panelRight;
			toggleButton.OffsetLeft = toggleLeft;
			toggleButton.OffsetRight = toggleRight;
		}

		toggleButton.Text = open ? "◀" : "▶";
		toggleButton.TooltipText = open ? "Close dice menu" : "Open dice menu";
	}
}
