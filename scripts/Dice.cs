using Godot;
using System;

public partial class Dice : RigidBody2D
{
	[Export] public AnimatedSprite2D AnimatedSprite;
	[Export] public CollisionShape2D CollisionShape;

	// [Export] public RigidBody2D Indicator;
	

	private Random random = new Random();
	private int currentResult = 1;
	
	private Vector2 dragStart;
	private Vector2 dragEnd;
	public bool isDragging = false;
	private bool isRolling = false;

	public override void _Ready()
	{
		AnimatedSprite.AnimationFinished += OnAnimationFinished;
		//Freeze = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		// GD.Print(Indicator.AngularVelocity);
		AnimatedSprite.Rotation = -Rotation;
		//GD.Print(isDragging + " " + isRolling + " " + AnimatedSprite.IsPlaying());
		if (AngularVelocity > 5)
		{
			//GD.Print(AngularVelocity);
			AnimatedSprite.Play("ide0");
		}
		else if (!isDragging && AnimatedSprite.IsPlaying() && (AnimatedSprite.Animation == "ide0" || AnimatedSprite.Animation == "ide1"))
		{
			Roll();
			GD.Print(AnimatedSprite.Animation + " " + AnimatedSprite.IsPlaying());
		}
		if (isDragging && AngularVelocity > 10)
		{
			AnimatedSprite.Play("ide1");
		}
	}


	public void Roll()
	{
		//Rotation = 0;
		currentResult = random.Next(1, 7);
		isRolling = true;
		AnimatedSprite.Play(currentResult.ToString());
		//CollisionShape.Disabled = true;
	}

	private void OnAnimationFinished()
	{
		if (isRolling)
		{
			GD.Print("Анімація завершена. Результат: ", currentResult);
			//Freeze = true;
			isRolling = false;
			EmitSignal(SignalName.DiceRolled, currentResult);
			//CollisionShape.Disabled = false;
		}
	}

	[Signal]
	public delegate void DiceRolledEventHandler(int result);
	public int GetResult() => currentResult;
}
