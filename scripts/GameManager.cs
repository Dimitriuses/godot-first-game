using Godot;
using System;
using System.Collections.Generic;

public partial class GameManager : Node2D
{
	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;
	[Export] public PackedScene DiceScene;

	private readonly List<Dice> dice = new();
	private readonly List<Dice> selectedDice = new();
	private readonly Dictionary<Dice, Vector2> groupDragOffsets = new();
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

	public override void _Ready()
	{
		MousePin.NodeA = MousePin.GetPathTo(FakeBody);
		DiceArea.BodyExited += OnBodyExited;

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
		MousePin.GlobalPosition = mouse;

		if (isGroupDragging)
		{
			dragVelocity = delta > 0 ? (mouse - lastMousePosition) / (float)delta : Vector2.Zero;
			lastMousePosition = mouse;
			foreach (Dice die in selectedDice)
				die.GlobalPosition = mouse + groupDragOffsets[die];
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey shiftKey && shiftKey.Keycode == Key.Shift
			&& shiftKey.Pressed && !shiftKey.Echo)
		{
			SelectAll();
			return;
		}

		if (@event is InputEventKey key && key.Keycode == Key.Space && key.Pressed && !key.Echo)
		{
			CancelDrag();
			foreach (Dice die in dice)
				die.Roll();
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
			MousePin.NodeB = MousePin.GetPathTo(clickedDie);
			clickedDie.isDragging = true;
			clickedDie.AngularDamp = 10;
			return;
		}

		isGroupDragging = true;
		groupDragOffsets.Clear();
		foreach (Dice die in selectedDice)
		{
			groupDragOffsets[die] = die.GlobalPosition - mouse;
			die.isDragging = true;
			die.Freeze = true;
		}
	}

	private void ReleaseDraggedDice()
	{
		if (isGroupDragging)
		{
			foreach (Dice die in selectedDice)
			{
				die.Freeze = false;
				die.LinearVelocity = dragVelocity;
				die.AngularVelocity = (float)Random.Shared.NextDouble() * 8f - 4f;
				die.isDragging = false;
				die.Roll();
			}
		}
		else if (draggedDie != null)
		{
			MousePin.NodeB = new NodePath();
			draggedDie.isDragging = false;
			draggedDie.AngularDamp = 0;
			draggedDie.Roll();
		}

		ClearDragState();
	}

	private void SpawnDie(Vector2 screenPosition)
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
		die.isDragging = false;
	}

	private void CancelDrag()
	{
		if (!isDragging)
			return;
		MousePin.NodeB = new NodePath();
		foreach (Dice die in selectedDice)
		{
			die.Freeze = false;
			die.isDragging = false;
		}
		ClearDragState();
	}

	private void ClearDragState()
	{
		isDragging = false;
		isGroupDragging = false;
		draggedDie = null;
		groupDragOffsets.Clear();
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
