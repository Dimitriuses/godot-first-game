using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node2D
{
	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;
	/// One entry per die type the palette offers. An array rather than a single scene
	/// so a d20 is an extra element, not a code change.
	/// <summary>
	/// The pack, read from a manifest rather than held as an exported array of
	/// `PackedScene`.
	///
	/// That is not a style choice. Loading a die's `PackedScene` loads its whole sheet
	/// set — measured at 180 MB of texture memory for the d20 — and an exported array
	/// loads every entry the moment `game.tscn` does, whether or not a die is ever
	/// thrown. Holding paths and loading on demand is what keeps a board of two d6 from
	/// paying for a d20 nobody touched.
	/// </summary>
	[Export] public string PackPath = "res://assets/dice/pack.json";

	/// One entry per die in the pack, in palette order.
	public readonly List<(string Scene, string Name, Rect2 Icon)> Pack = new();

	/// Where each die's art sits relative to its origin, and how much its icon was
	/// resized to fit the sheet. Parallel to Pack; used only by the loading placeholder.
	private readonly List<Vector2> packOffsets = new();
	private readonly List<float> packScales = new();

	/// Scenes already loaded, kept so a second d6 costs nothing. Godot caches resources
	/// itself; this only saves the repeated path lookup.
	private readonly Dictionary<string, PackedScene> loadedScenes = new();

	/// <summary>
	/// A die that has been asked for but whose scene is still loading.
	///
	/// The first d20 of a session takes about half a second to load — measured at 672ms
	/// blocking, 521ms threaded — and a die that takes half a second to turn up after
	/// the player drops it feels broken. So the palette's icon stands in: it is already
	/// in memory, it is the same artwork, and it plays the same arrival animation. The
	/// real die replaces it once both the load and the animation have finished, which
	/// makes the swap invisible — by then the placeholder is sitting still at full size,
	/// showing exactly what the die will show.
	/// </summary>
	private sealed class PendingSpawn
	{
		public string Path;
		public Vector2 At;
		public int Theme;
		public Sprite2D Ghost;
		public double Elapsed;
	}

	private readonly List<PendingSpawn> pendingSpawns = new();
	private Texture2D iconSheet;

	/// Fastest a dragged die is steered or released at, px/s. Without a bound, a flick
	/// hands the solver an impulse it has to fight, which is how dice used to come out
	/// through the walls.
	[Export] public float MaxDragSpeed = 4000f;

	/// Radius of the clump a Shift-drag gathers dice into, per die. The clump grows as
	/// the square root of the count, because that is how discs pack into a disc.
	[Export] public float GatherSpread = 38f;

	/// How hard and how long the board shudders when every die is thrown at once.
	/// Small on purpose: it is a table being knocked, not an earthquake.
	[Export] public float ShakeStrength = 5f;
	[Export] public float ShakeDuration = 0.3f;
	[Export] public float ShakeFrequency = 55f;

	/// <summary>
	/// Whether this board reads and writes the save file.
	///
	/// Off for the screenshot tool and for harnesses. Both would otherwise inherit
	/// whatever board the machine happened to have saved — and, worse, overwrite it on
	/// the way out.
	/// </summary>
	[Export] public bool PersistBoard = true;

	private readonly List<Dice> dice = new();
	private readonly List<Dice> selectedDice = new();
	private readonly HashSet<Dice> deletingDice = new();
	private Dice draggedDie;
	private Dice activeDie;
	private Dice hoveredDie;
	private DiceHud diceHud;
	private DiceMenu diceMenu;
	private DicePalette palette;
	private MuteButton muteButton;
	private GroupDragButton groupDragButton;

	/// Whether a drag takes every die, as holding Shift does. A toggle rather than a
	/// modifier because a touchscreen has no modifiers to hold.
	private bool groupDrag;

	/// Shaking the device throws the board, where there is a device to shake. Zero on a
	/// desktop and in a browser, so Feed is never reached and this costs nothing there.
	private readonly ShakeGesture shakeGesture = new();
	private CanvasLayer uiLayer;

	/// A copy waiting to be put down: the die type taken, the face it was showing, and
	/// the ghost that follows the cursor until a click places it.
	private string pendingCopyScene;
	private int pendingCopyFace;
	private int pendingCopyTheme;
	private TextureRect copyPreview;

	/// The die waiting to be paired, while its possible partners stand highlighted.
	private Dice pendingLink;
	private bool swallowNextDieClick;

	/// Set when a double tap has just opened a menu, so the emulated mouse press that
	/// Godot generates from the same tap does not also act on it.
	private bool swallowNextPress;

	/// Seconds of shudder left, and the direction it runs along.
	private float shakeLeft;
	private Vector2 shakeAxis = Vector2.Right;
	private bool isDragging;
	private bool isGroupDragging;
	private Vector2 lastMousePosition;
	private Vector2 dragVelocity;
	private Vector2 spawnPosition;

	/// The last state written, as JSON. The autosave compares against it rather than
	/// tracking a dirty flag through a dozen call sites: it catches the mute button and
	/// the palette's colours as readily as it catches a die being deleted, and a settled
	/// board serialises identically and so writes nothing.
	private string lastSaved = "";
	private ulong nextSaveCheckMs;
	/// How often the board is compared against what is on disk. This is the primary way
	/// a save happens, not a backstop: a browser tab can close without anything being
	/// notified, so the last second of play is what a web build stands to lose.
	private const ulong SaveCheckMs = 1000;
	private Vector2 singleGrabOffset;
	private Rect2 boardBounds;

	public override void _Ready()
	{
		MousePin.NodeA = MousePin.GetPathTo(FakeBody);
		DiceArea.BodyExited += OnBodyExited;
		boardBounds = ComputeBoardBounds();

		LoadPack();

		// Before the panels, so anything that makes a noise while building has somewhere
		// to send it.
		AddChild(new Sfx { Name = "Sfx" });

		uiLayer = new CanvasLayer { Name = "GameUiLayer" };
		AddChild(uiLayer);
		palette = new DicePalette { Name = "DicePalette", Pack = Pack };
		uiLayer.AddChild(palette);
		palette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		palette.SpawnRequested += (path, at, theme) => SpawnDie(path, at, (int)theme);

		diceHud = new DiceHud { Name = "DiceHud" };
		uiLayer.AddChild(diceHud);
		diceHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		diceHud.DeleteRequested += DeleteDie;
		diceHud.DeleteAllRequested += DeleteAllDice;

		muteButton = new MuteButton { Name = "MuteButton" };
		MuteButton mute = muteButton;
		uiLayer.AddChild(mute);
		mute.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		groupDragButton = new GroupDragButton { Name = "GroupDragButton" };
		uiLayer.AddChild(groupDragButton);
		groupDragButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		groupDragButton.Toggled += value => groupDrag = value;

		diceMenu = new DiceMenu { Name = "DiceMenu" };
		uiLayer.AddChild(diceMenu);
		diceMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		diceMenu.RollRequested += RollOne;
		diceMenu.CopyRequested += BeginCopy;
		diceMenu.DeleteRequested += DeleteDie;
		diceMenu.LinkRequested += BeginLink;
		diceMenu.UnlinkRequested += Unlink;
		diceMenu.ThrowAllRequested += ThrowAllDice;
		diceMenu.RespawnRequested += OnSpawnButton;
		diceMenu.DeleteAllRequested += DeleteAllDice;
		diceMenu.ThemeRequested += OnThemeChosen;

		foreach (Node child in GetChildren())
			if (child is Dice die)
				RegisterDie(die);

		if (dice.Count > 0)
		{
			spawnPosition = dice[0].Position;
			SelectOnly(dice[0]);
		}

		// After the spawn point is taken from the scene's own die, because that die is
		// about to be cleared away and the respawn anchor should not go with it.
		if (PersistBoard && SaveGame.Exists())
			ApplySave(SaveGame.Load());

		UpdateProcessing();         // idle until a copy or a shake needs the frame
	}

	/// <summary>
	/// A real quit, while the tree is still standing.
	///
	/// **Not `_ExitTree`**, which was the first attempt and is too late: children leave
	/// the tree before their parent, so every die has already run its `TreeExiting`
	/// handler and taken itself out of the list, and `Sfx.Instance` has already been
	/// cleared. The board saved there is an empty one that is not muted.
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest && PersistBoard)
			SaveIfChanged();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (PersistBoard && Time.GetTicksMsec() >= nextSaveCheckMs)
		{
			nextSaveCheckMs = Time.GetTicksMsec() + SaveCheckMs;
			SaveIfChanged();
		}

		if (pendingSpawns.Count > 0)
			StepPendingSpawns(delta);

		// A phone being shaken is the space bar. Godot returns zero from the accelerometer
		// on any platform without one, which is every platform this currently ships to —
		// see the note in ShakeGesture about the browser.
		Vector3 acceleration = Input.GetAccelerometer();
		if (acceleration != Vector3.Zero && shakeGesture.Feed(acceleration, delta)
			&& dice.Count > 0)
			ThrowAllDice();

		Vector2 mouse = GetGlobalMousePosition();

		// Let the cursor leave the board, but never let the pin follow it out. Dragged
		// unclamped, the joint hauls the die into a wall and the solver fights back hard
		// enough to squeeze it through — worst in the corners, where two walls push at once.
		Vector2 target = mouse;
		if (isDragging && !isGroupDragging && draggedDie != null && IsInstanceValid(draggedDie))
			target = ClampInto(OriginBoundsFor(draggedDie), mouse + singleGrabOffset)
				- singleGrabOffset;
		MousePin.GlobalPosition = target;

		if (!isDragging || delta <= 0)
			return;

		// Measured from the clamped point, not the raw cursor: once the die is up against
		// a wall it is not moving, so letting go there should not fling it.
		dragVelocity = (target - lastMousePosition) / (float)delta;
		lastMousePosition = target;

		if (isGroupDragging)
		{
			// Steer, rather than teleport: a frozen body moved by assigning GlobalPosition
			// ignores walls and other dice completely, while a body given a velocity gets
			// stopped by the solver.
			//
			// Everything is drawn to the cursor and nothing keeps the arrangement it was
			// picked up in. Holding the original formation meant each die fought to reach a
			// slot that was inside a wall, which is what made them strain against it.
			float gather = GatherSpread * Mathf.Sqrt(Mathf.Max(1, selectedDice.Count));
			foreach (Dice die in selectedDice)
			{
				if (!IsInstanceValid(die))
					continue;
				Vector2 toTarget = ClampInto(OriginBoundsFor(die), target) - die.GlobalPosition;
				float far = toTarget.Length() - gather;
				if (far > 0f)
					die.LinearVelocity = (toTarget.Normalized() * far / (float)delta)
						.LimitLength(MaxDragSpeed);
				else
					// Already in the clump: stop pushing and let them settle against each
					// other, so a pile against a wall sits still instead of shoving.
					die.LinearVelocity *= 0.8f;
			}
		}

		// Last line of defence. Steering a die by setting its velocity overrides the
		// contact response, so a die caught between another die and a wall gets extruded
		// through it — measured at 22px past the wall before this was added. Nudging it
		// back is a few pixels and only ever happens under that squeeze.
		foreach (Dice die in selectedDice)
		{
			if (!IsInstanceValid(die))
				continue;
			Vector2 inside = ClampInto(OriginBoundsFor(die), die.GlobalPosition);
			if (!inside.IsEqualApprox(die.GlobalPosition))
				die.GlobalPosition = inside;
		}
	}

	/// The playable rectangle, inside the walls. Worked out once at _Ready.
	public Rect2 BoardBounds => boardBounds;

	/// The playable rectangle, derived from the wall colliders rather than hardcoded, so
	/// nudging a wall in the editor moves the drag limit with it.
	private Rect2 ComputeBoardBounds()
	{
		var slabs = new List<Rect2>();
		foreach (Node child in GetChildren())
		{
			if (child is not StaticBody2D walls)
				continue;
			foreach (Node part in walls.GetChildren())
				if (part is CollisionShape2D cs && cs.Shape is RectangleShape2D rect)
				{
					Vector2 size = rect.Size * cs.GlobalScale.Abs();
					slabs.Add(new Rect2(cs.GlobalPosition - size / 2, size));
				}
		}
		if (slabs.Count == 0)
			return new Rect2(Vector2.Zero, GetViewportRect().Size);

		Rect2 outer = slabs[0];
		foreach (Rect2 s in slabs)
			outer = outer.Merge(s);

		// Each slab is one side of the frame; take its inner face.
		Vector2 mid = outer.Position + outer.Size / 2;
		float left = outer.Position.X, right = outer.End.X;
		float top = outer.Position.Y, bottom = outer.End.Y;
		foreach (Rect2 s in slabs)
		{
			Vector2 c = s.Position + s.Size / 2;
			if (s.Size.X >= s.Size.Y)
			{
				if (c.Y < mid.Y) top = Mathf.Max(top, s.End.Y);
				else bottom = Mathf.Min(bottom, s.Position.Y);
			}
			else
			{
				if (c.X < mid.X) left = Mathf.Max(left, s.End.X);
				else right = Mathf.Min(right, s.Position.X);
			}
		}
		return new Rect2(left, top, Mathf.Max(0, right - left), Mathf.Max(0, bottom - top));
	}

	/// Where a die's origin may sit so that its collider stays inside the walls. The
	/// collider is offset from the origin, so that offset has to come back out.
	private Rect2 OriginBoundsFor(Dice die)
	{
		float r = die.CollisionRadius;
		Vector2 size = boardBounds.Size - new Vector2(r, r) * 2f;
		return new Rect2(
			boardBounds.Position + new Vector2(r, r) - die.CollisionOffset,
			new Vector2(Mathf.Max(0, size.X), Mathf.Max(0, size.Y)));
	}

	private static Vector2 ClampInto(Rect2 box, Vector2 p) => new(
		Mathf.Clamp(p.X, box.Position.X, box.End.X),
		Mathf.Clamp(p.Y, box.Position.Y, box.End.Y));

	public override void _Input(InputEvent @event)
	{
		// A double tap is what a screen has instead of a second mouse button. Godot
		// reports the touch itself and, with emulate_mouse_from_touch on, a mouse event
		// alongside it — this reads the touch, and the flag stops the mouse copy from
		// acting on the same tap.
		if (@event is InputEventScreenTouch { Pressed: true, DoubleTap: true } tap)
		{
			OpenContextMenu(tap.Position);
			swallowNextPress = true;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventKey shiftKey && shiftKey.Keycode == Key.Shift
			&& shiftKey.Pressed && !shiftKey.Echo)
		{
			if (!isDragging)        // changing the selection mid-drag would be a surprise
				SelectAll();
			return;
		}

		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Space)
			{
				ThrowAllDice();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (key.Keycode == Key.Escape && (pendingCopyScene != null
				|| pendingLink != null || diceMenu.Target != null))
			{
				CancelCopy();
				CancelLink();
				diceMenu.Close();
				GetViewport().SetInputAsHandled();
				return;
			}

			// The menu's actions, on whichever die is under the cursor — or on the one
			// the open menu belongs to, since pointing at an item means not pointing at
			// the die any more.
			Dice subject = diceMenu.Target ?? hoveredDie;
			if (subject != null && IsInstanceValid(subject) && !isDragging)
			{
				if (key.Keycode == DiceMenu.RollKey)
				{
					RollOne(subject);
					diceMenu.Close();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (key.Keycode == DiceMenu.CopyKey)
				{
					BeginCopy(subject);
					diceMenu.Close();
					GetViewport().SetInputAsHandled();
					return;
				}
			}
		}

		if (@event is not InputEventMouseButton mouseButton || !mouseButton.Pressed)
		{
			if (isDragging && @event is InputEventMouseButton release
				&& release.ButtonIndex == MouseButton.Left && !release.Pressed)
			{
				ReleaseDraggedDice();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		// The emulated press that follows a double tap. Godot turns touches into mouse
		// events as well as reporting them, so without this the tap that opened the menu
		// goes on to grab whatever it was over.
		if (swallowNextPress && mouseButton.Pressed)
		{
			swallowNextPress = false;
			GetViewport().SetInputAsHandled();
			return;
		}
		swallowNextPress = false;

		// The event's own position, not the current cursor: they are the same thing in
		// the game and not in a harness, and the click should mean where it was made.
		Vector2 point = mouseButton.Position;
		// Cleared at the top of every press so a flag set for a click the die never
		// heard about cannot go on to swallow a later one.
		swallowNextDieClick = false;

		// A press on the menu is the menu's. Closing it here would take the panel away
		// before the button saw the release, and no item would ever fire.
		if (diceMenu.Covers(point))
			return;

		if (mouseButton.ButtonIndex == MouseButton.Right)
		{
			OpenContextMenu(point);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mouseButton.ButtonIndex != MouseButton.Left)
			return;

		diceMenu.Close();

		// _Input runs before the GUI gets a look, so a click on the palette or the die
		// list arrives here too. Acting on either at that point would drop a die behind
		// the panel that was clicked.
		if (GetViewport().GuiGetHoveredControl() != null)
			return;

		// Finishing a pairing has to happen here rather than in the die's own click
		// handler: this runs first, and whatever it decides is already done by the time
		// the die hears about the same click.
		if (pendingLink != null)
		{
			Dice source = pendingLink;
			CancelLink();
			Dice picked = DieAt(GetViewport().GetCanvasTransform().AffineInverse() * point);
			if (picked != null && Dice.CanPair(source, picked) && picked.Partner == null)
				Link(source, picked);
			// Consumed whether or not it landed on a partner: the click was the choice,
			// and it should not also grab whatever it happened to be over.
			swallowNextDieClick = true;
			GetViewport().SetInputAsHandled();
			return;
		}

		if (PlaceCopy(point, mouseButton.ShiftPressed))
			GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// The menu a right-click asks for — or a double-tap, which is the same request made
	/// with one finger on a screen that has no second button.
	///
	/// Which of the three menus it is depends on what is under the point, and that has to
	/// be settled here: `_Input` runs before the GUI and before physics picking, so
	/// waiting for either to report is waiting for a decision this has already made.
	/// </summary>
	private void OpenContextMenu(Vector2 point)
	{
		// Asked for while something is pending means "not that", and nothing more.
		bool wasPending = pendingCopyScene != null || pendingLink != null;
		CancelCopy();
		CancelLink();
		diceMenu.Close();
		if (wasPending)
			return;

		// The palette is about the kind of die, not about any die on the board, so it has
		// to be asked of the GUI before the physics world: the pointer is over a Control,
		// and DieAt would find nothing and open the board menu behind the panel.
		int slot = palette.SlotOf(GetViewport().GuiGetHoveredControl());
		if (slot >= 0)
		{
			diceMenu.OpenPalette(slot, palette.SlotName(slot), point,
				palette.SlotTheme(slot));
			return;
		}

		Dice under = DieAt(GetViewport().GetCanvasTransform().AffineInverse() * point);
		if (under != null)
			diceMenu.Open(under, point, LinkageOf(under), diceHud.LabelFor(under));
		else
			diceMenu.OpenBoard(point, dice.Count);
		swallowNextDieClick = true;
	}

	/// The die under a point on the board, or null. Asked of the physics world rather
	/// than waited for from the die itself, because picking reports after `_Input` has
	/// already run and decided.
	private Dice DieAt(Vector2 worldPoint)
	{
		var query = new PhysicsPointQueryParameters2D
		{
			Position = worldPoint,
			CollideWithBodies = true,
			CollideWithAreas = false
		};
		foreach (Godot.Collections.Dictionary hit in
			GetWorld2D().DirectSpaceState.IntersectPoint(query, 8))
			if (hit["collider"].As<GodotObject>() is Dice die)
				return die;
		return null;
	}

	private void RegisterDie(Dice die)
	{
		if (dice.Contains(die))
			return;

		dice.Add(die);
		diceHud.AddDie(die, die.Value);
		die.InputPickable = true;
		die.DiceRolled += result => OnDiceRolled(die, result);
		die.InputEvent += (Node viewport, InputEvent @event, long shapeIdx) =>
			OnDiceInput(die, @event);
		// Hovering a die names the number it is showing. A d20's up-face is small and
		// steeply foreshortened in this camera, so reading it off the die is a squint.
		die.MouseEntered += () => { hoveredDie = die; diceHud.SetDieHovered(die, true); };
		die.MouseExited += () =>
		{
			if (hoveredDie == die)
				hoveredDie = null;
			diceHud.SetDieHovered(die, false);
		};
		die.TreeExiting += () =>
		{
			dice.Remove(die);
			selectedDice.Remove(die);
			diceHud.RemoveDie(die);
			deletingDice.Remove(die);
			if (activeDie == die)
				activeDie = dice.Count > 0 ? dice[0] : null;
		};
	}

	private void OnDiceInput(Dice clickedDie, InputEvent @event)
	{
		if (isDragging || @event is not InputEventMouseButton mouseButton
			|| !mouseButton.Pressed)
			return;

		if (mouseButton.ButtonIndex != MouseButton.Left)
			return;

		// _Input already dealt with this click — as a menu, or as the second half of a
		// pairing. Either way the die has nothing left to do with it.
		if (swallowNextDieClick)
		{
			swallowNextDieClick = false;
			return;
		}
		diceMenu.Close();

		// A copy waiting to be placed wins over grabbing the die that was clicked; the
		// alternative is a click that looks dead because the copy is still on the cursor.
		if (PlaceCopy(mouseButton.Position, mouseButton.ShiftPressed))
		{
			GetViewport().SetInputAsHandled();
			return;
		}

		// The event's own modifier, not the keyboard's current state — the rule the copy
		// above already follows, and the only one a synthesised click can satisfy. The
		// toggle sits beside it because it means exactly the same thing.
		if (mouseButton.ShiftPressed || groupDrag)
		{
			SelectAll();
			activeDie = clickedDie;
		}
		else
			SelectOnly(clickedDie);

		BeginDrag(clickedDie);
		GetViewport().SetInputAsHandled();
	}

	private void BeginDrag(Dice clickedDie)
	{
		isDragging = true;
		draggedDie = clickedDie;
		Vector2 mouse = GetGlobalMousePosition();
		lastMousePosition = mouse;
		dragVelocity = Vector2.Zero;

		if (selectedDice.Count == 1)
		{
			// Remember where on the die the cursor grabbed it, so the clamp can keep the
			// die itself inside rather than just the pin.
			singleGrabOffset = clickedDie.GlobalPosition - mouse;
			MousePin.NodeB = MousePin.GetPathTo(clickedDie);
			clickedDie.StartDragging();
			clickedDie.AngularDamp = 10;
			return;
		}

		isGroupDragging = true;
		foreach (Dice die in selectedDice)
		{
			die.StartDragging();
			// Deliberately not frozen. A frozen body moved by hand is noclip: it walks
			// straight through the walls and through the other dice.
		}
	}

	/// How fast a release has to be before it counts as a throw rather than a put-down.
	private const float ThrowSoundSpeed = 220f;

	private void ReleaseDraggedDice()
	{
		if (isGroupDragging)
		{
			// The fastest of them decides, and the sound plays once for the throw rather
			// than once per die: a handful of them together is one gesture, and eight
			// copies of the same clip landing on one frame is just a louder clip.
			float fastest = 0f;
			foreach (Dice die in selectedDice)
			{
				// It is already moving at the steer velocity; do not overwrite that with
				// the raw cursor speed, which ignores whatever the die was pressed against.
				die.Freeze = false;
				die.LinearVelocity = die.LinearVelocity.LimitLength(MaxDragSpeed);
				float speed = die.LinearVelocity.Length();
				fastest = Mathf.Max(fastest, speed);
				die.ReleaseFromDrag(speed);
			}
			if (fastest > ThrowSoundSpeed)
				Sfx.Play("die_throw", 0f, 0.08f);
		}
		else if (draggedDie != null)
		{
			draggedDie.LinearVelocity = draggedDie.LinearVelocity.LimitLength(MaxDragSpeed);
			float releaseSpeed = draggedDie.LinearVelocity.Length();
			EndSingleDrag();
			draggedDie.ReleaseFromDrag(releaseSpeed);
			// A fling makes a noise; setting a die down does not.
			if (releaseSpeed > ThrowSoundSpeed)
				Sfx.Play("die_throw", 0f, 0.08f);
		}

		ClearDragState();
	}

	/// <summary>
	/// Put a die from the pack on the board, loading its scene if this is the first one.
	///
	/// The path overload is what everything outside this class should use — it is the
	/// only one that keeps the pack lazy.
	/// </summary>
	public void SpawnDie(string scenePath, Vector2 screenPosition,
		int theme = DiceTheme.Bone)
	{
		// Already in memory: nothing to wait for, so put it down the usual way.
		if (loadedScenes.ContainsKey(scenePath))
		{
			SpawnDie(SceneAtPath(scenePath), screenPosition, theme);
			return;
		}
		if (!Pack.Exists(entry => entry.Scene == scenePath))
			return;
		BeginPendingSpawn(scenePath, screenPosition, theme);
	}

	/// <summary>
	/// Start the die's scene loading and stand its icon in the meantime.
	///
	/// The icon is positioned by the manifest's offset and drawn at the reciprocal of
	/// its scale, because the icons were resized to a common cell and the dice are not
	/// the same size in frame. Get either wrong and the die jumps when it arrives.
	/// </summary>
	private void BeginPendingSpawn(string scenePath, Vector2 at, int theme)
	{
		ResourceLoader.LoadThreadedRequest(scenePath);

		Vector2 wanted = ClampSpawn(at);
		var pending = new PendingSpawn { Path = scenePath, At = wanted, Theme = theme };
		(string _, string _, Rect2 icon) = Pack.Find(e => e.Scene == scenePath);
		Vector2 offset = Vector2.Zero;
		float scale = 1f;
		int index = Pack.FindIndex(e => e.Scene == scenePath);
		if (index >= 0)
		{
			offset = packOffsets[index];
			scale = packScales[index];
		}

		iconSheet ??= GD.Load<Texture2D>("res://assets/dice/icons.png");
		pending.Ghost = new Sprite2D
		{
			Name = "Arriving",
			Texture = iconSheet == null ? null
				: new AtlasTexture { Atlas = iconSheet, Region = icon },
			// The icon cell is 64px of transparent border around cropped art, so the
			// offset lands the art where the die's own art will be.
			GlobalPosition = wanted + offset,
			Scale = Vector2.One * (Dice.AppearFrom / Mathf.Max(0.001f, scale)),
			Modulate = new Color(1f, 1f, 1f, 0f),
			Material = DiceTheme.MaterialFor(theme)
		};
		AddChild(pending.Ghost);

		// The same curve and duration Dice.Appear uses, so the handover is a continuation
		// rather than a second animation.
		float full = 1f / Mathf.Max(0.001f, scale);
		Tween tween = pending.Ghost.CreateTween().SetParallel();
		tween.TweenProperty(pending.Ghost, "scale", Vector2.One * full,
				Dice.AppearSeconds)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(pending.Ghost, "modulate:a", 1f, Dice.AppearSeconds * 0.45)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);

		Sfx.Play("spawn", 0f, 0.05f);
		pendingSpawns.Add(pending);
	}

	/// Poll the loads in flight. A die appears once its scene is ready *and* the arrival
	/// animation has run its course — waiting for both is what makes the swap invisible,
	/// because the placeholder is then sitting still at full size.
	private void StepPendingSpawns(double delta)
	{
		for (int i = pendingSpawns.Count - 1; i >= 0; i--)
		{
			PendingSpawn pending = pendingSpawns[i];
			pending.Elapsed += delta;

			var status = ResourceLoader.LoadThreadedGetStatus(pending.Path);
			if (status == ResourceLoader.ThreadLoadStatus.InProgress
				|| pending.Elapsed < Dice.AppearSeconds)
				continue;

			pendingSpawns.RemoveAt(i);
			if (IsInstanceValid(pending.Ghost))
				pending.Ghost.QueueFree();
			if (status != ResourceLoader.ThreadLoadStatus.Loaded)
			{
				GD.PushWarning($"could not load {pending.Path}");
				continue;
			}

			var scene = ResourceLoader.LoadThreadedGet(pending.Path) as PackedScene;
			if (scene == null)
				continue;
			loadedScenes[pending.Path] = scene;
			// Silent and unanimated: the placeholder already made the noise and played
			// the arrival.
			SpawnDie(scene, pending.At, pending.Theme, animate: false, quiet: true);
		}
	}

	/// <summary>
	/// Where a die dropped at this point should be seen.
	///
	/// Clamped in terms of the *drawn* die, not its origin: a die is drawn about twelve
	/// pixels below its own origin, so clamping the origin left every spawn sitting low.
	/// The caller subtracts the collider offset to get the origin.
	/// </summary>
	private Vector2 ClampSpawn(Vector2 screenPosition)
	{
		Vector2 view = GetViewportRect().Size;
		return new Vector2(
			Mathf.Clamp(screenPosition.X, 80f, view.X - 240f),
			Mathf.Clamp(screenPosition.Y, 90f, view.Y - 80f));
	}

	/// The die at this place in the pack, by palette order.
	public void SpawnDie(int slot, Vector2 screenPosition,
		int theme = DiceTheme.Bone)
	{
		if (slot >= 0 && slot < Pack.Count)
			SpawnDie(Pack[slot].Scene, screenPosition, theme);
	}

	public void SpawnDie(PackedScene scene, Vector2 screenPosition,
		int theme = DiceTheme.Bone, bool animate = true, bool quiet = false)
	{
		if (scene == null)
			return;

		Dice die = scene.Instantiate<Dice>();
		AddChild(die);
		die.Theme = theme;
		if (animate)
			die.Appear();
		if (!quiet)
			Sfx.Play("spawn", 0f, 0.05f);
		die.GlobalPosition = ClampSpawn(screenPosition) - die.CollisionOffset;
		RegisterDie(die);
		SelectOnly(die);
	}

	/// <summary>
	/// Select one die — and its partner with it, if it has one.
	///
	/// A linked pair reads as a single d100, so it picks up, moves and is thrown as one
	/// thing. Two dice selected is already the group drag, which steers both to the
	/// cursor and keeps them apart, so this needs no separate handling downstream.
	/// </summary>
	private void SelectOnly(Dice die)
	{
		selectedDice.Clear();
		selectedDice.Add(die);
		if (die.Partner != null && IsInstanceValid(die.Partner) && dice.Contains(die.Partner))
			selectedDice.Add(die.Partner);
		activeDie = die;
	}

	private void SelectAll()
	{
		selectedDice.Clear();
		foreach (Dice die in dice)
			selectedDice.Add(die);
	}

	/// <summary>
	/// Gather every die back to the middle.
	///
	/// The block is sized to the board rather than being four dice wide however many
	/// there are. That was the old rule, and with a lot of dice it built a column that
	/// ran off the bottom of the board — 40 dice made ten rows, 540px of them, from a
	/// spawn point 283px above the floor.
	///
	/// Roughly square, spaced no wider than fits, and centred on the spawn point unless
	/// that would hang the block over an edge. `ResetDie` clamps each die individually
	/// afterwards, so the arithmetic here only has to be close.
	/// </summary>
	public void OnSpawnButton()
	{
		CancelDrag();
		if (dice.Count == 0)
			return;
		Sfx.Play("respawn");

		// Inset by a die, so a die *centred* on the edge of this box is still inside the
		// board rather than half through the wall.
		Rect2 inner = boardBounds.Grow(-40f);
		int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dice.Count)));
		int rows = Mathf.CeilToInt(dice.Count / (float)columns);

		// Shrink the spacing rather than the block: dice that end up touching is what a
		// gather looks like, dice off the board is not.
		float spacing = Mathf.Min(54f,
			Mathf.Min(inner.Size.X / Mathf.Max(1, columns - 1),
				inner.Size.Y / Mathf.Max(1, rows - 1)));

		var block = new Vector2((columns - 1) * spacing, (rows - 1) * spacing);
		Vector2 topLeft = spawnPosition - block / 2f;
		topLeft = new Vector2(
			Mathf.Clamp(topLeft.X, inner.Position.X,
				Mathf.Max(inner.Position.X, inner.End.X - block.X)),
			Mathf.Clamp(topLeft.Y, inner.Position.Y,
				Mathf.Max(inner.Position.Y, inner.End.Y - block.Y)));

		for (int i = 0; i < dice.Count; i++)
			ResetDie(dice[i], topLeft
				+ new Vector2(i % columns * spacing, i / columns * spacing));
	}

	/// <summary>
	/// Put a die back where it started, however it got to where it is.
	///
	/// `Dice.TeleportTo` applies the move from inside `_IntegrateForces`, which is the
	/// engine's own answer to moving a rigid body and clears the velocities with it. This
	/// used to freeze the body, reach past the node into `PhysicsServer2D.BodySetState`
	/// and unfreeze again — see KNOWNISSUES 5, where the dance is re-measured and no
	/// longer needed.
	/// </summary>
	private void ResetDie(Dice die, Vector2 position)
	{
		die.CancelDragging();
		// Whatever state it was left in: a respawn is meant to un-stick things.
		die.Freeze = false;
		// Clamped per die, because each shape sits at its own offset from its origin.
		die.TeleportTo(ClampInto(OriginBoundsFor(die), position));
	}

	/// Detach the mouse pin and undo the extra damping the single drag applies.
	private void EndSingleDrag()
	{
		MousePin.NodeB = new NodePath();
		if (draggedDie != null && IsInstanceValid(draggedDie))
			draggedDie.AngularDamp = 0;
	}

	private void CancelDrag()
	{
		if (!isDragging)
			return;
		EndSingleDrag();
		foreach (Dice die in selectedDice)
		{
			die.Freeze = false;
			die.CancelDragging();
		}
		ClearDragState();
	}

	private void ClearDragState()
	{
		isDragging = false;
		isGroupDragging = false;
		draggedDie = null;
	}

	/// What the menu may offer for this die: pair up, come apart, or neither.
	private DiceMenu.Linkage LinkageOf(Dice die)
	{
		if (die.Partner != null && IsInstanceValid(die.Partner))
			return DiceMenu.Linkage.Linked;
		foreach (Dice other in dice)
			if (Dice.CanPair(die, other) && other.Partner == null)
				return DiceMenu.Linkage.Available;
		return DiceMenu.Linkage.Impossible;
	}

	/// <summary>
	/// Start picking a partner. The dice that could take the other half light up, and
	/// the next click on one of them makes the pair.
	/// </summary>
	private void BeginLink(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return;

		CancelLink();
		pendingLink = die;
		foreach (Dice other in dice)
			if (Dice.CanPair(die, other) && other.Partner == null)
				other.SetHovered(true);
	}

	private void CancelLink()
	{
		if (pendingLink == null)
			return;
		foreach (Dice other in dice)
			if (IsInstanceValid(other))
				other.SetHovered(false);
		pendingLink = null;
	}

	/// Read two dice as one d100. Either may already be in a pair; the old one comes
	/// apart first, because a die cannot be half of two hundreds at once.
	private void Link(Dice a, Dice b)
	{
		if (!Dice.CanPair(a, b))
			return;
		Unlink(a);
		Unlink(b);
		a.Partner = b;
		b.Partner = a;
		Sfx.Play("link");
		diceHud.UpdateValue(a, a.Value);        // redraw the list as one row
	}

	private void Unlink(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return;
		Dice partner = die.Partner;
		die.Partner = null;
		if (partner != null)
			Sfx.Play("unlink");
		if (partner != null && IsInstanceValid(partner))
		{
			partner.Partner = null;
			diceHud.UpdateValue(partner, partner.Value);
		}
		diceHud.UpdateValue(die, die.Value);
	}

	/// <summary>
	/// Take a copy of a die and wait for a click to put it down.
	///
	/// Deliberately not the palette's press-drag-release: this starts from a menu the
	/// pointer has already been pressed and released on, so there is no drag left to
	/// carry. Click once more to place, Escape or right-click to drop the idea.
	/// </summary>
	private void BeginCopy(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return;

		string scene = SceneOf(die);
		if (scene == null)
		{
			GD.PushWarning($"{die.Name}: cannot tell which scene it came from, not copying");
			return;
		}

		CancelCopy();
		pendingCopyScene = scene;
		pendingCopyFace = die.GetResult();      // a copy shows what it was copied from
		pendingCopyTheme = die.Theme;           // and wears what it was copied from

		copyPreview = new TextureRect
		{
			Name = "CopyPreview",
			// The face it was copied from, at rest and cropped to the die — the same
			// picture the palette puts on its buttons, so a copy on the cursor looks
			// like the thing being copied.
			Texture = DicePalette.CropToDie(die.RestingFrame(pendingCopyFace)),
			Material = DiceTheme.MaterialFor(pendingCopyTheme),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Size = new Vector2(DicePalette.GhostSize, DicePalette.GhostSize),
			Modulate = new Color(1, 1, 1, 0.65f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 100
		};
		uiLayer.AddChild(copyPreview);
		UpdateProcessing();
	}

	private void CancelCopy()
	{
		pendingCopyScene = null;
		copyPreview?.QueueFree();
		copyPreview = null;
		UpdateProcessing();
	}

	/// <summary>
	/// Put the waiting copy down.
	///
	/// <param name="keepGhost">
	/// Shift was held for this click: leave the copy on the cursor so the next click
	/// stamps another, rather than going back to the menu for each one. Taken from the
	/// click itself rather than from the live keyboard, so it is the modifier state of
	/// the press that decides — and so a harness can drive it.
	/// </param>
	/// </summary>
	private bool PlaceCopy(Vector2 screenPoint, bool keepGhost)
	{
		if (pendingCopyScene == null)
			return false;

		string scene = pendingCopyScene;
		int face = pendingCopyFace;
		int theme = pendingCopyTheme;
		if (!keepGhost)
			CancelCopy();

		SpawnDie(scene, screenPoint, theme);
		// SpawnDie selects what it made, so this is the copy and not the original.
		activeDie?.PlaceOnFace(face);
		return true;
	}

	/// Which scene a die was made from. Godot records that on the instance root, so it
	/// holds for the die placed in game.tscn as well as for every one spawned since.
	/// Where a die came from, as a path. Never loads anything: a die that exists has
	/// already had its scene loaded, and asking for it again by name would defeat the
	/// point of loading on demand.
	private string SceneOf(Dice die) =>
		string.IsNullOrEmpty(die.SceneFilePath) ? null : die.SceneFilePath;

	public override void _Process(double delta)
	{
		if (copyPreview != null)
			copyPreview.Position =
				GetViewport().GetMousePosition() - copyPreview.Size / 2f;
		if (shakeLeft > 0f)
			StepShake(delta);
	}

	private void DeleteDie(Dice die)
	{
		if (!IsInstanceValid(die) || deletingDice.Contains(die))
			return;

		if (isDragging && (draggedDie == die || selectedDice.Contains(die)))
			CancelDrag();

		Unlink(die);        // never leave the other half of a pair pointing at a corpse
		if (pendingLink == die)
			CancelLink();

		Sfx.Play("delete", 0f, 0.05f);
		deletingDice.Add(die);
		dice.Remove(die);
		selectedDice.Remove(die);
		diceHud.RemoveDie(die);
		if (activeDie == die)
			activeDie = dice.Count > 0 ? dice[0] : null;
		die.SetHovered(false);
		// Shrinks away and frees itself. Everything above has already taken it out of the
		// lists, so what is left on the board is a picture finishing its exit.
		die.Vanish();
	}

	// ------------------------------------------------------------------ persistence

	/// <summary>
	/// Everything worth remembering: the settings, and every die on the board.
	///
	/// Dice are recorded by the path of the scene they came from rather than by an index
	/// into <c>DiceScenes</c>, so reordering the pack cannot turn somebody's d20 into a
	/// d4. A die whose scene is no longer configured is dropped on load rather than
	/// guessed at.
	/// </summary>
	private Godot.Collections.Dictionary CollectSave()
	{
		var themes = new Godot.Collections.Array();
		for (int i = 0; i < palette.SlotCount; i++)
			themes.Add(palette.SlotTheme(i));

		// The savable dice, gathered first, because a pairing is stored as an index into
		// this list and it must not be an index into one that skipped an entry.
		var live = new List<Dice>();
		foreach (Dice die in dice)
			if (IsInstanceValid(die) && !string.IsNullOrEmpty(die.SceneFilePath))
				live.Add(die);

		var saved = new Godot.Collections.Array();
		foreach (Dice die in live)
			saved.Add(new Godot.Collections.Dictionary
			{
				["scene"] = die.SceneFilePath,
				// Rounded, and not only for tidiness: the autosave decides whether to
				// write by comparing this against the last one, and unrounded positions
				// change in the sixth decimal for as long as the physics is awake.
				["x"] = Mathf.Round(die.GlobalPosition.X),
				["y"] = Mathf.Round(die.GlobalPosition.Y),
				["rot"] = Mathf.Snapped(die.Rotation, 0.001f),
				["face"] = die.GetResult(),
				["theme"] = die.Theme,
				["partner"] = die.Partner != null && IsInstanceValid(die.Partner)
					? live.IndexOf(die.Partner) : -1,
			});

		return new Godot.Collections.Dictionary
		{
			["settings"] = new Godot.Collections.Dictionary
			{
				["muted"] = Sfx.Muted,
				["group_drag"] = groupDrag,
				["palette_open"] = palette.IsOpen,
				["list_open"] = diceHud.IsOpen,
				["palette_themes"] = themes,
			},
			["dice"] = saved,
		};
	}

	/// Write, but only when something has actually changed. Called on a timer rather than
	/// from every place that could change something, so nothing can be forgotten.
	private void SaveIfChanged()
	{
		if (palette == null || diceHud == null)
			return;
		Godot.Collections.Dictionary data = CollectSave();
		string text = Json.Stringify(data);
		if (text == lastSaved)
			return;
		lastSaved = text;
		SaveGame.Store(data);
	}

	/// <summary>
	/// Put a saved board back.
	///
	/// Every field is optional and every one is checked. A save is a file on someone's
	/// machine: it can be half-written, hand-edited, or left over from a build that knew
	/// different dice, and none of those may throw.
	/// </summary>
	private void ApplySave(Godot.Collections.Dictionary data)
	{
		if (data == null)
			return;

		if (data.TryGetValue("settings", out Variant settingsValue)
			&& settingsValue.VariantType == Variant.Type.Dictionary)
		{
			var settings = settingsValue.AsGodotDictionary();
			if (settings.TryGetValue("muted", out Variant muted))
			{
				Sfx.SetMuted(muted.AsBool());
				// The button was built before this ran, so it is still drawn for whatever
				// the sound was before the save was read.
				muteButton?.Refresh();
			}
			if (settings.TryGetValue("group_drag", out Variant grouped))
			{
				groupDrag = grouped.AsBool();
				groupDragButton?.SetOn(groupDrag);
			}
			if (settings.TryGetValue("palette_themes", out Variant themes)
				&& themes.VariantType == Variant.Type.Array)
			{
				Godot.Collections.Array list = themes.AsGodotArray();
				for (int i = 0; i < list.Count && i < palette.SlotCount; i++)
					palette.SetSlotTheme(i, Mathf.Clamp((int)list[i], 0,
						DiceTheme.Count - 1));
			}
			// Silent: the drawers are being put back where they were, not opened by a
			// player, and a game that chimes twice on startup is a game with a bug.
			if (settings.TryGetValue("palette_open", out Variant paletteOpen))
				palette.SetDrawerOpen(paletteOpen.AsBool(), false);
			if (settings.TryGetValue("list_open", out Variant listOpen))
				diceHud.SetOpen(listOpen.AsBool(), false);
		}

		if (!data.TryGetValue("dice", out Variant diceValue)
			|| diceValue.VariantType != Variant.Type.Array)
			return;
		Godot.Collections.Array entries = diceValue.AsGodotArray();

		ClearBoardSilently();

		// Nulls are kept in place so the partner indices still line up.
		var restored = new List<Dice>();
		foreach (Variant item in entries)
		{
			restored.Add(item.VariantType == Variant.Type.Dictionary
				? RestoreDie(item.AsGodotDictionary()) : null);
		}

		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i].VariantType != Variant.Type.Dictionary || restored[i] == null)
				continue;
			var entry = entries[i].AsGodotDictionary();
			if (!entry.TryGetValue("partner", out Variant partnerValue))
				continue;
			int other = (int)partnerValue;
			if (other < 0 || other >= restored.Count || restored[other] == null)
				continue;
			// Both sides together, and only if the pair is still a legal one — the save
			// may predate a change to what can be linked.
			if (Dice.CanPair(restored[i], restored[other]))
			{
				restored[i].Partner = restored[other];
				restored[other].Partner = restored[i];
			}
		}
		foreach (Dice die in dice)
			diceHud.UpdateValue(die, die.Value);

		if (dice.Count > 0)
			SelectOnly(dice[0]);
	}

	/// One die from its saved entry, put down rather than played in: no arrival animation
	/// and no spawn sound, because eight dice popping and chiming at startup is a mess.
	private Dice RestoreDie(Godot.Collections.Dictionary entry)
	{
		PackedScene scene = SceneAtPath(
			entry.TryGetValue("scene", out Variant path) ? path.AsString() : "");
		if (scene == null)
			return null;

		Dice die = scene.Instantiate<Dice>();
		AddChild(die);
		die.Theme = entry.TryGetValue("theme", out Variant theme)
			? Mathf.Clamp((int)theme, 0, DiceTheme.Count - 1) : DiceTheme.Bone;
		die.GlobalPosition = ClampInto(OriginBoundsFor(die), new Vector2(
			entry.TryGetValue("x", out Variant x) ? (float)x : spawnPosition.X,
			entry.TryGetValue("y", out Variant y) ? (float)y : spawnPosition.Y));
		die.Rotation = entry.TryGetValue("rot", out Variant rot) ? (float)rot : 0f;
		RegisterDie(die);

		int face = entry.TryGetValue("face", out Variant f) ? (int)f : 1;
		die.PlaceOnFace(Mathf.Clamp(face, 1, die.FaceCount));
		return die;
	}

	/// The configured scene with this path, or null. Restricted to the configured pack on
	/// purpose: a save should not be able to name an arbitrary resource to load.
	/// <summary>
	/// The scene at this path, loaded now if it has not been already, or null if the
	/// path is not one of the pack's.
	///
	/// The check is what stops a hand-edited save naming an arbitrary resource, and it
	/// is also what makes loading here safe: only eight paths can ever reach `GD.Load`.
	/// </summary>
	private PackedScene SceneAtPath(string path)
	{
		if (string.IsNullOrEmpty(path))
			return null;
		if (loadedScenes.TryGetValue(path, out PackedScene cached))
			return cached;
		if (!Pack.Exists(entry => entry.Scene == path))
			return null;
		var loaded = GD.Load<PackedScene>(path);
		if (loaded != null)
			loadedScenes[path] = loaded;
		return loaded;
	}

	/// <summary>
	/// Read the pack manifest: which dice there are, what they are called, and where
	/// their icons sit in the sheet. Generated by tools/dice-render/make_icons.py.
	/// </summary>
	private void LoadPack()
	{
		Pack.Clear();
		packOffsets.Clear();
		packScales.Clear();
		using FileAccess file = FileAccess.Open(PackPath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushError($"no die pack at {PackPath}; the palette will be empty");
			return;
		}
		Variant parsed = Json.ParseString(file.GetAsText());
		if (parsed.VariantType != Variant.Type.Array)
		{
			GD.PushError($"{PackPath} is not a JSON array");
			return;
		}
		foreach (Variant item in parsed.AsGodotArray())
		{
			if (item.VariantType != Variant.Type.Dictionary)
				continue;
			var entry = item.AsGodotDictionary();
			if (!entry.TryGetValue("scene", out Variant scene)
				|| !entry.TryGetValue("icon", out Variant icon)
				|| icon.VariantType != Variant.Type.Array)
				continue;
			Godot.Collections.Array box = icon.AsGodotArray();
			if (box.Count < 4)
				continue;
			Pack.Add((scene.AsString(),
				entry.TryGetValue("name", out Variant name) ? name.AsString() : "die",
				new Rect2((float)box[0], (float)box[1], (float)box[2], (float)box[3])));

			Vector2 offset = Vector2.Zero;
			if (entry.TryGetValue("offset", out Variant off)
				&& off.VariantType == Variant.Type.Array)
			{
				Godot.Collections.Array pair = off.AsGodotArray();
				if (pair.Count >= 2)
					offset = new Vector2((float)pair[0], (float)pair[1]);
			}
			packOffsets.Add(offset);
			packScales.Add(entry.TryGetValue("scale", out Variant sc) ? (float)sc : 1f);
		}
	}

	/// Empty the board without the sounds or the animations — this is a board being
	/// replaced before anyone has seen it, not dice being deleted.
	private void ClearBoardSilently()
	{
		CancelDrag();
		foreach (Dice die in new List<Dice>(dice))
		{
			if (die.Partner != null && IsInstanceValid(die.Partner))
				die.Partner.Partner = null;
			die.Partner = null;
			dice.Remove(die);
			selectedDice.Remove(die);
			diceHud.RemoveDie(die);
			die.SetHovered(false);
			die.QueueFree();
		}
		activeDie = null;
	}

	/// <summary>
	/// Roll one die where it stands — the menu's first item, and the R key.
	///
	/// The only two ways a roll is asked for rather than caused. A collision re-roll and a
	/// throw both start one too, and neither goes through here: they already make a noise
	/// of their own, and a rattle on top of a clack is just a louder clack.
	/// </summary>
	private void RollOne(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return;
		die.Roll(restart: true);
		Sfx.Play("die_roll", 0f, 0.06f);
	}

	/// <summary>
	/// A colour scheme was picked. What it lands on depends on where the menu was opened:
	/// a slot in the palette repaints that slot and everything made from it afterwards,
	/// a die on the board repaints only itself.
	///
	/// The menu does not decide this — it reports the choice and the board applies it,
	/// which is the same split every other item in that panel already uses.
	/// </summary>
	private void OnThemeChosen(int theme)
	{
		if (diceMenu.PaletteSlot >= 0)
		{
			palette.SetSlotTheme(diceMenu.PaletteSlot, theme);
			return;
		}

		Dice die = diceMenu.Target;
		if (die == null || !IsInstanceValid(die))
			return;
		die.Theme = theme;
		Sfx.Play("theme");
		// Both halves of a d100 together: they are read as one die, so a pair wearing two
		// colours would be a stranger thing than the pair not matching the board.
		if (die.Partner != null && IsInstanceValid(die.Partner))
			die.Partner.Theme = theme;
	}

	/// Scatter every die across the board and roll it — the space bar, and the board
	/// menu's first item.
	private void ThrowAllDice()
	{
		CancelDrag();
		foreach (Dice die in dice)
			die.Throw();
		if (dice.Count > 0)
		{
			Sfx.ThrowAll();
			StartShake();
		}
	}

	/// <summary>
	/// Knock the board, as though the table had been thumped.
	///
	/// The *view* moves, not the board: the dice are children of this node, so shifting
	/// it would teleport eight rigid bodies every frame — which reads as a hard contact,
	/// starts collision re-rolls and can push a die through a wall. Setting the
	/// viewport's canvas transform moves what is drawn and leaves the physics world
	/// exactly where it was. The HUD, palette and menus sit on a CanvasLayer, which has
	/// its own transform and does not follow, so only the board shudders.
	/// </summary>
	private void StartShake()
	{
		shakeLeft = ShakeDuration;
		float angle = Random.Shared.NextSingle() * Mathf.Tau;
		shakeAxis = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
		UpdateProcessing();
	}

	/// One decaying oscillation along a fixed direction, rather than a fresh random
	/// offset each frame: a thump that rings down, where per-frame noise reads as the
	/// picture being broken.
	private void StepShake(double delta)
	{
		shakeLeft -= (float)delta;
		if (shakeLeft <= 0f)
		{
			shakeLeft = 0f;
			GetViewport().CanvasTransform = Transform2D.Identity;
			UpdateProcessing();
			return;
		}

		float remaining = shakeLeft / ShakeDuration;        // 1 at the thump, 0 at rest
		float amplitude = ShakeStrength * remaining * remaining;
		float phase = (ShakeDuration - shakeLeft) * ShakeFrequency;
		GetViewport().CanvasTransform =
			new Transform2D(0f, shakeAxis * amplitude * Mathf.Sin(phase));
	}

	/// _Process runs only when something needs it: a copy riding the cursor, or a shake
	/// running down.
	private void UpdateProcessing() => SetProcess(copyPreview != null || shakeLeft > 0f);

	private void DeleteAllDice()
	{
		CancelDrag();
		foreach (Dice die in new List<Dice>(dice))
			DeleteDie(die);
	}

	private void OnDiceRolled(Dice die, int result)
	{
		// `result` is the face; what the HUD wants is what that face is worth, which
		// differs only on the percentile d10.
		diceHud.UpdateValue(die, die.Value);
		GD.Print("Rolled: " + result);
	}

	private void OnBodyExited(Node2D body)
	{
		if (body is Dice die && IsInstanceValid(die) && !deletingDice.Contains(die))
			CallDeferred(nameof(ResetExitedDie), die);
	}

	private void ResetExitedDie(Dice die)
	{
		if (IsInstanceValid(die))
			ResetDie(die, spawnPosition);
	}
}
