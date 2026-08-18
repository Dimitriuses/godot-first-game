using Godot;
using System.Collections.Generic;

public partial class GameManager : Node2D
{
	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;
	[Export] public PackedScene DiceScene;

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
	private DiceHud diceHud;
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

		var uiLayer = new CanvasLayer { Name = "GameUiLayer" };
		AddChild(uiLayer);
		var palette = new DicePalette { Name = "DicePalette", DiceScene = DiceScene };
		uiLayer.AddChild(palette);
		palette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		palette.SpawnRequested += SpawnDie;

		diceHud = new DiceHud { Name = "DiceHud" };
		uiLayer.AddChild(diceHud);
		diceHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		diceHud.DeleteRequested += DeleteDie;
		diceHud.DeleteAllRequested += DeleteAllDice;

		foreach (Node child in GetChildren())
			if (child is Dice die)
				RegisterDie(die);

		if (dice.Count > 0)
		{
			spawnPosition = dice[0].Position;
			SelectOnly(dice[0]);
		}
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

		if (@event is InputEventKey key && key.Keycode == Key.Space && key.Pressed && !key.Echo)
		{
			CancelDrag();
			foreach (Dice die in dice)
				die.Throw();        // scatter them as well as animating
			GetViewport().SetInputAsHandled();
			return;
		}

		if (isDragging && @event is InputEventMouseButton mouseButton
			&& mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
		{
			ReleaseDraggedDice();
			GetViewport().SetInputAsHandled();
		}
	}

	private void RegisterDie(Dice die)
	{
		if (dice.Contains(die))
			return;

		dice.Add(die);
		diceHud.AddDie(die, nextDieId++, die.GetResult());
		die.InputPickable = true;
		die.DiceRolled += result => OnDiceRolled(die, result);
		die.InputEvent += (Node viewport, InputEvent @event, long shapeIdx) =>
			OnDiceInput(die, @event);
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
			|| mouseButton.ButtonIndex != MouseButton.Left || !mouseButton.Pressed)
			return;

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

	public void SpawnDie(Vector2 screenPosition)
	{
		if (DiceScene == null)
			return;

		Dice die = DiceScene.Instantiate<Dice>();
		AddChild(die);
		Vector2 viewportSize = GetViewportRect().Size;
		die.GlobalPosition = new Vector2(
			Mathf.Clamp(screenPosition.X, 80f, viewportSize.X - 240f),
			Mathf.Clamp(screenPosition.Y, 90f, viewportSize.Y - 80f));
		RegisterDie(die);
		SelectOnly(die);
	}

	private void SelectOnly(Dice die)
	{
		selectedDice.Clear();
		selectedDice.Add(die);
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

	private void DeleteDie(Dice die)
	{
		if (!IsInstanceValid(die) || deletingDice.Contains(die))
			return;

		if (isDragging && (draggedDie == die || selectedDice.Contains(die)))
			CancelDrag();

		deletingDice.Add(die);
		dice.Remove(die);
		selectedDice.Remove(die);
		diceHud.RemoveDie(die);
		if (activeDie == die)
			activeDie = dice.Count > 0 ? dice[0] : null;
		die.SetHovered(false);
		die.QueueFree();
	}

	private void DeleteAllDice()
	{
		CancelDrag();
		foreach (Dice die in new List<Dice>(dice))
			DeleteDie(die);
	}

	private void OnDiceRolled(Dice die, int result)
	{
		diceHud.UpdateValue(die, result);
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
