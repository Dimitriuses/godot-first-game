using Godot;

public partial class GameManager : Node2D
{
	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;

	private Dice dice;
	private bool isDragging = false;
	private Vector2 spawnPosition;

	public override void _Ready()
	{
		dice = GetNode<Dice>("Dice");
		dice.InputPickable = true;
		dice.DiceRolled += OnDiceRolled;
		spawnPosition = dice.Position;

		// The pin joint anchors to an invisible static body that tracks the cursor;
		// grabbing the dice attaches the joint's other end to it.
		MousePin.NodeA = MousePin.GetPathTo(FakeBody);
		dice.InputEvent += OnInputEvent;
		DiceArea.BodyExited += OnBodyExited;
	}

	public override void _PhysicsProcess(double delta)
	{
		MousePin.GlobalPosition = GetGlobalMousePosition();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (isDragging && @event is InputEventMouseButton && !@event.IsPressed())
		{
			MousePin.NodeB = new NodePath();
			dice.isDragging = false;
			isDragging = false;
			dice.AngularDamp = 0;
			dice.Roll();
		}
	}

	private void OnInputEvent(Node viewport, InputEvent @event, long shapeIdx)
	{
		if (!isDragging && @event is InputEventMouseButton && @event.IsPressed())
		{
			MousePin.NodeB = MousePin.GetPathTo(dice);
			dice.isDragging = true;
			isDragging = true;
			dice.AngularDamp = 10;
		}
	}

	public void OnSpawnButton()
	{
		dice.Freeze = true;

		dice.LinearVelocity = Vector2.Zero;
		dice.AngularVelocity = 0;

		PhysicsServer2D.BodySetState(dice.GetRid(), PhysicsServer2D.BodyState.Transform,
			new Transform2D(0, spawnPosition));

		dice.Freeze = false;

		isDragging = false;
		MousePin.NodeB = new NodePath();
		dice.isDragging = false;
		GD.Print("Spawned on " + spawnPosition);
	}

	private void OnDiceRolled(int result)
	{
		GD.Print("Випало число: " + result);
	}

	private void OnBodyExited(Node body)
	{
		if (body is Dice)
		{
			GD.Print("Кубик вилетів за межі!");
			CallDeferred(nameof(OnSpawnButton));
		}
	}
}
