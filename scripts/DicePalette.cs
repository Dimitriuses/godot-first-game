using Godot;

public partial class DicePalette : Control
{
	[Signal]
	public delegate void SpawnRequestedEventHandler(PackedScene scene, Vector2 screenPosition);

	public Godot.Collections.Array<PackedScene> DiceScenes { get; set; } = new();

	private const float DrawerWidth = 180f;
	private PanelContainer drawer;
	private Button toggleButton;
	private TextureRect dragPreview;
	private PackedScene draggingScene;
	private Texture2D draggingIcon;
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

		Vector2 mouse = GetViewport().GetMousePosition();
		isDraggingIcon = false;
		dragPreview?.QueueFree();
		dragPreview = null;

		if (mouse.X < GetViewportRect().Size.X - DrawerWidth && draggingScene != null)
			EmitSignal(SignalName.SpawnRequested, draggingScene, mouse);
		draggingScene = null;
		GetViewport().SetInputAsHandled();
	}

	private void BuildInterface()
	{
		drawer = new PanelContainer { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
		drawer.SetAnchorsPreset(LayoutPreset.RightWide);
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
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		drawer.AddChild(margin);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 12);
		margin.AddChild(content);

		var title = new Label { Text = "DICE" };
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 20);
		content.AddChild(title);

		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		content.AddChild(scroll);

		var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		list.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(list);

		foreach (PackedScene scene in DiceScenes)
		{
			if (scene == null)
				continue;
			(string label, Texture2D icon) = DescribeDie(scene);
			AddDieOption(list, label, icon, $"Drag onto the board to add a {label}", scene);
		}

		var hint = new Label
		{
			Text = "Drag a die onto\nthe board",
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(0.78f, 0.82f, 0.9f)
		};
		content.AddChild(hint);

		toggleButton = new Button
		{
			Name = "Toggle",
			Text = "◀",
			TooltipText = "Open dice menu",
			MouseFilter = MouseFilterEnum.Stop,
			FocusMode = FocusModeEnum.None
		};
		toggleButton.SetAnchorsPreset(LayoutPreset.TopRight);
		toggleButton.Pressed += () => SetDrawerOpen(!isOpen, true);
		AddChild(toggleButton);
	}

	private void AddDieOption(Container list, string label, Texture2D icon, string tooltip,
		PackedScene scene)
	{
		var d6Button = new Button
		{
			Text = label,
			Icon = icon,
			ExpandIcon = true,
			CustomMinimumSize = new Vector2(140, 126),
			TooltipText = tooltip,
			FocusMode = FocusModeEnum.None
		};
		d6Button.AddThemeConstantOverride("icon_max_width", 92);
		d6Button.GuiInput += e => OnDieButtonInput(e, scene, icon);
		list.AddChild(d6Button);
	}

	/// <summary>
	/// What to call a die type and what to show for it, both read off the scene rather
	/// than hardcoded: the number of numbered clips is the number of faces, and the last
	/// frame of clip "1" is that die sitting at rest.
	/// </summary>
	private static (string, Texture2D) DescribeDie(PackedScene scene)
	{
		var probe = scene.Instantiate<Dice>();
		// The face count names the die unless it cannot: a pipped and a numbered d6
		// both have six, so a die may carry its own label to break the tie.
		string label = string.IsNullOrEmpty(probe.DieLabel)
			? $"d{probe.FaceCount}" : probe.DieLabel;
		Texture2D icon = null;
		SpriteFrames frames = probe.AnimatedSprite?.SpriteFrames;
		if (frames != null && frames.HasAnimation("1"))
			icon = frames.GetFrameTexture("1", frames.GetFrameCount("1") - 1);
		probe.Free();               // never entered the tree, so Free not QueueFree
		return (label, icon);
	}

	private void OnDieButtonInput(InputEvent @event, PackedScene scene, Texture2D icon)
	{
		if (@event is not InputEventMouseButton mouseButton
			|| mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
			return;

		isDraggingIcon = true;
		draggingScene = scene;
		draggingIcon = icon;
		dragPreview = new TextureRect
		{
			Texture = draggingIcon,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Size = new Vector2(96, 96),
			Modulate = new Color(1, 1, 1, 0.8f),
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 100
		};
		AddChild(dragPreview);
		dragPreview.Position = GetViewport().GetMousePosition() - dragPreview.Size / 2f;
		GetViewport().SetInputAsHandled();
	}

	public void SetDrawerOpen(bool open, bool animate)
	{
		isOpen = open;
		float panelLeft = open ? -DrawerWidth : 0f;
		float panelRight = open ? 0f : DrawerWidth;
		float toggleLeft = open ? -DrawerWidth - 48f : -48f;
		float toggleRight = open ? -DrawerWidth - 8f : -8f;

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

		toggleButton.Text = open ? "▶" : "◀";
		toggleButton.TooltipText = open ? "Close dice menu" : "Open dice menu";
	}
}
