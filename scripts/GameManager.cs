using Godot;
using System;

public partial class GameManager : Node2D
{
	//[Export] public PackedScene PlayerScene;
	//private Player player;

	//[Export] public PackedScene DiceScene;
	private Dice dice;

	[Export] public PinJoint2D MousePin;
	[Export] public StaticBody2D FakeBody;
	[Export] public Area2D DiceArea;
	private bool isDragging = false;
	private Vector2 spawnPosition;

	private Vector2[] cellPositions = new Vector2[]
	{
		new Vector2(100, 100), new Vector2(200, 100), new Vector2(300, 100),
		new Vector2(300, 200), new Vector2(300, 300), new Vector2(200, 300),
		new Vector2(100, 300), new Vector2(100, 200),
	};

	public override void _Ready()
	{
		//player = PlayerScene.Instantiate<Player>();
		//AddChild(player);
		//player.MoveToCell(cellPositions[0]);

		dice = GetNode<Dice>("Dice");
		dice.InputPickable = true;
		dice.DiceRolled += OnDiceRolled;
		spawnPosition = dice.Position;

		MousePin.NodeA = MousePin.GetPathTo(FakeBody);
		dice.InputEvent += OnInputEvent;
		DiceArea.BodyExited += OnBodyExited;
	}

	public override void _PhysicsProcess(double delta)
	{
		MousePin.GlobalPosition = GetGlobalMousePosition();
		if (isDragging)
		{
			//GD.Print(dice.Rotation);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (isDragging && @event is InputEventMouseButton mouseEvent && !@event.IsPressed())
		{
			MousePin.NodeB = new NodePath();
			dice.isDragging = false;
			isDragging = false;
			//dice.Rotation = 0;
			//dice.Rotate(0);
			dice.AngularDamp = 0;
			// dice.LockRotation = true;
			//GD.Print("unhold");
			dice.Roll();

		}
	}

	private void OnInputEvent(Node viewport, InputEvent @event, long shapeIdx)
	{
		if (!isDragging && @event is InputEventMouseButton mouseEvent && @event.IsPressed())
		{
			MousePin.NodeB = MousePin.GetPathTo(dice);
			dice.isDragging = true;
			isDragging = true;
			dice.AngularDamp = 10;
			// dice.LockRotation = false;
			//GD.Print("hold");
		}
	}
	public void OnSpawnButton()
	{
		
		dice.Freeze = true;


		dice.LinearVelocity = Vector2.Zero;
		dice.AngularVelocity = 0;

		
		PhysicsServer2D.BodySetState(dice.GetRid(), PhysicsServer2D.BodyState.Transform, new Transform2D(0, spawnPosition));

		
		dice.Freeze = false;

		
		isDragging = false;
		MousePin.NodeB = new NodePath();
		dice.isDragging = false;
		GD.Print("Spawned on " + spawnPosition);
	}

	private void OnDiceRolled(int result)
	{
		GD.Print("Випало число: " + result);
		// Рух гравця тощо
	}
	
	private void OnBodyExited(Node body)
    {
		// GD.Print("щось вилетіло за межі!");
        if (body is Dice dice)
		{
			GD.Print("Кубик вилетів за межі!");
			// Повернути в центр:
			CallDeferred(nameof(OnSpawnButton));
			// OnSpawnButton();
		}
    }
	

}
