using Godot;
using System.Collections.Generic;

public partial class GameManager : Node2D
{
	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;
	/// One entry per die type the palette offers. An array rather than a single scene
	/// so a d20 is an extra element, not a code change.
	[Export] public Godot.Collections.Array<PackedScene> DiceScenes = new();

	/// Fastest a dragged die is steered or released at, px/s. Without a bound, a flick
	/// hands the solver an impulse it has to fight, which is how dice used to come out
	/// through the walls.
	[Export] public float MaxDragSpeed = 4000f;

	/// Radius of the clump a Shift-drag gathers dice into, per die. The clump grows as
	/// the square root of the count, because that is how discs pack into a disc.
	[Export] public float GatherSpread = 38f;

	private readonly List<Dice> dice = new();
	private readonly List<Dice> selectedDice = new();
	private readonly HashSet<Dice> deletingDice = new();
	private Dice draggedDie;
	private Dice activeDie;
	private Dice hoveredDie;
	private DiceHud diceHud;
	private DiceMenu diceMenu;
	private CanvasLayer uiLayer;

	/// A copy waiting to be put down: the die type taken, the face it was showing, and
	/// the ghost that follows the cursor until a click places it.
	private PackedScene pendingCopyScene;
	private int pendingCopyFace;
	private TextureRect copyPreview;

	/// The die waiting to be paired, while its possible partners stand highlighted.
	private Dice pendingLink;
	private bool swallowNextDieClick;
	private int nextDieId = 1;
	private bool isDragging;
	private bool isGroupDragging;
	private Vector2 lastMousePosition;
	private Vector2 dragVelocity;
	private Vector2 spawnPosition;
	private Vector2 singleGrabOffset;
	private Rect2 boardBounds;

	public override void _Ready()
	{
		MousePin.NodeA = MousePin.GetPathTo(FakeBody);
		DiceArea.BodyExited += OnBodyExited;
		boardBounds = ComputeBoardBounds();

		uiLayer = new CanvasLayer { Name = "GameUiLayer" };
		AddChild(uiLayer);
		var palette = new DicePalette { Name = "DicePalette", DiceScenes = DiceScenes };
		uiLayer.AddChild(palette);
		palette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		palette.SpawnRequested += SpawnDie;

		diceHud = new DiceHud { Name = "DiceHud" };
		uiLayer.AddChild(diceHud);
		diceHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		diceHud.DeleteRequested += DeleteDie;
		diceHud.DeleteAllRequested += DeleteAllDice;

		diceMenu = new DiceMenu { Name = "DiceMenu" };
		uiLayer.AddChild(diceMenu);
		diceMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		diceMenu.RollRequested += die => die?.Roll(restart: true);
		diceMenu.CopyRequested += BeginCopy;
		diceMenu.DeleteRequested += DeleteDie;
		diceMenu.LinkRequested += BeginLink;
		diceMenu.UnlinkRequested += Unlink;
		diceMenu.ThrowAllRequested += ThrowAllDice;
		diceMenu.RespawnRequested += OnSpawnButton;
		diceMenu.DeleteAllRequested += DeleteAllDice;

		foreach (Node child in GetChildren())
			if (child is Dice die)
				RegisterDie(die);

		if (dice.Count > 0)
		{
			spawnPosition = dice[0].Position;
			SelectOnly(dice[0]);
		}

		SetProcess(false);          // only while a copy is riding the cursor
	}

	public override void _PhysicsProcess(double delta)
	{
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
					subject.Roll(restart: true);
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
			// A right-click while something is pending means "not that", and nothing more.
			bool wasPending = pendingCopyScene != null || pendingLink != null;
			CancelCopy();
			CancelLink();
			diceMenu.Close();
			if (wasPending)
				return;

			// Both menus open from here rather than from the die's own click handler,
			// which reports after this has already run and decided.
			Dice under = DieAt(GetViewport().GetCanvasTransform().AffineInverse() * point);
			if (under != null)
				diceMenu.Open(under, point, LinkageOf(under));
			else
				diceMenu.OpenBoard(point, dice.Count);
			swallowNextDieClick = true;
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
		diceHud.AddDie(die, nextDieId++, die.Value);
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

		if (Input.IsKeyPressed(Key.Shift))
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

	private void ReleaseDraggedDice()
	{
		if (isGroupDragging)
		{
			foreach (Dice die in selectedDice)
			{
				// It is already moving at the steer velocity; do not overwrite that with
				// the raw cursor speed, which ignores whatever the die was pressed against.
				die.Freeze = false;
				die.LinearVelocity = die.LinearVelocity.LimitLength(MaxDragSpeed);
				die.ReleaseFromDrag(die.LinearVelocity.Length());
			}
		}
		else if (draggedDie != null)
		{
			draggedDie.LinearVelocity = draggedDie.LinearVelocity.LimitLength(MaxDragSpeed);
			float releaseSpeed = draggedDie.LinearVelocity.Length();
			EndSingleDrag();
			draggedDie.ReleaseFromDrag(releaseSpeed);
		}

		ClearDragState();
	}

	public void SpawnDie(PackedScene scene, Vector2 screenPosition)
	{
		if (scene == null)
			return;

		Dice die = scene.Instantiate<Dice>();
		AddChild(die);
		Vector2 viewportSize = GetViewportRect().Size;
		die.GlobalPosition = new Vector2(
			Mathf.Clamp(screenPosition.X, 80f, viewportSize.X - 240f),
			Mathf.Clamp(screenPosition.Y, 90f, viewportSize.Y - 80f));
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

	public void OnSpawnButton()
	{
		CancelDrag();
		for (int i = 0; i < dice.Count; i++)
			ResetDie(dice[i], spawnPosition + new Vector2((i % 4) * 54f, (i / 4) * 54f));
	}

	private void ResetDie(Dice die, Vector2 position)
	{
		die.Freeze = true;
		die.LinearVelocity = Vector2.Zero;
		die.AngularVelocity = 0;
		PhysicsServer2D.BodySetState(die.GetRid(), PhysicsServer2D.BodyState.Transform,
			new Transform2D(0, position));
		die.Freeze = false;
		die.CancelDragging();
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
		diceHud.UpdateValue(a, a.Value);        // redraw the list as one row
	}

	private void Unlink(Dice die)
	{
		if (die == null || !IsInstanceValid(die))
			return;
		Dice partner = die.Partner;
		die.Partner = null;
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

		PackedScene scene = SceneOf(die);
		if (scene == null)
		{
			GD.PushWarning($"{die.Name}: cannot tell which scene it came from, not copying");
			return;
		}

		CancelCopy();
		pendingCopyScene = scene;
		pendingCopyFace = die.GetResult();      // a copy shows what it was copied from

		copyPreview = new TextureRect
		{
			Name = "CopyPreview",
			// The face it was copied from, at rest and cropped to the die — the same
			// picture the palette puts on its buttons, so a copy on the cursor looks
			// like the thing being copied.
			Texture = DicePalette.CropToDie(die.RestingFrame(pendingCopyFace)),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Size = new Vector2(96, 96),
			Modulate = new Color(1, 1, 1, 0.65f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 100
		};
		uiLayer.AddChild(copyPreview);
		SetProcess(true);
	}

	private void CancelCopy()
	{
		pendingCopyScene = null;
		copyPreview?.QueueFree();
		copyPreview = null;
		SetProcess(false);
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

		PackedScene scene = pendingCopyScene;
		int face = pendingCopyFace;
		if (!keepGhost)
			CancelCopy();

		SpawnDie(scene, screenPoint);
		// SpawnDie selects what it made, so this is the copy and not the original.
		activeDie?.PlaceOnFace(face);
		return true;
	}

	/// Which scene a die was made from. Godot records that on the instance root, so it
	/// holds for the die placed in game.tscn as well as for every one spawned since.
	private PackedScene SceneOf(Dice die)
	{
		if (!string.IsNullOrEmpty(die.SceneFilePath))
			return GD.Load<PackedScene>(die.SceneFilePath);

		// Built in code rather than instanced: fall back to whichever configured type
		// carries the same name.
		foreach (PackedScene candidate in DiceScenes)
		{
			if (candidate == null)
				continue;
			var probe = candidate.Instantiate<Dice>();
			bool match = probe.DisplayName == die.DisplayName;
			probe.Free();
			if (match)
				return candidate;
		}
		return null;
	}

	public override void _Process(double delta)
	{
		if (copyPreview != null)
			copyPreview.Position =
				GetViewport().GetMousePosition() - copyPreview.Size / 2f;
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

		deletingDice.Add(die);
		dice.Remove(die);
		selectedDice.Remove(die);
		diceHud.RemoveDie(die);
		if (activeDie == die)
			activeDie = dice.Count > 0 ? dice[0] : null;
		die.SetHovered(false);
		die.QueueFree();
	}

	/// Scatter every die across the board and roll it — the space bar, and the board
	/// menu's first item.
	private void ThrowAllDice()
	{
		CancelDrag();
		foreach (Dice die in dice)
			die.Throw();
	}

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
